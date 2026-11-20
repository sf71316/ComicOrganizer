using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using SkiaSharp;

namespace ComicOrganizer.VisualSearch;

public sealed record VisualIndexProgress(
    string Phase,
    int DoneComics,
    int TotalComics,
    int DonePages,
    int AddedVectors,
    int MovedComics,
    int DeletedComics,
    int SkippedComics,
    string CurrentTitle);

public sealed record VisualSyncSummary(
    int ScannedComics,
    int IndexedComics,
    int SkippedUnchanged,
    int MovedComics,
    int DeletedComics,
    int TotalPages,
    int TotalVectorsAdded,
    bool WasCancelled,
    double ElapsedSeconds);

/// <summary>
/// 視覺索引建庫與增量同步管線：
/// 支援新作者／新漫畫追加、XxHash3 指紋搬移偵測（零重算更新路徑）、已刪除清理、
/// 分片寫入、6 核心並行解碼與優雅中斷續建。
/// </summary>
public sealed class VisualIndexingPipeline
{
    private readonly VisualSearchPaths _paths;
    private readonly Func<IPanelDetector?> _panelDetectorFactory;
    private readonly Func<IVisualEmbedder> _embedderFactory;
    private readonly int _batchSize;
    private readonly int _maxVectorsPerShard;
    private readonly int _checkpointComicInterval;

    public VisualIndexingPipeline(
        VisualSearchPaths paths,
        bool useDirectMl = true,
        int batchSize = 32,
        int maxVectorsPerShard = ShardedVectorStore.DefaultMaxVectorsPerShard,
        int checkpointComicInterval = 20)
        : this(
            paths,
            () => new YoloPanelDetector(paths.PanelModelPath, useDirectMl),
            () => new SscdFeatureEmbedder(paths.EmbedModelPath, useDirectMl),
            batchSize,
            maxVectorsPerShard,
            checkpointComicInterval)
    {
    }

    /// <summary>
    /// 供單元測試注入輕量 Mock Detector / Embedder 的建構子。
    /// </summary>
    public VisualIndexingPipeline(
        VisualSearchPaths paths,
        Func<IPanelDetector?> panelDetectorFactory,
        Func<IVisualEmbedder> embedderFactory,
        int batchSize = 32,
        int maxVectorsPerShard = ShardedVectorStore.DefaultMaxVectorsPerShard,
        int checkpointComicInterval = 20)
    {
        _paths = paths;
        _panelDetectorFactory = panelDetectorFactory;
        _embedderFactory = embedderFactory;
        _batchSize = Math.Max(1, batchSize);
        _maxVectorsPerShard = Math.Max(1, maxVectorsPerShard);
        _checkpointComicInterval = Math.Max(1, checkpointComicInterval);
    }

    private sealed record PreparedPage(
        long MangaId,
        int PageNo,
        string EntryName,
        SKImage Image,
        PreparedYoloFrame? YoloInput) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    private sealed record PendingTensor(
        long MangaId,
        int PageNo,
        string EntryName,
        bool IsFullPage,
        float X,
        float Y,
        float W,
        float H,
        float[] ChwData);

    private sealed record EmbeddedItem(
        long MangaId,
        int PageNo,
        string EntryName,
        bool IsFullPage,
        float X,
        float Y,
        float W,
        float H,
        float[] Vector);

    /// <summary>
    /// 增量同步整個 RootDirectory（或僅針對 specificPaths 進行快速補建）。
    /// </summary>
    public async Task<VisualSyncSummary> SyncAsync(
        string? rootDirectory,
        IReadOnlyList<string>? specificPaths = null,
        int? maxComics = null,
        IProgress<VisualIndexProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _paths.EnsureDirectories();

        bool isFullLibrarySync = specificPaths == null || specificPaths.Count == 0;
        var discovered = isFullLibrarySync
            ? VisualComicReader.DiscoverComics(rootDirectory ?? "", maxComics)
            : specificPaths!
                .Select(VisualComicReader.TryCreateItem)
                .Where(x => x != null)
                .Cast<VisualComicItem>()
                .ToList();

        using var db = new VisualIndexDatabase(_paths.DatabasePath, readOnly: false);
        using var embedder = _embedderFactory();
        using var vectorStore = new ShardedVectorStore(_paths, embedder.Dimensions, readOnlyMmap: false, _maxVectorsPerShard);

        // 1. 先比對指紋：分類為「未變動 / 搬移改名 / 內容變更 / 全新作品」
        var workQueue = new List<(VisualComicItem Item, string Fingerprint, long? ExistingChangedMangaId)>();
        var presentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        int moved = 0;
        int deleted = 0;

        progress?.Report(new VisualIndexProgress("正在比對檔案指紋…", 0, discovered.Count, 0, 0, 0, 0, 0, ""));

        foreach (var comic in discovered)
        {
            ct.ThrowIfCancellationRequested();
            presentPaths.Add(comic.Path);

            var fp = VisualComicReader.ComputeContentFingerprint(comic);
            var byPath = db.GetMangaByPath(comic.Path);
            if (byPath != null)
            {
                if (string.Equals(byPath.Fingerprint, fp, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }
                // 同路徑但內容已變更（換高清版等）
                workQueue.Add((comic, fp, byPath.Id));
                continue;
            }

            // 路徑不在庫中 → 檢查是否為「搬移或改名」（指紋相同且舊路徑已不存在於磁碟）
            var sameFpMatches = db.FindMangasByFingerprint(fp);
            var orphanMatch = sameFpMatches.FirstOrDefault(m => !File.Exists(m.Path) && !Directory.Exists(m.Path));
            if (orphanMatch != null)
            {
                db.UpdateMangaLocation(orphanMatch.Id, comic.Path, comic.Title);
                presentPaths.Add(comic.Path);
                moved++;
                continue;
            }

            // 全新作品（包含新作者資料夾內的作品）
            workQueue.Add((comic, fp, null));
        }

        // 2. 全庫掃描且未設定 maxComics 上限時，自動清理已從磁碟移除的作品
        if (isFullLibrarySync && !maxComics.HasValue)
        {
            var allIndexed = db.ListAllMangas();
            foreach (var indexed in allIndexed)
            {
                if (presentPaths.Contains(indexed.Path)) continue;
                if (File.Exists(indexed.Path) || Directory.Exists(indexed.Path)) continue;

                var removedVecs = db.MarkMangaVectorsDeleted(indexed.Id, removeMangaRow: true);
                foreach (var (vecId, shardId) in removedVecs)
                {
                    vectorStore.Remove(shardId, (ulong)vecId);
                }
                deleted++;
            }
        }

        if (workQueue.Count == 0)
        {
            vectorStore.SaveDirtyShards();
            sw.Stop();
            progress?.Report(new VisualIndexProgress("索引已為最新狀態", 0, 0, 0, 0, moved, deleted, skipped, ""));
            return new VisualSyncSummary(discovered.Count, 0, skipped, moved, deleted, 0, 0, false, sw.Elapsed.TotalSeconds);
        }

        // 3. 逐批處理待建庫作品（每完成 _checkpointComicInterval 本就落盤一次，確保優雅中斷可無縫續建）
        int indexedComics = 0;
        int totalPages = 0;
        int totalVectorsAdded = 0;
        bool wasCancelled = false;

        using var detector = _panelDetectorFactory();

        for (int offset = 0; offset < workQueue.Count; offset += _checkpointComicInterval)
        {
            if (ct.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            var chunk = workQueue.Skip(offset).Take(_checkpointComicInterval).ToList();
            var chunkStats = await IndexChunkAsync(
                chunk,
                db,
                vectorStore,
                detector,
                embedder,
                offset,
                workQueue.Count,
                moved,
                deleted,
                skipped,
                totalPages,
                totalVectorsAdded,
                progress,
                ct).ConfigureAwait(false);

            indexedComics += chunkStats.Indexed;
            totalPages += chunkStats.Pages;
            totalVectorsAdded += chunkStats.Vectors;
            vectorStore.SaveDirtyShards();

            if (chunkStats.Cancelled)
            {
                wasCancelled = true;
                break;
            }
        }

        sw.Stop();
        progress?.Report(new VisualIndexProgress(
            wasCancelled ? "已安全中斷並保存進度" : "增量建庫完成",
            indexedComics,
            workQueue.Count,
            totalPages,
            totalVectorsAdded,
            moved,
            deleted,
            skipped,
            ""));

        return new VisualSyncSummary(
            discovered.Count,
            indexedComics,
            skipped,
            moved,
            deleted,
            totalPages,
            totalVectorsAdded,
            wasCancelled,
            sw.Elapsed.TotalSeconds);
    }

    private async Task<(int Indexed, int Pages, int Vectors, bool Cancelled)> IndexChunkAsync(
        List<(VisualComicItem Item, string Fingerprint, long? ExistingChangedMangaId)> chunk,
        VisualIndexDatabase db,
        ShardedVectorStore vectorStore,
        IPanelDetector? detector,
        IVisualEmbedder embedder,
        int baseDoneComics,
        int totalTargetComics,
        int moved,
        int deleted,
        int skipped,
        int basePages,
        int baseVectors,
        IProgress<VisualIndexProgress>? progress,
        CancellationToken ct)
    {
        // 先建立待處理作品清單（若為換版則先標記刪除舊向量）
        var preparedWork = new List<(VisualComicItem Item, string Fingerprint, long MangaId)>(chunk.Count);
        foreach (var (item, fp, changedId) in chunk)
        {
            if (changedId.HasValue)
            {
                var oldVecs = db.MarkMangaVectorsDeleted(changedId.Value, removeMangaRow: false);
                foreach (var (vecId, shardId) in oldVecs)
                {
                    vectorStore.Remove(shardId, (ulong)vecId);
                }
            }
            // 先暫存空白指紋，待該本全部向量寫入完成後再寫入正式指紋，確保中斷時未完成本下次會自動重跑
            long mangaId = db.UpsertManga(item.Path, item.Title, "__PENDING__", pageCount: 0);
            preparedWork.Add((item, fp, mangaId));
        }

        var rawChannel = Channel.CreateBounded<RawComicPage>(new BoundedChannelOptions(48)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var pageChannel = Channel.CreateBounded<PreparedPage>(new BoundedChannelOptions(32)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var writeChannel = Channel.CreateBounded<List<EmbeddedItem>>(new BoundedChannelOptions(16)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        int chunkPages = 0;
        int chunkVectors = 0;
        int completedComicsCount = 0;
        bool cancelled = false;
        var pagesPerManga = new ConcurrentDictionary<long, int>();
        var completedMangaIds = new ConcurrentBag<long>();

        // Stage 1a: I/O 串流解壓
        var ioTask = Task.Run(async () =>
        {
            try
            {
                foreach (var (item, _, mangaId) in preparedWork)
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    int pages = 0;
                    foreach (var raw in VisualComicReader.StreamRawPages(mangaId, item, ct))
                    {
                        pages++;
                        Interlocked.Increment(ref chunkPages);
                        await rawChannel.Writer.WriteAsync(raw, ct).ConfigureAwait(false);
                    }
                    pagesPerManga[mangaId] = pages;
                    completedMangaIds.Add(mangaId);
                    int doneNow = Interlocked.Increment(ref completedComicsCount);
                    progress?.Report(new VisualIndexProgress(
                        "正在建立視覺索引…",
                        baseDoneComics + doneNow,
                        totalTargetComics,
                        basePages + chunkPages,
                        baseVectors + Volatile.Read(ref chunkVectors),
                        moved,
                        deleted,
                        skipped,
                        item.Title));
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                rawChannel.Writer.Complete();
            }
        });

        // Stage 1b: 多核心並行解碼 + YOLO Letterbox
        int decodeWorkers = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        var decodeTasks = Enumerable.Range(0, decodeWorkers).Select(_ => Task.Run(async () =>
        {
            await foreach (var raw in rawChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                SKImage? img = null;
                try
                {
                    using var bmp = SKBitmap.Decode(raw.Bytes);
                    if (bmp == null) continue;
                    img = SKImage.FromBitmap(bmp);

                    PreparedYoloFrame? yoloIn = detector != null
                        ? VisualImagePreprocess.CreateYoloLetterbox(img, YoloPanelDetector.InputSize)
                        : null;

                    await pageChannel.Writer.WriteAsync(new PreparedPage(raw.MangaId, raw.PageNo, raw.EntryName, img, yoloIn)).ConfigureAwait(false);
                    img = null;
                }
                catch
                {
                    img?.Dispose();
                }
            }
        })).ToArray();

        var decodeCoordTask = Task.Run(async () =>
        {
            await Task.WhenAll(decodeTasks).ConfigureAwait(false);
            pageChannel.Writer.Complete();
        });

        // Stage 2: GPU 分鏡偵測 + 並行一步裁切縮放 + 批次嵌入
        var gpuTask = Task.Run(async () =>
        {
            var pending = new List<PendingTensor>(_batchSize * 2);

            async Task FlushBatchAsync(bool forceAll)
            {
                while (pending.Count >= _batchSize || (forceAll && pending.Count > 0))
                {
                    int take = Math.Min(_batchSize, pending.Count);
                    var slice = pending.GetRange(0, take);
                    pending.RemoveRange(0, take);

                    var vecs = embedder.EmbedPreprocessedBatch(slice.Select(s => s.ChwData).ToList());
                    var outBatch = new List<EmbeddedItem>(take);
                    for (int i = 0; i < take; i++)
                    {
                        var s = slice[i];
                        outBatch.Add(new EmbeddedItem(s.MangaId, s.PageNo, s.EntryName, s.IsFullPage, s.X, s.Y, s.W, s.H, vecs[i]));
                    }
                    await writeChannel.Writer.WriteAsync(outBatch).ConfigureAwait(false);
                }
            }

            try
            {
                await foreach (var work in pageChannel.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    using (work)
                    {
                        var img = work.Image;
                        var cropsToBuild = new List<(bool IsFullPage, float X, float Y, float W, float H, SKRect Rect)>
                        {
                            (true, 0f, 0f, 1f, 1f, new SKRect(0, 0, img.Width, img.Height))
                        };

                        if (detector != null && work.YoloInput is { } yIn)
                        {
                            var panels = detector.DetectPreprocessed(yIn)
                                .Where(b => b.ClassId == 0)
                                .ToList();

                            foreach (var p in panels)
                            {
                                float x1 = Math.Clamp(p.X * img.Width, 0, img.Width - 1);
                                float y1 = Math.Clamp(p.Y * img.Height, 0, img.Height - 1);
                                float x2 = Math.Clamp((p.X + p.W) * img.Width, x1 + 1, img.Width);
                                float y2 = Math.Clamp((p.Y + p.H) * img.Height, y1 + 1, img.Height);
                                if (x2 - x1 >= 32 && y2 - y1 >= 32)
                                {
                                    cropsToBuild.Add((false, p.X, p.Y, p.W, p.H, new SKRect(x1, y1, x2, y2)));
                                }
                            }
                        }

                        var built = new PendingTensor[cropsToBuild.Count];
                        Parallel.For(0, cropsToBuild.Count, i =>
                        {
                            var c = cropsToBuild[i];
                            var chw = VisualImagePreprocess.CreateCropNormalizedChw(img, c.Rect, embedder.InputSize);
                            built[i] = new PendingTensor(work.MangaId, work.PageNo, work.EntryName, c.IsFullPage, c.X, c.Y, c.W, c.H, chw);
                        });

                        pending.AddRange(built);
                        if (pending.Count >= _batchSize)
                        {
                            await FlushBatchAsync(forceAll: false).ConfigureAwait(false);
                        }
                    }
                }
                await FlushBatchAsync(forceAll: true).ConfigureAwait(false);
            }
            finally
            {
                writeChannel.Writer.Complete();
            }
        });

        // Stage 3: SQLite + 分片向量寫入
        var dbTask = Task.Run(async () =>
        {
            await foreach (var batch in writeChannel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                foreach (var group in batch.GroupBy(b => b.MangaId))
                {
                    var list = group.ToList();
                    int shardId = vectorStore.CurrentWriteShardId;
                    var metaRows = list.Select(x => (x.PageNo, x.EntryName, x.IsFullPage, x.X, x.Y, x.W, x.H)).ToList();
                    var vecIds = db.InsertVectorsBatch(group.Key, shardId, metaRows);
                    for (int i = 0; i < vecIds.Count; i++)
                    {
                        vectorStore.Add(shardId, (ulong)vecIds[i], list[i].Vector);
                    }
                    Interlocked.Add(ref chunkVectors, vecIds.Count);
                }
            }
        });

        await Task.WhenAll(ioTask, decodeCoordTask, gpuTask, dbTask).ConfigureAwait(false);

        // 僅對已完整寫完的作品提交正式指紋；中斷而未完的作品回滾刪除，確保續建乾淨無殘留
        var completedSet = completedMangaIds.ToHashSet();
        int finalized = 0;
        foreach (var (item, fp, mangaId) in preparedWork)
        {
            if (completedSet.Contains(mangaId))
            {
                int pc = pagesPerManga.GetValueOrDefault(mangaId, 0);
                db.UpsertManga(item.Path, item.Title, fp, pc);
                finalized++;
            }
            else
            {
                var partialVecs = db.MarkMangaVectorsDeleted(mangaId, removeMangaRow: true);
                foreach (var (vecId, shardId) in partialVecs)
                {
                    vectorStore.Remove(shardId, (ulong)vecId);
                }
            }
        }

        return (finalized, chunkPages, chunkVectors, cancelled);
    }

    /// <summary>
    /// 回收已刪除向量佔用的分片空間（預設僅重整刪除比例 &gt;= minDeletedRatio 的分片）。
    /// </summary>
    public static int CompactShards(VisualSearchPaths paths, int dimensions = SscdFeatureEmbedder.DefaultDimensions, double minDeletedRatio = 0.15)
    {
        if (!File.Exists(paths.DatabasePath)) return 0;

        using var db = new VisualIndexDatabase(paths.DatabasePath, readOnly: false);
        using var store = new ShardedVectorStore(paths, dimensions, readOnlyMmap: false);

        var overview = db.GetOverview();
        int compactedShards = 0;
        foreach (var shard in overview.Shards)
        {
            if (shard.DeletedCount > 0 && shard.DeletedRatio >= minDeletedRatio)
            {
                var activeIds = db.GetActiveVectorIdsInShard(shard.ShardId);
                store.CompactShard(shard.ShardId, activeIds);
                db.PurgeDeletedVectorsInShard(shard.ShardId);
                compactedShards++;
            }
        }
        return compactedShards;
    }
}

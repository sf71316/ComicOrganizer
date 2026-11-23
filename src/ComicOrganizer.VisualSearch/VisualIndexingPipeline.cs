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
    double ElapsedSeconds,
    int HeadBackfilledComics = 0);

/// <summary>
/// 視覺索引建庫與增量同步管線：
/// 支援新作者／新漫畫追加、XxHash3 指紋搬移偵測（零重算更新路徑）、已刪除清理、
/// 分片寫入、6 核心並行解碼、優雅中斷續建，以及方案 B 角色頭像向量增量補建（Backfill）。
/// </summary>
public sealed class VisualIndexingPipeline
{
    private readonly VisualSearchPaths _paths;
    private readonly Func<IPanelDetector?> _panelDetectorFactory;
    private readonly Func<IPanelDetector?> _headDetectorFactory;
    private readonly Func<IVisualEmbedder> _embedderFactory;
    private readonly bool _enableHeadIndex;
    private readonly int _batchSize;
    private readonly int _maxVectorsPerShard;
    private readonly int _checkpointComicInterval;

    public VisualIndexingPipeline(
        VisualSearchPaths paths,
        bool useDirectMl = true,
        int batchSize = 32,
        int maxVectorsPerShard = ShardedVectorStore.DefaultMaxVectorsPerShard,
        int checkpointComicInterval = 20,
        bool enableHeadIndex = false)
        : this(
            paths,
            () => new YoloPanelDetector(paths.PanelModelPath, useDirectMl),
            () => new SscdFeatureEmbedder(paths.EmbedModelPath, useDirectMl),
            batchSize,
            maxVectorsPerShard,
            checkpointComicInterval,
            enableHeadIndex,
            () => enableHeadIndex && paths.IsHeadModelReady()
                ? new YoloPanelDetector(paths.HeadModelPath, useDirectMl)
                : null)
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
        int checkpointComicInterval = 20,
        bool enableHeadIndex = false,
        Func<IPanelDetector?>? headDetectorFactory = null)
    {
        _paths = paths;
        _panelDetectorFactory = panelDetectorFactory;
        _headDetectorFactory = headDetectorFactory ?? (() => null);
        _embedderFactory = embedderFactory;
        _enableHeadIndex = enableHeadIndex;
        _batchSize = Math.Max(1, batchSize);
        _maxVectorsPerShard = Math.Max(1, maxVectorsPerShard);
        _checkpointComicInterval = Math.Max(1, checkpointComicInterval);
    }

    private enum ComicWorkMode
    {
        FullIndex = 0,
        HeadBackfillOnly = 1,
    }

    private sealed record QueuedComicWork(
        VisualComicItem Item,
        string Fingerprint,
        long? ExistingMangaId,
        ComicWorkMode Mode);

    private sealed record PreparedComicWork(
        VisualComicItem Item,
        string Fingerprint,
        long MangaId,
        ComicWorkMode Mode,
        Dictionary<int, List<VisualPanelBox>>? ExistingPanelsByPage);

    private sealed record PreparedPage(
        long MangaId,
        int PageNo,
        string EntryName,
        SKImage Image,
        PreparedYoloFrame? YoloInput,
        ComicWorkMode Mode) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    private sealed record PendingTensor(
        long MangaId,
        int PageNo,
        string EntryName,
        VisualRegionKind RegionKind,
        float X,
        float Y,
        float W,
        float H,
        float[] ChwData);

    private sealed record EmbeddedItem(
        long MangaId,
        int PageNo,
        string EntryName,
        VisualRegionKind RegionKind,
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
        VisualNativeLoader.EnsureLoaded();
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
        using var headDetector = _enableHeadIndex ? _headDetectorFactory() : null;
        bool headIndexActive = _enableHeadIndex && headDetector != null;

        // 1. 先比對指紋：分類為「未變動 / 搬移改名 / 僅補建人頭 / 內容變更 / 全新作品」
        var workQueue = new List<QueuedComicWork>();
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
                    if (headIndexActive && !byPath.HasHeadIndex)
                    {
                        workQueue.Add(new QueuedComicWork(comic, fp, byPath.Id, ComicWorkMode.HeadBackfillOnly));
                    }
                    else
                    {
                        skipped++;
                    }
                    continue;
                }
                // 同路徑但內容已變更（換高清版等）
                workQueue.Add(new QueuedComicWork(comic, fp, byPath.Id, ComicWorkMode.FullIndex));
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
                if (headIndexActive && !orphanMatch.HasHeadIndex)
                {
                    workQueue.Add(new QueuedComicWork(comic, fp, orphanMatch.Id, ComicWorkMode.HeadBackfillOnly));
                }
                continue;
            }

            // 全新作品（包含新作者資料夾內的作品）
            workQueue.Add(new QueuedComicWork(comic, fp, null, ComicWorkMode.FullIndex));
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
            return new VisualSyncSummary(discovered.Count, 0, skipped, moved, deleted, 0, 0, false, sw.Elapsed.TotalSeconds, 0);
        }

        // 3. 逐批處理待建庫作品（每完成 _checkpointComicInterval 本就落盤一次，確保優雅中斷可無縫續建）
        int indexedComics = 0;
        int headBackfilledComics = 0;
        int totalPages = 0;
        int totalVectorsAdded = 0;
        bool wasCancelled = false;

        bool needsPanelDetector = workQueue.Any(w => w.Mode == ComicWorkMode.FullIndex);
        using var detector = needsPanelDetector ? _panelDetectorFactory() : null;

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
                headDetector,
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

            indexedComics += chunkStats.FullIndexed;
            headBackfilledComics += chunkStats.HeadBackfilled;
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
            indexedComics + headBackfilledComics,
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
            sw.Elapsed.TotalSeconds,
            headBackfilledComics);
    }

    private async Task<(int FullIndexed, int HeadBackfilled, int Pages, int Vectors, bool Cancelled)> IndexChunkAsync(
        List<QueuedComicWork> chunk,
        VisualIndexDatabase db,
        ShardedVectorStore vectorStore,
        IPanelDetector? detector,
        IPanelDetector? headDetector,
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
        // 先建立待處理作品清單：
        // - FullIndex：若為換版則標記刪除舊向量，暫存 __PENDING__ 指紋
        // - HeadBackfillOnly：保留既有整頁與分鏡向量，預先讀出既有分鏡框供去重
        var preparedWork = new List<PreparedComicWork>(chunk.Count);
        var preparedByMangaId = new Dictionary<long, PreparedComicWork>(chunk.Count);
        foreach (var q in chunk)
        {
            if (q.Mode == ComicWorkMode.HeadBackfillOnly && q.ExistingMangaId.HasValue)
            {
                long mangaId = q.ExistingMangaId.Value;
                // 若上次補建人頭中途異常殘留，先清除殘留人頭向量再補
                var staleHeads = db.MarkMangaHeadVectorsDeleted(mangaId);
                foreach (var (vecId, shardId) in staleHeads)
                {
                    vectorStore.Remove(shardId, (ulong)vecId);
                }

                var existingPanels = db.GetPanelBoxesForManga(mangaId);
                var pw = new PreparedComicWork(q.Item, q.Fingerprint, mangaId, ComicWorkMode.HeadBackfillOnly, existingPanels);
                preparedWork.Add(pw);
                preparedByMangaId[mangaId] = pw;
            }
            else
            {
                if (q.ExistingMangaId.HasValue)
                {
                    var oldVecs = db.MarkMangaVectorsDeleted(q.ExistingMangaId.Value, removeMangaRow: false);
                    foreach (var (vecId, shardId) in oldVecs)
                    {
                        vectorStore.Remove(shardId, (ulong)vecId);
                    }
                }
                // 先暫存空白指紋，待該本全部向量寫入完成後再寫入正式指紋，確保中斷時未完成本下次會自動重跑
                long mangaId = db.UpsertManga(q.Item.Path, q.Item.Title, "__PENDING__", pageCount: 0, hasHeadIndex: false);
                var pw = new PreparedComicWork(q.Item, q.Fingerprint, mangaId, ComicWorkMode.FullIndex, null);
                preparedWork.Add(pw);
                preparedByMangaId[mangaId] = pw;
            }
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
                foreach (var pw in preparedWork)
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    int pages = 0;
                    foreach (var raw in VisualComicReader.StreamRawPages(pw.MangaId, pw.Item, ct))
                    {
                        pages++;
                        Interlocked.Increment(ref chunkPages);
                        await rawChannel.Writer.WriteAsync(raw, ct).ConfigureAwait(false);
                    }
                    pagesPerManga[pw.MangaId] = pages;
                    completedMangaIds.Add(pw.MangaId);
                    int doneNow = Interlocked.Increment(ref completedComicsCount);
                    progress?.Report(new VisualIndexProgress(
                        pw.Mode == ComicWorkMode.HeadBackfillOnly ? "正在增量補建頭像索引…" : "正在建立視覺索引…",
                        baseDoneComics + doneNow,
                        totalTargetComics,
                        basePages + chunkPages,
                        baseVectors + Volatile.Read(ref chunkVectors),
                        moved,
                        deleted,
                        skipped,
                        pw.Item.Title));
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
        bool needYoloFrame = detector != null || headDetector != null;
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

                    var mode = preparedByMangaId[raw.MangaId].Mode;
                    bool prepareYolo = mode == ComicWorkMode.HeadBackfillOnly
                        ? headDetector != null
                        : needYoloFrame;
                    PreparedYoloFrame? yoloIn = prepareYolo
                        ? VisualImagePreprocess.CreateYoloLetterbox(img, YoloPanelDetector.InputSize)
                        : null;

                    await pageChannel.Writer.WriteAsync(new PreparedPage(raw.MangaId, raw.PageNo, raw.EntryName, img, yoloIn, mode)).ConfigureAwait(false);
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

        // Stage 2: GPU 分鏡／人頭偵測 + 並行一步裁切縮放 + 批次嵌入
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
                        outBatch.Add(new EmbeddedItem(s.MangaId, s.PageNo, s.EntryName, s.RegionKind, s.X, s.Y, s.W, s.H, vecs[i]));
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
                        var cropsToBuild = new List<(VisualRegionKind RegionKind, float X, float Y, float W, float H, SKRect Rect)>();
                        IReadOnlyList<VisualPanelBox>? panelBoxesForDedup = null;

                        if (work.Mode == ComicWorkMode.FullIndex)
                        {
                            cropsToBuild.Add((VisualRegionKind.FullPage, 0f, 0f, 1f, 1f, new SKRect(0, 0, img.Width, img.Height)));

                            if (detector != null && work.YoloInput is { } yIn)
                            {
                                var panels = detector.DetectPreprocessed(yIn)
                                    .Where(b => b.ClassId == 0)
                                    .ToList();
                                panelBoxesForDedup = panels;

                                foreach (var p in panels)
                                {
                                    float x1 = Math.Clamp(p.X * img.Width, 0, img.Width - 1);
                                    float y1 = Math.Clamp(p.Y * img.Height, 0, img.Height - 1);
                                    float x2 = Math.Clamp((p.X + p.W) * img.Width, x1 + 1, img.Width);
                                    float y2 = Math.Clamp((p.Y + p.H) * img.Height, y1 + 1, img.Height);
                                    if (x2 - x1 >= 32 && y2 - y1 >= 32)
                                    {
                                        cropsToBuild.Add((VisualRegionKind.Panel, p.X, p.Y, p.W, p.H, new SKRect(x1, y1, x2, y2)));
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 僅補建人頭：直接沿用資料庫中已存的既有分鏡框做重疊去重，完全不重跑分鏡模型
                            var existingByPage = preparedByMangaId[work.MangaId].ExistingPanelsByPage;
                            if (existingByPage != null && existingByPage.TryGetValue(work.PageNo, out var savedPanels))
                            {
                                panelBoxesForDedup = savedPanels;
                            }
                        }

                        if (headDetector != null && work.YoloInput is { } hyIn)
                        {
                            var rawHeads = headDetector.DetectPreprocessed(
                                hyIn,
                                confThreshold: VisualHeadCropHelper.DefaultHeadConfThreshold,
                                iouThreshold: VisualHeadCropHelper.DefaultHeadIouThreshold,
                                minAreaRatio: VisualHeadCropHelper.DefaultHeadMinAreaRatio);
                            var expandedHeads = VisualHeadCropHelper.ExpandAndFilterHeads(rawHeads, panelBoxesForDedup, img.Width, img.Height);
                            foreach (var h in expandedHeads)
                            {
                                cropsToBuild.Add((VisualRegionKind.Head, h.X, h.Y, h.W, h.H, h.Rect));
                            }
                        }

                        if (cropsToBuild.Count == 0)
                        {
                            continue;
                        }

                        var built = new PendingTensor[cropsToBuild.Count];
                        Parallel.For(0, cropsToBuild.Count, i =>
                        {
                            var c = cropsToBuild[i];
                            var chw = VisualImagePreprocess.CreateCropNormalizedChw(img, c.Rect, embedder.InputSize);
                            built[i] = new PendingTensor(work.MangaId, work.PageNo, work.EntryName, c.RegionKind, c.X, c.Y, c.W, c.H, chw);
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
                    var metaRows = list.Select(x => (x.PageNo, x.EntryName, x.RegionKind, x.X, x.Y, x.W, x.H)).ToList();
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

        // 僅對已完整寫完的作品提交完成狀態；中斷而未完的作品回滾刪除，確保續建乾淨無殘留
        var completedSet = completedMangaIds.ToHashSet();
        int finalizedFull = 0;
        int finalizedHeadBackfill = 0;
        foreach (var pw in preparedWork)
        {
            if (pw.Mode == ComicWorkMode.HeadBackfillOnly)
            {
                if (completedSet.Contains(pw.MangaId))
                {
                    db.MarkMangaHeadIndexed(pw.MangaId);
                    finalizedHeadBackfill++;
                }
                else
                {
                    // 只回滾未完成的人頭向量，完整保留既有整頁與分鏡索引
                    var partialHeads = db.MarkMangaHeadVectorsDeleted(pw.MangaId);
                    foreach (var (vecId, shardId) in partialHeads)
                    {
                        vectorStore.Remove(shardId, (ulong)vecId);
                    }
                }
            }
            else
            {
                if (completedSet.Contains(pw.MangaId))
                {
                    int pc = pagesPerManga.GetValueOrDefault(pw.MangaId, 0);
                    db.UpsertManga(pw.Item.Path, pw.Item.Title, pw.Fingerprint, pc, hasHeadIndex: headDetector != null);
                    finalizedFull++;
                }
                else
                {
                    var partialVecs = db.MarkMangaVectorsDeleted(pw.MangaId, removeMangaRow: true);
                    foreach (var (vecId, shardId) in partialVecs)
                    {
                        vectorStore.Remove(shardId, (ulong)vecId);
                    }
                }
            }
        }

        return (finalizedFull, finalizedHeadBackfill, chunkPages, chunkVectors, cancelled);
    }

    /// <summary>
    /// 回收已刪除向量佔用的分片空間（預設僅重整刪除比例 &gt;= minDeletedRatio 的分片）。
    /// </summary>
    public static int CompactShards(VisualSearchPaths paths, int dimensions = SscdFeatureEmbedder.DefaultDimensions, double minDeletedRatio = 0.15)
    {
        if (!File.Exists(paths.DatabasePath)) return 0;

        VisualNativeLoader.EnsureLoaded();
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

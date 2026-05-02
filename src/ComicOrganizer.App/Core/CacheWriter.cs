using System.Collections.Concurrent;
using System.Diagnostics;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 一筆 cache 寫入操作。多種模式由不同欄位組合表示：
///   1. 縮圖寫入：Meta + ThumbKey + JpegBytes
///   2. Touch (cache hit)：TouchOnly = path
///   3. metadata 寫入：PathForMetadata + Parsed（可選 IsAuthorFolder / AuthorFolderPath）
/// 同一個 op 可同時帶縮圖+metadata；同一個 batch transaction 內 op 順序保留 → 先寫 catalog 再寫 metadata 不會找不到 asset_id。
/// </summary>
public class CacheWriteOp
{
    public AssetMeta? Meta { get; set; }
    public string? ThumbKey { get; set; }
    public byte[]? JpegBytes { get; set; }

    /// <summary>非 null 時表示這是 touch 操作：更新該 path 的 last_accessed。</summary>
    public string? TouchOnly { get; set; }

    /// <summary>metadata 寫入目標 path（用於查 asset_id）。配合 Parsed 使用。</summary>
    public string? PathForMetadata { get; set; }

    /// <summary>從 FilenameMetadataParser 解析出的 metadata 內容。</summary>
    public ParsedMetadata? Parsed { get; set; }

    /// <summary>true 時跳過 asset_meta 寫入、只 upsert author（適用 Author 資料夾本身）。</summary>
    public bool IsAuthorFolder { get; set; }

    /// <summary>Author 表的 folder_path 欄位值（IsAuthorFolder=true 時使用）。</summary>
    public string? AuthorFolderPath { get; set; }
}

/// <summary>
/// Cache 寫入背景 worker：UI thread / decode task 把 ops 丟進 queue 後立即返回，
/// 由 worker thread 取出批次（最多 100 ops 或 100ms）+ 一次 transaction 寫入。
/// 避免每筆 cache miss 都各自開 SQLite transaction 的 WAL flush 開銷。
/// </summary>
public class CacheWriter : IDisposable
{
    private readonly BlockingCollection<CacheWriteOp> _queue = new(boundedCapacity: 1024);
    private readonly Thread _worker;
    private readonly IAssetMetaStore _metaStore;
    private readonly IThumbStore _thumbStore;
    private readonly AppConfig _config;
    private const int MaxBatchSize = 100;
    private const int BatchWindowMs = 100;

    // LRU eviction：每 N 個批次或 N 秒檢查一次 cache 大小，超過 limit 才掃一次淘汰
    private int _batchesSinceLastEvictionCheck;
    private const int EvictionCheckEveryBatches = 20;

    /// <summary>累計被 LRU 淘汰的 thumb 數。可被外部 snapshot 比對 delta（例如 UpdateIndex 開始/結束）。</summary>
    private long _totalEvictedCount;
    public long TotalEvictedCount => Interlocked.Read(ref _totalEvictedCount);

    public CacheWriter(IAssetMetaStore metaStore, IThumbStore thumbStore, AppConfig config)
    {
        _metaStore = metaStore;
        _thumbStore = thumbStore;
        _config = config;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "CacheWriter"
        };
        _worker.Start();
    }

    /// <summary>把寫入 op 加入 queue。queue 滿時阻塞直到 worker 消化，避免 silent drop。</summary>
    public bool Enqueue(CacheWriteOp op)
    {
        try
        {
            _queue.Add(op);   // 阻塞直到 queue 有空間；CompleteAdding 後會 throw → 視為失敗
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Cache hit 路徑用：更新 last_accessed 不寫 thumb。</summary>
    public bool EnqueueTouch(string path)
    {
        try
        {
            _queue.Add(new CacheWriteOp { TouchOnly = path });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void WorkerLoop()
    {
        var batch = new List<CacheWriteOp>(MaxBatchSize);
        try
        {
            while (true)
            {
                // 阻塞等第一個 op；CompleteAdding 後 Take 會 throw → catch 退出
                CacheWriteOp first;
                try
                {
                    first = _queue.Take();
                }
                catch (InvalidOperationException)
                {
                    break;   // queue completed
                }
                batch.Add(first);

                // 收 100ms 內後續 ops，最多 100 筆
                var deadline = Environment.TickCount + BatchWindowMs;
                while (batch.Count < MaxBatchSize)
                {
                    int remain = deadline - Environment.TickCount;
                    if (remain <= 0) break;
                    if (!_queue.TryTake(out var next, remain)) break;
                    batch.Add(next);
                }

                FlushBatch(batch);
                batch.Clear();

                // 每 N 個批次檢查一次 cache 大小，超過 limit 才 evict
                if (++_batchesSinceLastEvictionCheck >= EvictionCheckEveryBatches)
                {
                    _batchesSinceLastEvictionCheck = 0;
                    TrimToLimit();
                }
            }

            // queue 已 complete，drain 剩餘 ops
            while (_queue.TryTake(out var op))
            {
                batch.Add(op);
                if (batch.Count >= MaxBatchSize)
                {
                    FlushBatch(batch);
                    batch.Clear();
                }
            }
            if (batch.Count > 0) FlushBatch(batch);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CacheWriter] worker crashed: {ex}");
        }
    }

    /// <summary>
    /// LRU eviction：算 thumbs/ 總大小，若超過 ThumbCacheMaxBytes 就分批刪最舊的 cache。
    /// 設定值為 0 視為不限制。
    /// 由 worker thread 在 batch 寫入後自動呼叫；外部（如 UpdateIndex 結束時、設定變更時）
    /// 也可主動呼叫做明確 trim。多執行緒下 _metaStore 用 _lock 序列化、_thumbStore 用 fs API thread-safe。
    /// </summary>
    public void TrimToLimit()
    {
        long limit = _config.ThumbCacheMaxBytes;
        if (limit <= 0) return;

        long totalSize;
        try { totalSize = _thumbStore.TotalSizeBytes(); }
        catch { return; }

        if (totalSize <= limit) return;

        Debug.WriteLine($"[CacheWriter] cache size {totalSize / 1024 / 1024}MB > {limit / 1024 / 1024}MB → evict");

        const int BatchEvictCount = 100;
        long evictedBytes = 0;
        int evictedCount = 0;
        int safetyMaxIterations = 20;   // 最多刪 20 batch (~2000 entries) 避免無限 loop

        try
        {
            while (totalSize > limit && safetyMaxIterations-- > 0)
            {
                var victims = _metaStore.ListLruVictims(BatchEvictCount);
                if (victims.Count == 0) break;

                _metaStore.RunInTransaction(() =>
                {
                    foreach (var v in victims)
                    {
                        if (v.CoverThumbKey != null)
                        {
                            try
                            {
                                using var stream = _thumbStore.OpenRead(v.CoverThumbKey);
                                evictedBytes += stream?.Length ?? 0;
                            }
                            catch { }
                            _thumbStore.Delete(v.CoverThumbKey);
                        }
                        // 保留 DB row 當 catalog，只清 thumb 相關欄位 — 下次 ListLruVictims 不會再選到這筆
                        v.CoverThumbKey = null;
                        v.CoverWidth = null;
                        v.CoverHeight = null;
                        _metaStore.Upsert(v);
                        evictedCount++;
                        Interlocked.Increment(ref _totalEvictedCount);
                    }
                });

                // 重算 total（每 batch 算一次）
                totalSize = _thumbStore.TotalSizeBytes();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CacheWriter] eviction failed: {ex.Message}");
        }

        Debug.WriteLine($"[CacheWriter] evicted {evictedCount} entries (~{evictedBytes / 1024}KB), now {totalSize / 1024 / 1024}MB");
    }

    private void FlushBatch(List<CacheWriteOp> batch)
    {
        try
        {
            _metaStore.RunInTransaction(() =>
            {
                foreach (var op in batch)
                {
                    try
                    {
                        // 1. Touch (cache hit 路徑)
                        if (op.TouchOnly != null)
                        {
                            _metaStore.TouchLastAccessed(op.TouchOnly, NowUnix());
                        }
                        // 2. 縮圖 + catalog 寫入
                        else if (op.Meta != null && op.ThumbKey != null && op.JpegBytes != null)
                        {
                            _thumbStore.Write(op.ThumbKey, op.JpegBytes);
                            _metaStore.Upsert(op.Meta);
                        }
                        // 2b. catalog-only（cover 失敗 / 沒 cover 的目錄也要進 catalog）
                        else if (op.Meta != null)
                        {
                            _metaStore.UpsertCatalogOnly(op.Meta);
                        }

                        // 3. metadata 寫入（catalog 必須已存在 — 同 batch 內前面的 Meta op 會建好）
                        if (op.Parsed != null && op.PathForMetadata != null)
                        {
                            WriteMetadata(op);
                        }
                    }
                    catch (Exception innerEx)
                    {
                        // 單筆失敗不影響 batch 其他 ops
                        Debug.WriteLine($"[CacheWriter] op failed: {innerEx.Message}");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CacheWriter] FlushBatch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 寫 metadata 到 author / asset_meta / tag / asset_tag 表。在 FlushBatch transaction 內呼叫。
    /// 找不到 asset_id 就 skip — 下次掃描會 retry（catalog 還沒寫，可能因為 race 或 op 順序）。
    /// </summary>
    private void WriteMetadata(CacheWriteOp op)
    {
        var assetId = _metaStore.GetAssetId(op.PathForMetadata!);
        if (assetId == null) return;

        var parsed = op.Parsed!;
        long? authorId = null;

        // Upsert author + alias（main_name 為 null 不寫，例：filename 沒帶 author）
        if (!string.IsNullOrEmpty(parsed.AuthorMainName))
        {
            authorId = _metaStore.UpsertAuthor(parsed.AuthorMainName, op.AuthorFolderPath);
            if (!string.IsNullOrEmpty(parsed.AuthorAlias))
            {
                _metaStore.AddAuthorAlias(authorId.Value, parsed.AuthorAlias);
            }
        }

        // Author 資料夾本身：只 upsert author，不寫 asset_meta（資料夾不是「漫畫」）
        if (op.IsAuthorFolder) return;

        // 寫 asset_meta（即使所有欄位都 null 也寫 row 標記「已 parse」）
        _metaStore.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId.Value,
            AuthorId = authorId,
            Title = parsed.Title,
            ChapterNo = parsed.ChapterNo,
            Language = parsed.Language,
            TranslationGroup = parsed.TranslationGroup,
            ParsedAt = NowUnix(),
        });

        // tags
        foreach (var tag in parsed.Tags)
        {
            var tagId = _metaStore.UpsertTag(tag.Name, tag.Category);
            _metaStore.AssociateTag(assetId.Value, tagId);
        }
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public void Dispose()
    {
        _queue.CompleteAdding();
        // 給 worker 最多 5s drain queue
        _worker.Join(TimeSpan.FromSeconds(5));
    }
}

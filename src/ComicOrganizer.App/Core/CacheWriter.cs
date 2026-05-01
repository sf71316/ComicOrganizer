using System.Collections.Concurrent;
using System.Diagnostics;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 一筆 cache 寫入操作。Upsert + thumb 寫入綁在同一筆 op，要嘛兩個都成、要嘛 transaction rollback。
/// TouchOnly 用於 cache hit 路徑 — 只更新 last_accessed，不寫 thumb。
/// </summary>
public class CacheWriteOp
{
    public AssetMeta? Meta { get; set; }
    public string? ThumbKey { get; set; }
    public byte[]? JpegBytes { get; set; }

    /// <summary>非 null 時表示這是 touch 操作：更新該 path 的 last_accessed。</summary>
    public string? TouchOnly { get; set; }
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
    private const int MaxBatchSize = 100;
    private const int BatchWindowMs = 100;

    public CacheWriter(IAssetMetaStore metaStore, IThumbStore thumbStore)
    {
        _metaStore = metaStore;
        _thumbStore = thumbStore;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "CacheWriter"
        };
        _worker.Start();
    }

    /// <summary>把寫入 op 加入 queue。queue 滿時 (極罕見) 直接丟棄不阻塞。</summary>
    public bool Enqueue(CacheWriteOp op)
    {
        return _queue.TryAdd(op);
    }

    /// <summary>Cache hit 路徑用：更新 last_accessed 不寫 thumb。</summary>
    public bool EnqueueTouch(string path)
    {
        return _queue.TryAdd(new CacheWriteOp { TouchOnly = path });
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

    private void FlushBatch(List<CacheWriteOp> batch)
    {
        try
        {
            using var tx = _metaStore.BeginTransaction();
            foreach (var op in batch)
            {
                try
                {
                    if (op.TouchOnly != null)
                    {
                        _metaStore.TouchLastAccessed(op.TouchOnly, NowUnix());
                    }
                    else if (op.Meta != null && op.ThumbKey != null && op.JpegBytes != null)
                    {
                        _thumbStore.Write(op.ThumbKey, op.JpegBytes);
                        _metaStore.Upsert(op.Meta);
                    }
                }
                catch (Exception innerEx)
                {
                    // 單筆失敗不影響 batch 其他 ops
                    Debug.WriteLine($"[CacheWriter] op failed: {innerEx.Message}");
                }
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CacheWriter] FlushBatch failed: {ex.Message}");
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

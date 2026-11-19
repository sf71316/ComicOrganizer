namespace ComicOrganizer.App.Core;

/// <summary>
/// 延遲提交「讀過」紀錄的 queue。誤點 / 即開即關 不應算一次閱讀，所以排程 N 秒後才實際
/// 呼叫 IAssetMetaStore.RecordRead。同 path 重排會 reset timer（短時間多次點擊不重複算）。
///
/// 內建 reader 完成後會直接走 reader 的更精準雙條件判斷（時間 + 翻頁數），
/// Phase 1.0 先用單純的「停留 N 秒」門檻服務外部 viewer 路徑。
///
/// 執行緒：EnqueueRead / Cancel 任意 thread；Recorded event 從 ThreadPool fire，
/// 訂閱者若需操作 UI 自行 Invoke。Flush / Dispose 同步等所有 pending。
/// </summary>
public class ReadRecordQueue : IDisposable
{
    private readonly IAssetMetaStore _store;
    private readonly int _thresholdSeconds;
    private readonly object _lock = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    private bool _disposed;

    /// <summary>實際 RecordRead 完成後 fire（path）。UI 用來 refresh 卡片 icon。</summary>
    public event Action<string>? Recorded;

    public ReadRecordQueue(IAssetMetaStore store, int thresholdSeconds = 30)
    {
        _store = store;
        _thresholdSeconds = Math.Max(1, thresholdSeconds);
    }

    /// <summary>排程一次「讀過」。已存在 pending 的同 path 會 reset timer，避免重複算。</summary>
    public void EnqueueRead(string path)
    {
        if (_disposed) return;
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_pending.TryGetValue(path, out var oldCts))
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }
            cts = new CancellationTokenSource();
            _pending[path] = cts;
        }

        _ = Task.Delay(TimeSpan.FromSeconds(_thresholdSeconds), cts.Token).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully) return;   // 被 cancel
            try
            {
                _store.RecordRead(path);
                Recorded?.Invoke(path);
            }
            catch { /* RecordRead 失敗不致命 */ }
            finally
            {
                lock (_lock)
                {
                    if (_pending.TryGetValue(path, out var current) && current == cts)
                        _pending.Remove(path);
                }
                cts.Dispose();
            }
        }, TaskScheduler.Default);
    }

    /// <summary>取消某個 path 的 pending（reader 內 < 門檻就關閉的場景）。</summary>
    public void Cancel(string path)
    {
        if (_disposed) return;
        lock (_lock)
        {
            if (_pending.TryGetValue(path, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _pending.Remove(path);
            }
        }
    }

    /// <summary>立即 commit 所有 pending（app 退出前呼叫）。</summary>
    public void Flush()
    {
        List<string> paths;
        lock (_lock)
        {
            paths = _pending.Keys.ToList();
            foreach (var cts in _pending.Values) { cts.Cancel(); cts.Dispose(); }
            _pending.Clear();
        }
        foreach (var p in paths)
        {
            try { _store.RecordRead(p); Recorded?.Invoke(p); }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
    }
}

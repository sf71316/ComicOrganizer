namespace ComicOrganizer.App.Core;

/// <summary>
/// 重建索引資料庫的「執行中狀態」共用追蹤器。
/// 解耦 CacheRebuildService 與 UI，讓使用者可關掉重建視窗、任務照常背景跑、
/// 重新開啟視窗看得到進度。statusBar 也訂閱即時顯示。
/// </summary>
public class RebuildProgressTracker
{
    private readonly object _lock = new();

    public bool IsRunning { get; private set; }
    public CacheRebuildService.Progress Current { get; private set; } = new();
    public string Phase { get; private set; } = "";        // "完整重建中" / "掃描中" / 等
    public bool WasInterrupted { get; private set; }       // 上次跑被取消 / 異常中止

    /// <summary>單一全域 CTS。任務啟動時建立、由 Cancel() / NotifyEnd 清空。</summary>
    public CancellationTokenSource? Cts { get; private set; }

    /// <summary>進度 / 階段 / 狀態任何變化都觸發。UI 訂閱此事件刷新。</summary>
    public event EventHandler? StateChanged;

    /// <summary>啟動：建立 CTS 並 fire 事件。回傳 token 給 service 用。</summary>
    public CancellationToken Start(string phase)
    {
        lock (_lock)
        {
            Cts?.Dispose();
            Cts = new CancellationTokenSource();
            IsRunning = true;
            Phase = phase;
            WasInterrupted = false;
            Current = new CacheRebuildService.Progress();
        }
        OnChanged();
        return Cts.Token;
    }

    public void Update(CacheRebuildService.Progress p)
    {
        lock (_lock) { Current = p; }
        OnChanged();
    }

    /// <summary>結束：cancelled=true 表示中途取消、可作為「繼續未完成」依據。</summary>
    public void NotifyEnd(bool cancelled)
    {
        lock (_lock)
        {
            IsRunning = false;
            WasInterrupted = cancelled;
            // CTS 留著 Cts 不 Dispose，避免 race；下次 Start 時才清舊
        }
        OnChanged();
    }

    /// <summary>外部觸發取消（按鈕）。</summary>
    public void Cancel()
    {
        Cts?.Cancel();
    }

    private void OnChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

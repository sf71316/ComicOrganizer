namespace ComicOrganizer.App.Core;

/// <summary>
/// 重建索引資料庫的「執行中狀態」共用追蹤器。
/// 解耦 CacheRebuildService 與 UI，讓使用者可關掉重建視窗、任務照常背景跑、
/// 重新開啟視窗看得到進度。statusBar 也訂閱即時顯示。
///
/// 跨 session 持久化：透過 IAssetMetaStore.app_state 寫旗標。App 啟動時若上次
/// 為「正在重建中」(crash 或刻意中斷)，自動標 WasInterrupted=true 顯示「繼續」按鈕。
/// </summary>
public class RebuildProgressTracker
{
    private readonly object _lock = new();
    private readonly IAssetMetaStore? _metaStore;
    private bool _stateChecked;

    private const string KeyInProgress = "rebuild_in_progress";    // "true" / null
    private const string KeyPhase = "rebuild_phase";

    public bool IsRunning { get; private set; }
    public RebuildProgress Current { get; private set; } = new();
    public string Phase { get; private set; } = "";
    public bool WasInterrupted { get; private set; }

    public CancellationTokenSource? Cts { get; private set; }

    public event EventHandler? StateChanged;

    public RebuildProgressTracker(IAssetMetaStore? metaStore = null, bool deferStateCheck = false)
    {
        _metaStore = metaStore;
        if (!deferStateCheck)
        {
            RefreshInterruptedStateFromStore();
        }
    }

    public void RefreshInterruptedStateFromStore()
    {
        lock (_lock)
        {
            if (_stateChecked)
            {
                return;
            }

            _stateChecked = true;
            if (_metaStore == null)
            {
                return;
            }

            // App 啟動 / 首次載入資料管理頁：讀 DB，若上次為 in_progress，把 WasInterrupted 標起來
            var inProg = _metaStore.GetState(KeyInProgress);
            if (inProg == "true")
            {
                WasInterrupted = true;
                Phase = _metaStore.GetState(KeyPhase) ?? "";
            }
        }
    }

    /// <summary>啟動：建立 CTS、寫 DB 旗標、fire 事件。回傳 token 給 service 用。</summary>
    public CancellationToken Start(string phase)
    {
        lock (_lock)
        {
            Cts?.Dispose();
            Cts = new CancellationTokenSource();
            IsRunning = true;
            Phase = phase;
            WasInterrupted = false;
            Current = new RebuildProgress();
            _metaStore?.SetState(KeyInProgress, "true");
            _metaStore?.SetState(KeyPhase, phase);
        }
        OnChanged();
        return Cts.Token;
    }

    public void Update(RebuildProgress p)
    {
        lock (_lock) { Current = p; }
        OnChanged();
    }

    /// <summary>結束：
    ///   cancelled=false (success) → 清 DB 旗標，下次 App 啟動不會 prompt 繼續
    ///   cancelled=true → 保留 DB 旗標（in_progress 仍 = true），下次 App 啟動仍提示繼續
    /// </summary>
    public void NotifyEnd(bool cancelled)
    {
        lock (_lock)
        {
            IsRunning = false;
            WasInterrupted = cancelled;
            if (cancelled)
            {
                // 留著旗標讓重啟也能繼續
                _metaStore?.SetState(KeyInProgress, "true");
            }
            else
            {
                // 完成 → 清旗標
                _metaStore?.SetState(KeyInProgress, null);
                _metaStore?.SetState(KeyPhase, null);
            }
        }
        OnChanged();
    }

    public void Cancel()
    {
        Cts?.Cancel();
    }

    private void OnChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

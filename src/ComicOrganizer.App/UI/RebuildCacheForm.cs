using AntdUI;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

public partial class RebuildCacheForm : Window
{
    private readonly CacheRebuildService _service;
    private readonly RebuildProgressTracker _tracker;

    public RebuildCacheForm(CacheRebuildService service, RebuildProgressTracker tracker)
    {
        InitializeComponent();
        _service = service;
        _tracker = tracker;
        windowBar.BackColor = ThemeColors.Primary;
        windowBar.ForeColor = ThemeColors.HeaderText;
        ApplyLocalization();
        WireEvents();

        _tracker.StateChanged += OnTrackerStateChanged;
        // 初始 sync 一次
        OnTrackerStateChanged(this, EventArgs.Empty);
    }

    private void ApplyLocalization()
    {
        windowBar.Text = LocalizationManager.GetString("RebuildCacheTitle") ?? "重建索引資料庫";
        Text = windowBar.Text;
        btnFullRebuild.Text = LocalizationManager.GetString("RebuildFull") ?? "完整重建";
        lblFullDesc.Text = LocalizationManager.GetString("RebuildFullDesc") ?? "清空所有 cache，從根目錄重新掃描";
        btnUpdateIndex.Text = LocalizationManager.GetString("RebuildUpdateIndex") ?? "更新索引資料庫";
        lblUpdateDesc.Text = LocalizationManager.GetString("RebuildUpdateIndexDesc") ?? "更新漫畫庫最新資料與刪除舊有資料";
        btnClearCache.Text = (LocalizationManager.GetString("RebuildClearCache") ?? "清除快取資料") + " ▾";
        lblClearDesc.Text = LocalizationManager.GetString("RebuildClearCacheDesc") ?? "選擇要清除的快取資料範圍（縮圖 / 完整）";
    }

    private void WireEvents()
    {
        btnFullRebuild.Click += async (s, e) =>
        {
            if (_tracker.IsRunning) return;   // 已在跑
            await _service.FullRebuildAsync();
        };
        btnUpdateIndex.Click += async (s, e) =>
        {
            if (_tracker.IsRunning) return;
            await ExecuteUpdateIndexAsync();
        };
        btnClearCache.Click += (s, e) =>
        {
            if (_tracker.IsRunning) return;
            ShowClearCacheMenu();
        };
        btnResume.Click += async (s, e) =>
        {
            if (_tracker.IsRunning) return;
            await _service.ResumeScanAsync();
        };
        btnCancel.Click += (s, e) => _tracker.Cancel();
        btnClose.Click += (s, e) => Close();
    }

    private void ShowClearCacheMenu()
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem("清除縮圖快取") { IconSvg = "FileImageOutlined" },
            new AntdUI.ContextMenuStripItem("清除閱讀記錄") { IconSvg = "UndoOutlined" },
            new AntdUI.ContextMenuStripItem("完整清除（含目錄索引）") { IconSvg = "DeleteOutlined" },
        };
        AntdUI.ContextMenuStrip.open(btnClearCache, mi =>
        {
            if (mi.Text == null) return;
            if (mi.Text.Contains("縮圖")) HandleClearThumbCache();
            else if (mi.Text.Contains("閱讀")) HandleClearReadingRecords();
            else if (mi.Text.Contains("完整")) HandleClearAll();
        }, items);
    }

    private void HandleClearReadingRecords()
    {
        AntdUI.Modal.open(new AntdUI.Modal.Config(this, "清除所有閱讀記錄",
            "清除全庫所有作品的閱讀狀態 / 次數 / 上次閱讀 / 上次頁碼？\n\n" +
            "此動作不可復原；metadata（作者 / 標題 / 標籤）會保留。",
            AntdUI.TType.Warn)
        {
            OkText = "清除",
            CancelText = "取消",
            OnOk = config =>
            {
                int affected = 0;
                try { affected = _service.ResetAllReadingRecords(); }
                catch { return true; }
                // Modal OnOk 在 worker thread fire；Notification 用 form 當 owner 必須回 UI thread
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    AntdUI.Notification.success(this, "完成",
                        $"已清除 {affected} 件作品的閱讀記錄", autoClose: 3, align: AntdUI.TAlignFrom.TR);
                });
                return true;
            }
        });
    }

    private void HandleClearThumbCache()
    {
        var thumbCount = _service.GetThumbCount();
        var msg =
            $"確定要清除 {thumbCount} 張縮圖嗎？\n\n" +
            $"DB 目錄索引（catalog）會保留，下次「更新索引資料庫」時會重新建立縮圖。\n\n" +
            $"此動作無法復原。";

        AntdUI.Modal.open(new AntdUI.Modal.Config(this, "清除縮圖快取", msg, AntdUI.TType.Warn)
        {
            OkText = "清除",
            CancelText = "取消",
            OnOk = config =>
            {
                _ = ExecuteClearThumbCacheAsync();
                return true;
            }
        });
    }

    private async Task ExecuteClearThumbCacheAsync()
    {
        await _service.ClearThumbCacheAsync();
        if (IsDisposed) return;
        AskRunUpdateAfterClear("縮圖快取已清除");
    }

    private void HandleClearAll()
    {
        var assetCount = _service.GetAssetCount();
        var thumbCount = _service.GetThumbCount();
        var msg =
            $"確定要完整清除嗎？\n\n" +
            $"DB 目錄索引: {assetCount} 筆\n" +
            $"縮圖: {thumbCount} 張\n\n" +
            $"清除後 DB 與 thumbs 都會回到空白狀態，需重新跑「完整重建」或「更新索引資料庫」才會有資料。\n\n" +
            $"此動作無法復原。";

        AntdUI.Modal.open(new AntdUI.Modal.Config(this, "完整清除", msg, AntdUI.TType.Warn)
        {
            OkText = "完整清除",
            CancelText = "取消",
            OnOk = config =>
            {
                _ = ExecuteClearAllAsync();
                return true;
            }
        });
    }

    private async Task ExecuteClearAllAsync()
    {
        await _service.ClearAllAsync();
        if (IsDisposed) return;
        AskRunUpdateAfterClear("已完整清除");
    }

    private void AskRunUpdateAfterClear(string clearedMsg)
    {
        AntdUI.Modal.open(new AntdUI.Modal.Config(this, "清除完成",
            $"{clearedMsg}。\n\n要立即執行「更新索引資料庫」重新建立嗎？", AntdUI.TType.Info)
        {
            OkText = "立即更新",
            CancelText = "稍後再說",
            OnOk = config =>
            {
                _ = ExecuteUpdateIndexAsync();
                return true;
            }
        });
    }

    private async Task ExecuteUpdateIndexAsync()
    {
        var summary = await _service.UpdateIndexAsync();
        if (summary != null && !IsDisposed) ShowUpdateSummary(summary);
    }

    private void OnTrackerStateChanged(object? sender, EventArgs e)
    {
        if (lblStatus.InvokeRequired)
        {
            lblStatus.BeginInvoke(new Action(() => OnTrackerStateChanged(sender, e)));
            return;
        }

        var p = _tracker.Current;
        bool running = _tracker.IsRunning;

        // 進度條 + 文字
        progressBar.Visible = running;
        progressBar.Value = (running && p.Total > 0) ? (float)p.Done / p.Total : 0f;
        if (running)
        {
            lblStatus.Text = p.Total > 0 ? $"{_tracker.Phase} ({p.Done}/{p.Total})" : _tracker.Phase;
            lblCurrent.Text = p.CurrentPath.Length > 80 ? "..." + p.CurrentPath[^77..] : p.CurrentPath;
        }
        else if (_tracker.WasInterrupted)
        {
            lblStatus.Text = LocalizationManager.GetString("RebuildCancelled") ?? "已取消";
            lblCurrent.Text = "";
        }
        else if (!string.IsNullOrEmpty(_tracker.Phase))
        {
            lblStatus.Text = LocalizationManager.GetString("RebuildDone") ?? "完成";
            Task.Factory.StartNew(() => {   
                Thread.Sleep(3000);
                if (IsDisposed) return;
                lblStatus.BeginInvoke(new Action(() => lblStatus.Text = ""));
            });
            lblCurrent.Text = "";
        }
        else
        {
            lblStatus.Text = "";
            lblCurrent.Text = "";
        }

        // 按鈕狀態
        btnFullRebuild.Enabled = !running;
        btnUpdateIndex.Enabled = !running;
        btnClearCache.Enabled = !running;
        btnResume.Enabled = !running && _tracker.WasInterrupted;
        btnCancel.Visible = running;
        btnClose.Enabled = true;                          // 永遠可關（任務背景跑）
        
    }

    private void ShowUpdateSummary(CacheRebuildService.UpdateIndexSummary s)
    {
        // 計算 cache 用量百分比；上限 0 = 不限制
        string cacheUsage;
        if (s.CacheLimitBytes > 0)
        {
            double pct = s.CacheUsedBytes * 100.0 / s.CacheLimitBytes;
            cacheUsage = $"{FormatBytes(s.CacheUsedBytes)} / {FormatBytes(s.CacheLimitBytes)} ({pct:F1}%)";
        }
        else
        {
            cacheUsage = $"{FormatBytes(s.CacheUsedBytes)} (無上限)";
        }

        var content =
            $"新增  : {s.AddedCount}\n" +
            $"更新  : {s.UpdatedCount}\n" +
            $"刪除  : {s.RemovedCount}\n" +
            $"淘汰  : {s.EvictedCount}\n" +
            $"\nCache 用量: {cacheUsage}";

        if (s.EvictedCount > 0)
        {
            content += $"\n\n注意：因 cache 已達上限，{s.EvictedCount} 個最舊的縮圖被淘汰；DB catalog 仍完整保留，使用者點開時會重新建立。";
        }

        var title = s.Cancelled ? "更新已取消（部分結果）" : "更新完成";
        var icon = s.Cancelled ? AntdUI.TType.Warn
                  : s.EvictedCount > 0 ? AntdUI.TType.Warn
                  : AntdUI.TType.Success;
        AntdUI.Modal.open(new AntdUI.Modal.Config(this, title, content, icon)
        {
            CancelText = null,   // 純資訊框，隱藏取消鈕
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 關 form 不取消任務 — 任務繼續背景跑、tracker 保持狀態，
        // 重新開啟時恢復顯示
        _tracker.StateChanged -= OnTrackerStateChanged;
        base.OnFormClosing(e);
    }
}

using AntdUI;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

public partial class RebuildCacheForm : Window
{
    private readonly CacheRebuildService _service;
    private readonly RebuildProgressTracker _tracker;
    private readonly AntdUI.FolderBrowserDialog _folderDlg = new();

    public RebuildCacheForm(CacheRebuildService service, RebuildProgressTracker tracker)
    {
        InitializeComponent();
        _service = service;
        _tracker = tracker;
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
        btnPartialRebuild.Text = LocalizationManager.GetString("RebuildPartial") ?? "部分重建...";
        lblPartialDesc.Text = LocalizationManager.GetString("RebuildPartialDesc") ?? "選擇子目錄重新掃描";
    }

    private void WireEvents()
    {
        btnFullRebuild.Click += async (s, e) =>
        {
            if (_tracker.IsRunning) return;   // 已在跑
            await _service.FullRebuildAsync();
        };
        btnPartialRebuild.Click += async (s, e) =>
        {
            if (_tracker.IsRunning) return;
            if (_folderDlg.ShowDialog() != DialogResult.OK) return;
            var sel = _folderDlg.DirectoryPath;
            if (string.IsNullOrEmpty(sel)) return;
            await _service.PartialRebuildAsync(sel);
        };
        btnResume.Click += async (s, e) =>
        {
            if (_tracker.IsRunning) return;
            await _service.ResumeScanAsync();
        };
        btnCancel.Click += (s, e) => _tracker.Cancel();
        btnClose.Click += (s, e) => Close();
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
            lblCurrent.Text = "";
        }
        else
        {
            lblStatus.Text = "";
            lblCurrent.Text = "";
        }

        // 按鈕狀態
        btnFullRebuild.Enabled = !running;
        btnPartialRebuild.Enabled = !running;
        btnResume.Enabled = !running && _tracker.WasInterrupted;
        btnResume.Visible = _tracker.WasInterrupted;     // 只有有未完成才顯示
        btnCancel.Visible = running;
        btnClose.Enabled = true;                          // 永遠可關（任務背景跑）
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 關 form 不取消任務 — 任務繼續背景跑、tracker 保持狀態，
        // 重新開啟時恢復顯示
        _tracker.StateChanged -= OnTrackerStateChanged;
        base.OnFormClosing(e);
    }
}

using AntdUI;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

public partial class RebuildCacheForm : Window
{
    private readonly CacheRebuildService _service;
    private readonly AntdUI.FolderBrowserDialog _folderDlg = new();
    private CancellationTokenSource? _cts;

    public RebuildCacheForm(CacheRebuildService service)
    {
        InitializeComponent();
        _service = service;
        ApplyLocalization();
        WireEvents();
    }

    private void ApplyLocalization()
    {
        windowBar.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildCacheTitle") ?? "重建索引資料庫";
        Text = windowBar.Text;
        btnFullRebuild.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildFull") ?? "完整重建";
        lblFullDesc.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildFullDesc") ?? "清空所有 cache，從根目錄重新掃描";
        btnPartialRebuild.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildPartial") ?? "部分重建...";
        lblPartialDesc.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildPartialDesc") ?? "選擇子目錄重新掃描";
        lblStatus.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildIdle") ?? "等待中";
    }

    private void WireEvents()
    {
        btnFullRebuild.Click += async (s, e) => await DoRebuild(isFull: true, subtree: null);
        btnPartialRebuild.Click += async (s, e) =>
        {
            if (_folderDlg.ShowDialog() != DialogResult.OK) return;
            var sel = _folderDlg.DirectoryPath;
            if (string.IsNullOrEmpty(sel)) return;
            await DoRebuild(isFull: false, subtree: sel);
        };
        btnCancel.Click += (s, e) => _cts?.Cancel();
        btnClose.Click += (s, e) => Close();
    }

    private async Task DoRebuild(bool isFull, string? subtree)
    {
        SetRunning(true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<CacheRebuildService.Progress>(p =>
        {
            // Progress 已自動 marshal 到 UI thread (Progress<T> SynchronizationContext)
            progressBar.Value = p.Total > 0 ? (float)p.Done / p.Total : 0f;
            lblStatus.Text = $"{p.Done} / {p.Total}";
            lblCurrent.Text = p.CurrentPath.Length > 60 ? "..." + p.CurrentPath[^57..] : p.CurrentPath;
        });

        try
        {
            if (isFull) await _service.FullRebuildAsync(progress, _cts.Token);
            else if (subtree != null) await _service.PartialRebuildAsync(subtree, progress, _cts.Token);
            lblStatus.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildDone") ?? "完成";
            lblCurrent.Text = "";
        }
        catch (OperationCanceledException)
        {
            lblStatus.Text = ComicOrganizer.App.Core.LocalizationManager.GetString("RebuildCancelled") ?? "已取消";
        }
        catch (Exception ex)
        {
            lblStatus.Text = "失敗: " + ex.Message;
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        btnFullRebuild.Enabled = !running;
        btnPartialRebuild.Enabled = !running;
        btnCancel.Visible = running;
        btnClose.Enabled = !running;
        if (!running) progressBar.Value = 0f;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}

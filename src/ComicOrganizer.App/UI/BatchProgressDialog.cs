using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 批次拉取進度 / 結果 dialog（#52 Phase F）。
///
/// 兩階段 UI：
///   進行中：進度條 + 當前作品名 + cancel button
///   完成後：統計 + 列表（每本一行 status + reason）+ close button
///
/// 用法：
///   var dlg = new BatchProgressDialog(totalEligible);
///   dlg.Show(parent);   // non-modal — 主要事件迴圈不阻塞，async 跑完才 dlg.MarkComplete
///   await foreach (var res in service.ProcessAll(...)) dlg.AppendResult(res);
///   dlg.MarkComplete();   // 切到結果模式
///   user 自己關閉 dialog
/// </summary>
public class BatchProgressDialog : AntdUI.Window, IThemedControl
{
    private readonly AntdUI.Label lblHeader;
    private readonly AntdUI.Progress progress;
    private readonly AntdUI.Label lblCurrent;
    private readonly AntdUI.Label lblStats;
    private readonly System.Windows.Forms.ListBox listResults;
    private readonly AntdUI.Button btnCancel;
    private readonly AntdUI.Button btnClose;

    private readonly int _total;
    private int _processed;
    private int _applied;
    private int _skipped;
    private int _notFound;
    private int _errors;
    private bool _completed;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>批次取消 token — caller 用此停止 enumerate。</summary>
    public CancellationToken CancellationToken => _cts.Token;
    public bool IsCancelled => _cts.IsCancellationRequested;

    public BatchProgressDialog(int total)
    {
        _total = total;
        Text = LocalizationManager.GetString("BatchSyncDialogTitle") ?? "批次同步作品";
        Size = new Size(560, 480);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 20, 18),
        };
        Controls.Add(content);

        lblHeader = new AntdUI.Label
        {
            Text = string.Format(LocalizationManager.GetString("BatchSyncProgressFmt") ?? "處理中 {0} / {1}", 0, total),
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font("Microsoft JhengHei UI", 10.5F, FontStyle.Bold),
        };

        progress = new AntdUI.Progress
        {
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 8, 0, 0),
            Value = 0f,    // 0~1 ratio
        };

        lblCurrent = new AntdUI.Label
        {
            Text = "",
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 8, 0, 0),
        };

        lblStats = new AntdUI.Label
        {
            Text = "",
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 4, 0, 0),
        };

        listResults = new System.Windows.Forms.ListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
        };

        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
        };

        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 80, Height = 32,
        };
        btnCancel.Click += (s, e) => RequestCancel();

        btnClose = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogClose") ?? "關閉",
            Type = AntdUI.TTypeMini.Primary,
            Width = 80, Height = 32,
            Visible = false,
        };
        btnClose.Click += (s, e) => Close();

        btnRow.Controls.Add(btnClose);
        btnRow.Controls.Add(btnCancel);

        content.Controls.Add(listResults);
        content.Controls.Add(lblStats);
        content.Controls.Add(lblCurrent);
        content.Controls.Add(progress);
        content.Controls.Add(lblHeader);
        content.Controls.Add(btnRow);

        this.WireThemeAndLocalization();
    }

    private void RequestCancel()
    {
        if (_completed) return;
        _cts.Cancel();
        btnCancel.Enabled = false;
        lblCurrent.Text = LocalizationManager.GetString("BatchSyncCancelling") ?? "取消中…";
    }

    /// <summary>更新「目前處理到」的作品名 — caller 在每本 fetch 前呼叫。</summary>
    public void SetCurrent(string fileName)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetCurrent(fileName)); return; }
        lblCurrent.Text = string.Format(
            LocalizationManager.GetString("BatchSyncCurrentFmt") ?? "正在處理：{0}",
            fileName);
    }

    /// <summary>追加單本處理結果到 list + 更新統計。</summary>
    public void AppendResult(BatchItemResult r)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => AppendResult(r)); return; }

        _processed++;
        switch (r.Status)
        {
            case BatchItemStatus.Applied: _applied++; break;
            case BatchItemStatus.Skipped: _skipped++; break;
            case BatchItemStatus.NotFound: _notFound++; break;
            case BatchItemStatus.Error: _errors++; break;
        }

        progress.Value = _total > 0 ? (float)_processed / _total : 1f;
        lblHeader.Text = string.Format(
            LocalizationManager.GetString("BatchSyncProgressFmt") ?? "處理中 {0} / {1}",
            _processed, _total);
        lblStats.Text = string.Format(
            LocalizationManager.GetString("BatchSyncStatsFmt") ?? "套用 {0} / 跳過 {1} / 找不到 {2} / 失敗 {3}",
            _applied, _skipped, _notFound, _errors);

        var icon = r.Status switch
        {
            BatchItemStatus.Applied => "✓",
            BatchItemStatus.Skipped => "−",
            BatchItemStatus.NotFound => "?",
            BatchItemStatus.Error => "✗",
            _ => " ",
        };
        var name = Path.GetFileName(r.Path);
        var msg = r.Message != null ? $" — {r.Message}" : "";
        listResults.Items.Add($"{icon} {name}{msg}");
    }

    /// <summary>切到完成模式：cancel 變 close，header 顯示完成。</summary>
    public void MarkComplete()
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(MarkComplete); return; }

        _completed = true;
        btnCancel.Visible = false;
        btnClose.Visible = true;
        lblCurrent.Text = "";
        lblHeader.Text = _cts.IsCancellationRequested
            ? (LocalizationManager.GetString("BatchSyncCancelled") ?? "已取消")
            : (LocalizationManager.GetString("BatchSyncDone") ?? "完成");
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
    }

    public void ApplyLocalization()
    {
        // labels are formatted at runtime via SetCurrent / AppendResult
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
        btnClose.Text = LocalizationManager.GetString("DialogClose") ?? "關閉";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}

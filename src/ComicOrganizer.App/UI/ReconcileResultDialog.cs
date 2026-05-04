using AntdUI;
using ComicOrganizer.App.Core;
using System.ComponentModel;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// CatalogReconcileService.Analyze 結果展示。
/// 三個 list：路徑變更 / 孤兒 / 歧義。使用者勾選要套用的項目。
/// </summary>
public class ReconcileResultDialog : AntdUI.Window, IThemedControl
{
    private readonly ReconcileReport _report;
    private readonly CatalogReconcileService _service;

    private readonly AntdUI.Tabs tabs;
    private readonly Table tablePathChanges;
    private readonly Table tableOrphans;
    private readonly Table tableAmbiguous;
    private readonly AntdUI.Label lblSummary;
    private readonly AntdUI.Button btnApplyPathChanges;
    private readonly AntdUI.Button btnDeleteOrphans;
    private readonly AntdUI.Button btnClose;

    public ReconcileResultDialog(ReconcileReport report, CatalogReconcileService service)
    {
        _report = report;
        _service = service;

        Text = LocalizationManager.GetString("ReconcileResultTitle") ?? "比對結果";
        Size = new System.Drawing.Size(900, 600);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
        };
        Controls.Add(content);

        // 摘要
        lblSummary = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = string.Format(
                LocalizationManager.GetString("ReconcileSummaryFmt") ??
                    "FS 掃描 {0} 件 / DB 索引 {1} 件 → 路徑變更 {2} / 孤兒 {3} / 歧義 {4}",
                report.FsWorksScanned, report.DbAssetsScanned,
                report.PathChanges.Count, report.Orphans.Count, report.Ambiguous.Count),
        };

        // 三個 tab
        tabs = new AntdUI.Tabs
        {
            Dock = DockStyle.Fill,
        };

        tablePathChanges = MakeTable();
        tableOrphans = MakeTable();
        tableAmbiguous = MakeTable();

        ConfigurePathChangesTable();
        ConfigureOrphansTable();
        ConfigureAmbiguousTable();

        var tabPathChanges = new AntdUI.TabPage
        {
            Text = string.Format(
                LocalizationManager.GetString("ReconcileTabPathChangesFmt") ?? "路徑變更 ({0})",
                report.PathChanges.Count),
        };
        tabPathChanges.Controls.Add(tablePathChanges);

        var tabOrphans = new AntdUI.TabPage
        {
            Text = string.Format(
                LocalizationManager.GetString("ReconcileTabOrphansFmt") ?? "孤兒索引 ({0})",
                report.Orphans.Count),
        };
        tabOrphans.Controls.Add(tableOrphans);

        var tabAmbiguous = new AntdUI.TabPage
        {
            Text = string.Format(
                LocalizationManager.GetString("ReconcileTabAmbiguousFmt") ?? "歧義 ({0})",
                report.Ambiguous.Count),
        };
        tabAmbiguous.Controls.Add(tableAmbiguous);

        tabs.Pages.Add(tabPathChanges);
        tabs.Pages.Add(tabOrphans);
        tabs.Pages.Add(tabAmbiguous);

        // 動作 row
        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 50,
            Padding = new Padding(0, 8, 0, 0),
        };
        btnClose = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogClose") ?? "關閉",
            Width = 80, Height = 32,
            Margin = new Padding(8, 0, 0, 0),
        };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

        btnDeleteOrphans = new AntdUI.Button
        {
            Text = string.Format(
                LocalizationManager.GetString("ReconcileDeleteOrphansFmt") ?? "刪除選取的孤兒 ({0})",
                report.Orphans.Count),
            Type = TTypeMini.Error,
            Width = 200, Height = 32,
            Margin = new Padding(8, 0, 0, 0),
            Enabled = report.Orphans.Count > 0,
        };
        btnDeleteOrphans.Click += (s, e) => OnDeleteOrphans();

        btnApplyPathChanges = new AntdUI.Button
        {
            Text = string.Format(
                LocalizationManager.GetString("ReconcileApplyPathChangesFmt") ?? "套用選取的路徑變更 ({0})",
                report.PathChanges.Count),
            Type = TTypeMini.Primary,
            Width = 220, Height = 32,
            Margin = new Padding(8, 0, 0, 0),
            Enabled = report.PathChanges.Count > 0,
        };
        btnApplyPathChanges.Click += (s, e) => OnApplyPathChanges();

        btnRow.Controls.Add(btnClose);
        btnRow.Controls.Add(btnDeleteOrphans);
        btnRow.Controls.Add(btnApplyPathChanges);

        content.Controls.Add(tabs);
        content.Controls.Add(lblSummary);
        content.Controls.Add(btnRow);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        this.WireThemeAndLocalization();
    }

    private static Table MakeTable() => new Table
    {
        Dock = DockStyle.Fill,
        AutoSizeColumnsMode = ColumnsMode.Fill,
        EnableHeaderResizing = true,
        RowHeight = 36,
    };

    private void ConfigurePathChangesTable()
    {
        tablePathChanges.Columns = new ColumnCollection
        {
            new ColumnCheck("Selected", "")            { Width = "5%" },
            new Column("OldPath",  LocalizationManager.GetString("ReconcileColOld")  ?? "舊路徑")  { Width = "44%" },
            new Column("NewPath",  LocalizationManager.GetString("ReconcileColNew")  ?? "新路徑")  { Width = "44%" },
            new Column("SizeText", LocalizationManager.GetString("ReconcileColSize") ?? "大小")    { Width = "7%" },
        };
        var rows = new BindingList<PathChangeRow>(
            _report.PathChanges.Select(c => new PathChangeRow
            {
                Selected = true,    // 預設全選
                AssetId = c.AssetId,
                OldPath = c.OldPath,
                NewPath = c.NewPath,
                SizeText = FormatSize(c.Size),
            }).ToList());
        tablePathChanges.DataSource = rows;
    }

    private void ConfigureOrphansTable()
    {
        tableOrphans.Columns = new ColumnCollection
        {
            new ColumnCheck("Selected", "")            { Width = "5%" },
            new Column("Path",     LocalizationManager.GetString("ReconcileColPath") ?? "路徑")    { Width = "88%" },
            new Column("SizeText", LocalizationManager.GetString("ReconcileColSize") ?? "大小")    { Width = "7%" },
        };
        var rows = new BindingList<OrphanRow>(
            _report.Orphans.Select(o => new OrphanRow
            {
                Selected = false,   // 刪除預設不選 — 使用者主動勾選
                AssetId = o.AssetId,
                Path = o.Path,
                SizeText = FormatSize(o.Size ?? 0),
            }).ToList());
        tableOrphans.DataSource = rows;
    }

    private void ConfigureAmbiguousTable()
    {
        tableAmbiguous.Columns = new ColumnCollection
        {
            new Column("OldPath",        LocalizationManager.GetString("ReconcileColOld")        ?? "舊路徑")    { Width = "40%" },
            new Column("CandidatesText", LocalizationManager.GetString("ReconcileColCandidates") ?? "候選新位置") { Width = "53%" },
            new Column("SizeText",       LocalizationManager.GetString("ReconcileColSize")       ?? "大小")      { Width = "7%" },
        };
        var rows = new BindingList<AmbiguousRow>(
            _report.Ambiguous.Select(a => new AmbiguousRow
            {
                AssetId = a.AssetId,
                OldPath = a.OldPath,
                CandidatesText = string.Join("\n", a.CandidateFsPaths),
                SizeText = FormatSize(a.Size),
            }).ToList());
        tableAmbiguous.DataSource = rows;
    }

    // ── 動作 ─────────────────────────────────────────────────────────────

    private void OnApplyPathChanges()
    {
        if (tablePathChanges.DataSource is not BindingList<PathChangeRow> rows) return;
        var selected = rows.Where(r => r.Selected).ToList();
        if (selected.Count == 0) return;

        AntdUI.Modal.open(new AntdUI.Modal.Config(this,
            LocalizationManager.GetString("ReconcileApplyConfirmTitle") ?? "確認套用",
            string.Format(
                LocalizationManager.GetString("ReconcileApplyConfirmMsgFmt") ??
                    "更新 {0} 筆 asset 的 path（保留 metadata / 閱讀記錄 / 標籤）。\n此動作不可復原。",
                selected.Count),
            AntdUI.TType.Info)
        {
            OkText = LocalizationManager.GetString("DialogConfirm") ?? "確認",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ =>
            {
                int n = _service.ApplyPathChanges(
                    selected.Select(r => new PathChange(r.AssetId, r.OldPath, r.NewPath, 0)));
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    AntdUI.Notification.success(this,
                        LocalizationManager.GetString("ReconcileDoneTitle") ?? "完成",
                        string.Format(LocalizationManager.GetString("ReconcileApplyDoneFmt") ?? "{0} 筆路徑已更新", n),
                        autoClose: 3, align: AntdUI.TAlignFrom.TR);
                    // 移除已套用的 row
                    foreach (var r in selected) rows.Remove(r);
                    btnApplyPathChanges.Enabled = rows.Count > 0;
                    btnApplyPathChanges.Text = string.Format(
                        LocalizationManager.GetString("ReconcileApplyPathChangesFmt") ?? "套用選取的路徑變更 ({0})",
                        rows.Count);
                }));
                return true;
            }
        });
    }

    private void OnDeleteOrphans()
    {
        if (tableOrphans.DataSource is not BindingList<OrphanRow> rows) return;
        var selected = rows.Where(r => r.Selected).ToList();
        if (selected.Count == 0) return;

        AntdUI.Modal.open(new AntdUI.Modal.Config(this,
            LocalizationManager.GetString("ReconcileDeleteConfirmTitle") ?? "確認刪除",
            string.Format(
                LocalizationManager.GetString("ReconcileDeleteConfirmMsgFmt") ??
                    "從索引刪除 {0} 筆孤兒 asset 紀錄（連同 metadata / 縮圖 / 標籤）。\n此動作不可復原。",
                selected.Count),
            AntdUI.TType.Warn)
        {
            OkText = LocalizationManager.GetString("DialogDelete") ?? "刪除",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ =>
            {
                int n = _service.DeleteOrphans(selected.Select(r => r.AssetId));
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    AntdUI.Notification.success(this,
                        LocalizationManager.GetString("ReconcileDoneTitle") ?? "完成",
                        string.Format(LocalizationManager.GetString("ReconcileDeleteDoneFmt") ?? "{0} 筆孤兒已刪除", n),
                        autoClose: 3, align: AntdUI.TAlignFrom.TR);
                    foreach (var r in selected) rows.Remove(r);
                    btnDeleteOrphans.Enabled = rows.Count > 0;
                    btnDeleteOrphans.Text = string.Format(
                        LocalizationManager.GetString("ReconcileDeleteOrphansFmt") ?? "刪除選取的孤兒 ({0})",
                        rows.Count);
                }));
                return true;
            }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblSummary.ForeColor = ThemeColors.Text;
    }

    public void ApplyLocalization() { /* 動態欄位 / 摘要建構時取一次，不重新整理 */ }

    // ── DTO ──────────────────────────────────────────────────────────────

    public class PathChangeRow
    {
        public bool Selected { get; set; }
        public long AssetId { get; set; }
        public string OldPath { get; set; } = "";
        public string NewPath { get; set; } = "";
        public string SizeText { get; set; } = "";
    }
    public class OrphanRow
    {
        public bool Selected { get; set; }
        public long AssetId { get; set; }
        public string Path { get; set; } = "";
        public string SizeText { get; set; } = "";
    }
    public class AmbiguousRow
    {
        public long AssetId { get; set; }
        public string OldPath { get; set; } = "";
        public string CandidatesText { get; set; } = "";
        public string SizeText { get; set; } = "";
    }
}

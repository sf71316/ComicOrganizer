using AntdUI;
using ComicOrganizer.App.Core;
using System.ComponentModel;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 作者作品清單 — Phase I：在「詮釋資料管理 / 作者」master-detail 右側顯示。
/// 使用者選擇作者 → 載入該作者所有作品 → 多選 → 觸發批次 metadata 同步。
///
/// Status 標示：可同步 / 已綁 / 已編輯 / 無 id。
/// 預設只勾「可同步」（btnSelectEligible 預設 click），user 可手動覆寫。
/// 「無 id」的作品仍可勾，但 BatchExternalFetchService.ProcessOneAsync 會 skip。
/// </summary>
public partial class AuthorWorksListPanel : UserControl, IThemedControl
{
    private IAssetMetaStore? _store;
    private List<AuthorWorkRow> _all = new();
    private long _currentAuthorId;

    /// <summary>使用者按「同步選取作品」 — host 接此事件啟動批次（共用 Phase F 流程）。</summary>
    public event Action<long, IReadOnlyList<string>>? RequestBatchSync;

    /// <summary>使用者點 row 上的同步 icon — host 接此事件對單本跑 Phase E 單本拉取流程。</summary>
    public event Action<string>? RequestSyncOne;

    /// <summary>由 host 設定 — 控制按鈕是否啟用（依 config.EnableExternalMetadata）。</summary>
    public bool ExternalMetadataEnabled { get; set; }

    public AuthorWorksListPanel()
    {
        InitializeComponent();
        ConfigureTable();
        WireEvents();
        this.WireThemeAndLocalization();
    }

    public void Initialize(IAssetMetaStore store) => _store = store;

    /// <summary>由 AuthorManagePanel 在使用者點作者 row 時呼叫。authorId=0 → 清空顯示。</summary>
    public void LoadAuthorWorks(long authorId, string authorName)
    {
        _currentAuthorId = authorId;
        if (_store == null || authorId <= 0)
        {
            _all.Clear();
            tableWorks.DataSource = new BindingList<AuthorWorkRow>();
            lblHeader.Text = LocalizationManager.GetString("AuthorWorksHeaderEmpty") ?? "選擇作者以檢視作品";
            UpdateStatus();
            btnSyncSelected.Enabled = false;
            return;
        }

        var summaries = _store.ListAuthorWorkSummaries(authorId);
        _all = summaries.Select(BuildRow).ToList();

        lblHeader.Text = string.Format(
            LocalizationManager.GetString("AuthorWorksHeaderFmt") ?? "「{0}」的作品（共 {1} 本）",
            authorName, _all.Count);

        // 預設勾「可同步」
        foreach (var r in _all) r.Selected = r.Status == "可同步";
        ApplyFilter();
        UpdateStatus();
    }

    // ── 內部 ──────────────────────────────────────────────────────

    private void ConfigureTable()
    {
        tableWorks.Columns = new ColumnCollection
        {
            new ColumnCheck("Selected", "")                                                     { Width = "5%" },
            new Column("Title",       LocalizationManager.GetString("WorksColTitle")    ?? "標題")    { Width = "32%" },
            new Column("Language",    LocalizationManager.GetString("WorksColLanguage") ?? "語言")    { Width = "7%" },
            new Column("PageCount",   LocalizationManager.GetString("WorksColPages")    ?? "頁數")    { Width = "6%", Align = ColumnAlign.Right },
            new Column("TagCount",    LocalizationManager.GetString("WorksColTags")     ?? "標籤")    { Width = "6%", Align = ColumnAlign.Right },
            new Column("ExternalId",  LocalizationManager.GetString("WorksColExtId")    ?? "external_id")    { Width = "10%" },
            new Column("ManualMark",  LocalizationManager.GetString("WorksColManual")   ?? "手動")    { Width = "5%" },
            new Column("Status",      LocalizationManager.GetString("WorksColStatus")   ?? "狀態")    { Width = "11%" },
            new Column("Action",      LocalizationManager.GetString("TagColAction")     ?? "操作")    { Width = "10%" },
        };
    }

    private void WireEvents()
    {
        txtSearch.TextChanged += (s, e) => ApplyFilter();
        btnSelectAll.Click += (s, e) => SetAllSelected(true);
        btnDeselectAll.Click += (s, e) => SetAllSelected(false);
        btnSelectEligible.Click += (s, e) =>
        {
            foreach (var r in _all) r.Selected = r.Status == "可同步";
            ApplyFilter();
            UpdateStatus();
        };
        btnSyncSelected.Click += (s, e) =>
        {
            if (_currentAuthorId <= 0) return;
            var paths = _all.Where(r => r.Selected).Select(r => r.Path).ToList();
            if (paths.Count == 0) return;
            RequestBatchSync?.Invoke(_currentAuthorId, paths);
        };

        // Per-row sync icon click → fire event給 host 單本跑流程
        tableWorks.CellButtonClick += (s, e) =>
        {
            if (e.Record is AuthorWorkRow row && e.Btn.Id == "Sync")
                RequestSyncOne?.Invoke(row.Path);
        };
        // 無內建 row check 變更事件 — 用 timer/poll 太醜。改 ColumnCheck 改變後 BindingList 自動更新 _all 中對應物件
        // 因為 BindingList 包的就是 _all，所以 row.Selected 直接寫到我們的 list。每次 paint 後我們用 timer 更新 status。
        // 簡化：使用者按按鈕後才更新狀態。其他變更靠 toolbar 按鈕觸發 UpdateStatus。
        // 為了即時更新，掛 BindingListChanged 事件
    }

    private void SetAllSelected(bool sel)
    {
        var visible = Filtered();
        foreach (var r in visible) r.Selected = sel;
        ApplyFilter();
        UpdateStatus();
    }

    private string SearchTerm => txtSearch.Text?.Trim() ?? "";

    private List<AuthorWorkRow> Filtered()
    {
        if (string.IsNullOrEmpty(SearchTerm)) return _all;
        return _all.Where(r => r.Title.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ApplyFilter()
    {
        var visible = Filtered();
        var bl = new BindingList<AuthorWorkRow>(visible);
        bl.ListChanged += (s, e) =>
        {
            // 主要捕捉 ColumnCheck 改變，呼叫 UpdateStatus 同步按鈕跟 lblStatus
            if (e.ListChangedType == ListChangedType.ItemChanged) UpdateStatus();
        };
        tableWorks.DataSource = bl;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var visible = Filtered();
        var selected = visible.Count(r => r.Selected);
        var total = visible.Count;
        var eligible = visible.Count(r => r.Status == "可同步");
        lblStatus.Text = string.Format(
            LocalizationManager.GetString("AuthorWorksStatusFmt") ?? "顯示 {0} / 已選 {1} / 可同步 {2}",
            total, selected, eligible);
        btnSyncSelected.Enabled = ExternalMetadataEnabled && selected > 0;
    }

    private static AuthorWorkRow BuildRow(AuthorWorkSummary summary)
    {
        string status;
        if (summary.ManuallyEdited) status = "已編輯";
        else if (!string.IsNullOrEmpty(summary.ExternalId)) status = "已綁";
        else status = "可同步";

        var actions = new CellLink[]
        {
            new CellButton("Sync", "", TTypeMini.Default) { Ghost = true, IconSvg = "ApiOutlined" },
        };

        return new AuthorWorkRow
        {
            Path = summary.Path,
            Title = !string.IsNullOrWhiteSpace(summary.Title) ? summary.Title : Path.GetFileNameWithoutExtension(summary.Path),
            Language = summary.Language ?? "",
            PageCount = summary.PageCount?.ToString() ?? "",
            TagCount = summary.TagCount.ToString(),
            ExternalId = summary.ExternalId ?? "",
            ManualMark = summary.ManuallyEdited ? "✓" : "",
            Status = status,
            Selected = false,
            Action = actions,
        };
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        toolbarPanel.BackColor = ThemeColors.Background;
        tableWorks.BackColor = ThemeColors.Surface;
    }

    public void ApplyLocalization()
    {
        btnSelectAll.Text = LocalizationManager.GetString("BtnSelectAll") ?? "全選";
        btnSelectEligible.Text = LocalizationManager.GetString("BtnSelectEligible") ?? "選可同步";
        btnDeselectAll.Text = LocalizationManager.GetString("BtnDeselectAll") ?? "清空";
        btnSyncSelected.Text = LocalizationManager.GetString("BtnSyncSelected") ?? "同步選取作品";
        txtSearch.PlaceholderText = LocalizationManager.GetString("WorksSearchPlaceholder") ?? "搜尋作品標題...";
    }

    /// <summary>實作 INotifyPropertyChanged 才能讓 BindingList.ListChanged 在 row.Selected 改變
    /// 時 fire ItemChanged → UpdateStatus 重算「同步選取作品」按鈕 enable 狀態。</summary>
    public class AuthorWorkRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _selected;
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string Language { get; set; } = "";
        public string PageCount { get; set; } = "";
        public string TagCount { get; set; } = "";
        public string ExternalId { get; set; } = "";
        public string ManualMark { get; set; } = "";
        public string Status { get; set; } = "";
        public CellLink[]? Action { get; set; }
    }
}

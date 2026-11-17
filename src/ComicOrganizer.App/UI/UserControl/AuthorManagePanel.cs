using AntdUI;
using ComicOrganizer.App.Core;
using System.ComponentModel;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

public partial class AuthorManagePanel : UserControl, IThemedControl
{
    private const string FilterAll = "__all__";
    private const string FilterPhysical = "__physical__";    // 有 folder_path
    private const string FilterMetadataOnly = "__meta__";    // 沒 folder_path

    private IAssetMetaStore? _metaStore;
    private TagTranslator? _translator;
    private List<AuthorUsage> _allAuthors = new();
    private string _searchText = "";
    private string _filter = FilterAll;
    private readonly List<string> _filterTokens = new();

    public AuthorManagePanel()
    {
        InitializeComponent();
        ConfigureTable();
        BuildFilterDropdown();
        WireEvents();
        this.WireThemeAndLocalization();

        // SplitContainer 在 Designer 時 Size 還是預設 150x100，SplitterDistance=420 會被 clamp。
        // 等 control 拿到第一次 Size（HandleCreated 後 Layout pass）才能可靠設值。
        Layout += (s, e) =>
        {
            // 只設一次 — 之後尊重 user 拖拉
            if (_splitterInitialized) return;
            if (splitContainer.Width < 200) return;   // 還沒撐開
            splitContainer.SplitterDistance = Math.Min(600, splitContainer.Width - 200);
            _splitterInitialized = true;
        };
    }
    private bool _splitterInitialized;

    /// <summary>使用者按右側「同步選取作品」按鈕 — host 接此事件啟動批次（同 Phase F 流程，
    /// 但作品集合是 user 在右側勾選的子集，不是整個作者所有作品）。</summary>
    public event Action<long, IReadOnlyList<string>>? RequestBatchSync;

    /// <summary>使用者點 row 上的 sync icon — 對單本跑 Phase E 流程。</summary>
    public event Action<string>? RequestSyncOne;

    /// <summary>使用者點擊列上「在畫廊檢視」按鈕時觸發（傳入 artist:... DSL）。</summary>
    public event Action<string>? RequestViewInGallery;

    public void Initialize(IAssetMetaStore metaStore, TagTranslator? translator = null)
    {
        _metaStore = metaStore;
        _translator = translator;
        worksPanel.Initialize(metaStore);
        worksPanel.RequestBatchSync += (authorId, paths) => RequestBatchSync?.Invoke(authorId, paths);
        worksPanel.RequestSyncOne += path => RequestSyncOne?.Invoke(path);
    }

    /// <summary>由 host 設定 — 控制右側「同步選取作品」按鈕是否啟用（依 config.EnableExternalMetadata）。</summary>
    public bool ExternalMetadataEnabled
    {
        get => worksPanel.ExternalMetadataEnabled;
        set => worksPanel.ExternalMetadataEnabled = value;
    }

    public new void Refresh()
    {
        if (_metaStore == null) return;
        _allAuthors = _metaStore.ListAuthorsByUsage().ToList();
        ApplyFilter();
    }

    // ── 內部 ──────────────────────────────────────────────────────────

    private void ConfigureTable()
    {
        tableAuthors.AutoSizeColumnsMode = ColumnsMode.Fill;
        tableAuthors.EmptyText = "（無資料）";
        tableAuthors.Columns = new ColumnCollection
        {
            new Column("MainName",   LocalizationManager.GetString("AuthorColMainName")  ?? "主名")    { Width = "20%", SortOrder = true },
            new Column("FolderPath", LocalizationManager.GetString("AuthorColFolder")    ?? "實體資料夾"){ Width = "17%" },
            new Column("AliasCount", LocalizationManager.GetString("AuthorColAliases")   ?? "別名數")  { Width = "8%",  SortOrder = true, Align = ColumnAlign.Right },
            new Column("TagCount",   LocalizationManager.GetString("AuthorColTags")      ?? "標籤數")  { Width = "8%",  SortOrder = true, Align = ColumnAlign.Right },
            new Column("WorkCount",  LocalizationManager.GetString("AuthorColWorks")     ?? "作品數")  { Width = "9%",  SortOrder = true, Align = ColumnAlign.Right },
            new Column("LearnedAt",  LocalizationManager.GetString("AuthorColLearnedAt") ?? "建立時間"){ Width = "20%", SortOrder = true },
            new Column("Action",     LocalizationManager.GetString("TagColAction")       ?? "操作")    { Width = "18%" },
        };
    }

    private void BuildFilterDropdown()
    {
        _filterTokens.Clear();
        selectFilter.Items.Clear();

        _filterTokens.Add(FilterAll);
        selectFilter.Items.Add(LocalizationManager.GetString("AuthorFilterAll") ?? "全部作者");

        _filterTokens.Add(FilterPhysical);
        selectFilter.Items.Add(LocalizationManager.GetString("AuthorFilterPhysical") ?? "有實體資料夾");

        _filterTokens.Add(FilterMetadataOnly);
        selectFilter.Items.Add(LocalizationManager.GetString("AuthorFilterMetaOnly") ?? "僅 metadata");

        selectFilter.SelectedIndex = 0;
    }

    private void WireEvents()
    {
        btnRefresh.Click += (s, e) => Refresh();

        txtSearch.TextChanged += (s, e) =>
        {
            _searchText = txtSearch.Text?.Trim() ?? "";
            ApplyFilter();
        };

        selectFilter.SelectedValueChanged += (s, e) =>
        {
            if (selectFilter.SelectedIndex < 0) _filter = FilterAll;
            else if (selectFilter.SelectedIndex < _filterTokens.Count)
                _filter = _filterTokens[selectFilter.SelectedIndex];
            ApplyFilter();
        };

        tableAuthors.CellButtonClick += (s, e) =>
        {
            if (e.Record is not AuthorRow row) return;
            switch (e.Btn.Id)
            {
                case "Search":
                    var dsl = SearchQueryParser.BuildFieldToken("artist", row.MainName);
                    if (!string.IsNullOrEmpty(dsl)) RequestViewInGallery?.Invoke(dsl);
                    break;
                case "Rename": OnRenameClicked(row); break;
                case "Merge":  OnMergeClicked(row);  break;
            }
        };

        // Phase I: 點 row 任何 cell → 載入該作者作品到右側清單。
        // 用 CellClick.e.Record 直接拿到 AuthorRow，避開 SelectedIndex / BindingList index
        // 對不上（AntdUI.Table 內部可能含 header / sort 影響）的 off-by-one 問題。
        tableAuthors.CellClick += (s, e) =>
        {
            if (e.Record is AuthorRow row)
                worksPanel.LoadAuthorWorks(row.Id, row.MainName);
        };
    }

    private void ApplyFilter()
    {
        IEnumerable<AuthorUsage> q = _allAuthors;

        if (!string.IsNullOrEmpty(_searchText))
            q = q.Where(a =>
                a.Author.MainName.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        if (_filter == FilterPhysical)
            q = q.Where(a => !string.IsNullOrEmpty(a.Author.FolderPath));
        else if (_filter == FilterMetadataOnly)
            q = q.Where(a => string.IsNullOrEmpty(a.Author.FolderPath));

        var rows = q.Select(ToRow).ToList();
        tableAuthors.DataSource = new BindingList<AuthorRow>(rows);
        lblCount.Text = string.Format(
            LocalizationManager.GetString("AuthorManageCountFmt") ?? "共 {0} 位作者",
            rows.Count);
    }

    private AuthorRow ToRow(AuthorUsage u) => new AuthorRow
    {
        Id = u.Author.Id,
        MainName = u.Author.MainName,
        FolderPath = u.Author.FolderPath ?? "",
        AliasCount = u.AliasCount,
        TagCount = u.TagCount,
        WorkCount = u.WorkCount,
        // yyyy-MM-dd hh:mm tt — 12-hour AM/PM 標記。InvariantCulture 強制英文
        // AM/PM（不要被 zh-TW 顯示成「上午/下午」）。注意 12-hour 字串在同一天內
        // 排序會錯（"03:42 PM" < "11:30 AM" 字典序），跨日期 OK。
        LearnedAt = u.Author.LearnedAt > 0
            ? DateTimeOffset.FromUnixTimeSeconds(u.Author.LearnedAt).LocalDateTime
                .ToString("yyyy-MM-dd hh:mm tt", System.Globalization.CultureInfo.InvariantCulture)
            : "",
        Action = new CellLink[]
        {
            new CellButton("Search", "", TTypeMini.Primary) { Ghost = true, IconSvg = "SearchOutlined"     },
            new CellButton("Rename", "", TTypeMini.Default) { Ghost = true, IconSvg = "EditOutlined"        },
            new CellButton("Merge",  "", TTypeMini.Default) { Ghost = true, IconSvg = "MergeCellsOutlined" },
        }
    };

    // ── 列操作 ─────────────────────────────────────────────────────────

    private void OnRenameClicked(AuthorRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        using var dlg = new SimpleInputDialog(
            LocalizationManager.GetString("AuthorOpRenameTitle") ?? "重新命名作者",
            (LocalizationManager.GetString("AuthorOpRenamePrompt") ?? "新主名（{0}）：").Replace("{0}", row.MainName),
            row.MainName,
            autocomplete: _translator);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;
        var newName = dlg.Value;
        if (string.IsNullOrWhiteSpace(newName) || newName == row.MainName) return;

        try
        {
            _metaStore.RenameAuthor(row.Id, newName);
            Refresh();
        }
        catch (InvalidOperationException)
        {
            // 衝突 → 詢問合併
            var existing = _allAuthors.FirstOrDefault(a => a.Author.MainName.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;

            UI.ModalEx.Open(new AntdUI.Modal.Config(form,
                LocalizationManager.GetString("AuthorOpRenameConflictTitle") ?? "作者主名衝突",
                string.Format(
                    LocalizationManager.GetString("AuthorOpRenameConflictMsg") ?? "已有作者「{0}」（{1} 部作品）。要把當前作者合併進去嗎？",
                    newName, existing.WorkCount),
                AntdUI.TType.Warn)
            {
                OkText = LocalizationManager.GetString("AuthorOpMergeButton") ?? "合併",
                CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
                OnOk = _ =>
                {
                    try { _metaStore.MergeAuthor(row.Id, existing.Author.Id); }
                    catch (Exception ex) { AntdUI.Message.error(form, ex.Message); return true; }
                    BeginInvoke(new Action(Refresh));
                    return true;
                }
            });
        }
    }

    private void OnMergeClicked(AuthorRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        using var dlg = new AuthorPickerDialog(
            LocalizationManager.GetString("AuthorOpMergeTitle") ?? "合併作者到…",
            string.Format(LocalizationManager.GetString("AuthorOpMergePrompt") ?? "選擇要把「{0}」合併到哪位作者：", row.MainName),
            _allAuthors, row.Id);
        if (dlg.ShowDialog(form) != DialogResult.OK || dlg.SelectedAuthor == null) return;

        var dest = dlg.SelectedAuthor;
        UI.ModalEx.Open(new AntdUI.Modal.Config(form,
            LocalizationManager.GetString("AuthorOpMergeConfirmTitle") ?? "確認合併",
            string.Format(
                LocalizationManager.GetString("AuthorOpMergeConfirmMsg") ??
                "將「{0}」（{1} 作品 / {2} 別名 / {3} 標籤）合併到「{4}」（{5} 作品）。\n來源作者主名會自動變成目標作者的別名，原作者刪除。\n此動作不可復原。",
                row.MainName, row.WorkCount, row.AliasCount, row.TagCount,
                dest.Author.MainName, dest.WorkCount),
            AntdUI.TType.Warn)
        {
            OkText = LocalizationManager.GetString("AuthorOpMergeButton") ?? "合併",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ =>
            {
                try { _metaStore.MergeAuthor(row.Id, dest.Author.Id); }
                catch (Exception ex) { AntdUI.Message.error(form, ex.Message); return true; }
                BeginInvoke(new Action(Refresh));
                return true;
            }
        });
    }

    // ── Theme / Localization ───────────────────────────────────────────────

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        toolbarPanel.BackColor = ThemeColors.Surface;
        tableAuthors.BackColor = ThemeColors.Surface;
        lblTitle.ForeColor = ThemeColors.Text;
        lblCount.ForeColor = ThemeColors.Secondary;
    }

    public void ApplyLocalization()
    {
        lblTitle.Text = LocalizationManager.GetString("AuthorManageTitle") ?? "作者管理";
        txtSearch.PlaceholderText = LocalizationManager.GetString("AuthorManageSearchPlaceholder") ?? "搜尋作者主名 / 別名...";
        selectFilter.PlaceholderText = LocalizationManager.GetString("AuthorFilterAll") ?? "全部作者";
        btnRefresh.Text = LocalizationManager.GetString("TagManageRefresh") ?? "重新整理";
    }

    // ── DTO for table row ─────────────────────────────────────────────────

    public class AuthorRow
    {
        public long Id { get; set; }
        public string MainName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public int AliasCount { get; set; }
        public int TagCount { get; set; }
        public int WorkCount { get; set; }
        public string LearnedAt { get; set; } = "";
        public CellLink[]? Action { get; set; }
    }
}

using AntdUI;
using ComicOrganizer.App.Core;
using System.ComponentModel;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

public partial class TagManagePanel : UserControl, IThemedControl
{
    private const string CategoryAllToken = "__all__";
    private const string CategoryUncategorizedToken = "__none__";

    private IAssetMetaStore? _metaStore;
    private TagTranslator? _translator;
    private List<TagUsage> _allTags = new();      // 從 DB 撈進來的全集（refresh 後）
    private string _searchText = "";
    private string _categoryFilter = CategoryAllToken;

    public void SetTagTranslator(TagTranslator? translator)
    {
        _translator = translator;
        ApplyFilter();
    }

    public TagManagePanel()
    {
        InitializeComponent();
        ConfigureTable();
        WireEvents();
        this.WireThemeAndLocalization();
    }

    public void Initialize(IAssetMetaStore metaStore)
    {
        _metaStore = metaStore;
    }

    public new void Refresh()
    {
        if (_metaStore == null) return;
        _allTags = _metaStore.ListTagsByUsage().ToList();
        RebuildCategoryDropdown();
        ApplyFilter();
    }

    // ── 內部 ──────────────────────────────────────────────────────────

    private void ConfigureTable()
    {
        tableTags.AutoSizeColumnsMode = ColumnsMode.Fill;
        tableTags.EmptyText = "（無資料）";
        tableTags.Columns = new ColumnCollection
        {
            new Column("Name",       LocalizationManager.GetString("TagColName")     ?? "名稱")     { Width = "20%", SortOrder = true },
            new Column("Localized",  LocalizationManager.GetString("TagColLocalized")?? "中文")     { Width = "16%" },
            new Column("Category",   LocalizationManager.GetString("TagColCategory") ?? "分類")     { Width = "16%", SortOrder = true },
            new Column("AssetCount", LocalizationManager.GetString("TagColWorks")    ?? "作品數")   { Width = "9%", SortOrder = true, Align = ColumnAlign.Right },
            new Column("AuthorCount",LocalizationManager.GetString("TagColAuthors")  ?? "作者數")   { Width = "9%", SortOrder = true, Align = ColumnAlign.Right },
            new Column("TotalCount", LocalizationManager.GetString("TagColTotal")    ?? "總計")     { Width = "9%", SortOrder = true, Align = ColumnAlign.Right },
            new Column("Action",     LocalizationManager.GetString("TagColAction")   ?? "操作")     { Width = "21%" },
        };
    }

    private void WireEvents()
    {
        btnRefresh.Click += (s, e) => Refresh();
        btnTranslateAll.Click += (s, e) => OnTranslateAllClicked();

        txtSearch.TextChanged += (s, e) =>
        {
            _searchText = txtSearch.Text?.Trim() ?? "";
            ApplyFilter();
        };

        selectCategory.SelectedValueChanged += (s, e) =>
        {
            if (selectCategory.SelectedIndex < 0) _categoryFilter = CategoryAllToken;
            else if (selectCategory.SelectedIndex < _categoryTokens.Count)
                _categoryFilter = _categoryTokens[selectCategory.SelectedIndex];
            ApplyFilter();
        };

        tableTags.CellButtonClick += (s, e) =>
        {
            if (e.Record is not TagRow row) return;
            switch (e.Btn.Id)
            {
                case "Rename":    OnRenameClicked(row);    break;
                case "Translate": OnTranslateClicked(row); break;
                case "Category":  OnCategoryClicked(row);  break;
                case "Merge":     OnMergeClicked(row);     break;
                case "Delete":    OnDeleteClicked(row);    break;
            }
        };
    }

    private readonly List<string> _categoryTokens = new();

    private void RebuildCategoryDropdown()
    {
        _categoryTokens.Clear();
        selectCategory.Items.Clear();

        // 「全部分類」
        _categoryTokens.Add(CategoryAllToken);
        selectCategory.Items.Add(LocalizationManager.GetString("TagManageCategoryAll") ?? "全部分類");

        // 從 _allTags 收集已存在的 category（含 null）
        var distinctCats = _allTags
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c == null ? 1 : 0)   // null 放最後
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        foreach (var cat in distinctCats)
        {
            if (string.IsNullOrEmpty(cat))
            {
                _categoryTokens.Add(CategoryUncategorizedToken);
                selectCategory.Items.Add(LocalizationManager.GetString("TagCategoryUncategorized") ?? "(未分類)");
            }
            else
            {
                _categoryTokens.Add(cat);
                selectCategory.Items.Add(cat);
            }
        }

        // 還原選擇（categoryFilter 還在合法 token 中就還原，否則回 All）
        var restoreIdx = _categoryTokens.IndexOf(_categoryFilter);
        selectCategory.SelectedIndex = restoreIdx >= 0 ? restoreIdx : 0;
    }

    private string CurrentLang
    {
        get
        {
            var lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (lang.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            return lang;
        }
    }

    private IReadOnlyDictionary<long, string> _dbTranslationCache = new Dictionary<long, string>();

    private void ApplyFilter()
    {
        IEnumerable<TagUsage> q = _allTags;

        if (!string.IsNullOrEmpty(_searchText))
            q = q.Where(t => t.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        if (_categoryFilter == CategoryUncategorizedToken)
            q = q.Where(t => string.IsNullOrEmpty(t.Category));
        else if (_categoryFilter != CategoryAllToken)
            q = q.Where(t => t.Category == _categoryFilter);

        var filtered = q.ToList();

        // batch 抓 DB 譯文（schema v6），優先順序：DB > runtime translator > 空
        if (_metaStore != null)
            _dbTranslationCache = _metaStore.GetTagTranslations(filtered.Select(t => t.Id).ToList(), CurrentLang);

        var rows = filtered.Select(ToRow).ToList();
        tableTags.DataSource = new BindingList<TagRow>(rows);
        lblCount.Text = string.Format(
            LocalizationManager.GetString("TagManageCountFmt") ?? "共 {0} 個標籤",
            rows.Count);
    }

    private TagRow ToRow(TagUsage t)
    {
        var uncatLabel = LocalizationManager.GetString("TagCategoryUncategorized") ?? "(未分類)";

        // 譯文順序：DB (manual / ehtag cached) > runtime translator > 空
        string localized = "";
        if (_dbTranslationCache.TryGetValue(t.Id, out var fromDb))
        {
            localized = fromDb;
        }
        else if (_translator != null)
        {
            var zh = !string.IsNullOrEmpty(t.Category)
                ? _translator.Translate(t.Category!, t.Name)
                : _translator.TranslateAny(t.Name);
            localized = zh ?? "";
        }

        return new TagRow
        {
            Id = t.Id,
            Name = t.Name,
            Localized = localized,
            Category = string.IsNullOrEmpty(t.Category) ? uncatLabel : t.Category,
            CategoryRaw = t.Category,
            AssetCount = t.AssetCount,
            AuthorCount = t.AuthorCount,
            TotalCount = t.TotalCount,
            Action = new CellLink[]
            {
                new CellButton("Rename",     "", TTypeMini.Default) { Ghost = true, IconSvg = "EditOutlined"      },
                new CellButton("Translate",  "", TTypeMini.Default) { Ghost = true, IconSvg = "TranslationOutlined" },
                new CellButton("Category",   "", TTypeMini.Default) { Ghost = true, IconSvg = "AppstoreOutlined" },
                new CellButton("Merge",      "", TTypeMini.Default) { Ghost = true, IconSvg = "MergeCellsOutlined"},
                new CellButton("Delete",     "", TTypeMini.Error)   { Ghost = true, IconSvg = "DeleteOutlined"   },
            }
        };
    }

    // ── 列操作 ─────────────────────────────────────────────────────────

    /// <summary>對所有 tag 跑 EhTag 翻譯庫一次補進 tag_translation（manual 標記不動）。</summary>
    private async void OnTranslateAllClicked()
    {
        if (_metaStore == null || _translator == null || !_translator.IsAvailable) return;
        var form = this.FindForm();
        if (form == null) return;

        btnTranslateAll.Enabled = false;
        try
        {
            // 進度提示
            AntdUI.Message.info(form,
                LocalizationManager.GetString("TagTranslateAllRunning") ?? "翻譯中…", autoClose: 1);

            var lang = CurrentLang;
            // 用全部 tag (含已 manual 的)；SyncTranslationsToDb 內會跳過 manual
            var nameToId = _allTags.ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);
            int written = await Task.Run(() => _translator.SyncTranslationsToDb(_metaStore, lang, nameToId));

            // refresh 顯示新譯文
            Refresh();
            AntdUI.Message.success(form, string.Format(
                LocalizationManager.GetString("TagTranslateAllDoneFmt") ?? "翻譯完成：補了 {0} 個（共 {1} 個 tag）",
                written, _allTags.Count));
        }
        finally
        {
            btnTranslateAll.Enabled = true;
        }
    }

    private void OnTranslateClicked(TagRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        var lang = CurrentLang;
        var current = _metaStore.GetTagTranslation(row.Id, lang) ?? "";

        using var dlg = new SimpleInputDialog(
            LocalizationManager.GetString("TagOpTranslateTitle") ?? $"編輯譯文 ({lang})",
            (LocalizationManager.GetString("TagOpTranslatePrompt") ?? "「{0}」的 {1} 譯文（留空 = 移除譯文）：")
                .Replace("{0}", row.Name).Replace("{1}", lang),
            current);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;

        var newValue = dlg.Value;
        if (newValue == current) return;

        if (string.IsNullOrWhiteSpace(newValue))
            _metaStore.DeleteTagTranslation(row.Id, lang);
        else
            _metaStore.UpsertTagTranslation(row.Id, lang, newValue, "manual");

        Refresh();
    }

    private void OnRenameClicked(TagRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        using var dlg = new SimpleInputDialog(
            LocalizationManager.GetString("TagOpRenameTitle") ?? "重新命名標籤",
            (LocalizationManager.GetString("TagOpRenamePrompt") ?? "新名稱（{0}）：")
                .Replace("{0}", row.Name),
            row.Name);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;

        var newName = dlg.Value;
        if (string.IsNullOrWhiteSpace(newName) || newName == row.Name) return;

        try
        {
            _metaStore.RenameTag(row.Id, newName);
            Refresh();
        }
        catch (InvalidOperationException)
        {
            // 衝突：名稱已存在 → 詢問合併
            var existing = _allTags.FirstOrDefault(t => t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;
            UI.ModalEx.Open(new AntdUI.Modal.Config(form,
                LocalizationManager.GetString("TagOpRenameConflictTitle") ?? "標籤名稱衝突",
                string.Format(LocalizationManager.GetString("TagOpRenameConflictMsg") ?? "已有標籤「{0}」，要改為合併嗎？\n合併後 {1} 個關聯會轉移到目標標籤。",
                    newName, row.TotalCount),
                AntdUI.TType.Warn)
            {
                OkText = LocalizationManager.GetString("TagOpMergeButton") ?? "合併",
                CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
                OnOk = _ =>
                {
                    try { _metaStore.MergeTag(row.Id, existing.Id); }
                    catch (Exception ex) { AntdUI.Message.error(form, ex.Message); return true; }
                    BeginInvoke(new Action(Refresh));
                    return true;
                }
            });
        }
    }

    private void OnCategoryClicked(TagRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        using var dlg = new SimpleInputDialog(
            LocalizationManager.GetString("TagOpCategoryTitle") ?? "編輯分類",
            (LocalizationManager.GetString("TagOpCategoryPrompt") ?? "分類名稱（標籤：{0}，留空表示「未分類」）：")
                .Replace("{0}", row.Name),
            row.CategoryRaw ?? "");
        if (dlg.ShowDialog(form) != DialogResult.OK) return;

        var newCategory = string.IsNullOrWhiteSpace(dlg.Value) ? null : dlg.Value;
        if (newCategory == row.CategoryRaw) return;

        try
        {
            _metaStore.SetTagCategory(row.Id, newCategory);
            Refresh();
        }
        catch (Exception ex)
        {
            AntdUI.Message.error(form, ex.Message);
        }
    }

    private void OnMergeClicked(TagRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        using var dlg = new TagPickerDialog(
            LocalizationManager.GetString("TagOpMergeTitle") ?? "合併標籤到…",
            string.Format(LocalizationManager.GetString("TagOpMergePrompt") ?? "選擇要把「{0}」合併到哪個標籤：", row.Name),
            _allTags, row.Id);
        if (dlg.ShowDialog(form) != DialogResult.OK || dlg.SelectedTag == null) return;

        var dest = dlg.SelectedTag;
        UI.ModalEx.Open(new AntdUI.Modal.Config(form,
            LocalizationManager.GetString("TagOpMergeConfirmTitle") ?? "確認合併",
            string.Format(
                LocalizationManager.GetString("TagOpMergeConfirmMsg") ?? "將「{0}」（{1} 關聯）合併到「{2}」（{3} 關聯）。\n來源標籤會被刪除，此動作不可復原。",
                row.Name, row.TotalCount, dest.Name, dest.TotalCount),
            AntdUI.TType.Warn)
        {
            OkText = LocalizationManager.GetString("TagOpMergeButton") ?? "合併",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ =>
            {
                try { _metaStore.MergeTag(row.Id, dest.Id); }
                catch (Exception ex) { AntdUI.Message.error(form, ex.Message); return true; }
                BeginInvoke(new Action(Refresh));
                return true;
            }
        });
    }

    private void OnDeleteClicked(TagRow row)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;

        UI.ModalEx.Open(new AntdUI.Modal.Config(form,
            LocalizationManager.GetString("TagOpDeleteTitle") ?? "刪除標籤",
            string.Format(
                LocalizationManager.GetString("TagOpDeleteMsg") ?? "永久刪除標籤「{0}」與其 {1} 個關聯（{2} 作品 + {3} 作者）。\n此動作不可復原。",
                row.Name, row.TotalCount, row.AssetCount, row.AuthorCount),
            AntdUI.TType.Error)
        {
            OkText = LocalizationManager.GetString("DialogDelete") ?? "刪除",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ =>
            {
                try { _metaStore.DeleteTag(row.Id); }
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
        tableTags.BackColor = ThemeColors.Surface;
        lblTitle.ForeColor = ThemeColors.Text;
        lblCount.ForeColor = ThemeColors.Secondary;
    }

    public void ApplyLocalization()
    {
        lblTitle.Text = LocalizationManager.GetString("TagManageTitle") ?? "標籤管理";
        txtSearch.PlaceholderText = LocalizationManager.GetString("TagManageSearchPlaceholder") ?? "搜尋標籤名稱...";
        selectCategory.PlaceholderText = LocalizationManager.GetString("TagManageCategoryAll") ?? "全部分類";
        btnRefresh.Text = LocalizationManager.GetString("TagManageRefresh") ?? "重新整理";
        btnTranslateAll.Text = LocalizationManager.GetString("TagManageTranslateAll") ?? "自動翻譯所有";
    }

    // ── DTO for table row ─────────────────────────────────────────────────

    public class TagRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Localized { get; set; } = "";   // EhTag 翻譯（找不到 = 空）
        public string Category { get; set; } = "";   // 顯示用（含「(未分類)」）
        public string? CategoryRaw { get; set; }     // 原始 category 值（編輯用）
        public int AssetCount { get; set; }
        public int AuthorCount { get; set; }
        public int TotalCount { get; set; }
        public CellLink[]? Action { get; set; }
    }
}

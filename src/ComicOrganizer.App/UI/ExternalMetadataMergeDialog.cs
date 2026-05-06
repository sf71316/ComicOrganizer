using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 從 nhentai 拉到外部 metadata 後，跟本地 metadata 並排比對，
/// 讓使用者 per-field 選擇保留哪邊。Tags 用 checkbox 多選合併。
///
/// 用法：
///   var dlg = new ExternalMetadataMergeDialog(localDetail, externalResult);
///   if (dlg.ShowDialog(parent) == DialogResult.OK) Apply(dlg.Result);
/// </summary>
public class ExternalMetadataMergeDialog : AntdUI.Window, IThemedControl
{
    private readonly WorkDetail _local;
    private readonly MetadataLookupResult _external;
    private readonly TagTranslator? _translator;

    public MergeResult? Result { get; private set; }

    // 單選 row：本地 / 外部 兩個 RadioButton + 顯示值
    private readonly List<FieldRow> _rows = new();
    // tag checkboxes
    private readonly Dictionary<string, AntdUI.Checkbox> _tagChecks = new(StringComparer.OrdinalIgnoreCase);
    private AntdUI.Checkbox? cbApplyToRemaining;

    // 手動 layout cursor
    private int _yCursor = 0;
    private System.Windows.Forms.Panel _scrollHost = null!;

    private AntdUI.Button btnOk;
    private AntdUI.Button btnCancel;

    public ExternalMetadataMergeDialog(WorkDetail local, MetadataLookupResult external,
                                       bool batchMode = false, TagTranslator? translator = null)
    {
        _local = local;
        _external = external;
        _translator = translator;

        Text = LocalizationManager.GetString("MergeDialogTitle") ?? "從 nhentai 拉取 metadata";
        Size = new System.Drawing.Size(820, 720);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
            AutoScroll = true,
        };
        Controls.Add(content);

        // header
        var lblHeader = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Font = new System.Drawing.Font("Microsoft JhengHei UI", 11F, System.Drawing.FontStyle.Bold),
            Text = string.Format(
                LocalizationManager.GetString("MergeDialogHeaderFmt") ?? "比對結果（外部 ID: {0}）",
                external.ExternalId),
        };
        var lblPath = new AntdUI.Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = local.Path,
        };

        // 動作 row（底部）
        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 56,
            Padding = new Padding(0, 12, 0, 0),
        };
        btnOk = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("MergeDialogApply") ?? "套用",
            Type = TTypeMini.Primary,
            Width = 100, Height = 36,
            Margin = new Padding(8, 0, 0, 0),
        };
        btnOk.Click += (s, e) => OnApply();

        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 100, Height = 36,
        };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        if (batchMode)
        {
            cbApplyToRemaining = new AntdUI.Checkbox
            {
                Text = LocalizationManager.GetString("MergeDialogApplyToRemaining") ?? "套用此選擇到剩餘批次",
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = System.Drawing.Color.Transparent,
            };
        }

        // 主內容：可捲動 Panel + 手動 y 定位（先 Add 進 content 取得 ClientSize）
        _scrollHost = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = System.Drawing.Color.Transparent,
        };

        // 先把 dock 結構建好（Bottom / Top 先 Add，Fill 最後）
        content.Controls.Add(_scrollHost);   // Fill
        content.Controls.Add(lblPath);       // Top
        content.Controls.Add(lblHeader);     // Top
        if (cbApplyToRemaining != null) content.Controls.Add(cbApplyToRemaining);
        content.Controls.Add(btnRow);

        // 強制 layout 一次拿到實際尺寸再加 row
        content.PerformLayout();

        AddScalarRow(LocalizationManager.GetString("MergeFieldTitle") ?? "標題",
            local.Title, ChooseExternalTitle(),
            preferLocal: false);

        AddScalarRow(LocalizationManager.GetString("MergeFieldAuthor") ?? "作者",
            local.AuthorMainName, ChooseExternalAuthor(),
            preferLocal: false);

        AddScalarRow(LocalizationManager.GetString("MergeFieldLanguage") ?? "語言",
            local.Language, ChooseExternalLanguage(),
            preferLocal: true,
            warning: LocalizationManager.GetString("MergeWarnLanguage") ??
                "⚠ 警告：語言不同可能表示是不同版本（譯本 vs 原版），預設保留本地");

        AddScalarRow(LocalizationManager.GetString("MergeFieldPageCount") ?? "頁數",
            local.PageCount?.ToString(), external.NumPages?.ToString(),
            preferLocal: false);

        AddScalarRow(LocalizationManager.GetString("MergeFieldScanlator") ?? "翻譯組",
            local.TranslationGroup, string.IsNullOrEmpty(external.Scanlator) ? null : external.Scanlator,
            preferLocal: false);

        // Tag 區
        AddTagsRow();

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        this.WireThemeAndLocalization();
    }

    // ── Field rows ─────────────────────────────────────────────────────

    private void AddScalarRow(string label,
                              string? localValue, string? externalValue,
                              bool preferLocal, string? warning = null)
    {
        int rowWidth = Math.Max(_scrollHost.ClientSize.Width - 24, 740);
        int rowHeight = warning != null ? 95 : 75;
        var row = new System.Windows.Forms.Panel
        {
            Location = new System.Drawing.Point(0, _yCursor),
            Width = rowWidth,
            Height = rowHeight,
            BackColor = System.Drawing.Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var lblLabel = new AntdUI.Label
        {
            Text = label,
            Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold),
            Location = new System.Drawing.Point(0, 0),
            Size = new System.Drawing.Size(200, 22),
            BackColor = System.Drawing.Color.Transparent,
        };

        var rbLocal = new System.Windows.Forms.RadioButton
        {
            Text = $"本地: {DisplayValue(localValue)}",
            Location = new System.Drawing.Point(0, 24),
            Size = new System.Drawing.Size(rowWidth - 8, 22),
            BackColor = System.Drawing.Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = true,
        };
        var rbExternal = new System.Windows.Forms.RadioButton
        {
            Text = $"外部: {DisplayValue(externalValue)}",
            Location = new System.Drawing.Point(0, 48),
            Size = new System.Drawing.Size(rowWidth - 8, 22),
            BackColor = System.Drawing.Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = true,
        };

        // 預設選擇：preferLocal 或外部沒值 → 本地；否則外部
        bool externalHasValue = !string.IsNullOrEmpty(externalValue);
        bool localHasValue = !string.IsNullOrEmpty(localValue);
        bool selectExternal = !preferLocal && externalHasValue;
        if (!externalHasValue) selectExternal = false;
        if (selectExternal) rbExternal.Checked = true;
        else rbLocal.Checked = true;

        // 雙方都沒值 → 兩個都 disable
        if (!localHasValue && !externalHasValue)
        {
            rbLocal.Enabled = false;
            rbExternal.Enabled = false;
        }

        row.Controls.Add(rbExternal);
        row.Controls.Add(rbLocal);
        row.Controls.Add(lblLabel);

        if (warning != null)
        {
            var lblWarn = new AntdUI.Label
            {
                Text = warning,
                Location = new System.Drawing.Point(0, 72),
                Size = new System.Drawing.Size(720, 22),
                ForeColor = System.Drawing.Color.OrangeRed,
                BackColor = System.Drawing.Color.Transparent,
            };
            row.Controls.Add(lblWarn);
        }

        _scrollHost.Controls.Add(row);
        _yCursor += rowHeight + 8;

        _rows.Add(new FieldRow(label, localValue, externalValue, rbLocal, rbExternal));
    }

    private void AddTagsRow()
    {
        var localTagNames = _local.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalTagNames = _external.Tags
            .Where(t => t.Type != "language" && t.Type != "category")  // language 進 asset_meta.language，category 進 asset_meta
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allTags = localTagNames.Union(externalTagNames, StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();

        int rowWidth = Math.Max(_scrollHost.ClientSize.Width - 24, 740);
        int rowHeight = 36 + Math.Max(allTags.Count, 1) * 26 + 12;
        var row = new System.Windows.Forms.Panel
        {
            Location = new System.Drawing.Point(0, _yCursor),
            Width = rowWidth,
            Height = rowHeight,
            BackColor = System.Drawing.Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var lblLabel = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("MergeFieldTags") ?? "Tags（勾選保留）",
            Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold),
            Location = new System.Drawing.Point(0, 0),
            Size = new System.Drawing.Size(300, 22),
            BackColor = System.Drawing.Color.Transparent,
        };
        row.Controls.Add(lblLabel);

        if (allTags.Count == 0)
        {
            var lblEmpty = new AntdUI.Label
            {
                Text = LocalizationManager.GetString("MergeNoTags") ?? "(無 tag)",
                Location = new System.Drawing.Point(0, 30),
                Size = new System.Drawing.Size(300, 22),
                BackColor = System.Drawing.Color.Transparent,
                ForeColor = System.Drawing.Color.Gray,
            };
            row.Controls.Add(lblEmpty);
        }
        else
        {
            // 建一個 slug → category 對映（從 external tags 撈，本地 tags 也會有 category）
            var categoryBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _external.Tags) categoryBySlug.TryAdd(t.Name, t.Type);
            foreach (var t in _local.Tags) categoryBySlug.TryAdd(t.Name, t.Category ?? "");

            int y = 30;
            foreach (var tagName in allTags)
            {
                bool inLocal = localTagNames.Contains(tagName);
                bool inExternal = externalTagNames.Contains(tagName);
                string source = (inLocal, inExternal) switch
                {
                    (true, true) => "[both]",
                    (true, false) => "[local]",
                    (false, true) => "[external]",
                    _ => "",
                };
                // 嘗試翻譯
                string display = tagName;
                if (_translator != null)
                {
                    string? zh = categoryBySlug.TryGetValue(tagName, out var cat)
                        ? _translator.Translate(cat, tagName)
                        : _translator.TranslateAny(tagName);
                    if (!string.IsNullOrEmpty(zh) && zh != tagName)
                        display = $"{zh}  ({tagName})";
                }
                var cb = new AntdUI.Checkbox
                {
                    Text = $"{display}  {source}",
                    Location = new System.Drawing.Point(20, y),
                    Size = new System.Drawing.Size(rowWidth - 28, 24),
                    Checked = true,   // 全部預設選取
                    BackColor = System.Drawing.Color.Transparent,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };
                row.Controls.Add(cb);
                _tagChecks[tagName] = cb;
                y += 26;
            }
        }

        _scrollHost.Controls.Add(row);
        _yCursor += rowHeight + 8;
    }

    // ── Build result ───────────────────────────────────────────────────

    private void OnApply()
    {
        var result = new MergeResult
        {
            ApplyToRemaining = cbApplyToRemaining?.Checked ?? false,
        };

        // 每個 row：external radio checked 才填 result（null = 保留本地）
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            string? selected = r.RbExternal.Checked ? r.ExternalValue : r.LocalValue;
            // 若選 local，也仍需填回 — 因為 caller 會用 result 寫 metadata，null 才表示「不動」
            // 但既有 metadata 寫入流程是 upsert，給 null 不會清舊值
            // 所以策略：external 選擇且值不同於 local → 填值；其他 → null
            switch (r.Label)
            {
                case var l when l == (LocalizationManager.GetString("MergeFieldTitle") ?? "標題"):
                    result.Title = r.RbExternal.Checked ? r.ExternalValue : null; break;
                case var l when l == (LocalizationManager.GetString("MergeFieldAuthor") ?? "作者"):
                    result.AuthorMainName = r.RbExternal.Checked ? r.ExternalValue : null;
                    // 使用者選了「外部」並且我們有翻譯後跟 raw 不同的 slug → 把 slug 進 alias 留著
                    if (r.RbExternal.Checked && !string.IsNullOrEmpty(_selectedArtistSlug))
                        result.AuthorAliasSlug = _selectedArtistSlug;
                    break;
                case var l when l == (LocalizationManager.GetString("MergeFieldLanguage") ?? "語言"):
                    result.Language = r.RbExternal.Checked ? r.ExternalValue : null; break;
                case var l when l == (LocalizationManager.GetString("MergeFieldPageCount") ?? "頁數"):
                    if (r.RbExternal.Checked && int.TryParse(r.ExternalValue, out var pc)) result.PageCount = pc;
                    break;
                case var l when l == (LocalizationManager.GetString("MergeFieldScanlator") ?? "翻譯組"):
                    result.TranslationGroup = r.RbExternal.Checked ? r.ExternalValue : null; break;
            }
        }

        // Tags：勾選清單即最終 tag 名集合
        result.Tags = _tagChecks
            .Where(kv => kv.Value.Checked)
            .Select(kv => kv.Key)
            .ToList();

        // ApplyToRemaining: 從本次選擇生成策略，給後續 N-1 本批次自動套用
        if (result.ApplyToRemaining)
        {
            result.Strategy = new BatchSyncStrategy
            {
                TitleSource = SourceFor("MergeFieldTitle", "標題"),
                AuthorSource = SourceFor("MergeFieldAuthor", "作者"),
                LanguageSource = SourceFor("MergeFieldLanguage", "語言"),
                TranslationGroupSource = SourceFor("MergeFieldScanlator", "翻譯組"),
                PageCountSource = SourceFor("MergeFieldPageCount", "頁數"),
                // tag 策略：勾本地 tag 全部跟外部聯集 → UnionBoth；只勾外部 → AllExternal；都不勾 → LocalOnly
                TagsPolicy = DeriveTagsPolicy(),
            };
        }

        // 外部 source raw_json 永遠寫進去（archive）
        result.ExternalSourceRecord = new AssetExternalSource
        {
            AssetId = _local.AssetId,
            Source = _external.Source,
            SourceId = _external.ExternalId,
            MediaId = _external.MediaId,
            UploadAt = _external.UploadAt,
            RawJson = _external.RawJson,
            FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        Result = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>找指定 label 的 row，回傳該 row 使用者選的 source。找不到 → Local（保守）。</summary>
    private FieldSource SourceFor(string locKey, string fallback)
    {
        var label = LocalizationManager.GetString(locKey) ?? fallback;
        var row = _rows.FirstOrDefault(r => r.Label == label);
        return row != null && row.RbExternal.Checked ? FieldSource.External : FieldSource.Local;
    }

    /// <summary>從 tag checkbox 狀態推導 tag 策略。本地 + 外部 tags 比對勾選狀況。</summary>
    private TagsMergePolicy DeriveTagsPolicy()
    {
        var localNames = _local.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var externalNames = _external.Tags
            .Where(t => t.Type != "language" && t.Type != "category")
            .Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var checkedNames = _tagChecks.Where(kv => kv.Value.Checked).Select(kv => kv.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool keptAllLocal = localNames.IsSubsetOf(checkedNames);
        bool keptAnyExternal = externalNames.Any(n => checkedNames.Contains(n));

        if (keptAnyExternal && keptAllLocal) return TagsMergePolicy.UnionBoth;
        if (keptAnyExternal) return TagsMergePolicy.AllExternal;
        return TagsMergePolicy.LocalOnly;
    }

    private string? ChooseExternalTitle()
    {
        // pretty 是乾淨標題，預設用這個
        if (!string.IsNullOrEmpty(_external.TitlePretty)) return _external.TitlePretty;
        if (!string.IsNullOrEmpty(_external.TitleEnglish)) return _external.TitleEnglish;
        return _external.TitleJapanese;
    }

    /// <summary>取第一個 artist tag 並回傳「給 user 看的顯示名」。
    /// 有 translator 命中 → 回 CJK 主名（slug 從 _selectedArtistSlug 帶到 result 進 alias）。
    /// 沒命中 → 回 nhentai raw artist.Name（通常已是英文/羅馬拼音）。</summary>
    private string? ChooseExternalAuthor()
    {
        var artist = _external.Tags.FirstOrDefault(t => t.Type == "artist");
        if (artist == null) return null;
        _selectedArtistSlug = artist.Slug;

        // 用 EhTag 嘗試 canonicalize 成 CJK 主名（#53 Phase 6）
        if (_translator != null)
        {
            var cjk = _translator.TranslateArtist(artist.Slug);
            if (!string.IsNullOrEmpty(cjk)) return cjk;
        }
        return artist.Name;
    }

    /// <summary>記住目前 dialog 顯示的 artist slug — ConfirmAndClose 時帶進 result.AuthorAliasSlug。</summary>
    private string? _selectedArtistSlug;

    private string? ChooseExternalLanguage()
    {
        // 取第一個 language tag（排除 "translated" 這種泛指標記）
        var langs = _external.Tags.Where(t => t.Type == "language" && t.Slug != "translated").ToList();
        return langs.FirstOrDefault()?.Name;
    }

    private static string DisplayValue(string? value)
    {
        return string.IsNullOrEmpty(value) ? "(空)" : value;
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
    }

    public void ApplyLocalization()
    {
        // 標題與按鈕已於建構時設定，不重設
    }

    private record FieldRow(string Label, string? LocalValue, string? ExternalValue,
                            System.Windows.Forms.RadioButton RbLocal,
                            System.Windows.Forms.RadioButton RbExternal);
}

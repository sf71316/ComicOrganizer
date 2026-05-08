using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 自訂虛擬庫 編輯 / 新增 dialog（#54 Phase 2）。
///
/// 兩種模式：
///   創建：傳 initialDsl（可選，從搜尋結果存入時帶入），其他欄位空
///   編輯：傳 existing VirtualLibrary，預填全部欄位
///
/// 即時計數：txtDsl TextChanged → debounce 300ms → SearchExecutor 跑一次 →
/// 顯示「找到 N 件」。calling thread 用 SearchExecutor.Execute（同步）OK，
/// 因為大多 query 在 sub-second 完成；真的卡才考慮 async。
///
/// DialogResult.OK 後讀 ResultName / ResultDsl / ResultIconSvg；caller 自行
/// 寫進 DB（CreateVirtualLibrary / UpdateVirtualLibrary）。
/// </summary>
public class SaveVirtualLibraryDialog : AntdUI.Window, IThemedControl
{
    private readonly IAssetMetaStore _store;
    private readonly VirtualLibrary? _editing;

    private readonly AntdUI.Label lblNameHint;
    private readonly AntdUI.Input txtName;
    private readonly AntdUI.Label lblDslHint;
    private readonly AntdUI.Input txtDsl;
    private readonly AntdUI.Label lblIconHint;
    private readonly AntdUI.Select selectIcon;
    private readonly AntdUI.Label lblPreview;
    private readonly AntdUI.Button btnOk;
    private readonly AntdUI.Button btnCancel;
    private readonly System.Windows.Forms.Timer _previewDebounce;

    /// <summary>10 個常用 svg icon 名 — 給使用者下拉選。null = 預設（caller 視為 BookOutlined）。</summary>
    private static readonly (string svg, string label)[] IconChoices = new[]
    {
        ("BookOutlined", "📚 書"),
        ("StarOutlined", "⭐ 星"),
        ("HeartOutlined", "❤ 愛心"),
        ("FireOutlined", "🔥 火"),
        ("TagOutlined", "🏷 標籤"),
        ("UserOutlined", "👤 作者"),
        ("GlobalOutlined", "🌐 語言"),
        ("ReadOutlined", "📖 閱讀"),
        ("HistoryOutlined", "🕒 軌跡"),
        ("FolderOutlined", "📁 資料夾"),
    };

    public string ResultName { get; private set; } = "";
    public string ResultDsl { get; private set; } = "";
    public string? ResultIconSvg { get; private set; }

    /// <summary>新增模式 — initialDsl 可選（從搜尋結果存入時帶入）。</summary>
    public SaveVirtualLibraryDialog(IAssetMetaStore store, string? initialDsl = null)
        : this(store, existing: null, initialDsl: initialDsl) { }

    /// <summary>編輯模式 — 預填全部欄位。</summary>
    public SaveVirtualLibraryDialog(IAssetMetaStore store, VirtualLibrary existing)
        : this(store, existing: existing, initialDsl: null) { }

    private SaveVirtualLibraryDialog(IAssetMetaStore store, VirtualLibrary? existing, string? initialDsl)
    {
        _store = store;
        _editing = existing;

        Text = existing != null
            ? (LocalizationManager.GetString("SaveVLibEditTitle") ?? "編輯自訂虛擬庫")
            : (LocalizationManager.GetString("SaveVLibCreateTitle") ?? "新增自訂虛擬庫");
        Size = new Size(520, 360);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 16),
        };
        Controls.Add(content);

        // ── 名稱 ───────────────────────────────────────
        lblNameHint = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("SaveVLibNameLabel") ?? "名稱",
            Dock = DockStyle.Top,
            Height = 22,
        };
        txtName = new AntdUI.Input
        {
            Text = existing?.Name ?? "",
            Dock = DockStyle.Top,
            Height = 32,
            Margin = new Padding(0, 4, 0, 0),
            PlaceholderText = LocalizationManager.GetString("SaveVLibNamePlaceholder") ?? "例：Flugel 中文版",
        };

        // ── DSL ───────────────────────────────────────
        lblDslHint = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("SaveVLibDslLabel") ?? "條件 (DSL)",
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 12, 0, 0),
        };
        txtDsl = new AntdUI.Input
        {
            Text = existing?.Dsl ?? initialDsl ?? "",
            Dock = DockStyle.Top,
            Multiline = true,
            Height = 70,
            Margin = new Padding(0, 4, 0, 0),
            PlaceholderText = LocalizationManager.GetString("SaveVLibDslPlaceholder")
                ?? "artist:flugel language:zh -tag:DL版",
            Font = new Font("Consolas", 10F),
        };

        // ── 圖示 ───────────────────────────────────────
        lblIconHint = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("SaveVLibIconLabel") ?? "圖示",
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 8, 0, 0),
        };
        selectIcon = new AntdUI.Select
        {
            Dock = DockStyle.Top,
            Height = 32,
            Margin = new Padding(0, 4, 0, 0),
        };
        foreach (var (_, label) in IconChoices) selectIcon.Items.Add(label);
        // 預填：編輯模式找對應 svg index；否則預設 BookOutlined (index 0)
        var initialIdx = 0;
        if (existing?.IconSvg != null)
        {
            for (int i = 0; i < IconChoices.Length; i++)
                if (IconChoices[i].svg == existing.IconSvg) { initialIdx = i; break; }
        }
        selectIcon.SelectedIndex = initialIdx;

        // ── 即時計數預覽 ─────────────────────────────
        lblPreview = new AntdUI.Label
        {
            Text = "",
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 12, 0, 0),
        };

        // ── 按鈕列 ─────────────────────────────────
        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
        };
        btnOk = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogSave") ?? "儲存",
            Type = AntdUI.TTypeMini.Primary,
            Width = 80, Height = 32,
            Margin = new Padding(8, 0, 0, 0),
        };
        btnOk.Click += (s, e) => OnSave();
        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 80, Height = 32,
        };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);

        // 加入順序 = top-down 反向（DockStyle.Top 後加在底）
        // 我們要：name / dsl / icon / preview 依序 → 反向 add: preview, icon-block, dsl-block, name-block
        content.Controls.Add(lblPreview);
        content.Controls.Add(selectIcon);
        content.Controls.Add(lblIconHint);
        content.Controls.Add(txtDsl);
        content.Controls.Add(lblDslHint);
        content.Controls.Add(txtName);
        content.Controls.Add(lblNameHint);
        content.Controls.Add(btnRow);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // ── 即時計數 debounce ────────────────────────
        _previewDebounce = new System.Windows.Forms.Timer { Interval = 300 };
        _previewDebounce.Tick += (s, e) =>
        {
            _previewDebounce.Stop();
            UpdatePreviewCount();
        };
        txtDsl.TextChanged += (s, e) => { _previewDebounce.Stop(); _previewDebounce.Start(); };

        // 立刻先跑一次預覽（沒有 debounce 延遲）
        UpdatePreviewCount();

        this.WireThemeAndLocalization();
    }

    private void UpdatePreviewCount()
    {
        var dsl = (txtDsl.Text ?? "").Trim();
        if (string.IsNullOrEmpty(dsl))
        {
            lblPreview.Text = "";
            return;
        }
        try
        {
            var query = SearchQueryParser.Parse(dsl);
            if (query.IsEmpty)
            {
                lblPreview.Text = LocalizationManager.GetString("SaveVLibInvalidQuery") ?? "（條件無法解析）";
                return;
            }
            var lang = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW" : null;
            var executor = new SearchExecutor(_store, lang);
            var paths = executor.Execute(query);
            lblPreview.Text = string.Format(
                LocalizationManager.GetString("SaveVLibPreviewFmt") ?? "✓ 找到 {0} 件",
                paths.Count);
        }
        catch (Exception ex)
        {
            lblPreview.Text = string.Format(
                LocalizationManager.GetString("SaveVLibPreviewErrFmt") ?? "（錯誤：{0}）",
                ex.Message);
        }
    }

    private void OnSave()
    {
        var name = (txtName.Text ?? "").Trim();
        var dsl = (txtDsl.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            AntdUI.Message.warn(this, LocalizationManager.GetString("SaveVLibErrNoName") ?? "請輸入名稱");
            txtName.Focus();
            return;
        }
        if (string.IsNullOrEmpty(dsl))
        {
            AntdUI.Message.warn(this, LocalizationManager.GetString("SaveVLibErrNoDsl") ?? "請輸入查詢條件");
            txtDsl.Focus();
            return;
        }
        // 驗 DSL 至少能 parse
        var query = SearchQueryParser.Parse(dsl);
        if (query.IsEmpty)
        {
            AntdUI.Message.warn(this, LocalizationManager.GetString("SaveVLibErrInvalidDsl") ?? "查詢條件無法解析");
            txtDsl.Focus();
            return;
        }

        ResultName = name;
        ResultDsl = dsl;
        var iconIdx = selectIcon.SelectedIndex;
        ResultIconSvg = (iconIdx >= 0 && iconIdx < IconChoices.Length)
            ? IconChoices[iconIdx].svg : null;

        DialogResult = DialogResult.OK;
        Close();
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblNameHint.ForeColor = ThemeColors.Text;
        lblDslHint.ForeColor = ThemeColors.Text;
        lblIconHint.ForeColor = ThemeColors.Text;
        lblPreview.ForeColor = ThemeColors.Primary;
    }

    public void ApplyLocalization()
    {
        Text = _editing != null
            ? (LocalizationManager.GetString("SaveVLibEditTitle") ?? "編輯自訂虛擬庫")
            : (LocalizationManager.GetString("SaveVLibCreateTitle") ?? "新增自訂虛擬庫");
        lblNameHint.Text = LocalizationManager.GetString("SaveVLibNameLabel") ?? "名稱";
        lblDslHint.Text = LocalizationManager.GetString("SaveVLibDslLabel") ?? "條件 (DSL)";
        lblIconHint.Text = LocalizationManager.GetString("SaveVLibIconLabel") ?? "圖示";
        txtName.PlaceholderText = LocalizationManager.GetString("SaveVLibNamePlaceholder") ?? "例：Flugel 中文版";
        txtDsl.PlaceholderText = LocalizationManager.GetString("SaveVLibDslPlaceholder") ?? "artist:flugel language:zh -tag:DL版";
        btnOk.Text = LocalizationManager.GetString("DialogSave") ?? "儲存";
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
        UpdatePreviewCount();   // preview 文字也含 i18n
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _previewDebounce.Dispose();
        base.Dispose(disposing);
    }
}

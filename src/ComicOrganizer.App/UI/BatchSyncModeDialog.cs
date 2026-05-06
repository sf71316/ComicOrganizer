using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 批次同步預檢 + 模式選擇 dialog（#52 Phase I refactor）。
///
/// 兩種 mode：
///   1. PerWorkConfirm — 對選取作品依序展開 MergeDialog（user 每本確認）
///   2. SmartSearchBatch — 用標題搜尋首筆 + 預設覆蓋策略自動套；找不到 / 模糊才跳 picker
/// </summary>
public class BatchSyncModeDialog : AntdUI.Window, IThemedControl
{
    private readonly AntdUI.Label lblHeader;
    private readonly AntdUI.Label lblStats;
    private readonly AntdUI.Radio rdPerWork;
    private readonly AntdUI.Radio rdSmartBatch;
    private readonly AntdUI.Label lblPolicyHeader;
    private readonly Dictionary<OverwritePolicyPreset, AntdUI.Radio> _policyRadios = new();
    private readonly AntdUI.Button btnOk;
    private readonly AntdUI.Button btnCancel;

    public BatchSyncMode SelectedMode { get; private set; } = BatchSyncMode.PerWorkConfirm;
    public OverwritePolicyPreset SelectedPolicy { get; private set; } = OverwritePolicyPreset.KeepAuthor;

    public BatchSyncModeDialog(string authorName, int totalCount, int eligibleCount, IReadOnlyDictionary<string, int> skippedReasons)
    {
        Text = LocalizationManager.GetString("BatchSyncDialogTitle") ?? "批次同步";
        Size = new Size(540, 540);
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

        // 底部按鈕列
        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
        };
        btnOk = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("BatchSyncProceed") ?? "開始",
            Type = AntdUI.TTypeMini.Primary,
            Width = 80, Height = 32,
            Margin = new Padding(8, 0, 0, 0),
        };
        btnOk.Click += (s, e) => { CommitSelection(); DialogResult = DialogResult.OK; Close(); };
        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 80, Height = 32,
        };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnRow.Controls.Add(btnOk);
        btnRow.Controls.Add(btnCancel);
        content.Controls.Add(btnRow);

        // 主要內容用垂直 stack
        var stack = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };
        content.Controls.Add(stack);

        lblHeader = new AntdUI.Label
        {
            Text = string.Format(
                LocalizationManager.GetString("BatchSyncPreCheckHeaderFmt") ?? "作者「{0}」", authorName),
            Font = new Font("Microsoft JhengHei UI", 11F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };
        stack.Controls.Add(lblHeader);

        var skipText = skippedReasons.Count > 0
            ? "\n跳過：" + string.Join("、", skippedReasons.Select(kv => $"{kv.Key}({kv.Value})"))
            : "";
        lblStats = new AntdUI.Label
        {
            Text = string.Format(
                LocalizationManager.GetString("BatchSyncStatsLabelFmt") ?? "共 {0} 本，可同步 {1} 本。{2}",
                totalCount, eligibleCount, skipText),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14),
        };
        stack.Controls.Add(lblStats);

        // ── 模式選擇 ───────────────────────────────────────
        var lblModeHeader = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("BatchSyncModeHeader") ?? "處理模式",
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        stack.Controls.Add(lblModeHeader);

        rdPerWork = MakeRadio(
            LocalizationManager.GetString("BatchSyncModePerWork") ?? "逐一確認",
            LocalizationManager.GetString("BatchSyncModePerWorkDesc") ?? "每本都跳 dialog 給你確認（推薦：精準審核）",
            isDefault: true);
        rdPerWork.CheckedChanged += (s, e) => OnModeChanged();
        stack.Controls.Add(rdPerWork);

        rdSmartBatch = MakeRadio(
            LocalizationManager.GetString("BatchSyncModeSmart") ?? "智慧搜尋批次",
            LocalizationManager.GetString("BatchSyncModeSmartDesc") ?? "用作品標題自動搜尋首筆，套用覆蓋策略（推薦：大量、檔名清楚）",
            isDefault: false);
        rdSmartBatch.CheckedChanged += (s, e) => OnModeChanged();
        stack.Controls.Add(rdSmartBatch);

        // ── 覆蓋策略（智慧搜尋批次才顯示） ─────────────────────────
        lblPolicyHeader = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("BatchSyncPolicyHeader") ?? "覆蓋策略（搜尋命中後）",
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 4),
            Enabled = false,
        };
        stack.Controls.Add(lblPolicyHeader);

        AddPolicyRadio(stack, OverwritePolicyPreset.KeepAuthor,
            LocalizationManager.GetString("BatchSyncPolicyKeepAuthor")     ?? "C. 保留作者",
            LocalizationManager.GetString("BatchSyncPolicyKeepAuthorDesc") ?? "本地作者不動 + 其他全覆蓋（推薦：庫已分好作者資料夾）",
            isDefault: true);
        AddPolicyRadio(stack, OverwritePolicyPreset.OnlyTagsAndLanguage,
            LocalizationManager.GetString("BatchSyncPolicyOnlyTagsLang")     ?? "E. 只補 tags / language",
            LocalizationManager.GetString("BatchSyncPolicyOnlyTagsLangDesc") ?? "本地全保留，只加外部 tags（聯集）+ language");
        AddPolicyRadio(stack, OverwritePolicyPreset.KeepTitleAndAuthor,
            LocalizationManager.GetString("BatchSyncPolicyKeepTitleAuthor")     ?? "D. 保留標題與作者",
            LocalizationManager.GetString("BatchSyncPolicyKeepTitleAuthorDesc") ?? "本地標題 + 作者不動，補 language / scanlator / pages / tags");
        AddPolicyRadio(stack, OverwritePolicyPreset.KeepTitle,
            LocalizationManager.GetString("BatchSyncPolicyKeepTitle")     ?? "B. 保留標題",
            LocalizationManager.GetString("BatchSyncPolicyKeepTitleDesc") ?? "本地標題不動，其他全覆蓋");
        AddPolicyRadio(stack, OverwritePolicyPreset.FullOverwrite,
            LocalizationManager.GetString("BatchSyncPolicyFull")     ?? "A. 全覆蓋",
            LocalizationManager.GetString("BatchSyncPolicyFullDesc") ?? "全部用外部資料（title / author / language / tags 全替換）");

        OnModeChanged();   // 初始化 policy radio enabled state

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        this.WireThemeAndLocalization();
    }

    private AntdUI.Radio MakeRadio(string text, string desc, bool isDefault)
    {
        return new AntdUI.Radio
        {
            Text = text + (string.IsNullOrEmpty(desc) ? "" : "\n      " + desc),
            Checked = isDefault,
            AutoSize = true,
            Margin = new Padding(4, 4, 0, 6),
            BackColor = Color.Transparent,   // 跟 form surface 一致，不要有白底色塊
        };
    }

    private void AddPolicyRadio(Control stack, OverwritePolicyPreset preset, string text, string desc, bool isDefault = false)
    {
        var rd = MakeRadio(text, desc, isDefault);
        rd.Margin = new Padding(20, 2, 0, 4);   // 右側縮排，視覺上是 mode 的子選項
        rd.Enabled = false;   // 預設 disabled，rdSmartBatch 勾才 enable
        rd.CheckedChanged += (s, e) => { if (rd.Checked) SelectedPolicy = preset; };
        _policyRadios[preset] = rd;
        if (isDefault) SelectedPolicy = preset;
        stack.Controls.Add(rd);
    }

    private void OnModeChanged()
    {
        var smartActive = rdSmartBatch.Checked;
        SelectedMode = smartActive ? BatchSyncMode.SmartSearchBatch : BatchSyncMode.PerWorkConfirm;
        lblPolicyHeader.Enabled = smartActive;
        foreach (var rd in _policyRadios.Values) rd.Enabled = smartActive;
    }

    private void CommitSelection()
    {
        SelectedMode = rdSmartBatch.Checked ? BatchSyncMode.SmartSearchBatch : BatchSyncMode.PerWorkConfirm;
        // SelectedPolicy 已在 radio CheckedChanged 中即時更新
    }

    public void ApplyThemeColors() { BackColor = ThemeColors.Surface; }
    public void ApplyLocalization()
    {
        btnOk.Text = LocalizationManager.GetString("BatchSyncProceed") ?? "開始";
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
    }
}

/// <summary>批次同步處理模式。</summary>
public enum BatchSyncMode
{
    /// <summary>逐一確認 — 每本都跳 MergeDialog 或 search picker。</summary>
    PerWorkConfirm,
    /// <summary>智慧搜尋批次 — 標題搜尋首筆自動套用 + 預設覆蓋策略。</summary>
    SmartSearchBatch,
}

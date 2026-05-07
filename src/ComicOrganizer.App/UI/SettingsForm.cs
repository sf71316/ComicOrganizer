using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

public partial class SettingsForm : Window, IThemedControl
{
    private readonly ConfigManager _configManager;
    private readonly UserPreferencesManager _prefsManager;
    public AppConfig Config { get; private set; }
    public UserPreferences Prefs { get; private set; }

    // 從 Designer.cs 搬到這裡 inline 初始化，避免 VS Designer regenerate 重複把 new 那行吃掉
    private readonly AntdUI.FolderBrowserDialog folderBrowserDialog = new AntdUI.FolderBrowserDialog();

    public SettingsForm(ConfigManager configManager, AppConfig config,
                        UserPreferencesManager prefsManager, UserPreferences prefs)
    {
        InitializeComponent();
        _configManager = configManager;
        _prefsManager = prefsManager;
        Config = config;
        Prefs = prefs;

        LoadConfig();
        WireEvents();

        this.WireThemeAndLocalization();
    }

    public void ApplyThemeColors()
    {
        windowBar.BackColor = Core.ThemeColors.Primary;
        windowBar.ForeColor = Core.ThemeColors.HeaderText;
        content.BackColor = Core.ThemeColors.Surface;
        leftMenu.BackColor = Core.ThemeColors.Background;
        footer.BackColor = Core.ThemeColors.Surface;
        this.BackColor = Core.ThemeColors.Background;
        btnBrowseSource.DefaultBack = Core.ThemeColors.Primary;
        btnBrowseTarget.DefaultBack = Core.ThemeColors.Primary;
        btnBrowseViewer.DefaultBack = Core.ThemeColors.Primary;
        btnSave.BackColor = Core.ThemeColors.Primary;
        // 外部 metadata 文字色
        lblExtTitle.ForeColor = Core.ThemeColors.Text;
        lblExtDesc.ForeColor = Core.ThemeColors.Text;
        lblExtEnable.ForeColor = Core.ThemeColors.Text;
        lblExtKey.ForeColor = Core.ThemeColors.Text;
        lblExtStatus.ForeColor = Core.ThemeColors.Text;
        this.Refresh();
    }

    private static string FmtRtl() => LocalizationManager.GetString("ReadingDirectionRtl") ?? "左←右";
    private static string FmtLtr() => LocalizationManager.GetString("ReadingDirectionLtr") ?? "左→右";

    private void LoadConfig()
    {
        txtSource.Text = Config.UnorganizedDirectory;
        txtTarget.Text = Config.RootDirectory;
        txtImageViewer.Text = Config.ImageViewerPath;
        txtImageViewerArgs.Text = Config.ImageViewerArgs;
        cbDebug.Checked = Config.DebugMode;
        cbTrackReading.Checked = Prefs.TrackReadingRecord;
        numMaxScanDepth.Value = Config.MaxScanDepth;
        LoadSizeWithUnit(Config.MinCoverFileSizeBytes, numMinCoverSize, selectMinCoverUnit);
        LoadSizeWithUnit(Config.MaxCoverFileSizeBytes, numMaxCoverSize, selectMaxCoverUnit);
        LoadSizeWithUnit_MB_GB(Config.ThumbCacheMaxBytes, numThumbCacheMax, selectThumbCacheUnit);

        // Theme selector - 簡化 Items 避免轉型錯誤
        selectTheme.Items.Clear();
        selectTheme.Items.Add(LocalizationManager.GetString("ThemeLight"));
        selectTheme.Items.Add(LocalizationManager.GetString("ThemeDark"));
        selectTheme.Items.Add(LocalizationManager.GetString("ThemeSystem"));
        selectTheme.SelectedIndex = (int)Prefs.Theme;

        // Language selector
        selectLang.Items.Clear();
        selectLang.Items.Add("繁體中文");
        selectLang.Items.Add("English");
        selectLang.SelectedIndex = Prefs.Language == "zh-TW" ? 0 : 1;

        // Reader: 內建 reader / 閱讀方向 / 雙頁封面獨立
        cbUseBuiltInReader.Checked = Prefs.UseBuiltInReader;
        selectReadingDirection.Items.Clear();
        selectReadingDirection.Items.Add(FmtRtl());
        selectReadingDirection.Items.Add(FmtLtr());
        selectReadingDirection.SelectedIndex = Prefs.DefaultReadingDirection == "ltr" ? 1 : 0;
        cbDualPageSpreadFromCover.Checked = Prefs.DualPageSpreadFromCover;

        // 外部 metadata (#52)
        swEnableExternal.Checked = Config.EnableExternalMetadata;
        txtExtKey.Text = "";   // 不顯示真實 key
        var hasKey = !string.IsNullOrEmpty(Config.GetNhentaiApiKey());
        txtExtKey.PlaceholderText = hasKey
            ? (LocalizationManager.GetString("ExtKeyPlaceholderSet") ?? "(已儲存 — 留空保持原值)")
            : (LocalizationManager.GetString("ExtKeyPlaceholderEmpty") ?? "nhk_...");

        // 翻譯庫線上更新 (#53 Phase 5-C)
        swAutoCheckTrDb.Checked = Config.AutoCheckTranslationDbUpdate;
    }

    private void WireEvents()
    {
        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnBrowseViewer.Click += (s, e) => BrowseFile(txtImageViewer, "執行檔 (*.exe)|*.exe|所有檔案 (*.*)|*.*");
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        // 左側選單切換 sub-panel
        btnMenuGeneral.Click += (s, e) => ShowSection(SettingsSection.General);
        btnMenuLibrary.Click += (s, e) => ShowSection(SettingsSection.Library);
        btnMenuReader.Click += (s, e) => ShowSection(SettingsSection.Reader);
        btnMenuExternal.Click += (s, e) => ShowSection(SettingsSection.External);

        btnTestConnection.Click += async (s, e) => await OnTestConnection();

        selectTheme.SelectedValueChanged += (s, e) =>
        {
            if (selectTheme.SelectedIndex >= 0)
            {
                ThemeManager.CurrentTheme = (AppTheme)selectTheme.SelectedIndex;
            }
        };

        selectLang.SelectedValueChanged += (s, e) =>
        {
            if (selectLang.SelectedIndex >= 0)
            {
                string lang = selectLang.SelectedIndex == 0 ? "zh-TW" : "en-US";
                LocalizationManager.SetLanguage(lang);
            }
        };
    }

    private enum SettingsSection { General, Library, Reader, External }

    private void ShowSection(SettingsSection section)
    {
        generalPanel.Visible = section == SettingsSection.General;
        libraryPanel.Visible = section == SettingsSection.Library;
        readerPanel.Visible = section == SettingsSection.Reader;
        externalPanel.Visible = section == SettingsSection.External;
        // 選中的按鈕用 Primary 樣式，未選中用 Default
        btnMenuGeneral.Type  = section == SettingsSection.General  ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuLibrary.Type  = section == SettingsSection.Library  ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuReader.Type   = section == SettingsSection.Reader   ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuExternal.Type = section == SettingsSection.External ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
    }

    private async Task OnTestConnection()
    {
        var inputKey = txtExtKey.Text?.Trim();
        var keyToUse = !string.IsNullOrEmpty(inputKey) ? inputKey : Config.GetNhentaiApiKey();
        if (string.IsNullOrEmpty(keyToUse))
        {
            lblExtStatus.Text = LocalizationManager.GetString("ExtStatusNoKey") ?? "尚未設定 API key";
            return;
        }

        btnTestConnection.Loading = true;
        lblExtStatus.Text = LocalizationManager.GetString("ExtStatusTesting") ?? "測試中…";
        try
        {
            using var client = new NhentaiApiClient(keyToUse);
            var result = await client.TestConnectionAsync();
            if (IsDisposed) return;
            lblExtStatus.Text = result.Success
                ? string.Format(LocalizationManager.GetString("ExtStatusOkFmt") ?? "✓ 連線成功（user: {0}）", result.UserName ?? "?")
                : string.Format(LocalizationManager.GetString("ExtStatusFailFmt") ?? "✗ 連線失敗：{0}", result.ErrorMessage ?? "unknown");
        }
        catch (Exception ex)
        {
            lblExtStatus.Text = string.Format(
                LocalizationManager.GetString("ExtStatusFailFmt") ?? "✗ 連線失敗：{0}", ex.Message);
        }
        finally
        {
            btnTestConnection.Loading = false;
        }
    }

    public void ApplyLocalization()
    {
        windowBar.Text = LocalizationManager.GetString("SettingsTitle");
        label1.Text = LocalizationManager.GetString("SettingsSourcePath");
        label2.Text = LocalizationManager.GetString("SettingsTargetPath");
        label3.Text = LocalizationManager.GetString("SettingsImageViewer");
        labelTheme.Text = LocalizationManager.GetString("ThemeLabel") ?? "主題";
        labelLang.Text = LocalizationManager.GetString("LanguageLabel") ?? "語言";
        btnSave.Text = LocalizationManager.GetString("SettingsSave");
        btnCancel.Text = LocalizationManager.GetString("SettingsCancel");
        btnBrowseSource.Text = btnBrowseTarget.Text = btnBrowseViewer.Text = LocalizationManager.GetString("Browse") ?? "瀏覽";

        // 左側選單分類
        btnMenuGeneral.Text  = LocalizationManager.GetString("SettingsMenuGeneral")  ?? "一般設定";
        btnMenuLibrary.Text  = LocalizationManager.GetString("SettingsMenuLibrary")  ?? "漫畫庫管理";
        btnMenuReader.Text   = LocalizationManager.GetString("SettingsMenuReader")   ?? "閱讀器";
        btnMenuExternal.Text = LocalizationManager.GetString("SettingsMenuExternal") ?? "外部資料來源";

        // 外部 metadata 分頁
        lblExtTitle.Text = LocalizationManager.GetString("ExtTitle") ?? "nhentai API";
        lblExtDesc.Text  = LocalizationManager.GetString("ExtDesc") ?? "啟用後可從 nhentai API 拉取作品 metadata。API key 用 Windows DPAPI 加密儲存。";
        lblExtEnable.Text = LocalizationManager.GetString("ExtEnableLabel") ?? "啟用：";
        lblExtKey.Text   = LocalizationManager.GetString("ExtKeyLabel") ?? "API Key:";
        btnTestConnection.Text = LocalizationManager.GetString("ExtTestConnection") ?? "測試連線";

        // 翻譯庫子區塊（#53 Phase 5-C）
        lblTrDbTitle.Text = LocalizationManager.GetString("SettingsTrDbTitle") ?? "翻譯庫線上更新";
        lblTrDbDesc.Text = LocalizationManager.GetString("SettingsTrDbDesc")
            ?? "啟動時自動檢查 EhTag 翻譯庫是否有新版（每天最多檢查一次）。關掉只影響背景檢查；工具頁的「立即檢查更新」按鈕仍可用。";
        lblTrDbAutoCheck.Text = LocalizationManager.GetString("SettingsTrDbAutoCheckLabel") ?? "自動檢查：";

        // 閱讀器分頁
        cbUseBuiltInReader.Text = LocalizationManager.GetString("SettingsUseBuiltInReader") ?? "使用內建閱讀器（取代外部 viewer）";
        labelReadingDirection.Text = LocalizationManager.GetString("SettingsReadingDirection") ?? "閱讀方向";
        cbDualPageSpreadFromCover.Text = LocalizationManager.GetString("SettingsDualPageSpreadFromCover") ?? "雙頁模式封面獨立顯示";

        // selectReadingDirection 內容受語言影響 — 重新填
        var rtlIdx = selectReadingDirection.SelectedIndex;
        selectReadingDirection.Items.Clear();
        selectReadingDirection.Items.Add(FmtRtl());
        selectReadingDirection.Items.Add(FmtLtr());
        if (rtlIdx >= 0) selectReadingDirection.SelectedIndex = rtlIdx;

        // 漫畫庫管理 - 搜尋層數 + 封面大小過濾
        labelMaxScanDepth.Text = LocalizationManager.GetString("SettingsMaxScanDepth") ?? "搜尋層數限制 (封面遞迴)";
        labelCoverSizeRange.Text = LocalizationManager.GetString("SettingsCoverSizeRange") ?? "封面取樣檔案大小範圍";
        labelThumbCacheMax.Text = LocalizationManager.GetString("SettingsThumbCacheMax") ?? "縮圖快取上限 (超過會 LRU 淘汰最舊)";
    }

    /// <summary>
    /// 把 bytes 轉成最易讀的 KB 或 MB 顯示。≥ 1MB 顯示 MB，否則 KB。
    /// </summary>
    private static void LoadSizeWithUnit(long bytes, AntdUI.InputNumber num, AntdUI.Select unit)
    {
        const long OneMB = 1024 * 1024;
        if (bytes >= OneMB && bytes % OneMB == 0)
        {
            unit.SelectedIndex = 1;        // MB
            num.Value = bytes / OneMB;
        }
        else if (bytes >= OneMB)
        {
            unit.SelectedIndex = 1;        // MB (有餘數仍用 MB 但會 round)
            num.Value = bytes / OneMB;
        }
        else
        {
            unit.SelectedIndex = 0;        // KB
            num.Value = bytes / 1024;
        }
    }

    private static long SaveSizeWithUnit(AntdUI.InputNumber num, AntdUI.Select unit)
    {
        long multiplier = unit.SelectedIndex == 1 ? 1024L * 1024 : 1024L;
        return (long)num.Value * multiplier;
    }

    /// <summary>同上但單位用 MB / GB（縮圖快取上限通常以 GB 為單位）。</summary>
    private static void LoadSizeWithUnit_MB_GB(long bytes, AntdUI.InputNumber num, AntdUI.Select unit)
    {
        const long OneGB = 1024L * 1024 * 1024;
        const long OneMB = 1024L * 1024;
        if (bytes >= OneGB && bytes % OneGB == 0)
        {
            unit.SelectedIndex = 1;        // GB
            num.Value = bytes / OneGB;
        }
        else
        {
            unit.SelectedIndex = 0;        // MB
            num.Value = bytes / OneMB;
        }
    }

    private static long SaveSizeWithUnit_MB_GB(AntdUI.InputNumber num, AntdUI.Select unit)
    {
        long multiplier = unit.SelectedIndex == 1 ? 1024L * 1024 * 1024 : 1024L * 1024;
        return (long)num.Value * multiplier;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        DraggableMouseDown();
        base.OnMouseDown(e);
    }

    private void BrowseFolder(AntdUI.Input input)
    {
        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
        {
            if (!string.IsNullOrEmpty(folderBrowserDialog.DirectoryPath))
            {
                input.Text = folderBrowserDialog.DirectoryPath;
            }
        }
    }

    private void BrowseFile(AntdUI.Input input, string filter)
    {
        using var ofd = new OpenFileDialog { Filter = filter };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            input.Text = ofd.FileName;
        }
    }

    private void SaveAndClose()
    {
        Config.UnorganizedDirectory = txtSource.Text;
        Config.RootDirectory = txtTarget.Text;
        Config.ImageViewerPath = txtImageViewer.Text;
        Config.ImageViewerArgs = string.IsNullOrWhiteSpace(txtImageViewerArgs.Text) ? "\"{path}\"" : txtImageViewerArgs.Text;
        Config.DebugMode = cbDebug.Checked;
        Prefs.TrackReadingRecord = cbTrackReading.Checked;
        Config.MaxScanDepth = (int)numMaxScanDepth.Value;
        Config.MinCoverFileSizeBytes = SaveSizeWithUnit(numMinCoverSize, selectMinCoverUnit);
        Config.MaxCoverFileSizeBytes = SaveSizeWithUnit(numMaxCoverSize, selectMaxCoverUnit);
        Config.ThumbCacheMaxBytes = SaveSizeWithUnit_MB_GB(numThumbCacheMax, selectThumbCacheUnit);

        // Reader 偏好
        Prefs.UseBuiltInReader = cbUseBuiltInReader.Checked;
        Prefs.DefaultReadingDirection = selectReadingDirection.SelectedIndex == 1 ? "ltr" : "rtl";
        Prefs.DualPageSpreadFromCover = cbDualPageSpreadFromCover.Checked;

        // 外部 metadata (#52)
        Config.EnableExternalMetadata = swEnableExternal.Checked;
        var newKey = txtExtKey.Text?.Trim() ?? "";
        // 空字串 = 不變動（避免使用者沒輸入就 Save 把舊 key 清掉）
        if (!string.IsNullOrEmpty(newKey))
            Config.SetNhentaiApiKey(newKey);

        // 翻譯庫線上更新 (#53 Phase 5-C)
        Config.AutoCheckTranslationDbUpdate = swAutoCheckTrDb.Checked;

        // Theme + Language 寫到 preferences.json（與 appsettings.json 分檔）
        if (selectTheme.SelectedIndex >= 0)
            Prefs.Theme = (AppTheme)selectTheme.SelectedIndex;

        if (selectLang.SelectedIndex >= 0)
            Prefs.Language = selectLang.SelectedIndex == 0 ? "zh-TW" : "en-US";

        _configManager.Save(Config);
        _prefsManager.Save(Prefs);
        // 訊息留給 caller (MainForm.OpenSettings) 顯示，否則 Close() 後 message 隨 modal 消失看不到
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnSave_Click(object sender, EventArgs e)
    {
        SaveAndClose();
    }
}

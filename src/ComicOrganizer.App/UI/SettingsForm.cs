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

    // 翻譯服務商預設（OpenAI 相容端點 base；服務會自動補 /v1/chat/completions）。第 0 項=自訂/本地，不覆寫端點。
    private static readonly (string Name, string? Base)[] TrProviders =
    {
        ("自訂 / 本地 (LM Studio・Ollama)", null),
        ("OpenAI", "https://api.openai.com"),
        ("OpenRouter", "https://openrouter.ai/api"),
        ("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai"),
    };

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

        // AI 翻譯文字色與背景
        lblTrTitle.ForeColor = Core.ThemeColors.Text;
        lblTrDesc.ForeColor = Core.ThemeColors.Text;
        lblTrApiType.ForeColor = Core.ThemeColors.Text;
        lblTrEndpoint.ForeColor = Core.ThemeColors.Text;
        lblTrModel.ForeColor = Core.ThemeColors.Text;
        lblTrProvider.ForeColor = Core.ThemeColors.Text;
        lblTrApiKey.ForeColor = Core.ThemeColors.Text;
        lblTrStatus.ForeColor = Core.ThemeColors.Text;
        lblPreciseErase.ForeColor = Core.ThemeColors.Text;
        lblPreciseEraseHint.ForeColor = Core.ThemeColors.Secondary;
        btnTestTrConnection.DefaultBack = Core.ThemeColors.Primary;
        labelOcrEngine.ForeColor = Core.ThemeColors.Text;

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
        cbDualPageSpreadFromCover.Checked = Prefs.DualPageSpreadFromCover;
        cbUseGpuRendering.Checked = Config.UseGpuRendering;

        // OCR 引擎載入
        selectOcrEngine.Items.Clear();
        selectOcrEngine.Items.Add("Windows Native OCR");
        selectOcrEngine.Items.Add("PaddleOCR (日文漫畫推薦)");
        selectOcrEngine.SelectedIndex = Math.Clamp(Config.OcrEngineType, 0, 1);

        // 外部 metadata (#52)
        swEnableExternal.Checked = Config.EnableExternalMetadata;
        txtExtKey.Text = "";   // 不顯示真實 key
        var hasKey = !string.IsNullOrEmpty(Config.GetNhentaiApiKey());
        txtExtKey.PlaceholderText = hasKey
            ? (LocalizationManager.GetString("ExtKeyPlaceholderSet") ?? "(已儲存 — 留空保持原值)")
            : (LocalizationManager.GetString("ExtKeyPlaceholderEmpty") ?? "nhk_...");

        // 翻譯庫線上更新 (#53 Phase 5-C)
        swAutoCheckTrDb.Checked = Config.AutoCheckTranslationDbUpdate;

        // AI 即時翻譯載入
        selectTrApiType.Items.Clear();
        selectTrApiType.Items.Add("OpenAI 相容 (LM-Studio / OpenAI)");
        selectTrApiType.Items.Add("Ollama 原生 API");
        selectTrApiType.SelectedIndex = Math.Clamp(Config.TranslationApiType, 0, 1);
        txtTrEndpoint.Text = Config.TranslationEndpoint;
        txtTrModel.Text = Config.TranslationModelName;
        numTrConcurrency.Value = Math.Clamp(Config.TranslationMaxConcurrency, 1, 100);

        // 服務商預設下拉：依已存端點自動選中對應項，否則「自訂/本地」
        selectTrProvider.Items.Clear();
        foreach (var p in TrProviders) selectTrProvider.Items.Add(p.Name);
        int provIdx = 0;
        for (int i = 1; i < TrProviders.Length; i++)
            if (string.Equals(Config.TranslationEndpoint?.TrimEnd('/'), TrProviders[i].Base, StringComparison.OrdinalIgnoreCase)) { provIdx = i; break; }
        selectTrProvider.SelectedIndex = provIdx;

        // API 金鑰（不顯示真實值；留空=不變）
        txtTrApiKey.Text = "";
        var hasTrKey = !string.IsNullOrEmpty(Config.GetTranslationApiKey());
        txtTrApiKey.PlaceholderText = hasTrKey ? "(已儲存 — 留空保持原值)" : "本地端免填；雲端貼上金鑰 (sk-... / key)";

        // 精準原文抹除（route-2 ComicTextDetector）：MaskEngineType 2=啟用、其餘=關。
        // 此處程式化設值在 WireEvents 訂閱之前 → 不會誤觸發下載。
        swPreciseErase.Checked = Config.MaskEngineType == 2;
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
        btnMenuTranslation.Click += (s, e) => ShowSection(SettingsSection.Translation);

        btnTestConnection.Click += async (s, e) => await OnTestConnection();
        btnTestTrConnection.Click += async (s, e) => await OnTestTrConnection();

        // 勾選「精準原文抹除」且模型缺檔 → 觸發按需下載；取消/失敗則退回關閉
        swPreciseErase.CheckedChanged += async (s, e) => await OnPreciseEraseToggled();

        // 選服務商預設 → 自動填端點（第 0 項「自訂/本地」不覆寫）
        selectTrProvider.SelectedValueChanged += (s, e) =>
        {
            int i = selectTrProvider.SelectedIndex;
            if (i > 0 && i < TrProviders.Length && TrProviders[i].Base is string baseUrl)
                txtTrEndpoint.Text = baseUrl;
        };

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

    /// <summary>
    /// 啟用「精準原文抹除」(route-2 ComicTextDetector) 時確保 comic-text-detector.onnx 存在：
    /// 缺檔則跳下載對話框（~95MB，HuggingFace，僅個人用）。取消/失敗 → 退回關閉、不啟用。關閉開關時不需動作。
    /// </summary>
    private Task OnPreciseEraseToggled()
    {
        if (!swPreciseErase.Checked) return Task.CompletedTask;                  // 關 → 存檔寫 MaskEngineType=0
        if (File.Exists(ModelDownloader.ComicTextDetectorPath)) return Task.CompletedTask;  // 已在 → 直接啟用

        using var dlg = new ModelDownloadForm();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            swPreciseErase.Checked = false;   // 取消/失敗 → 退回（再觸發一次本方法，Checked=false 即早退）
        return Task.CompletedTask;
    }

    private enum SettingsSection { General, Library, Reader, External, Translation }

    private void ShowSection(SettingsSection section)
    {
        generalPanel.Visible = section == SettingsSection.General;
        libraryPanel.Visible = section == SettingsSection.Library;
        readerPanel.Visible = section == SettingsSection.Reader;
        externalPanel.Visible = section == SettingsSection.External;
        translationPanel.Visible = section == SettingsSection.Translation;
        // 選中的按鈕用 Primary 樣式，未選中用 Default
        btnMenuGeneral.Type     = section == SettingsSection.General     ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuLibrary.Type     = section == SettingsSection.Library     ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuReader.Type      = section == SettingsSection.Reader      ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuExternal.Type    = section == SettingsSection.External    ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuTranslation.Type = section == SettingsSection.Translation ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
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

    private async Task OnTestTrConnection()
    {
        var endpoint = txtTrEndpoint.Text?.Trim() ?? "";
        var modelName = txtTrModel.Text?.Trim() ?? "";
        var apiType = (LlmApiType)(selectTrApiType.SelectedIndex >= 0 ? selectTrApiType.SelectedIndex : 0);
        // 金鑰：優先用剛輸入的，否則用已儲存的（測試雲端端點需帶）
        var apiKey = txtTrApiKey.Text?.Trim();
        if (string.IsNullOrEmpty(apiKey)) apiKey = Config.GetTranslationApiKey();

        if (string.IsNullOrEmpty(endpoint))
        {
            lblTrStatus.Text = "端點 URL 不能為空";
            return;
        }

        btnTestTrConnection.Loading = true;
        lblTrStatus.Text = "正在測試連線與翻譯（這可能需要幾秒鐘）…";
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var translator = new LlmTranslationService(client);
            translator.Configure(endpoint, modelName, apiType, apiKey);

            var testInput = new List<string> { "こんにちは" };
            var result = await translator.TranslateBatchAsync(testInput);

            if (IsDisposed) return;

            if (result != null && result.Count > 0 && result[0] != testInput[0])
            {
                lblTrStatus.Text = $"✓ 連線與翻譯成功！\n  「{testInput[0]}」 ➔ 「{result[0]}」";
            }
            else
            {
                lblTrStatus.Text = $"✗ 連線成功但翻譯失敗：\n  回傳了原始文字，請檢查模型名稱是否有載入。\n  錯誤: {translator.LastError}";
            }
        }
        catch (Exception ex)
        {
            lblTrStatus.Text = $"✗ 連線失敗：{ex.Message}";
        }
        finally
        {
            btnTestTrConnection.Loading = false;
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
        labelOcrEngine.Text = LocalizationManager.GetString("SettingsOcrEngineLabel") ?? "OCR 引擎";

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

        // AI 即時翻譯分頁
        lblTrTitle.Text = LocalizationManager.GetString("TranslationTitle") ?? "AI 即時翻譯設定";
        lblTrDesc.Text = LocalizationManager.GetString("TranslationDesc") ?? "設定內建閱讀器即時翻譯功能所對接的本地端或雲端 LLM 服務。";
        lblTrApiType.Text = LocalizationManager.GetString("TranslationApiType") ?? "API 類型：";
        lblTrEndpoint.Text = LocalizationManager.GetString("TranslationEndpoint") ?? "API 端點：";
        lblTrModel.Text = LocalizationManager.GetString("TranslationModelNameLabel") ?? "模型名稱：";
        lblTrConcurrency.Text = LocalizationManager.GetString("TranslationMaxConcurrency") ?? "併發限制：";
        btnTestTrConnection.Text = LocalizationManager.GetString("TranslationTestConnection") ?? "測試 API 連線";
        btnMenuTranslation.Text = LocalizationManager.GetString("SettingsMenuTranslation") ?? "AI 即時翻譯";
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

        Prefs.UseBuiltInReader = cbUseBuiltInReader.Checked;
        Prefs.DefaultReadingDirection = selectReadingDirection.SelectedIndex == 1 ? "ltr" : "rtl";
        Prefs.DualPageSpreadFromCover = cbDualPageSpreadFromCover.Checked;
        Config.UseGpuRendering = cbUseGpuRendering.Checked;
        if (selectOcrEngine.SelectedIndex >= 0)
        {
            Config.OcrEngineType = selectOcrEngine.SelectedIndex;
        }

        // 外部 metadata (#52)
        Config.EnableExternalMetadata = swEnableExternal.Checked;
        var newKey = txtExtKey.Text?.Trim() ?? "";
        // 空字串 = 不變動（避免使用者沒輸入就 Save 把舊 key 清掉）
        if (!string.IsNullOrEmpty(newKey))
            Config.SetNhentaiApiKey(newKey);

        // 翻譯庫線上更新 (#53 Phase 5-C)
        Config.AutoCheckTranslationDbUpdate = swAutoCheckTrDb.Checked;

        // 精準原文抹除：開=ComicTextDetector(2)、關=逐欄白底(0)
        Config.MaskEngineType = swPreciseErase.Checked ? 2 : 0;

        // AI 即時翻譯儲存
        Config.TranslationEndpoint = txtTrEndpoint.Text?.Trim() ?? "";
        Config.TranslationModelName = txtTrModel.Text?.Trim() ?? "";
        Config.TranslationApiType = selectTrApiType.SelectedIndex >= 0 ? selectTrApiType.SelectedIndex : 0;
        Config.TranslationMaxConcurrency = Math.Clamp((int)numTrConcurrency.Value, 1, 100);

        // API 金鑰：留空=不變（避免沒輸入就 Save 把舊金鑰清掉）
        var trKey = txtTrApiKey.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(trKey)) Config.SetTranslationApiKey(trKey);

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

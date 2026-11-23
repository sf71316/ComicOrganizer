using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.Views.Controls;
using ComicOrganizer.VisualSearch;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed record SettingsOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class AccentColorOption : ObservableObject
{
    private static readonly IBrush UnselectedBorderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128));

    private readonly IBrush _selectedBackgroundBrush;
    private readonly Action<string> _onSelect;

    public AccentColorOption(string name, string displayName, Color previewColor, Action<string> onSelect)
    {
        Name = name;
        DisplayName = displayName;
        PreviewColor = previewColor;
        _onSelect = onSelect;
        SwatchBrush = new SolidColorBrush(previewColor);
        _selectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(36, previewColor.R, previewColor.G, previewColor.B));
    }

    public string Name { get; }

    public string DisplayName { get; }

    public Color PreviewColor { get; }

    public IBrush SwatchBrush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(CardBackgroundBrush))]
    private bool _isSelected;

    public IBrush CardBorderBrush => IsSelected ? SwatchBrush : UnselectedBorderBrush;

    public IBrush CardBackgroundBrush => IsSelected ? _selectedBackgroundBrush : Brushes.Transparent;

    [RelayCommand]
    private void Select() => _onSelect(Name);
}

public sealed partial class SettingsPageViewModel : ViewModelBase
{
    private static readonly IBrush StatusSuccessBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush StatusErrorBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StatusNeutralBrush = new SolidColorBrush(Color.Parse("#94A3B8"));

    private static readonly Dictionary<string, string> AccentDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blue"] = "海洋藍",
        ["Purple"] = "暮光紫",
        ["Rose"] = "玫瑰紅",
        ["Emerald"] = "翡翠綠",
        ["Amber"] = "琥珀金",
    };

    private static readonly IBrush WarnBgBrush = new SolidColorBrush(Color.FromArgb(24, 245, 158, 11));
    private static readonly IBrush WarnBorderBrush = new SolidColorBrush(Color.FromArgb(136, 245, 158, 11));
    private static readonly IBrush WarnTextBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush ReadyBgBrush = new SolidColorBrush(Color.FromArgb(24, 34, 197, 94));
    private static readonly IBrush ReadyBorderBrush = new SolidColorBrush(Color.FromArgb(120, 34, 197, 94));

    private readonly AppDataContext _dataContext;
    private readonly Func<Action, Task> _runOnUiAsync;
    private bool _isSynchronizing;

    public SettingsPageViewModel(AppDataContext dataContext, Func<Action, Task>? runOnUiAsync = null)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        LanguageOptions =
        [
            new SettingsOption<string>("zh-TW", "繁體中文 (zh-TW)"),
            new SettingsOption<string>("en-US", "English (en-US)"),
        ];

        ReadingDirectionOptions =
        [
            new SettingsOption<string>("rtl", "右 → 左（日式漫畫）"),
            new SettingsOption<string>("ltr", "左 → 右（一般 / 西式）"),
        ];

        ReaderViewModeOptions =
        [
            new SettingsOption<string>("SinglePage", "單頁模式"),
            new SettingsOption<string>("DualPage", "雙頁跨頁模式"),
            new SettingsOption<string>("ContinuousScroll", "垂直連續捲動"),
        ];

        ReaderZoomModeOptions =
        [
            new SettingsOption<string>("FitPage", "符合整頁"),
            new SettingsOption<string>("FitWidth", "符合視窗寬度"),
            new SettingsOption<string>("Actual", "原始尺寸 (100%)"),
        ];

        OcrEngineOptions =
        [
            new SettingsOption<int>(0, "Windows 原生 OCR（系統內建免下載）"),
            new SettingsOption<int>(1, "PaddleOCR（外掛模型 PP-OCRv6）"),
        ];

        MaskEngineOptions =
        [
            new SettingsOption<int>(2, "ComicTextDetector ONNX（精準原文抹除）"),
            new SettingsOption<int>(0, "逐欄矩形白底遮罩"),
        ];

        TranslationApiTypeOptions =
        [
            new SettingsOption<int>(0, "OpenAI 相容（LM Studio / OpenAI / OpenRouter / Gemini）"),
            new SettingsOption<int>(1, "Ollama 原生 API"),
        ];

        AccentOptions = _dataContext.AvailableAccentNames
            .Select(name => new AccentColorOption(
                name,
                AccentDisplayNames.GetValueOrDefault(name, name),
                AppDataContext.GetAccentPreviewColor(name),
                SelectAccent))
            .ToArray();

        _selectedLanguageOption = LanguageOptions[0];
        _selectedReadingDirection = ReadingDirectionOptions[0];
        _selectedReaderViewMode = ReaderViewModeOptions[0];
        _selectedReaderZoomMode = ReaderZoomModeOptions[0];
        _selectedOcrEngine = OcrEngineOptions[0];
        _selectedMaskEngine = MaskEngineOptions[0];
        _selectedTranslationApiType = TranslationApiTypeOptions[0];
        _selectedAccentName = _dataContext.CurrentAccentName;

        _dataContext.ThemeChanged += OnDataContextThemeChanged;

        LoadFromDataContext();
    }

    /// <summary>
    /// 由 View 注入之資料夾選擇器委派：(對話框標題, 初始路徑) → 使用者選取的本機路徑（取消回 null）。
    /// </summary>
    public Func<string, string?, Task<string?>>? PickFolderAsync { get; set; }

    /// <summary>
    /// 由 View 注入之檔案選擇器委派：(對話框標題, 初始資料夾, 篩選說明, 副檔名清單) → 使用者選取的本機檔案路徑。
    /// </summary>
    public Func<string, string?, string, IReadOnlyList<string>, Task<string?>>? PickFileAsync { get; set; }

    public string DataDirectory => _dataContext.DataDirectory;

    public bool IsSharedDevDataDirectory => _dataContext.IsSharedDevDataDirectory;

    public string DataDirectoryBadgeText => IsSharedDevDataDirectory
        ? "開發共享資料目錄"
        : "應用程式本機目錄";

    public string DataDirectoryDescription => _dataContext.IsReadOnlyDatabase
        ? "目前以唯讀模式使用資料目錄（--read-only 或 COMIC_ORGANIZER_READONLY=1），不會寫入 cache.db；僅在按下「儲存設定」時才會寫入設定檔。"
        : IsSharedDevDataDirectory
            ? "目前與既有漫畫庫共用資料目錄（appsettings.json / preferences.json / cache.db / thumbs），已開啟寫入模式；異動會直接寫入共用 cache.db。"
            : "應用程式設定檔與快取索引皆存放於此目錄中。";

    public IReadOnlyList<AccentColorOption> AccentOptions { get; }

    public IReadOnlyList<SettingsOption<string>> LanguageOptions { get; }

    public IReadOnlyList<SettingsOption<string>> ReadingDirectionOptions { get; }

    public IReadOnlyList<SettingsOption<string>> ReaderViewModeOptions { get; }

    public IReadOnlyList<SettingsOption<string>> ReaderZoomModeOptions { get; }

    public IReadOnlyList<SettingsOption<int>> OcrEngineOptions { get; }

    public IReadOnlyList<SettingsOption<int>> MaskEngineOptions { get; }

    public IReadOnlyList<SettingsOption<int>> TranslationApiTypeOptions { get; }

    // ── 組 1：外觀與介面 ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _selectedAccentName;

    [ObservableProperty]
    private SettingsOption<string> _selectedLanguageOption;

    [ObservableProperty]
    private decimal? _galleryWheelScrollDip = ScrollWheelStepHelper.DefaultWheelScrollDip;

    // ── 組 2：路徑與共用資料目錄 ────────────────────────────────────────

    [ObservableProperty]
    private string _unorganizedDirectory = string.Empty;

    [ObservableProperty]
    private string _rootDirectory = string.Empty;

    // ── 組 3：歸檔比對與快取設定 ────────────────────────────────────────

    [ObservableProperty]
    private decimal? _maxScanDepth = 3;

    [ObservableProperty]
    private decimal? _thumbCacheMaxMB = 1024;

    [ObservableProperty]
    private bool _debugMode;

    [ObservableProperty]
    private string _blacklistText = string.Empty;

    [ObservableProperty]
    private string _blacklistPatternsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlacklistPatternsError))]
    private string? _blacklistPatternsError;

    public bool HasBlacklistPatternsError => !string.IsNullOrWhiteSpace(BlacklistPatternsError);

    // ── 組 4：內建閱讀器與翻譯（OCR / LLM） ─────────────────────────────

    [ObservableProperty]
    private bool _useBuiltInReader;

    [ObservableProperty]
    private SettingsOption<string> _selectedReadingDirection;

    [ObservableProperty]
    private SettingsOption<string> _selectedReaderViewMode;

    [ObservableProperty]
    private SettingsOption<string> _selectedReaderZoomMode;

    [ObservableProperty]
    private bool _dualPageSpreadFromCover;

    [ObservableProperty]
    private bool _trackReadingRecord = true;

    [ObservableProperty]
    private bool _useGpuRendering;

    [ObservableProperty]
    private SettingsOption<int> _selectedOcrEngine;

    [ObservableProperty]
    private SettingsOption<int> _selectedMaskEngine;

    [ObservableProperty]
    private SettingsOption<int> _selectedTranslationApiType;

    [ObservableProperty]
    private string _translationEndpoint = string.Empty;

    [ObservableProperty]
    private string _translationModelName = string.Empty;

    [ObservableProperty]
    private decimal? _translationMaxConcurrency = 1;

    [ObservableProperty]
    private string _translationApiKey = string.Empty;

    // ── 組 5：外部 Metadata 同步（nhentai） ─────────────────────────────

    [ObservableProperty]
    private bool _enableExternalMetadata;

    [ObservableProperty]
    private bool _autoCheckTranslationDbUpdate = true;

    [ObservableProperty]
    private string _nhentaiApiKey = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestNhentaiConnectionCommand))]
    [NotifyPropertyChangedFor(nameof(TestNhentaiButtonText))]
    private bool _isTestingNhentaiConnection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNhentaiConnectionStatus))]
    private string _nhentaiConnectionStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NhentaiConnectionStatusBrush))]
    private bool? _nhentaiConnectionSucceeded;

    public bool HasNhentaiConnectionStatus => !string.IsNullOrWhiteSpace(NhentaiConnectionStatus);

    public string TestNhentaiButtonText => IsTestingNhentaiConnection ? "測試中…" : "測試連線";

    public IBrush NhentaiConnectionStatusBrush => NhentaiConnectionSucceeded switch
    {
        true => StatusSuccessBrush,
        false => StatusErrorBrush,
        _ => StatusNeutralBrush,
    };

    public event Action? OpenVisualSearchRequested;

    // ── 組 6：圖搜漫（額外選配 ONNX 引擎） ─────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVisualIndexGuideCard))]
    private bool _enableVisualSearch;

    [ObservableProperty]
    private bool _visualSearchEnableMultiCropRerank = true;

    [ObservableProperty]
    private bool _visualSearchAutoIndexAfterArchive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisualIndexGoButtonText))]
    private bool _visualSearchEnableHeadIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisualIndexGoButtonText))]
    private int _visualIndexPendingHeadBackfillCount;

    [ObservableProperty]
    private string _visualSearchPanelModelUrl = string.Empty;

    [ObservableProperty]
    private string _visualSearchEmbedModelUrl = string.Empty;

    [ObservableProperty]
    private string _visualSearchHeadModelUrl = string.Empty;

    [ObservableProperty]
    private string _panelModelStatusText = "尚未安裝";

    [ObservableProperty]
    private string _embedModelStatusText = "尚未安裝";

    [ObservableProperty]
    private string _headModelStatusText = "尚未安裝（選配）";

    [ObservableProperty]
    private bool _isPanelModelReady;

    [ObservableProperty]
    private bool _isEmbedModelReady;

    [ObservableProperty]
    private bool _isHeadModelReady;

    [ObservableProperty]
    private bool _isDownloadingVisualModel;

    [ObservableProperty]
    private string _visualModelDownloadProgressText = string.Empty;

    [ObservableProperty]
    private double _visualModelDownloadPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisualIndexGuideBgBrush))]
    [NotifyPropertyChangedFor(nameof(VisualIndexGuideBorderBrush))]
    [NotifyPropertyChangedFor(nameof(VisualIndexGuideTitleBrush))]
    [NotifyPropertyChangedFor(nameof(VisualIndexGoButtonText))]
    private bool _visualIndexReady;

    [ObservableProperty]
    private string _visualIndexGuideTitle = "正在檢查圖搜漫索引資料庫…";

    [ObservableProperty]
    private string _visualIndexGuideDetail = string.Empty;

    [ObservableProperty]
    private string _visualIndexEstimatedDurationText = string.Empty;

    public string VisualSearchModelsDirectory => new VisualSearchPaths(_dataContext.DataDirectory).ModelsDirectory;

    public bool AreAllVisualModelsReady => IsPanelModelReady && IsEmbedModelReady;

    public bool ShowVisualIndexGuideCard => EnableVisualSearch;

    public string VisualIndexGoButtonText => !VisualIndexReady
        ? "🚀 儲存設定並前往建立索引庫"
        : (VisualSearchEnableHeadIndex && VisualIndexPendingHeadBackfillCount > 0
            ? "🚀 儲存設定並前往補建頭像索引"
            : "🔎 儲存並前往「圖搜漫」頁面");

    public IBrush VisualIndexGuideBgBrush => VisualIndexReady ? ReadyBgBrush : WarnBgBrush;

    public IBrush VisualIndexGuideBorderBrush => VisualIndexReady ? ReadyBorderBrush : WarnBorderBrush;

    public IBrush VisualIndexGuideTitleBrush => VisualIndexReady ? StatusSuccessBrush : WarnTextBrush;

    public IBrush PanelModelStatusBrush => IsPanelModelReady ? StatusSuccessBrush : StatusNeutralBrush;

    public IBrush EmbedModelStatusBrush => IsEmbedModelReady ? StatusSuccessBrush : StatusNeutralBrush;

    public IBrush HeadModelStatusBrush => IsHeadModelReady ? StatusSuccessBrush : StatusNeutralBrush;

    partial void OnIsHeadModelReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(HeadModelStatusBrush));
    }

    partial void OnEnableVisualSearchChanged(bool value)
    {
        if (!_isSynchronizing && value)
        {
            _ = RefreshVisualSearchIndexStatusAsync();
        }
    }

    partial void OnVisualSearchEnableHeadIndexChanged(bool value)
    {
        if (!_isSynchronizing && EnableVisualSearch)
        {
            _ = RefreshVisualSearchIndexStatusAsync();
        }
    }

    partial void OnIsPanelModelReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisualModelsReady));
        OnPropertyChanged(nameof(PanelModelStatusBrush));
        if (!_isSynchronizing && EnableVisualSearch)
        {
            _ = RefreshVisualSearchIndexStatusAsync();
        }
    }

    partial void OnIsEmbedModelReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(AreAllVisualModelsReady));
        OnPropertyChanged(nameof(EmbedModelStatusBrush));
        if (!_isSynchronizing && EnableVisualSearch)
        {
            _ = RefreshVisualSearchIndexStatusAsync();
        }
    }

    // ── 屬性變更連動 ────────────────────────────────────────────────────

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (_isSynchronizing)
        {
            return;
        }

        _dataContext.ApplyTheme(value, SelectedAccentName);
    }

    partial void OnBlacklistPatternsTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(BlacklistPatternsError))
        {
            BlacklistPatternsError = null;
        }
    }

    private void OnDataContextThemeChanged()
    {
        if (_isSynchronizing)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            IsDarkTheme = _dataContext.IsDarkTheme;
            SelectedAccentName = _dataContext.CurrentAccentName;
            UpdateAccentSelection(SelectedAccentName);
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    // ── Commands ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectAccent(string? accentName)
    {
        if (string.IsNullOrWhiteSpace(accentName))
        {
            return;
        }

        SelectedAccentName = accentName;
        UpdateAccentSelection(accentName);

        if (!_isSynchronizing)
        {
            _dataContext.ApplyTheme(IsDarkTheme, accentName);
        }
    }

    [RelayCommand]
    private async Task BrowseUnorganizedDirectoryAsync()
    {
        if (PickFolderAsync is null)
        {
            return;
        }

        var selected = await PickFolderAsync("選擇未整理來源資料夾", UnorganizedDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            UnorganizedDirectory = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseRootDirectoryAsync()
    {
        if (PickFolderAsync is null)
        {
            return;
        }

        var selected = await PickFolderAsync("選擇漫畫庫根目錄", RootDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            RootDirectory = selected;
        }
    }

    private bool CanTestNhentaiConnection() => !IsTestingNhentaiConnection;

    [RelayCommand(CanExecute = nameof(CanTestNhentaiConnection))]
    private async Task TestNhentaiConnectionAsync()
    {
        var apiKey = NhentaiApiKey.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            NhentaiConnectionSucceeded = false;
            NhentaiConnectionStatus = "請先輸入 nhentai API Key 後再進行測試。";
            return;
        }

        IsTestingNhentaiConnection = true;
        NhentaiConnectionSucceeded = null;
        NhentaiConnectionStatus = "正在連線 nhentai API 驗證金鑰…";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new NhentaiApiClient(apiKey);
            var result = await client.TestConnectionAsync(cts.Token);

            if (result.Success)
            {
                NhentaiConnectionSucceeded = true;
                var userLabel = string.IsNullOrWhiteSpace(result.UserName) ? "已驗證" : result.UserName;
                NhentaiConnectionStatus = $"連線成功（帳號：{userLabel}）";
            }
            else
            {
                NhentaiConnectionSucceeded = false;
                NhentaiConnectionStatus = $"連線失敗：{result.ErrorMessage ?? "未知錯誤"}";
            }
        }
        catch (OperationCanceledException)
        {
            NhentaiConnectionSucceeded = false;
            NhentaiConnectionStatus = "連線逾時（超過 15 秒未回應），請檢查網路狀態。";
        }
        catch (Exception ex)
        {
            NhentaiConnectionSucceeded = false;
            NhentaiConnectionStatus = $"連線失敗：{ex.Message}";
        }
        finally
        {
            IsTestingNhentaiConnection = false;
        }
    }

    [RelayCommand]
    private void Reload()
    {
        _dataContext.ReloadSettings();
        LoadFromDataContext();
        ShowToast("已重新載入設定", "已放棄尚未儲存的修改並重新讀取設定檔。", NotificationType.Information, 3);
    }

    [RelayCommand]
    private void Save()
    {
        var blacklist = ParseDistinctLines(BlacklistText);
        var blacklistPatterns = ParseDistinctLines(BlacklistPatternsText);

        for (var i = 0; i < blacklistPatterns.Count; i++)
        {
            var pattern = blacklistPatterns[i];
            try
            {
                _ = new Regex(pattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                var errorMessage = $"第 {i + 1} 條正則規則「{pattern}」語法無效：{ex.Message}";
                BlacklistPatternsError = errorMessage;
                ShowToast("正則規則驗證失敗", errorMessage, NotificationType.Error, 5);
                return;
            }
        }

        BlacklistPatternsError = null;

        try
        {
            var updatedConfig = CloneObject(_dataContext.Config);
            var updatedPrefs = CloneObject(_dataContext.Preferences);

            // 組 1：外觀與介面
            updatedPrefs.Theme = IsDarkTheme ? AppTheme.Dark : AppTheme.Light;
            updatedPrefs.Language = SelectedLanguageOption?.Value ?? "zh-TW";
            int clampedWheelDip = ScrollWheelStepHelper.ClampStepDip(
                (int)Math.Round(GalleryWheelScrollDip ?? ScrollWheelStepHelper.DefaultWheelScrollDip));
            updatedPrefs.GalleryWheelScrollDip = clampedWheelDip;
            GalleryWheelScrollDip = clampedWheelDip;

            // 組 2：路徑
            updatedConfig.UnorganizedDirectory = UnorganizedDirectory.Trim();
            updatedConfig.RootDirectory = RootDirectory.Trim();

            // 組 3：歸檔比對與快取設定
            updatedConfig.MaxScanDepth = Math.Clamp((int)Math.Round(MaxScanDepth ?? 3m), 1, 10);
            var cacheMaxMb = Math.Clamp((long)Math.Round(ThumbCacheMaxMB ?? 1024m), 0L, 1048576L);
            updatedConfig.ThumbCacheMaxBytes = cacheMaxMb * 1024L * 1024L;
            updatedConfig.DebugMode = DebugMode;
            updatedConfig.Blacklist = blacklist;
            updatedConfig.BlacklistPatterns = blacklistPatterns;

            // 組 4：內建閱讀器與翻譯（OCR / LLM）
            updatedPrefs.UseBuiltInReader = UseBuiltInReader;
            updatedPrefs.DefaultReadingDirection = SelectedReadingDirection?.Value ?? "rtl";
            updatedPrefs.DefaultReaderViewMode = SelectedReaderViewMode?.Value ?? "SinglePage";
            updatedPrefs.DefaultReaderZoomMode = SelectedReaderZoomMode?.Value ?? "FitPage";
            updatedPrefs.DualPageSpreadFromCover = DualPageSpreadFromCover;
            updatedPrefs.TrackReadingRecord = TrackReadingRecord;
            updatedConfig.UseGpuRendering = UseGpuRendering;

            updatedConfig.OcrEngineType = SelectedOcrEngine?.Value ?? 0;
            updatedConfig.MaskEngineType = SelectedMaskEngine?.Value ?? 2;
            updatedConfig.TranslationApiType = SelectedTranslationApiType?.Value ?? 0;
            updatedConfig.TranslationEndpoint = TranslationEndpoint.Trim();
            updatedConfig.TranslationModelName = TranslationModelName.Trim();
            updatedConfig.TranslationMaxConcurrency = Math.Clamp((int)Math.Round(TranslationMaxConcurrency ?? 1m), 1, 32);
            updatedConfig.TranslationApiKeyProtected = ProtectApiKey(TranslationApiKey);

            // 組 5：外部 Metadata 同步（nhentai）
            updatedConfig.EnableExternalMetadata = EnableExternalMetadata;
            updatedConfig.AutoCheckTranslationDbUpdate = AutoCheckTranslationDbUpdate;
            updatedConfig.NhentaiApiKeyProtected = ProtectApiKey(NhentaiApiKey);

            // 組 6：圖搜漫（額外選配 ONNX 引擎）
            updatedConfig.EnableVisualSearch = EnableVisualSearch;
            updatedConfig.VisualSearchEnableMultiCropRerank = VisualSearchEnableMultiCropRerank;
            updatedConfig.VisualSearchAutoIndexAfterArchive = VisualSearchAutoIndexAfterArchive;
            updatedConfig.VisualSearchEnableHeadIndex = VisualSearchEnableHeadIndex;
            updatedConfig.VisualSearchPanelModelUrl = VisualSearchPanelModelUrl.Trim();
            updatedConfig.VisualSearchEmbedModelUrl = VisualSearchEmbedModelUrl.Trim();
            updatedConfig.VisualSearchHeadModelUrl = VisualSearchHeadModelUrl.Trim();

            _dataContext.SaveConfig(updatedConfig);
            _dataContext.SavePreferences(updatedPrefs);
            _dataContext.ApplyTheme(IsDarkTheme, SelectedAccentName);

            // 正規化回填多行文字框（反映已去除的空白行）
            BlacklistText = string.Join(Environment.NewLine, blacklist);
            BlacklistPatternsText = string.Join(Environment.NewLine, blacklistPatterns);

            if (EnableVisualSearch)
            {
                var paths = new VisualSearchPaths(_dataContext.DataDirectory);
                bool modelsReady = paths.AreModelsReady();
                var snap = InspectVisualIndexAndEstimate(paths);
                ApplyVisualIndexSnapshot(snap, modelsReady);

                if (!modelsReady)
                {
                    ShowToast(
                        "設定已儲存（圖搜漫尚缺模型）",
                        "請先下載或匯入 2 個 ONNX 模型，再前往左側「🔎 圖搜漫」建立索引資料庫。",
                        NotificationType.Warning,
                        5);
                    return;
                }

                if (!VisualIndexReady)
                {
                    ShowToast(
                        "設定已儲存，請建立圖搜漫索引庫",
                        $"尚未建立圖搜漫索引（{VisualIndexEstimatedDurationText}），請前往左側「🔎 圖搜漫」開始建庫。",
                        NotificationType.Warning,
                        6);
                    return;
                }
            }

            ShowToast("設定已儲存", "所有變更已成功寫入 appsettings.json 與 preferences.json。", NotificationType.Success, 3);
        }
        catch (Exception ex)
        {
            ShowToast("儲存設定失敗", ex.Message, NotificationType.Error, 5);
        }
    }

    // ── 內部輔助 ────────────────────────────────────────────────────────

    private void LoadFromDataContext()
    {
        _isSynchronizing = true;
        try
        {
            var cfg = _dataContext.Config;
            var prefs = _dataContext.Preferences;

            // 組 1：外觀與介面
            IsDarkTheme = _dataContext.IsDarkTheme;
            SelectedAccentName = _dataContext.CurrentAccentName;
            UpdateAccentSelection(SelectedAccentName);
            SelectedLanguageOption = FindOption(LanguageOptions, prefs.Language, StringComparer.OrdinalIgnoreCase);
            GalleryWheelScrollDip = ScrollWheelStepHelper.ClampOrDefault(prefs.GalleryWheelScrollDip);

            // 組 2：路徑
            UnorganizedDirectory = cfg.UnorganizedDirectory ?? string.Empty;
            RootDirectory = cfg.RootDirectory ?? string.Empty;

            // 組 3：歸檔比對與快取設定
            MaxScanDepth = Math.Clamp(cfg.MaxScanDepth, 1, 10);
            ThumbCacheMaxMB = cfg.ThumbCacheMaxBytes <= 0
                ? 0m
                : Math.Max(1L, cfg.ThumbCacheMaxBytes / (1024L * 1024L));
            DebugMode = cfg.DebugMode;
            BlacklistText = string.Join(Environment.NewLine, cfg.Blacklist ?? []);
            BlacklistPatternsText = string.Join(Environment.NewLine, cfg.BlacklistPatterns ?? []);
            BlacklistPatternsError = null;

            // 組 4：內建閱讀器與翻譯
            UseBuiltInReader = prefs.UseBuiltInReader;
            SelectedReadingDirection = FindOption(ReadingDirectionOptions, prefs.DefaultReadingDirection, StringComparer.OrdinalIgnoreCase);
            SelectedReaderViewMode = FindOption(ReaderViewModeOptions, prefs.DefaultReaderViewMode, StringComparer.OrdinalIgnoreCase);
            SelectedReaderZoomMode = FindOption(ReaderZoomModeOptions, prefs.DefaultReaderZoomMode, StringComparer.OrdinalIgnoreCase);
            DualPageSpreadFromCover = prefs.DualPageSpreadFromCover;
            TrackReadingRecord = prefs.TrackReadingRecord;
            UseGpuRendering = cfg.UseGpuRendering;

            SelectedOcrEngine = FindOption(OcrEngineOptions, cfg.OcrEngineType, EqualityComparer<int>.Default);
            SelectedMaskEngine = FindOption(MaskEngineOptions, cfg.MaskEngineType == 2 ? 2 : 0, EqualityComparer<int>.Default);
            SelectedTranslationApiType = FindOption(TranslationApiTypeOptions, cfg.TranslationApiType, EqualityComparer<int>.Default);
            TranslationEndpoint = cfg.TranslationEndpoint ?? string.Empty;
            TranslationModelName = cfg.TranslationModelName ?? string.Empty;
            TranslationMaxConcurrency = Math.Clamp(cfg.TranslationMaxConcurrency, 1, 32);
            TranslationApiKey = TryUnprotectApiKey(cfg.TranslationApiKeyProtected);

            // 組 5：外部 Metadata 同步（nhentai）
            EnableExternalMetadata = cfg.EnableExternalMetadata;
            AutoCheckTranslationDbUpdate = cfg.AutoCheckTranslationDbUpdate;
            NhentaiApiKey = TryUnprotectApiKey(cfg.NhentaiApiKeyProtected);
            NhentaiConnectionStatus = string.Empty;
            NhentaiConnectionSucceeded = null;

            // 組 6：圖搜漫
            EnableVisualSearch = cfg.EnableVisualSearch;
            VisualSearchEnableMultiCropRerank = cfg.VisualSearchEnableMultiCropRerank;
            VisualSearchAutoIndexAfterArchive = cfg.VisualSearchAutoIndexAfterArchive;
            VisualSearchEnableHeadIndex = cfg.VisualSearchEnableHeadIndex;
            VisualSearchPanelModelUrl = cfg.VisualSearchPanelModelUrl ?? string.Empty;
            VisualSearchEmbedModelUrl = cfg.VisualSearchEmbedModelUrl ?? string.Empty;
            VisualSearchHeadModelUrl = cfg.VisualSearchHeadModelUrl ?? string.Empty;
            RefreshVisualSearchModelStatus();
        }
        finally
        {
            _isSynchronizing = false;
        }

        if (EnableVisualSearch)
        {
            _ = RefreshVisualSearchIndexStatusAsync();
        }
    }

    [RelayCommand]
    public async Task RefreshVisualSearchIndexStatusAsync()
    {
        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        bool modelsReady = paths.AreModelsReady();

        var snapshot = await Task.Run(() => InspectVisualIndexAndEstimate(paths)).ConfigureAwait(false);

        await _runOnUiAsync(() => ApplyVisualIndexSnapshot(snapshot, modelsReady)).ConfigureAwait(false);
    }

    private void ApplyVisualIndexSnapshot(VisualIndexEstimateSnapshot snapshot, bool modelsReady)
    {
        VisualIndexEstimatedDurationText = snapshot.DurationText;
        VisualIndexReady = snapshot.IndexedMangas > 0;

        if (!modelsReady)
        {
            VisualIndexGuideTitle = "⚠️ 尚未備齊 ONNX 模型（完成後需建立索引庫）";
            VisualIndexGuideDetail =
                $"圖搜漫必須先建置索引庫才能進行截圖搜尋。請先安裝上方 2 個 ONNX 模型，" +
                $"之後再前往「🔎 圖搜漫」建庫；依目前書庫規模（{snapshot.ScopeSummary}），首次建庫預估約需 {snapshot.DurationText}、佔用約 {snapshot.EstimatedSizeText}（過程可隨時停止並續建）。";
        }
        else if (snapshot.IndexedMangas <= 0)
        {
            VisualIndexGuideTitle = "📦 尚未建立「圖搜漫」索引資料庫";
            VisualIndexGuideDetail =
                $"必須先建立視覺索引資料庫才能使用圖搜漫。依目前漫畫庫規模（{snapshot.ScopeSummary}），" +
                $"首次完整建庫預估約需 {snapshot.DurationText}、索引空間約 {snapshot.EstimatedSizeText}（實測 GPU 速率 ~78 頁/秒；建庫期間可隨時停止，已完成進度會保存並支援接續）。";
        }
        else
        {
            int pendingHeads = Math.Max(0, snapshot.IndexedMangas - snapshot.HeadIndexedMangas);
            VisualIndexPendingHeadBackfillCount = pendingHeads;
            string pendingHint = snapshot.EstimatedWorks > snapshot.IndexedMangas + 5
                ? $" 目前書庫估計約 {snapshot.EstimatedWorks:N0} 本，尚有約 {snapshot.EstimatedWorks - snapshot.IndexedMangas:N0} 本待增量補建（約需 {snapshot.IncrementalDurationText}）。"
                : string.Empty;
            string headHint = VisualSearchEnableHeadIndex && pendingHeads > 0
                ? (IsHeadModelReady
                    ? $" 已勾選頭像索引：尚有 {pendingHeads:N0} 本待增量補建角色頭像（不重算舊分鏡），請點右側按鈕儲存並前往補建。"
                    : $" 已勾選頭像索引，但尚未匯入 {VisualSearchPaths.HeadModelFileName}。")
                : string.Empty;
            VisualIndexGuideTitle = "✅ 圖搜漫索引資料庫已就緒";
            VisualIndexGuideDetail =
                $"已建庫 {snapshot.IndexedMangas:N0} 本 · {snapshot.IndexedPages:N0} 頁 · {snapshot.ActiveVectors:N0} 筆向量 · " +
                $"佔用 {FormatMbOrGb(snapshot.DiskBytes)}。{pendingHint}{headHint}";
        }
    }

    [RelayCommand]
    private void SaveAndGoToVisualSearch()
    {
        if (!EnableVisualSearch)
        {
            EnableVisualSearch = true;
        }
        Save();
        OpenVisualSearchRequested?.Invoke();
    }

    private sealed record VisualIndexEstimateSnapshot(
        int IndexedMangas,
        int IndexedPages,
        int ActiveVectors,
        int HeadIndexedMangas,
        long DiskBytes,
        int EstimatedWorks,
        long EstimatedPages,
        string ScopeSummary,
        string DurationText,
        string IncrementalDurationText,
        string EstimatedSizeText);

    private VisualIndexEstimateSnapshot InspectVisualIndexAndEstimate(VisualSearchPaths paths)
    {
        int indexedMangas = 0, indexedPages = 0, activeVecs = 0, headIndexedMangas = 0;
        long diskBytes = 0;

        if (File.Exists(paths.DatabasePath))
        {
            try
            {
                using var db = new VisualIndexDatabase(paths.DatabasePath, readOnly: true);
                var ov = db.GetOverview();
                indexedMangas = ov.MangaCount;
                indexedPages = ov.TotalPages;
                activeVecs = ov.ActiveVectorCount;
                headIndexedMangas = ov.HeadIndexedMangaCount;
                diskBytes = paths.GetTotalDiskUsageBytes();
            }
            catch
            {
            }
        }

        int totalWorks = 0;
        long knownPages = 0;
        int knownWorkCount = 0;
        try
        {
            var works = _dataContext.MetaStore.GetExploreCorpusSnapshot("zh-TW").Works;
            totalWorks = works.Count;
            foreach (var w in works)
            {
                if (w.PageCount is > 0)
                {
                    knownPages += w.PageCount.Value;
                    knownWorkCount++;
                }
            }
        }
        catch
        {
        }

        if (totalWorks == 0 && !string.IsNullOrWhiteSpace(_dataContext.Config.RootDirectory))
        {
            try
            {
                totalWorks = VisualComicReader.DiscoverComics(_dataContext.Config.RootDirectory).Count;
            }
            catch
            {
            }
        }

        double avgPages = knownWorkCount > 0 ? (double)knownPages / knownWorkCount : 68.0;
        long estTotalPages = totalWorks > 0
            ? knownPages + (long)Math.Round(Math.Max(0, totalWorks - knownWorkCount) * avgPages)
            : 0;

        const double pagesPerSec = 78.0; // M4 RTX 4080 DirectML 實測吞吐
        const double bytesPerPage = 4383.0; // M4 USearch Int8 + SQLite 實測均攤

        double estTotalSec = estTotalPages / pagesPerSec;
        int diffWorks = Math.Max(0, totalWorks - indexedMangas);
        double estIncSec = (diffWorks * avgPages) / pagesPerSec;
        long estBytes = (long)Math.Round(estTotalPages * bytesPerPage);

        string scopeSummary = totalWorks > 0
            ? $"約 {totalWorks:N0} 本 / {estTotalPages:N0} 頁"
            : "每 200 頁漫畫";
        string durationText = totalWorks > 0
            ? FormatDuration(estTotalSec)
            : "每本約 2.6 秒（每 1,000 本約 15 分鐘）";
        string sizeText = totalWorks > 0
            ? FormatMbOrGb(estBytes)
            : "每 1,000 本約 300 MB";

        return new VisualIndexEstimateSnapshot(
            indexedMangas,
            indexedPages,
            activeVecs,
            headIndexedMangas,
            diskBytes,
            totalWorks,
            estTotalPages,
            scopeSummary,
            durationText,
            FormatDuration(estIncSec),
            sizeText);
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds < 60) return "約 1 分鐘內";
        int totalMinutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
        if (totalMinutes < 60) return $"約 {totalMinutes} 分鐘";
        int hours = totalMinutes / 60;
        int mins = totalMinutes % 60;
        return mins > 0 ? $"約 {hours} 小時 {mins} 分鐘" : $"約 {hours} 小時";
    }

    private static string FormatMbOrGb(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024.0 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
    }

    [RelayCommand]
    public void RefreshVisualSearchModelStatus()
    {
        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        var statuses = paths.GetModelStatuses();
        var panel = statuses[0];
        var embed = statuses[1];
        var head = statuses[2];

        IsPanelModelReady = panel.Exists;
        PanelModelStatusText = panel.Exists
            ? $"✅ 已就緒（{panel.SizeBytes / (1024.0 * 1024.0):F1} MB）"
            : $"⬜ 尚未下載 / 匯入（預期 {panel.ExpectedSizeHint}）";
        if (panel.Exists) ApplyModelPath(VisualSearchPaths.PanelModelFileName, panel.FullPath, overwrite: false);

        IsEmbedModelReady = embed.Exists;
        EmbedModelStatusText = embed.Exists
            ? $"✅ 已就緒（{embed.SizeBytes / (1024.0 * 1024.0):F1} MB）"
            : $"⬜ 尚未下載 / 匯入（預期 {embed.ExpectedSizeHint}）";
        if (embed.Exists) ApplyModelPath(VisualSearchPaths.EmbedModelFileName, embed.FullPath, overwrite: false);

        IsHeadModelReady = head.Exists;
        HeadModelStatusText = head.Exists
            ? $"✅ 已就緒（{head.SizeBytes / (1024.0 * 1024.0):F1} MB）"
            : $"⬜ 選配未安裝（預期 {head.ExpectedSizeHint}）";
        if (head.Exists) ApplyModelPath(VisualSearchPaths.HeadModelFileName, head.FullPath, overwrite: false);
    }

    private void ApplyModelPath(string targetFileName, string path, bool overwrite)
    {
        if (string.Equals(targetFileName, VisualSearchPaths.PanelModelFileName, StringComparison.OrdinalIgnoreCase))
        {
            if (overwrite || string.IsNullOrWhiteSpace(VisualSearchPanelModelUrl))
                VisualSearchPanelModelUrl = path;
        }
        else if (string.Equals(targetFileName, VisualSearchPaths.EmbedModelFileName, StringComparison.OrdinalIgnoreCase))
        {
            if (overwrite || string.IsNullOrWhiteSpace(VisualSearchEmbedModelUrl))
                VisualSearchEmbedModelUrl = path;
        }
        else if (string.Equals(targetFileName, VisualSearchPaths.HeadModelFileName, StringComparison.OrdinalIgnoreCase))
        {
            if (overwrite || string.IsNullOrWhiteSpace(VisualSearchHeadModelUrl))
                VisualSearchHeadModelUrl = path;
        }
    }

    [RelayCommand]
    private async Task ImportPanelModelAsync()
        => await ImportVisualModelAsync("選擇分鏡偵測模型 (panel_detector.onnx)", VisualSearchPaths.PanelModelFileName);

    [RelayCommand]
    private async Task ImportEmbedModelAsync()
        => await ImportVisualModelAsync("選擇特徵提取模型 (sscd_disc_mixup.onnx)", VisualSearchPaths.EmbedModelFileName);

    [RelayCommand]
    private async Task ImportHeadModelAsync()
        => await ImportVisualModelAsync("選擇角色頭像偵測模型 (head_detector.onnx)", VisualSearchPaths.HeadModelFileName);

    [RelayCommand]
    private async Task DownloadPanelModelAsync()
        => await DownloadVisualModelAsync(VisualSearchPanelModelUrl, VisualSearchPaths.PanelModelFileName);

    [RelayCommand]
    private async Task DownloadEmbedModelAsync()
        => await DownloadVisualModelAsync(VisualSearchEmbedModelUrl, VisualSearchPaths.EmbedModelFileName);

    [RelayCommand]
    private async Task DownloadHeadModelAsync()
        => await DownloadVisualModelAsync(VisualSearchHeadModelUrl, VisualSearchPaths.HeadModelFileName);

    [RelayCommand]
    private void OpenVisualSearchModelsFolder()
    {
        var paths = new VisualSearchPaths(_dataContext.DataDirectory);
        paths.EnsureDirectories();
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{paths.ModelsDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast("無法開啟資料夾", ex.Message, NotificationType.Warning, 4);
        }
    }

    private async Task ImportVisualModelAsync(string title, string targetFileName)
    {
        if (PickFileAsync is null) return;

        var initialDir = Directory.Exists(VisualSearchModelsDirectory) ? VisualSearchModelsDirectory : _dataContext.DataDirectory;
        var selected = await PickFileAsync(title, initialDir, "ONNX 模型檔 (*.onnx)", ["*.onnx"]);
        if (string.IsNullOrWhiteSpace(selected)) return;

        try
        {
            var paths = new VisualSearchPaths(_dataContext.DataDirectory);
            paths.ImportLocalModel(selected, targetFileName);
            ApplyModelPath(targetFileName, selected, overwrite: true);
            RefreshVisualSearchModelStatus();
            _dataContext.NotifySettingsChanged();
            ShowToast("模型已匯入", $"{targetFileName} 已安裝至視覺搜尋模型目錄。", NotificationType.Success, 3);
        }
        catch (Exception ex)
        {
            ShowToast("匯入模型失敗", ex.Message, NotificationType.Error, 4);
        }
    }

    private async Task DownloadVisualModelAsync(string url, string targetFileName)
    {
        if (IsDownloadingVisualModel) return;
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowToast("請先輸入下載網址", $"請於左側欄位填入 {targetFileName} 的直連網址，或改用「匯入本機 .onnx」。", NotificationType.Warning, 4);
            return;
        }

        IsDownloadingVisualModel = true;
        VisualModelDownloadPercent = 0;
        VisualModelDownloadProgressText = $"正在下載 {targetFileName}…";

        try
        {
            var paths = new VisualSearchPaths(_dataContext.DataDirectory);
            var progress = new Progress<(long Downloaded, long? Total)>(p =>
            {
                double mb = p.Downloaded / (1024.0 * 1024.0);
                if (p.Total is > 0)
                {
                    double totalMb = p.Total.Value / (1024.0 * 1024.0);
                    VisualModelDownloadPercent = Math.Clamp(100.0 * p.Downloaded / p.Total.Value, 0, 100);
                    VisualModelDownloadProgressText = $"正在下載 {targetFileName}：{mb:F1} / {totalMb:F1} MB ({VisualModelDownloadPercent:F0}%)";
                }
                else
                {
                    VisualModelDownloadProgressText = $"正在下載 {targetFileName}：{mb:F1} MB";
                }
            });

            await paths.DownloadModelAsync(url.Trim(), targetFileName, progress);
            RefreshVisualSearchModelStatus();
            _dataContext.NotifySettingsChanged();
            VisualModelDownloadPercent = 100;
            VisualModelDownloadProgressText = $"✅ {targetFileName} 下載完成";
            ShowToast("下載完成", $"{targetFileName} 已安裝就緒。", NotificationType.Success, 3);
        }
        catch (Exception ex)
        {
            VisualModelDownloadProgressText = $"⚠️ 下載失敗：{ex.Message}";
            ShowToast("下載模型失敗", ex.Message, NotificationType.Error, 5);
        }
        finally
        {
            IsDownloadingVisualModel = false;
        }
    }

    private void UpdateAccentSelection(string activeName)
    {
        foreach (var option in AccentOptions)
        {
            option.IsSelected = string.Equals(option.Name, activeName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static SettingsOption<T> FindOption<T>(
        IReadOnlyList<SettingsOption<T>> options,
        T? value,
        IEqualityComparer<T> comparer)
    {
        if (value is not null)
        {
            foreach (var option in options)
            {
                if (comparer.Equals(option.Value, value))
                {
                    return option;
                }
            }
        }

        return options[0];
    }

    private static List<string> ParseDistinctLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static string TryUnprotectApiKey(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return string.Empty;
        }

        return SecureStringStore.TryUnprotect(protectedBase64) ?? string.Empty;
    }

    private static string ProtectApiKey(string? plainText)
    {
        var trimmed = plainText?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        return SecureStringStore.Protect(trimmed);
    }

    private static T CloneObject<T>(T source) where T : new()
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new T();
    }

    private void ShowToast(string title, string content, NotificationType type, int seconds)
        => _dataContext.TryShowToast(title, content, type, seconds);
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
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

    private readonly AppDataContext _dataContext;
    private bool _isSynchronizing;

    public SettingsPageViewModel(AppDataContext dataContext)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));

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

    public string DataDirectory => _dataContext.DataDirectory;

    public bool IsSharedDevDataDirectory => _dataContext.IsSharedDevDataDirectory;

    public string DataDirectoryBadgeText => IsSharedDevDataDirectory
        ? "開發共享資料目錄"
        : "應用程式本機目錄";

    public string DataDirectoryDescription => IsSharedDevDataDirectory
        ? "目前唯讀共用既有漫畫庫資料目錄（appsettings.json / preferences.json / cache.db / thumbs），僅在按下「儲存設定」時才會寫入設定檔。"
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

            _dataContext.SaveConfig(updatedConfig);
            _dataContext.SavePreferences(updatedPrefs);
            _dataContext.ApplyTheme(IsDarkTheme, SelectedAccentName);

            // 正規化回填多行文字框（反映已去除的空白行）
            BlacklistText = string.Join(Environment.NewLine, blacklist);
            BlacklistPatternsText = string.Join(Environment.NewLine, blacklistPatterns);

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
        }
        finally
        {
            _isSynchronizing = false;
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
    {
        _dataContext.ToastManager
            .CreateToast()
            .WithTitle(title)
            .WithContent(content)
            .OfType(type)
            .Dismiss().After(TimeSpan.FromSeconds(seconds))
            .Dismiss().ByClicking()
            .Queue();
    }
}

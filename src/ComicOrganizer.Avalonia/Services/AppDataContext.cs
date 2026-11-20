using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using SukiUI;
using SukiUI.Models;
using SukiUI.Toasts;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// Avalonia 殼層核心資料與設定上下文。
/// 負責解析共用資料目錄、唯讀載入 appsettings.json / preferences.json / cache.db / thumbs / 標籤翻譯庫，
/// 並統一管理 SukiUI 主題切換與 Toast 通知。
/// </summary>
public sealed class AppDataContext : IDisposable
{
    public const string DataDirEnvVar = "COMIC_ORGANIZER_DATA_DIR";
    public const string DefaultDevDataDirectory = @"D:\ComicOrganizer";

    private static readonly string[] AccentNames = ["Blue", "Purple", "Rose", "Emerald", "Amber"];

    private static readonly Dictionary<string, SukiColorTheme> AccentThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blue"] = new SukiColorTheme("Blue", Color.Parse("#2563EB"), Color.Parse("#60A5FA")),
        ["Purple"] = new SukiColorTheme("Purple", Color.Parse("#7C3AED"), Color.Parse("#A78BFA")),
        ["Rose"] = new SukiColorTheme("Rose", Color.Parse("#E11D48"), Color.Parse("#FB7185")),
        ["Emerald"] = new SukiColorTheme("Emerald", Color.Parse("#059669"), Color.Parse("#34D399")),
        ["Amber"] = new SukiColorTheme("Amber", Color.Parse("#D97706"), Color.Parse("#FBBF24")),
    };

    private readonly ConfigManager _configManager;
    private readonly UserPreferencesManager _preferencesManager;
    private readonly Lazy<IAssetMetaStore> _metaStore;
    private readonly Lazy<IThumbStore> _thumbStore;
    private readonly SukiToastManager _toastManager;
    private bool _disposed;

    public AppDataContext(string? explicitDataDirectory = null, string[]? args = null, bool? forceReadOnlyDatabase = null)
    {
        var (resolvedDir, isSharedDev) = ResolveDataDirectory(explicitDataDirectory, args);
        DataDirectory = resolvedDir;
        IsSharedDevDataDirectory = isSharedDev;
        IsReadOnlyDatabase = forceReadOnlyDatabase ?? (isSharedDev && File.Exists(Path.Combine(DataDirectory, "cache.db")));

        _configManager = new ConfigManager(Path.Combine(DataDirectory, "appsettings.json"));
        _preferencesManager = new UserPreferencesManager(Path.Combine(DataDirectory, "preferences.json"));

        // 啟動時僅讀取設定，絕不覆寫或建立使用者設定檔
        Config = _configManager.Load();
        Preferences = _preferencesManager.Load();

        IsDarkTheme = ResolveInitialDarkTheme(Preferences.Theme);
        CurrentAccentName = AccentNames[0];

        _metaStore = new Lazy<IAssetMetaStore>(CreateMetaStore);
        _thumbStore = new Lazy<IThumbStore>(() => new FileSystemThumbStore(Path.Combine(DataDirectory, "thumbs")));

        var translationDiskPath = Path.Combine(DataDirectory, "translations", "db.text.json.gz");
        TagTranslator = new TagTranslator(translationDiskPath);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            SkiaOcrPluginManager.RegisterNativeFallback(() => new WindowsSkiaOcrService());
        }

        _toastManager = new SukiToastManager();
    }

    public string DataDirectory { get; }

    public bool IsSharedDevDataDirectory { get; }

    public bool IsReadOnlyDatabase { get; }

    public AppConfig Config { get; private set; }

    public UserPreferences Preferences { get; private set; }

    public IAssetMetaStore MetaStore => _metaStore.Value;

    public IThumbStore ThumbStore => _thumbStore.Value;

    public TagTranslator TagTranslator { get; private set; }

    public void ReloadTagTranslator()
    {
        var translationDiskPath = Path.Combine(DataDirectory, "translations", "db.text.json.gz");
        TagTranslator = new TagTranslator(translationDiskPath);
    }

    public ISukiToastManager ToastManager => _toastManager;

    /// <summary>
    /// 在有執行期 Avalonia 視覺環境時安全發送 Toast（無介面單元測試環境下自動略過，避免 SukiUI.Content.Icons 例外）。
    /// </summary>
    public void TryShowToast(string title, string message, NotificationType type, int durationSeconds = 3)
    {
        if (Application.Current == null)
        {
            return;
        }

        try
        {
            _toastManager.CreateToast()
                .WithTitle(title)
                .WithContent(message)
                .OfType(type)
                .Dismiss().After(TimeSpan.FromSeconds(durationSeconds))
                .Queue();
        }
        catch
        {
            // 非視覺測試環境忽略 Toast 失敗
        }
    }

    public IReadOnlyList<string> AvailableAccentNames => AccentNames;

    public bool IsDarkTheme { get; private set; }

    public string CurrentAccentName { get; private set; }

    public event Action? ThemeChanged;

    public event Action? SettingsChanged;

    public static Color GetAccentPreviewColor(string accentName)
    {
        return AccentThemes.TryGetValue(accentName, out var theme)
            ? theme.Primary
            : AccentThemes["Blue"].Primary;
    }

    public void ApplyInitialTheme()
    {
        ApplyTheme(IsDarkTheme, CurrentAccentName);
    }

    public void ApplyTheme(bool isDark, string accentName)
    {
        var normalized = NormalizeAccentName(accentName);
        IsDarkTheme = isDark;
        CurrentAccentName = normalized;

        if (Application.Current != null)
        {
            try
            {
                var sukiTheme = SukiTheme.GetInstance();
                EnsureColorThemesRegistered(sukiTheme);
                sukiTheme.ChangeBaseTheme(isDark ? ThemeVariant.Dark : ThemeVariant.Light);

                if (AccentThemes.TryGetValue(normalized, out var colorTheme))
                {
                    sukiTheme.ChangeColorTheme(colorTheme);
                }
            }
            catch (InvalidOperationException)
            {
                // SukiTheme 尚未掛載至 Application.Styles 時忽略（如無 UI 測試或設計期）
            }
        }

        ThemeChanged?.Invoke();
    }

    public void SaveConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        Directory.CreateDirectory(DataDirectory);
        _configManager.Save(config);
        Config = config;
        SettingsChanged?.Invoke();
    }

    public void SavePreferences(UserPreferences prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        Directory.CreateDirectory(DataDirectory);
        _preferencesManager.Save(prefs);
        Preferences = prefs;
        SettingsChanged?.Invoke();
    }

    public void ReloadSettings()
    {
        Config = _configManager.Load();
        Preferences = _preferencesManager.Load();
        var isDark = ResolveInitialDarkTheme(Preferences.Theme);
        ApplyTheme(isDark, CurrentAccentName);
        SettingsChanged?.Invoke();
    }

    private IAssetMetaStore CreateMetaStore()
    {
        var dbPath = Path.Combine(DataDirectory, "cache.db");
        return new SqliteAssetMetaStore(dbPath, readOnly: IsReadOnlyDatabase);
    }

    private static void EnsureColorThemesRegistered(SukiTheme sukiTheme)
    {
        foreach (var (_, theme) in AccentThemes)
        {
            if (!sukiTheme.ColorThemes.Any(t => string.Equals(t.DisplayName, theme.DisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                sukiTheme.AddColorTheme(theme);
            }
        }
    }

    private static string NormalizeAccentName(string? accentName)
    {
        if (!string.IsNullOrWhiteSpace(accentName))
        {
            foreach (var name in AccentNames)
            {
                if (string.Equals(name, accentName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        return AccentNames[0];
    }

    private static bool ResolveInitialDarkTheme(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => DetectSystemDarkTheme(),
        };
    }

    private static bool DetectSystemDarkTheme()
    {
        try
        {
            var platformTheme = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
            if (platformTheme.HasValue)
            {
                return platformTheme.Value == PlatformThemeVariant.Dark;
            }
        }
        catch
        {
            // 非桌面環境或平台設定尚未初始化時 fallback
        }

        return true;
    }

    private static (string DirectoryPath, bool IsSharedDev) ResolveDataDirectory(string? explicitDataDirectory, string[]? args)
    {
        var baseDir = NormalizeDirectoryPath(AppContext.BaseDirectory);

        // (1) 建構參數 / CLI --data-dir / 環境變數 COMIC_ORGANIZER_DATA_DIR
        var overrideDir = !string.IsNullOrWhiteSpace(explicitDataDirectory)
            ? explicitDataDirectory
            : ParseCliDataDir(args) ?? Environment.GetEnvironmentVariable(DataDirEnvVar);

        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var normalizedOverride = NormalizeDirectoryPath(overrideDir);
            var isDevShared = PathsEqual(normalizedOverride, DefaultDevDataDirectory)
                              && !PathsEqual(baseDir, DefaultDevDataDirectory);
            return (normalizedOverride, isDevShared);
        }

        // (2) AppContext.BaseDirectory 若已有 cache.db 或 appsettings.json
        if (File.Exists(Path.Combine(baseDir, "cache.db"))
            || File.Exists(Path.Combine(baseDir, "appsettings.json")))
        {
            return (baseDir, false);
        }

        // (3) 若 D:\ComicOrganizer 存在則使用該共享開發資料目錄
        if (Directory.Exists(DefaultDevDataDirectory))
        {
            return (NormalizeDirectoryPath(DefaultDevDataDirectory), true);
        }

        // (4) Fallback AppContext.BaseDirectory
        return (baseDir, false);
    }

    private static string? ParseCliDataDir(string[]? args)
    {
        var effectiveArgs = args ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
        for (var i = 0; i < effectiveArgs.Length; i++)
        {
            var arg = effectiveArgs[i];
            if (string.Equals(arg, "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < effectiveArgs.Length && !string.IsNullOrWhiteSpace(effectiveArgs[i + 1]))
                {
                    return effectiveArgs[i + 1];
                }
            }
            else if (arg.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg["--data-dir=".Length..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path.Trim().Trim('"'))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeDirectoryPath(left),
            NormalizeDirectoryPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _toastManager.Dispose();

        if (_metaStore.IsValueCreated)
        {
            _metaStore.Value.Dispose();
        }
    }
}

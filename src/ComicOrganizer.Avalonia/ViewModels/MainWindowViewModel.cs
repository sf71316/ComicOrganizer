using System.ComponentModel;
using Avalonia.Media;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed partial class AccentThemeChoice : ObservableObject
{
    private readonly Action<string> _onSelect;

    public AccentThemeChoice(string name, string displayName, IBrush swatchBrush, bool isSelected, Action<string> onSelect)
    {
        Name = name;
        DisplayName = displayName;
        SwatchBrush = swatchBrush;
        _isSelected = isSelected;
        _onSelect = onSelect;
    }

    public string Name { get; }

    public string DisplayName { get; }

    public IBrush SwatchBrush { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(Name);
}

/// <summary>
/// 主視窗殼層 ViewModel：負責 6 大分頁導覽、頂部主題／強調色／全域搜尋預留與共用資料目錄狀態。
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly Dictionary<string, string> AccentLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blue"] = "蔚藍",
        ["Purple"] = "紫韻",
        ["Rose"] = "玫瑰",
        ["Emerald"] = "翡翠",
        ["Amber"] = "琥珀",
    };

    private readonly AppDataContext _dataContext;
    private readonly SkiaCoverService _coverService;
    private bool _isSyncingSearch;
    private bool _disposed;

    public MainWindowViewModel(AppDataContext dataContext)
    {
        _dataContext = dataContext;
        _coverService = new SkiaCoverService(dataContext.ThumbStore);

        _isDarkTheme = dataContext.IsDarkTheme;
        _currentAccentName = dataContext.CurrentAccentName;
        _currentAccentBrush = new SolidColorBrush(AppDataContext.GetAccentPreviewColor(_currentAccentName));

        AccentOptions = dataContext.AvailableAccentNames
            .Select(name => new AccentThemeChoice(
                name,
                FormatAccentLabel(name),
                new SolidColorBrush(AppDataContext.GetAccentPreviewColor(name)),
                string.Equals(name, _currentAccentName, StringComparison.OrdinalIgnoreCase),
                SelectAccent))
            .ToList();

        ExplorePage = new ExplorePageViewModel(dataContext, _coverService);
        ExplorePage.PropertyChanged += OnExplorePagePropertyChanged;
        ExplorePage.OpenInGalleryRequested += OnOpenInGalleryRequested;

        GalleryPage = new GalleryPageViewModel(dataContext, _coverService);
        ReadingTrackPage = new ReadingTrackPageViewModel(dataContext, _coverService);
        ReadingTrackPage.OpenInGalleryRequested += OnOpenInGalleryRequested;
        AutoArchivePage = new AutoArchivePageViewModel(dataContext);
        DataManagementPage = new DataManagementPageViewModel(dataContext, _coverService);
        DataManagementPage.OpenInGalleryRequested += OnOpenInGalleryRequested;
        SettingsPage = new SettingsPageViewModel(dataContext);

        _dataContext.ThemeChanged += OnDataContextThemeChanged;
        _dataContext.SettingsChanged += OnDataContextSettingsChanged;
    }

    public ISukiToastManager ToastManager => _dataContext.ToastManager;

    public event Action<ViewModelBase>? NavigateToPageRequested;

    public IReadOnlyList<AccentThemeChoice> AccentOptions { get; }

    public ExplorePageViewModel ExplorePage { get; }

    public GalleryPageViewModel GalleryPage { get; }

    public ReadingTrackPageViewModel ReadingTrackPage { get; }

    public AutoArchivePageViewModel AutoArchivePage { get; }

    public DataManagementPageViewModel DataManagementPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    [ObservableProperty]
    private bool _isSideMenuExpanded = true;

    [ObservableProperty]
    private string _globalSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeToggleLabel))]
    private bool _isDarkTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentAccentShortLabel))]
    private string _currentAccentName;

    [ObservableProperty]
    private IBrush _currentAccentBrush;

    public string ThemeToggleLabel => IsDarkTheme ? "🌙 深色" : "☀️ 淺色";

    public string CurrentAccentShortLabel => AccentLabels.TryGetValue(CurrentAccentName, out var label)
        ? label
        : CurrentAccentName;

    public string DataDirectoryBadgeText => _dataContext.IsReadOnlyDatabase
        ? "🔒 共享開發庫（唯讀）"
        : _dataContext.IsSharedDevDataDirectory
            ? "📂 共享開發庫"
            : "📂 本地資料目錄";

    public string DataDirectoryShortPath => _dataContext.DataDirectory;

    public string DataDirectoryTooltip => _dataContext.IsReadOnlyDatabase
        ? $"目前共用資料目錄：{_dataContext.DataDirectory}\n模式：唯讀預覽（不寫入或遷移既有 cache.db；僅於系統設定按下儲存時寫入設定檔）"
        : $"目前共用資料目錄：{_dataContext.DataDirectory}";

    [RelayCommand]
    private void ToggleTheme()
    {
        _dataContext.ApplyTheme(!_dataContext.IsDarkTheme, _dataContext.CurrentAccentName);
    }

    [RelayCommand]
    private void SelectAccent(string? accentName)
    {
        if (string.IsNullOrWhiteSpace(accentName))
        {
            return;
        }

        _dataContext.ApplyTheme(_dataContext.IsDarkTheme, accentName);
    }

    partial void OnGlobalSearchTextChanged(string value)
    {
        if (_isSyncingSearch || _disposed)
        {
            return;
        }

        _isSyncingSearch = true;
        try
        {
            ExplorePage.SearchKeyword = value ?? string.Empty;
        }
        finally
        {
            _isSyncingSearch = false;
        }
    }

    private void OnOpenInGalleryRequested(string dsl)
    {
        if (_disposed)
        {
            return;
        }

        _ = GalleryPage.ApplyExternalDslQueryAsync(dsl);
        NavigateToPageRequested?.Invoke(GalleryPage);
    }

    private void OnExplorePagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isSyncingSearch || _disposed || e.PropertyName != nameof(ExplorePageViewModel.SearchKeyword))
        {
            return;
        }

        var next = ExplorePage.SearchKeyword ?? string.Empty;
        if (string.Equals(GlobalSearchText, next, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingSearch = true;
        try
        {
            GlobalSearchText = next;
        }
        finally
        {
            _isSyncingSearch = false;
        }
    }

    [RelayCommand]
    private async Task ExecuteGlobalSearchAsync()
    {
        if (_disposed)
        {
            return;
        }

        var query = GlobalSearchText ?? string.Empty;
        ExplorePage.SearchKeyword = query;
        await Task.WhenAll(
            ExplorePage.ApplySearchImmediateAsync(),
            GalleryPage.ApplyExternalDslQueryAsync(query));
    }

    private void OnDataContextThemeChanged()
    {
        IsDarkTheme = _dataContext.IsDarkTheme;
        CurrentAccentName = _dataContext.CurrentAccentName;
        CurrentAccentBrush = new SolidColorBrush(AppDataContext.GetAccentPreviewColor(_dataContext.CurrentAccentName));

        foreach (var option in AccentOptions)
        {
            option.IsSelected = string.Equals(option.Name, _dataContext.CurrentAccentName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnDataContextSettingsChanged()
    {
        _ = ExplorePage.RefreshCommand.ExecuteAsync(null);
        _ = GalleryPage.RefreshAsync();
        _ = ReadingTrackPage.RefreshAsync();
        AutoArchivePage.RefreshConfigSummary();
        DataManagementPage.RefreshAll();
    }

    private static string FormatAccentLabel(string name)
    {
        return AccentLabels.TryGetValue(name, out var zh) ? $"{zh} {name}" : name;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dataContext.ThemeChanged -= OnDataContextThemeChanged;
        _dataContext.SettingsChanged -= OnDataContextSettingsChanged;
        ExplorePage.PropertyChanged -= OnExplorePagePropertyChanged;
        ExplorePage.OpenInGalleryRequested -= OnOpenInGalleryRequested;
        ExplorePage.Dispose();
        GalleryPage.Dispose();
        ReadingTrackPage.OpenInGalleryRequested -= OnOpenInGalleryRequested;
        ReadingTrackPage.Dispose();
        DataManagementPage.OpenInGalleryRequested -= OnOpenInGalleryRequested;
        DataManagementPage.Dispose();
        _coverService.Dispose();
    }
}

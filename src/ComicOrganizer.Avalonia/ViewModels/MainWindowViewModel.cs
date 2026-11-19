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
    private readonly IReadOnlyList<PlaceholderPageViewModel> _placeholderPages;
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

        GalleryPage = CreateGalleryPage();
        ReadingTrackPage = CreateReadingTrackPage();
        AutoArchivePage = CreateAutoArchivePage();
        DataManagementPage = CreateDataManagementPage();
        SettingsPage = new SettingsPageViewModel(dataContext);

        _placeholderPages =
        [
            GalleryPage,
            ReadingTrackPage,
            AutoArchivePage,
            DataManagementPage,
        ];

        _dataContext.ThemeChanged += OnDataContextThemeChanged;
        _dataContext.SettingsChanged += OnDataContextSettingsChanged;
    }

    public ISukiToastManager ToastManager => _dataContext.ToastManager;

    public IReadOnlyList<AccentThemeChoice> AccentOptions { get; }

    public ExplorePageViewModel ExplorePage { get; }

    public PlaceholderPageViewModel GalleryPage { get; }

    public PlaceholderPageViewModel ReadingTrackPage { get; }

    public PlaceholderPageViewModel AutoArchivePage { get; }

    public PlaceholderPageViewModel DataManagementPage { get; }

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

        ExplorePage.SearchKeyword = GlobalSearchText ?? string.Empty;
        await ExplorePage.ApplySearchImmediateAsync();
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
        foreach (var page in _placeholderPages)
        {
            _ = page.RefreshAsync();
        }
    }

    private PlaceholderPageViewModel CreateGalleryPage()
    {
        return new PlaceholderPageViewModel(
            "📚",
            "漫畫庫畫廊",
            "Phase 2 實作",
            "主力瀏覽與管理介面，提供多尺寸卡片與列表檢視、目錄樹、搜尋 DSL、自訂虛擬庫與作品詳情抽屜。",
            [
                "大／中／小卡片與詳細列表模式，搭配自然排序與多維過濾",
                "整合搜尋 DSL（tag: / artist: / series: / group: / -排除）與虛擬庫側欄",
                "卡片右鍵選單、詳情編輯抽屜與一鍵開啟內建閱讀器",
            ],
            () =>
            {
                var store = _dataContext.MetaStore;
                var config = _dataContext.Config;
                var totalAssets = store.TotalAssetCount();
                var cachedThumbs = store.TotalCachedCount();
                var thumbBytes = store.TotalThumbBytes();

                return new PlaceholderSnapshot(
                    [
                        new PlaceholderMetricItem("漫畫庫路徑", FormatPathOrUnset(config.RootDirectory), "可於「⚙️ 系統設定」調整漫畫庫根目錄"),
                        new PlaceholderMetricItem("已索引作品數", $"{totalAssets:N0}", "與既有 WinForms SQLite 資料庫完全相容"),
                        new PlaceholderMetricItem("縮圖快取狀態", FormatBytes(thumbBytes), $"共 {cachedThumbs:N0} 張快取縮圖｜上限 {FormatBytes(config.ThumbCacheMaxBytes)}"),
                    ],
                    BuildDbFootnote());
            });
    }

    private PlaceholderPageViewModel CreateReadingTrackPage()
    {
        return new PlaceholderPageViewModel(
            "🕒",
            "閱讀軌跡",
            "Phase 2 實作",
            "追蹤你的近期閱讀歷程、進行中進度與最常閱讀的作者與標籤排行，隨時從上次停下的頁碼繼續閱讀。",
            [
                "「繼續閱讀」快捷清單與精準上次閱讀頁碼（last_page）還原",
                "最近閱讀、最近新增、最常閱讀作者與熱門標籤視覺化排行",
                "閱讀狀態標記（未讀／閱讀中／已完成）與批次重置工具",
            ],
            () =>
            {
                var store = _dataContext.MetaStore;
                var recentRead = store.ListRecentlyRead(50).Count;
                var inProgress = store.ListReadingInProgress().Count;
                var completed = store.ListWorksByReadStatus(ReadStatus.Completed).Count;
                var topAuthors = store.ListMostReadAuthors(3);
                var topAuthorHint = topAuthors.Count == 0
                    ? "尚無閱讀次數排行資料"
                    : "熱門作者：" + string.Join("、", topAuthors.Select(a => $"{a.Author.MainName}({a.TotalReads})"));

                return new PlaceholderSnapshot(
                    [
                        new PlaceholderMetricItem("近期閱讀紀錄", $"{recentRead:N0}", "統計最近閱讀的前 50 部作品紀錄"),
                        new PlaceholderMetricItem("閱讀進行中", $"{inProgress:N0}", "狀態為 Reading 且可接續上次進度的作品"),
                        new PlaceholderMetricItem("已完成作品", $"{completed:N0}", topAuthorHint),
                    ],
                    BuildDbFootnote());
            });
    }

    private PlaceholderPageViewModel CreateAutoArchivePage()
    {
        return new PlaceholderPageViewModel(
            "📦",
            "自動歸檔",
            "Phase 4 實作",
            "自動掃描未整理資料夾中的漫畫與壓縮檔，結合自我學習知識庫與模糊比對建議最佳目標目錄，經預覽確認後安全搬檔。",
            [
                "兩階段比對：優先命中自我學習知識庫（mappings.json），再以 FuzzySharp 計分",
                "歸檔預覽清單：搬檔前可逐項勾選、修正目標路徑或略過雜訊項目",
                "非同步批次搬檔與成功歸檔自動回寫學習記錄",
            ],
            () =>
            {
                var config = _dataContext.Config;
                var mappingsExists = File.Exists(Path.Combine(_dataContext.DataDirectory, "mappings.json"));

                return new PlaceholderSnapshot(
                    [
                        new PlaceholderMetricItem("來源未整理目錄", FormatPathOrUnset(config.UnorganizedDirectory), "等待掃描與自動比對的下載／未整理目錄"),
                        new PlaceholderMetricItem("目標漫畫庫路徑", FormatPathOrUnset(config.RootDirectory), "歸檔確認後移入的漫畫庫根目錄"),
                        new PlaceholderMetricItem("雜訊過濾規則", $"{config.Blacklist.Count} 詞 / {config.BlacklistPatterns.Count} 正則", $"自我學習庫 mappings.json：{(mappingsExists ? "已就緒" : "尚未建立")}｜掃描深度 {config.MaxScanDepth} 層"),
                    ],
                    $"設定檔來源：{Path.Combine(_dataContext.DataDirectory, "appsettings.json")}（更新時間：{DateTime.Now:HH:mm:ss}）");
            });
    }

    private PlaceholderPageViewModel CreateDataManagementPage()
    {
        return new PlaceholderPageViewModel(
            "🏷️",
            "資料管理",
            "Phase 4 實作",
            "集中維護漫畫庫的標籤、作者、多語別名與外部 Metadata 同步，提供批次整理、合併與譯名編輯能力。",
            [
                "標籤管理與虛擬主標籤合併（含 tag_alias 自動導向與一鍵畫廊檢視）",
                "作者主名重命名、合併重複作者與多語別名維護",
                "nhentai 外部 Metadata 同步與 EhTag 中文翻譯詞庫線上更新",
            ],
            () =>
            {
                var store = _dataContext.MetaStore;
                var tagCount = store.ListTagsByUsage().Count;
                var authorCount = store.ListAuthorsByUsage().Count;
                var authorAliasCount = store.ListAllAuthorAliases().Count;
                var tagAliasCount = store.ListAllTagAliases().Count;
                var hasDiskTranslation = File.Exists(Path.Combine(_dataContext.DataDirectory, "translations", "db.text.json.gz"));

                return new PlaceholderSnapshot(
                    [
                        new PlaceholderMetricItem("標籤總數", $"{tagCount:N0}", "包含所有已分類與待分類的漫畫標籤"),
                        new PlaceholderMetricItem("作者總數", $"{authorCount:N0}", "已登錄的漫畫作者與社團主檔數量"),
                        new PlaceholderMetricItem("別名總數", $"{authorAliasCount + tagAliasCount:N0}", $"作者別名：{authorAliasCount:N0} 筆｜標籤別名：{tagAliasCount:N0} 筆"),
                        new PlaceholderMetricItem("翻譯詞庫", hasDiskTranslation ? "本地快取詞庫" : "內嵌 EhTag 詞庫", $"外部 Metadata 同步：{(_dataContext.Config.EnableExternalMetadata ? "已啟用" : "未啟用")}"),
                    ],
                    BuildDbFootnote());
            });
    }

    private string BuildDbFootnote()
    {
        var dbMode = _dataContext.IsReadOnlyDatabase ? "唯讀預覽（不改寫 cache.db）" : "讀寫模式";
        return $"資料目錄：{_dataContext.DataDirectory}｜資料庫模式：{dbMode}｜更新時間：{DateTime.Now:HH:mm:ss}";
    }

    private static string FormatAccentLabel(string name)
    {
        return AccentLabels.TryGetValue(name, out var zh) ? $"{zh} {name}" : name;
    }

    private static string FormatPathOrUnset(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "尚未設定" : path;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        const double gb = mb * 1024.0;

        return bytes >= gb
            ? $"{bytes / gb:F2} GB"
            : bytes >= mb
                ? $"{bytes / mb:F1} MB"
                : bytes >= kb
                    ? $"{bytes / kb:F1} KB"
                    : $"{bytes} B";
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
        ExplorePage.Dispose();
        _coverService.Dispose();
    }
}

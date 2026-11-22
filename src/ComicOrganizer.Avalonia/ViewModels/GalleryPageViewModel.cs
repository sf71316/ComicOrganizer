using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;
using NotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace ComicOrganizer.Avalonia.ViewModels;

public enum GallerySidebarMode
{
    Folders = 0,
    Authors = 1,
    Tags = 2,
    MyLibraries = 3,
}

public enum GalleryViewMode
{
    LargeCard = 0,
    MediumCard = 1,
    SmallCard = 2,
    DetailList = 3,
}

public sealed record GallerySortFieldOption(GallerySortField Field, string Label)
{
    public override string ToString() => Label;
}

public sealed record GalleryReadFilterOption(GalleryReadFilter Filter, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class GalleryNavNodeVm : ObservableObject
{
    private readonly Action<GalleryNavNodeVm> _onSelect;

    public GalleryNavNodeVm(
        string key,
        string icon,
        string displayName,
        string subText,
        string? folderPath,
        IVirtualLibraryNode? virtualNode,
        bool isSelected,
        Action<GalleryNavNodeVm> onSelect)
    {
        Key = key;
        Icon = icon;
        DisplayName = displayName;
        SubText = subText;
        FolderPath = folderPath;
        VirtualNode = virtualNode;
        _isSelected = isSelected;
        _onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
    }

    public string Key { get; }

    public string Icon { get; }

    public string DisplayName { get; }

    public string SubText { get; }

    public string? FolderPath { get; }

    public IVirtualLibraryNode? VirtualNode { get; }

    public bool HasSubText => !string.IsNullOrWhiteSpace(SubText);

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(this);

    /// <summary>顯示內容（key、圖示、名稱、副標、資料夾路徑）是否相同；重整時相同的節點直接沿用舊的。</summary>
    public bool HasSameContent(GalleryNavNodeVm other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(Key, other.Key, StringComparison.Ordinal)
            && string.Equals(Icon, other.Icon, StringComparison.Ordinal)
            && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
            && string.Equals(SubText, other.SubText, StringComparison.Ordinal)
            && string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GalleryBreadcrumbItemVm
{
    public GalleryBreadcrumbItemVm(string label, string fullPath, bool isCurrent, Action<string> onNavigate)
    {
        Label = label;
        FullPath = fullPath;
        IsCurrent = isCurrent;
        NavigateCommand = new RelayCommand(() => onNavigate(FullPath));
    }

    public string Label { get; }

    public string FullPath { get; }

    public bool IsCurrent { get; }

    public IRelayCommand NavigateCommand { get; }
}

public sealed class GalleryFuzzyRecommendationVm
{
    public GalleryFuzzyRecommendationVm(SearchRecommendation rec, Action<string> onApply)
    {
        DisplayText = rec.DisplayText;
        ReplacementQuery = rec.ReplacementQuery;
        Score = rec.Score;
        ApplyCommand = new RelayCommand(() => onApply(ReplacementQuery));
    }

    public string DisplayText { get; }

    public string ReplacementQuery { get; }

    public int Score { get; }

    public IRelayCommand ApplyCommand { get; }
}

public sealed partial class GalleryItemVm : ViewModelBase
{
    private readonly Action<GalleryItemVm> _onActivate;
    private readonly Action<GalleryItemVm> _onRevealInExplorer;
    private readonly Action<GalleryItemVm>? _onInspectDetail;
    private readonly Action<GalleryItemVm>? _onCoverLoaded;
    private readonly Action<GalleryItemVm>? _onOpenInReader;
    private readonly Action<GalleryItemVm>? _onOpenExternal;
    private readonly Func<Action, Task> _runOnUiAsync;

    public GalleryItemVm(
        GalleryItemSortMetadata sortMeta,
        string? authorName,
        string? coverThumbKey,
        int? pageCount,
        string? language,
        bool isAuthorFolder,
        Action<GalleryItemVm> onActivate,
        Action<GalleryItemVm> onRevealInExplorer,
        Func<Action, Task>? runOnUiAsync = null,
        Action<GalleryItemVm>? onInspectDetail = null,
        Action<GalleryItemVm>? onCoverLoaded = null,
        Action<GalleryItemVm>? onOpenInReader = null,
        Action<GalleryItemVm>? onOpenExternal = null)
    {
        SortMetadata = sortMeta ?? throw new ArgumentNullException(nameof(sortMeta));
        Path = sortMeta.Path;
        Name = sortMeta.Name;
        DisplayTitle = !string.IsNullOrWhiteSpace(sortMeta.DisplayTitle) ? sortMeta.DisplayTitle! : sortMeta.Name;
        AuthorName = authorName ?? string.Empty;
        IsFolder = sortMeta.IsFolder;
        IsAuthorFolder = isAuthorFolder;
        SizeBytes = sortMeta.SizeBytes;
        ModifiedTicks = sortMeta.ModifiedTicks;
        ReadStatus = sortMeta.ReadStatus;
        ReadCount = sortMeta.ReadCount;
        LastRead = sortMeta.LastRead;
        PageCount = pageCount;
        Language = language;

        _onActivate = onActivate ?? throw new ArgumentNullException(nameof(onActivate));
        _onRevealInExplorer = onRevealInExplorer ?? throw new ArgumentNullException(nameof(onRevealInExplorer));
        _onInspectDetail = onInspectDetail;
        _onCoverLoaded = onCoverLoaded;
        _onOpenInReader = onOpenInReader;
        _onOpenExternal = onOpenExternal;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        AccentColorHex = ExploreVisualFormatting.PickAccentHex(DisplayTitle);
        FallbackGlyph = IsFolder
            ? (IsAuthorFolder ? "👤" : "📁")
            : ExploreVisualFormatting.ExtractFallbackGlyph(DisplayTitle);

        // 預先計算顯示字串並快取，避免虛擬化卡片回收重用時反覆格式化日期、配置 List 與字串插值
        HasAuthor = !string.IsNullOrWhiteSpace(AuthorName);
        TypeBadgeText = IsFolder
            ? (IsAuthorFolder ? "👤 作者目錄" : "📁 資料夾")
            : ReadStatus switch
            {
                ReadStatus.Reading => "📖 閱讀中",
                ReadStatus.Completed => "✓ 已完成",
                ReadStatus.Dropped => "✕ 放棄",
                _ => "● 未讀",
            };
        HasPageCount = !IsFolder && PageCount is > 0;
        PageCountText = HasPageCount ? $"{PageCount}P" : string.Empty;
        SizeDisplay = IsFolder ? "資料夾" : FormatSize(SizeBytes);
        ModifiedDisplay = ModifiedTicks > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(ModifiedTicks).ToLocalTime().ToString("yyyy-MM-dd")
            : string.Empty;
        SubtitleText = BuildSubtitleText();
        PrimaryActionTooltip = IsFolder
            ? $"進入資料夾：{DisplayTitle}"
            : $"以內建閱讀器開啟：{DisplayTitle}";

        var dummyWork = new ExploreWorkItem(
            AssetId: 0,
            Path: Path,
            FileName: Name,
            Title: DisplayTitle,
            AuthorId: null,
            AuthorName: AuthorName,
            Language: Language,
            TranslationGroup: null,
            PageCount: PageCount,
            ReadStatus: ReadStatus,
            ReadCount: ReadCount,
            LastRead: LastRead,
            AddedAt: ModifiedTicks,
            ExternalId: null,
            CoverThumbKey: coverThumbKey,
            Tags: Array.Empty<ExploreTagBadge>());

        CoverSlot = new CoverSlotVm(dummyWork, AccentColorHex);
        CoverSlot.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CoverSlotVm.Image))
            {
                OnPropertyChanged(nameof(Cover1));
                OnPropertyChanged(nameof(HasCover1));
                _onCoverLoaded?.Invoke(this);
            }
        };
    }

    [ObservableProperty]
    private bool _isSelected;

    public GalleryItemSortMetadata SortMetadata { get; }

    public string Path { get; }

    public string Name { get; }

    public string DisplayTitle { get; }

    public string AuthorName { get; }

    public bool HasAuthor { get; }

    public bool IsFolder { get; }

    public bool IsAuthorFolder { get; }

    public long SizeBytes { get; }

    public long ModifiedTicks { get; }

    public ReadStatus ReadStatus { get; }

    public int ReadCount { get; }

    public long? LastRead { get; }

    public int? PageCount { get; }

    public string? Language { get; }

    public CoverSlotVm CoverSlot { get; }

    public Bitmap? Cover1 => CoverSlot.Image;

    public bool HasCover1 => Cover1 != null;

    public string FallbackGlyph { get; }

    public string AccentColorHex { get; }

    public string TypeBadgeText { get; }

    public bool HasPageCount { get; }

    public string PageCountText { get; }

    public string SizeDisplay { get; }

    public string ModifiedDisplay { get; }

    public string SubtitleText { get; }

    public string PrimaryActionTooltip { get; }

    private string BuildSubtitleText()
    {
        if (IsFolder)
        {
            return IsAuthorFolder ? "點擊進入作者作品目錄" : "點擊進入子資料夾";
        }

        var parts = new List<string>(3);
        if (HasAuthor)
        {
            parts.Add(AuthorName);
        }
        if (!string.IsNullOrWhiteSpace(Language))
        {
            parts.Add(Language!);
        }
        if (SizeBytes > 0)
        {
            parts.Add(SizeDisplay);
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : Name;
    }

    [RelayCommand]
    private void Activate() => _onActivate(this);

    [RelayCommand]
    private void OpenInReader() => (_onOpenInReader ?? _onActivate)(this);

    [RelayCommand]
    private void OpenExternal() => _onOpenExternal?.Invoke(this);

    [RelayCommand]
    private void InspectDetail() => _onInspectDetail?.Invoke(this);

    [RelayCommand]
    private void RevealInExplorer() => _onRevealInExplorer(this);

    public Task EnsureCoverLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken ct = default)
        => CoverSlot.EnsureLoadedAsync(coverService, concurrencyLimiter, _runOnUiAsync, ct);

    /// <summary>放掉封面參考（需在 UI 執行緒呼叫）；再次顯示時會重新載入。</summary>
    public void ReleaseCover() => CoverSlot.ReleaseImage();

    /// <summary>
    /// 是否與另一個項目顯示內容完全相同（排序 / 過濾用中繼資料、作者、縮圖 key、頁數、語言、作者目錄旗標）。
    /// 重整時相同的項目直接沿用舊的，保留已載入的封面與選取狀態。
    /// </summary>
    public bool HasSameContent(GalleryItemVm other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SortMetadata == other.SortMetadata
            && string.Equals(AuthorName, other.AuthorName, StringComparison.Ordinal)
            && string.Equals(CoverSlot.ThumbKey, other.CoverSlot.ThumbKey, StringComparison.Ordinal)
            && PageCount == other.PageCount
            && string.Equals(Language, other.Language, StringComparison.Ordinal)
            && IsAuthorFolder == other.IsAuthorFolder;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024.0)
        {
            return $"{mb / 1024.0:F2} GB";
        }
        if (mb >= 1.0)
        {
            return $"{mb:F1} MB";
        }
        return $"{Math.Max(1.0, bytes / 1024.0):F0} KB";
    }
}

/// <summary>
/// Phase 2 W2.1 漫畫庫畫廊 ViewModel：
/// 整合左側 4 維度導覽側欄（資料夾樹／依作者／依標籤／我的閱讀清單）、
/// 支援 @ 觸發與標籤區塊化之 <see cref="DslSearchBoxViewModel"/>、
/// 5 種排序、閱讀狀態與頁內過濾、4 種檢視尺寸（大／中／小卡片與詳細列表）；清單虛擬化，封面隨卡片建立 / 回收載入與釋放。
/// </summary>
public sealed partial class GalleryPageViewModel : ViewModelBase, IDisposable
{
    // 套用過濾 / 排序後，先替最前面這幾張預載封面（其餘由虛擬化清單建立卡片時再載入）
    private const int CoverPreloadBatchSize = 24;

    /// <summary>
    /// 虛擬化清單回收的卡片，最近的這麼多張先保留封面（往回捲一點不必重新載入），超過才放掉最舊的。
    /// </summary>
    public const int RecycledCoverRetention = 120;

    private readonly AppDataContext? _dataContext;
    private readonly IAssetMetaStore _store;
    private readonly SkiaCoverService _coverService;
    private readonly IExternalLauncher _externalLauncher;
    private readonly IReaderWindowLauncher _readerLauncher;
    private readonly ISukiToastManager? _toastManager;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly SemaphoreSlim _coverLoadSemaphore = new(4, 4);
    private readonly object _ctsLock = new();

    private IReadOnlyList<GalleryNavNodeVm> _allSidebarNodes = Array.Empty<GalleryNavNodeVm>();
    private IReadOnlyList<GalleryItemVm> _allRawItems = Array.Empty<GalleryItemVm>();
    private IReadOnlyList<GalleryItemVm> _allFilteredSortedItems = Array.Empty<GalleryItemVm>();
    private GalleryItemVm? _selectedGalleryItem;

    // 目前清單對應的範圍（見 BuildScopeKey）；同一範圍重新載入時沿用沒變的項目、不捲回頂端
    private string? _loadedScopeKey;
    private ExploreCorpusSnapshot? _cachedSnapshot;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _coverCts;

    // 已被虛擬化清單回收、但還保留封面的卡片（最舊的在前面）；只在 UI 執行緒存取
    private readonly LinkedList<GalleryItemVm> _recycledCoverQueue = new();
    private readonly Dictionary<GalleryItemVm, LinkedListNode<GalleryItemVm>> _recycledCoverNodes =
        new(ReferenceEqualityComparer.Instance);

    // 目前已建立卡片各自的封面載入取消權杖；卡片被回收時取消，還在排隊或解碼中的載入就不再做白工，
    // 封面載入名額讓給畫面上的卡片。只在 UI 執行緒存取
    private readonly Dictionary<GalleryItemVm, CancellationTokenSource> _realizedCoverLoads =
        new(ReferenceEqualityComparer.Instance);
    private int _loadVersion;
    private bool _suppressReload;
    private volatile bool _disposed;

    public GalleryPageViewModel(AppDataContext dataContext, SkiaCoverService coverService, bool autoLoad = true)
        : this(
            dataContext?.MetaStore ?? throw new ArgumentNullException(nameof(dataContext)),
            coverService,
            dataContext.Config.RootDirectory,
            new SystemExternalLauncher(),
            dataContext.ToastManager,
            ExploreCardVmBase.DefaultRunOnUiAsync,
            autoLoad: autoLoad,
            isReadOnlyDatabase: dataContext.IsReadOnlyDatabase,
            readerLauncher: new AvaloniaReaderWindowLauncher(dataContext))
    {
        _dataContext = dataContext;
    }

    public GalleryPageViewModel(
        IAssetMetaStore store,
        SkiaCoverService coverService,
        string? rootDirectory = null,
        IExternalLauncher? externalLauncher = null,
        ISukiToastManager? toastManager = null,
        Func<Action, Task>? runOnUiAsync = null,
        bool autoLoad = true,
        bool isReadOnlyDatabase = false,
        IReaderWindowLauncher? readerLauncher = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
        _externalLauncher = externalLauncher ?? new SystemExternalLauncher();
        _readerLauncher = readerLauncher ?? new FallbackExternalReaderLauncher(_externalLauncher);
        _toastManager = toastManager;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;
        IsReadOnlyDatabase = isReadOnlyDatabase;

        _rootDirectory = NormalizePathOrEmpty(rootDirectory);
        _currentFolderPath = _rootDirectory;

        SearchBox = new DslSearchBoxViewModel(_store, "zh-TW");
        SearchBox.SearchSubmitted += OnDslSearchSubmitted;
        SearchBox.QueryChanged += OnDslQueryChanged;

        DetailDrawer = new GalleryDetailDrawerViewModel(
            _store,
            isReadOnlyDatabase,
            () => RootDirectory,
            dsl => _ = ApplyExternalDslQueryAsync(dsl),
            NavigateToFolder,
            OpenWorkPathInReader,
            RevealPathInExplorer,
            OnDetailDrawerMutated,
            _toastManager,
            OpenPathWithDefaultApp);

        SortOptions =
        [
            new GallerySortFieldOption(GallerySortField.Name, "名稱（自然排序）"),
            new GallerySortFieldOption(GallerySortField.ModifiedTime, "修改時間"),
            new GallerySortFieldOption(GallerySortField.Size, "檔案大小"),
            new GallerySortFieldOption(GallerySortField.LastRead, "最近閱讀"),
            new GallerySortFieldOption(GallerySortField.ReadCount, "閱讀次數"),
        ];
        _selectedSortOption = SortOptions[0];

        ReadFilterOptions =
        [
            new GalleryReadFilterOption(GalleryReadFilter.All, "全部狀態"),
            new GalleryReadFilterOption(GalleryReadFilter.Unread, "● 未讀"),
            new GalleryReadFilterOption(GalleryReadFilter.Reading, "📖 閱讀中"),
            new GalleryReadFilterOption(GalleryReadFilter.Completed, "✓ 已完成"),
        ];
        _selectedReadFilterOption = ReadFilterOptions[0];

        if (autoLoad)
        {
            InitializationTask = ReloadAllAsync();
        }
        else
        {
            InitializationTask = Task.CompletedTask;
        }
    }

    public Task InitializationTask { get; private set; }

    public DslSearchBoxViewModel SearchBox { get; }

    public GalleryDetailDrawerViewModel DetailDrawer { get; }

    public bool IsReadOnlyDatabase { get; }

    public IReadOnlyList<GallerySortFieldOption> SortOptions { get; }

    public IReadOnlyList<GalleryReadFilterOption> ReadFilterOptions { get; }

    public BatchObservableCollection<GalleryNavNodeVm> SidebarNodes { get; } = [];

    public BatchObservableCollection<GalleryBreadcrumbItemVm> Breadcrumbs { get; } = [];

    public BatchObservableCollection<GalleryFuzzyRecommendationVm> FuzzyRecommendations { get; } = [];

    public BatchObservableCollection<GalleryItemVm> Items { get; } = [];

    public event Action? ScrollResetRequested;

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFoldersSidebarMode))]
    [NotifyPropertyChangedFor(nameof(IsAuthorsSidebarMode))]
    [NotifyPropertyChangedFor(nameof(IsTagsSidebarMode))]
    [NotifyPropertyChangedFor(nameof(IsMyLibrariesSidebarMode))]
    [NotifyPropertyChangedFor(nameof(SidebarHeaderTitle))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedVirtualLibrary))]
    private GallerySidebarMode _sidebarMode = GallerySidebarMode.Folders;

    [ObservableProperty]
    private string _sidebarFilterText = string.Empty;

    [ObservableProperty]
    private GalleryNavNodeVm? _selectedSidebarNode;

    [ObservableProperty]
    private bool _isSaveLibraryPromptOpen;

    [ObservableProperty]
    private string _newVirtualLibraryName = string.Empty;

    [ObservableProperty]
    private string _rootDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateUpFolder))]
    private string _currentFolderPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLargeCardMode))]
    [NotifyPropertyChangedFor(nameof(IsMediumCardMode))]
    [NotifyPropertyChangedFor(nameof(IsSmallCardMode))]
    [NotifyPropertyChangedFor(nameof(IsDetailListMode))]
    [NotifyPropertyChangedFor(nameof(IsCardGridMode))]
    [NotifyPropertyChangedFor(nameof(CardWidth))]
    [NotifyPropertyChangedFor(nameof(CardCoverHeight))]
    [NotifyPropertyChangedFor(nameof(CardCoverWidth))]
    private GalleryViewMode _viewMode = GalleryViewMode.LargeCard;

    [ObservableProperty]
    private GallerySortFieldOption _selectedSortOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionButtonText))]
    [NotifyPropertyChangedFor(nameof(SortDirectionTooltip))]
    private bool _isSortDescending;

    [ObservableProperty]
    private GalleryReadFilterOption _selectedReadFilterOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalFilterKeyword))]
    private string _localFilterKeyword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    private string _scopeTitle = "📚 漫畫庫畫廊";

    [ObservableProperty]
    private string _scopeSubtitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionBadgeTitle))]
    [NotifyPropertyChangedFor(nameof(PositionBadgeDetail))]
    private int _visibleRangeStart = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionBadgeTitle))]
    [NotifyPropertyChangedFor(nameof(PositionBadgeDetail))]
    private int _visibleRangeEnd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionBadgeDetail))]
    private double _scrollProgressPercent;

    [ObservableProperty]
    private bool _showBackToTopButton;

    public bool IsFoldersSidebarMode => SidebarMode == GallerySidebarMode.Folders;

    public bool IsAuthorsSidebarMode => SidebarMode == GallerySidebarMode.Authors;

    public bool IsTagsSidebarMode => SidebarMode == GallerySidebarMode.Tags;

    public bool IsMyLibrariesSidebarMode => SidebarMode == GallerySidebarMode.MyLibraries;

    public string SidebarHeaderTitle => SidebarMode switch
    {
        GallerySidebarMode.Folders => "📁 資料夾目錄",
        GallerySidebarMode.Authors => "👤 作者虛擬庫",
        GallerySidebarMode.Tags => "🏷️ 標籤虛擬庫",
        GallerySidebarMode.MyLibraries => "⭐ 我的閱讀清單",
        _ => "導覽",
    };

    public bool CanDeleteSelectedVirtualLibrary =>
        IsMyLibrariesSidebarMode && SelectedSidebarNode?.VirtualNode is MyVirtualLibraryNode;

    public bool IsLargeCardMode => ViewMode == GalleryViewMode.LargeCard;

    public bool IsMediumCardMode => ViewMode == GalleryViewMode.MediumCard;

    public bool IsSmallCardMode => ViewMode == GalleryViewMode.SmallCard;

    public bool IsDetailListMode => ViewMode == GalleryViewMode.DetailList;

    public bool IsCardGridMode => ViewMode != GalleryViewMode.DetailList;

    public double CardWidth => ViewMode switch
    {
        GalleryViewMode.LargeCard => 226,
        GalleryViewMode.MediumCard => 182,
        GalleryViewMode.SmallCard => 148,
        _ => 226,
    };

    /// <summary>卡片左右內距（與 XAML 卡片的 Padding 一致）。</summary>
    public const double CardHorizontalPadding = 10;

    /// <summary>
    /// 封面格子寬度 = 卡片寬度扣掉左右內距；固定寬度讓每張卡片的封面 / 佔位區塊一樣大
    /// （不固定時會隨標題長度或封面比例變寬變窄）。
    /// </summary>
    public double CardCoverWidth => CardWidth - (CardHorizontalPadding * 2);

    public double CardCoverHeight => ViewMode switch
    {
        GalleryViewMode.LargeCard => 264,
        GalleryViewMode.MediumCard => 212,
        GalleryViewMode.SmallCard => 168,
        _ => 264,
    };

    public string SortDirectionButtonText => IsSortDescending ? "↓ 降冪" : "↑ 升冪";

    public string SortDirectionTooltip => IsSortDescending ? "目前為降冪排序（點擊切換為升冪）" : "目前為升冪排序（點擊切換為降冪）";

    public bool HasLocalFilterKeyword => !string.IsNullOrWhiteSpace(LocalFilterKeyword);

    public bool IsDslSearchActive => !string.IsNullOrWhiteSpace(SearchBox.QueryText);

    public bool HasFuzzyRecommendations => FuzzyRecommendations.Count > 0;

    public bool HasBreadcrumbs => IsFoldersSidebarMode && !IsDslSearchActive && Breadcrumbs.Count > 0;

    public bool CanNavigateUpFolder
    {
        get
        {
            if (!IsFoldersSidebarMode || IsDslSearchActive || string.IsNullOrWhiteSpace(CurrentFolderPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(RootDirectory))
            {
                var parent = Directory.GetParent(CurrentFolderPath);
                return parent != null;
            }

            return !PathsEqual(CurrentFolderPath, RootDirectory)
                   && CurrentFolderPath.StartsWith(RootDirectory, StringComparison.OrdinalIgnoreCase);
        }
    }

    public int CurrentItemCount => Items.Count;

    public int TotalMatchingItems => _allFilteredSortedItems.Count;

    public bool HasItems => CurrentItemCount > 0;

    public bool IsEmptyState => !IsLoading && !HasItems;

    public bool HasPositionBadge => !IsLoading && TotalMatchingItems > 0;

    public string PositionBadgeTitle
    {
        get
        {
            if (TotalMatchingItems <= 0 || CurrentItemCount <= 0)
            {
                return string.Empty;
            }

            int start = Math.Clamp(VisibleRangeStart, 1, CurrentItemCount);
            int end = Math.Clamp(VisibleRangeEnd >= start ? VisibleRangeEnd : Math.Min(CurrentItemCount, 12), start, CurrentItemCount);
            return $"第 {start:N0}–{end:N0} 筆 / 共 {TotalMatchingItems:N0} 筆";
        }
    }

    public string PositionBadgeDetail
    {
        get
        {
            if (TotalMatchingItems <= 0)
            {
                return string.Empty;
            }

            int pct = (int)Math.Round(Math.Clamp(ScrollProgressPercent, 0, 100));
            return $"捲動進度 {pct}%";
        }
    }

    public string EmptyStateTitle => IsDslSearchActive
        ? "找不到符合搜尋區塊條件的作品"
        : HasLocalFilterKeyword || SelectedReadFilterOption.Filter != GalleryReadFilter.All
            ? "目前過濾條件下沒有符合的項目"
            : "此目錄或虛擬庫目前沒有項目";

    public string EmptyStateHint => IsDslSearchActive
        ? "可嘗試移除部分標籤區塊、參考上方「您是不是要找」相近建議，或點擊清空搜尋。"
        : "可在左側切換其他資料夾／作者／標籤節點，或於「⚙️ 系統設定」檢查漫畫庫根目錄。";

    partial void OnSidebarModeChanged(GallerySidebarMode value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        _ = ReloadSidebarAndItemsAsync();
    }

    partial void OnSidebarFilterTextChanged(string value)
    {
        if (_disposed)
        {
            return;
        }

        ApplySidebarFilterOnUi();
    }

    partial void OnSelectedSortOptionChanged(GallerySortFieldOption value)
    {
        if (_suppressReload || _disposed || value == null)
        {
            return;
        }

        ApplyFilterAndSortOnUi(resetScroll: true);
    }

    partial void OnIsSortDescendingChanged(bool value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        ApplyFilterAndSortOnUi(resetScroll: true);
    }

    partial void OnSelectedReadFilterOptionChanged(GalleryReadFilterOption value)
    {
        if (_suppressReload || _disposed || value == null)
        {
            return;
        }

        ApplyFilterAndSortOnUi(resetScroll: true);
    }

    partial void OnLocalFilterKeywordChanged(string value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        ApplyFilterAndSortOnUi(resetScroll: true);
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    private void SelectFoldersSidebar() => SidebarMode = GallerySidebarMode.Folders;

    [RelayCommand]
    private void SelectAuthorsSidebar() => SidebarMode = GallerySidebarMode.Authors;

    [RelayCommand]
    private void SelectTagsSidebar() => SidebarMode = GallerySidebarMode.Tags;

    [RelayCommand]
    private void SelectMyLibrariesSidebar() => SidebarMode = GallerySidebarMode.MyLibraries;

    [RelayCommand]
    private void SelectLargeCardView() => ViewMode = GalleryViewMode.LargeCard;

    [RelayCommand]
    private void SelectMediumCardView() => ViewMode = GalleryViewMode.MediumCard;

    [RelayCommand]
    private void SelectSmallCardView() => ViewMode = GalleryViewMode.SmallCard;

    [RelayCommand]
    private void SelectDetailListView() => ViewMode = GalleryViewMode.DetailList;

    [RelayCommand]
    private void ToggleSortDirection() => IsSortDescending = !IsSortDescending;

    [RelayCommand]
    private void ToggleDetailDrawer()
    {
        if (DetailDrawer.IsOpen)
        {
            DetailDrawer.IsOpen = false;
            return;
        }

        if (_selectedGalleryItem != null)
        {
            DetailDrawer.InspectItem(_selectedGalleryItem, openDrawer: true);
        }
        else if (SelectedSidebarNode?.VirtualNode is AuthorVirtualNode authorNode)
        {
            DetailDrawer.InspectAuthorNode(authorNode.AuthorId, authorNode.MainName, openDrawer: true);
        }
        else if (Items.Count > 0)
        {
            OnItemInspectDetail(Items[0]);
        }
        else
        {
            DetailDrawer.IsOpen = true;
        }
    }

    [RelayCommand]
    private void ToggleSaveLibraryPrompt()
    {
        if (!IsDslSearchActive)
        {
            return;
        }

        if (!IsSaveLibraryPromptOpen && string.IsNullOrWhiteSpace(NewVirtualLibraryName))
        {
            NewVirtualLibraryName = SearchBox.QueryText;
        }
        IsSaveLibraryPromptOpen = !IsSaveLibraryPromptOpen;
    }

    [RelayCommand]
    private void SaveCurrentSearchAsVirtualLibrary()
    {
        var dsl = SearchBox.QueryText?.Trim();
        if (string.IsNullOrWhiteSpace(dsl))
        {
            ShowToast("無法建立閱讀清單", "請先輸入 DSL 搜尋條件。", NotificationType.Warning);
            return;
        }

        if (!DetailDrawer.EnsureWritable("儲存為閱讀清單"))
        {
            IsSaveLibraryPromptOpen = false;
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewVirtualLibraryName) ? dsl : NewVirtualLibraryName.Trim();
        _store.CreateVirtualLibrary(name, dsl, null);
        NewVirtualLibraryName = string.Empty;
        IsSaveLibraryPromptOpen = false;
        ShowToast("已儲存閱讀清單", $"已新增「{name}」至「⭐ 我的閱讀清單」。", NotificationType.Success);

        if (IsMyLibrariesSidebarMode)
        {
            _ = ReloadSidebarAndItemsAsync();
        }
        else if (DetailDrawer.IsWorkKind && DetailDrawer.HasTarget)
        {
            var selected = _selectedGalleryItem;
            if (selected != null)
            {
                DetailDrawer.InspectItem(selected, openDrawer: DetailDrawer.IsOpen);
            }
        }
    }

    [RelayCommand]
    private void DeleteSelectedVirtualLibrary()
    {
        if (SelectedSidebarNode?.VirtualNode is not MyVirtualLibraryNode myNode)
        {
            return;
        }

        if (!DetailDrawer.EnsureWritable("刪除自訂閱讀清單"))
        {
            return;
        }

        _store.DeleteVirtualLibrary(myNode.Library.Id);
        ShowToast("已刪除閱讀清單", $"已移除「{myNode.Library.Name}」。", NotificationType.Information);
        _ = ReloadSidebarAndItemsAsync();
    }

    [RelayCommand]
    private void ClearLocalFilter() => LocalFilterKeyword = string.Empty;

    [RelayCommand]
    private void NavigateUpFolder()
    {
        if (!CanNavigateUpFolder)
        {
            return;
        }

        var parent = Directory.GetParent(CurrentFolderPath)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent))
        {
            NavigateToFolder(parent);
        }
    }

    [RelayCommand]
    public Task RefreshAsync() => ReloadAllAsync();

    public Task ReloadAllAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (_dataContext != null)
        {
            var latestRoot = NormalizePathOrEmpty(_dataContext.Config.RootDirectory);
            if (!PathsEqual(RootDirectory, latestRoot))
            {
                RootDirectory = latestRoot;
                if (string.IsNullOrWhiteSpace(CurrentFolderPath))
                {
                    CurrentFolderPath = latestRoot;
                }
            }
        }

        _cachedSnapshot = null;
        var task = ReloadSidebarAndItemsAsync();
        InitializationTask = task;
        return task;
    }

    private ExploreCorpusSnapshot GetOrLoadSnapshot()
    {
        var snap = _cachedSnapshot;
        if (snap != null)
        {
            return snap;
        }

        snap = _store.GetExploreCorpusSnapshot("zh-TW");
        _cachedSnapshot = snap;
        return snap;
    }

    public void NavigateToFolder(string folderPath)
    {
        if (_disposed || string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        var normalized = NormalizePathOrEmpty(folderPath);
        _suppressReload = true;
        try
        {
            if (IsDslSearchActive)
            {
                SearchBox.SetQueryFromExternal(string.Empty, triggerSubmit: false);
            }
            SidebarMode = GallerySidebarMode.Folders;
            CurrentFolderPath = normalized;
        }
        finally
        {
            _suppressReload = false;
        }

        _ = ReloadSidebarAndItemsAsync();
    }

    /// <summary>由外部（例如頂部全域搜尋列或探索牆跳轉）套用 DSL 查詢並立即執行搜尋。</summary>
    public Task ApplyExternalDslQueryAsync(string? dslQuery)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        SearchBox.SetQueryFromExternal(dslQuery, triggerSubmit: false);
        return LoadCurrentScopeItemsAsync();
    }

    public Task RequestItemCoverAsync(GalleryItemVm? item)
    {
        if (_disposed || item == null)
        {
            return Task.CompletedTask;
        }

        return item.EnsureCoverLoadedAsync(_coverService, _coverLoadSemaphore);
    }

    /// <summary>
    /// 虛擬化清單建立（或重用）一張卡片時呼叫：移出回收佇列並確保封面已載入。
    /// 載入綁定這張卡片的取消權杖，卡片被回收時（<see cref="NotifyItemRecycled"/>）會取消。需在 UI 執行緒呼叫。
    /// </summary>
    public Task NotifyItemRealized(GalleryItemVm? item)
    {
        if (item == null)
        {
            return Task.CompletedTask;
        }

        if (_recycledCoverNodes.Remove(item, out var node))
        {
            _recycledCoverQueue.Remove(node);
        }

        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (!_realizedCoverLoads.TryGetValue(item, out var cts))
        {
            cts = new CancellationTokenSource();
            _realizedCoverLoads[item] = cts;
        }

        return item.EnsureCoverLoadedAsync(_coverService, _coverLoadSemaphore, cts.Token);
    }

    /// <summary>取消這張卡片還沒完成的封面載入（已載入的封面不受影響）。</summary>
    private void CancelRealizedCoverLoad(GalleryItemVm item)
    {
        if (_realizedCoverLoads.Remove(item, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// 虛擬化清單回收一張卡片時呼叫：放進回收佇列，佇列超過 <see cref="RecycledCoverRetention"/> 時
    /// 放掉最舊卡片的封面，讓封面記憶體只跟「畫面附近的卡片數」有關，與作品總數無關。需在 UI 執行緒呼叫。
    /// </summary>
    public void NotifyItemRecycled(GalleryItemVm? item)
    {
        if (item == null)
        {
            return;
        }

        // 捲走了：還在排隊或解碼中的封面不必再載
        CancelRealizedCoverLoad(item);

        if (_recycledCoverNodes.Remove(item, out var existing))
        {
            _recycledCoverQueue.Remove(existing);
        }

        _recycledCoverNodes[item] = _recycledCoverQueue.AddLast(item);

        while (_recycledCoverQueue.Count > RecycledCoverRetention && _recycledCoverQueue.First is { } oldest)
        {
            _recycledCoverQueue.RemoveFirst();
            _recycledCoverNodes.Remove(oldest.Value);
            oldest.Value.ReleaseCover();
        }
    }

    public void UpdateViewportRange(int firstVisibleOneBased, int lastVisibleOneBased, double offsetY)
    {
        int loaded = CurrentItemCount;
        int total = TotalMatchingItems;

        if (loaded <= 0 || total <= 0)
        {
            VisibleRangeStart = 0;
            VisibleRangeEnd = 0;
            ScrollProgressPercent = 0;
            ShowBackToTopButton = false;
            return;
        }

        int start = Math.Clamp(firstVisibleOneBased, 1, loaded);
        int end = Math.Clamp(lastVisibleOneBased >= start ? lastVisibleOneBased : start, start, loaded);
        VisibleRangeStart = start;
        VisibleRangeEnd = end;
        ScrollProgressPercent = Math.Clamp((double)end / total * 100.0, 0, 100);
        ShowBackToTopButton = offsetY > 220;
    }

    private void OnDslSearchSubmitted(string query)
    {
        if (_disposed)
        {
            return;
        }

        _ = LoadCurrentScopeItemsAsync();
    }

    private void OnDslQueryChanged(string query)
    {
        if (_disposed)
        {
            return;
        }

        OnPropertyChanged(nameof(IsDslSearchActive));
        OnPropertyChanged(nameof(HasBreadcrumbs));
        OnPropertyChanged(nameof(CanNavigateUpFolder));

        if (string.IsNullOrWhiteSpace(query))
        {
            _ = LoadCurrentScopeItemsAsync();
        }
    }

    private async Task ReloadSidebarAndItemsAsync()
    {
        var cts = ReplaceCts(ref _loadCts);
        var ct = cts.Token;
        int version = Interlocked.Increment(ref _loadVersion);

        await _runOnUiAsync(() => IsLoading = true).ConfigureAwait(false);

        try
        {
            var mode = SidebarMode;
            var rootDir = RootDirectory;
            var curFolder = string.IsNullOrWhiteSpace(CurrentFolderPath) ? rootDir : CurrentFolderPath;
            string dsl = SearchBox.QueryText?.Trim() ?? string.Empty;
            string? previousSelectedKey = SelectedSidebarNode?.Key;

            // 背景一次算好側欄、要顯示的範圍與作品清單，UI 只套用一次：
            // 側欄、麵包屑與作品清單在同一次更新完成，切換目錄時不會先換側欄、下一拍才換作品
            var resolved = await Task.Run(() =>
            {
                var sidebarNodes = BuildSidebarNodes(mode, rootDir, curFolder, ct);
                ct.ThrowIfCancellationRequested();

                string scopeFolder = curFolder ?? string.Empty;
                string? scopeNodeKey = null;
                bool keepPreviousSelection = false;
                GalleryNavNodeVm? scopeNode = null;
                if (mode == GallerySidebarMode.Folders)
                {
                    if (string.IsNullOrWhiteSpace(scopeFolder)
                        && sidebarNodes.Count > 0
                        && !string.IsNullOrWhiteSpace(sidebarNodes[0].FolderPath))
                    {
                        scopeFolder = sidebarNodes[0].FolderPath!;
                    }
                }
                else
                {
                    var match = previousSelectedKey == null
                        ? null
                        : sidebarNodes.FirstOrDefault(n => string.Equals(n.Key, previousSelectedKey, StringComparison.Ordinal));
                    keepPreviousSelection = match != null;
                    scopeNode = match ?? sidebarNodes.FirstOrDefault();
                    scopeNodeKey = scopeNode?.Key;
                }

                var payload = ResolveScopePayload(dsl, mode, scopeFolder, scopeNode, ct);
                return (SidebarNodes: sidebarNodes, ScopeFolder: scopeFolder, ScopeNodeKey: scopeNodeKey,
                    KeepPreviousSelection: keepPreviousSelection, Payload: payload);
            }, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || version != Volatile.Read(ref _loadVersion) || _disposed)
            {
                return;
            }

            await _runOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested || version != Volatile.Read(ref _loadVersion) || _disposed)
                {
                    return;
                }

                // 沿用內容沒變的節點，側欄清單用差量同步：重整後節點沒變時，側欄上的按鈕完全不重建
                var nodes = MergeSidebarNodes(_allSidebarNodes, resolved.SidebarNodes);
                _allSidebarNodes = nodes;
                ApplySidebarFilterOnUi();

                if (mode == GallerySidebarMode.Folders)
                {
                    if (!string.IsNullOrWhiteSpace(resolved.ScopeFolder))
                    {
                        CurrentFolderPath = resolved.ScopeFolder;
                    }
                }
                else
                {
                    // 合併後的清單可能沿用舊節點實例，依 key 找回清單中的節點
                    var match = resolved.ScopeNodeKey == null
                        ? null
                        : nodes.FirstOrDefault(n => string.Equals(n.Key, resolved.ScopeNodeKey, StringComparison.Ordinal));
                    if (resolved.KeepPreviousSelection && match != null)
                    {
                        // 原本選取的節點還在：改指向清單中的節點並恢復高亮（重建的節點預設未選取）
                        ApplySidebarSelection(match);
                    }
                    else
                    {
                        SelectSidebarNodeInternal(match ?? nodes.FirstOrDefault(), reloadItems: false);
                    }
                }

                ApplyScopePayloadOnUi(dsl, mode, CurrentFolderPath, SelectedSidebarNode, resolved.Payload);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                IsLoading = false;
                ShowToast("載入畫廊失敗", ex.Message, NotificationType.Error);
            }).ConfigureAwait(false);
        }
    }

    private async Task LoadCurrentScopeItemsAsync()
    {
        var cts = ReplaceCts(ref _loadCts);
        var ct = cts.Token;
        int version = Interlocked.Increment(ref _loadVersion);

        await _runOnUiAsync(() => IsLoading = true).ConfigureAwait(false);

        try
        {
            await LoadCurrentScopeItemsCoreAsync(version, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                IsLoading = false;
                ShowToast("查詢畫廊失敗", ex.Message, NotificationType.Error);
            }).ConfigureAwait(false);
        }
    }

    private async Task LoadCurrentScopeItemsCoreAsync(int version, CancellationToken ct)
    {
        string dsl = SearchBox.QueryText?.Trim() ?? string.Empty;
        var mode = SidebarMode;
        var curFolder = CurrentFolderPath;
        var selectedNode = SelectedSidebarNode;

        var payload = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return ResolveScopePayload(dsl, mode, curFolder, selectedNode, ct);
        }, ct).ConfigureAwait(false);

        if (ct.IsCancellationRequested || version != Volatile.Read(ref _loadVersion) || _disposed)
        {
            return;
        }

        await _runOnUiAsync(() =>
        {
            if (ct.IsCancellationRequested || version != Volatile.Read(ref _loadVersion) || _disposed)
            {
                return;
            }

            ApplyScopePayloadOnUi(dsl, mode, curFolder, selectedNode, payload);
        }).ConfigureAwait(false);
    }

    /// <summary>把背景算好的範圍內容套用到畫面（需在 UI 執行緒呼叫）：標題、作品清單、推薦、麵包屑與捲動位置。</summary>
    private void ApplyScopePayloadOnUi(
        string dsl,
        GallerySidebarMode mode,
        string? curFolder,
        GalleryNavNodeVm? selectedNode,
        (string Title, string Subtitle, IReadOnlyList<GalleryItemVm> Items, IReadOnlyList<SearchRecommendation> Recommendations) payload)
    {
        // 同一個範圍（同資料夾 / 同側欄節點 / 同搜尋）重新載入：沿用沒變的項目、保留捲動位置
        string scopeKey = BuildScopeKey(dsl, mode, curFolder, selectedNode);
        bool sameScope = string.Equals(scopeKey, _loadedScopeKey, StringComparison.OrdinalIgnoreCase);
        _loadedScopeKey = scopeKey;

        ScopeTitle = payload.Title;
        ScopeSubtitle = payload.Subtitle;
        _allRawItems = sameScope
            ? MergeWithExistingItems(_allRawItems, payload.Items)
            : payload.Items;

        var recVms = payload.Recommendations
            .Select(r => new GalleryFuzzyRecommendationVm(r, OnApplyFuzzyRecommendation))
            .ToList();
        FuzzyRecommendations.ReplaceAll(recVms);
        OnPropertyChanged(nameof(HasFuzzyRecommendations));
        OnPropertyChanged(nameof(IsDslSearchActive));
        OnPropertyChanged(nameof(HasBreadcrumbs));
        OnPropertyChanged(nameof(CanNavigateUpFolder));

        RebuildBreadcrumbsOnUi();
        ApplyFilterAndSortOnUi(resetScroll: !sameScope);
        IsLoading = false;
    }

    /// <summary>目前範圍的識別字串：搜尋優先，其次資料夾模式的目前資料夾，或側欄選取的節點。</summary>
    private static string BuildScopeKey(string dsl, GallerySidebarMode mode, string? curFolder, GalleryNavNodeVm? selectedNode)
    {
        if (!string.IsNullOrWhiteSpace(dsl))
        {
            return $"dsl|{dsl}";
        }

        return mode == GallerySidebarMode.Folders
            ? $"folder|{NormalizePathOrEmpty(curFolder)}"
            : $"{mode}|{selectedNode?.Key}";
    }

    /// <summary>
    /// 同一範圍重新載入時，把新查到的項目與畫面上的舊項目依路徑比對（需在 UI 執行緒呼叫）：
    /// <list type="bullet">
    /// <item>內容完全相同：直接沿用舊項目（封面、選取狀態、清單上的卡片都不動）。</item>
    /// <item>內容有變（例如讀完、改了標題）：用新項目，但縮圖相同時先沿用舊封面，並接手選取狀態與詳情抽屜。</item>
    /// </list>
    /// </summary>
    private IReadOnlyList<GalleryItemVm> MergeWithExistingItems(
        IReadOnlyList<GalleryItemVm> existingItems,
        IReadOnlyList<GalleryItemVm> freshItems)
    {
        if (existingItems.Count == 0 || freshItems.Count == 0)
        {
            return freshItems;
        }

        var existingByPath = new Dictionary<string, GalleryItemVm>(existingItems.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var item in existingItems)
        {
            existingByPath.TryAdd(item.Path, item);
        }

        var merged = new List<GalleryItemVm>(freshItems.Count);
        foreach (var fresh in freshItems)
        {
            if (!existingByPath.TryGetValue(fresh.Path, out var existing))
            {
                merged.Add(fresh);
                continue;
            }

            if (existing.HasSameContent(fresh))
            {
                merged.Add(existing);
                continue;
            }

            fresh.CoverSlot.TryAdoptImageFrom(existing.CoverSlot);
            if (ReferenceEquals(_selectedGalleryItem, existing))
            {
                existing.IsSelected = false;
                fresh.IsSelected = true;
                _selectedGalleryItem = fresh;
            }

            DetailDrawer.RetargetItem(existing, fresh);
            merged.Add(fresh);
        }

        return merged;
    }

    private (string Title, string Subtitle, IReadOnlyList<GalleryItemVm> Items, IReadOnlyList<SearchRecommendation> Recommendations)
        ResolveScopePayload(
            string dsl,
            GallerySidebarMode mode,
            string curFolder,
            GalleryNavNodeVm? selectedNode,
            CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dsl))
        {
            var query = SearchQueryParser.Parse(dsl);
            var executor = new SearchExecutor(_store, "zh-TW");
            var matchedPaths = executor.Execute(query);
            var recs = matchedPaths.Count == 0
                ? SearchFuzzyRecommender.FindRecommendations(_store, dsl, query, "zh-TW", maxResults: 4)
                : Array.Empty<SearchRecommendation>();

            var items = BuildGalleryItemsFromPaths(matchedPaths, ct);
            return (
                $"🔍 搜尋結果：{dsl}",
                $"共命中 {items.Count:N0} 部作品",
                items,
                recs);
        }

        if (mode == GallerySidebarMode.Folders)
        {
            var folder = string.IsNullOrWhiteSpace(curFolder) ? RootDirectory : curFolder;
            var items = BuildFolderChildrenItems(folder, ct);
            string folderTitle = string.IsNullOrWhiteSpace(folder)
                ? "📁 漫畫庫根目錄"
                : $"📁 {GetFolderDisplayName(folder)}";
            string sub = string.IsNullOrWhiteSpace(folder)
                ? $"共 {items.Count:N0} 個項目"
                : $"{folder}（共 {items.Count:N0} 個項目）";
            return (folderTitle, sub, items, Array.Empty<SearchRecommendation>());
        }

        if (selectedNode?.VirtualNode is { } vnode)
        {
            var paths = vnode.ResolveWorks(_store);
            var items = BuildGalleryItemsFromPaths(paths, ct);
            return (
                $"{selectedNode.Icon} {selectedNode.DisplayName}",
                $"虛擬庫檢視 · 共 {items.Count:N0} 部作品",
                items,
                Array.Empty<SearchRecommendation>());
        }

        return (SidebarHeaderTitle, "請從左側選擇一個節點", Array.Empty<GalleryItemVm>(), Array.Empty<SearchRecommendation>());
    }

    private List<GalleryNavNodeVm> BuildSidebarNodes(
        GallerySidebarMode mode,
        string rootDir,
        string curFolder,
        CancellationToken ct)
    {
        var nodes = new List<GalleryNavNodeVm>();

        switch (mode)
        {
            case GallerySidebarMode.Folders:
            {
                var baseDir = !string.IsNullOrWhiteSpace(curFolder) ? curFolder : rootDir;
                var subFolders = CollectSubFolders(baseDir);
                var authorFolderSet = BuildAuthorFolderSet(GetOrLoadSnapshot());

                if (!string.IsNullOrWhiteSpace(rootDir))
                {
                    nodes.Add(new GalleryNavNodeVm(
                        $"folder:{rootDir}",
                        "🏠",
                        "漫畫庫根目錄",
                        GetFolderDisplayName(rootDir),
                        rootDir,
                        null,
                        PathsEqual(baseDir, rootDir),
                        OnSidebarNodeClicked));
                }

                foreach (var dir in subFolders.OrderBy(d => d.Name, NaturalStringComparer.Instance))
                {
                    ct.ThrowIfCancellationRequested();
                    bool isAuthorDir = authorFolderSet.Contains(NormalizePathOrEmpty(dir.Path));
                    nodes.Add(new GalleryNavNodeVm(
                        $"folder:{dir.Path}",
                        isAuthorDir ? "👤" : "📁",
                        dir.Name,
                        isAuthorDir ? "作者" : string.Empty,
                        dir.Path,
                        null,
                        PathsEqual(baseDir, dir.Path),
                        OnSidebarNodeClicked));
                }
                break;
            }

            case GallerySidebarMode.Authors:
            {
                foreach (var authorNode in VirtualLibraryProvider.GetAuthorNodes(_store))
                {
                    ct.ThrowIfCancellationRequested();
                    nodes.Add(new GalleryNavNodeVm(
                        $"author:{authorNode.AuthorId}",
                        "👤",
                        authorNode.DisplayName,
                        $"{authorNode.WorkCount:N0}",
                        null,
                        authorNode,
                        false,
                        OnSidebarNodeClicked));
                }
                break;
            }

            case GallerySidebarMode.Tags:
            {
                var tagNodes = VirtualLibraryProvider.GetTagNodes(_store);
                var transMap = tagNodes.Count > 0
                    ? _store.GetTagTranslations(tagNodes.Select(t => t.TagId).ToList(), "zh-TW")
                    : new Dictionary<long, string>();

                foreach (var tagNode in tagNodes)
                {
                    ct.ThrowIfCancellationRequested();
                    transMap.TryGetValue(tagNode.TagId, out var zh);
                    var display = !string.IsNullOrEmpty(zh) && !string.Equals(zh, tagNode.Name, StringComparison.OrdinalIgnoreCase)
                        ? $"{zh} ({tagNode.Name})"
                        : tagNode.Name;

                    nodes.Add(new GalleryNavNodeVm(
                        $"tag:{tagNode.TagId}",
                        "🏷️",
                        display,
                        $"{tagNode.WorkCount:N0}",
                        null,
                        tagNode,
                        false,
                        OnSidebarNodeClicked));
                }
                break;
            }

            case GallerySidebarMode.MyLibraries:
            {
                foreach (var libNode in VirtualLibraryProvider.GetMyLibraryNodes(_store))
                {
                    ct.ThrowIfCancellationRequested();
                    var pinPrefix = libNode.Library.Pinned ? "📌 " : string.Empty;
                    nodes.Add(new GalleryNavNodeVm(
                        $"mylib:{libNode.Library.Id}",
                        "⭐",
                        $"{pinPrefix}{libNode.DisplayName}",
                        libNode.Library.Dsl,
                        null,
                        libNode,
                        false,
                        OnSidebarNodeClicked));
                }
                break;
            }
        }

        return nodes;
    }

    private List<GalleryItemVm> BuildFolderChildrenItems(string? folderPath, CancellationToken ct)
    {
        var entryMap = new Dictionary<string, (string Path, string Name, bool IsFolder, long Size, long Mtime, string? ThumbKey)>(
            StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            // 1. 先從 SQLite catalog 撈直接子項目（排除孫層以下路徑）
            foreach (var child in _store.ListChildren(folderPath))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsDirectChildPath(folderPath, child.Path))
                {
                    continue;
                }

                if (!child.IsFolder && !IsSupportedGalleryFile(child.Name))
                {
                    continue;
                }

                entryMap[child.Path] = (
                    child.Path,
                    child.Name,
                    child.IsFolder,
                    child.FileSize ?? 0,
                    child.Mtime,
                    child.CoverThumbKey);
            }

            // 2. 若實體目錄存在於磁碟，補充尚未入庫的實體子資料夾與壓縮檔／PDF
            if (Directory.Exists(folderPath))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    foreach (var subDir in dirInfo.EnumerateDirectories())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!entryMap.ContainsKey(subDir.FullName))
                        {
                            long mtime = new DateTimeOffset(subDir.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                            entryMap[subDir.FullName] = (
                                subDir.FullName,
                                subDir.Name,
                                true,
                                0,
                                mtime,
                                null);
                        }
                    }

                    foreach (var file in dirInfo.EnumerateFiles())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!IsSupportedGalleryFile(file.Name))
                        {
                            continue;
                        }

                        if (!entryMap.ContainsKey(file.FullName))
                        {
                            long mtime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                            entryMap[file.FullName] = (
                                file.FullName,
                                file.Name,
                                false,
                                file.Length,
                                mtime,
                                null);
                        }
                    }
                }
                catch
                {
                    // 忽略無權限讀取的子目錄例外，保留 SQLite 已知項目
                }
            }
        }
        else
        {
            // 若尚未設定 RootDirectory，退回列出資料庫中所有已索引檔案
            return BuildGalleryItemsFromPaths(_store.ListAllAssetPaths(), ct);
        }

        if (entryMap.Count == 0)
        {
            return [];
        }

        var snapshot = GetOrLoadSnapshot();
        var authorFolderSet = BuildAuthorFolderSet(snapshot);
        var workRowByPath = new Dictionary<string, ExploreWorkRow>(snapshot.Works.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var w in snapshot.Works)
        {
            workRowByPath[w.Path] = w;
        }

        var items = new List<GalleryItemVm>(entryMap.Count);

        foreach (var entry in entryMap.Values)
        {
            ct.ThrowIfCancellationRequested();
            workRowByPath.TryGetValue(entry.Path, out var workRow);
            bool isAuthorFolder = entry.IsFolder && authorFolderSet.Contains(NormalizePathOrEmpty(entry.Path));
            var readStatus = workRow != null && Enum.IsDefined(typeof(ReadStatus), workRow.ReadStatus)
                ? (ReadStatus)workRow.ReadStatus
                : ReadStatus.Unread;

            var sortMeta = new GalleryItemSortMetadata(
                entry.Path,
                entry.Name,
                workRow?.Title,
                entry.IsFolder,
                entry.Size,
                NormalizeTimestampMs(entry.Mtime > 0 ? entry.Mtime : (workRow?.AddedAt ?? 0)),
                readStatus,
                workRow?.ReadCount ?? 0,
                workRow?.LastRead);

            items.Add(new GalleryItemVm(
                sortMeta,
                workRow?.AuthorName,
                entry.ThumbKey ?? workRow?.CoverThumbKey,
                workRow?.PageCount,
                workRow?.Language,
                isAuthorFolder,
                OnItemActivated,
                OnItemRevealInExplorer,
                _runOnUiAsync,
                OnItemInspectDetail,
                OnItemCoverLoaded,
                OnItemOpenInReader,
                OnItemOpenExternal));
        }

        return items;
    }

    private List<GalleryItemVm> BuildGalleryItemsFromPaths(IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        var snapshot = GetOrLoadSnapshot();
        var authorFolderSet = BuildAuthorFolderSet(snapshot);
        var workRowByPath = new Dictionary<string, ExploreWorkRow>(snapshot.Works.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var w in snapshot.Works)
        {
            workRowByPath[w.Path] = w;
        }

        var assetEntries = _store.ListAssetEntries();
        var assetByPath = new Dictionary<string, AssetCatalogEntry>(assetEntries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var a in assetEntries)
        {
            assetByPath[a.Path] = a;
        }

        var items = new List<GalleryItemVm>(paths.Count);

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            assetByPath.TryGetValue(path, out var asset);
            workRowByPath.TryGetValue(path, out var workRow);

            string name = asset?.Name ?? workRow?.FileName ?? System.IO.Path.GetFileName(path);
            bool isFolder = asset?.IsFolder ?? false;
            bool isAuthorFolder = isFolder && authorFolderSet.Contains(NormalizePathOrEmpty(path));
            long size = asset?.FileSize ?? 0;
            long mtime = NormalizeTimestampMs(workRow?.AddedAt ?? 0);
            var readStatus = workRow != null && Enum.IsDefined(typeof(ReadStatus), workRow.ReadStatus)
                ? (ReadStatus)workRow.ReadStatus
                : ReadStatus.Unread;

            var sortMeta = new GalleryItemSortMetadata(
                path,
                name,
                workRow?.Title,
                isFolder,
                size,
                mtime,
                readStatus,
                workRow?.ReadCount ?? 0,
                workRow?.LastRead);

            items.Add(new GalleryItemVm(
                sortMeta,
                workRow?.AuthorName,
                workRow?.CoverThumbKey,
                workRow?.PageCount,
                workRow?.Language,
                isAuthorFolder,
                OnItemActivated,
                OnItemRevealInExplorer,
                _runOnUiAsync,
                OnItemInspectDetail,
                OnItemCoverLoaded,
                OnItemOpenInReader,
                OnItemOpenExternal));
        }

        return items;
    }

    private static HashSet<string> BuildAuthorFolderSet(ExploreCorpusSnapshot snapshot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var author in snapshot.Authors)
        {
            if (!string.IsNullOrWhiteSpace(author.FolderPath))
            {
                set.Add(NormalizePathOrEmpty(author.FolderPath));
            }
        }
        return set;
    }

    private List<(string Path, string Name)> CollectSubFolders(string? baseDir)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            foreach (var child in _store.ListChildren(baseDir))
            {
                if (child.IsFolder && IsDirectChildPath(baseDir, child.Path))
                {
                    map[child.Path] = child.Name;
                }
            }

            if (Directory.Exists(baseDir))
            {
                try
                {
                    foreach (var d in new DirectoryInfo(baseDir).EnumerateDirectories())
                    {
                        map.TryAdd(d.FullName, d.Name);
                    }
                }
                catch
                {
                }
            }
        }

        return map.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private void ApplySidebarFilterOnUi()
    {
        var kw = SidebarFilterText?.Trim();
        if (string.IsNullOrEmpty(kw))
        {
            SidebarNodes.SyncTo(_allSidebarNodes);
            return;
        }

        var filtered = _allSidebarNodes
            .Where(n => n.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || n.SubText.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .ToList();
        SidebarNodes.SyncTo(filtered);
    }

    /// <summary>
    /// 重建側欄節點時沿用內容相同的舊節點（需在 UI 執行緒呼叫）。
    /// 沿用的節點接手新節點的選取狀態（資料夾模式依目前資料夾決定）；其他模式的選取之後再補。
    /// </summary>
    private static IReadOnlyList<GalleryNavNodeVm> MergeSidebarNodes(
        IReadOnlyList<GalleryNavNodeVm> existingNodes,
        IReadOnlyList<GalleryNavNodeVm> freshNodes)
    {
        if (existingNodes.Count == 0 || freshNodes.Count == 0)
        {
            return freshNodes;
        }

        var existingByKey = new Dictionary<string, GalleryNavNodeVm>(existingNodes.Count, StringComparer.Ordinal);
        foreach (var node in existingNodes)
        {
            existingByKey.TryAdd(node.Key, node);
        }

        var merged = new List<GalleryNavNodeVm>(freshNodes.Count);
        foreach (var fresh in freshNodes)
        {
            if (existingByKey.TryGetValue(fresh.Key, out var existing) && existing.HasSameContent(fresh))
            {
                existing.IsSelected = fresh.IsSelected;
                merged.Add(existing);
            }
            else
            {
                merged.Add(fresh);
            }
        }

        return merged;
    }

    /// <summary>只更新側欄選取與高亮，不觸發重新載入或詳情抽屜。</summary>
    private void ApplySidebarSelection(GalleryNavNodeVm node)
    {
        if (!ReferenceEquals(SelectedSidebarNode, node))
        {
            SelectedSidebarNode = node;
            OnPropertyChanged(nameof(CanDeleteSelectedVirtualLibrary));
        }

        foreach (var item in _allSidebarNodes)
        {
            item.IsSelected = ReferenceEquals(item, node);
        }
    }

    private void OnSidebarNodeClicked(GalleryNavNodeVm node)
    {
        if (_disposed)
        {
            return;
        }

        if (IsDslSearchActive)
        {
            SearchBox.SetQueryFromExternal(string.Empty, triggerSubmit: false);
        }

        if (SidebarMode == GallerySidebarMode.Folders && !string.IsNullOrWhiteSpace(node.FolderPath))
        {
            NavigateToFolder(node.FolderPath);
            return;
        }

        SelectSidebarNodeInternal(node, reloadItems: true);
    }

    private void SelectSidebarNodeInternal(GalleryNavNodeVm? node, bool reloadItems)
    {
        SelectedSidebarNode = node;
        OnPropertyChanged(nameof(CanDeleteSelectedVirtualLibrary));
        foreach (var item in _allSidebarNodes)
        {
            item.IsSelected = ReferenceEquals(item, node);
        }

        if (DetailDrawer.IsOpen && node?.VirtualNode is AuthorVirtualNode authorNode)
        {
            DetailDrawer.InspectAuthorNode(authorNode.AuthorId, authorNode.MainName, openDrawer: true);
        }

        if (reloadItems)
        {
            _ = LoadCurrentScopeItemsAsync();
        }
    }

    private void RebuildBreadcrumbsOnUi()
    {
        if (!IsFoldersSidebarMode || string.IsNullOrWhiteSpace(CurrentFolderPath))
        {
            Breadcrumbs.Clear();
            OnPropertyChanged(nameof(HasBreadcrumbs));
            return;
        }

        var crumbs = new List<GalleryBreadcrumbItemVm>();
        var root = RootDirectory;

        if (!string.IsNullOrWhiteSpace(root) && CurrentFolderPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            bool atRoot = PathsEqual(CurrentFolderPath, root);
            crumbs.Add(new GalleryBreadcrumbItemVm("🏠 根目錄", root, atRoot, NavigateToFolder));

            if (!atRoot)
            {
                var rel = CurrentFolderPath[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                var segments = rel.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
                string running = root;
                for (int i = 0; i < segments.Length; i++)
                {
                    running = System.IO.Path.Combine(running, segments[i]);
                    crumbs.Add(new GalleryBreadcrumbItemVm(
                        segments[i],
                        running,
                        i == segments.Length - 1,
                        NavigateToFolder));
                }
            }
        }
        else
        {
            crumbs.Add(new GalleryBreadcrumbItemVm(
                GetFolderDisplayName(CurrentFolderPath),
                CurrentFolderPath,
                isCurrent: true,
                NavigateToFolder));
        }

        Breadcrumbs.ReplaceAll(crumbs);
        OnPropertyChanged(nameof(HasBreadcrumbs));
        OnPropertyChanged(nameof(CanNavigateUpFolder));
    }

    private void ApplyFilterAndSortOnUi(bool resetScroll)
    {
        var statusFilter = SelectedReadFilterOption?.Filter ?? GalleryReadFilter.All;
        var keyword = string.IsNullOrWhiteSpace(LocalFilterKeyword) ? null : LocalFilterKeyword.Trim();
        var sortField = SelectedSortOption?.Field ?? GallerySortField.Name;
        bool desc = IsSortDescending;

        var filtered = _allRawItems
            .Where(item => GallerySortFilterHelper.MatchesFilter(item.SortMetadata, statusFilter, keyword))
            .ToList();

        filtered.Sort((a, b) => GallerySortFilterHelper.Compare(a.SortMetadata, b.SortMetadata, sortField, desc));

        // 清單已虛擬化（只建立畫面附近的卡片），所以一次把全部結果放進去，不再分批載入。
        // 用差量同步：內容沒變時不發通知、少量變動逐筆更新，清單上其他卡片不會被拆掉重建
        _allFilteredSortedItems = filtered;
        Items.SyncTo(filtered);

        NotifyGalleryMetricsChanged();

        if (resetScroll)
        {
            ScrollResetRequested?.Invoke();

            // 只有回到頂端時才預載最前面的封面；保留捲動位置時，畫面上的卡片由虛擬化清單自行載入，
            // 預載畫面外的項目反而會留下不會被回收佇列管理的封面
            if (filtered.Count > 0)
            {
                StartCoverPreload(filtered, Math.Min(filtered.Count, CoverPreloadBatchSize), cancelPrevious: true);
            }
        }
    }

    private void StartCoverPreload(IReadOnlyList<GalleryItemVm> items, int count, bool cancelPrevious)
    {
        var cts = cancelPrevious ? ReplaceCts(ref _coverCts) : new CancellationTokenSource();
        var ct = cts.Token;
        int limit = Math.Min(items.Count, count);

        // 切換範圍後卡片還沒建立前，先在 UI 執行緒把記憶體快取裡已有的封面直接設好（只查字典，很快）；
        // 背景預載只剩真的要讀檔的項目，不會搶走卡片建立後要用的載入名額
        if (Dispatcher.UIThread.CheckAccess())
        {
            for (int i = 0; i < limit; i++)
            {
                items[i].CoverSlot.TryApplyFromMemoryCache(_coverService);
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var tasks = new List<Task>(limit);
                for (int i = 0; i < limit; i++)
                {
                    if (ct.IsCancellationRequested || _disposed)
                    {
                        return;
                    }

                    tasks.Add(items[i].EnsureCoverLoadedAsync(_coverService, _coverLoadSemaphore, ct));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!cancelPrevious)
                {
                    cts.Dispose();
                }
            }
        }, ct);
    }

    private void OnApplyFuzzyRecommendation(string replacementQuery)
    {
        SearchBox.SetQueryFromExternal(replacementQuery, triggerSubmit: true);
    }

    private void OnItemActivated(GalleryItemVm item)
    {
        SelectGalleryItem(item);
        if (item.IsFolder)
        {
            NavigateToFolder(item.Path);
            return;
        }

        OpenWorkInReader(item.Path, item.DisplayTitle, item.AuthorName);
    }

    private void OnItemOpenInReader(GalleryItemVm item)
    {
        SelectGalleryItem(item);
        OpenWorkInReader(item.Path, item.DisplayTitle, item.AuthorName);
    }

    private void OnItemOpenExternal(GalleryItemVm item)
    {
        SelectGalleryItem(item);
        OpenPathWithDefaultApp(item.Path);
    }

    private void OpenWorkPathInReader(string path)
    {
        OpenWorkInReader(path, null, null);
    }

    private void OpenWorkInReader(string path, string? displayTitle, string? authorName)
    {
        if (_disposed) return;
        if (!_readerLauncher.OpenInReader(path, displayTitle, authorName, OnReaderClosedRefreshIfWritable, out var error))
        {
            ShowToast("無法開啟內建閱讀器", error ?? path, NotificationType.Warning);
        }
    }

    private void OnReaderClosedRefreshIfWritable()
    {
        if (!_disposed && !IsReadOnlyDatabase)
        {
            _cachedSnapshot = null;
            _ = LoadCurrentScopeItemsAsync();
        }
    }

    private void OnItemInspectDetail(GalleryItemVm item)
    {
        SelectGalleryItem(item);
        DetailDrawer.InspectItem(item, openDrawer: true);
    }

    private void OnItemCoverLoaded(GalleryItemVm item)
    {
        if (ReferenceEquals(_selectedGalleryItem, item))
        {
            DetailDrawer.SyncCoverFromCurrentItem();
        }
    }

    private void SelectGalleryItem(GalleryItemVm item)
    {
        if (_selectedGalleryItem != null && !ReferenceEquals(_selectedGalleryItem, item))
        {
            _selectedGalleryItem.IsSelected = false;
        }
        _selectedGalleryItem = item;
        item.IsSelected = true;
    }

    private void OpenPathWithDefaultApp(string path)
    {
        if (!_externalLauncher.OpenWithDefaultApp(path, out var error))
        {
            ShowToast("無法開啟作品", error ?? path, NotificationType.Warning);
        }
    }

    private void RevealPathInExplorer(string path)
    {
        if (!_externalLauncher.RevealInFileExplorer(path, out var error))
        {
            ShowToast("無法開啟檔案總管", error ?? path, NotificationType.Warning);
        }
    }

    private void OnDetailDrawerMutated()
    {
        _cachedSnapshot = null;
        _ = LoadCurrentScopeItemsAsync();
    }

    private void OnItemRevealInExplorer(GalleryItemVm item)
        => RevealPathInExplorer(item.Path);

    private void NotifyGalleryMetricsChanged()
    {
        int loaded = CurrentItemCount;
        int total = TotalMatchingItems;
        if (loaded > 0 && total > 0)
        {
            int fallbackEnd = VisibleRangeEnd >= VisibleRangeStart && VisibleRangeEnd > 0
                ? Math.Min(loaded, VisibleRangeEnd)
                : Math.Min(loaded, 12);
            int fallbackStart = Math.Clamp(VisibleRangeStart > 0 ? VisibleRangeStart : 1, 1, fallbackEnd);
            VisibleRangeStart = fallbackStart;
            VisibleRangeEnd = fallbackEnd;
            ScrollProgressPercent = Math.Clamp((double)fallbackEnd / total * 100.0, 0, 100);
        }
        else
        {
            VisibleRangeStart = 0;
            VisibleRangeEnd = 0;
            ScrollProgressPercent = 0;
            ShowBackToTopButton = false;
        }

        OnPropertyChanged(nameof(CurrentItemCount));
        OnPropertyChanged(nameof(TotalMatchingItems));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(HasPositionBadge));
        OnPropertyChanged(nameof(PositionBadgeTitle));
        OnPropertyChanged(nameof(PositionBadgeDetail));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
    }

    private void ShowToast(string title, string content, NotificationType type)
    {
        if (_toastManager == null)
        {
            return;
        }

        _toastManager.CreateToast()
            .WithTitle(title)
            .WithContent(content)
            .OfType(type)
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
    }

    private CancellationTokenSource ReplaceCts(ref CancellationTokenSource? target)
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? old;
        lock (_ctsLock)
        {
            old = target;
            target = next;
        }

        if (old != null)
        {
            try
            {
                old.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            old.Dispose();
        }

        return next;
    }

    private static long NormalizeTimestampMs(long rawTs)
        => rawTs switch
        {
            <= 0 => 0,
            // AssetMeta.Mtime / added_at 舊庫欄位以 Unix 秒儲存，統一轉毫秒供排序與日期顯示
            < 100_000_000_000L => rawTs * 1000L,
            _ => rawTs,
        };

    private static bool IsDirectChildPath(string parentDir, string candidatePath)
    {
        var candidateParent = System.IO.Path.GetDirectoryName(NormalizePathOrEmpty(candidatePath));
        return !string.IsNullOrEmpty(candidateParent) && PathsEqual(parentDir, candidateParent);
    }

    private static bool IsSupportedGalleryFile(string fileName)
        => SupportedExtensions.BuiltInReader.Contains(System.IO.Path.GetExtension(fileName));

    private static string NormalizePathOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string? a, string? b)
        => string.Equals(NormalizePathOrEmpty(a), NormalizePathOrEmpty(b), StringComparison.OrdinalIgnoreCase);

    private static string GetFolderDisplayName(string path)
    {
        var trimmed = NormalizePathOrEmpty(path);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SearchBox.SearchSubmitted -= OnDslSearchSubmitted;
        SearchBox.QueryChanged -= OnDslQueryChanged;

        lock (_ctsLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;
        }

        foreach (var cts in _realizedCoverLoads.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _realizedCoverLoads.Clear();
        _coverLoadSemaphore.Dispose();
    }
}

using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;
using NotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace ComicOrganizer.Avalonia.ViewModels;

/// <summary>
/// Phase 1 W1.2 靈感探索牆主 ViewModel：
/// 負責作者探索／主題・原作牆／熱門雙組合切換、雙封面模式、單列熱門標籤＋玻璃彈窗挑選器、
/// 多標籤 AND 過濾、排序／隨機洗牌、首屏分批快速渲染與 SemaphoreSlim(4) 非同步封面載入。
/// </summary>
public sealed partial class ExplorePageViewModel : ViewModelBase, IDisposable
{
    private const int SpotlightTopCount = 10;
    private const int DefaultMaxCards = 0;
    private const int InitialWallCardPageSize = 30;
    private const int WallCardPageStep = 18;
    private const int InitialPickerTagPageSize = 100;
    private const int PickerTagPageStep = 120;
    private const int CoverPreloadCount = 18;
    private static readonly TimeSpan DefaultSearchDebounce = TimeSpan.FromMilliseconds(150);

    private readonly ExploreWallService _wallService;
    private readonly SkiaCoverService _coverService;
    private readonly IExternalLauncher _externalLauncher;
    private readonly ISukiToastManager? _toastManager;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly TimeSpan _searchDebounceDelay;
    private readonly TimeSpan _pickerDebounceDelay;
    private readonly SemaphoreSlim _coverLoadSemaphore = new(4, 4);
    private readonly object _ctsLock = new();

    private readonly List<long> _selectedTagIds = [];
    private readonly Dictionary<long, ExploreTagBadge> _selectedTagMetadata = [];

    private IReadOnlyList<ExploreAuthorCardVm> _allAuthorCardVms = Array.Empty<ExploreAuthorCardVm>();
    private IReadOnlyList<ExploreThemeCardVm> _allThemeCardVms = Array.Empty<ExploreThemeCardVm>();
    private IReadOnlyList<ExploreComboCardVm> _allComboCardVms = Array.Empty<ExploreComboCardVm>();
    private IReadOnlyList<ExploreTagBadge> _allMatchingPickerBadges = Array.Empty<ExploreTagBadge>();
    private IReadOnlyList<ExploreWorkItem> _allDrillDownRawWorks = Array.Empty<ExploreWorkItem>();
    private IReadOnlyList<ExploreWorkCardVm> _allFilteredDrillDownWorkVms = Array.Empty<ExploreWorkCardVm>();
    private ExploreCardSelection? _currentDrillDownSelection;
    private int _visibleWallCardLimit = InitialWallCardPageSize;
    private int _visibleDrillDownWorkLimit = InitialWallCardPageSize;
    private int _visiblePickerTagLimit = InitialPickerTagPageSize;

    private CancellationTokenSource? _wallCts;
    private CancellationTokenSource? _drillDownCts;
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _pickerDebounceCts;
    private CancellationTokenSource? _pickerCts;
    private CancellationTokenSource? _coverLoadCts;
    private CancellationTokenSource? _comboCoverCts;
    private int _wallRequestVersion;
    private int _drillDownRequestVersion;
    private int _pickerRequestVersion;
    private int _randomSeed;
    private (long TagIdA, long TagIdB)? _lastRandomComboPair;
    private bool _suppressReload;
    private volatile bool _disposed;

    public ExplorePageViewModel(AppDataContext dataContext, SkiaCoverService coverService)
        : this(
            new ExploreWallService(
                (dataContext ?? throw new ArgumentNullException(nameof(dataContext))).MetaStore,
                dataContext.TagTranslator,
                dataContext.Preferences.Language),
            coverService,
            dataContext.ToastManager,
            autoLoad: true)
    {
    }

    public ExplorePageViewModel(
        ExploreWallService wallService,
        SkiaCoverService coverService,
        ISukiToastManager? toastManager = null,
        bool autoLoad = true,
        TimeSpan? searchDebounceDelay = null,
        Func<Action, Task>? runOnUiAsync = null,
        IExternalLauncher? externalLauncher = null)
    {
        _wallService = wallService ?? throw new ArgumentNullException(nameof(wallService));
        _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
        _externalLauncher = externalLauncher ?? new SystemExternalLauncher();
        _toastManager = toastManager;
        _searchDebounceDelay = searchDebounceDelay ?? DefaultSearchDebounce;
        _pickerDebounceDelay = TimeSpan.FromMilliseconds(Math.Min(120, _searchDebounceDelay.TotalMilliseconds));
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        SortOptions =
        [
            new ExploreSortOption(ExploreSortMode.WorkCountDesc, "作品最多"),
            new ExploreSortOption(ExploreSortMode.UnreadDesc, "未讀優先"),
            new ExploreSortOption(ExploreSortMode.RandomShuffle, "隨機靈感"),
            new ExploreSortOption(ExploreSortMode.NameAsc, "名稱 A-Z"),
        ];
        _selectedSortOption = SortOptions[0];

        PickerCategoryOptions =
        [
            new ExplorePickerCategoryItemVm("all", "全部", true, SelectPickerCategory),
            new ExplorePickerCategoryItemVm("content", "內容主題", false, SelectPickerCategory),
            new ExplorePickerCategoryItemVm("parody_character", "原作・角色", false, SelectPickerCategory),
            new ExplorePickerCategoryItemVm("artist_group", "作者・社團", false, SelectPickerCategory),
        ];

        InitializationTask = autoLoad ? ReloadAllAsync(invalidateCache: false) : Task.CompletedTask;
    }

    /// <summary>初始載入工作（供無 UI 測試同步等待初次回填完成）。</summary>
    public Task InitializationTask { get; private set; }

    /// <summary>通知 View 在切換頁籤／條件後將主捲軸重設至頂部。</summary>
    public event Action? ScrollResetRequested;

    /// <summary>W1.3 卡片點擊鑽取事件。</summary>
    public event Action<ExploreCardSelection>? CardActivated;

    /// <summary>W1.3 隨機雙題材碰撞橫幅預留 hook。</summary>
    public Action<ExploreComboCard>? ShowComboPlaceholder { get; set; }

    public IReadOnlyList<ExploreSortOption> SortOptions { get; }

    public IReadOnlyList<ExplorePickerCategoryItemVm> PickerCategoryOptions { get; }

    public BatchObservableCollection<ExploreTagChipVm> SpotlightTags { get; } = [];

    public BatchObservableCollection<ExploreTagChipVm> SelectedFilterTags { get; } = [];

    public BatchObservableCollection<ExploreTagChipVm> PickerTags { get; } = [];

    public BatchObservableCollection<ExploreAuthorCardVm> AuthorCards { get; } = [];

    public BatchObservableCollection<ExploreThemeCardVm> ThemeCards { get; } = [];

    public BatchObservableCollection<ExploreComboCardVm> ComboCards { get; } = [];

    public BatchObservableCollection<ExploreWorkCardVm> DrillDownWorks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveRandomCombo))]
    private ExploreComboCardVm? _activeRandomCombo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveRandomCombo))]
    [NotifyPropertyChangedFor(nameof(IsWallContentVisible))]
    [NotifyPropertyChangedFor(nameof(CurrentCardCount))]
    [NotifyPropertyChangedFor(nameof(TotalMatchingWallCards))]
    [NotifyPropertyChangedFor(nameof(HasMoreWallCards))]
    [NotifyPropertyChangedFor(nameof(RemainingWallCardsCount))]
    [NotifyPropertyChangedFor(nameof(LoadMoreWallCardsButtonText))]
    [NotifyPropertyChangedFor(nameof(HasCards))]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateHint))]
    [NotifyPropertyChangedFor(nameof(StatusSummaryText))]
    private bool _isDrillDownMode;

    [ObservableProperty]
    private string _drillDownTitle = string.Empty;

    [ObservableProperty]
    private string _drillDownSubtitle = string.Empty;

    [ObservableProperty]
    private bool _hasDrillDownAppliedTags;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAuthorMode))]
    [NotifyPropertyChangedFor(nameof(IsThemeWallMode))]
    [NotifyPropertyChangedFor(nameof(IsContentThemeSubTabActive))]
    [NotifyPropertyChangedFor(nameof(IsParodySubTabActive))]
    [NotifyPropertyChangedFor(nameof(IsHotComboSubTabActive))]
    [NotifyPropertyChangedFor(nameof(CurrentCardCount))]
    [NotifyPropertyChangedFor(nameof(TotalMatchingWallCards))]
    [NotifyPropertyChangedFor(nameof(HasMoreWallCards))]
    [NotifyPropertyChangedFor(nameof(RemainingWallCardsCount))]
    [NotifyPropertyChangedFor(nameof(LoadMoreWallCardsButtonText))]
    [NotifyPropertyChangedFor(nameof(HasCards))]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateHint))]
    [NotifyPropertyChangedFor(nameof(StatusSummaryText))]
    private ExploreMainTab _selectedMainTab = ExploreMainTab.AuthorExplore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentThemeSubTabActive))]
    [NotifyPropertyChangedFor(nameof(IsParodySubTabActive))]
    [NotifyPropertyChangedFor(nameof(IsHotComboSubTabActive))]
    [NotifyPropertyChangedFor(nameof(CurrentCardCount))]
    [NotifyPropertyChangedFor(nameof(TotalMatchingWallCards))]
    [NotifyPropertyChangedFor(nameof(HasMoreWallCards))]
    [NotifyPropertyChangedFor(nameof(RemainingWallCardsCount))]
    [NotifyPropertyChangedFor(nameof(LoadMoreWallCardsButtonText))]
    [NotifyPropertyChangedFor(nameof(HasCards))]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateHint))]
    [NotifyPropertyChangedFor(nameof(StatusSummaryText))]
    private ExploreThemeCategory _selectedThemeSubTab = ExploreThemeCategory.ContentThemes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeckCoverStyle))]
    [NotifyPropertyChangedFor(nameof(IsSingleCoverStyle))]
    [NotifyPropertyChangedFor(nameof(CoverStyleToggleLabel))]
    private ExploreCoverStyle _coverStyle = ExploreCoverStyle.Deck;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRandomSortActive))]
    private ExploreSortOption _selectedSortOption;

    [ObservableProperty]
    private bool _unreadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchKeyword))]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private bool _isTagPickerOpen;

    [ObservableProperty]
    private string _tagPickerSearchText = string.Empty;

    [ObservableProperty]
    private string _tagPickerCategory = "all";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTags))]
    [NotifyPropertyChangedFor(nameof(TagPickerButtonText))]
    [NotifyPropertyChangedFor(nameof(EmptyStateHint))]
    private int _selectedFilterCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyState))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusSummaryText))]
    private ExploreStatsSummary? _statsSummary;

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

    public bool IsAuthorMode => SelectedMainTab == ExploreMainTab.AuthorExplore;

    public bool IsThemeWallMode => SelectedMainTab == ExploreMainTab.ThemeParodyWall;

    public bool IsContentThemeSubTabActive => IsThemeWallMode && SelectedThemeSubTab == ExploreThemeCategory.ContentThemes;

    public bool IsParodySubTabActive => IsThemeWallMode && SelectedThemeSubTab == ExploreThemeCategory.ParodiesAndCharacters;

    public bool IsHotComboSubTabActive => IsThemeWallMode && SelectedThemeSubTab == ExploreThemeCategory.HotCombos;

    public bool IsDeckCoverStyle => CoverStyle == ExploreCoverStyle.Deck;

    public bool IsSingleCoverStyle => CoverStyle == ExploreCoverStyle.Single;

    public string CoverStyleToggleLabel => IsDeckCoverStyle ? "🃏 疊牌扇形" : "🖼️ 單張精選";

    public bool IsRandomSortActive => SelectedSortOption.Mode == ExploreSortMode.RandomShuffle;

    public bool HasSearchKeyword => !string.IsNullOrWhiteSpace(SearchKeyword);

    public bool HasSelectedTags => SelectedFilterCount > 0;

    public string TagPickerButtonText => HasSelectedTags
        ? $"🔍 全部標籤 ({SelectedFilterCount}) ▾"
        : "🔍 全部標籤 ▾";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public int CurrentCardCount => IsDrillDownMode
        ? DrillDownWorks.Count
        : SelectedMainTab switch
        {
            ExploreMainTab.AuthorExplore => AuthorCards.Count,
            _ when SelectedThemeSubTab == ExploreThemeCategory.HotCombos => ComboCards.Count,
            _ => ThemeCards.Count,
        };

    public int TotalMatchingWallCards => IsDrillDownMode
        ? _allFilteredDrillDownWorkVms.Count
        : SelectedMainTab switch
        {
            ExploreMainTab.AuthorExplore => _allAuthorCardVms.Count,
            _ when SelectedThemeSubTab == ExploreThemeCategory.HotCombos => _allComboCardVms.Count,
            _ => _allThemeCardVms.Count,
        };

    public bool HasActiveRandomCombo => ActiveRandomCombo != null && !IsDrillDownMode;

    public bool IsWallContentVisible => !IsDrillDownMode;

    public int TotalDrillDownWorks => _allFilteredDrillDownWorkVms.Count;

    public bool HasMoreWallCards => CurrentCardCount < TotalMatchingWallCards;

    public int RemainingWallCardsCount => Math.Max(0, TotalMatchingWallCards - CurrentCardCount);

    public bool HasPositionBadge => TotalMatchingWallCards > 0;

    public string PositionBadgeTitle
    {
        get
        {
            if (TotalMatchingWallCards <= 0 || CurrentCardCount <= 0)
            {
                return string.Empty;
            }

            int start = Math.Clamp(VisibleRangeStart, 1, CurrentCardCount);
            int end = Math.Clamp(VisibleRangeEnd >= start ? VisibleRangeEnd : Math.Min(CurrentCardCount, 12), start, CurrentCardCount);
            return $"第 {start:N0}–{end:N0} 筆 / 共 {TotalMatchingWallCards:N0} 筆";
        }
    }

    public string PositionBadgeDetail
    {
        get
        {
            if (TotalMatchingWallCards <= 0)
            {
                return string.Empty;
            }

            int pct = (int)Math.Round(Math.Clamp(ScrollProgressPercent, 0, 100));
            return HasMoreWallCards
                ? $"已載入 {CurrentCardCount:N0} 筆 · {pct}%"
                : $"已全數載入 · {pct}%";
        }
    }

    public string LoadMoreWallCardsButtonText
    {
        get
        {
            int nextStep = Math.Min(WallCardPageStep, RemainingWallCardsCount);
            return IsDrillDownMode
                ? $"載入更多作品（+{nextStep:N0}，還剩 {RemainingWallCardsCount:N0} 本）"
                : $"載入更多卡片（+{nextStep:N0}，還剩 {RemainingWallCardsCount:N0} 筆）";
        }
    }

    public string LoadMoreWallCardsShortText
    {
        get
        {
            int nextStep = Math.Min(WallCardPageStep, RemainingWallCardsCount);
            return $"+{nextStep:N0}";
        }
    }

    public string LoadMoreWallCardsShortTooltip
    {
        get
        {
            int nextStep = Math.Min(WallCardPageStep, RemainingWallCardsCount);
            return IsDrillDownMode
                ? $"再載入下一批 {nextStep:N0} 本作品"
                : $"再載入下一批 {nextStep:N0} 筆資料";
        }
    }

    public int TotalMatchingPickerTags => _allMatchingPickerBadges.Count;

    public bool HasMorePickerTags => PickerTags.Count < TotalMatchingPickerTags;

    public int RemainingPickerTagsCount => Math.Max(0, TotalMatchingPickerTags - PickerTags.Count);

    public string PickerSummaryText
    {
        get
        {
            if (TotalMatchingPickerTags == 0)
            {
                return "沒有符合關鍵字的標籤";
            }

            if (HasMorePickerTags)
            {
                return $"已顯示前 {PickerTags.Count:N0} / 共 {TotalMatchingPickerTags:N0} 顆標籤（輸入關鍵字可精準篩選全庫）";
            }

            return $"共 {TotalMatchingPickerTags:N0} 顆標籤";
        }
    }

    public string LoadMorePickerTagsButtonText
    {
        get
        {
            int nextBatch = Math.Min(PickerTagPageStep, RemainingPickerTagsCount);
            return $"顯示更多 +{nextBatch:N0} 顆（還有 {RemainingPickerTagsCount:N0} 顆）";
        }
    }

    public bool HasCards => CurrentCardCount > 0;

    public bool IsEmptyState => !IsLoading && !HasCards;

    public string EmptyStateTitle => IsDrillDownMode
        ? "此集合目前沒有符合條件的作品"
        : (HasSelectedTags || HasSearchKeyword || UnreadOnly)
            ? "沒有符合條件的探索項目"
            : "目前尚未載入可探索的作品資料";

    public string EmptyStateHint => IsDrillDownMode
        ? "嘗試關閉「僅看未讀」、清空頂部搜尋字詞，或點擊返回探索牆瀏覽其他題材。"
        : (HasSelectedTags || HasSearchKeyword || UnreadOnly)
            ? "嘗試移除部分已選標籤、關閉「僅看未讀」或調整搜尋關鍵字。"
            : "請確認漫畫庫路徑與索引資料庫是否已建立完成，或點擊重新整理重試。";

    public string StatusSummaryText
    {
        get
        {
            if (IsDrillDownMode)
            {
                int unreadInSet = _allFilteredDrillDownWorkVms.Count(w => w.IsUnread);
                return $"{DrillDownTitle} · {TotalDrillDownWorks:N0} 本作品（{unreadInSet:N0} 未讀）";
            }

            int matched = TotalMatchingWallCards;
            var modeLabel = SelectedMainTab switch
            {
                ExploreMainTab.AuthorExplore => $"{matched:N0} 位作者",
                _ when SelectedThemeSubTab == ExploreThemeCategory.ContentThemes => $"{matched:N0} 個內容主題",
                _ when SelectedThemeSubTab == ExploreThemeCategory.ParodiesAndCharacters => $"{matched:N0} 個原作／角色",
                _ => $"{matched:N0} 組熱門雙組合",
            };

            if (StatsSummary == null)
            {
                return modeLabel;
            }

            return $"{modeLabel} · 庫內 {StatsSummary.TotalWorks:N0} 部作品（{StatsSummary.UnreadWorks:N0} 未讀）";
        }
    }

    public IAsyncRelayCommand RandomComboCommand => PickRandomComboCommand;

    // ── 屬性變更聯動 ────────────────────────────────────────────────────

    partial void OnSelectedMainTabChanged(ExploreMainTab value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        if (IsDrillDownMode)
        {
            ClearDrillDownState();
        }

        _ = LoadWallAsync();
    }

    partial void OnSelectedThemeSubTabChanged(ExploreThemeCategory value)
    {
        if (_suppressReload || _disposed || !IsThemeWallMode)
        {
            return;
        }

        if (IsDrillDownMode)
        {
            ClearDrillDownState();
        }

        _ = LoadWallAsync();
    }

    partial void OnCoverStyleChanged(ExploreCoverStyle value)
    {
        if (_disposed || value != ExploreCoverStyle.Deck || IsDrillDownMode)
        {
            return;
        }

        IReadOnlyList<ExploreCardVmBase> activeCards = SelectedMainTab switch
        {
            ExploreMainTab.AuthorExplore => AuthorCards.ToArray(),
            _ when SelectedThemeSubTab == ExploreThemeCategory.HotCombos => ComboCards.ToArray(),
            _ => ThemeCards.ToArray(),
        };

        if (activeCards.Count > 0)
        {
            StartCoverPreload(activeCards, activeCards.Count, cancelPrevious: false);
        }
    }

    partial void OnSelectedSortOptionChanged(ExploreSortOption value)
    {
        if (value == null)
        {
            return;
        }

        if (value.Mode == ExploreSortMode.RandomShuffle && _randomSeed == 0)
        {
            _randomSeed = NextNonZeroSeed(0);
        }

        if (_suppressReload || _disposed)
        {
            return;
        }

        if (IsDrillDownMode)
        {
            RebuildDrillDownWorksOnUi(resetScroll: true);
            return;
        }

        _ = LoadWallAsync();
    }

    partial void OnUnreadOnlyChanged(bool value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        if (IsDrillDownMode)
        {
            RebuildDrillDownWorksOnUi(resetScroll: true);
            return;
        }

        _ = LoadWallAsync();
    }

    partial void OnSearchKeywordChanged(string value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        ScheduleDebouncedSearchReload();
    }

    partial void OnIsTagPickerOpenChanged(bool value)
    {
        if (value && !_disposed)
        {
            _visiblePickerTagLimit = InitialPickerTagPageSize;
            _ = LoadPickerTagsAsync();
        }
    }

    partial void OnTagPickerSearchTextChanged(string value)
    {
        if (_suppressReload || _disposed)
        {
            return;
        }

        _visiblePickerTagLimit = InitialPickerTagPageSize;
        if (string.IsNullOrEmpty(value) || _pickerDebounceDelay <= TimeSpan.Zero)
        {
            CancelCts(ref _pickerDebounceCts);
            _ = LoadPickerTagsAsync();
        }
        else
        {
            ScheduleDebouncedPickerReload();
        }
    }

    partial void OnTagPickerCategoryChanged(string value)
    {
        var normalized = NormalizePickerCategory(value);
        foreach (var item in PickerCategoryOptions)
        {
            item.IsSelected = string.Equals(item.Key, normalized, StringComparison.OrdinalIgnoreCase);
        }

        if (_suppressReload || _disposed)
        {
            return;
        }

        _visiblePickerTagLimit = InitialPickerTagPageSize;
        CancelCts(ref _pickerDebounceCts);
        _ = LoadPickerTagsAsync();
    }

    // ── Commands ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectMainTab(ExploreMainTab tab)
    {
        if (SelectedMainTab == tab && IsDrillDownMode)
        {
            ExitDrillDown();
            return;
        }

        SelectedMainTab = tab;
    }

    [RelayCommand]
    private void SelectAuthorMode()
    {
        if (IsAuthorMode && IsDrillDownMode)
        {
            ExitDrillDown();
            return;
        }

        SelectedMainTab = ExploreMainTab.AuthorExplore;
    }

    [RelayCommand]
    private void SelectThemeWallMode()
    {
        if (IsThemeWallMode && IsDrillDownMode)
        {
            ExitDrillDown();
            return;
        }

        SelectedMainTab = ExploreMainTab.ThemeParodyWall;
    }

    [RelayCommand]
    private void SelectThemeSubTab(ExploreThemeCategory subTab)
    {
        bool unchanged = IsThemeWallMode && SelectedThemeSubTab == subTab;
        if (unchanged && IsDrillDownMode)
        {
            ExitDrillDown();
            return;
        }

        _suppressReload = true;
        try
        {
            SelectedMainTab = ExploreMainTab.ThemeParodyWall;
            SelectedThemeSubTab = subTab;
        }
        finally
        {
            _suppressReload = false;
        }

        if (IsDrillDownMode)
        {
            ClearDrillDownState();
        }

        if (!_disposed)
        {
            _ = LoadWallAsync();
        }
    }

    [RelayCommand]
    private void SelectContentThemesSubTab() => SelectThemeSubTab(ExploreThemeCategory.ContentThemes);

    [RelayCommand]
    private void SelectParodiesSubTab() => SelectThemeSubTab(ExploreThemeCategory.ParodiesAndCharacters);

    [RelayCommand]
    private void SelectHotCombosSubTab() => SelectThemeSubTab(ExploreThemeCategory.HotCombos);

    [RelayCommand]
    private void SelectCoverStyle(ExploreCoverStyle style)
    {
        CoverStyle = style;
    }

    [RelayCommand]
    private void SelectDeckCoverStyle()
    {
        CoverStyle = ExploreCoverStyle.Deck;
    }

    [RelayCommand]
    private void SelectSingleCoverStyle()
    {
        CoverStyle = ExploreCoverStyle.Single;
    }

    [RelayCommand]
    private void ToggleCoverStyle()
    {
        CoverStyle = IsDeckCoverStyle ? ExploreCoverStyle.Single : ExploreCoverStyle.Deck;
    }

    [RelayCommand]
    private Task ShuffleAgainAsync()
    {
        _randomSeed = NextNonZeroSeed(_randomSeed);

        var randomOption = SortOptions.First(o => o.Mode == ExploreSortMode.RandomShuffle);
        if (!ReferenceEquals(SelectedSortOption, randomOption))
        {
            SelectedSortOption = randomOption;
            return Task.CompletedTask;
        }

        if (IsDrillDownMode)
        {
            RebuildDrillDownWorksOnUi(resetScroll: true);
            return Task.CompletedTask;
        }

        return LoadWallAsync();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchKeyword = string.Empty;
    }

    [RelayCommand]
    private void ToggleTagPicker()
    {
        IsTagPickerOpen = !IsTagPickerOpen;
    }

    [RelayCommand]
    private void CloseTagPicker()
    {
        IsTagPickerOpen = false;
    }

    [RelayCommand]
    private void SelectPickerCategory(string? categoryKey)
    {
        TagPickerCategory = NormalizePickerCategory(categoryKey);
    }

    [RelayCommand]
    private Task ClearTagFiltersAsync()
    {
        if (_selectedTagIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        _selectedTagIds.Clear();
        _selectedTagMetadata.Clear();
        return ApplyTagSelectionChangeAsync();
    }

    [RelayCommand]
    private void LoadMoreWallCards()
    {
        if (_disposed || !HasMoreWallCards)
        {
            return;
        }

        if (IsDrillDownMode)
        {
            int prevDrill = DrillDownWorks.Count;
            _visibleDrillDownWorkLimit += WallCardPageStep;
            var addedWorks = AppendSlice(DrillDownWorks, _allFilteredDrillDownWorkVms, prevDrill);
            NotifyCardMetricsChanged();
            if (addedWorks.Count > 0)
            {
                StartCoverPreload(addedWorks, addedWorks.Count, cancelPrevious: false);
            }
            return;
        }

        int previousVisible = CurrentCardCount;
        _visibleWallCardLimit += WallCardPageStep;

        IReadOnlyList<ExploreCardVmBase> newlyAdded = SelectedMainTab switch
        {
            ExploreMainTab.AuthorExplore => AppendSlice(AuthorCards, _allAuthorCardVms, previousVisible),
            _ when SelectedThemeSubTab == ExploreThemeCategory.HotCombos => AppendSlice(ComboCards, _allComboCardVms, previousVisible),
            _ => AppendSlice(ThemeCards, _allThemeCardVms, previousVisible),
        };

        NotifyCardMetricsChanged();

        if (newlyAdded.Count > 0)
        {
            StartCoverPreload(newlyAdded, newlyAdded.Count, cancelPrevious: false);
        }
    }

    [RelayCommand]
    private void LoadMorePickerTags()
    {
        if (_disposed || !HasMorePickerTags)
        {
            return;
        }

        int before = PickerTags.Count;
        _visiblePickerTagLimit += PickerTagPageStep;
        var selectedSet = _selectedTagIds.ToHashSet();
        var nextSlice = _allMatchingPickerBadges
            .Skip(before)
            .Take(_visiblePickerTagLimit - before)
            .Select(b => new ExploreTagChipVm(b, selectedSet.Contains(b.TagId), OnTagChipToggled))
            .ToList();

        PickerTags.AddRange(nextSlice);
        NotifyPickerMetricsChanged();
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        if (IsDrillDownMode && _currentDrillDownSelection != null)
        {
            _wallService.InvalidateCache();
            return EnterDrillDownAsync(_currentDrillDownSelection);
        }

        return ReloadAllAsync(invalidateCache: true);
    }

    [RelayCommand]
    private async Task PickRandomComboAsync()
    {
        if (_disposed)
        {
            return;
        }

        var prevPair = _lastRandomComboPair;
        ExploreComboCard? combo;
        try
        {
            combo = await Task.Run(() => _wallService.PickRandomDoubleCombo(prevPair)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                ErrorMessage = ex.Message;
                ShowToast("隨機雙題材碰撞失敗", ex.Message, NotificationType.Error, 4);
            }).ConfigureAwait(false);
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (combo == null)
        {
            await _runOnUiAsync(() =>
            {
                ShowToast(
                    "找不到可碰撞的組合",
                    "目前漫畫庫尚無足夠交集標籤，請先解析或同步作品標籤。",
                    NotificationType.Warning,
                    3);
            }).ConfigureAwait(false);
            return;
        }

        _lastRandomComboPair = (combo.FirstTag.TagId, combo.SecondTag.TagId);
        ExploreComboCardVm? comboVm = null;

        await _runOnUiAsync(() =>
        {
            if (IsDrillDownMode)
            {
                ClearDrillDownState();
                ScrollResetRequested?.Invoke();
            }

            comboVm = new ExploreComboCardVm(combo, _card => { _ = BrowseActiveRandomComboAsync(); }, _runOnUiAsync);
            ActiveRandomCombo = comboVm;
            ShowComboPlaceholder?.Invoke(combo);
        }).ConfigureAwait(false);

        if (comboVm != null && !_disposed)
        {
            var bannerCts = ReplaceCancellationSource(ref _comboCoverCts);
            _ = comboVm.EnsureCoversLoadedAsync(_coverService, _coverLoadSemaphore, bannerCts.Token);
        }
    }

    [RelayCommand]
    private void DismissRandomCombo()
    {
        CancelCts(ref _comboCoverCts);
        ActiveRandomCombo = null;
    }

    [RelayCommand]
    private Task BrowseActiveRandomComboAsync()
    {
        if (_disposed || ActiveRandomCombo == null)
        {
            return Task.CompletedTask;
        }

        var combo = ActiveRandomCombo.SourceCard;
        var selection = new ExploreCardSelection(
            ExploreCardSelectionKind.RandomCombo,
            $"{combo.FirstTag.DisplayName} × {combo.SecondTag.DisplayName}",
            TagIds: new[] { combo.FirstTag.TagId, combo.SecondTag.TagId },
            ComboCard: combo);

        return EnterDrillDownAsync(selection);
    }

    [RelayCommand]
    private Task ApplyActiveRandomComboFiltersAsync()
    {
        if (_disposed || ActiveRandomCombo == null)
        {
            return Task.CompletedTask;
        }

        var combo = ActiveRandomCombo.SourceCard;
        _selectedTagIds.Clear();
        _selectedTagIds.Add(combo.FirstTag.TagId);
        _selectedTagIds.Add(combo.SecondTag.TagId);
        _selectedTagMetadata[combo.FirstTag.TagId] = combo.FirstTag;
        _selectedTagMetadata[combo.SecondTag.TagId] = combo.SecondTag;
        IsTagPickerOpen = false;

        return ApplyTagSelectionChangeAsync();
    }

    [RelayCommand]
    private void ExitDrillDown()
    {
        if (!IsDrillDownMode)
        {
            return;
        }

        ClearDrillDownState();
        ScrollResetRequested?.Invoke();

        // 若探索牆集合為空（例如首次先經碰撞橫幅或剛切換條件），確保重新填回牆面
        if (TotalMatchingWallCards == 0 && !IsLoading)
        {
            _ = LoadWallAsync();
        }
        else
        {
            NotifyCardMetricsChanged();
        }
    }

    [RelayCommand]
    private Task ShowAllForDrillDownAuthorAsync()
    {
        if (_disposed || _currentDrillDownSelection?.AuthorId is not long authorId)
        {
            return Task.CompletedTask;
        }

        var clearSelection = _currentDrillDownSelection with { TagIds = Array.Empty<long>() };
        return EnterDrillDownAsync(clearSelection);
    }

    // ── 公開輔助方法（供接線與單元測試使用）────────────────────────────

    public async Task ApplySearchImmediateAsync()
    {
        if (_disposed)
        {
            return;
        }

        CancelCts(ref _searchDebounceCts);
        if (IsDrillDownMode)
        {
            await _runOnUiAsync(() => RebuildDrillDownWorksOnUi(resetScroll: true)).ConfigureAwait(false);
            return;
        }

        await LoadWallAsync().ConfigureAwait(false);
    }

    public Task ReloadAllAsync(bool invalidateCache = false)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        var task = ExecuteWallLoadAsync(invalidateCache: invalidateCache, refreshPickerTags: true);
        InitializationTask = task;
        return task;
    }

    public Task LoadWallAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        return ExecuteWallLoadAsync(invalidateCache: false, refreshPickerTags: false);
    }

    public Task ToggleTagSelectionAsync(long tagId, ExploreTagBadge? badgeHint = null)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        int existingIndex = _selectedTagIds.IndexOf(tagId);
        if (existingIndex >= 0)
        {
            _selectedTagIds.RemoveAt(existingIndex);
            _selectedTagMetadata.Remove(tagId);
        }
        else
        {
            _selectedTagIds.Add(tagId);
            if (badgeHint != null)
            {
                _selectedTagMetadata[tagId] = badgeHint;
            }
        }

        return ApplyTagSelectionChangeAsync();
    }

    public async Task RequestCardCoversAsync(ExploreCardVmBase? card)
    {
        if (_disposed || card == null)
        {
            return;
        }

        await card.EnsurePrimaryCoverLoadedAsync(_coverService, _coverLoadSemaphore).ConfigureAwait(false);

        if (!_disposed && IsDeckCoverStyle && card.RepresentativeCount > 1)
        {
            await card.EnsureSecondaryCoversLoadedAsync(_coverService, _coverLoadSemaphore).ConfigureAwait(false);
        }
    }

    /// <summary>由 View 根據目前捲動視口實際可見卡片範圍同步位置膠囊與百分比進度。</summary>
    public void UpdateViewportRange(int firstVisibleOneBased, int lastVisibleOneBased, double offsetY)
    {
        int loaded = CurrentCardCount;
        int total = TotalMatchingWallCards;

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

    public async Task LoadPickerTagsAsync()
    {
        if (_disposed)
        {
            return;
        }

        var cts = ReplaceCancellationSource(ref _pickerCts);
        var ct = cts.Token;
        int requestVersion = Interlocked.Increment(ref _pickerRequestVersion);
        var keyword = TagPickerSearchText;
        var category = TagPickerCategory;

        try
        {
            var tags = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return _wallService.GetAllPickerTags(keyword, category);
            }, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || requestVersion != Volatile.Read(ref _pickerRequestVersion) || _disposed)
            {
                return;
            }

            await _runOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested || requestVersion != Volatile.Read(ref _pickerRequestVersion) || _disposed)
                {
                    return;
                }

                ApplyPickerTagsOnUi(tags);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 被新搜尋取消
        }
        catch
        {
            // 避免背景錯誤中斷 UI
        }
    }

    // ── 內部實作 ────────────────────────────────────────────────────────

    private async Task ExecuteWallLoadAsync(bool invalidateCache, bool refreshPickerTags)
    {
        var cts = ReplaceCancellationSource(ref _wallCts);
        var ct = cts.Token;
        int requestVersion = Interlocked.Increment(ref _wallRequestVersion);

        await _runOnUiAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            var selectedIdsSnapshot = _selectedTagIds.ToArray();
            var options = BuildQueryOptions(selectedIdsSnapshot);
            var mainTab = SelectedMainTab;
            var subTab = SelectedThemeSubTab;
            var pickerOpen = IsTagPickerOpen;
            var needPickerTags = pickerOpen || (refreshPickerTags && selectedIdsSnapshot.Length > 0);
            var pickerKeyword = TagPickerSearchText;
            var pickerCategory = TagPickerCategory;

            var payload = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (invalidateCache)
                {
                    _wallService.InvalidateCache();
                }

                var stats = _wallService.GetStatsSummary();
                var spotlight = _wallService.GetTopSpotlightTags(selectedIdsSnapshot, SpotlightTopCount);
                var pickerTags = needPickerTags
                    ? _wallService.GetAllPickerTags(
                        pickerOpen ? pickerKeyword : null,
                        pickerOpen ? pickerCategory : "all")
                    : Array.Empty<ExploreTagBadge>();

                IReadOnlyList<ExploreAuthorCard>? authorCards = null;
                IReadOnlyList<ExploreThemeCard>? themeCards = null;
                IReadOnlyList<ExploreComboCard>? comboCards = null;

                switch (mainTab)
                {
                    case ExploreMainTab.AuthorExplore:
                        authorCards = _wallService.GetAuthorCards(options);
                        break;
                    case ExploreMainTab.ThemeParodyWall when subTab == ExploreThemeCategory.ContentThemes:
                        themeCards = _wallService.GetContentThemeCards(options);
                        break;
                    case ExploreMainTab.ThemeParodyWall when subTab == ExploreThemeCategory.ParodiesAndCharacters:
                        themeCards = _wallService.GetParodyAndCharacterCards(options);
                        break;
                    default:
                        comboCards = _wallService.GetHotComboCards(options);
                        break;
                }

                return new WallSnapshotPayload(stats, spotlight, needPickerTags, pickerOpen, pickerTags, authorCards, themeCards, comboCards);
            }, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || requestVersion != Volatile.Read(ref _wallRequestVersion) || _disposed)
            {
                return;
            }

            IReadOnlyList<ExploreCardVmBase> initialVisibleCards = Array.Empty<ExploreCardVmBase>();

            await _runOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested || requestVersion != Volatile.Read(ref _wallRequestVersion) || _disposed)
                {
                    return;
                }

                StatsSummary = payload.Stats;
                CacheTagMetadata(payload.SpotlightTags);
                if (payload.PickerLoaded)
                {
                    CacheTagMetadata(payload.PickerTags);
                }

                SyncSpotlightAndSelectedCollections(payload.SpotlightTags);

                if (payload.ApplyPickerToUi)
                {
                    ApplyPickerTagsOnUi(payload.PickerTags);
                }

                initialVisibleCards = ApplyWallCardsOnUi(payload);
                IsLoading = false;
                ScrollResetRequested?.Invoke();
            }).ConfigureAwait(false);

            if (!ct.IsCancellationRequested && !_disposed && initialVisibleCards.Count > 0)
            {
                StartCoverPreload(initialVisibleCards, initialVisibleCards.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // 被新查詢取代，靜默忽略
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                if (requestVersion == Volatile.Read(ref _wallRequestVersion) && !_disposed)
                {
                    ErrorMessage = ex.Message;
                    IsLoading = false;
                }
            }).ConfigureAwait(false);
        }
    }

    private Task ApplyTagSelectionChangeAsync()
    {
        if (IsDrillDownMode)
        {
            ClearDrillDownState();
        }

        var selectedSet = _selectedTagIds.ToHashSet();
        foreach (var chip in PickerTags)
        {
            chip.IsSelected = selectedSet.Contains(chip.TagId);
        }

        SelectedFilterCount = _selectedTagIds.Count;
        return LoadWallAsync();
    }

    private void OnTagChipToggled(ExploreTagChipVm chip)
    {
        var badge = new ExploreTagBadge(
            chip.TagId,
            chip.Name,
            chip.DisplayName,
            chip.Category,
            chip.WorkCount,
            chip.IsStructural);

        _ = ToggleTagSelectionAsync(chip.TagId, badge);
    }

    private void ApplyPickerTagsOnUi(IReadOnlyList<ExploreTagBadge> matchedTags)
    {
        _allMatchingPickerBadges = matchedTags;
        CacheTagMetadata(matchedTags);
        var visibleBadges = matchedTags.Take(_visiblePickerTagLimit).ToList();
        ReplaceTagChipCollection(PickerTags, visibleBadges);
        NotifyPickerMetricsChanged();
    }

    private void SyncSpotlightAndSelectedCollections(IReadOnlyList<ExploreTagBadge> spotlightBadges)
    {
        var selectedSet = _selectedTagIds.ToHashSet();

        ReplaceTagChipCollection(SpotlightTags, spotlightBadges);

        var nextSelected = new List<ExploreTagChipVm>(_selectedTagIds.Count);
        foreach (var tid in _selectedTagIds)
        {
            if (_selectedTagMetadata.TryGetValue(tid, out var badge))
            {
                nextSelected.Add(new ExploreTagChipVm(badge, isSelected: true, OnTagChipToggled));
            }
        }
        SelectedFilterTags.ReplaceAll(nextSelected);

        foreach (var chip in PickerTags)
        {
            chip.IsSelected = selectedSet.Contains(chip.TagId);
        }

        SelectedFilterCount = _selectedTagIds.Count;
    }

    private void ReplaceTagChipCollection(BatchObservableCollection<ExploreTagChipVm> target, IReadOnlyList<ExploreTagBadge> badges)
    {
        var selectedSet = _selectedTagIds.ToHashSet();
        var vms = badges.Select(b => new ExploreTagChipVm(b, selectedSet.Contains(b.TagId), OnTagChipToggled)).ToList();
        target.ReplaceAll(vms);
    }

    private void CacheTagMetadata(IReadOnlyList<ExploreTagBadge> badges)
    {
        foreach (var badge in badges)
        {
            _selectedTagMetadata[badge.TagId] = badge;
        }
    }

    private IReadOnlyList<ExploreCardVmBase> ApplyWallCardsOnUi(WallSnapshotPayload payload)
    {
        _visibleWallCardLimit = InitialWallCardPageSize;
        IReadOnlyList<ExploreCardVmBase> visibleSlice = Array.Empty<ExploreCardVmBase>();

        if (payload.AuthorCards != null)
        {
            _allThemeCardVms = Array.Empty<ExploreThemeCardVm>();
            _allComboCardVms = Array.Empty<ExploreComboCardVm>();
            ThemeCards.Clear();
            ComboCards.Clear();

            var all = payload.AuthorCards
                .Select(card => new ExploreAuthorCardVm(card, OnCardSelected, _runOnUiAsync))
                .ToList();
            _allAuthorCardVms = all;
            var slice = all.Take(_visibleWallCardLimit).ToList();
            AuthorCards.ReplaceAll(slice);
            visibleSlice = slice;
        }
        else if (payload.ThemeCards != null)
        {
            _allAuthorCardVms = Array.Empty<ExploreAuthorCardVm>();
            _allComboCardVms = Array.Empty<ExploreComboCardVm>();
            AuthorCards.Clear();
            ComboCards.Clear();

            var all = payload.ThemeCards
                .Select(card => new ExploreThemeCardVm(card, OnCardSelected, _runOnUiAsync))
                .ToList();
            _allThemeCardVms = all;
            var slice = all.Take(_visibleWallCardLimit).ToList();
            ThemeCards.ReplaceAll(slice);
            visibleSlice = slice;
        }
        else if (payload.ComboCards != null)
        {
            _allAuthorCardVms = Array.Empty<ExploreAuthorCardVm>();
            _allThemeCardVms = Array.Empty<ExploreThemeCardVm>();
            AuthorCards.Clear();
            ThemeCards.Clear();

            var all = payload.ComboCards
                .Select(card => new ExploreComboCardVm(card, OnCardSelected, _runOnUiAsync))
                .ToList();
            _allComboCardVms = all;
            var slice = all.Take(_visibleWallCardLimit).ToList();
            ComboCards.ReplaceAll(slice);
            visibleSlice = slice;
        }

        NotifyCardMetricsChanged();
        return visibleSlice;
    }

    private List<ExploreCardVmBase> AppendSlice<T>(
        BatchObservableCollection<T> target,
        IReadOnlyList<T> allItems,
        int startIndex)
        where T : ExploreCardVmBase
    {
        int takeCount = Math.Max(0, Math.Min(WallCardPageStep, allItems.Count - startIndex));
        if (takeCount == 0)
        {
            return [];
        }

        var nextBatch = new List<T>(takeCount);
        var baseList = new List<ExploreCardVmBase>(takeCount);
        for (int i = 0; i < takeCount; i++)
        {
            var item = allItems[startIndex + i];
            nextBatch.Add(item);
            baseList.Add(item);
        }

        target.AddRange(nextBatch);
        return baseList;
    }

    private void NotifyCardMetricsChanged()
    {
        int loaded = CurrentCardCount;
        int total = TotalMatchingWallCards;
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

        OnPropertyChanged(nameof(CurrentCardCount));
        OnPropertyChanged(nameof(TotalMatchingWallCards));
        OnPropertyChanged(nameof(HasMoreWallCards));
        OnPropertyChanged(nameof(RemainingWallCardsCount));
        OnPropertyChanged(nameof(HasPositionBadge));
        OnPropertyChanged(nameof(PositionBadgeTitle));
        OnPropertyChanged(nameof(PositionBadgeDetail));
        OnPropertyChanged(nameof(LoadMoreWallCardsButtonText));
        OnPropertyChanged(nameof(LoadMoreWallCardsShortText));
        OnPropertyChanged(nameof(LoadMoreWallCardsShortTooltip));
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(StatusSummaryText));
    }

    private void NotifyPickerMetricsChanged()
    {
        OnPropertyChanged(nameof(TotalMatchingPickerTags));
        OnPropertyChanged(nameof(HasMorePickerTags));
        OnPropertyChanged(nameof(RemainingPickerTagsCount));
        OnPropertyChanged(nameof(PickerSummaryText));
        OnPropertyChanged(nameof(LoadMorePickerTagsButtonText));
    }

    private void StartCoverPreload(IReadOnlyList<ExploreCardVmBase> cards, int maxPreloadCount, bool cancelPrevious = true)
    {
        var cts = cancelPrevious
            ? ReplaceCancellationSource(ref _coverLoadCts)
            : new CancellationTokenSource();
        var ct = cts.Token;
        int limit = Math.Min(cards.Count, maxPreloadCount);
        bool loadSecondaryDeckCovers = IsDeckCoverStyle;

        _ = Task.Run(async () =>
        {
            try
            {
                // 第一階段：優先載入所有新卡片的正面主封面，確保捲動出現時第一時間有圖
                var primaryTasks = new List<Task>(limit);
                for (int i = 0; i < limit; i++)
                {
                    if (ct.IsCancellationRequested || _disposed)
                    {
                        return;
                    }

                    primaryTasks.Add(cards[i].EnsurePrimaryCoverLoadedAsync(_coverService, _coverLoadSemaphore, ct));
                }

                await Task.WhenAll(primaryTasks).ConfigureAwait(false);

                if (!loadSecondaryDeckCovers || ct.IsCancellationRequested || _disposed)
                {
                    return;
                }

                // 第二階段：疊牌模式下再依序補齊左右背景副封面，避免與主封面搶佔解碼通道
                var secondaryTasks = new List<Task>(limit);
                for (int i = 0; i < limit; i++)
                {
                    if (ct.IsCancellationRequested || _disposed)
                    {
                        return;
                    }

                    if (cards[i].RepresentativeCount > 1)
                    {
                        secondaryTasks.Add(cards[i].EnsureSecondaryCoversLoadedAsync(_coverService, _coverLoadSemaphore, ct));
                    }
                }

                if (secondaryTasks.Count > 0)
                {
                    await Task.WhenAll(secondaryTasks).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 切換分頁或條件時取消舊縮圖預載
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

    private void OnCardSelected(ExploreCardVmBase card)
    {
        ExploreCardSelection selection = card switch
        {
            ExploreAuthorCardVm author => new ExploreCardSelection(
                ExploreCardSelectionKind.Author,
                author.MainName,
                AuthorId: author.AuthorId,
                TagIds: _selectedTagIds.ToArray(),
                AuthorCard: author.SourceCard),
            ExploreThemeCardVm theme => new ExploreCardSelection(
                ExploreCardSelectionKind.Theme,
                theme.DisplayName,
                TagIds: MergeWithSelectedTags(theme.TagId),
                ThemeCard: theme.SourceCard),
            ExploreComboCardVm combo => new ExploreCardSelection(
                ExploreCardSelectionKind.Combo,
                combo.PairTitle,
                TagIds: MergeWithSelectedTags(combo.FirstTag.TagId, combo.SecondTag.TagId),
                ComboCard: combo.SourceCard),
            _ => throw new InvalidOperationException($"未預期的卡片型別：{card.GetType().Name}"),
        };

        CardActivated?.Invoke(selection);
        _ = EnterDrillDownAsync(selection);
    }

    public async Task EnterDrillDownAsync(ExploreCardSelection selection)
    {
        if (_disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(selection);

        var cts = ReplaceCancellationSource(ref _drillDownCts);
        var ct = cts.Token;
        int requestVersion = Interlocked.Increment(ref _drillDownRequestVersion);

        await _runOnUiAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            IReadOnlyList<ExploreWorkItem> works = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return selection.Kind == ExploreCardSelectionKind.Author && selection.AuthorId.HasValue
                    ? _wallService.GetWorksForAuthor(selection.AuthorId.Value, selection.TagIds)
                    : _wallService.GetWorksForTags(selection.TagIds ?? Array.Empty<long>(), intersect: true);
            }, ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested || requestVersion != Volatile.Read(ref _drillDownRequestVersion) || _disposed)
            {
                return;
            }

            await _runOnUiAsync(() =>
            {
                if (ct.IsCancellationRequested || requestVersion != Volatile.Read(ref _drillDownRequestVersion) || _disposed)
                {
                    return;
                }

                _currentDrillDownSelection = selection;
                _allDrillDownRawWorks = works;

                var kindPrefix = selection.Kind switch
                {
                    ExploreCardSelectionKind.Author => "👤 ",
                    ExploreCardSelectionKind.RandomCombo => "⚡ ",
                    ExploreCardSelectionKind.Combo => "⚡ ",
                    _ => "🏷️ ",
                };

                DrillDownTitle = $"{kindPrefix}{selection.Title}";
                HasDrillDownAppliedTags = selection.Kind == ExploreCardSelectionKind.Author
                                          && selection.TagIds is { Count: > 0 };
                DrillDownSubtitle = BuildDrillDownSubtitle(selection, works.Count);
                IsDrillDownMode = true;
                RebuildDrillDownWorksOnUi(resetScroll: true);
                IsLoading = false;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 被新的鑽取請求取代
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                if (requestVersion == Volatile.Read(ref _drillDownRequestVersion) && !_disposed)
                {
                    ErrorMessage = ex.Message;
                    IsLoading = false;
                }
            }).ConfigureAwait(false);
        }
    }

    private string BuildDrillDownSubtitle(ExploreCardSelection selection, int rawCount)
    {
        if (selection.Kind == ExploreCardSelectionKind.Author)
        {
            return selection.TagIds is { Count: > 0 }
                ? $"作者作品集合（已套用 {selection.TagIds.Count} 個探索標籤篩選 · 原始命中 {rawCount:N0} 本）"
                : $"作者全部索引作品（共 {rawCount:N0} 本）";
        }

        if (selection.Kind is ExploreCardSelectionKind.Combo or ExploreCardSelectionKind.RandomCombo)
        {
            return $"雙題材交集作品（共 {rawCount:N0} 本）— 點擊作品可立即開啟閱讀";
        }

        return $"主題交集作品（共 {rawCount:N0} 本）— 點擊作品可立即開啟閱讀";
    }

    private void RebuildDrillDownWorksOnUi(bool resetScroll)
    {
        IEnumerable<ExploreWorkItem> filtered = _allDrillDownRawWorks;

        if (UnreadOnly)
        {
            filtered = filtered.Where(w => w.ReadStatus == ReadStatus.Unread);
        }

        var kw = string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword.Trim();
        if (!string.IsNullOrEmpty(kw))
        {
            filtered = filtered.Where(w =>
                w.Title.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || w.FileName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || w.AuthorName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || w.Tags.Any(t => t.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                   || t.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        var filteredList = filtered.ToList();
        IEnumerable<ExploreWorkItem> ordered = SelectedSortOption.Mode switch
        {
            ExploreSortMode.UnreadDesc => filteredList
                .OrderBy(w => w.ReadStatus == ReadStatus.Unread ? 0 : 1)
                .ThenBy(w => w.Title, NaturalStringComparer.Instance),
            ExploreSortMode.NameAsc => filteredList
                .OrderBy(w => w.Title, NaturalStringComparer.Instance),
            ExploreSortMode.RandomShuffle => ShuffleWorkItems(filteredList, _randomSeed),
            _ => filteredList,
        };

        var vms = ordered
            .Select(w => new ExploreWorkCardVm(w, OpenWork, RevealWorkInExplorer, _runOnUiAsync))
            .ToList();

        _allFilteredDrillDownWorkVms = vms;
        _visibleDrillDownWorkLimit = InitialWallCardPageSize;
        var initialSlice = vms.Take(_visibleDrillDownWorkLimit).ToList();
        DrillDownWorks.ReplaceAll(initialSlice);
        NotifyCardMetricsChanged();

        if (resetScroll)
        {
            ScrollResetRequested?.Invoke();
        }

        if (initialSlice.Count > 0)
        {
            StartCoverPreload(initialSlice, CoverPreloadCount);
        }
    }

    private static List<ExploreWorkItem> ShuffleWorkItems(List<ExploreWorkItem> items, int seed)
    {
        var copy = new List<ExploreWorkItem>(items);
        var rng = seed != 0 ? new Random(seed) : Random.Shared;
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    private void ClearDrillDownState()
    {
        CancelCts(ref _drillDownCts);
        _currentDrillDownSelection = null;
        _allDrillDownRawWorks = Array.Empty<ExploreWorkItem>();
        _allFilteredDrillDownWorkVms = Array.Empty<ExploreWorkCardVm>();
        _visibleDrillDownWorkLimit = InitialWallCardPageSize;
        DrillDownWorks.Clear();
        DrillDownTitle = string.Empty;
        DrillDownSubtitle = string.Empty;
        HasDrillDownAppliedTags = false;
        IsDrillDownMode = false;
    }

    private void OpenWork(ExploreWorkItem work)
    {
        if (_disposed)
        {
            return;
        }

        if (_externalLauncher.OpenWithDefaultApp(work.Path, out var error))
        {
            ShowToast("已開啟外部閱讀", $"《{work.Title}》", NotificationType.Success, 2);
        }
        else
        {
            ShowToast("開啟作品失敗", error ?? "無法開啟指定檔案或資料夾。", NotificationType.Error, 4);
        }
    }

    private void RevealWorkInExplorer(ExploreWorkItem work)
    {
        if (_disposed)
        {
            return;
        }

        if (!_externalLauncher.RevealInFileExplorer(work.Path, out var error))
        {
            ShowToast("無法開啟檔案總管", error ?? "無法定位檔案。", NotificationType.Error, 4);
        }
    }

    private IReadOnlyList<long> MergeWithSelectedTags(params long[] cardTagIds)
    {
        var merged = new List<long>(_selectedTagIds.Count + cardTagIds.Length);
        var seen = new HashSet<long>();
        foreach (var id in _selectedTagIds)
        {
            if (seen.Add(id))
            {
                merged.Add(id);
            }
        }

        foreach (var id in cardTagIds)
        {
            if (seen.Add(id))
            {
                merged.Add(id);
            }
        }

        return merged;
    }

    private void ScheduleDebouncedSearchReload()
    {
        ScheduleDebouncedAction(
            ref _searchDebounceCts,
            _searchDebounceDelay,
            async () =>
            {
                if (IsDrillDownMode)
                {
                    await _runOnUiAsync(() => RebuildDrillDownWorksOnUi(resetScroll: true)).ConfigureAwait(false);
                }
                else
                {
                    await LoadWallAsync().ConfigureAwait(false);
                }
            });
    }

    private void ScheduleDebouncedPickerReload()
    {
        ScheduleDebouncedAction(
            ref _pickerDebounceCts,
            _pickerDebounceDelay,
            () => LoadPickerTagsAsync());
    }

    private void ScheduleDebouncedAction(
        ref CancellationTokenSource? slot,
        TimeSpan delay,
        Func<Task> action)
    {
        var cts = ReplaceCancellationSource(ref slot);
        var ct = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }

                if (!ct.IsCancellationRequested && !_disposed)
                {
                    await action().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 持續輸入中取消前次防抖
            }
        }, ct);
    }

    private ExploreQueryOptions BuildQueryOptions(IReadOnlyList<long> selectedTagIds)
    {
        return new ExploreQueryOptions
        {
            SearchText = string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword.Trim(),
            SelectedTagIds = selectedTagIds,
            UnreadOnly = UnreadOnly,
            SortMode = SelectedSortOption.Mode,
            RandomSeed = _randomSeed,
            MaxCards = DefaultMaxCards,
        };
    }

    private CancellationTokenSource ReplaceCancellationSource(ref CancellationTokenSource? target)
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_ctsLock)
        {
            previous = target;
            target = next;
        }

        if (previous != null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            previous.Dispose();
        }

        return next;
    }

    private void CancelCts(ref CancellationTokenSource? cts)
    {
        CancellationTokenSource? current;
        lock (_ctsLock)
        {
            current = cts;
            cts = null;
        }

        if (current != null)
        {
            try
            {
                current.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            current.Dispose();
        }
    }

    private static string NormalizePickerCategory(string? categoryKey)
    {
        return (categoryKey ?? "all").Trim().ToLowerInvariant() switch
        {
            "content" => "content",
            "parody_character" => "parody_character",
            "artist_group" => "artist_group",
            _ => "all",
        };
    }

    private static int NextNonZeroSeed(int current)
    {
        int next = Random.Shared.Next(1, int.MaxValue);
        if (next == current)
        {
            next = current == int.MaxValue ? 1 : current + 1;
        }
        return next;
    }

    private void ShowToast(string title, string content, NotificationType type, int seconds)
    {
        _toastManager?
            .CreateToast()
            .WithTitle(title)
            .WithContent(content)
            .OfType(type)
            .Dismiss().After(TimeSpan.FromSeconds(seconds))
            .Dismiss().ByClicking()
            .Queue();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CancelCts(ref _wallCts);
        CancelCts(ref _drillDownCts);
        CancelCts(ref _searchDebounceCts);
        CancelCts(ref _pickerDebounceCts);
        CancelCts(ref _pickerCts);
        CancelCts(ref _coverLoadCts);
        CancelCts(ref _comboCoverCts);

        _coverLoadSemaphore.Dispose();
    }

    private sealed record WallSnapshotPayload(
        ExploreStatsSummary Stats,
        IReadOnlyList<ExploreTagBadge> SpotlightTags,
        bool PickerLoaded,
        bool ApplyPickerToUi,
        IReadOnlyList<ExploreTagBadge> PickerTags,
        IReadOnlyList<ExploreAuthorCard>? AuthorCards,
        IReadOnlyList<ExploreThemeCard>? ThemeCards,
        IReadOnlyList<ExploreComboCard>? ComboCards);
}

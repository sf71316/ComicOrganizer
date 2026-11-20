using Avalonia.Media.Imaging;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;
using NotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace ComicOrganizer.Avalonia.ViewModels;

public enum ReadingTrackRecordTab
{
    RecentlyRead = 0,
    RecentlyAdded = 1,
    Completed = 2,
}

public sealed partial class ReadingWorkItemVm : ViewModelBase
{
    private readonly Action<ReadingWorkItemVm> _onOpen;
    private readonly Action<ReadingWorkItemVm>? _onOpenExternal;
    private readonly Action<ReadingWorkItemVm> _onReveal;
    private readonly Action<ReadingWorkItemVm> _onMarkCompleted;
    private readonly Action<ReadingWorkItemVm> _onResetUnread;
    private readonly Func<Action, Task> _runOnUiAsync;

    public ReadingWorkItemVm(
        string path,
        string title,
        string authorName,
        string? coverThumbKey,
        int? pageCount,
        int? lastPage,
        ReadStatus readStatus,
        int readCount,
        long? timestampRaw,
        string timestampPrefix,
        Action<ReadingWorkItemVm> onOpen,
        Action<ReadingWorkItemVm> onReveal,
        Action<ReadingWorkItemVm> onMarkCompleted,
        Action<ReadingWorkItemVm> onResetUnread,
        Func<Action, Task>? runOnUiAsync = null,
        Action<ReadingWorkItemVm>? onOpenExternal = null)
    {
        Path = path;
        Title = title;
        AuthorName = authorName;
        PageCount = pageCount;
        LastPage = lastPage;
        ReadStatus = readStatus;
        ReadCount = readCount;

        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        _onOpenExternal = onOpenExternal;
        _onReveal = onReveal ?? throw new ArgumentNullException(nameof(onReveal));
        _onMarkCompleted = onMarkCompleted ?? throw new ArgumentNullException(nameof(onMarkCompleted));
        _onResetUnread = onResetUnread ?? throw new ArgumentNullException(nameof(onResetUnread));
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        AccentColorHex = ExploreVisualFormatting.PickAccentHex(title);
        FallbackGlyph = ExploreVisualFormatting.ExtractFallbackGlyph(title);

        if (lastPage is >= 0 && pageCount is > 0)
        {
            int displayPage = Math.Clamp(lastPage.Value + 1, 1, pageCount.Value);
            ProgressPercent = Math.Clamp((double)displayPage / pageCount.Value * 100.0, 0, 100);
            ProgressText = $"第 {displayPage:N0} / {pageCount.Value:N0} 頁（{(int)Math.Round(ProgressPercent)}%）";
            HasProgress = true;
        }
        else if (pageCount is > 0)
        {
            ProgressPercent = readStatus == ReadStatus.Completed ? 100 : 0;
            ProgressText = $"共 {pageCount.Value:N0} 頁";
            HasProgress = readStatus == ReadStatus.Completed;
        }
        else
        {
            ProgressPercent = readStatus == ReadStatus.Completed ? 100 : 0;
            ProgressText = readStatus switch
            {
                ReadStatus.Reading => "進行中",
                ReadStatus.Completed => "已完成",
                _ => "未讀",
            };
            HasProgress = false;
        }

        string formattedDate = FormatDate(timestampRaw);
        TimestampText = !string.IsNullOrEmpty(formattedDate)
            ? $"{timestampPrefix} {formattedDate}"
            : string.Empty;

        MetadataText = string.IsNullOrWhiteSpace(authorName)
            ? $"讀 {readCount:N0} 次"
            : $"{authorName} · 讀 {readCount:N0} 次";

        CanMarkCompleted = readStatus != ReadStatus.Completed;
        CanResetUnread = readStatus != ReadStatus.Unread || readCount > 0;

        var dummyWork = new ExploreWorkItem(
            AssetId: 0,
            Path: path,
            FileName: System.IO.Path.GetFileName(path),
            Title: title,
            AuthorId: null,
            AuthorName: authorName,
            Language: null,
            TranslationGroup: null,
            PageCount: pageCount,
            ReadStatus: readStatus,
            ReadCount: readCount,
            LastRead: timestampRaw,
            AddedAt: timestampRaw ?? 0,
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
            }
        };
    }

    public string Path { get; }

    public string Title { get; }

    public string AuthorName { get; }

    public int? PageCount { get; }

    public int? LastPage { get; }

    public ReadStatus ReadStatus { get; }

    public int ReadCount { get; }

    public CoverSlotVm CoverSlot { get; }

    public Bitmap? Cover1 => CoverSlot.Image;

    public bool HasCover1 => Cover1 != null;

    public string AccentColorHex { get; }

    public string FallbackGlyph { get; }

    public bool HasProgress { get; }

    public double ProgressPercent { get; }

    public string ProgressText { get; }

    public string TimestampText { get; }

    public bool HasTimestamp => !string.IsNullOrEmpty(TimestampText);

    public string MetadataText { get; }

    public bool CanMarkCompleted { get; }

    public bool CanResetUnread { get; }

    [RelayCommand]
    private void Open() => _onOpen(this);

    [RelayCommand]
    private void OpenExternal() => _onOpenExternal?.Invoke(this);

    [RelayCommand]
    private void RevealInExplorer() => _onReveal(this);

    [RelayCommand]
    private void MarkCompleted() => _onMarkCompleted(this);

    [RelayCommand]
    private void ResetUnread() => _onResetUnread(this);

    public Task EnsureCoverLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim limiter,
        CancellationToken ct = default)
        => CoverSlot.EnsureLoadedAsync(coverService, limiter, _runOnUiAsync, ct);

    private static string FormatDate(long? rawTs)
    {
        if (rawTs is not > 0)
        {
            return string.Empty;
        }

        long ms = rawTs.Value < 100_000_000_000L ? rawTs.Value * 1000L : rawTs.Value;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}

public sealed class ReadingRankEntryVm
{
    public ReadingRankEntryVm(
        int rank,
        string icon,
        string title,
        string subText,
        int totalReads,
        double ratioPercent,
        string dslQuery,
        Action<string> onSearchInGallery)
    {
        Rank = rank;
        RankText = $"#{rank}";
        Icon = icon;
        Title = title;
        SubText = subText;
        TotalReads = totalReads;
        ReadsText = $"{totalReads:N0} 次";
        RatioPercent = Math.Clamp(ratioPercent, 4, 100);
        DslQuery = dslQuery;
        OpenInGalleryCommand = new RelayCommand(() => onSearchInGallery(DslQuery));
    }

    public int Rank { get; }

    public string RankText { get; }

    public string Icon { get; }

    public string Title { get; }

    public string SubText { get; }

    public bool HasSubText => !string.IsNullOrWhiteSpace(SubText);

    public int TotalReads { get; }

    public string ReadsText { get; }

    public double RatioPercent { get; }

    public string DslQuery { get; }

    public IRelayCommand OpenInGalleryCommand { get; }
}

/// <summary>
/// Phase 2 W2.3「🕒 閱讀軌跡」專頁 ViewModel：
/// 整合「繼續閱讀（進行中＋頁碼進度）」、「近期紀錄牆（最近閱讀／最近新增／已完成）」與
/// 「最常閱讀作者／標籤排行（點擊一鍵跳轉畫廊篩選）」，完整支援共用資料庫唯讀保護（1.A）。
/// </summary>
public sealed partial class ReadingTrackPageViewModel : ViewModelBase, IDisposable
{
    private const int InitialTabSliceSize = 24;
    private const int TabSliceStep = 24;
    private const int RankTopLimit = 12;
    private const int RecentFetchLimit = 60;

    private readonly AppDataContext? _dataContext;
    private readonly IAssetMetaStore _store;
    private readonly SkiaCoverService _coverService;
    private readonly IExternalLauncher _externalLauncher;
    private readonly IReaderWindowLauncher _readerLauncher;
    private readonly ISukiToastManager? _toastManager;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly SemaphoreSlim _coverLoadSemaphore = new(4, 4);
    private readonly object _ctsLock = new();

    private IReadOnlyList<ReadingWorkItemVm> _recentlyReadItems = Array.Empty<ReadingWorkItemVm>();
    private IReadOnlyList<ReadingWorkItemVm> _recentlyAddedItems = Array.Empty<ReadingWorkItemVm>();
    private IReadOnlyList<ReadingWorkItemVm> _completedItems = Array.Empty<ReadingWorkItemVm>();
    private int _visibleRecordLimit = InitialTabSliceSize;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _coverCts;
    private int _loadVersion;
    private volatile bool _disposed;

    public ReadingTrackPageViewModel(AppDataContext dataContext, SkiaCoverService coverService, bool autoLoad = true)
        : this(
            dataContext?.MetaStore ?? throw new ArgumentNullException(nameof(dataContext)),
            coverService,
            new SystemExternalLauncher(),
            dataContext.ToastManager,
            ExploreCardVmBase.DefaultRunOnUiAsync,
            autoLoad: autoLoad,
            isReadOnlyDatabase: dataContext.IsReadOnlyDatabase,
            readerLauncher: new AvaloniaReaderWindowLauncher(dataContext))
    {
        _dataContext = dataContext;
    }

    public ReadingTrackPageViewModel(
        IAssetMetaStore store,
        SkiaCoverService coverService,
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

        InitializationTask = autoLoad ? RefreshAsync() : Task.CompletedTask;
    }

    public Task InitializationTask { get; private set; }

    public bool IsReadOnlyDatabase { get; }

    public string ReadOnlyBannerText => "🔒 共用資料庫唯讀保護中（狀態變更與重置操作僅在可寫入模式開放，不改寫 cache.db）";

    public event Action<string>? OpenInGalleryRequested;

    public BatchObservableCollection<ReadingWorkItemVm> InProgressWorks { get; } = [];

    public BatchObservableCollection<ReadingWorkItemVm> ActiveRecordWorks { get; } = [];

    public BatchObservableCollection<ReadingRankEntryVm> TopAuthors { get; } = [];

    public BatchObservableCollection<ReadingRankEntryVm> TopTags { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecentlyReadTabActive))]
    [NotifyPropertyChangedFor(nameof(IsRecentlyAddedTabActive))]
    [NotifyPropertyChangedFor(nameof(IsCompletedTabActive))]
    private ReadingTrackRecordTab _activeRecordTab = ReadingTrackRecordTab.RecentlyRead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InProgressBadgeText))]
    private int _inProgressCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecentlyReadBadgeText))]
    private int _recentlyReadCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecentlyAddedBadgeText))]
    private int _recentlyAddedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletedBadgeText))]
    private int _completedCount;

    public bool HasInProgressWorks => InProgressWorks.Count > 0;

    public bool HasActiveRecordWorks => ActiveRecordWorks.Count > 0;

    public bool HasMoreActiveRecords => ActiveRecordWorks.Count < GetActiveTabSourceList().Count;

    public string LoadMoreRecordsText
    {
        get
        {
            int remain = Math.Max(0, GetActiveTabSourceList().Count - ActiveRecordWorks.Count);
            int next = Math.Min(TabSliceStep, remain);
            return $"載入更多紀錄（+{next:N0}，還剩 {remain:N0} 筆）";
        }
    }

    public bool HasTopAuthors => TopAuthors.Count > 0;

    public bool HasTopTags => TopTags.Count > 0;

    public bool IsRecentlyReadTabActive => ActiveRecordTab == ReadingTrackRecordTab.RecentlyRead;

    public bool IsRecentlyAddedTabActive => ActiveRecordTab == ReadingTrackRecordTab.RecentlyAdded;

    public bool IsCompletedTabActive => ActiveRecordTab == ReadingTrackRecordTab.Completed;

    public string InProgressBadgeText => $"{InProgressCount:N0} 本進行中";

    public string RecentlyReadBadgeText => $"最近閱讀 ({RecentlyReadCount:N0})";

    public string RecentlyAddedBadgeText => $"最近新增 ({RecentlyAddedCount:N0})";

    public string CompletedBadgeText => $"已完成 ({CompletedCount:N0})";

    partial void OnActiveRecordTabChanged(ReadingTrackRecordTab value)
    {
        if (_disposed)
        {
            return;
        }

        ApplyActiveTabSliceOnUi();
    }

    [RelayCommand]
    private void SelectRecentlyReadTab() => ActiveRecordTab = ReadingTrackRecordTab.RecentlyRead;

    [RelayCommand]
    private void SelectRecentlyAddedTab() => ActiveRecordTab = ReadingTrackRecordTab.RecentlyAdded;

    [RelayCommand]
    private void SelectCompletedTab() => ActiveRecordTab = ReadingTrackRecordTab.Completed;

    [RelayCommand]
    private void LoadMoreRecords()
    {
        if (_disposed || !HasMoreActiveRecords)
        {
            return;
        }

        var source = GetActiveTabSourceList();
        int before = ActiveRecordWorks.Count;
        _visibleRecordLimit += TabSliceStep;
        int take = Math.Max(0, Math.Min(TabSliceStep, source.Count - before));
        if (take == 0)
        {
            return;
        }

        var nextBatch = new List<ReadingWorkItemVm>(take);
        for (int i = 0; i < take; i++)
        {
            nextBatch.Add(source[before + i]);
        }

        ActiveRecordWorks.AddRange(nextBatch);
        NotifyRecordTabMetrics();
        StartCoverPreload(nextBatch, nextBatch.Count, cancelPrevious: false);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        var cts = ReplaceCts(ref _loadCts);
        var ct = cts.Token;
        int version = Interlocked.Increment(ref _loadVersion);

        await _runOnUiAsync(() => IsLoading = true).ConfigureAwait(false);

        try
        {
            var payload = await Task.Run(() => BuildTrackPayload(ct), ct).ConfigureAwait(false);
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

                InProgressWorks.ReplaceAll(payload.InProgress);
                _recentlyReadItems = payload.RecentlyRead;
                _recentlyAddedItems = payload.RecentlyAdded;
                _completedItems = payload.Completed;

                InProgressCount = payload.InProgress.Count;
                RecentlyReadCount = payload.RecentlyRead.Count;
                RecentlyAddedCount = payload.RecentlyAdded.Count;
                CompletedCount = payload.Completed.Count;

                TopAuthors.ReplaceAll(payload.TopAuthors);
                TopTags.ReplaceAll(payload.TopTags);

                ApplyActiveTabSliceOnUi();
                OnPropertyChanged(nameof(HasInProgressWorks));
                OnPropertyChanged(nameof(HasTopAuthors));
                OnPropertyChanged(nameof(HasTopTags));
                IsLoading = false;

                var toWarm = payload.InProgress.Take(12).Concat(ActiveRecordWorks.Take(12)).ToList();
                if (toWarm.Count > 0)
                {
                    StartCoverPreload(toWarm, toWarm.Count, cancelPrevious: true);
                }
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
                ShowToast("載入閱讀軌跡失敗", ex.Message, NotificationType.Error);
            }).ConfigureAwait(false);
        }
    }

    public Task RequestCoverAsync(ReadingWorkItemVm? item)
    {
        if (_disposed || item == null)
        {
            return Task.CompletedTask;
        }

        return item.EnsureCoverLoadedAsync(_coverService, _coverLoadSemaphore);
    }

    private TrackPayload BuildTrackPayload(CancellationToken ct)
    {
        var snapshot = _store.GetExploreCorpusSnapshot("zh-TW");
        var workRowByPath = new Dictionary<string, ExploreWorkRow>(snapshot.Works.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in snapshot.Works)
        {
            workRowByPath[row.Path] = row;
        }

        var inProgressPaths = _store.ListReadingInProgress();
        var recentReadPaths = _store.ListRecentlyRead(RecentFetchLimit);
        var recentAddedPaths = _store.ListRecentlyAdded(RecentFetchLimit);
        var completedPaths = _store.ListWorksByReadStatus(ReadStatus.Completed);

        // 只針對進行中少數作品補查 last_page，避免全表 N+1
        var inProgressLastPageMap = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in inProgressPaths)
        {
            ct.ThrowIfCancellationRequested();
            inProgressLastPageMap[path] = _store.GetLastPage(path);
        }

        var inProgressVms = BuildWorkItems(inProgressPaths, workRowByPath, inProgressLastPageMap, ReadStatus.Reading, "上次閱讀", useLastReadTs: true, ct);
        var recentReadVms = BuildWorkItems(recentReadPaths, workRowByPath, null, null, "最後閱讀", useLastReadTs: true, ct);
        var recentAddedVms = BuildWorkItems(recentAddedPaths, workRowByPath, null, null, "入庫時間", useLastReadTs: false, ct);
        var completedVms = BuildWorkItems(completedPaths, workRowByPath, null, ReadStatus.Completed, "完成／閱讀", useLastReadTs: true, ct);

        var rawTopAuthors = _store.ListMostReadAuthors(RankTopLimit);
        int maxAuthorReads = rawTopAuthors.Count > 0 ? Math.Max(1, rawTopAuthors[0].TotalReads) : 1;
        var authorRankVms = rawTopAuthors.Select((entry, idx) =>
        {
            var token = SearchQueryParser.BuildFieldToken("artist", entry.Author.MainName);
            double ratio = (double)entry.TotalReads / maxAuthorReads * 100.0;
            return new ReadingRankEntryVm(
                idx + 1,
                "👤",
                entry.Author.MainName,
                "點擊在畫廊瀏覽作者作品",
                entry.TotalReads,
                ratio,
                token,
                OnRankEntryClicked);
        }).ToList();

        var rawTopTags = _store.ListMostReadTags(RankTopLimit);
        var tagTrans = rawTopTags.Count > 0
            ? _store.GetTagTranslations(rawTopTags.Select(t => t.Tag.Id).ToList(), "zh-TW")
            : new Dictionary<long, string>();
        int maxTagReads = rawTopTags.Count > 0 ? Math.Max(1, rawTopTags[0].TotalReads) : 1;
        var tagRankVms = rawTopTags.Select((entry, idx) =>
        {
            tagTrans.TryGetValue(entry.Tag.Id, out var zh);
            var display = !string.IsNullOrEmpty(zh) && !string.Equals(zh, entry.Tag.Name, StringComparison.OrdinalIgnoreCase)
                ? zh
                : entry.Tag.Name;
            var sub = !string.IsNullOrEmpty(zh) && !string.Equals(zh, entry.Tag.Name, StringComparison.OrdinalIgnoreCase)
                ? entry.Tag.Name
                : (entry.Tag.Category ?? string.Empty);
            var token = SearchQueryParser.BuildFieldToken("tag", display);
            double ratio = (double)entry.TotalReads / maxTagReads * 100.0;
            return new ReadingRankEntryVm(
                idx + 1,
                "🏷️",
                display,
                sub,
                entry.TotalReads,
                ratio,
                token,
                OnRankEntryClicked);
        }).ToList();

        return new TrackPayload(inProgressVms, recentReadVms, recentAddedVms, completedVms, authorRankVms, tagRankVms);
    }

    private List<ReadingWorkItemVm> BuildWorkItems(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, ExploreWorkRow> workRowByPath,
        IReadOnlyDictionary<string, int?>? lastPageMap,
        ReadStatus? statusOverride,
        string tsPrefix,
        bool useLastReadTs,
        CancellationToken ct)
    {
        var list = new List<ReadingWorkItemVm>(paths.Count);
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            workRowByPath.TryGetValue(path, out var row);

            string fileName = System.IO.Path.GetFileName(path);
            string title = !string.IsNullOrWhiteSpace(row?.Title) ? row!.Title! : (row?.FileName ?? fileName);
            string authorName = row?.AuthorName ?? string.Empty;
            var status = statusOverride ?? (row != null && Enum.IsDefined(typeof(ReadStatus), row.ReadStatus)
                ? (ReadStatus)row.ReadStatus
                : ReadStatus.Unread);

            int? lastPage = null;
            if (lastPageMap != null)
            {
                lastPageMap.TryGetValue(path, out lastPage);
            }

            long? ts = useLastReadTs
                ? (row?.LastRead ?? row?.AddedAt)
                : row?.AddedAt;

            list.Add(new ReadingWorkItemVm(
                path,
                title,
                authorName,
                row?.CoverThumbKey,
                row?.PageCount,
                lastPage,
                status,
                row?.ReadCount ?? 0,
                ts,
                tsPrefix,
                OnOpenWork,
                OnRevealWork,
                OnMarkCompleted,
                OnResetUnread,
                _runOnUiAsync,
                OnOpenWorkExternal));
        }

        return list;
    }

    private IReadOnlyList<ReadingWorkItemVm> GetActiveTabSourceList() => ActiveRecordTab switch
    {
        ReadingTrackRecordTab.RecentlyAdded => _recentlyAddedItems,
        ReadingTrackRecordTab.Completed => _completedItems,
        _ => _recentlyReadItems,
    };

    private void ApplyActiveTabSliceOnUi()
    {
        _visibleRecordLimit = InitialTabSliceSize;
        var source = GetActiveTabSourceList();
        var slice = source.Take(_visibleRecordLimit).ToList();
        ActiveRecordWorks.ReplaceAll(slice);
        NotifyRecordTabMetrics();

        if (slice.Count > 0)
        {
            StartCoverPreload(slice, Math.Min(slice.Count, 18), cancelPrevious: false);
        }
    }

    private void NotifyRecordTabMetrics()
    {
        OnPropertyChanged(nameof(HasActiveRecordWorks));
        OnPropertyChanged(nameof(HasMoreActiveRecords));
        OnPropertyChanged(nameof(LoadMoreRecordsText));
    }

    private void StartCoverPreload(IReadOnlyList<ReadingWorkItemVm> items, int count, bool cancelPrevious)
    {
        var cts = cancelPrevious ? ReplaceCts(ref _coverCts) : new CancellationTokenSource();
        var ct = cts.Token;
        int limit = Math.Min(items.Count, count);

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

    private void OnRankEntryClicked(string dsl)
    {
        if (!string.IsNullOrWhiteSpace(dsl))
        {
            OpenInGalleryRequested?.Invoke(dsl);
        }
    }

    private void OnOpenWork(ReadingWorkItemVm item)
    {
        if (!_readerLauncher.OpenInReader(item.Path, item.Title, item.AuthorName, OnReaderClosedRefreshIfWritable, out var error))
        {
            ShowToast("無法開啟內建閱讀器", error ?? item.Path, NotificationType.Warning);
        }
    }

    private void OnOpenWorkExternal(ReadingWorkItemVm item)
    {
        if (!_externalLauncher.OpenWithDefaultApp(item.Path, out var error))
        {
            ShowToast("無法開啟作品", error ?? item.Path, NotificationType.Warning);
        }
    }

    private void OnReaderClosedRefreshIfWritable()
    {
        if (!_disposed && !IsReadOnlyDatabase)
        {
            _ = RefreshAsync();
        }
    }

    private void OnRevealWork(ReadingWorkItemVm item)
    {
        if (!_externalLauncher.RevealInFileExplorer(item.Path, out var error))
        {
            ShowToast("無法開啟檔案總管", error ?? item.Path, NotificationType.Warning);
        }
    }

    private void OnMarkCompleted(ReadingWorkItemVm item)
    {
        if (!EnsureWritable("標記已完成"))
        {
            return;
        }

        _store.SetReadStatus(item.Path, ReadStatus.Completed);
        ShowToast("已標記完成", item.Title, NotificationType.Success);
        _ = RefreshAsync();
    }

    private void OnResetUnread(ReadingWorkItemVm item)
    {
        if (!EnsureWritable("重置閱讀紀錄"))
        {
            return;
        }

        _store.ResetReadingRecord(item.Path);
        ShowToast("已重置閱讀紀錄", item.Title, NotificationType.Information);
        _ = RefreshAsync();
    }

    private bool EnsureWritable(string actionName)
    {
        if (!IsReadOnlyDatabase)
        {
            return true;
        }

        ShowToast(
            "共用資料庫唯讀保護",
            $"目前以唯讀模式瀏覽共用開發庫，「{actionName}」不會寫入 D:\\ComicOrganizer\\cache.db。",
            NotificationType.Warning);
        return false;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_ctsLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;
        }

        _coverLoadSemaphore.Dispose();
    }

    private sealed record TrackPayload(
        IReadOnlyList<ReadingWorkItemVm> InProgress,
        IReadOnlyList<ReadingWorkItemVm> RecentlyRead,
        IReadOnlyList<ReadingWorkItemVm> RecentlyAdded,
        IReadOnlyList<ReadingWorkItemVm> Completed,
        IReadOnlyList<ReadingRankEntryVm> TopAuthors,
        IReadOnlyList<ReadingRankEntryVm> TopTags);
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ComicOrganizer.Avalonia.ViewModels;

/// <summary>
/// 支援整批替換與批次追加的 ObservableCollection：僅發出單一次 Reset 通知，避免 UI 逐筆重複排版。
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        RaiseResetNotifications();
    }

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        var added = items as List<T> ?? items.ToList();
        if (added.Count == 0)
        {
            return;
        }

        int startIndex = Items.Count;
        foreach (var item in added)
        {
            Items.Add(item);
        }

        // 以標準範圍 Add 通知取代 Reset，避免 ItemsControl 銷毀並重建既有所有卡片容器
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            (System.Collections.IList)added,
            startIndex));
    }

    private void RaiseResetNotifications()
    {
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public enum ExploreMainTab
{
    AuthorExplore = 0,
    ThemeParodyWall = 1,
}

public enum ExploreCoverStyle
{
    Deck = 0,
    Single = 1,
}

public enum ExploreCardSelectionKind
{
    Author = 0,
    Theme = 1,
    Combo = 2,
    RandomCombo = 3,
}

public sealed record ExploreSortOption(ExploreSortMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record ExploreCardSelection(
    ExploreCardSelectionKind Kind,
    string Title,
    long? AuthorId = null,
    IReadOnlyList<long>? TagIds = null,
    ExploreAuthorCard? AuthorCard = null,
    ExploreThemeCard? ThemeCard = null,
    ExploreComboCard? ComboCard = null);

public sealed partial class ExplorePickerCategoryItemVm : ObservableObject
{
    private readonly Action<string> _onSelect;

    public ExplorePickerCategoryItemVm(string key, string label, bool isSelected, Action<string> onSelect)
    {
        Key = key;
        Label = label;
        _isSelected = isSelected;
        _onSelect = onSelect ?? throw new ArgumentNullException(nameof(onSelect));
    }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => _onSelect(Key);
}

public sealed partial class ExploreTagChipVm : ObservableObject
{
    private readonly Action<ExploreTagChipVm> _onToggle;

    public ExploreTagChipVm(ExploreTagBadge badge, bool isSelected, Action<ExploreTagChipVm> onToggle)
    {
        ArgumentNullException.ThrowIfNull(badge);
        TagId = badge.TagId;
        Name = badge.Name;
        DisplayName = badge.DisplayName;
        Category = badge.Category;
        CategoryLabel = ExploreVisualFormatting.FormatCategoryLabel(badge.Category);
        WorkCount = badge.WorkCount;
        IsStructural = badge.IsStructural;
        _isSelected = isSelected;
        _onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
    }

    public long TagId { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public string CategoryLabel { get; }

    public int WorkCount { get; }

    public bool IsStructural { get; }

    public string WorkCountText => $"{WorkCount:N0}";

    public bool HasDifferentOriginalName => !string.Equals(Name, DisplayName, StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Toggle() => _onToggle(this);
}

/// <summary>
/// 單張代表作封面槽位 ViewModel。
/// 注意：<see cref="Image"/> 實例由 <see cref="SkiaCoverService"/> 記憶體 LRU 快取共享管理，此處不主動 Dispose。
/// 虛擬化清單可呼叫 <see cref="ReleaseImage"/> 放掉參考，沒有人參考後由 GC 回收原生記憶體。
/// </summary>
public sealed partial class CoverSlotVm : ObservableObject
{
    private readonly object _loadLock = new();
    private Task? _inflightTask;

    // 進行中那次載入使用的取消權杖：已取消時（例如卡片被回收）不能沿用，要另起一次載入
    private CancellationToken _inflightToken;

    // _completed：目前已持有封面（或確定不需要再載入）；ReleaseImage 會重設，之後可再載入
    private volatile bool _completed;

    // _unavailable：沒有封面或載入失敗，之後不再重試（避免反覆解壓縮）
    private volatile bool _unavailable;

    // _imageWanted：ReleaseImage 後若仍有進行中的載入，完成時不套用圖片
    private volatile bool _imageWanted = true;

    /// <param name="decodeDipSize">
    /// 封面顯示框大小（DIP），用於依顯示大小縮小解碼以節省記憶體；<c>null</c> 表示原尺寸解碼。
    /// </param>
    public CoverSlotVm(ExploreWorkItem work, string accentHex, CoverDipSize? decodeDipSize = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        DecodeDipSize = decodeDipSize;
        AssetId = work.AssetId;
        SourcePath = work.Path;
        ThumbKey = work.CoverThumbKey;
        WorkTitle = work.Title;
        FallbackGlyph = ExploreVisualFormatting.ExtractFallbackGlyph(work.Title);
        AccentHex = accentHex;
    }

    public long AssetId { get; }

    public string SourcePath { get; }

    public string? ThumbKey { get; }

    public string WorkTitle { get; }

    public string FallbackGlyph { get; }

    public string AccentHex { get; }

    /// <summary>封面顯示框大小（DIP）；<c>null</c> 表示原尺寸解碼。</summary>
    public CoverDipSize? DecodeDipSize { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private Bitmap? _image;

    public bool HasImage => Image != null;

    public Task EnsureLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        Func<Action, Task> runOnUiAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(coverService);
        ArgumentNullException.ThrowIfNull(concurrencyLimiter);
        ArgumentNullException.ThrowIfNull(runOnUiAsync);

        _imageWanted = true;
        if (_completed || _unavailable || ct.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        lock (_loadLock)
        {
            if (_completed || _unavailable || ct.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            if (_inflightTask != null && !_inflightTask.IsCompleted && !_inflightToken.IsCancellationRequested)
            {
                return _inflightTask;
            }

            _inflightToken = ct;
            _inflightTask = LoadCoreAsync(coverService, concurrencyLimiter, runOnUiAsync, ct);
            return _inflightTask;
        }
    }

    /// <summary>
    /// 放掉目前持有的封面參考（需在 UI 執行緒呼叫）。之後再呼叫 <see cref="EnsureLoadedAsync"/> 會重新載入，
    /// 通常直接命中 <see cref="SkiaCoverService"/> 的記憶體快取或磁碟縮圖。
    /// </summary>
    public void ReleaseImage()
    {
        _imageWanted = false;
        _completed = false;
        if (Image != null)
        {
            Image = null;
        }
    }

    private async Task LoadCoreAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        Func<Action, Task> runOnUiAsync,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && string.IsNullOrWhiteSpace(ThumbKey))
        {
            _unavailable = true;
            return;
        }

        var acquired = false;
        try
        {
            await concurrencyLimiter.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;

            if (ct.IsCancellationRequested)
            {
                return;
            }

            var decodeSize = DecodeDipSize is { } dip
                ? coverService.ToDecodeSize(dip.Width, dip.Height)
                : CoverDecodeSize.Original;
            var bitmap = await coverService
                .LoadOrExtractCoverAsync(SourcePath, ThumbKey, ct, decodeSize)
                .ConfigureAwait(false);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (bitmap != null)
            {
                // 在 UI 執行緒判斷並設定，與 ReleaseImage 依序執行，避免放掉後又被舊的載入結果填回
                await runOnUiAsync(() =>
                {
                    if (_imageWanted)
                    {
                        Image = bitmap;
                        _completed = true;
                    }
                }).ConfigureAwait(false);
            }
            else
            {
                _unavailable = true;
            }
        }
        catch (OperationCanceledException)
        {
            // 被取消時保留 _completed = false，讓後續重新進入可視範圍時可再嘗試載入
        }
        catch (ObjectDisposedException)
        {
            _unavailable = true;
        }
        catch
        {
            _unavailable = true;
        }
        finally
        {
            if (acquired)
            {
                try
                {
                    concurrencyLimiter.Release();
                }
                catch (ObjectDisposedException)
                {
                    // ViewModel 已釋放時安全忽略
                }
            }
        }
    }
}

public abstract partial class ExploreCardVmBase : ViewModelBase
{
    private readonly Action<ExploreCardVmBase> _onSelectCard;
    private readonly Func<Action, Task> _runOnUiAsync;

    protected ExploreCardVmBase(
        IReadOnlyList<ExploreWorkItem> representativeWorks,
        string fallbackText,
        string accentColorHex,
        Action<ExploreCardVmBase> onSelectCard,
        Func<Action, Task>? runOnUiAsync = null,
        CoverDipSize? primaryCoverDecodeDipSize = null,
        CoverDipSize? secondaryCoverDecodeDipSize = null)
    {
        _onSelectCard = onSelectCard ?? throw new ArgumentNullException(nameof(onSelectCard));
        _runOnUiAsync = runOnUiAsync ?? DefaultRunOnUiAsync;
        FallbackGlyph = ExploreVisualFormatting.ExtractFallbackGlyph(fallbackText);
        AccentColorHex = accentColorHex;

        var slots = new List<CoverSlotVm>(Math.Min(3, representativeWorks.Count));
        for (int i = 0; i < representativeWorks.Count && i < 3; i++)
        {
            // 第 1 張與第 2、3 張可用不同解碼大小（見 CoverDipSize：單張精選模式只顯示第 1 張且尺寸較大）
            var slot = new CoverSlotVm(
                representativeWorks[i],
                accentColorHex,
                i == 0 ? primaryCoverDecodeDipSize : secondaryCoverDecodeDipSize);
            int slotIndex = i;
            slot.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CoverSlotVm.Image))
                {
                    NotifyCoverSlotChanged(slotIndex);
                }
            };
            slots.Add(slot);
        }

        CoverSlots = slots;
    }

    public IReadOnlyList<CoverSlotVm> CoverSlots { get; }

    public CoverSlotVm? CoverSlot1 => CoverSlots.Count > 0 ? CoverSlots[0] : null;

    public CoverSlotVm? CoverSlot2 => CoverSlots.Count > 1 ? CoverSlots[1] : null;

    public CoverSlotVm? CoverSlot3 => CoverSlots.Count > 2 ? CoverSlots[2] : null;

    public Bitmap? Cover1 => CoverSlot1?.Image;

    public Bitmap? Cover2 => CoverSlot2?.Image;

    public Bitmap? Cover3 => CoverSlot3?.Image;

    public bool HasCover1 => Cover1 != null;

    public bool HasCover2 => Cover2 != null;

    public bool HasCover3 => Cover3 != null;

    public int RepresentativeCount => CoverSlots.Count;

    public string FallbackGlyph { get; }

    public string AccentColorHex { get; }

    public abstract string CountBadgeText { get; }

    [RelayCommand]
    private void SelectCard() => _onSelectCard(this);

    public async Task EnsureCoversLoadedAsync(SkiaCoverService coverService, CancellationToken ct = default)
    {
        using var localLimiter = new SemaphoreSlim(4, 4);
        await EnsureCoversLoadedCoreAsync(coverService, localLimiter, ct).ConfigureAwait(false);
    }

    public Task EnsureCoversLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(concurrencyLimiter);
        return EnsureCoverSlotRangeLoadedAsync(coverService, concurrencyLimiter, 0, CoverSlots.Count, ct);
    }

    public Task EnsurePrimaryCoverLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(concurrencyLimiter);
        return EnsureCoverSlotRangeLoadedAsync(coverService, concurrencyLimiter, 0, 1, ct);
    }

    public Task EnsureSecondaryCoversLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(concurrencyLimiter);
        return CoverSlots.Count <= 1
            ? Task.CompletedTask
            : EnsureCoverSlotRangeLoadedAsync(coverService, concurrencyLimiter, 1, CoverSlots.Count - 1, ct);
    }

    private Task EnsureCoversLoadedCoreAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        CancellationToken ct)
        => EnsureCoverSlotRangeLoadedAsync(coverService, concurrencyLimiter, 0, CoverSlots.Count, ct);

    private async Task EnsureCoverSlotRangeLoadedAsync(
        SkiaCoverService coverService,
        SemaphoreSlim concurrencyLimiter,
        int startSlotIndex,
        int maxCount,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(coverService);
        if (CoverSlots.Count == 0 || startSlotIndex >= CoverSlots.Count || maxCount <= 0 || ct.IsCancellationRequested)
        {
            return;
        }

        int endExclusive = Math.Min(CoverSlots.Count, startSlotIndex + maxCount);
        int count = endExclusive - startSlotIndex;
        if (count == 1)
        {
            await CoverSlots[startSlotIndex]
                .EnsureLoadedAsync(coverService, concurrencyLimiter, _runOnUiAsync, ct)
                .ConfigureAwait(false);
            return;
        }

        var tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = CoverSlots[startSlotIndex + i].EnsureLoadedAsync(coverService, concurrencyLimiter, _runOnUiAsync, ct);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void NotifyCoverSlotChanged(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0:
                OnPropertyChanged(nameof(Cover1));
                OnPropertyChanged(nameof(HasCover1));
                break;
            case 1:
                OnPropertyChanged(nameof(Cover2));
                OnPropertyChanged(nameof(HasCover2));
                break;
            case 2:
                OnPropertyChanged(nameof(Cover3));
                OnPropertyChanged(nameof(HasCover3));
                break;
        }
    }

    internal static Task DefaultRunOnUiAsync(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}

public sealed class ExploreAuthorCardVm : ExploreCardVmBase
{
    public ExploreAuthorCardVm(
        ExploreAuthorCard card,
        Action<ExploreCardVmBase> onSelectCard,
        Func<Action, Task>? runOnUiAsync = null)
        : base(
            card.RepresentativeWorks,
            card.MainName,
            ExploreVisualFormatting.PickAccentHex(card.MainName),
            onSelectCard,
            runOnUiAsync,
            primaryCoverDecodeDipSize: null,
            secondaryCoverDecodeDipSize: CoverDipSize.DeckCover)
    {
        SourceCard = card;
        AuthorId = card.AuthorId;
        MainName = card.MainName;
        Aliases = card.Aliases;
        AliasesDisplay = card.Aliases.Count > 0
            ? string.Join(" / ", card.Aliases.Take(3))
            : string.Empty;
        FolderPath = card.FolderPath;
        TotalWorks = card.TotalWorks;
        AllLibraryWorks = card.AllLibraryWorks;
        UnreadWorks = card.UnreadWorks;
        ReadingWorks = card.ReadingWorks;
        CompletedWorks = card.CompletedWorks;
        DistinctTagCount = card.DistinctTagCount;
        TopDisplayTags = card.TopTags.Take(3).ToList();
        OverflowTagCount = Math.Max(0, card.DistinctTagCount - TopDisplayTags.Count);
    }

    public ExploreAuthorCard SourceCard { get; }

    public long AuthorId { get; }

    public string MainName { get; }

    public IReadOnlyList<string> Aliases { get; }

    public string AliasesDisplay { get; }

    public bool HasAliases => !string.IsNullOrWhiteSpace(AliasesDisplay);

    public string? FolderPath { get; }

    public int TotalWorks { get; }

    public int AllLibraryWorks { get; }

    public int UnreadWorks { get; }

    public int ReadingWorks { get; }

    public int CompletedWorks { get; }

    public int DistinctTagCount { get; }

    /// <summary>作者卡底部展示之主要題材標籤（嚴格上限 3 顆）。</summary>
    public IReadOnlyList<ExploreTagBadge> TopDisplayTags { get; }

    public int OverflowTagCount { get; }

    public bool HasOverflowTags => OverflowTagCount > 0;

    public string OverflowTagText => $"+{OverflowTagCount}";

    public bool HasUnread => UnreadWorks > 0;

    public string TotalSummaryText => UnreadWorks > 0
        ? $"{TotalWorks} 本 · {UnreadWorks} 未讀"
        : $"{TotalWorks} 本";

    public override string CountBadgeText => TotalSummaryText;
}

public sealed class ExploreThemeCardVm : ExploreCardVmBase
{
    public ExploreThemeCardVm(
        ExploreThemeCard card,
        Action<ExploreCardVmBase> onSelectCard,
        Func<Action, Task>? runOnUiAsync = null)
        : base(
            card.RepresentativeWorks,
            card.DisplayName,
            ExploreVisualFormatting.PickAccentHex(card.Name),
            onSelectCard,
            runOnUiAsync,
            primaryCoverDecodeDipSize: null,
            secondaryCoverDecodeDipSize: CoverDipSize.DeckCover)
    {
        SourceCard = card;
        TagId = card.TagId;
        OriginalName = card.Name;
        DisplayName = card.DisplayName;
        Category = card.Category;
        CategoryLabel = ExploreVisualFormatting.FormatCategoryLabel(card.Category);
        WorkCount = card.WorkCount;
        UnreadWorks = card.UnreadWorks;
    }

    public ExploreThemeCard SourceCard { get; }

    public long TagId { get; }

    public string OriginalName { get; }

    public string DisplayName { get; }

    public bool HasDifferentOriginalName => !string.Equals(OriginalName, DisplayName, StringComparison.OrdinalIgnoreCase);

    public string Category { get; }

    public string CategoryLabel { get; }

    public int WorkCount { get; }

    public int UnreadWorks { get; }

    public bool HasUnread => UnreadWorks > 0;

    public string TotalSummaryText => UnreadWorks > 0
        ? $"{WorkCount} 本 · {UnreadWorks} 未讀"
        : $"{WorkCount} 本";

    public override string CountBadgeText => TotalSummaryText;
}

public sealed class ExploreComboCardVm : ExploreCardVmBase
{
    /// <param name="isRandomComboPreview">
    /// true = 探索牆頂部「隨機雙題材碰撞」預覽（3 張都是 62x88 小圖，沒有單張精選模式）；
    /// false = 組合卡片（第 1 張原尺寸、第 2、3 張疊牌大小）。
    /// </param>
    public ExploreComboCardVm(
        ExploreComboCard card,
        Action<ExploreCardVmBase> onSelectCard,
        Func<Action, Task>? runOnUiAsync = null,
        bool isRandomComboPreview = false)
        : base(
            card.RepresentativeWorks,
            card.FirstTag.DisplayName,
            ExploreVisualFormatting.PickAccentHex($"{card.FirstTag.Name}:{card.SecondTag.Name}"),
            onSelectCard,
            runOnUiAsync,
            primaryCoverDecodeDipSize: isRandomComboPreview ? CoverDipSize.RandomComboPreview : null,
            secondaryCoverDecodeDipSize: isRandomComboPreview ? CoverDipSize.RandomComboPreview : CoverDipSize.DeckCover)
    {
        SourceCard = card;
        FirstTag = card.FirstTag;
        SecondTag = card.SecondTag;
        PairTitle = $"{card.FirstTag.DisplayName} × {card.SecondTag.DisplayName}";
        WorkCount = card.WorkCount;
        UnreadWorks = card.UnreadWorks;
    }

    public ExploreComboCard SourceCard { get; }

    public ExploreTagBadge FirstTag { get; }

    public ExploreTagBadge SecondTag { get; }

    public string FirstTagDisplayName => FirstTag.DisplayName;

    public string SecondTagDisplayName => SecondTag.DisplayName;

    public string PairTitle { get; }

    public int WorkCount { get; }

    public int UnreadWorks { get; }

    public bool HasUnread => UnreadWorks > 0;

    public string TotalSummaryText => UnreadWorks > 0
        ? $"{WorkCount} 本 · {UnreadWorks} 未讀"
        : $"{WorkCount} 本";

    public override string CountBadgeText => TotalSummaryText;
}

public sealed partial class ExploreWorkCardVm : ExploreCardVmBase
{
    private readonly Action<ExploreWorkItem> _onRevealInExplorer;
    private readonly Action<ExploreWorkItem>? _onOpenExternal;

    public ExploreWorkCardVm(
        ExploreWorkItem work,
        Action<ExploreWorkItem> onOpenWork,
        Action<ExploreWorkItem> onRevealInExplorer,
        Func<Action, Task>? runOnUiAsync = null,
        Action<ExploreWorkItem>? onOpenExternal = null)
        : base(
            new[] { work ?? throw new ArgumentNullException(nameof(work)) },
            work.Title,
            ExploreVisualFormatting.PickAccentHex(work.Title),
            _ => (onOpenWork ?? throw new ArgumentNullException(nameof(onOpenWork)))(work),
            runOnUiAsync)
    {
        SourceWork = work;
        _onRevealInExplorer = onRevealInExplorer ?? throw new ArgumentNullException(nameof(onRevealInExplorer));
        _onOpenExternal = onOpenExternal;

        var filteredTags = work.Tags
            .Where(t => !string.Equals(t.Category, "artist", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(t.Category, "group", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var displayPool = filteredTags.Count > 0 ? filteredTags : work.Tags;
        TopDisplayTags = displayPool.Take(3).ToList();
        OverflowTagCount = Math.Max(0, displayPool.Count - TopDisplayTags.Count);
    }

    public ExploreWorkItem SourceWork { get; }

    public long AssetId => SourceWork.AssetId;

    public string Path => SourceWork.Path;

    public string Title => SourceWork.Title;

    public string AuthorName => SourceWork.AuthorName;

    public string? Language => SourceWork.Language;

    public string? TranslationGroup => SourceWork.TranslationGroup;

    public int? PageCount => SourceWork.PageCount;

    public ReadStatus ReadStatus => SourceWork.ReadStatus;

    public int ReadCount => SourceWork.ReadCount;

    public bool IsUnread => ReadStatus == ReadStatus.Unread;

    public bool IsReading => ReadStatus == ReadStatus.Reading;

    public bool IsCompleted => ReadStatus == ReadStatus.Completed;

    public string ReadStatusLabel => ReadStatus switch
    {
        ReadStatus.Reading => "閱讀中",
        ReadStatus.Completed => "已完成",
        ReadStatus.Dropped => "已放棄",
        _ => "未讀",
    };

    public string PageCountText => PageCount.HasValue && PageCount.Value > 0 ? $"{PageCount.Value}P" : string.Empty;

    public bool HasPageCount => !string.IsNullOrEmpty(PageCountText);

    public string MetadataSummaryText
    {
        get
        {
            var parts = new List<string>(4) { AuthorName };
            if (HasPageCount) parts.Add(PageCountText);
            if (!string.IsNullOrWhiteSpace(Language)) parts.Add(Language!);
            if (!string.IsNullOrWhiteSpace(TranslationGroup)) parts.Add(TranslationGroup!);
            return string.Join(" · ", parts);
        }
    }

    public IReadOnlyList<ExploreTagBadge> TopDisplayTags { get; }

    public int OverflowTagCount { get; }

    public bool HasOverflowTags => OverflowTagCount > 0;

    public string OverflowTagText => $"+{OverflowTagCount}";

    public override string CountBadgeText => HasPageCount ? $"{PageCountText} · {ReadStatusLabel}" : ReadStatusLabel;

    [RelayCommand]
    private void RevealInExplorer() => _onRevealInExplorer(SourceWork);

    [RelayCommand]
    private void OpenExternal() => _onOpenExternal?.Invoke(SourceWork);
}

/// <summary>
/// 封面顯示框大小（DIP），用於依顯示大小縮小解碼。數值需與 XAML 版面一致，改版面時一併調整。
/// 疊牌卡片的第 1 張刻意維持原尺寸：探索牆可隨時切到「單張精選」，該模式只顯示第 1 張並填滿整張卡片（約 212x186 DIP，
/// 懸停再放大 1.035 倍），高 DPI 下需要完整縮圖解析度；第 2、3 張只在疊牌模式出現，才用 <see cref="DeckCover"/>。
/// </summary>
public readonly record struct CoverDipSize(double Width, double Height)
{
    /// <summary>
    /// 疊牌單張封面：DeckCoverControl 的 deck-slot 為 120x168，懸停時最前張放大 1.04 倍（role-front hovered）。
    /// </summary>
    public static CoverDipSize DeckCover { get; } = new(120 * 1.04, 168 * 1.04);

    /// <summary>探索牆頂部「隨機雙題材碰撞」預覽小圖（ExplorePageView 的 62x88 Border）。</summary>
    public static CoverDipSize RandomComboPreview { get; } = new(62, 88);
}

internal static class ExploreVisualFormatting
{
    private static readonly string[] AccentPalette =
    [
        "#3B82F6", // Blue
        "#8B5CF6", // Violet
        "#EC4899", // Pink
        "#10B981", // Emerald
        "#F59E0B", // Amber
        "#06B6D4", // Cyan
        "#F43F5E", // Rose
        "#6366F1", // Indigo
    ];

    public static string PickAccentHex(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return AccentPalette[0];
        }

        unchecked
        {
            uint hash = 2166136261u;
            foreach (char ch in seed)
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return AccentPalette[(int)(hash % (uint)AccentPalette.Length)];
        }
    }

    public static string ExtractFallbackGlyph(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "📖";
        }

        var trimmed = text.Trim();
        return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : "📖";
    }

    public static string FormatCategoryLabel(string? category)
    {
        return (category ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "virtual" => "虛擬主題",
            "parody" => "原作",
            "character" => "角色",
            "artist" => "作者",
            "group" => "社團",
            "language" => "語系",
            _ => "內容主題",
        };
    }
}

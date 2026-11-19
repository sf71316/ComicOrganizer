using Avalonia.Media.Imaging;
using ComicOrganizer.App.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;
using NotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace ComicOrganizer.Avalonia.ViewModels;

public enum GalleryDetailKind
{
    None = 0,
    Work = 1,
    Author = 2,
    Folder = 3,
}

public sealed class GalleryDetailTagVm
{
    public GalleryDetailTagVm(
        long tagId,
        string name,
        string displayName,
        string category,
        bool isRemovable,
        Action<GalleryDetailTagVm>? onRemove,
        Action<GalleryDetailTagVm>? onFilter)
    {
        TagId = tagId;
        Name = name;
        DisplayName = displayName;
        Category = category;
        IsRemovable = isRemovable;
        LabelText = !string.IsNullOrEmpty(displayName) && !string.Equals(displayName, name, StringComparison.OrdinalIgnoreCase)
            ? $"{displayName} ({name})"
            : name;
        RemoveCommand = new RelayCommand(() => onRemove?.Invoke(this));
        FilterCommand = new RelayCommand(() => onFilter?.Invoke(this));
    }

    public long TagId { get; }

    public string Name { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public bool IsRemovable { get; }

    public string LabelText { get; }

    public IRelayCommand RemoveCommand { get; }

    public IRelayCommand FilterCommand { get; }
}

public sealed partial class GalleryWorkLibraryToggleVm : ObservableObject
{
    private readonly Action<GalleryWorkLibraryToggleVm> _onToggle;

    public GalleryWorkLibraryToggleVm(
        long libraryId,
        string name,
        bool isMember,
        Action<GalleryWorkLibraryToggleVm> onToggle)
    {
        LibraryId = libraryId;
        Name = name;
        _isMember = isMember;
        _onToggle = onToggle ?? throw new ArgumentNullException(nameof(onToggle));
    }

    public long LibraryId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isMember;

    [RelayCommand]
    private void Toggle() => _onToggle(this);
}

/// <summary>
/// Phase 2 W2.2 畫廊右側詳情與編輯抽屜 ViewModel：
/// 支援作品 Metadata 檢視與編輯、作者聚合資訊、一般資料夾偵測，以及共用資料庫唯讀保護（策略 1.A）。
/// </summary>
public sealed partial class GalleryDetailDrawerViewModel : ViewModelBase
{
    private readonly IAssetMetaStore _store;
    private readonly bool _isReadOnlyDatabase;
    private readonly Func<string> _getRootDirectory;
    private readonly Action<string> _onSearchDslRequested;
    private readonly Action<string> _onNavigateFolderRequested;
    private readonly Action<string> _onOpenPathRequested;
    private readonly Action<string> _onRevealPathRequested;
    private readonly Action _onDataMutated;
    private readonly ISukiToastManager? _toastManager;

    private GalleryItemVm? _currentItem;
    private long? _currentAssetId;
    private long? _currentAuthorId;
    private long? _loadedModifiedTicks;
    private long _loadedFileSize;
    private int? _loadedRating;
    private int _loadedReadCount;
    private long? _loadedLastRead;
    private int? _loadedLastPage;
    private string? _loadedExternalId;
    private long _loadedParsedAt;

    public GalleryDetailDrawerViewModel(
        IAssetMetaStore store,
        bool isReadOnlyDatabase,
        Func<string> getRootDirectory,
        Action<string> onSearchDslRequested,
        Action<string> onNavigateFolderRequested,
        Action<string> onOpenPathRequested,
        Action<string> onRevealPathRequested,
        Action onDataMutated,
        ISukiToastManager? toastManager = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _isReadOnlyDatabase = isReadOnlyDatabase;
        _getRootDirectory = getRootDirectory ?? throw new ArgumentNullException(nameof(getRootDirectory));
        _onSearchDslRequested = onSearchDslRequested ?? throw new ArgumentNullException(nameof(onSearchDslRequested));
        _onNavigateFolderRequested = onNavigateFolderRequested ?? throw new ArgumentNullException(nameof(onNavigateFolderRequested));
        _onOpenPathRequested = onOpenPathRequested ?? throw new ArgumentNullException(nameof(onOpenPathRequested));
        _onRevealPathRequested = onRevealPathRequested ?? throw new ArgumentNullException(nameof(onRevealPathRequested));
        _onDataMutated = onDataMutated ?? throw new ArgumentNullException(nameof(onDataMutated));
        _toastManager = toastManager;
    }

    public BatchObservableCollection<GalleryDetailTagVm> WorkTags { get; } = [];

    public BatchObservableCollection<GalleryDetailTagVm> AuthorTags { get; } = [];

    public BatchObservableCollection<string> AuthorTopTags { get; } = [];

    public BatchObservableCollection<GalleryWorkLibraryToggleVm> VirtualLibraryMemberships { get; } = [];

    public bool IsReadOnlyDatabase => _isReadOnlyDatabase;

    public bool IsWritable => !_isReadOnlyDatabase;

    public string ReadOnlyBannerText => "🔒 共用資料庫唯讀保護中（不會修改 D:\\ComicOrganizer\\cache.db）";

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkKind))]
    [NotifyPropertyChangedFor(nameof(IsAuthorKind))]
    [NotifyPropertyChangedFor(nameof(IsFolderKind))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    [NotifyPropertyChangedFor(nameof(OpenTargetButtonText))]
    [NotifyPropertyChangedFor(nameof(OpenTargetTooltip))]
    private GalleryDetailKind _detailKind = GalleryDetailKind.None;

    [ObservableProperty]
    private string _headerTitle = "作品詳情";

    [ObservableProperty]
    private string _headerSubtitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetPath))]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewCover))]
    private Bitmap? _previewCover;

    [ObservableProperty]
    private string _fallbackGlyph = "📚";

    [ObservableProperty]
    private string _accentColorHex = "#3B82F6";

    // ── 作品編輯欄位 ────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    private string _editAuthorName = string.Empty;

    [ObservableProperty]
    private string _editSeries = string.Empty;

    [ObservableProperty]
    private string _editChapterNo = string.Empty;

    [ObservableProperty]
    private string _editLanguage = string.Empty;

    [ObservableProperty]
    private string _editTranslationGroup = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnreadStatusSelected))]
    [NotifyPropertyChangedFor(nameof(IsReadingStatusSelected))]
    [NotifyPropertyChangedFor(nameof(IsCompletedStatusSelected))]
    [NotifyPropertyChangedFor(nameof(IsDroppedStatusSelected))]
    private ReadStatus _currentReadStatus = ReadStatus.Unread;

    [ObservableProperty]
    private string _workStatsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExternalId))]
    private string _externalIdText = string.Empty;

    [ObservableProperty]
    private bool _isManuallyEdited;

    [ObservableProperty]
    private string _newTagInput = string.Empty;

    // ── 作者檢視欄位 ────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _authorMainName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthorAliases))]
    private string _authorAliasesText = string.Empty;

    [ObservableProperty]
    private string _authorStatsText = string.Empty;

    [ObservableProperty]
    private string _newAuthorTagInput = string.Empty;

    // ── 一般資料夾檢視欄位 ──────────────────────────────────────────────────
    [ObservableProperty]
    private string _folderRoleLabel = string.Empty;

    [ObservableProperty]
    private string _folderSummaryText = string.Empty;

    // ── 唯讀或儲存狀態提示 ──────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusFeedback))]
    private string _statusFeedbackText = string.Empty;

    public bool HasTarget => DetailKind != GalleryDetailKind.None;

    public bool IsWorkKind => DetailKind == GalleryDetailKind.Work;

    public bool IsAuthorKind => DetailKind == GalleryDetailKind.Author;

    public bool IsFolderKind => DetailKind == GalleryDetailKind.Folder;

    public bool HasTargetPath => !string.IsNullOrWhiteSpace(TargetPath);

    public string OpenTargetButtonText => IsWorkKind ? "📖 開啟閱讀" : "📁 進入此資料夾";

    public string OpenTargetTooltip => IsWorkKind
        ? "以系統預設程式開啟閱讀此作品"
        : "只在左側／目前畫廊中進入瀏覽這個實體資料夾內的檔案";

    public bool HasPreviewCover => PreviewCover != null;

    public bool HasExternalId => !string.IsNullOrWhiteSpace(ExternalIdText);

    public bool HasAuthorAliases => !string.IsNullOrWhiteSpace(AuthorAliasesText);

    public bool HasWorkTags => WorkTags.Count > 0;

    public bool HasAuthorTags => AuthorTags.Count > 0;

    public bool HasAuthorTopTags => AuthorTopTags.Count > 0;

    public bool HasVirtualLibraries => VirtualLibraryMemberships.Count > 0;

    public bool HasStatusFeedback => !string.IsNullOrWhiteSpace(StatusFeedbackText);

    public bool IsUnreadStatusSelected => CurrentReadStatus == ReadStatus.Unread;

    public bool IsReadingStatusSelected => CurrentReadStatus == ReadStatus.Reading;

    public bool IsCompletedStatusSelected => CurrentReadStatus == ReadStatus.Completed;

    public bool IsDroppedStatusSelected => CurrentReadStatus == ReadStatus.Dropped;

    public void InspectItem(GalleryItemVm item, bool openDrawer = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        _currentItem = item;
        TargetPath = item.Path;
        PreviewCover = item.Cover1;
        FallbackGlyph = item.FallbackGlyph;
        AccentColorHex = item.AccentColorHex;
        StatusFeedbackText = string.Empty;
        _currentAssetId = null;
        _currentAuthorId = null;

        if (!item.IsFolder)
        {
            LoadWorkDetail(item);
        }
        else
        {
            LoadFolderOrAuthorDetail(item);
        }

        if (openDrawer)
        {
            IsOpen = true;
        }
    }

    public void InspectAuthorNode(long authorId, string mainName, bool openDrawer = true)
    {
        _currentItem = null;
        _currentAssetId = null;
        _currentAuthorId = authorId;
        PreviewCover = null;
        FallbackGlyph = "👤";
        AccentColorHex = ExploreVisualFormatting.PickAccentHex(mainName);
        StatusFeedbackText = string.Empty;

        var info = _store.GetAuthorInfo(authorId);
        if (info == null)
        {
            DetailKind = GalleryDetailKind.Folder;
            HeaderTitle = mainName;
            HeaderSubtitle = "找不到作者詳細紀錄";
            TargetPath = string.Empty;
            FolderRoleLabel = "作者虛擬庫";
            FolderSummaryText = "目前資料庫尚無此作者的詳細統計。";
        }
        else
        {
            PopulateAuthorInfo(info);
        }

        if (openDrawer)
        {
            IsOpen = true;
        }
    }

    public void SyncCoverFromCurrentItem()
    {
        if (_currentItem != null)
        {
            PreviewCover = _currentItem.Cover1;
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void OpenTarget()
    {
        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            return;
        }

        if (IsWorkKind)
        {
            _onOpenPathRequested(TargetPath);
        }
        else
        {
            _onNavigateFolderRequested(TargetPath);
        }
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        if (!string.IsNullOrWhiteSpace(TargetPath))
        {
            _onRevealPathRequested(TargetPath);
        }
    }

    [RelayCommand]
    private void SearchByCurrentAuthor()
    {
        var name = IsAuthorKind ? AuthorMainName : EditAuthorName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var token = SearchQueryParser.BuildFieldToken("artist", name);
        if (!string.IsNullOrEmpty(token))
        {
            _onSearchDslRequested(token);
        }
    }

    [RelayCommand]
    private void SetReadStatus(ReadStatus status)
    {
        if (!IsWorkKind || string.IsNullOrWhiteSpace(TargetPath))
        {
            return;
        }

        if (!EnsureWritable("變更閱讀狀態"))
        {
            return;
        }

        EnsureAssetExistsInStore();
        _store.SetReadStatus(TargetPath, status);
        CurrentReadStatus = status;
        StatusFeedbackText = $"✓ 已將閱讀狀態更新為「{FormatReadStatus(status)}」";
        ShowToast("狀態已更新", StatusFeedbackText, NotificationType.Success);
        if (_currentItem != null)
        {
            LoadWorkDetail(_currentItem);
        }
        _onDataMutated();
    }

    [RelayCommand]
    private void SetUnreadStatus() => SetReadStatus(ReadStatus.Unread);

    [RelayCommand]
    private void SetReadingStatus() => SetReadStatus(ReadStatus.Reading);

    [RelayCommand]
    private void SetCompletedStatus() => SetReadStatus(ReadStatus.Completed);

    [RelayCommand]
    private void SetDroppedStatus() => SetReadStatus(ReadStatus.Dropped);

    [RelayCommand]
    private void SaveWorkMetadata()
    {
        if (!IsWorkKind || string.IsNullOrWhiteSpace(TargetPath))
        {
            return;
        }

        if (!EnsureWritable("儲存作品資訊"))
        {
            return;
        }

        var assetId = EnsureAssetExistsInStore();
        if (!assetId.HasValue)
        {
            return;
        }

        long? authorId = null;
        var trimmedAuthor = EditAuthorName?.Trim();
        if (!string.IsNullOrEmpty(trimmedAuthor))
        {
            authorId = _store.UpsertAuthor(trimmedAuthor);
        }

        var existingMeta = _store.GetMetadata(assetId.Value);
        var updated = new AssetMetadata
        {
            AssetId = assetId.Value,
            AuthorId = authorId,
            Title = NullIfEmpty(EditTitle),
            Series = NullIfEmpty(EditSeries),
            ChapterNo = NullIfEmpty(EditChapterNo),
            Language = NullIfEmpty(EditLanguage),
            TranslationGroup = NullIfEmpty(EditTranslationGroup),
            PageCount = existingMeta?.PageCount,
            Rating = existingMeta?.Rating ?? _loadedRating,
            ReadStatus = CurrentReadStatus,
            ReadCount = existingMeta?.ReadCount ?? _loadedReadCount,
            LastRead = existingMeta?.LastRead ?? _loadedLastRead,
            ExternalId = existingMeta?.ExternalId ?? _loadedExternalId,
            ParsedAt = existingMeta?.ParsedAt ?? _loadedParsedAt,
            LastPage = existingMeta?.LastPage ?? _loadedLastPage,
            ManuallyEdited = true,
        };

        _store.UpsertMetadata(updated);
        _store.SetMetadataManuallyEdited(assetId.Value, true);
        IsManuallyEdited = true;
        StatusFeedbackText = "✓ 已儲存作品 Metadata（已標記手動編輯保護）";
        ShowToast("儲存成功", "作品 Metadata 已更新並標記為手動保護。", NotificationType.Success);

        if (_currentItem != null)
        {
            LoadWorkDetail(_currentItem);
        }
        _onDataMutated();
    }

    [RelayCommand]
    private void AddWorkTag()
    {
        if (!IsWorkKind || string.IsNullOrWhiteSpace(NewTagInput))
        {
            return;
        }

        if (!EnsureWritable("新增作品標籤"))
        {
            return;
        }

        var assetId = EnsureAssetExistsInStore();
        if (!assetId.HasValue)
        {
            return;
        }

        var cleanName = NewTagInput.Trim();
        var tagId = _store.UpsertTag(cleanName);
        _store.AssociateTag(assetId.Value, tagId);
        _store.SetMetadataManuallyEdited(assetId.Value, true);
        NewTagInput = string.Empty;
        StatusFeedbackText = $"✓ 已新增標籤「{cleanName}」";

        if (_currentItem != null)
        {
            LoadWorkDetail(_currentItem);
        }
        _onDataMutated();
    }

    [RelayCommand]
    private void AddAuthorTag()
    {
        if (!IsAuthorKind || !_currentAuthorId.HasValue || string.IsNullOrWhiteSpace(NewAuthorTagInput))
        {
            return;
        }

        if (!EnsureWritable("新增作者標籤"))
        {
            return;
        }

        var cleanName = NewAuthorTagInput.Trim();
        var tagId = _store.UpsertTag(cleanName);
        _store.AssociateAuthorTag(_currentAuthorId.Value, tagId);
        NewAuthorTagInput = string.Empty;
        StatusFeedbackText = $"✓ 已為作者新增標籤「{cleanName}」";

        var info = _store.GetAuthorInfo(_currentAuthorId.Value);
        if (info != null)
        {
            PopulateAuthorInfo(info);
        }
        _onDataMutated();
    }

    private void LoadWorkDetail(GalleryItemVm item)
    {
        DetailKind = GalleryDetailKind.Work;
        var detail = _store.GetWorkDetail(item.Path);
        _currentAssetId = detail?.AssetId ?? _store.GetAssetId(item.Path);
        _currentAuthorId = detail?.AuthorId;

        HeaderTitle = !string.IsNullOrWhiteSpace(detail?.Title) ? detail!.Title! : item.DisplayTitle;
        HeaderSubtitle = item.Name;

        _loadedModifiedTicks = item.ModifiedTicks;
        _loadedFileSize = detail?.FileSize ?? item.SizeBytes;
        _loadedRating = detail?.Rating;
        _loadedReadCount = detail?.ReadCount ?? item.ReadCount;
        _loadedLastRead = detail?.LastRead ?? item.LastRead;
        _loadedLastPage = detail?.LastPage;
        _loadedExternalId = detail?.ExternalId;
        _loadedParsedAt = detail?.ParsedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        EditTitle = detail?.Title ?? item.DisplayTitle;
        EditAuthorName = detail?.AuthorMainName ?? item.AuthorName;
        EditSeries = detail?.Series ?? string.Empty;
        EditChapterNo = detail?.ChapterNo ?? string.Empty;
        EditLanguage = detail?.Language ?? item.Language ?? string.Empty;
        EditTranslationGroup = detail?.TranslationGroup ?? string.Empty;
        CurrentReadStatus = detail?.ReadStatus ?? item.ReadStatus;
        IsManuallyEdited = detail?.ManuallyEdited ?? false;
        ExternalIdText = detail?.ExternalId ?? string.Empty;

        var parts = new List<string>();
        int? pages = detail?.PageCount ?? item.PageCount;
        if (pages is > 0)
        {
            parts.Add($"{pages} 頁");
        }
        if (!string.IsNullOrWhiteSpace(item.SizeDisplay))
        {
            parts.Add(item.SizeDisplay);
        }
        parts.Add($"閱讀 {_loadedReadCount:N0} 次");
        if (_loadedLastPage is >= 0)
        {
            parts.Add($"上次停在第 {_loadedLastPage + 1} 頁");
        }
        WorkStatsText = string.Join(" · ", parts);

        var tags = detail?.Tags ?? [];
        var transMap = tags.Count > 0
            ? _store.GetTagTranslations(tags.Select(t => t.Id).ToList(), "zh-TW")
            : new Dictionary<long, string>();

        var tagVms = tags.Select(t =>
        {
            transMap.TryGetValue(t.Id, out var zh);
            return new GalleryDetailTagVm(
                t.Id,
                t.Name,
                !string.IsNullOrEmpty(zh) ? zh : t.Name,
                t.Category ?? string.Empty,
                isRemovable: true,
                onRemove: OnRemoveWorkTag,
                onFilter: tag =>
                {
                    var dsl = SearchQueryParser.BuildFieldToken("tag", tag.DisplayName);
                    if (!string.IsNullOrEmpty(dsl))
                    {
                        _onSearchDslRequested(dsl);
                    }
                });
        }).ToList();

        WorkTags.ReplaceAll(tagVms);
        AuthorTags.Clear();
        AuthorTopTags.Clear();

        var libs = _store.ListVirtualLibraries();
        var memberIds = _store.ListVirtualLibraryIdsForWork(item.Path);
        var libVms = libs.Select(l => new GalleryWorkLibraryToggleVm(
            l.Id,
            l.Name,
            memberIds.Contains(l.Id),
            OnToggleVirtualLibraryMembership)).ToList();
        VirtualLibraryMemberships.ReplaceAll(libVms);

        NotifyDetailMetricsChanged();
    }

    private void LoadFolderOrAuthorDetail(GalleryItemVm item)
    {
        var authorId = _store.ResolveAuthorIdByPath(item.Path);
        if (authorId.HasValue)
        {
            var info = _store.GetAuthorInfo(authorId.Value);
            if (info != null)
            {
                _currentAuthorId = authorId.Value;
                PopulateAuthorInfo(info, item.Path);
                return;
            }
        }

        // Fallback 用 FolderRoleDetector 辨識資料夾角色（分類目錄 / 作品目錄 / 一般目錄）
        var detector = new FolderRoleDetector(_getRootDirectory());
        var role = detector.Detect(item.Path);
        if (role == FolderRole.Work)
        {
            LoadWorkDetail(item);
            return;
        }

        DetailKind = GalleryDetailKind.Folder;
        HeaderTitle = item.DisplayTitle;
        HeaderSubtitle = item.Path;
        FolderRoleLabel = role switch
        {
            FolderRole.Author => "👤 作者資料夾（尚未索引 Author 紀錄）",
            FolderRole.Category => "🗂️ 分類／系列目錄",
            FolderRole.Library => "🏠 漫畫庫根目錄",
            _ => "📁 一般資料夾",
        };

        int subDirs = 0;
        int subFiles = 0;
        if (Directory.Exists(item.Path))
        {
            try
            {
                subDirs = Directory.EnumerateDirectories(item.Path).Count();
                subFiles = Directory.EnumerateFiles(item.Path).Count();
            }
            catch
            {
            }
        }
        FolderSummaryText = $"包含 {subDirs:N0} 個子資料夾 · {subFiles:N0} 個檔案（點擊「進入資料夾」可瀏覽內容）";

        WorkTags.Clear();
        AuthorTags.Clear();
        AuthorTopTags.Clear();
        VirtualLibraryMemberships.Clear();
        NotifyDetailMetricsChanged();
    }

    private void PopulateAuthorInfo(AuthorInfo info, string? fallbackFolderPath = null)
    {
        DetailKind = GalleryDetailKind.Author;
        _currentAuthorId = info.Author.Id;
        TargetPath = !string.IsNullOrWhiteSpace(info.Author.FolderPath)
            ? info.Author.FolderPath!
            : (fallbackFolderPath ?? string.Empty);

        HeaderTitle = info.Author.MainName;
        HeaderSubtitle = string.IsNullOrWhiteSpace(TargetPath) ? "資料庫作者節點" : TargetPath;
        AuthorMainName = info.Author.MainName;
        AuthorAliasesText = info.Aliases.Count > 0
            ? string.Join(" / ", info.Aliases.Select(a => a.Name))
            : string.Empty;
        AuthorStatsText = $"共 {info.TotalWorks:N0} 部作品 · 未讀 {info.UnreadCount:N0} · 閱讀中 {info.ReadingCount:N0} · 已完成 {info.CompletedCount:N0}（累計閱讀 {info.TotalReadCount:N0} 次）";

        var topTagNames = info.TopTags
            .Take(10)
            .Select(t => $"{t.Name} ({t.Count})")
            .ToList();
        AuthorTopTags.ReplaceAll(topTagNames);

        var transMap = info.AuthorTags.Count > 0
            ? _store.GetTagTranslations(info.AuthorTags.Select(t => t.Id).ToList(), "zh-TW")
            : new Dictionary<long, string>();

        var authorTagVms = info.AuthorTags.Select(t =>
        {
            transMap.TryGetValue(t.Id, out var zh);
            return new GalleryDetailTagVm(
                t.Id,
                t.Name,
                !string.IsNullOrEmpty(zh) ? zh : t.Name,
                t.Category ?? string.Empty,
                isRemovable: true,
                onRemove: OnRemoveAuthorTag,
                onFilter: tag =>
                {
                    var dsl = SearchQueryParser.BuildFieldToken("tag", tag.DisplayName);
                    if (!string.IsNullOrEmpty(dsl))
                    {
                        _onSearchDslRequested(dsl);
                    }
                });
        }).ToList();
        AuthorTags.ReplaceAll(authorTagVms);

        WorkTags.Clear();
        VirtualLibraryMemberships.Clear();
        NotifyDetailMetricsChanged();
    }

    private void OnRemoveWorkTag(GalleryDetailTagVm tagVm)
    {
        if (!EnsureWritable("移除作品標籤"))
        {
            return;
        }

        if (_currentAssetId.HasValue)
        {
            _store.DisassociateTag(_currentAssetId.Value, tagVm.TagId);
            _store.SetMetadataManuallyEdited(_currentAssetId.Value, true);
            StatusFeedbackText = $"✓ 已移除標籤「{tagVm.LabelText}」";
            if (_currentItem != null)
            {
                LoadWorkDetail(_currentItem);
            }
            _onDataMutated();
        }
    }

    private void OnRemoveAuthorTag(GalleryDetailTagVm tagVm)
    {
        if (!EnsureWritable("移除作者標籤"))
        {
            return;
        }

        if (_currentAuthorId.HasValue)
        {
            _store.DisassociateAuthorTag(_currentAuthorId.Value, tagVm.TagId);
            StatusFeedbackText = $"✓ 已移除作者標籤「{tagVm.LabelText}」";
            var info = _store.GetAuthorInfo(_currentAuthorId.Value);
            if (info != null)
            {
                PopulateAuthorInfo(info);
            }
            _onDataMutated();
        }
    }

    private void OnToggleVirtualLibraryMembership(GalleryWorkLibraryToggleVm toggleVm)
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !IsWorkKind)
        {
            return;
        }

        if (!EnsureWritable("調整閱讀清單歸屬"))
        {
            return;
        }

        if (toggleVm.IsMember)
        {
            _store.RemoveWorkFromVirtualLibrary(toggleVm.LibraryId, TargetPath);
            toggleVm.IsMember = false;
            StatusFeedbackText = $"已從「{toggleVm.Name}」移除此作品";
        }
        else
        {
            _store.AddWorkToVirtualLibrary(toggleVm.LibraryId, TargetPath);
            toggleVm.IsMember = true;
            StatusFeedbackText = $"✓ 已加入「{toggleVm.Name}」";
        }

        _onDataMutated();
    }

    private long? EnsureAssetExistsInStore()
    {
        if (_currentAssetId.HasValue)
        {
            return _currentAssetId.Value;
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            return null;
        }

        var existingId = _store.GetAssetId(TargetPath);
        if (existingId.HasValue)
        {
            _currentAssetId = existingId.Value;
            return existingId;
        }

        var name = Path.GetFileName(TargetPath);
        _store.UpsertCatalogOnly(new AssetMeta
        {
            Path = TargetPath,
            Name = string.IsNullOrEmpty(name) ? TargetPath : name,
            IsFolder = Directory.Exists(TargetPath),
            FileSize = _loadedFileSize,
            Mtime = (_loadedModifiedTicks ?? 0) / 1000L,
            LastScanned = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });

        _currentAssetId = _store.GetAssetId(TargetPath);
        return _currentAssetId;
    }

    public bool EnsureWritable(string actionName)
    {
        if (!_isReadOnlyDatabase)
        {
            return true;
        }

        StatusFeedbackText = $"🔒 共用資料庫唯讀保護中，已略過「{actionName}」（不修改 cache.db）。";
        ShowToast(
            "共用資料庫唯讀保護",
            $"目前以唯讀模式瀏覽共用開發庫，「{actionName}」不會寫入 D:\\ComicOrganizer\\cache.db。",
            NotificationType.Warning);
        return false;
    }

    private void NotifyDetailMetricsChanged()
    {
        OnPropertyChanged(nameof(HasWorkTags));
        OnPropertyChanged(nameof(HasAuthorTags));
        OnPropertyChanged(nameof(HasAuthorTopTags));
        OnPropertyChanged(nameof(HasVirtualLibraries));
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

    private static string? NullIfEmpty(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string FormatReadStatus(ReadStatus status) => status switch
    {
        ReadStatus.Reading => "閱讀中",
        ReadStatus.Completed => "已完成",
        ReadStatus.Dropped => "放棄",
        _ => "未讀",
    };
}

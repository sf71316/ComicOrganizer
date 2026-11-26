using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls.Notifications;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed partial class TagManagerItemVm : ObservableObject
{
    public TagManagerItemVm(long id, string name, string? category, string? zhTwName, int assetCount, int authorCount, bool isVirtual)
    {
        Id = id;
        Name = name;
        Category = category ?? string.Empty;
        TranslatedName = zhTwName ?? string.Empty;
        AssetCount = assetCount;
        AuthorCount = authorCount;
        IsVirtual = isVirtual;
    }

    public long Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string TranslatedName { get; }
    public int AssetCount { get; }
    public int AuthorCount { get; }
    public bool IsVirtual { get; }

    public int TotalCount => AssetCount + AuthorCount;
    public bool HasTranslation => !string.IsNullOrWhiteSpace(TranslatedName);
    public string DisplayTitle => HasTranslation ? $"{TranslatedName} ({Name})" : Name;
    public string CategoryBadge => string.IsNullOrWhiteSpace(Category) ? "未分類" : Category;
    public string CountText => $"{TotalCount:N0} 關聯";

    [ObservableProperty]
    private bool _isChecked;
}

public sealed class TagAliasItemVm
{
    private readonly Action<TagAliasItemVm> _onDelete;

    public TagAliasItemVm(string aliasName, long targetTagId, Action<TagAliasItemVm> onDelete)
    {
        AliasName = aliasName;
        TargetTagId = targetTagId;
        _onDelete = onDelete;
    }

    public string AliasName { get; }
    public long TargetTagId { get; }
    public IRelayCommand DeleteCommand => new RelayCommand(() => _onDelete(this));
}

public sealed partial class AuthorManagerItemVm : ObservableObject
{
    public AuthorManagerItemVm(long id, string mainName, string? folderPath, int workCount, int aliasCount)
    {
        Id = id;
        MainName = mainName;
        FolderPath = folderPath ?? string.Empty;
        WorkCount = workCount;
        AliasCount = aliasCount;
    }

    public long Id { get; }
    public string MainName { get; }
    public string FolderPath { get; }
    public int WorkCount { get; }
    public int AliasCount { get; }
    public string StatsText => $"{WorkCount:N0} 本 · {AliasCount:N0} 別名";
    public override string ToString() => $"{MainName} ({WorkCount:N0}本)";
}

public sealed class AuthorAliasItemVm
{
    private readonly Action<AuthorAliasItemVm> _onDelete;

    public AuthorAliasItemVm(long id, string name, string? lang, Action<AuthorAliasItemVm> onDelete)
    {
        Id = id;
        Name = name;
        Lang = string.IsNullOrWhiteSpace(lang) ? "未指定" : lang!;
        _onDelete = onDelete;
    }

    public long Id { get; }
    public string Name { get; }
    public string Lang { get; }
    public IRelayCommand DeleteCommand => new RelayCommand(() => _onDelete(this));
}

/// <summary>
/// Phase 4 W4.2 + W4.3 資料管理主 ViewModel：
/// 1. 標籤、翻譯與虛擬主標籤別名合併 (tag_alias)
/// 2. 作者主名、重複作者合併與多語別名維護
/// 3. nhentai 外部 Metadata 同步與 EhTag 中文翻譯詞庫線上更新
/// 4. 資料庫與快取維護（完整性檢查、目錄對帳、Metadata 重解析、Skia 縮圖增量同步）
/// 全數寫入操作皆受 1.A 共用開發庫唯讀保護。
/// </summary>
public sealed partial class DataManagementPageViewModel : ViewModelBase, IDisposable
{
    private readonly AppDataContext _dataContext;
    private readonly IAssetMetaStore _store;
    private readonly IThumbStore _thumbStore;
    private readonly SkiaCoverService _coverService;
    private readonly ISukiToastManager? _toastManager;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly RebuildProgressTracker _tracker;

    private List<TagManagerItemVm> _allTags = new();
    private List<AuthorManagerItemVm> _allAuthors = new();
    private CacheIntegrityChecker.IntegrityReport? _lastIntegrityReport;
    private ReconcileReport? _lastReconcileReport;
    private LatestReleaseInfo? _latestEhTagRelease;
    private bool _hasLoadedOnce;
    private bool _disposed;

    public DataManagementPageViewModel(
        AppDataContext dataContext,
        SkiaCoverService coverService,
        Func<Action, Task>? runOnUiAsync = null,
        bool autoLoad = true)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _store = dataContext.MetaStore;
        _thumbStore = dataContext.ThumbStore;
        _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
        _toastManager = dataContext.ToastManager;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        // 唯讀資料庫下僅追蹤記憶體內進度，避免 RebuildProgressTracker.Start 寫入 app_state 時觸發唯讀例外
        _tracker = new RebuildProgressTracker(IsReadOnlyDatabase ? null : _store);
        _tracker.StateChanged += OnTrackerStateChanged;

        TagCategoryOptions = ["", "content", "parody", "character", "artist", "group", "language", "virtual"];
        if (autoLoad)
        {
            RefreshAll();
        }
    }

    public Task EnsureLoadedAsync()
    {
        if (_hasLoadedOnce || _disposed) return Task.CompletedTask;
        return _runOnUiAsync(() =>
        {
            if (!_hasLoadedOnce && !_disposed)
            {
                RefreshAll();
            }
        });
    }

    public bool IsReadOnlyDatabase => _dataContext.IsReadOnlyDatabase;
    public bool IsWritable => !IsReadOnlyDatabase;

    public string ReadOnlyBannerText =>
        "🔒 共用開發庫唯讀保護中（瀏覽搜尋、快取完整性掃描、檔案對帳分析與 EhTag 版本檢查皆可使用；修改/合併/寫入快取已鎖定防止異動 cache.db）";

    public IReadOnlyList<string> TagCategoryOptions { get; }

    public ObservableCollection<TagManagerItemVm> VisibleTags { get; } = new();
    public ObservableCollection<TagAliasItemVm> SelectedTagAliases { get; } = new();
    public ObservableCollection<AuthorManagerItemVm> VisibleAuthors { get; } = new();
    public ObservableCollection<AuthorManagerItemVm> MergeTargetAuthors { get; } = new();
    public ObservableCollection<AuthorAliasItemVm> SelectedAuthorAliases { get; } = new();
    public ObservableCollection<string> MaintenanceLogs { get; } = new();

    public event Action<string>? OpenInGalleryRequested;

    [ObservableProperty]
    private int _selectedTabIndex;

    // ── Tab 1: 標籤管理 ───────────────────────────────────────────────────
    [ObservableProperty]
    private string _tagSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedTagCategoryFilter = string.Empty;

    [ObservableProperty]
    private TagManagerItemVm? _selectedTag;

    [ObservableProperty]
    private string _editTagName = string.Empty;

    [ObservableProperty]
    private string _editTagCategory = string.Empty;

    [ObservableProperty]
    private string _editTagTranslation = string.Empty;

    [ObservableProperty]
    private string _newTagAliasText = string.Empty;

    [ObservableProperty]
    private string _virtualMasterName = string.Empty;

    [ObservableProperty]
    private string _virtualMasterZhName = string.Empty;

    [ObservableProperty]
    private string _tagSummaryText = string.Empty;

    // ── Tab 2: 作者管理 ───────────────────────────────────────────────────
    [ObservableProperty]
    private string _authorSearchText = string.Empty;

    [ObservableProperty]
    private AuthorManagerItemVm? _selectedAuthor;

    [ObservableProperty]
    private string _editAuthorMainName = string.Empty;

    [ObservableProperty]
    private string _newAuthorAliasName = string.Empty;

    [ObservableProperty]
    private string _newAuthorAliasLang = "ja";

    [ObservableProperty]
    private AuthorManagerItemVm? _mergeDestinationAuthor;

    [ObservableProperty]
    private string _authorSummaryText = string.Empty;

    // ── Tab 3: 外部資料同步與詞庫 ─────────────────────────────────────────
    [ObservableProperty]
    private string _ehTagStatusText = "尚未檢查線上版本";

    [ObservableProperty]
    private string _ehTagDownloadButtonText = "⬇️ 下載並套用最新翻譯庫";

    [ObservableProperty]
    private bool _hasEhTagUpdateAvailable;

    [ObservableProperty]
    private string _externalSyncSummaryText = "可自動為尚未綁定 ExternalId 的作品比對 nhentai 標籤與語系（跳過手動編輯項目）。";

    [ObservableProperty]
    private int _syncConfidenceThreshold = 80;

    // ── Tab 4: 資料庫與快取維護 ───────────────────────────────────────────
    [ObservableProperty]
    private string _cacheStatsText = string.Empty;

    [ObservableProperty]
    private string _integritySummaryText = "點擊「🔬 檢查快取完整性」進行只讀健檢";

    [ObservableProperty]
    private string _reconcileSummaryText = "點擊「🧭 分析目錄對帳」檢查搬移或已刪除項目";

    [ObservableProperty]
    private bool _canFixIntegrity;

    [ObservableProperty]
    private bool _canApplyReconcile;

    [ObservableProperty]
    private bool _isMaintenanceRunning;

    [ObservableProperty]
    private string _maintenancePhaseText = "待機中";

    [ObservableProperty]
    private double _maintenanceProgressPercent;

    public bool HasSelectedTag => SelectedTag != null;
    public bool HasSelectedAuthor => SelectedAuthor != null;

    partial void OnTagSearchTextChanged(string value) => FilterTags();
    partial void OnSelectedTagCategoryFilterChanged(string value) => FilterTags();
    partial void OnAuthorSearchTextChanged(string value) => FilterAuthors();

    partial void OnSelectedTagChanged(TagManagerItemVm? value)
    {
        OnPropertyChanged(nameof(HasSelectedTag));
        SelectedTagAliases.Clear();
        if (value == null)
        {
            EditTagName = string.Empty;
            EditTagCategory = string.Empty;
            EditTagTranslation = string.Empty;
            return;
        }

        EditTagName = value.Name;
        EditTagCategory = value.Category;
        EditTagTranslation = value.TranslatedName;

        try
        {
            var aliases = _store.ListTagAliases(value.Id);
            foreach (var a in aliases)
            {
                SelectedTagAliases.Add(new TagAliasItemVm(a.AliasName, a.TargetTagId, OnDeleteTagAlias));
            }
        }
        catch { }
    }

    partial void OnSelectedAuthorChanged(AuthorManagerItemVm? value)
    {
        OnPropertyChanged(nameof(HasSelectedAuthor));
        SelectedAuthorAliases.Clear();
        if (value == null)
        {
            EditAuthorMainName = string.Empty;
            return;
        }

        EditAuthorMainName = value.MainName;
        try
        {
            var aliases = _store.ListAuthorAliases(value.Id);
            foreach (var a in aliases)
            {
                SelectedAuthorAliases.Add(new AuthorAliasItemVm(a.Id, a.Name, a.Lang, OnDeleteAuthorAlias));
            }
        }
        catch { }
    }

    [RelayCommand]
    public void RefreshAll()
    {
        _hasLoadedOnce = true;
        LoadTags();
        LoadAuthors();
        RefreshCacheStats();
        RefreshEhTagLocalSummary();
    }

    private void LoadTags()
    {
        var usages = _store.ListTagsByUsage();
        var ids = usages.Select(u => u.Id).ToList();
        var translations = ids.Count > 0 ? _store.GetTagTranslations(ids, "zh-TW") : new Dictionary<long, string>();
        var aliases = _store.ListAllTagAliases();

        _allTags = usages
            .Select(u =>
            {
                translations.TryGetValue(u.Id, out var zh);
                bool isVirtual = string.Equals(u.Category, "virtual", StringComparison.OrdinalIgnoreCase);
                return new TagManagerItemVm(u.Id, u.Name, u.Category, zh, u.AssetCount, u.AuthorCount, isVirtual);
            })
            .ToList();

        TagSummaryText = $"共 {_allTags.Count:N0} 個標籤 · {aliases.Count:N0} 條別名規則";
        FilterTags();
    }

    private void FilterTags()
    {
        long? prevId = SelectedTag?.Id;
        VisibleTags.Clear();
        var kw = string.IsNullOrWhiteSpace(TagSearchText) ? null : TagSearchText.Trim();
        var cat = string.IsNullOrWhiteSpace(SelectedTagCategoryFilter) ? null : SelectedTagCategoryFilter.Trim();

        foreach (var t in _allTags)
        {
            if (cat != null && !string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase))
                continue;
            if (kw != null &&
                !t.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                !t.TranslatedName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                continue;
            VisibleTags.Add(t);
        }

        SelectedTag = prevId.HasValue
            ? VisibleTags.FirstOrDefault(t => t.Id == prevId.Value) ?? VisibleTags.FirstOrDefault()
            : VisibleTags.FirstOrDefault();
    }

    private void LoadAuthors()
    {
        var usages = _store.ListAuthorsByUsage();
        _allAuthors = usages
            .Select(u => new AuthorManagerItemVm(u.Author.Id, u.Author.MainName, u.Author.FolderPath, u.WorkCount, u.AliasCount))
            .ToList();

        MergeTargetAuthors.Clear();
        foreach (var a in _allAuthors)
            MergeTargetAuthors.Add(a);

        var totalAliases = _store.ListAllAuthorAliases().Count;
        AuthorSummaryText = $"共 {_allAuthors.Count:N0} 位作者 · {totalAliases:N0} 筆別名";
        FilterAuthors();
    }

    private void FilterAuthors()
    {
        long? prevId = SelectedAuthor?.Id;
        VisibleAuthors.Clear();
        var kw = string.IsNullOrWhiteSpace(AuthorSearchText) ? null : AuthorSearchText.Trim();

        foreach (var a in _allAuthors)
        {
            if (kw != null && !a.MainName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                continue;
            VisibleAuthors.Add(a);
        }

        SelectedAuthor = prevId.HasValue
            ? VisibleAuthors.FirstOrDefault(a => a.Id == prevId.Value) ?? VisibleAuthors.FirstOrDefault()
            : VisibleAuthors.FirstOrDefault();
    }

    private void RefreshCacheStats()
    {
        try
        {
            long assets = _store.TotalAssetCount();
            long cachedThumbs = _store.TotalCachedCount();
            long bytes = _thumbStore.TotalSizeBytes();
            double mb = bytes / (1024.0 * 1024.0);
            long limitMb = _dataContext.Config.ThumbCacheMaxBytes / (1024L * 1024L);
            CacheStatsText = $"已編目資產 {assets:N0} 筆 · 已快取縮圖 {cachedThumbs:N0} 張 · 縮圖磁碟占用 {mb:F1} MB / {(limitMb > 0 ? $"{limitMb:N0} MB" : "不限制")}";
        }
        catch
        {
            CacheStatsText = "無法讀取快取統計資訊";
        }
    }

    private void RefreshEhTagLocalSummary()
    {
        var tr = _dataContext.TagTranslator;
        var srcLabel = tr.Source == TagTranslationDbSource.DiskCache ? "本地磁碟詞庫" : "內嵌 EhTag 詞庫";
        var shaShort = string.IsNullOrWhiteSpace(tr.HeadSha) ? "(未知)" : tr.HeadSha[..Math.Min(8, tr.HeadSha.Length)];
        EhTagStatusText = $"目前詞庫：{srcLabel} · {tr.Count:N0} 筆詞條 · SHA {shaShort}";
    }

    // ── W4.2 Tag Commands ─────────────────────────────────────────────────

    [RelayCommand]
    public void SaveSelectedTag()
    {
        if (SelectedTag == null || !EnsureWritable("更新標籤資訊")) return;

        var tagId = SelectedTag.Id;
        var trimmedName = EditTagName.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedName) && !string.Equals(trimmedName, SelectedTag.Name, StringComparison.Ordinal))
        {
            _store.RenameTag(tagId, trimmedName);
        }

        _store.SetTagCategory(tagId, string.IsNullOrWhiteSpace(EditTagCategory) ? null : EditTagCategory.Trim());

        var trimmedZh = EditTagTranslation.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedZh))
            _store.UpsertTagTranslation(tagId, "zh-TW", trimmedZh, "manual");
        else
            _store.DeleteTagTranslation(tagId, "zh-TW");

        LoadTags();
        ShowToast("標籤已更新", trimmedName, NotificationType.Success);
    }

    [RelayCommand]
    public void AddTagAlias()
    {
        if (SelectedTag == null || string.IsNullOrWhiteSpace(NewTagAliasText)) return;
        if (!EnsureWritable("新增標籤別名")) return;

        var alias = NewTagAliasText.Trim();
        _store.AddTagAlias(SelectedTag.Id, alias);
        NewTagAliasText = string.Empty;

        SelectedTagAliases.Add(new TagAliasItemVm(alias, SelectedTag.Id, OnDeleteTagAlias));
        LoadTags();
        ShowToast("已新增標籤別名", $"{alias} → {SelectedTag.Name}", NotificationType.Success);
    }

    private void OnDeleteTagAlias(TagAliasItemVm item)
    {
        if (!EnsureWritable("刪除標籤別名")) return;
        _store.DeleteTagAlias(item.AliasName);
        SelectedTagAliases.Remove(item);
        LoadTags();
    }

    [RelayCommand]
    public void MergeCheckedTagsIntoVirtual()
    {
        var checkedIds = _allTags.Where(t => t.IsChecked).Select(t => t.Id).ToList();
        if (checkedIds.Count == 0)
        {
            ShowToast("未勾選標籤", "請先於清單勾選要合併成虛擬主標籤的項目。", NotificationType.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(VirtualMasterName))
        {
            ShowToast("缺少主標籤名稱", "請輸入合併後的虛擬主標籤英文/原始名稱。", NotificationType.Warning);
            return;
        }

        if (!EnsureWritable("合併成虛擬主標籤")) return;

        _dataContext.TryCreateSafetyBackup("merge_tags");
        var masterName = VirtualMasterName.Trim();
        var zh = string.IsNullOrWhiteSpace(VirtualMasterZhName) ? null : VirtualMasterZhName.Trim();
        long masterId = _store.MergeTagsIntoVirtual(checkedIds, masterName, "virtual", zh);

        VirtualMasterName = string.Empty;
        VirtualMasterZhName = string.Empty;
        LoadTags();
        SelectedTag = VisibleTags.FirstOrDefault(t => t.Id == masterId);
        ShowToast("已合併為虛擬主標籤", $"主標籤：{masterName}（合併 {checkedIds.Count} 個來源標籤）", NotificationType.Success);
    }

    [RelayCommand]
    public void DeleteSelectedTag()
    {
        if (SelectedTag == null || !EnsureWritable("刪除標籤")) return;
        _dataContext.TryCreateSafetyBackup("delete_tag");
        string name = SelectedTag.Name;
        _store.DeleteTag(SelectedTag.Id);
        LoadTags();
        ShowToast("已刪除標籤", name, NotificationType.Success);
    }

    [RelayCommand]
    public void CleanupOrphanTags()
    {
        if (!EnsureWritable("清理孤兒標籤")) return;
        int removed = _store.DeleteOrphanTags();
        LoadTags();
        ShowToast("孤兒標籤清理完成", $"共移除 {removed} 個無關聯標籤。", NotificationType.Success);
    }

    [RelayCommand]
    private void OpenSelectedTagInGallery()
    {
        if (SelectedTag == null) return;
        OpenInGalleryRequested?.Invoke(SearchQueryParser.BuildFieldToken("tag", SelectedTag.Name));
    }

    // ── W4.2 Author Commands ──────────────────────────────────────────────

    [RelayCommand]
    public void RenameSelectedAuthor()
    {
        if (SelectedAuthor == null || string.IsNullOrWhiteSpace(EditAuthorMainName)) return;
        if (!EnsureWritable("重命名作者")) return;

        var nextName = EditAuthorMainName.Trim();
        _store.RenameAuthor(SelectedAuthor.Id, nextName);
        LoadAuthors();
        ShowToast("已重命名作者", nextName, NotificationType.Success);
    }

    [RelayCommand]
    public void AddAuthorAlias()
    {
        if (SelectedAuthor == null || string.IsNullOrWhiteSpace(NewAuthorAliasName)) return;
        if (!EnsureWritable("新增作者別名")) return;

        var alias = NewAuthorAliasName.Trim();
        var lang = string.IsNullOrWhiteSpace(NewAuthorAliasLang) ? null : NewAuthorAliasLang.Trim();
        _store.AddAuthorAlias(SelectedAuthor.Id, alias, lang);
        NewAuthorAliasName = string.Empty;

        LoadAuthors();
        OnSelectedAuthorChanged(SelectedAuthor);
        ShowToast("已新增作者別名", $"{alias} → {SelectedAuthor?.MainName}", NotificationType.Success);
    }

    private void OnDeleteAuthorAlias(AuthorAliasItemVm item)
    {
        if (!EnsureWritable("刪除作者別名")) return;
        _store.DeleteAuthorAlias(item.Id);
        SelectedAuthorAliases.Remove(item);
        LoadAuthors();
    }

    [RelayCommand]
    public void MergeSelectedAuthorIntoTarget()
    {
        if (SelectedAuthor == null || MergeDestinationAuthor == null) return;
        if (SelectedAuthor.Id == MergeDestinationAuthor.Id)
        {
            ShowToast("無效操作", "來源作者與目標作者不可相同。", NotificationType.Warning);
            return;
        }
        if (!EnsureWritable("合併重複作者")) return;

        _dataContext.TryCreateSafetyBackup("merge_authors");
        string srcName = SelectedAuthor.MainName;
        string dstName = MergeDestinationAuthor.MainName;
        _store.MergeAuthor(SelectedAuthor.Id, MergeDestinationAuthor.Id);
        LoadAuthors();
        SelectedAuthor = VisibleAuthors.FirstOrDefault(a => string.Equals(a.MainName, dstName, StringComparison.OrdinalIgnoreCase));
        ShowToast("作者合併完成", $"已將「{srcName}」合併至「{dstName}」並保留原別名。", NotificationType.Success);
    }

    [RelayCommand]
    private void OpenSelectedAuthorInGallery()
    {
        if (SelectedAuthor == null) return;
        OpenInGalleryRequested?.Invoke(SearchQueryParser.BuildFieldToken("artist", SelectedAuthor.MainName));
    }

    // ── W4.3 External Sync & EhTag Updater ────────────────────────────────

    [RelayCommand]
    public async Task CheckEhTagUpdateAsync()
    {
        EhTagStatusText = "正在連線 GitHub 檢查 EhTag 翻譯庫最新版本…";
        var client = new TagTranslationUpdater();
        var latest = await client.CheckLatestAsync().ConfigureAwait(false);

        await _runOnUiAsync(() =>
        {
            _latestEhTagRelease = latest;
            if (latest == null)
            {
                HasEhTagUpdateAvailable = false;
                EhTagStatusText = "⚠️ 無法取得遠端版本（請檢查網路連線或 GitHub API 速率限制）。";
                return;
            }

            var localSha = _dataContext.TagTranslator.HeadSha;
            bool hasUpdate = TagTranslationUpdater.IsUpdateAvailable(localSha, latest.Sha);
            HasEhTagUpdateAvailable = hasUpdate;
            double mb = latest.DbSizeBytes / (1024.0 * 1024.0);
            EhTagDownloadButtonText = $"⬇️ 下載 {latest.Tag} ({mb:F1} MB)";
            EhTagStatusText = hasUpdate
                ? $"🆕 發現新版本 {latest.Tag}（SHA {latest.Sha[..Math.Min(8, latest.Sha.Length)]}），發布於 {latest.PublishedAt ?? "-"}"
                : $"✅ 目前已是最新版本 {latest.Tag}（SHA {latest.Sha[..Math.Min(8, latest.Sha.Length)]}）";
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task DownloadAndApplyEhTagAsync()
    {
        if (!EnsureWritable("更新 EhTag 翻譯詞庫")) return;

        var release = _latestEhTagRelease;
        if (release == null)
        {
            await CheckEhTagUpdateAsync().ConfigureAwait(false);
            release = _latestEhTagRelease;
            if (release == null) return;
        }

        string destPath = Path.Combine(_dataContext.DataDirectory, "translations", "db.text.json.gz");
        await _runOnUiAsync(() => EhTagStatusText = $"正在下載 {release.Tag}…").ConfigureAwait(false);

        var updater = new TagTranslationUpdater();
        var progress = new Progress<double>(pct =>
        {
            _ = _runOnUiAsync(() => EhTagStatusText = $"下載 EhTag 詞庫中… {(int)Math.Round(pct * 100)}%");
        });

        bool ok = await updater.DownloadAsync(release.DbAssetUrl, destPath, progress).ConfigureAwait(false);
        if (!ok)
        {
            await _runOnUiAsync(() =>
            {
                EhTagStatusText = "❌ 下載翻譯庫失敗，保留原詞庫不變。";
                ShowToast("下載失敗", EhTagStatusText, NotificationType.Error);
            }).ConfigureAwait(false);
            return;
        }

        _dataContext.ReloadTagTranslator();
        int syncedCount = await Task.Run(() =>
        {
            var tags = _store.ListTagsByUsage();
            var map = tags.ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);
            return _dataContext.TagTranslator.SyncTranslationsToDb(_store, "zh-TW", map);
        }).ConfigureAwait(false);

        await _runOnUiAsync(() =>
        {
            HasEhTagUpdateAvailable = false;
            RefreshEhTagLocalSummary();
            LoadTags();
            AddMaintenanceLog($"已更新 EhTag 翻譯詞庫至 {release.Tag}，並同步寫入 {syncedCount:N0} 筆標籤譯文。");
            ShowToast("EhTag 詞庫已更新", $"已同步 {syncedCount:N0} 筆標籤譯文", NotificationType.Success);
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task RunBatchNhentaiSyncAsync()
    {
        if (IsMaintenanceRunning || !EnsureWritable("執行 nhentai 批次同步")) return;

        var apiKey = _dataContext.Config.GetNhentaiApiKey();
        await _runOnUiAsync(() =>
        {
            IsMaintenanceRunning = true;
            ExternalSyncSummaryText = "正在執行全庫 nhentai 標籤比對與同步…";
        }).ConfigureAwait(false);

        try
        {
            using var apiClient = new NhentaiApiClient(apiKey);
            var service = new BatchExternalFetchService(_store, apiClient, _dataContext.TagTranslator);
            var summary = await service.RunLibraryAutoTagFetchAsync(_tracker, SyncConfidenceThreshold).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                var msg = summary.Cancelled
                    ? $"已取消同步：套用 {summary.Applied} / 掃描 {summary.Total}（略過 {summary.Skipped}、未命中 {summary.Unmatched}、錯誤 {summary.Errors}）"
                    : $"同步完成：套用 {summary.Applied:N0} / 掃描 {summary.Total:N0}（略過 {summary.Skipped:N0}、未命中 {summary.Unmatched:N0}、錯誤 {summary.Errors:N0}）";
                ExternalSyncSummaryText = msg;
                AddMaintenanceLog(msg);
                RefreshAll();
                ShowToast("nhentai 同步結束", msg, summary.Errors == 0 ? NotificationType.Success : NotificationType.Warning);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsMaintenanceRunning = false).ConfigureAwait(false);
        }
    }

    // ── W4.3 Cache, Reparse & Reconcile Commands ──────────────────────────

    [RelayCommand]
    public async Task ScanIntegrityAsync()
    {
        if (IsMaintenanceRunning) return;
        IsMaintenanceRunning = true;
        MaintenancePhaseText = "正在掃描快取完整性 (只讀檢查)…";

        try
        {
            var report = await Task.Run(() =>
            {
                var ct = _tracker.Start("檢查快取完整性");
                try
                {
                    var checker = new CacheIntegrityChecker(_store, _thumbStore, _tracker);
                    var r = checker.Scan(ct);
                    _tracker.NotifyEnd(cancelled: false);
                    return r;
                }
                catch (OperationCanceledException)
                {
                    _tracker.NotifyEnd(cancelled: true);
                    return null;
                }
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _lastIntegrityReport = report;
                if (report == null)
                {
                    IntegritySummaryText = "完整性掃描已取消";
                    CanFixIntegrity = false;
                    return;
                }

                IntegritySummaryText =
                    $"資產 {report.TotalDbRecords:N0} 筆 · 縮圖檔 {report.TotalThumbFiles:N0} 個 ｜ " +
                    $"孤兒紀錄 {report.OrphanDbRecords} · 孤兒縮圖 {report.OrphanThumbs} · 遺失縮圖 {report.MissingThumbs} · Mtime 過期 {report.StaleMtime}";
                CanFixIntegrity = IsWritable && (report.OrphanDbRecords > 0 || report.OrphanThumbs > 0);
                AddMaintenanceLog($"完整性掃描：{IntegritySummaryText}");
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsMaintenanceRunning = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task FixIntegrityOrphansAsync()
    {
        if (_lastIntegrityReport == null || !EnsureWritable("清理孤兒快取紀錄")) return;

        var report = _lastIntegrityReport;
        await Task.Run(() =>
        {
            _dataContext.TryCreateSafetyBackup("fix_orphans");
            var ct = _tracker.Start("清理孤兒紀錄與縮圖");
            try
            {
                var checker = new CacheIntegrityChecker(_store, _thumbStore, _tracker);
                checker.Fix(report, ct);
                _tracker.NotifyEnd(false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(true);
            }
        }).ConfigureAwait(false);

        await ScanIntegrityAsync().ConfigureAwait(false);
        await _runOnUiAsync(() =>
        {
            RefreshCacheStats();
            AddMaintenanceLog("已完成孤兒資料庫紀錄與縮圖檔案清理。");
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task AnalyzeReconcileAsync()
    {
        if (IsMaintenanceRunning) return;
        var cfg = _dataContext.Config;
        if (string.IsNullOrWhiteSpace(cfg.RootDirectory) || !Directory.Exists(cfg.RootDirectory))
        {
            ShowToast("根目錄未設定", "請先於「系統設定」設定有效的 RootDirectory。", NotificationType.Warning);
            return;
        }

        IsMaintenanceRunning = true;
        MaintenancePhaseText = "正在分析磁碟與資料庫對帳差異 (只讀分析)…";

        try
        {
            var svc = new CatalogReconcileService(cfg, _store);
            var report = await svc.AnalyzeAsync().ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _lastReconcileReport = report;
                ReconcileSummaryText =
                    $"掃描 DB {report.DbAssetsScanned:N0} / FS {report.FsWorksScanned:N0} ｜ " +
                    $"路徑搬移變更 {report.PathChanges.Count} · 失聯孤兒 {report.Orphans.Count} · 歧義略過 {report.Ambiguous.Count}";
                CanApplyReconcile = IsWritable && (report.PathChanges.Count > 0 || report.Orphans.Count > 0);
                AddMaintenanceLog($"對帳分析：{ReconcileSummaryText}");
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsMaintenanceRunning = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task ApplyReconcileChangesAsync()
    {
        if (_lastReconcileReport == null || !EnsureWritable("套用目錄對帳更新")) return;

        var report = _lastReconcileReport;
        var svc = new CatalogReconcileService(_dataContext.Config, _store);
        var (moved, deleted) = await Task.Run(() =>
        {
            _dataContext.TryCreateSafetyBackup("reconcile");
            int m = svc.ApplyPathChanges(report.PathChanges);
            int d = svc.DeleteOrphans(report.Orphans.Select(o => o.AssetId));
            return (m, d);
        }).ConfigureAwait(false);

        await _runOnUiAsync(() =>
        {
            CanApplyReconcile = false;
            _lastReconcileReport = null;
            RefreshAll();
            var msg = $"已套用對帳變更：更新搬移路徑 {moved} 筆，移除失聯孤兒紀錄 {deleted} 筆。";
            ReconcileSummaryText = msg;
            AddMaintenanceLog(msg);
            ShowToast("對帳套用完成", msg, NotificationType.Success);
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task RunReparseAllAsync()
    {
        if (IsMaintenanceRunning || !EnsureWritable("重解析全庫 Metadata")) return;

        IsMaintenanceRunning = true;
        try
        {
            _dataContext.TryCreateSafetyBackup("reparse_all");
            var svc = new ReparseAllService(_store, _tracker, () => _dataContext.Config.RootDirectory);
            var summary = await svc.RunAsync().ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                var msg = summary.Cancelled
                    ? $"已取消重解析：完成 {summary.Reparsed} / {summary.Total}"
                    : $"重解析完成：更新 {summary.Reparsed:N0} 筆、補登 {summary.Cataloged:N0} 筆、跳過手動編輯 {summary.Skipped:N0} 筆、失敗 {summary.Failed} 筆";
                AddMaintenanceLog(msg);
                RefreshAll();
                ShowToast("Metadata 重解析完成", msg, NotificationType.Success);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsMaintenanceRunning = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task SyncSkiaThumbnailsAsync()
    {
        if (IsMaintenanceRunning || !EnsureWritable("增量更新 Skia 縮圖與索引")) return;

        var cfg = _dataContext.Config;
        if (string.IsNullOrWhiteSpace(cfg.RootDirectory) || !Directory.Exists(cfg.RootDirectory))
        {
            ShowToast("根目錄未設定", "請先設定有效的 RootDirectory。", NotificationType.Warning);
            return;
        }

        IsMaintenanceRunning = true;
        try
        {
            var (added, skipped) = await Task.Run<(int Added, int Skipped)>(async () =>
            {
                var ct = _tracker.Start("Skia 縮圖增量同步中");
                int addCnt = 0;
                int skipCnt = 0;
                try
                {
                    var entries = _store.ListAssetEntries()
                        .Where(e => !(!e.IsFolder && SupportedExtensions.IsImage(e.Path)))
                        .ToList();

                    _tracker.Update(new RebuildProgress { Done = 0, Total = entries.Count });

                    for (int i = 0; i < entries.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = entries[i];
                        var existing = _store.Get(entry.Path);
                        if (existing != null && !string.IsNullOrEmpty(existing.CoverThumbKey))
                        {
                            using var s = _thumbStore.OpenRead(existing.CoverThumbKey);
                            if (s != null)
                            {
                                skipCnt++;
                                _tracker.Update(new RebuildProgress { Done = i + 1, Total = entries.Count, CurrentPath = entry.Path });
                                continue;
                            }
                        }

                        var jpeg = await _coverService
                            .ExtractCoverJpegBytesAsync(entry.Path, SkiaCoverService.DefaultThumbWidth, SkiaCoverService.DefaultJpegQuality, ct)
                            .ConfigureAwait(false);

                        if (jpeg != null && jpeg.Length > 0)
                        {
                            string thumbKey = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(entry.Path))).ToLowerInvariant();
                            _thumbStore.Write(thumbKey, jpeg);
                            int? width = null;
                            int? height = null;
                            try
                            {
                                using var ms = new MemoryStream(jpeg, writable: false);
                                using var codec = SkiaSharp.SKCodec.Create(ms);
                                if (codec != null)
                                {
                                    width = codec.Info.Width;
                                    height = codec.Info.Height;
                                }
                            }
                            catch { }

                            long mtime = Directory.Exists(entry.Path)
                                ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(entry.Path)).ToUnixTimeSeconds()
                                : (File.Exists(entry.Path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(entry.Path)).ToUnixTimeSeconds() : 0);

                            _store.Upsert(new AssetMeta
                            {
                                Path = entry.Path,
                                Name = Path.GetFileName(entry.Path),
                                IsFolder = entry.IsFolder,
                                FileSize = entry.IsFolder ? null : (File.Exists(entry.Path) ? new FileInfo(entry.Path).Length : null),
                                Mtime = mtime,
                                CoverOrigin = "skia-sync",
                                CoverThumbKey = thumbKey,
                                CoverWidth = width,
                                CoverHeight = height,
                                LastScanned = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                LastAccessed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            });
                            addCnt++;
                        }

                        _tracker.Update(new RebuildProgress { Done = i + 1, Total = entries.Count, CurrentPath = entry.Path });
                    }
                    _tracker.NotifyEnd(false);
                }
                catch (OperationCanceledException)
                {
                    _tracker.NotifyEnd(true);
                }
                return (addCnt, skipCnt);
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                RefreshCacheStats();
                var msg = $"Skia 縮圖增量同步完成：補建 {added:N0} 張，快取命中略過 {skipped:N0} 張。";
                AddMaintenanceLog(msg);
                ShowToast("縮圖同步完成", msg, NotificationType.Success);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsMaintenanceRunning = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task ClearThumbCacheAsync()
    {
        if (IsMaintenanceRunning || !EnsureWritable("清除縮圖快取")) return;
        IsMaintenanceRunning = true;
        try
        {
            await Task.Run(() =>
            {
                var ct = _tracker.Start("清除縮圖快取中");
                _thumbStore.Clear();
                _store.ClearThumbFields();
                _tracker.NotifyEnd(false);
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _coverService.ClearMemoryCache();
                RefreshCacheStats();
                AddMaintenanceLog("已清除所有縮圖磁碟快取（保留 catalog 與 metadata）。");
                ShowToast("縮圖快取已清除", "可隨時點選「🖼️ 增量更新縮圖」重建。", NotificationType.Success);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsMaintenanceRunning = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void CancelMaintenance()
    {
        _tracker.Cancel();
    }

    private void OnTrackerStateChanged(object? sender, EventArgs e)
    {
        var cur = _tracker.Current;
        var phase = _tracker.Phase;
        _ = _runOnUiAsync(() =>
        {
            MaintenancePhaseText = string.IsNullOrWhiteSpace(phase) ? "待機中" : $"{phase} ({cur.Done:N0}/{cur.Total:N0})";
            MaintenanceProgressPercent = cur.Total > 0 ? Math.Clamp((double)cur.Done / cur.Total * 100.0, 0, 100) : 0;
        });
    }

    private void AddMaintenanceLog(string message)
    {
        MaintenanceLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (MaintenanceLogs.Count > 50)
            MaintenanceLogs.RemoveAt(MaintenanceLogs.Count - 1);
    }

    public bool EnsureWritable(string actionName)
    {
        if (!IsReadOnlyDatabase) return true;
        ShowToast("唯讀保護攔截", $"共用開發庫為唯讀模式，已安全停用「{actionName}」，不更動 D:\\ComicOrganizer\\cache.db。", NotificationType.Warning);
        return false;
    }

    private void ShowToast(string title, string message, NotificationType type)
        => _dataContext.TryShowToast(title, message, type);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.StateChanged -= OnTrackerStateChanged;
        _tracker.Cancel();
    }
}

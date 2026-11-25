using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.VisualSearch;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;

namespace ComicOrganizer.Avalonia.ViewModels;

public enum AutoArchiveFilterTab
{
    All = 0,
    CheckedOnly = 1,
    UnmatchedOnly = 2
}

public sealed partial class AutoArchiveCandidateVm : ObservableObject
{
    private readonly string _rootDirectory;
    private readonly Action<AutoArchiveCandidateVm> _onInspectInReader;
    private readonly Action<AutoArchiveCandidateVm> _onRevealInExplorer;
    private readonly Action<AutoArchiveCandidateVm> _onRemoveFromList;
    private readonly Action<AutoArchiveCandidateVm> _onCheckedChanged;
    private readonly Action<AutoArchiveCandidateVm> _onTargetFolderChanged;
    private bool _suppressChangeNotify;

    public AutoArchiveCandidateVm(
        string sourceName,
        string sourceFullPath,
        bool isDirectory,
        string? matchedFolder,
        double confidence,
        bool isLearned,
        IReadOnlyList<string> availableTargetFolders,
        string rootDirectory,
        Action<AutoArchiveCandidateVm> onInspectInReader,
        Action<AutoArchiveCandidateVm> onRevealInExplorer,
        Action<AutoArchiveCandidateVm> onRemoveFromList,
        Action<AutoArchiveCandidateVm> onCheckedChanged,
        Action<AutoArchiveCandidateVm> onTargetFolderChanged)
    {
        SourceName = sourceName;
        SourceFullPath = sourceFullPath;
        IsDirectory = isDirectory;
        _rootDirectory = rootDirectory;
        Confidence = confidence;
        TargetFolderOptions = availableTargetFolders;
        _onInspectInReader = onInspectInReader;
        _onRevealInExplorer = onRevealInExplorer;
        _onRemoveFromList = onRemoveFromList;
        _onCheckedChanged = onCheckedChanged;
        _onTargetFolderChanged = onTargetFolderChanged;

        _suppressChangeNotify = true;
        try
        {
            SelectedTargetFolder = string.IsNullOrWhiteSpace(matchedFolder) ? "未匹配" : matchedFolder;
            MatchSourceLabel = string.IsNullOrWhiteSpace(matchedFolder)
                ? "❓ 未匹配"
                : (isLearned ? "🧠 知識庫" : "🔍 模糊比對");
            _isChecked = !string.IsNullOrWhiteSpace(matchedFolder) && confidence >= 85.0;
        }
        finally
        {
            _suppressChangeNotify = false;
        }
    }

    public string SourceName { get; }
    public string SourceFullPath { get; }
    public bool IsDirectory { get; }
    public double Confidence { get; }
    public IReadOnlyList<string> TargetFolderOptions { get; }

    public string TypeBadgeText => IsDirectory ? "📁 資料夾" : Path.GetExtension(SourceName).TrimStart('.').ToUpperInvariant() switch
    {
        "" => "📄 檔案",
        var ext => $"📦 {ext}"
    };

    public string ConfidenceText => Confidence > 0 ? $"{Confidence:F0}%" : "-";

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidDestination))]
    [NotifyPropertyChangedFor(nameof(DestinationPath))]
    private string _selectedTargetFolder = "未匹配";

    [ObservableProperty]
    private string _matchSourceLabel = "❓ 未匹配";

    [ObservableProperty]
    private string _executionStatus = "待處理";

    [ObservableProperty]
    private string _executionMessage = string.Empty;

    public bool HasValidDestination =>
        !string.IsNullOrWhiteSpace(SelectedTargetFolder) &&
        !string.Equals(SelectedTargetFolder, "未匹配", StringComparison.Ordinal);

    public string DestinationPath => HasValidDestination && !string.IsNullOrWhiteSpace(_rootDirectory)
        ? Path.Combine(_rootDirectory, SelectedTargetFolder)
        : string.Empty;

    public void SetCheckedSilently(bool isChecked)
    {
        if (IsChecked == isChecked)
        {
            return;
        }

        _suppressChangeNotify = true;
        try
        {
            IsChecked = isChecked;
        }
        finally
        {
            _suppressChangeNotify = false;
        }
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_suppressChangeNotify)
            _onCheckedChanged(this);
    }

    partial void OnSelectedTargetFolderChanged(string value)
    {
        if (_suppressChangeNotify) return;

        _suppressChangeNotify = true;
        try
        {
            if (HasValidDestination)
            {
                MatchSourceLabel = "✏️ 手動指定";
                IsChecked = true;
            }
            else
            {
                MatchSourceLabel = "❓ 未匹配";
                IsChecked = false;
            }
        }
        finally
        {
            _suppressChangeNotify = false;
        }

        _onTargetFolderChanged(this);
    }

    [RelayCommand]
    private void InspectInReader() => _onInspectInReader(this);

    [RelayCommand]
    private void RevealInExplorer() => _onRevealInExplorer(this);

    [RelayCommand]
    private void RemoveFromList() => _onRemoveFromList(this);
}

/// <summary>
/// 依目標目錄彙整的自動歸檔群組 ViewModel：支援整組勾選、展開／收合與最低信心提醒。
/// </summary>
public sealed partial class AutoArchiveTargetGroupVm : ObservableObject
{
    private readonly Action _onGroupCheckBatchChanged;
    private readonly Action<string, bool> _onExpandStateChanged;
    private bool _suppressGroupCheck;

    public AutoArchiveTargetGroupVm(
        string targetFolderName,
        bool isUnmatchedGroup,
        IReadOnlyList<AutoArchiveCandidateVm> items,
        bool isExpanded,
        Action onGroupCheckBatchChanged,
        Action<string, bool> onExpandStateChanged)
    {
        TargetFolderName = targetFolderName;
        IsUnmatchedGroup = isUnmatchedGroup;
        _isExpanded = isExpanded;
        _onGroupCheckBatchChanged = onGroupCheckBatchChanged;
        _onExpandStateChanged = onExpandStateChanged;

        foreach (var item in items)
        {
            Items.Add(item);
        }

        RefreshStats();
    }

    public string TargetFolderName { get; }
    public bool IsUnmatchedGroup { get; }
    public ObservableCollection<AutoArchiveCandidateVm> Items { get; } = new();

    public string GroupHeaderTitle => IsUnmatchedGroup ? "❓ 尚未匹配目標目錄" : $"📁 {TargetFolderName}";
    public int TotalCount => Items.Count;
    public int CheckedCount => Items.Count(i => i.IsChecked);

    public string SummaryBadgeText => IsUnmatchedGroup
        ? $"共 {TotalCount} 筆待指定 · 已勾選 {CheckedCount} 筆"
        : $"共 {TotalCount} 筆 · 已勾選 {CheckedCount} 筆";

    public bool HasConfidenceBadge
    {
        get
        {
            if (IsUnmatchedGroup)
            {
                return false;
            }

            return Items.Any(i => i.Confidence > 0);
        }
    }

    public bool HasLowConfidenceItem =>
        !IsUnmatchedGroup && Items.Any(i => i.Confidence > 0 && i.Confidence < 85.0);

    public bool HasNormalConfidenceBadge => HasConfidenceBadge && !HasLowConfidenceItem;

    public string GroupConfidenceBadgeText
    {
        get
        {
            if (IsUnmatchedGroup)
            {
                return string.Empty;
            }

            var confs = Items.Where(i => i.Confidence > 0).Select(i => i.Confidence).ToList();
            if (confs.Count == 0)
            {
                return string.Empty;
            }

            double min = confs.Min();
            double max = confs.Max();
            if (min < 85.0)
            {
                return $"含低信心 (最低 {min:F0}%)";
            }

            return Math.Abs(max - min) < 0.5 ? $"{min:F0}%" : $"{min:F0}%–{max:F0}%";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandGlyph))]
    private bool _isExpanded;

    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    [ObservableProperty]
    private bool? _isGroupChecked;

    partial void OnIsGroupCheckedChanged(bool? value)
    {
        if (_suppressGroupCheck)
        {
            return;
        }

        bool next = value == true;
        foreach (var item in Items)
        {
            item.SetCheckedSilently(next);
        }

        RefreshStats();
        _onGroupCheckBatchChanged();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        _onExpandStateChanged(TargetFolderName, value);
    }

    public void RefreshStats()
    {
        int total = Items.Count;
        int checkedCount = Items.Count(i => i.IsChecked);

        _suppressGroupCheck = true;
        try
        {
            IsGroupChecked = total == 0 || checkedCount == 0
                ? false
                : (checkedCount == total ? true : null);
        }
        finally
        {
            _suppressGroupCheck = false;
        }

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(SummaryBadgeText));
        OnPropertyChanged(nameof(HasConfidenceBadge));
        OnPropertyChanged(nameof(HasLowConfidenceItem));
        OnPropertyChanged(nameof(HasNormalConfidenceBadge));
        OnPropertyChanged(nameof(GroupConfidenceBadgeText));
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}

/// <summary>
/// Phase 4 W4.1 自動歸檔分頁 ViewModel：
/// 結合 <see cref="MappingIntelligence"/>（mappings.json）＋ <see cref="ArchiveOrganizer"/> 智慧兩階段比對、
/// 提供可調整目標目錄之歸檔預覽清單、模擬檢查（Dry-Run）以及具共用庫 1.A 唯讀保護的安全搬檔與學習回寫。
/// </summary>
public sealed partial class AutoArchivePageViewModel : ViewModelBase
{
    private readonly AppDataContext _dataContext;
    private readonly IAssetMetaStore _store;
    private readonly IExternalLauncher _externalLauncher;
    private readonly IReaderWindowLauncher _readerLauncher;
    private readonly ISukiToastManager? _toastManager;
    private readonly Func<Action, Task> _runOnUiAsync;

    private readonly List<AutoArchiveCandidateVm> _allCandidates = new();
    private readonly HashSet<string> _collapsedGroupKeys = new(StringComparer.Ordinal);
    private MappingIntelligence? _intelligence;

    public AutoArchivePageViewModel(
        AppDataContext dataContext,
        IExternalLauncher? externalLauncher = null,
        IReaderWindowLauncher? readerLauncher = null,
        Func<Action, Task>? runOnUiAsync = null)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _store = dataContext.MetaStore;
        _externalLauncher = externalLauncher ?? new SystemExternalLauncher();
        _readerLauncher = readerLauncher ?? new AvaloniaReaderWindowLauncher(dataContext);
        _toastManager = dataContext.ToastManager;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        RefreshConfigSummary();
    }

    public bool IsReadOnlyDatabase => _dataContext.IsReadOnlyDatabase;
    public bool IsWritable => !IsReadOnlyDatabase;

    public string ReadOnlyBannerText =>
        "🔒 共用開發庫唯讀保護中（可自由掃描比對與執行 🧪 模擬測試；實際搬移檔案與寫入 mappings.json / cache.db 已鎖定防止異動）";

    public ObservableCollection<AutoArchiveCandidateVm> VisibleCandidates { get; } = new();
    public ObservableCollection<AutoArchiveTargetGroupVm> TargetGroups { get; } = new();

    [ObservableProperty]
    private string _unorganizedDirectoryText = "尚未設定";

    [ObservableProperty]
    private string _rootDirectoryText = "尚未設定";

    [ObservableProperty]
    private string _rulesSummaryText = "0 條學習規則";

    [ObservableProperty]
    private string _statusMessage = "點擊「🔍 掃描與智慧配對」讀取未整理來源目錄";

    [ObservableProperty]
    private string _filterKeyword = string.Empty;

    [ObservableProperty]
    private AutoArchiveFilterTab _activeFilterTab = AutoArchiveFilterTab.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlatView))]
    private bool _isGroupedView = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteRealMove))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteRealMove))]
    private int _checkedCount;

    [ObservableProperty]
    private int _totalScannedCount;

    [ObservableProperty]
    private int _matchedCount;

    [ObservableProperty]
    private int _unmatchedCount;

    public bool CanExecuteRealMove => IsWritable && !IsBusy && CheckedCount > 0;
    public bool CanSimulate => !IsBusy && CheckedCount > 0;
    public bool HasCandidates => VisibleCandidates.Count > 0;
    public bool IsFlatView => !IsGroupedView;

    public bool IsAllFilterTab => ActiveFilterTab == AutoArchiveFilterTab.All;
    public bool IsCheckedFilterTab => ActiveFilterTab == AutoArchiveFilterTab.CheckedOnly;
    public bool IsUnmatchedFilterTab => ActiveFilterTab == AutoArchiveFilterTab.UnmatchedOnly;

    partial void OnFilterKeywordChanged(string value) => ApplyFilterOnUi();

    partial void OnActiveFilterTabChanged(AutoArchiveFilterTab value)
    {
        OnPropertyChanged(nameof(IsAllFilterTab));
        OnPropertyChanged(nameof(IsCheckedFilterTab));
        OnPropertyChanged(nameof(IsUnmatchedFilterTab));
        ApplyFilterOnUi();
    }

    public void RefreshConfigSummary()
    {
        var cfg = _dataContext.Config;
        UnorganizedDirectoryText = string.IsNullOrWhiteSpace(cfg.UnorganizedDirectory) ? "尚未設定" : cfg.UnorganizedDirectory;
        RootDirectoryText = string.IsNullOrWhiteSpace(cfg.RootDirectory) ? "尚未設定" : cfg.RootDirectory;

        var mappingsPath = Path.Combine(_dataContext.DataDirectory, "mappings.json");
        _intelligence = new MappingIntelligence(mappingsPath);
        RulesSummaryText = $"知識庫 {_intelligence.KnowledgeCount} 條 · 過濾 {cfg.Blacklist.Count} 詞 / {cfg.BlacklistPatterns.Count} 正則";
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        RefreshConfigSummary();

        var cfg = _dataContext.Config;
        if (string.IsNullOrWhiteSpace(cfg.UnorganizedDirectory) || !Directory.Exists(cfg.UnorganizedDirectory))
        {
            StatusMessage = "⚠️ 請先於「系統設定」配置有效的來源未整理目錄。";
            ShowToast("來源路徑無效", StatusMessage, NotificationType.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.RootDirectory) || !Directory.Exists(cfg.RootDirectory))
        {
            StatusMessage = "⚠️ 請先於「系統設定」配置有效的目標漫畫庫根目錄。";
            ShowToast("目標庫路徑無效", StatusMessage, NotificationType.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "正在掃描未整理項目並執行智慧配對…";

        try
        {
            var results = await Task.Run(() =>
            {
                var excluded = new HashSet<string>(
                    cfg.ExcludedDirectories.Select(d => d.Trim()),
                    StringComparer.OrdinalIgnoreCase);

                var targetFolderNames = Directory.GetDirectories(cfg.RootDirectory)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrWhiteSpace(n) && !excluded.Contains(n!) && !n!.StartsWith('.'))
                    .Cast<string>()
                    .OrderBy(n => n, NaturalStringComparer.Instance)
                    .ToList();

                var dropdownOptions = new List<string>(targetFolderNames.Count + 1) { "未匹配" };
                dropdownOptions.AddRange(targetFolderNames);

                var intelligence = _intelligence ?? new MappingIntelligence(Path.Combine(_dataContext.DataDirectory, "mappings.json"));
                var organizer = new ArchiveOrganizer(intelligence, cfg);
                var sourceEntries = Directory.GetFileSystemEntries(cfg.UnorganizedDirectory);
                var list = new List<AutoArchiveCandidateVm>(sourceEntries.Length);

                foreach (var entryPath in sourceEntries)
                {
                    var name = Path.GetFileName(entryPath);
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.'))
                        continue;

                    bool isDir = Directory.Exists(entryPath);
                    if (!isDir && !SupportedExtensions.Gallery.Contains(Path.GetExtension(entryPath)))
                        continue;

                    var match = organizer.FindBestMatch(name, targetFolderNames);
                    list.Add(new AutoArchiveCandidateVm(
                        name,
                        entryPath,
                        isDir,
                        match?.FolderName,
                        match?.Confidence ?? 0,
                        match?.IsLearned ?? false,
                        dropdownOptions,
                        cfg.RootDirectory,
                        OnInspectInReader,
                        OnRevealInExplorer,
                        OnRemoveCandidate,
                        OnCandidateCheckedChanged,
                        OnCandidateTargetFolderChanged));
                }

                return list
                    .OrderByDescending(c => c.Confidence)
                    .ThenBy(c => c.SourceName, NaturalStringComparer.Instance)
                    .ToList();
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _allCandidates.Clear();
                _allCandidates.AddRange(results);
                ApplyFilterOnUi();
                UpdateSelectionCounts();
                StatusMessage = $"掃描完成：共 {TotalScannedCount} 筆項目（已匹配 {MatchedCount} 筆、未匹配 {UnmatchedCount} 筆、已勾選 {CheckedCount} 筆）";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                StatusMessage = $"掃描失敗：{ex.Message}";
                ShowToast("掃描失敗", ex.Message, NotificationType.Error);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void SelectFilterTab(string tab)
    {
        ActiveFilterTab = tab switch
        {
            "Checked" => AutoArchiveFilterTab.CheckedOnly,
            "Unmatched" => AutoArchiveFilterTab.UnmatchedOnly,
            _ => AutoArchiveFilterTab.All
        };
    }

    [RelayCommand]
    private void SelectViewMode(string mode)
    {
        IsGroupedView = !string.Equals(mode, "Flat", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void ExpandAllGroups()
    {
        _collapsedGroupKeys.Clear();
        foreach (var group in TargetGroups)
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAllGroups()
    {
        foreach (var group in TargetGroups)
        {
            _collapsedGroupKeys.Add(group.TargetFolderName);
            group.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void CheckHighConfidence()
    {
        foreach (var c in _allCandidates)
            c.SetCheckedSilently(c.HasValidDestination && c.Confidence >= 85.0);
        RefreshAfterBatchCheckChange();
    }

    [RelayCommand]
    private void CheckAllMatched()
    {
        foreach (var c in _allCandidates)
            c.SetCheckedSilently(c.HasValidDestination);
        RefreshAfterBatchCheckChange();
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var c in _allCandidates)
            c.SetCheckedSilently(false);
        RefreshAfterBatchCheckChange();
    }

    [RelayCommand]
    public async Task SimulateExecuteAsync()
    {
        await RunArchiveExecutionAsync(isDryRun: true);
    }

    [RelayCommand]
    public async Task ExecuteArchiveAsync()
    {
        if (IsReadOnlyDatabase)
        {
            StatusMessage = "🔒 共用開發庫為唯讀模式，已攔截真實搬檔（請改用「🧪 模擬測試」驗證流程）。";
            ShowToast("唯讀保護", StatusMessage, NotificationType.Warning);
            return;
        }

        await RunArchiveExecutionAsync(isDryRun: false);
    }

    private async Task RunArchiveExecutionAsync(bool isDryRun)
    {
        if (IsBusy) return;

        var selected = _allCandidates.Where(c => c.IsChecked).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "請先勾選至少一個待歸檔項目。";
            ShowToast("未勾選項目", StatusMessage, NotificationType.Warning);
            return;
        }

        IsBusy = true;
        int okCount = 0;
        int skipOrFailCount = 0;

        try
        {
            var cfg = _dataContext.Config;
            var mappingsPath = Path.Combine(_dataContext.DataDirectory, "mappings.json");
            var intelligence = _intelligence ?? new MappingIntelligence(mappingsPath);
            var organizer = new ArchiveOrganizer(intelligence, cfg);
            var movedPaths = new List<string>();

            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    if (!item.HasValidDestination)
                    {
                        item.ExecutionStatus = "✕ 略過";
                        item.ExecutionMessage = "尚未選擇目標資料夾";
                        skipOrFailCount++;
                        continue;
                    }

                    string cleanSource = item.SourceFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string targetPath = Path.Combine(item.DestinationPath, Path.GetFileName(cleanSource));

                    if (!File.Exists(cleanSource) && !Directory.Exists(cleanSource))
                    {
                        item.ExecutionStatus = "✕ 找不到來源";
                        item.ExecutionMessage = "來源檔案或資料夾已不存在";
                        skipOrFailCount++;
                        continue;
                    }

                    if (File.Exists(targetPath) || Directory.Exists(targetPath))
                    {
                        item.ExecutionStatus = "⚠️ 名稱衝突";
                        item.ExecutionMessage = $"目標目錄已存在同名項目：{Path.GetFileName(targetPath)}";
                        skipOrFailCount++;
                        continue;
                    }

                    if (isDryRun)
                    {
                        item.ExecutionStatus = "🧪 模擬通過";
                        item.ExecutionMessage = $"將搬移至 {item.SelectedTargetFolder}";
                        okCount++;
                        continue;
                    }

                    try
                    {
                        item.ExecutionStatus = "⏳ 搬移中";
                        string movedPath = ArchiveMoveHelper.MoveItemSafely(item.SourceFullPath, item.DestinationPath);
                        UpsertMovedCatalog(movedPath, item.SelectedTargetFolder);

                        var keywords = organizer.ExtractAllKeywords(item.SourceName);
                        foreach (var kw in keywords)
                        {
                            if (organizer.IsKeywordValidForTarget(kw, item.SelectedTargetFolder))
                            {
                                intelligence.Learn(kw, item.SelectedTargetFolder);
                            }
                        }

                        item.ExecutionStatus = "✓ 已歸檔";
                        item.ExecutionMessage = movedPath;
                        item.IsChecked = false;
                        movedPaths.Add(movedPath);
                        okCount++;
                    }
                    catch (Exception ex)
                    {
                        item.ExecutionStatus = "✕ 失敗";
                        item.ExecutionMessage = ex.Message;
                        skipOrFailCount++;
                    }
                }
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                RefreshConfigSummary();
                RefreshAfterBatchCheckChange();
                string actionWord = isDryRun ? "模擬測試完成" : "歸檔搬檔完成";
                StatusMessage = $"{actionWord}：成功 {okCount} 筆，略過／失敗 {skipOrFailCount} 筆";
                ShowToast(
                    actionWord,
                    StatusMessage,
                    skipOrFailCount == 0 ? NotificationType.Success : NotificationType.Warning);
            }).ConfigureAwait(false);

            if (!isDryRun
                && movedPaths.Count > 0
                && !IsReadOnlyDatabase
                && cfg is { EnableVisualSearch: true, VisualSearchAutoIndexAfterArchive: true }
                && new VisualSearchPaths(_dataContext.DataDirectory).AreModelsReady())
            {
                _ = _dataContext.VisualIndexHost.RunSyncWorkerAsync(
                    cfg.RootDirectory,
                    movedPaths,
                    rebuild: false,
                    enableHeadIndex: cfg.VisualSearchEnableHeadIndex);
            }
        }
        finally
        {
            await _runOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private void UpsertMovedCatalog(string movedPath, string targetFolder)
    {
        if (IsReadOnlyDatabase) return;
        try
        {
            bool isDir = Directory.Exists(movedPath);
            var name = Path.GetFileName(movedPath);
            long mtime = isDir
                ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(movedPath)).ToUnixTimeSeconds()
                : new DateTimeOffset(File.GetLastWriteTimeUtc(movedPath)).ToUnixTimeSeconds();
            long? fileSize = isDir ? null : new FileInfo(movedPath).Length;

            _store.UpsertCatalogOnly(new AssetMeta
            {
                Path = movedPath,
                Name = name,
                IsFolder = isDir,
                FileSize = fileSize,
                Mtime = mtime,
                LastScanned = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastAccessed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            var assetId = _store.GetAssetId(movedPath);
            if (!assetId.HasValue) return;

            var parsed = FilenameMetadataParser.ParseWorkName(name, authorFromParent: targetFolder);
            var authorName = !string.IsNullOrWhiteSpace(parsed.AuthorMainName) ? parsed.AuthorMainName : targetFolder;
            long? authorId = !string.IsNullOrWhiteSpace(authorName) ? _store.UpsertAuthor(authorName) : null;
            if (authorId.HasValue && !string.IsNullOrWhiteSpace(parsed.AuthorAlias))
            {
                _store.AddAuthorAlias(authorId.Value, parsed.AuthorAlias);
            }

            _store.UpsertMetadata(new AssetMetadata
            {
                AssetId = assetId.Value,
                AuthorId = authorId,
                Title = parsed.Title ?? name,
                ChapterNo = parsed.ChapterNo,
                Language = parsed.Language,
                TranslationGroup = parsed.TranslationGroup,
                ParsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

            var filteredTags = parsed.Tags
                .Where(t => !string.Equals(t.Category, "language", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(t.Category, "translation_group", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filteredTags.Count > 0)
            {
                _store.SetAssetTagEntries(assetId.Value, filteredTags);
            }
        }
        catch
        {
            // Catalog 回寫失敗不阻斷檔案搬移回報
        }
    }

    private void ApplyFilterOnUi()
    {
        VisibleCandidates.Clear();
        TargetGroups.Clear();
        var kw = string.IsNullOrWhiteSpace(FilterKeyword) ? null : FilterKeyword.Trim();

        foreach (var item in _allCandidates)
        {
            if (ActiveFilterTab == AutoArchiveFilterTab.CheckedOnly && !item.IsChecked)
                continue;
            if (ActiveFilterTab == AutoArchiveFilterTab.UnmatchedOnly && item.HasValidDestination)
                continue;

            if (kw != null &&
                !item.SourceName.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                !item.SelectedTargetFolder.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            VisibleCandidates.Add(item);
        }

        var grouped = VisibleCandidates
            .GroupBy(c => c.HasValidDestination ? c.SelectedTargetFolder : "未匹配", StringComparer.Ordinal)
            .Select(g => new
            {
                Key = g.Key,
                IsUnmatched = string.Equals(g.Key, "未匹配", StringComparison.Ordinal),
                Items = g.OrderByDescending(i => i.Confidence)
                         .ThenBy(i => i.SourceName, NaturalStringComparer.Instance)
                         .ToList()
            })
            .OrderBy(g => g.IsUnmatched ? 1 : 0)
            .ThenByDescending(g => g.Items.Count)
            .ThenBy(g => g.Key, NaturalStringComparer.Instance);

        foreach (var entry in grouped)
        {
            bool expanded = !_collapsedGroupKeys.Contains(entry.Key);
            TargetGroups.Add(new AutoArchiveTargetGroupVm(
                entry.Key,
                entry.IsUnmatched,
                entry.Items,
                expanded,
                OnGroupCheckBatchChanged,
                OnGroupExpandStateChanged));
        }

        OnPropertyChanged(nameof(HasCandidates));
    }

    private void OnCandidateCheckedChanged(AutoArchiveCandidateVm item)
    {
        if (ActiveFilterTab == AutoArchiveFilterTab.CheckedOnly)
        {
            ApplyFilterOnUi();
        }
        else
        {
            RefreshTargetGroupStats();
        }

        UpdateSelectionCounts();
    }

    private void OnCandidateTargetFolderChanged(AutoArchiveCandidateVm item)
    {
        ApplyFilterOnUi();
        UpdateSelectionCounts();
    }

    private void OnGroupCheckBatchChanged()
    {
        if (ActiveFilterTab == AutoArchiveFilterTab.CheckedOnly)
        {
            ApplyFilterOnUi();
        }

        UpdateSelectionCounts();
    }

    private void OnGroupExpandStateChanged(string groupKey, bool isExpanded)
    {
        if (isExpanded)
        {
            _collapsedGroupKeys.Remove(groupKey);
        }
        else
        {
            _collapsedGroupKeys.Add(groupKey);
        }
    }

    private void RefreshAfterBatchCheckChange()
    {
        if (ActiveFilterTab == AutoArchiveFilterTab.CheckedOnly)
        {
            ApplyFilterOnUi();
        }
        else
        {
            RefreshTargetGroupStats();
        }

        UpdateSelectionCounts();
    }

    private void RefreshTargetGroupStats()
    {
        foreach (var group in TargetGroups)
        {
            group.RefreshStats();
        }
    }

    private void UpdateSelectionCounts()
    {
        TotalScannedCount = _allCandidates.Count;
        MatchedCount = _allCandidates.Count(c => c.HasValidDestination);
        UnmatchedCount = TotalScannedCount - MatchedCount;
        CheckedCount = _allCandidates.Count(c => c.IsChecked);
        OnPropertyChanged(nameof(CanSimulate));
        OnPropertyChanged(nameof(CanExecuteRealMove));
    }

    private void OnInspectInReader(AutoArchiveCandidateVm item)
    {
        if (!_readerLauncher.OpenInReader(item.SourceFullPath, item.SourceName, item.HasValidDestination ? item.SelectedTargetFolder : null, null, out var err))
        {
            ShowToast("無法預覽", err ?? item.SourceFullPath, NotificationType.Warning);
        }
    }

    private void OnRevealInExplorer(AutoArchiveCandidateVm item)
    {
        if (!_externalLauncher.RevealInFileExplorer(item.SourceFullPath, out var err))
        {
            ShowToast("無法開啟檔案總管", err ?? item.SourceFullPath, NotificationType.Warning);
        }
    }

    private void OnRemoveCandidate(AutoArchiveCandidateVm item)
    {
        _allCandidates.Remove(item);
        ApplyFilterOnUi();
        UpdateSelectionCounts();
    }

    private void ShowToast(string title, string message, NotificationType type)
        => _dataContext.TryShowToast(title, message, type);
}

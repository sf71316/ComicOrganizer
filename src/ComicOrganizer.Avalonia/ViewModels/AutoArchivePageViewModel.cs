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
        Action<AutoArchiveCandidateVm> onTargetFolderChanged,
        string? matchSourceSummary = null)
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
                : (isLearned
                    ? "🧠 知識庫"
                    : (!string.IsNullOrWhiteSpace(matchSourceSummary) ? $"🔍 {matchSourceSummary}" : "🔍 模糊比對"));
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

    public bool IsManuallyAssigned { get; private set; }

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
                IsManuallyAssigned = true;
                MatchSourceLabel = "✏️ 手動指定";
                IsChecked = true;
            }
            else
            {
                IsManuallyAssigned = false;
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
/// 「📥 待歸檔清單」抽屜內的單筆待搬移作品項目。
/// </summary>
public sealed partial class AutoArchiveQueuedItemVm : ObservableObject
{
    private readonly Action<AutoArchiveQueuedItemVm> _onInspectInReader;
    private readonly Action<AutoArchiveQueuedItemVm> _onRevealInExplorer;
    private readonly Action<AutoArchiveQueuedItemVm> _onRemove;

    public AutoArchiveQueuedItemVm(
        AutoArchiveCandidateVm sourceCandidate,
        string targetFolderName,
        string destinationPath,
        bool isManuallyAssigned,
        Action<AutoArchiveQueuedItemVm> onInspectInReader,
        Action<AutoArchiveQueuedItemVm> onRevealInExplorer,
        Action<AutoArchiveQueuedItemVm> onRemove)
    {
        SourceCandidate = sourceCandidate;
        SourceName = sourceCandidate.SourceName;
        SourceFullPath = sourceCandidate.SourceFullPath;
        IsDirectory = sourceCandidate.IsDirectory;
        TypeBadgeText = sourceCandidate.TypeBadgeText;
        _targetFolderName = targetFolderName;
        _destinationPath = destinationPath;
        _isManuallyAssigned = isManuallyAssigned;
        _onInspectInReader = onInspectInReader;
        _onRevealInExplorer = onRevealInExplorer;
        _onRemove = onRemove;
    }

    public AutoArchiveCandidateVm SourceCandidate { get; }
    public string SourceName { get; }
    public string SourceFullPath { get; }
    public bool IsDirectory { get; }
    public string TypeBadgeText { get; }

    [ObservableProperty]
    private string _targetFolderName;

    [ObservableProperty]
    private string _destinationPath;

    [ObservableProperty]
    private bool _isManuallyAssigned;

    [ObservableProperty]
    private string _statusText = "待歸檔";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _progressValue;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    public string ProgressText => IsProgressIndeterminate ? "搬移中..." : $"{ProgressValue:F0}%";

    public void UpdateTarget(AutoArchiveCandidateVm candidate, string targetFolderName, string destinationPath, bool isManuallyAssigned)
    {
        TargetFolderName = targetFolderName;
        DestinationPath = destinationPath;
        IsManuallyAssigned = isManuallyAssigned;
        StatusText = "待歸檔";
        StatusMessage = string.Empty;
        IsProcessing = false;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
    }

    public void SetProgressState(bool isProcessing, double progressValue, bool isIndeterminate)
    {
        IsProcessing = isProcessing;
        ProgressValue = Math.Clamp(progressValue, 0.0, 100.0);
        IsProgressIndeterminate = isIndeterminate;
    }

    [RelayCommand]
    private void InspectInReader() => _onInspectInReader(this);

    [RelayCommand]
    private void RevealInExplorer() => _onRevealInExplorer(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}

/// <summary>
/// 依目標目錄彙整的自動歸檔群組 ViewModel：支援整條標題點擊展開／收合、整組勾選與一鍵加入待歸檔清單。
/// </summary>
public sealed partial class AutoArchiveTargetGroupVm : ObservableObject
{
    private readonly Action _onGroupCheckBatchChanged;
    private readonly Action<string, bool> _onExpandStateChanged;
    private readonly Action<AutoArchiveTargetGroupVm> _onAddCheckedToQueue;
    private bool _suppressGroupCheck;

    public AutoArchiveTargetGroupVm(
        string targetFolderName,
        bool isUnmatchedGroup,
        IReadOnlyList<AutoArchiveCandidateVm> items,
        bool isExpanded,
        Action onGroupCheckBatchChanged,
        Action<string, bool> onExpandStateChanged,
        Action<AutoArchiveTargetGroupVm> onAddCheckedToQueue)
    {
        TargetFolderName = targetFolderName;
        IsUnmatchedGroup = isUnmatchedGroup;
        _isExpanded = isExpanded;
        _onGroupCheckBatchChanged = onGroupCheckBatchChanged;
        _onExpandStateChanged = onExpandStateChanged;
        _onAddCheckedToQueue = onAddCheckedToQueue;

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

    public int EnqueuedCheckedCount => Items.Count(i => i.IsChecked && i.HasValidDestination);
    public bool CanAddCheckedToQueue => !IsUnmatchedGroup && EnqueuedCheckedCount > 0;
    public string AddToQueueButtonText => EnqueuedCheckedCount > 0 ? $"📥 放入清單 ({EnqueuedCheckedCount})" : "📥 放入清單";

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
        OnPropertyChanged(nameof(EnqueuedCheckedCount));
        OnPropertyChanged(nameof(CanAddCheckedToQueue));
        OnPropertyChanged(nameof(AddToQueueButtonText));
        AddCheckedToQueueCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SummaryBadgeText));
        OnPropertyChanged(nameof(HasConfidenceBadge));
        OnPropertyChanged(nameof(HasLowConfidenceItem));
        OnPropertyChanged(nameof(HasNormalConfidenceBadge));
        OnPropertyChanged(nameof(GroupConfidenceBadgeText));
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand(CanExecute = nameof(CanAddCheckedToQueue))]
    private void AddCheckedToQueue() => _onAddCheckedToQueue(this);
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
    private readonly IArchiveConflictPrompt _conflictPrompt;

    private readonly List<AutoArchiveCandidateVm> _allCandidates = new();
    private readonly HashSet<string> _collapsedGroupKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _explicitExpandedGroups = new(StringComparer.Ordinal);
    private MappingIntelligence? _intelligence;

    public AutoArchivePageViewModel(
        AppDataContext dataContext,
        IExternalLauncher? externalLauncher = null,
        IReaderWindowLauncher? readerLauncher = null,
        Func<Action, Task>? runOnUiAsync = null,
        IArchiveConflictPrompt? conflictPrompt = null)
    {
        _dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
        _store = dataContext.MetaStore;
        _externalLauncher = externalLauncher ?? new SystemExternalLauncher();
        _readerLauncher = readerLauncher ?? new AvaloniaReaderWindowLauncher(dataContext);
        _toastManager = dataContext.ToastManager;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;
        _conflictPrompt = conflictPrompt ?? new AvaloniaArchiveConflictPrompt();

        RefreshConfigSummary();
    }

    public bool IsReadOnlyDatabase => _dataContext.IsReadOnlyDatabase;
    public bool IsWritable => !IsReadOnlyDatabase;

    public string ReadOnlyBannerText =>
        "🔒 共用開發庫唯讀保護中（可自由掃描比對與執行 🧪 模擬測試；實際搬移檔案與寫入 mappings.json / cache.db 已鎖定防止異動）";

    public ObservableCollection<AutoArchiveCandidateVm> VisibleCandidates { get; } = new();
    public ObservableCollection<AutoArchiveTargetGroupVm> TargetGroups { get; } = new();
    public ObservableCollection<AutoArchiveQueuedItemVm> QueuedItems { get; } = new();

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
    private bool _isQueueDrawerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteRealMove))]
    [NotifyPropertyChangedFor(nameof(CanSimulate))]
    [NotifyPropertyChangedFor(nameof(CanExecuteQueue))]
    [NotifyPropertyChangedFor(nameof(CanSimulateQueue))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecuteRealMove))]
    [NotifyPropertyChangedFor(nameof(CanSimulate))]
    [NotifyPropertyChangedFor(nameof(CanAddAnyCheckedToQueue))]
    private int _checkedCount;

    [ObservableProperty]
    private int _totalScannedCount;

    [ObservableProperty]
    private int _matchedCount;

    [ObservableProperty]
    private int _unmatchedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchProgressPercent))]
    [NotifyPropertyChangedFor(nameof(BatchProgressText))]
    private int _batchProcessedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchProgressPercent))]
    [NotifyPropertyChangedFor(nameof(BatchProgressText))]
    private int _batchTotalCount;

    public bool CanExecuteRealMove => IsWritable && !IsBusy && CheckedCount > 0;
    public bool CanSimulate => !IsBusy && CheckedCount > 0;
    public bool HasCandidates => VisibleCandidates.Count > 0;
    public bool IsFlatView => !IsGroupedView;

    public double BatchProgressPercent => BatchTotalCount <= 0
        ? 0.0
        : Math.Clamp((double)BatchProcessedCount / BatchTotalCount * 100.0, 0.0, 100.0);

    public string BatchProgressText => BatchTotalCount <= 0
        ? string.Empty
        : $"進度 {BatchProcessedCount} / {BatchTotalCount}";

    public int QueueCount => QueuedItems.Count;
    public bool HasQueuedItems => QueuedItems.Count > 0;
    public bool IsQueueEmpty => QueuedItems.Count == 0;
    public string QueueToggleButtonLabel => $"📥 待歸檔清單 ({QueueCount})";
    public string QueueHeaderCountText => $"({QueueCount})";
    public bool CanSimulateQueue => !IsBusy && HasQueuedItems;
    public bool CanExecuteQueue => IsWritable && !IsBusy && HasQueuedItems;
    public bool CanAddAnyCheckedToQueue
    {
        get
        {
            var queuedFolders = GetQueuedTargetFolders();
            return _allCandidates.Any(c =>
                c.IsChecked &&
                c.HasValidDestination &&
                !queuedFolders.Contains(c.SelectedTargetFolder));
        }
    }

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
                        OnCandidateTargetFolderChanged,
                        match?.MatchSourceSummary));
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
                QueuedItems.Clear();
                IsQueueDrawerOpen = false;
                ApplyFilterOnUi();
                UpdateSelectionCounts();
                NotifyQueueStateChanged();
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
        _explicitExpandedGroups.Add("未匹配");
        foreach (var group in TargetGroups)
        {
            _explicitExpandedGroups.Add(group.TargetFolderName);
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAllGroups()
    {
        _explicitExpandedGroups.Clear();
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
    private void ToggleQueueDrawer() => IsQueueDrawerOpen = !IsQueueDrawerOpen;

    [RelayCommand]
    private void CloseQueueDrawer() => IsQueueDrawerOpen = false;

    [RelayCommand(CanExecute = nameof(CanAddAnyCheckedToQueue))]
    private void AddAllCheckedToQueue()
    {
        var queuedFolders = GetQueuedTargetFolders();
        var ready = _allCandidates
            .Where(c => c.IsChecked && c.HasValidDestination && !queuedFolders.Contains(c.SelectedTargetFolder))
            .ToList();
        EnqueueCandidates(ready, openDrawer: true);
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (QueuedItems.Count == 0)
        {
            return;
        }

        var affectedFolders = QueuedItems
            .Select(q => q.TargetFolderName)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var q in QueuedItems)
        {
            RestoreCandidateStatusIfQueued(q.SourceFullPath);
        }

        QueuedItems.Clear();

        if (affectedFolders.Count <= 8)
        {
            RestoreFoldersIncremental(affectedFolders);
        }
        else
        {
            ApplyFilterOnUi();
        }

        UpdateSelectionCounts();
        NotifyQueueStateChanged();
    }

    private void OnRemoveQueuedItem(AutoArchiveQueuedItemVm queuedItem)
    {
        if (queuedItem.IsProcessing)
        {
            return;
        }

        QueuedItems.Remove(queuedItem);
        RestoreCandidateStatusIfQueued(queuedItem.SourceFullPath);

        bool folderStillQueued = QueuedItems.Any(
            q => string.Equals(q.TargetFolderName, queuedItem.TargetFolderName, StringComparison.OrdinalIgnoreCase));
        if (!folderStillQueued && !string.IsNullOrWhiteSpace(queuedItem.TargetFolderName))
        {
            RestoreFoldersIncremental(new[] { queuedItem.TargetFolderName });
        }

        UpdateSelectionCounts();
        NotifyQueueStateChanged();
    }

    private void RestoreCandidateStatusIfQueued(string sourceFullPath)
    {
        var candidate = _allCandidates.FirstOrDefault(
            c => string.Equals(c.SourceFullPath, sourceFullPath, StringComparison.OrdinalIgnoreCase));
        if (candidate != null && string.Equals(candidate.ExecutionStatus, "📥 已入清單", StringComparison.Ordinal))
        {
            candidate.ExecutionStatus = "待處理";
            candidate.ExecutionMessage = string.Empty;
        }
    }

    private void OnAddGroupCheckedToQueue(AutoArchiveTargetGroupVm group)
    {
        var ready = group.Items.Where(i => i.IsChecked && i.HasValidDestination).ToList();
        EnqueueCandidates(ready, openDrawer: true);
    }

    private void EnqueueCandidates(IReadOnlyList<AutoArchiveCandidateVm> candidates, bool openDrawer)
    {
        if (candidates.Count == 0)
        {
            StatusMessage = "請先勾選至少一個已指定目標目錄的作品再放入清單。";
            ShowToast("無可入列項目", StatusMessage, NotificationType.Warning);
            return;
        }

        int added = 0;
        int updated = 0;
        var affectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidates)
        {
            if (!item.HasValidDestination)
            {
                continue;
            }

            affectedFolders.Add(item.SelectedTargetFolder);

            var existing = QueuedItems.FirstOrDefault(
                q => string.Equals(q.SourceFullPath, item.SourceFullPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.UpdateTarget(item, item.SelectedTargetFolder, item.DestinationPath, item.IsManuallyAssigned);
                updated++;
            }
            else
            {
                QueuedItems.Add(new AutoArchiveQueuedItemVm(
                    item,
                    item.SelectedTargetFolder,
                    item.DestinationPath,
                    item.IsManuallyAssigned,
                    OnInspectQueuedInReader,
                    OnRevealQueuedInExplorer,
                    OnRemoveQueuedItem));
                added++;
            }

            // 選項 A：保留左側預覽項目但取消勾選，改標記「📥 已入清單」避免重複誤入列
            item.SetCheckedSilently(false);
            item.ExecutionStatus = "📥 已入清單";
            item.ExecutionMessage = $"待歸檔至：{item.SelectedTargetFolder}";
        }

        if (openDrawer && (added > 0 || updated > 0))
        {
            IsQueueDrawerOpen = true;
        }

        HideFoldersIncremental(affectedFolders);
        UpdateSelectionCounts();
        NotifyQueueStateChanged();
        StatusMessage = updated > 0
            ? $"已加入 {added} 筆並更新 {updated} 筆至「📥 待歸檔清單」（共 {QueueCount} 筆）"
            : $"已將 {added} 筆加入「📥 待歸檔清單」（共 {QueueCount} 筆）";
    }

    [RelayCommand(CanExecute = nameof(CanSimulateQueue))]
    public async Task SimulateQueueAsync()
    {
        await RunQueuedExecutionAsync(isDryRun: true);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQueue))]
    public async Task ExecuteQueueAsync()
    {
        if (IsReadOnlyDatabase)
        {
            StatusMessage = "🔒 共用開發庫為唯讀模式，已攔截真實搬檔（請改用「🧪 模擬測試」驗證流程）。";
            ShowToast("唯讀保護", StatusMessage, NotificationType.Warning);
            return;
        }

        await RunQueuedExecutionAsync(isDryRun: false);
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

    private async Task RunQueuedExecutionAsync(bool isDryRun)
    {
        if (IsBusy) return;
        var snapshot = QueuedItems.ToList();
        if (snapshot.Count == 0)
        {
            StatusMessage = "「📥 待歸檔清單」目前為空。";
            ShowToast("清單為空", StatusMessage, NotificationType.Warning);
            return;
        }

        var entries = snapshot.Select(q => new BatchMoveEntry(
            SourceName: q.SourceName,
            SourceFullPath: q.SourceFullPath,
            TargetFolder: q.TargetFolderName,
            DestinationPath: q.DestinationPath,
            IsManuallyAssigned: q.IsManuallyAssigned,
            SetStatus: (status, msg) =>
            {
                q.StatusText = status;
                q.StatusMessage = msg;
                var candidate = _allCandidates.FirstOrDefault(
                    c => string.Equals(c.SourceFullPath, q.SourceFullPath, StringComparison.OrdinalIgnoreCase));
                if (candidate != null)
                {
                    candidate.ExecutionStatus = status;
                    candidate.ExecutionMessage = msg;
                }
            },
            OnMoveSucceeded: _ => q.SourceCandidate.SetCheckedSilently(false),
            SetProgress: (isProcessing, progressValue, isIndeterminate) =>
                q.SetProgressState(isProcessing, progressValue, isIndeterminate),
            OnQueuedCompletedRemove: folderName =>
            {
                QueuedItems.Remove(q);
                _allCandidates.RemoveAll(
                    c => string.Equals(c.SourceFullPath, q.SourceFullPath, StringComparison.OrdinalIgnoreCase));

                bool folderStillQueued = QueuedItems.Any(
                    other => string.Equals(other.TargetFolderName, folderName, StringComparison.OrdinalIgnoreCase));
                if (!folderStillQueued && !string.IsNullOrWhiteSpace(folderName))
                {
                    RestoreFoldersIncremental(new[] { folderName });
                }

                UpdateSelectionCounts();
                NotifyQueueStateChanged();
            })).ToList();

        await ExecuteBatchEntriesAsync(entries, isDryRun);
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

        var entries = selected.Select(item => new BatchMoveEntry(
            SourceName: item.SourceName,
            SourceFullPath: item.SourceFullPath,
            TargetFolder: item.SelectedTargetFolder,
            DestinationPath: item.DestinationPath,
            IsManuallyAssigned: item.IsManuallyAssigned,
            SetStatus: (status, msg) =>
            {
                item.ExecutionStatus = status;
                item.ExecutionMessage = msg;
            },
            OnMoveSucceeded: _ => item.IsChecked = false)).ToList();

        await ExecuteBatchEntriesAsync(entries, isDryRun);
    }

    private sealed record BatchMoveEntry(
        string SourceName,
        string SourceFullPath,
        string TargetFolder,
        string DestinationPath,
        bool IsManuallyAssigned,
        Action<string, string> SetStatus,
        Action<string> OnMoveSucceeded,
        Action<bool, double, bool>? SetProgress = null,
        Action<string>? OnQueuedCompletedRemove = null);

    private async Task<List<int>> ExecuteBatchEntriesAsync(IReadOnlyList<BatchMoveEntry> entries, bool isDryRun)
    {
        IsBusy = true;
        BatchTotalCount = entries.Count;
        BatchProcessedCount = 0;

        int okCount = 0;
        int skipOrFailCount = 0;
        int processedCount = 0;
        bool cancelledByUser = false;
        var succeededIndices = new List<int>();

        try
        {
            var cfg = _dataContext.Config;
            var mappingsPath = Path.Combine(_dataContext.DataDirectory, "mappings.json");
            var intelligence = _intelligence ?? new MappingIntelligence(mappingsPath);
            var organizer = new ArchiveOrganizer(intelligence, cfg);
            var movedPaths = new List<string>();

            await Task.Run(async () =>
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var item = entries[i];
                    if (string.IsNullOrWhiteSpace(item.TargetFolder) ||
                        string.Equals(item.TargetFolder, "未匹配", StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(item.DestinationPath))
                    {
                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(false, 0, false);
                            item.SetStatus("✕ 略過", "尚未選擇目標資料夾");
                            BatchProcessedCount = ++processedCount;
                        }).ConfigureAwait(false);
                        skipOrFailCount++;
                        continue;
                    }

                    string cleanSource = item.SourceFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string targetPath = Path.Combine(item.DestinationPath, Path.GetFileName(cleanSource));

                    if (!File.Exists(cleanSource) && !Directory.Exists(cleanSource))
                    {
                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(false, 0, false);
                            item.SetStatus("✕ 找不到來源", "來源檔案或資料夾已不存在");
                            BatchProcessedCount = ++processedCount;
                        }).ConfigureAwait(false);
                        skipOrFailCount++;
                        continue;
                    }

                    bool hasConflict = File.Exists(targetPath) || Directory.Exists(targetPath);
                    bool allowOverwrite = false;

                    if (hasConflict)
                    {
                        if (isDryRun)
                        {
                            await _runOnUiAsync(() =>
                            {
                                item.SetProgress?.Invoke(false, 0, false);
                                item.SetStatus("⚠️ 名稱衝突", $"目標目錄已存在同名項目：{Path.GetFileName(targetPath)}");
                                BatchProcessedCount = ++processedCount;
                            }).ConfigureAwait(false);
                            skipOrFailCount++;
                            continue;
                        }

                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(true, 0, true);
                            item.SetStatus("⚠️ 等待確認", $"目標已存在：{Path.GetFileName(targetPath)}");
                        }).ConfigureAwait(false);

                        var conflictInfo = ArchiveConflictInfo.FromPaths(cleanSource, item.TargetFolder, targetPath);
                        var decision = await _conflictPrompt.ConfirmOverwriteAsync(conflictInfo).ConfigureAwait(false);

                        if (decision == ArchiveConflictDecision.Skip)
                        {
                            await _runOnUiAsync(() =>
                            {
                                item.SetProgress?.Invoke(false, 0, false);
                                item.SetStatus("⏭️ 已略過", $"保留目標既有項目：{Path.GetFileName(targetPath)}");
                                BatchProcessedCount = ++processedCount;
                            }).ConfigureAwait(false);
                            skipOrFailCount++;
                            continue;
                        }

                        if (decision == ArchiveConflictDecision.CancelRemaining)
                        {
                            await _runOnUiAsync(() =>
                            {
                                item.SetProgress?.Invoke(false, 0, false);
                                item.SetStatus("⏹️ 已中止", "使用者中止後續歸檔作業");
                            }).ConfigureAwait(false);
                            cancelledByUser = true;
                            break;
                        }

                        allowOverwrite = decision == ArchiveConflictDecision.Overwrite;
                    }

                    if (isDryRun)
                    {
                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(false, 100, false);
                            item.SetStatus("🧪 模擬通過", $"將搬移至 {item.TargetFolder}");
                            BatchProcessedCount = ++processedCount;
                        }).ConfigureAwait(false);
                        okCount++;
                        continue;
                    }

                    try
                    {
                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(true, 0, true);
                            item.SetStatus("⏳ 搬移中", allowOverwrite ? "覆蓋目標並搬移中..." : string.Empty);
                        }).ConfigureAwait(false);

                        double lastReportedProgress = -5.0;
                        string movedPath = ArchiveMoveHelper.MoveItemSafely(
                            item.SourceFullPath,
                            item.DestinationPath,
                            overwrite: allowOverwrite,
                            progressCallback: percent =>
                            {
                                if (item.SetProgress == null)
                                {
                                    return;
                                }

                                if (percent < 100.0 && percent - lastReportedProgress < 2.0)
                                {
                                    return;
                                }

                                lastReportedProgress = percent;
                                _ = _runOnUiAsync(() => item.SetProgress(true, percent, false));
                            });

                        UpsertMovedCatalog(movedPath, item.TargetFolder);

                        var keywords = organizer.ExtractAuthorKeywordsForLearning(item.SourceName);
                        foreach (var kw in keywords)
                        {
                            bool shouldLearn = item.IsManuallyAssigned
                                ? MappingIntelligence.IsValidLearningKey(MappingIntelligence.NormalizeKey(kw))
                                : organizer.IsKeywordValidForTarget(kw, item.TargetFolder);
                            if (shouldLearn)
                            {
                                intelligence.Learn(kw, item.TargetFolder);
                            }
                        }

                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(false, 100, false);
                            item.SetStatus("✓ 已歸檔", movedPath);
                            item.OnMoveSucceeded(movedPath);
                            item.OnQueuedCompletedRemove?.Invoke(item.TargetFolder);
                            BatchProcessedCount = ++processedCount;
                        }).ConfigureAwait(false);

                        movedPaths.Add(movedPath);
                        succeededIndices.Add(i);
                        okCount++;
                    }
                    catch (Exception ex)
                    {
                        await _runOnUiAsync(() =>
                        {
                            item.SetProgress?.Invoke(false, 0, false);
                            item.SetStatus("✕ 失敗", ex.Message);
                            BatchProcessedCount = ++processedCount;
                        }).ConfigureAwait(false);
                        skipOrFailCount++;
                    }
                }
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                RefreshConfigSummary();
                RefreshAfterBatchCheckChange();
                string actionWord = isDryRun
                    ? "模擬測試完成"
                    : (cancelledByUser ? "已中止後續歸檔" : "歸檔搬檔完成");
                StatusMessage = $"{actionWord}：成功 {okCount} 筆，略過／失敗 {skipOrFailCount} 筆";
                ShowToast(
                    actionWord,
                    StatusMessage,
                    cancelledByUser || skipOrFailCount > 0 ? NotificationType.Warning : NotificationType.Success);
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
            await _runOnUiAsync(() =>
            {
                IsBusy = false;
                BatchTotalCount = 0;
                BatchProcessedCount = 0;
            }).ConfigureAwait(false);
        }

        return succeededIndices;
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

    private HashSet<string> GetQueuedTargetFolders()
        => QueuedItems
            .Select(q => q.TargetFolderName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private bool ShouldShowCandidate(AutoArchiveCandidateVm item, string? keyword, IReadOnlySet<string> queuedFolders)
    {
        // 若該作者目標分類已經有項目加入待歸檔清單，則整個分類（含未勾選項目）一起隱藏
        if (item.HasValidDestination && queuedFolders.Contains(item.SelectedTargetFolder))
        {
            return false;
        }

        if (ActiveFilterTab == AutoArchiveFilterTab.CheckedOnly && !item.IsChecked)
        {
            return false;
        }

        if (ActiveFilterTab == AutoArchiveFilterTab.UnmatchedOnly && item.HasValidDestination)
        {
            return false;
        }

        if (keyword != null &&
            !item.SourceName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
            !item.SelectedTargetFolder.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private bool ResolveGroupExpanded(string groupKey, bool isUnmatched)
    {
        if (isUnmatched && ActiveFilterTab != AutoArchiveFilterTab.UnmatchedOnly)
        {
            // 「尚未匹配目標目錄」在大量未歸類檔案掃描時預設收合，避免同時實體化過多 ComboBox 造成卡頓
            return _explicitExpandedGroups.Contains(groupKey);
        }

        return !_collapsedGroupKeys.Contains(groupKey);
    }

    private AutoArchiveTargetGroupVm BuildGroupVm(string groupKey, IReadOnlyList<AutoArchiveCandidateVm> items)
    {
        bool isUnmatched = string.Equals(groupKey, "未匹配", StringComparison.Ordinal);
        var sortedItems = items
            .OrderByDescending(i => i.Confidence)
            .ThenBy(i => i.SourceName, NaturalStringComparer.Instance)
            .ToList();

        return new AutoArchiveTargetGroupVm(
            groupKey,
            isUnmatched,
            sortedItems,
            ResolveGroupExpanded(groupKey, isUnmatched),
            OnGroupCheckBatchChanged,
            OnGroupExpandStateChanged,
            OnAddGroupCheckedToQueue);
    }

    private static int CompareGroups(AutoArchiveTargetGroupVm a, AutoArchiveTargetGroupVm b)
    {
        int unmatchedCmp = (a.IsUnmatchedGroup ? 1 : 0).CompareTo(b.IsUnmatchedGroup ? 1 : 0);
        if (unmatchedCmp != 0)
        {
            return unmatchedCmp;
        }

        int countCmp = b.TotalCount.CompareTo(a.TotalCount);
        if (countCmp != 0)
        {
            return countCmp;
        }

        return NaturalStringComparer.Instance.Compare(a.TargetFolderName, b.TargetFolderName);
    }

    private void InsertGroupSorted(AutoArchiveTargetGroupVm group)
    {
        int insertIndex = 0;
        while (insertIndex < TargetGroups.Count && CompareGroups(TargetGroups[insertIndex], group) <= 0)
        {
            insertIndex++;
        }

        TargetGroups.Insert(insertIndex, group);
    }

    private void HideFoldersIncremental(IReadOnlyCollection<string> folderNames)
    {
        if (folderNames.Count == 0)
        {
            return;
        }

        var folderSet = folderNames as HashSet<string> ?? new HashSet<string>(folderNames, StringComparer.OrdinalIgnoreCase);

        for (int i = TargetGroups.Count - 1; i >= 0; i--)
        {
            var group = TargetGroups[i];
            if (!group.IsUnmatchedGroup && folderSet.Contains(group.TargetFolderName))
            {
                TargetGroups.RemoveAt(i);
            }
        }

        for (int i = VisibleCandidates.Count - 1; i >= 0; i--)
        {
            var item = VisibleCandidates[i];
            if (item.HasValidDestination && folderSet.Contains(item.SelectedTargetFolder))
            {
                VisibleCandidates.RemoveAt(i);
            }
        }

        OnPropertyChanged(nameof(HasCandidates));
    }

    private void RestoreFoldersIncremental(IReadOnlyCollection<string> folderNames)
    {
        if (folderNames.Count == 0)
        {
            return;
        }

        var folderSet = new HashSet<string>(folderNames, StringComparer.OrdinalIgnoreCase);
        var kw = string.IsNullOrWhiteSpace(FilterKeyword) ? null : FilterKeyword.Trim();
        var queuedFolders = GetQueuedTargetFolders();

        var existingVisible = new HashSet<AutoArchiveCandidateVm>(VisibleCandidates);
        int visibleInsertIndex = 0;
        var groupedItems = new Dictionary<string, List<AutoArchiveCandidateVm>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in _allCandidates)
        {
            bool isAlreadyVisible = existingVisible.Contains(candidate);
            if (isAlreadyVisible)
            {
                visibleInsertIndex++;
                continue;
            }

            if (!candidate.HasValidDestination || !folderSet.Contains(candidate.SelectedTargetFolder))
            {
                continue;
            }

            if (!ShouldShowCandidate(candidate, kw, queuedFolders))
            {
                continue;
            }

            VisibleCandidates.Insert(visibleInsertIndex, candidate);
            existingVisible.Add(candidate);
            visibleInsertIndex++;

            if (!groupedItems.TryGetValue(candidate.SelectedTargetFolder, out var bucket))
            {
                bucket = new List<AutoArchiveCandidateVm>();
                groupedItems[candidate.SelectedTargetFolder] = bucket;
            }

            bucket.Add(candidate);
        }

        foreach (var kvp in groupedItems)
        {
            for (int i = TargetGroups.Count - 1; i >= 0; i--)
            {
                if (string.Equals(TargetGroups[i].TargetFolderName, kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    TargetGroups.RemoveAt(i);
                }
            }

            InsertGroupSorted(BuildGroupVm(kvp.Key, kvp.Value));
        }

        OnPropertyChanged(nameof(HasCandidates));
    }

    private void ApplyFilterOnUi()
    {
        VisibleCandidates.Clear();
        TargetGroups.Clear();
        var kw = string.IsNullOrWhiteSpace(FilterKeyword) ? null : FilterKeyword.Trim();
        var queuedFolders = GetQueuedTargetFolders();

        foreach (var item in _allCandidates)
        {
            if (!ShouldShowCandidate(item, kw, queuedFolders))
            {
                continue;
            }

            VisibleCandidates.Add(item);
        }

        var grouped = VisibleCandidates
            .GroupBy(c => c.HasValidDestination ? c.SelectedTargetFolder : "未匹配", StringComparer.Ordinal)
            .Select(g => BuildGroupVm(g.Key, g.ToList()))
            .ToList();
        grouped.Sort(CompareGroups);

        foreach (var group in grouped)
        {
            TargetGroups.Add(group);
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
            _explicitExpandedGroups.Add(groupKey);
        }
        else
        {
            _collapsedGroupKeys.Add(groupKey);
            _explicitExpandedGroups.Remove(groupKey);
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
        var queuedFolders = GetQueuedTargetFolders();
        var active = _allCandidates
            .Where(c => !(c.HasValidDestination && queuedFolders.Contains(c.SelectedTargetFolder)))
            .ToList();

        TotalScannedCount = active.Count;
        MatchedCount = active.Count(c => c.HasValidDestination);
        UnmatchedCount = TotalScannedCount - MatchedCount;
        CheckedCount = active.Count(c => c.IsChecked);
        OnPropertyChanged(nameof(CanSimulate));
        OnPropertyChanged(nameof(CanExecuteRealMove));
        OnPropertyChanged(nameof(CanAddAnyCheckedToQueue));
        AddAllCheckedToQueueCommand.NotifyCanExecuteChanged();
    }

    private void NotifyQueueStateChanged()
    {
        OnPropertyChanged(nameof(QueueCount));
        OnPropertyChanged(nameof(HasQueuedItems));
        OnPropertyChanged(nameof(IsQueueEmpty));
        OnPropertyChanged(nameof(QueueToggleButtonLabel));
        OnPropertyChanged(nameof(QueueHeaderCountText));
        OnPropertyChanged(nameof(CanSimulateQueue));
        OnPropertyChanged(nameof(CanExecuteQueue));
        SimulateQueueCommand.NotifyCanExecuteChanged();
        ExecuteQueueCommand.NotifyCanExecuteChanged();
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

    private void OnInspectQueuedInReader(AutoArchiveQueuedItemVm item)
    {
        if (!_readerLauncher.OpenInReader(item.SourceFullPath, item.SourceName, item.TargetFolderName, null, out var err))
        {
            ShowToast("無法預覽", err ?? item.SourceFullPath, NotificationType.Warning);
        }
    }

    private void OnRevealQueuedInExplorer(AutoArchiveQueuedItemVm item)
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

using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SukiUI.Toasts;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed partial class WorkBasketItemVm : ObservableObject, IDisposable
{
    private readonly Action<WorkBasketItemVm> _onPreview;
    private readonly Action<WorkBasketItemVm> _onOpenInReader;
    private readonly Action<WorkBasketItemVm> _onMoveUp;
    private readonly Action<WorkBasketItemVm> _onMoveDown;
    private readonly Action<WorkBasketItemVm> _onRemove;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly CoverSlotVm _coverSlot;
    private bool _disposed;

    public WorkBasketItemVm(
        WorkBasketEntry entry,
        Action<WorkBasketItemVm> onPreview,
        Action<WorkBasketItemVm> onOpenInReader,
        Action<WorkBasketItemVm> onMoveUp,
        Action<WorkBasketItemVm> onMoveDown,
        Action<WorkBasketItemVm> onRemove,
        Func<Action, Task>? runOnUiAsync = null)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _onPreview = onPreview ?? throw new ArgumentNullException(nameof(onPreview));
        _onOpenInReader = onOpenInReader ?? throw new ArgumentNullException(nameof(onOpenInReader));
        _onMoveUp = onMoveUp ?? throw new ArgumentNullException(nameof(onMoveUp));
        _onMoveDown = onMoveDown ?? throw new ArgumentNullException(nameof(onMoveDown));
        _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        NormalizedPath = WorkBasketEntry.NormalizePathKey(entry.Path);
        AccentColorHex = ExploreVisualFormatting.PickAccentHex(Title);
        FallbackGlyph = ExploreVisualFormatting.ExtractFallbackGlyph(Title);

        var dummyWork = new ExploreWorkItem(
            AssetId: 0,
            Path: entry.Path,
            FileName: System.IO.Path.GetFileName(entry.Path),
            Title: Title,
            AuthorId: null,
            AuthorName: entry.AuthorName,
            Language: entry.Language,
            TranslationGroup: null,
            PageCount: entry.PageCount,
            ReadStatus: ReadStatus.Unread,
            ReadCount: 0,
            LastRead: null,
            AddedAt: 0,
            ExternalId: null,
            CoverThumbKey: entry.CoverThumbKey,
            Tags: Array.Empty<ExploreTagBadge>());

        _coverSlot = new CoverSlotVm(dummyWork, AccentColorHex);
        _coverSlot.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CoverSlotVm.Image))
            {
                OnPropertyChanged(nameof(Cover));
                OnPropertyChanged(nameof(HasCover));
            }
        };
    }

    public WorkBasketEntry Entry { get; }
    public string NormalizedPath { get; }
    public string Path => Entry.Path;
    public string Title => string.IsNullOrWhiteSpace(Entry.Title) ? System.IO.Path.GetFileName(Entry.Path) : Entry.Title;
    public string AuthorName => string.IsNullOrWhiteSpace(Entry.AuthorName) ? "未登錄作者" : Entry.AuthorName;
    public bool HasPageCount => Entry.PageCount is > 0;
    public string PageCountText => HasPageCount ? $"{Entry.PageCount}P" : string.Empty;
    public string AccentColorHex { get; }
    public string FallbackGlyph { get; }

    public string SubtitleText
    {
        get
        {
            var parts = new List<string>(3) { AuthorName };
            if (HasPageCount) parts.Add(PageCountText);
            if (!string.IsNullOrWhiteSpace(Entry.Language)) parts.Add(Entry.Language!);
            return string.Join(" · ", parts);
        }
    }

    public Bitmap? Cover => _coverSlot.Image;
    public bool HasCover => Cover != null;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    public Task EnsureCoverLoadedAsync(SkiaCoverService coverService, SemaphoreSlim limiter, CancellationToken ct)
        => _disposed ? Task.CompletedTask : _coverSlot.EnsureLoadedAsync(coverService, limiter, _runOnUiAsync, ct);

    [RelayCommand]
    private void Preview() => _onPreview(this);

    [RelayCommand]
    private void OpenInReader() => _onOpenInReader(this);

    [RelayCommand]
    private void MoveUp() => _onMoveUp(this);

    [RelayCommand]
    private void MoveDown() => _onMoveDown(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coverSlot.ReleaseImage();
    }
}

/// <summary>
/// 跨頁面共用的「📌 待挑書籃」ViewModel（探索牆與畫廊全模式通用）。
/// 資料僅暫存於記憶體中，關閉程式即清空；除非使用者按「儲存為閱讀清單」寫入 SQLite。
/// </summary>
public sealed partial class WorkBasketViewModel : ViewModelBase, IDisposable
{
    private readonly IAssetMetaStore _store;
    private readonly SkiaCoverService _coverService;
    private readonly IReaderWindowLauncher _readerLauncher;
    private readonly IWorkPreviewLauncher _previewLauncher;
    private readonly IWorkBasketSaveLauncher _saveLauncher;
    private readonly ISukiToastManager? _toastManager;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly SemaphoreSlim _coverSemaphore = new(2, 2);
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _pathSet = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public WorkBasketViewModel(
        AppDataContext dataContext,
        SkiaCoverService coverService,
        IReaderWindowLauncher? readerLauncher = null,
        IWorkPreviewLauncher? previewLauncher = null,
        IWorkBasketSaveLauncher? saveLauncher = null,
        Func<Action, Task>? runOnUiAsync = null)
        : this(
            (dataContext ?? throw new ArgumentNullException(nameof(dataContext))).MetaStore,
            coverService,
            readerLauncher ?? new AvaloniaReaderWindowLauncher(dataContext),
            previewLauncher ?? new AvaloniaWorkPreviewLauncher(),
            saveLauncher ?? new AvaloniaWorkBasketSaveLauncher(),
            dataContext.IsReadOnlyDatabase,
            dataContext.ToastManager,
            runOnUiAsync)
    {
    }

    public WorkBasketViewModel(
        IAssetMetaStore store,
        SkiaCoverService coverService,
        IReaderWindowLauncher readerLauncher,
        IWorkPreviewLauncher previewLauncher,
        bool isReadOnlyDatabase = false,
        ISukiToastManager? toastManager = null,
        Func<Action, Task>? runOnUiAsync = null)
        : this(
            store,
            coverService,
            readerLauncher,
            previewLauncher,
            new AvaloniaWorkBasketSaveLauncher(),
            isReadOnlyDatabase,
            toastManager,
            runOnUiAsync)
    {
    }

    public WorkBasketViewModel(
        IAssetMetaStore store,
        SkiaCoverService coverService,
        IReaderWindowLauncher readerLauncher,
        IWorkPreviewLauncher previewLauncher,
        IWorkBasketSaveLauncher saveLauncher,
        bool isReadOnlyDatabase = false,
        ISukiToastManager? toastManager = null,
        Func<Action, Task>? runOnUiAsync = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
        _readerLauncher = readerLauncher ?? throw new ArgumentNullException(nameof(readerLauncher));
        _previewLauncher = previewLauncher ?? throw new ArgumentNullException(nameof(previewLauncher));
        _saveLauncher = saveLauncher ?? throw new ArgumentNullException(nameof(saveLauncher));
        IsReadOnlyDatabase = isReadOnlyDatabase;
        _toastManager = toastManager;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;
    }

    public bool IsReadOnlyDatabase { get; }

    public ObservableCollection<WorkBasketItemVm> Items { get; } = [];

    /// <summary>書籃成員變動時觸發：(NormalizedPath, IsAdded)；Clear 時會以 (string.Empty, false) 通知。</summary>
    public event Action<string, bool>? MembershipChanged;

    /// <summary>書籃儲存成永久閱讀清單後觸發，供畫廊「⭐ 清單」即時刷新。</summary>
    public event Action? VirtualLibrarySaved;

    [ObservableProperty]
    private bool _isOpen;
    public int Count => Items.Count;
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public string HeaderCountText => $"({Items.Count})";
    public string ToggleButtonLabel => Items.Count > 0 ? $"📌 待挑書籃 ({Items.Count})" : "📌 待挑書籃";

    public bool ContainsPath(string? path)
    {
        var key = WorkBasketEntry.NormalizePathKey(path);
        return !string.IsNullOrEmpty(key) && _pathSet.Contains(key);
    }

    public bool Toggle(WorkBasketEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (ContainsPath(entry.Path))
        {
            RemoveByPath(entry.Path);
            return false;
        }

        return Add(entry);
    }

    public bool Add(WorkBasketEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = WorkBasketEntry.NormalizePathKey(entry.Path);
        if (string.IsNullOrEmpty(key) || !_pathSet.Add(key))
        {
            return false;
        }

        var vm = new WorkBasketItemVm(
            entry,
            onPreview: OpenPreviewForItem,
            onOpenInReader: OpenInReader,
            onMoveUp: MoveUp,
            onMoveDown: MoveDown,
            onRemove: RemoveItem,
            runOnUiAsync: _runOnUiAsync);

        Items.Add(vm);
        UpdateReorderFlags();
        NotifyCountChanged();
        MembershipChanged?.Invoke(key, true);
        _ = vm.EnsureCoverLoadedAsync(_coverService, _coverSemaphore, _cts.Token);
        return true;
    }

    public bool RemoveByPath(string? path)
    {
        var key = WorkBasketEntry.NormalizePathKey(path);
        if (string.IsNullOrEmpty(key) || !_pathSet.Remove(key))
        {
            return false;
        }

        for (int i = 0; i < Items.Count; i++)
        {
            if (string.Equals(Items[i].NormalizedPath, key, StringComparison.OrdinalIgnoreCase))
            {
                var target = Items[i];
                Items.RemoveAt(i);
                target.Dispose();
                break;
            }
        }

        UpdateReorderFlags();
        NotifyCountChanged();
        MembershipChanged?.Invoke(key, false);
        return true;
    }

    public void OpenPreviewForEntry(WorkBasketEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_previewLauncher.OpenPreview(entry, this, _readerLauncher, out var error))
        {
            ShowToast("無法開啟作品預覽", error ?? entry.Path, NotificationType.Warning);
        }
    }

    private void OpenPreviewForItem(WorkBasketItemVm item) => OpenPreviewForEntry(item.Entry);

    private void OpenInReader(WorkBasketItemVm item)
    {
        // 從書籃開啟閱讀後維持項目於書籃內，不自動移除
        if (!_readerLauncher.OpenInReader(
                item.Path,
                item.Title,
                item.AuthorName == "未登錄作者" ? null : item.AuthorName,
                onReaderClosed: null,
                out var error))
        {
            ShowToast("無法開啟內建閱讀器", error ?? item.Path, NotificationType.Warning);
        }
    }

    [RelayCommand]
    private void OpenAllInReader()
    {
        if (Items.Count == 0)
        {
            ShowToast("書籃是空的", "請先將想看的作品加入書籃。", NotificationType.Information);
            return;
        }

        int failed = 0;
        string? lastError = null;
        foreach (var item in Items.ToList())
        {
            if (!_readerLauncher.OpenInReader(
                    item.Path,
                    item.Title,
                    item.AuthorName == "未登錄作者" ? null : item.AuthorName,
                    onReaderClosed: null,
                    out var error))
            {
                failed++;
                lastError = error ?? item.Path;
            }
        }

        if (failed > 0)
        {
            ShowToast(
                "部分作品無法開啟閱讀器",
                $"共 {failed} 本開啟失敗（{lastError}）。",
                NotificationType.Warning);
        }
    }

    private void RemoveItem(WorkBasketItemVm item) => RemoveByPath(item.Path);

    private void MoveUp(WorkBasketItemVm item)
    {
        int idx = Items.IndexOf(item);
        if (idx <= 0) return;
        Items.Move(idx, idx - 1);
        UpdateReorderFlags();
    }

    private void MoveDown(WorkBasketItemVm item)
    {
        int idx = Items.IndexOf(item);
        if (idx < 0 || idx >= Items.Count - 1) return;
        Items.Move(idx, idx + 1);
        UpdateReorderFlags();
    }

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Clear()
    {
        if (Items.Count == 0) return;
        foreach (var item in Items)
        {
            item.Dispose();
        }
        Items.Clear();
        _pathSet.Clear();
        NotifyCountChanged();
        MembershipChanged?.Invoke(string.Empty, false);
    }

    public void RefreshSaveTargets()
    {
        // 既有清單清單改成點擊「儲存至清單」開啟確認對話框時即時讀取
    }

    [RelayCommand]
    private void RequestSaveToLibrary()
    {
        if (Items.Count == 0)
        {
            ShowToast("書籃是空的", "請先從畫廊或探索牆加入想保存的作品。", NotificationType.Information);
            return;
        }

        if (IsReadOnlyDatabase)
        {
            ShowToast("共用庫唯讀模式", "目前無法寫入永久閱讀清單，請以可寫模式啟動後再儲存。", NotificationType.Warning);
            return;
        }

        IReadOnlyList<VirtualLibrary> existingLibs;
        try
        {
            existingLibs = _store.ListVirtualLibraries();
        }
        catch
        {
            existingLibs = Array.Empty<VirtualLibrary>();
        }

        var dialogVm = new WorkBasketSaveViewModel(
            Items.Count,
            existingLibs,
            CommitSaveToLibrary);
        _saveLauncher.OpenSaveDialog(dialogVm);
    }

    internal (bool Ok, string? Error) CommitSaveToLibrary(bool createNew, string? newLibraryName, VirtualLibrary? existingLibrary)
    {
        if (Items.Count == 0)
        {
            return (false, "書籃目前沒有作品。");
        }

        if (IsReadOnlyDatabase)
        {
            return (false, "共用庫唯讀模式無法寫入書單。");
        }

        long targetLibraryId;
        string targetName;
        if (createNew)
        {
            targetName = string.IsNullOrWhiteSpace(newLibraryName)
                ? $"待挑精選 {DateTime.Now:yyyy-MM-dd HH:mm}"
                : newLibraryName.Trim();
            targetLibraryId = _store.CreateVirtualLibrary(targetName, string.Empty, null);
        }
        else
        {
            if (existingLibrary == null)
            {
                return (false, "請先選擇要加入的既有書單。");
            }

            var current = _store.GetVirtualLibrary(existingLibrary.Id);
            if (current == null)
            {
                return (false, "找不到指定的既有書單（可能已被刪除）。");
            }

            targetLibraryId = current.Id;
            targetName = current.Name;
        }

        int savedCount = 0;
        foreach (var item in Items)
        {
            if (_store.AddWorkToVirtualLibrary(targetLibraryId, item.Path))
            {
                savedCount++;
            }
        }

        VirtualLibrarySaved?.Invoke();
        ShowToast("已儲存至「⭐ 清單」", $"已將 {savedCount} 部作品寫入「{targetName}」。", NotificationType.Success);
        return (true, null);
    }

    private void UpdateReorderFlags()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].CanMoveUp = i > 0;
            Items[i].CanMoveDown = i < Items.Count - 1;
        }
    }

    private void NotifyCountChanged()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HeaderCountText));
        OnPropertyChanged(nameof(ToggleButtonLabel));
    }

    private void ShowToast(string title, string message, NotificationType type)
    {
        if (global::Avalonia.Application.Current == null || _toastManager == null)
        {
            return;
        }

        try
        {
            _toastManager.CreateToast()
                .WithTitle(title)
                .WithContent(message)
                .OfType(type)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        foreach (var item in Items)
        {
            item.Dispose();
        }
        Items.Clear();
        _pathSet.Clear();
        _coverSemaphore.Dispose();
    }
}

public sealed partial class WorkBasketSaveViewModel : ViewModelBase
{
    private readonly Func<bool, string?, VirtualLibrary?, (bool Ok, string? Error)> _onConfirm;

    public WorkBasketSaveViewModel(
        int itemCount,
        IReadOnlyList<VirtualLibrary> existingLibraries,
        Func<bool, string?, VirtualLibrary?, (bool Ok, string? Error)> onConfirm)
    {
        ItemCount = itemCount;
        SummaryText = $"將把書籃內共 {itemCount} 部作品加入「⭐ 我的閱讀清單」（寫入後即使關閉程式也會保留）。";
        _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));

        foreach (var lib in existingLibraries)
        {
            ExistingLibraries.Add(lib);
        }

        _selectedExistingLibrary = ExistingLibraries.FirstOrDefault();
    }

    public int ItemCount { get; }
    public string SummaryText { get; }
    public ObservableCollection<VirtualLibrary> ExistingLibraries { get; } = [];
    public bool CanMergeExisting => ExistingLibraries.Count > 0;

    public event Action<bool>? RequestClose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMergeExistingMode))]
    private bool _isCreateNewMode = true;

    public bool IsMergeExistingMode
    {
        get => !IsCreateNewMode;
        set
        {
            if (value && CanMergeExisting)
            {
                IsCreateNewMode = false;
            }
        }
    }

    [ObservableProperty]
    private string _newLibraryName = string.Empty;

    [ObservableProperty]
    private VirtualLibrary? _selectedExistingLibrary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string? _validationError;

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    [RelayCommand]
    private void ConfirmSave()
    {
        ValidationError = null;
        var (ok, error) = _onConfirm(IsCreateNewMode, NewLibraryName, SelectedExistingLibrary);
        if (!ok)
        {
            ValidationError = error ?? "儲存失敗。";
            return;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}

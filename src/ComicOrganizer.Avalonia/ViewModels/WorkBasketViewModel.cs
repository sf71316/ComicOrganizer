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
        Func<Action, Task>? runOnUiAsync = null)
        : this(
            (dataContext ?? throw new ArgumentNullException(nameof(dataContext))).MetaStore,
            coverService,
            readerLauncher ?? new AvaloniaReaderWindowLauncher(dataContext),
            previewLauncher ?? new AvaloniaWorkPreviewLauncher(),
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
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coverService = coverService ?? throw new ArgumentNullException(nameof(coverService));
        _readerLauncher = readerLauncher ?? throw new ArgumentNullException(nameof(readerLauncher));
        _previewLauncher = previewLauncher ?? throw new ArgumentNullException(nameof(previewLauncher));
        IsReadOnlyDatabase = isReadOnlyDatabase;
        _toastManager = toastManager;
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        _selectedSaveTarget = new VirtualLibraryTargetOption(null, "＋ 建立新書單…");
        RefreshSaveTargets();
    }

    public bool IsReadOnlyDatabase { get; }

    public ObservableCollection<WorkBasketItemVm> Items { get; } = [];

    public ObservableCollection<VirtualLibraryTargetOption> SaveTargets { get; } = [];

    /// <summary>書籃成員變動時觸發：(NormalizedPath, IsAdded)；Clear 時會以 (string.Empty, false) 通知。</summary>
    public event Action<string, bool>? MembershipChanged;

    /// <summary>書籃儲存成永久閱讀清單後觸發，供畫廊「⭐ 清單」即時刷新。</summary>
    public event Action? VirtualLibrarySaved;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _autoRemoveAfterRead = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreatingNewLibrary))]
    private VirtualLibraryTargetOption _selectedSaveTarget;

    [ObservableProperty]
    private string _newLibraryName = string.Empty;

    public bool IsCreatingNewLibrary => SelectedSaveTarget?.LibraryId is null;
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
        bool autoRemove = AutoRemoveAfterRead;
        string path = item.Path;
        Action? onClosed = autoRemove
            ? () => _ = _runOnUiAsync(() => RemoveByPath(path))
            : null;

        if (!_readerLauncher.OpenInReader(
                item.Path,
                item.Title,
                item.AuthorName == "未登錄作者" ? null : item.AuthorName,
                onClosed,
                out var error))
        {
            ShowToast("無法開啟內建閱讀器", error ?? item.Path, NotificationType.Warning);
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
        long? prevId = SelectedSaveTarget?.LibraryId;
        var list = new List<VirtualLibraryTargetOption>
        {
            new(null, "＋ 建立新書單…"),
        };

        try
        {
            foreach (var lib in _store.ListVirtualLibraries())
            {
                list.Add(new VirtualLibraryTargetOption(lib.Id, $"併入：{lib.Name}"));
            }
        }
        catch
        {
            // 讀取書單失敗時保留建立新書單選項
        }

        SaveTargets.Clear();
        foreach (var opt in list)
        {
            SaveTargets.Add(opt);
        }

        SelectedSaveTarget = (prevId.HasValue
            ? list.FirstOrDefault(o => o.LibraryId == prevId.Value)
            : null) ?? list[0];
    }

    [RelayCommand]
    private void SaveToLibrary()
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

        long targetLibraryId;
        string targetName;
        if (SelectedSaveTarget?.LibraryId is long existingId)
        {
            var existing = _store.GetVirtualLibrary(existingId);
            if (existing == null)
            {
                ShowToast("找不到目標書單", "該閱讀清單可能已被刪除，請重新選擇。", NotificationType.Warning);
                RefreshSaveTargets();
                return;
            }
            targetLibraryId = existing.Id;
            targetName = existing.Name;
        }
        else
        {
            targetName = string.IsNullOrWhiteSpace(NewLibraryName)
                ? $"待挑精選 {DateTime.Now:yyyy-MM-dd HH:mm}"
                : NewLibraryName.Trim();
            targetLibraryId = _store.CreateVirtualLibrary(targetName, string.Empty, null);
        }

        int savedCount = 0;
        foreach (var item in Items)
        {
            if (_store.AddWorkToVirtualLibrary(targetLibraryId, item.Path))
            {
                savedCount++;
            }
        }

        NewLibraryName = string.Empty;
        RefreshSaveTargets();
        SelectedSaveTarget = SaveTargets.FirstOrDefault(t => t.LibraryId == targetLibraryId) ?? SaveTargets[0];
        VirtualLibrarySaved?.Invoke();
        ShowToast("已儲存至「⭐ 清單」", $"已將 {savedCount} 部作品寫入「{targetName}」。", NotificationType.Success);
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

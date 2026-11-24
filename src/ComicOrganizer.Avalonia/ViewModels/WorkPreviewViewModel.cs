using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media.Imaging;
using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed partial class WorkPreviewThumbVm : ObservableObject, IDisposable
{
    private readonly Action<WorkPreviewThumbVm> _onSelect;

    public WorkPreviewThumbVm(
        int pageIndexZeroBased,
        int totalPages,
        Action<WorkPreviewThumbVm> onSelect)
    {
        PageIndexZeroBased = pageIndexZeroBased;
        PageLabel = $"第 {pageIndexZeroBased + 1} / {totalPages} 頁";
        _onSelect = onSelect;
    }

    public int PageIndexZeroBased { get; }
    public string PageLabel { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isZoomed;

    public bool HasThumbnail => Thumbnail != null;

    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

    [RelayCommand]
    private void Select() => _onSelect(this);

    public void Dispose()
    {
        var old = Thumbnail;
        Thumbnail = null;
        old?.Dispose();
    }
}

/// <summary>
/// 作品多圖快速預覽小視窗 ViewModel：
/// 不啟動完整閱讀器，直接背景讀取來源作品的前 6 頁或全本均勻抽 6 頁縮圖，並支援單頁點擊放大、加入／移出書籃與正式開啟閱讀。
/// </summary>
public sealed partial class WorkPreviewViewModel : ViewModelBase, IDisposable
{
    private readonly WorkBasketEntry _entry;
    private readonly WorkBasketViewModel _basket;
    private readonly IReaderWindowLauncher _readerLauncher;
    private readonly Func<Action, Task> _runOnUiAsync;
    private readonly object _loadLock = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _zoomCts;
    private bool _disposed;

    public WorkPreviewViewModel(
        WorkBasketEntry entry,
        WorkBasketViewModel basket,
        IReaderWindowLauncher readerLauncher,
        Func<Action, Task>? runOnUiAsync = null)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _basket = basket ?? throw new ArgumentNullException(nameof(basket));
        _readerLauncher = readerLauncher ?? throw new ArgumentNullException(nameof(readerLauncher));
        _runOnUiAsync = runOnUiAsync ?? ExploreCardVmBase.DefaultRunOnUiAsync;

        _basket.Items.CollectionChanged += OnBasketCollectionChanged;
        _isInBasket = _basket.ContainsPath(_entry.Path);
        StartLoadSampleAsync();
    }

    public ObservableCollection<WorkPreviewThumbVm> PreviewPages { get; } = [];

    public event Action? RequestClose;

    public string Title => string.IsNullOrWhiteSpace(_entry.Title)
        ? System.IO.Path.GetFileName(_entry.Path)
        : _entry.Title;

    public string Subtitle => string.IsNullOrWhiteSpace(_entry.AuthorName)
        ? _entry.Path
        : $"{_entry.AuthorName} · {(_entry.PageCount is > 0 ? $"{_entry.PageCount}P" : System.IO.Path.GetFileName(_entry.Path))}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFrontSampleMode))]
    [NotifyPropertyChangedFor(nameof(IsUniformSampleMode))]
    private WorkPreviewSampleMode _sampleMode = WorkPreviewSampleMode.FrontPages;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _resolvedTotalPages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BasketActionText))]
    private bool _isInBasket;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasZoomPage))]
    private Bitmap? _zoomBitmap;

    [ObservableProperty]
    private string _zoomCaption = string.Empty;

    [ObservableProperty]
    private bool _isZoomLoading;

    private int? _zoomedPageIndex;

    public bool IsFrontSampleMode => SampleMode == WorkPreviewSampleMode.FrontPages;
    public bool IsUniformSampleMode => SampleMode == WorkPreviewSampleMode.UniformSample;
    public bool HasZoomPage => ZoomBitmap != null || IsZoomLoading;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string BasketActionText => IsInBasket ? "✓ 已在書籃（點此移出）" : "📌 加入待挑書籃";

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void OnBasketCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_disposed)
        {
            IsInBasket = _basket.ContainsPath(_entry.Path);
        }
    }

    [RelayCommand]
    private void SelectFrontSample()
    {
        if (SampleMode == WorkPreviewSampleMode.FrontPages) return;
        SampleMode = WorkPreviewSampleMode.FrontPages;
        CollapseZoom();
        StartLoadSampleAsync();
    }

    [RelayCommand]
    private void SelectUniformSample()
    {
        if (SampleMode == WorkPreviewSampleMode.UniformSample) return;
        SampleMode = WorkPreviewSampleMode.UniformSample;
        CollapseZoom();
        StartLoadSampleAsync();
    }

    [RelayCommand]
    private void ToggleBasket()
    {
        _basket.Toggle(_entry);
        IsInBasket = _basket.ContainsPath(_entry.Path);
    }

    [RelayCommand]
    private void OpenInReader()
    {
        string author = string.IsNullOrWhiteSpace(_entry.AuthorName) || _entry.AuthorName == "未登錄作者"
            ? string.Empty
            : _entry.AuthorName;
        if (!_readerLauncher.OpenInReader(
                _entry.Path,
                Title,
                string.IsNullOrEmpty(author) ? null : author,
                onReaderClosed: null,
                out var error))
        {
            ErrorMessage = error ?? "無法開啟閱讀器。";
            return;
        }

        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void CollapseZoom()
    {
        CancelAndDisposeCts(ref _zoomCts);
        _zoomedPageIndex = null;
        IsZoomLoading = false;
        var old = ZoomBitmap;
        ZoomBitmap = null;
        old?.Dispose();
        ZoomCaption = string.Empty;
        foreach (var p in PreviewPages)
        {
            p.IsZoomed = false;
        }
    }

    [RelayCommand]
    private void CloseWindow() => RequestClose?.Invoke();

    public Task LoadingTask { get; private set; } = Task.CompletedTask;

    private void StartLoadSampleAsync()
    {
        var cts = ReplaceCts(ref _loadCts);
        var mode = SampleMode;
        var ct = cts.Token;

        ClearPreviewPages();
        ErrorMessage = null;
        IsLoading = true;

        LoadingTask = Task.Run(async () =>
        {
            try
            {
                using var source = ReaderSourceFactory.Open(_entry.Path, out _);
                int total = source.PageCount;
                if (total <= 0)
                {
                    await _runOnUiAsync(() =>
                    {
                        if (ct.IsCancellationRequested || _disposed) return;
                        ResolvedTotalPages = 0;
                        IsLoading = false;
                        ErrorMessage = "此作品內找不到可預覽的圖片頁。";
                    }).ConfigureAwait(false);
                    return;
                }

                var sampleIndices = WorkPreviewPageSampler.ComputeSamplePageIndices(total, mode, maxSamples: 6);
                var thumbVms = sampleIndices
                    .Select(idx => new WorkPreviewThumbVm(idx, total, OnThumbSelected))
                    .ToList();

                await _runOnUiAsync(() =>
                {
                    if (ct.IsCancellationRequested || _disposed)
                    {
                        foreach (var t in thumbVms) t.Dispose();
                        return;
                    }
                    ResolvedTotalPages = total;
                    PreviewPages.Clear();
                    foreach (var t in thumbVms)
                    {
                        PreviewPages.Add(t);
                    }
                }).ConfigureAwait(false);

                foreach (var vm in thumbVms)
                {
                    if (ct.IsCancellationRequested || _disposed) break;
                    var bmp = DecodeScaledPageBitmap(source, vm.PageIndexZeroBased, maxW: 320, maxH: 440, quality: 82);
                    await _runOnUiAsync(() =>
                    {
                        if (ct.IsCancellationRequested || _disposed)
                        {
                            bmp?.Dispose();
                            return;
                        }
                        var prev = vm.Thumbnail;
                        vm.Thumbnail = bmp;
                        prev?.Dispose();
                    }).ConfigureAwait(false);
                }

                await _runOnUiAsync(() =>
                {
                    if (!ct.IsCancellationRequested && !_disposed)
                    {
                        IsLoading = false;
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
                    if (!ct.IsCancellationRequested && !_disposed)
                    {
                        IsLoading = false;
                        ErrorMessage = $"預覽載入失敗：{ex.Message}";
                    }
                }).ConfigureAwait(false);
            }
        }, ct);
    }

    private void OnThumbSelected(WorkPreviewThumbVm thumb)
    {
        if (_disposed) return;
        if (_zoomedPageIndex == thumb.PageIndexZeroBased)
        {
            CollapseZoom();
            return;
        }

        var cts = ReplaceCts(ref _zoomCts);
        var ct = cts.Token;
        _zoomedPageIndex = thumb.PageIndexZeroBased;
        foreach (var p in PreviewPages)
        {
            p.IsZoomed = p.PageIndexZeroBased == thumb.PageIndexZeroBased;
        }

        ZoomCaption = $"🔍 {thumb.PageLabel} 放大檢視";
        IsZoomLoading = true;

        _ = Task.Run(async () =>
        {
            try
            {
                using var source = ReaderSourceFactory.Open(_entry.Path, out _);
                var bmp = DecodeScaledPageBitmap(source, thumb.PageIndexZeroBased, maxW: 1100, maxH: 1400, quality: 90);
                await _runOnUiAsync(() =>
                {
                    if (ct.IsCancellationRequested || _disposed)
                    {
                        bmp?.Dispose();
                        return;
                    }
                    var prev = ZoomBitmap;
                    ZoomBitmap = bmp;
                    prev?.Dispose();
                    IsZoomLoading = false;
                }).ConfigureAwait(false);
            }
            catch
            {
                await _runOnUiAsync(() =>
                {
                    if (!ct.IsCancellationRequested && !_disposed)
                    {
                        IsZoomLoading = false;
                    }
                }).ConfigureAwait(false);
            }
        }, ct);
    }

    private static Bitmap? DecodeScaledPageBitmap(IReaderSource source, int pageIndex, int maxW, int maxH, int quality)
    {
        try
        {
            using var decoded = SkiaPageRenderer.DecodeFromSource(source, pageIndex);
            if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                return null;
            }

            float scale = Math.Min(1f, Math.Min((float)maxW / decoded.Width, (float)maxH / decoded.Height));
            int outW = Math.Max(1, (int)Math.Round(decoded.Width * scale));
            int outH = Math.Max(1, (int)Math.Round(decoded.Height * scale));

            var info = new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface == null)
            {
                return null;
            }

            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(
                decoded,
                new SKRect(0, 0, outW, outH),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            surface.Canvas.Flush();

            using var snap = surface.Snapshot();
            using var data = snap.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (data == null)
            {
                return null;
            }

            using var ms = new MemoryStream(data.ToArray(), writable: false);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private void ClearPreviewPages()
    {
        foreach (var item in PreviewPages)
        {
            item.Dispose();
        }
        PreviewPages.Clear();
    }

    private CancellationTokenSource ReplaceCts(ref CancellationTokenSource? target)
    {
        var next = new CancellationTokenSource();
        CancellationTokenSource? prev;
        lock (_loadLock)
        {
            prev = target;
            target = next;
        }
        if (prev != null)
        {
            try { prev.Cancel(); } catch { }
            prev.Dispose();
        }
        return next;
    }

    private void CancelAndDisposeCts(ref CancellationTokenSource? target)
    {
        CancellationTokenSource? cur;
        lock (_loadLock)
        {
            cur = target;
            target = null;
        }
        if (cur != null)
        {
            try { cur.Cancel(); } catch { }
            cur.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _basket.Items.CollectionChanged -= OnBasketCollectionChanged;
        CancelAndDisposeCts(ref _loadCts);
        CancelAndDisposeCts(ref _zoomCts);
        ClearPreviewPages();
        var oldZoom = ZoomBitmap;
        ZoomBitmap = null;
        oldZoom?.Dispose();
    }
}

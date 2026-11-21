using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views;

public partial class GalleryPageView : UserControl
{
    private GalleryPageViewModel? _boundViewModel;
    private bool _viewportUpdateScheduled;
    private double _lastAutoLoadExtentHeight = -1;

    public GalleryPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        AttachViewModel();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachViewModel();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachViewModel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 保留頁面（KeepAlivePageHost）切回本頁時重新計算可見範圍；隱藏期間版面不會更新
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            ScheduleViewportRangeUpdate();
        }
    }

    private void AttachViewModel()
    {
        if (DataContext is not GalleryPageViewModel vm)
        {
            return;
        }

        if (!ReferenceEquals(_boundViewModel, vm))
        {
            _boundViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ScrollResetRequested += OnScrollResetRequested;
        }

        _lastAutoLoadExtentHeight = -1;
        SyncCardSizeResources(vm);
        ScheduleViewportRangeUpdate();
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.ScrollResetRequested -= OnScrollResetRequested;
            _boundViewModel = null;
        }
    }

    private void OnScrollResetRequested()
    {
        _lastAutoLoadExtentHeight = -1;
        GalleryScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, Vector.Zero);
        ScheduleViewportRangeUpdate();
    }

    private void OnGalleryViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ScheduleViewportRangeUpdate();
    }

    private void OnBackToTopClick(object? sender, RoutedEventArgs e)
    {
        GalleryScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, Vector.Zero);
        ScheduleViewportRangeUpdate();
    }

    private void OnGalleryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_boundViewModel is null || sender is not ScrollViewer sv)
        {
            return;
        }

        if (_boundViewModel.HasMoreItems &&
            (sv.Offset.Y > 24 || e.OffsetDelta.Y > 0))
        {
            double maxOffsetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            double preloadThreshold = Math.Max(680, sv.Viewport.Height * 1.5);
            if (maxOffsetY > 0 &&
                sv.Offset.Y >= maxOffsetY - preloadThreshold &&
                Math.Abs(sv.Extent.Height - _lastAutoLoadExtentHeight) > 1.0)
            {
                _lastAutoLoadExtentHeight = sv.Extent.Height;
                _boundViewModel.LoadMoreItemsCommand.Execute(null);
            }
        }

        ScheduleViewportRangeUpdate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GalleryPageViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(GalleryPageViewModel.ViewMode)
            or nameof(GalleryPageViewModel.CardWidth)
            or nameof(GalleryPageViewModel.CardCoverHeight))
        {
            SyncCardSizeResources(vm);
            _lastAutoLoadExtentHeight = -1;
            ScheduleViewportRangeUpdate();
        }
        else if (e.PropertyName is nameof(GalleryPageViewModel.SidebarMode)
                 or nameof(GalleryPageViewModel.CurrentFolderPath))
        {
            _lastAutoLoadExtentHeight = -1;
            ScheduleViewportRangeUpdate();
        }
        else if (e.PropertyName is nameof(GalleryPageViewModel.CurrentItemCount)
                 or nameof(GalleryPageViewModel.IsLoading))
        {
            ScheduleViewportRangeUpdate();
        }
    }

    private void SyncCardSizeResources(GalleryPageViewModel vm)
    {
        Resources["GalleryCardWidth"] = vm.CardWidth;
        Resources["GalleryCardCoverHeight"] = vm.CardCoverHeight;
    }

    private void ScheduleViewportRangeUpdate()
    {
        if (_viewportUpdateScheduled || _boundViewModel is null)
        {
            return;
        }

        _viewportUpdateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _viewportUpdateScheduled = false;
            UpdateViewportRangeImmediate();
        }, DispatcherPriority.Background);
    }

    private void UpdateViewportRangeImmediate()
    {
        if (_boundViewModel is null || GalleryScrollViewer is null)
        {
            return;
        }

        var vm = _boundViewModel;
        int loadedCount = vm.CurrentItemCount;
        if (loadedCount <= 0)
        {
            vm.UpdateViewportRange(0, 0, 0);
            return;
        }

        ItemsControl? activeControl = vm.IsDetailListMode
            ? DetailListItemsControl
            : CardGridItemsControl;

        double viewportHeight = GalleryScrollViewer.Viewport.Height;
        double offsetY = GalleryScrollViewer.Offset.Y;

        if (activeControl?.ItemsPanelRoot is Panel panel && panel.Children.Count > 0 && viewportHeight > 0)
        {
            var origin = panel.TranslatePoint(new Point(0, 0), GalleryScrollViewer);
            if (origin.HasValue)
            {
                int first = int.MaxValue;
                int last = 0;

                for (int i = 0; i < panel.Children.Count; i++)
                {
                    var bounds = panel.Children[i].Bounds;
                    if (bounds.Height <= 0)
                    {
                        continue;
                    }

                    double top = origin.Value.Y + bounds.Top;
                    double bottom = origin.Value.Y + bounds.Bottom;
                    if (bottom > 4 && top < viewportHeight - 4)
                    {
                        int oneBased = i + 1;
                        if (oneBased < first) first = oneBased;
                        if (oneBased > last) last = oneBased;
                    }
                }

                if (last > 0 && first <= last)
                {
                    vm.UpdateViewportRange(first, last, offsetY);
                    return;
                }
            }
        }

        vm.UpdateViewportRange(1, Math.Min(loadedCount, 12), offsetY);
    }

    private void OnItemEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0)
        {
            return;
        }

        if (_boundViewModel is not null && sender is Control { DataContext: GalleryItemVm item })
        {
            _ = _boundViewModel.RequestItemCoverAsync(item);
        }
    }
}

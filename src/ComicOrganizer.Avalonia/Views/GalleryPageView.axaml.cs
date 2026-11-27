using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class GalleryPageView : UserControl
{
    private GalleryPageViewModel? _boundViewModel;
    private bool _viewportUpdateScheduled;

    public GalleryPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ScrollWheelStepHelper.Attach(
            GalleryScrollViewer,
            () => _boundViewModel?.WheelScrollStepDip ?? ScrollWheelStepHelper.DefaultWheelScrollDip);
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

        SyncCardSizeResources(vm);
        SyncActiveRepeater(vm);
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
        if (_boundViewModel is null)
        {
            return;
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
            SyncActiveRepeater(vm);
            ScheduleViewportRangeUpdate();
        }
        else if (e.PropertyName is nameof(GalleryPageViewModel.SidebarMode)
                 or nameof(GalleryPageViewModel.CurrentFolderPath))
        {
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
        Resources["GalleryCardCoverWidth"] = vm.CardCoverWidth;

        // UniformGridLayout 會記住第一張卡片量到的格子大小；卡片尺寸改變時換一個新的版面，強制重新量測
        if (CardGridRepeater is not null)
        {
            CardGridRepeater.Layout = CreateCardGridLayout();
        }
    }

    /// <summary>
    /// 只讓目前模式的清單綁定 Items；隱藏的清單清空 ItemsSource，
    /// 它建立過的卡片會觸發 ElementClearing 回收，封面也跟著進入回收佇列。
    /// </summary>
    private void SyncActiveRepeater(GalleryPageViewModel vm)
    {
        ItemsRepeater? active = vm.IsDetailListMode ? DetailListRepeater : CardGridRepeater;
        ItemsRepeater? inactive = vm.IsDetailListMode ? CardGridRepeater : DetailListRepeater;

        if (inactive is not null && inactive.ItemsSource is not null)
        {
            inactive.ItemsSource = null;
        }

        if (active is not null && !ReferenceEquals(active.ItemsSource, vm.Items))
        {
            active.ItemsSource = vm.Items;
        }
    }

    private static UniformGridLayout CreateCardGridLayout()
    {
        return new UniformGridLayout
        {
            Orientation = Orientation.Horizontal,
            ItemsJustification = UniformGridLayoutItemsJustification.Start,
            ItemsStretch = UniformGridLayoutItemsStretch.None,
        };
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

        double viewportHeight = GalleryScrollViewer.Viewport.Height;
        double offsetY = GalleryScrollViewer.Offset.Y;

        ItemsRepeater? activeRepeater = vm.IsDetailListMode ? DetailListRepeater : CardGridRepeater;
        if (TryUpdateViewportRangeFromRepeater(vm, activeRepeater, viewportHeight, offsetY))
        {
            return;
        }

        vm.UpdateViewportRange(1, Math.Min(loadedCount, 12), offsetY);
    }

    /// <summary>
    /// 虛擬化清單：只掃描 ItemsRepeater 目前建立的卡片，以 <see cref="ItemsRepeater.GetElementIndex"/> 取得實際索引。
    /// 回收中的卡片索引為 -1，直接略過。
    /// </summary>
    private bool TryUpdateViewportRangeFromRepeater(
        GalleryPageViewModel vm,
        ItemsRepeater? repeater,
        double viewportHeight,
        double offsetY)
    {
        if (repeater is null || viewportHeight <= 0 || repeater.Children.Count == 0)
        {
            return false;
        }

        var origin = repeater.TranslatePoint(new Point(0, 0), GalleryScrollViewer);
        if (!origin.HasValue)
        {
            return false;
        }

        int first = int.MaxValue;
        int last = 0;
        foreach (var child in repeater.Children)
        {
            int index = repeater.GetElementIndex(child);
            var bounds = child.Bounds;
            if (index < 0 || bounds.Height <= 0)
            {
                continue;
            }

            double top = origin.Value.Y + bounds.Top;
            double bottom = origin.Value.Y + bounds.Bottom;
            if (bottom > 4 && top < viewportHeight - 4)
            {
                int oneBased = index + 1;
                if (oneBased < first)
                {
                    first = oneBased;
                }
                if (oneBased > last)
                {
                    last = oneBased;
                }
            }
        }

        if (last > 0 && first <= last)
        {
            vm.UpdateViewportRange(first, last, offsetY);
            return true;
        }

        return false;
    }

    /// <summary>虛擬化清單建立（或回收重用）一張卡片時，載入該卡片的封面。</summary>
    private void OnRepeaterElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (_boundViewModel is not null && e.Element.DataContext is GalleryItemVm item)
        {
            _ = _boundViewModel.NotifyItemRealized(item);
        }
    }

    /// <summary>虛擬化清單回收一張卡片時，交給 ViewModel 的回收佇列決定何時放掉封面。</summary>
    private void OnRepeaterElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        if (_boundViewModel is not null && e.Element.DataContext is GalleryItemVm item)
        {
            _boundViewModel.NotifyItemRecycled(item);
        }
    }

    private void OnFolderTreeNodeTapped(object? sender, global::Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control { DataContext: GalleryFolderTreeNodeVm node } && !node.IsPlaceholder)
        {
            node.NavigateCommand.Execute(null);
            e.Handled = true;
        }
    }
}

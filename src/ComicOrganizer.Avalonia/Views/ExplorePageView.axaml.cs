using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class ExplorePageView : UserControl
{
    private ExplorePageViewModel? _boundViewModel;
    private bool _viewportUpdateScheduled;

    public ExplorePageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        KeyDown += OnViewKeyDown;
        ScrollWheelStepHelper.Attach(
            WallScrollViewer,
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
        if (DataContext is not ExplorePageViewModel vm)
        {
            return;
        }

        if (!ReferenceEquals(_boundViewModel, vm))
        {
            _boundViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ScrollResetRequested += OnScrollResetRequested;
        }

        SyncCoverModeResource(vm.IsDeckCoverStyle);
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
        WallScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, Vector.Zero);
        ScheduleViewportRangeUpdate();
    }

    private void OnWallViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ScheduleViewportRangeUpdate();
    }

    private void OnBackToTopClick(object? sender, RoutedEventArgs e)
    {
        WallScrollViewer?.SetCurrentValue(ScrollViewer.OffsetProperty, Vector.Zero);
        ScheduleViewportRangeUpdate();
    }

    private void OnWallScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        ScheduleViewportRangeUpdate();
    }

    private void SyncActiveRepeater(ExplorePageViewModel vm)
    {
        ItemsRepeater? active;
        System.Collections.IEnumerable activeSource;

        if (vm.IsDrillDownMode)
        {
            active = DrillDownItemsRepeater;
            activeSource = vm.DrillDownWorks;
        }
        else if (vm.IsAuthorMode)
        {
            active = AuthorItemsRepeater;
            activeSource = vm.AuthorCards;
        }
        else if (vm.IsHotComboSubTabActive)
        {
            active = ComboItemsRepeater;
            activeSource = vm.ComboCards;
        }
        else
        {
            active = ThemeItemsRepeater;
            activeSource = vm.ThemeCards;
        }

        ItemsRepeater?[] all = [AuthorItemsRepeater, ThemeItemsRepeater, ComboItemsRepeater, DrillDownItemsRepeater];
        foreach (var repeater in all)
        {
            if (repeater is null || ReferenceEquals(repeater, active))
            {
                continue;
            }

            if (repeater.ItemsSource is not null)
            {
                repeater.ItemsSource = null;
            }
        }

        if (active is not null && !ReferenceEquals(active.ItemsSource, activeSource))
        {
            active.ItemsSource = activeSource;
        }
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
        if (_boundViewModel is null || WallScrollViewer is null)
        {
            return;
        }

        var vm = _boundViewModel;
        int loadedCount = vm.CurrentCardCount;
        if (loadedCount <= 0)
        {
            vm.UpdateViewportRange(0, 0, 0);
            return;
        }

        ItemsRepeater? activeRepeater = vm.IsDrillDownMode
            ? DrillDownItemsRepeater
            : vm.IsAuthorMode
                ? AuthorItemsRepeater
                : vm.IsHotComboSubTabActive
                    ? ComboItemsRepeater
                    : ThemeItemsRepeater;

        double viewportHeight = WallScrollViewer.Viewport.Height;
        double offsetY = WallScrollViewer.Offset.Y;

        if (TryUpdateViewportRangeFromRepeater(vm, activeRepeater, viewportHeight, offsetY))
        {
            return;
        }

        vm.UpdateViewportRange(1, Math.Min(loadedCount, 12), offsetY);
    }

    private bool TryUpdateViewportRangeFromRepeater(
        ExplorePageViewModel vm,
        ItemsRepeater? repeater,
        double viewportHeight,
        double offsetY)
    {
        if (repeater is null || viewportHeight <= 0 || repeater.Children.Count == 0)
        {
            return false;
        }

        var origin = repeater.TranslatePoint(new Point(0, 0), WallScrollViewer);
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
                if (oneBased < first) first = oneBased;
                if (oneBased > last) last = oneBased;
            }
        }

        if (last > 0 && first <= last)
        {
            vm.UpdateViewportRange(first, last, offsetY);
            return true;
        }

        return false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ExplorePageViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(ExplorePageViewModel.IsDeckCoverStyle) ||
            e.PropertyName == nameof(ExplorePageViewModel.CoverStyle))
        {
            SyncCoverModeResource(vm.IsDeckCoverStyle);
        }
        else if (e.PropertyName is nameof(ExplorePageViewModel.IsDrillDownMode)
                 or nameof(ExplorePageViewModel.SelectedMainTab)
                 or nameof(ExplorePageViewModel.SelectedThemeSubTab))
        {
            SyncActiveRepeater(vm);
            ScheduleViewportRangeUpdate();
        }
        else if (e.PropertyName == nameof(ExplorePageViewModel.CurrentCardCount) ||
                 e.PropertyName == nameof(ExplorePageViewModel.IsLoading))
        {
            ScheduleViewportRangeUpdate();
        }
    }

    private void SyncCoverModeResource(bool isDeckMode)
    {
        Resources["IsDeckCoverMode"] = isDeckMode;
    }

    /// <summary>虛擬化清單建立（或回收重用）一張探索卡片時，啟動該卡片的封面載入。</summary>
    private void OnRepeaterElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (_boundViewModel is not null && e.Element.DataContext is ExploreCardVmBase card)
        {
            _ = _boundViewModel.NotifyCardRealized(card);
        }
    }

    /// <summary>虛擬化清單回收一張探索卡片時，重設懸停動畫並取消／排入封面回收佇列。</summary>
    private void OnRepeaterElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        if (e.Element.FindDescendantOfType<DeckCoverControl>() is { } deck)
        {
            deck.IsCardHovered = false;
        }

        if (_boundViewModel is not null && e.Element.DataContext is ExploreCardVmBase card)
        {
            _boundViewModel.NotifyCardRecycled(card);
        }
    }

    private void OnExploreCardPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Visual visual && visual.FindDescendantOfType<DeckCoverControl>() is { } deck)
        {
            deck.IsCardHovered = true;
        }
    }

    private void OnExploreCardPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Visual visual && visual.FindDescendantOfType<DeckCoverControl>() is { } deck)
        {
            deck.IsCardHovered = false;
        }
    }

    private void OnTagPickerBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_boundViewModel is { IsTagPickerOpen: true })
        {
            _boundViewModel.CloseTagPickerCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _boundViewModel is null)
        {
            return;
        }

        if (_boundViewModel.IsTagPickerOpen)
        {
            _boundViewModel.CloseTagPickerCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_boundViewModel.IsDrillDownMode)
        {
            _boundViewModel.ExitDrillDownCommand.Execute(null);
            e.Handled = true;
        }
    }
}

/// <summary>
/// 將十六進位色彩字串（例如 #3B82F6）轉換為快取的 <see cref="IBrush"/>。
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IBrush FallbackBrush = new SolidColorBrush(Color.Parse("#3B82F6"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            return Cache.GetOrAdd(hex.Trim(), static key =>
                Color.TryParse(key, out var color)
                    ? new SolidColorBrush(color)
                    : FallbackBrush);
        }

        return FallbackBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

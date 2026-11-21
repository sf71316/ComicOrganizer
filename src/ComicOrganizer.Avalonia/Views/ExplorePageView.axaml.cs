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
    private double _lastAutoLoadExtentHeight = -1;

    public ExplorePageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        KeyDown += OnViewKeyDown;
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
        _lastAutoLoadExtentHeight = -1;
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
        if (_boundViewModel is null || sender is not ScrollViewer sv)
        {
            return;
        }

        if (_boundViewModel.HasMoreWallCards &&
            (sv.Offset.Y > 24 || e.OffsetDelta.Y > 0))
        {
            double maxOffsetY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            double preloadThreshold = Math.Max(680, sv.Viewport.Height * 1.5);
            if (maxOffsetY > 0 &&
                sv.Offset.Y >= maxOffsetY - preloadThreshold &&
                Math.Abs(sv.Extent.Height - _lastAutoLoadExtentHeight) > 1.0)
            {
                // 記錄觸發當下的 Extent 高度，避免版面重排尚未完成前同一幀滾輪事件重複連發
                _lastAutoLoadExtentHeight = sv.Extent.Height;
                _boundViewModel.LoadMoreWallCardsCommand.Execute(null);
            }
        }

        ScheduleViewportRangeUpdate();
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

        ItemsControl? activeControl = vm.IsDrillDownMode
            ? DrillDownItemsControl
            : vm.IsAuthorMode
                ? AuthorItemsControl
                : vm.IsHotComboSubTabActive
                    ? ComboItemsControl
                    : ThemeItemsControl;

        double viewportHeight = WallScrollViewer.Viewport.Height;
        double offsetY = WallScrollViewer.Offset.Y;

        if (activeControl?.ItemsPanelRoot is Panel panel && panel.Children.Count > 0 && viewportHeight > 0)
        {
            var origin = panel.TranslatePoint(new Point(0, 0), WallScrollViewer);
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
            _lastAutoLoadExtentHeight = -1;
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

    private void OnCardEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0)
        {
            return;
        }

        if (_boundViewModel is not null && sender is Control { DataContext: ExploreCardVmBase card })
        {
            _ = _boundViewModel.RequestCardCoversAsync(card);
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

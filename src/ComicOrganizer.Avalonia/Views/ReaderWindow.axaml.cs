using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using ComicOrganizer.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ComicOrganizer.Avalonia.Views;

/// <summary>
/// 獨立漫畫閱讀視窗：自動繼承 <see cref="Services.AppDataContext"/> 主題，整合 GPU/Skia 閱讀畫布與可收合右側功能欄。
/// </summary>
public partial class ReaderWindow : SukiWindow
{
    private ReaderViewModel? _vm;
    private WindowState _preFullscreenState = WindowState.Normal;

    public ReaderWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnWindowClosed;
        Deactivated += OnWindowDeactivated;
        AddHandler(PointerPressedEvent, OnWindowTunnelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Bubble, handledEventsToo: false);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            DetachViewModel(_vm);
        }

        _vm = DataContext as ReaderViewModel;
        if (_vm != null)
        {
            AttachViewModel(_vm);
        }
    }

    private void AttachViewModel(ReaderViewModel vm)
    {
        vm.NavigationRequested += OnNavigationRequested;
        vm.ViewModeChanged += OnViewModeChanged;
        vm.ZoomModeResetRequested += OnZoomModeResetRequested;
        vm.ZoomStepRequested += OnZoomStepRequested;
        vm.FullscreenChanged += OnFullscreenChanged;
        vm.PropertyChanged += OnVmPropertyChanged;

        Viewport.CurrentPageChanged += OnViewportPageChanged;
        Viewport.ZoomPercentChanged += OnViewportZoomChanged;
        Viewport.EnhancementOptionsChanged += OnViewportEnhancementChanged;
        Viewport.TranslationToggledRequested += OnViewportToggleTranslation;
        Viewport.CompareOriginalToggledRequested += OnViewportToggleCompareOriginal;
        Viewport.ForceRetranslateCurrentPageRequested += OnViewportForceRetranslate;
        Viewport.CustomEnhancementDialogRequested += OnViewportCustomEnhancementRequested;
        Viewport.StatusNotificationRequested += OnViewportStatusNotification;

        Viewport.Pipeline = vm.Pipeline;
        Viewport.ViewMode = vm.ViewMode;
        Viewport.IsRightToLeft = vm.IsRightToLeft;
        Viewport.ContinuousGapPx = vm.ContinuousGapPx;
        Viewport.IsTranslationEnabled = vm.IsTranslationEnabled;
        Viewport.IsComparingOriginal = vm.IsComparingOriginal;
        Viewport.EnhancementOptions = vm.EnhancementOptions;
        if (vm.InitialHighlightBox is { } hb)
        {
            Viewport.SetHighlightPanel(hb.PageIndex, hb.X, hb.Y, hb.W, hb.H);
        }
        Viewport.ResetAutoZoom(vm.ZoomMode);
        Viewport.GoToPage(vm.CurrentPageIndex);
    }

    private void DetachViewModel(ReaderViewModel vm)
    {
        vm.NavigationRequested -= OnNavigationRequested;
        vm.ViewModeChanged -= OnViewModeChanged;
        vm.ZoomModeResetRequested -= OnZoomModeResetRequested;
        vm.ZoomStepRequested -= OnZoomStepRequested;
        vm.FullscreenChanged -= OnFullscreenChanged;
        vm.PropertyChanged -= OnVmPropertyChanged;

        Viewport.CurrentPageChanged -= OnViewportPageChanged;
        Viewport.ZoomPercentChanged -= OnViewportZoomChanged;
        Viewport.EnhancementOptionsChanged -= OnViewportEnhancementChanged;
        Viewport.TranslationToggledRequested -= OnViewportToggleTranslation;
        Viewport.CompareOriginalToggledRequested -= OnViewportToggleCompareOriginal;
        Viewport.ForceRetranslateCurrentPageRequested -= OnViewportForceRetranslate;
        Viewport.CustomEnhancementDialogRequested -= OnViewportCustomEnhancementRequested;
        Viewport.StatusNotificationRequested -= OnViewportStatusNotification;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;
        switch (e.PropertyName)
        {
            case nameof(ReaderViewModel.IsRightToLeft):
                Viewport.IsRightToLeft = _vm.IsRightToLeft;
                break;
            case nameof(ReaderViewModel.ContinuousGapPx):
                Viewport.ContinuousGapPx = _vm.ContinuousGapPx;
                break;
            case nameof(ReaderViewModel.IsTranslationEnabled):
                Viewport.IsTranslationEnabled = _vm.IsTranslationEnabled;
                break;
            case nameof(ReaderViewModel.IsComparingOriginal):
                Viewport.IsComparingOriginal = _vm.IsComparingOriginal;
                break;
            case nameof(ReaderViewModel.EnhancementOptions):
                Viewport.EnhancementOptions = _vm.EnhancementOptions;
                break;
        }
    }

    private void OnNavigationRequested(int pageIndex) => Viewport.GoToPage(pageIndex);
    private void OnViewModeChanged(ComicOrganizer.App.UI.ReaderViewMode mode) => Viewport.ViewMode = mode;
    private void OnZoomModeResetRequested(ComicOrganizer.App.UI.ReaderZoomMode zoomMode) => Viewport.ResetAutoZoom(zoomMode);
    private void OnZoomStepRequested(float step) => Viewport.AdjustZoomByStep(step);

    private void OnFullscreenChanged(bool isFullscreen)
    {
        if (isFullscreen)
        {
            _preFullscreenState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState;
            IsTitleBarVisible = false;
            WindowState = WindowState.FullScreen;
        }
        else
        {
            IsTitleBarVisible = true;
            WindowState = _preFullscreenState;
        }
    }

    private void OnViewportPageChanged(int pageIndex) => _vm?.NotifyViewportPageChanged(pageIndex);
    private void OnViewportZoomChanged(int percent) => _vm?.NotifyViewportZoomChanged(percent);
    private void OnViewportEnhancementChanged(ComicOrganizer.App.Core.ReaderImageEnhancementOptions opts) => _vm?.SyncEnhancementFromViewport(opts);
    private void OnViewportToggleTranslation() => _vm?.ToggleTranslationCommand.Execute(null);
    private void OnViewportToggleCompareOriginal() => _vm?.ToggleCompareOriginalCommand.Execute(null);
    private void OnViewportForceRetranslate() => _vm?.ForceRetranslateCurrentPageCommand.Execute(null);
    private void OnViewportCustomEnhancementRequested() => _vm?.OpenEnhancementTabInDrawer();
    private void OnViewportStatusNotification(string title, string message)
    {
        if (_vm != null)
            _vm.TranslationStatusText = $"{title}：{message}";
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm == null || e.Handled) return;

        // 避免在文字編輯等情況攔截按鍵
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        switch (e.Key)
        {
            case Key.Left:
                if (_vm.IsRightToLeft) Viewport.NextSpreadOrPage();
                else Viewport.PrevSpreadOrPage();
                e.Handled = true;
                break;

            case Key.Right:
                if (_vm.IsRightToLeft) Viewport.PrevSpreadOrPage();
                else Viewport.NextSpreadOrPage();
                e.Handled = true;
                break;

            case Key.PageDown:
                Viewport.NextSpreadOrPage();
                e.Handled = true;
                break;

            case Key.PageUp:
                Viewport.PrevSpreadOrPage();
                e.Handled = true;
                break;

            case Key.Home:
                Viewport.GoToPage(0);
                e.Handled = true;
                break;

            case Key.End:
                Viewport.GoToPage(Math.Max(0, _vm.PageCount - 1));
                e.Handled = true;
                break;

            case Key.Space:
                if (_vm.IsTranslationEnabled)
                    _vm.ToggleCompareOriginalCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.T:
                _vm.ToggleTranslationCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.B:
                _vm.ToggleCurrentBookmarkCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.H:
                _vm.ToggleHudCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F11:
                _vm.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (_vm.IsFullscreen)
                {
                    _vm.IsFullscreen = false;
                }
                else if (_vm.IsDrawerOpen)
                {
                    _vm.IsDrawerOpen = false;
                }
                else if (!_vm.IsHudVisible)
                {
                    _vm.IsHudVisible = true;
                }
                else
                {
                    Close();
                }
                e.Handled = true;
                break;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
        => Viewport.CloseContextMenu();

    private void OnWindowTunnelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Viewport.ContextMenu is { IsOpen: true } menu &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            !IsSourceInsideContextMenu(e.Source, menu))
        {
            menu.Close();
            e.Handled = true;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || WindowState == WindowState.FullScreen)
        {
            return;
        }

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (IsInsideInteractiveControl(e.Source))
        {
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    internal static bool IsSourceInsideContextMenu(object? source, ContextMenu menu)
    {
        for (Visual? cur = source as Visual; cur != null; cur = cur.GetVisualParent())
        {
            if (ReferenceEquals(cur, menu) || cur is ContextMenu || cur is MenuItem)
            {
                return true;
            }
        }

        for (ILogical? cur = source as ILogical; cur != null; cur = cur.LogicalParent)
        {
            if (ReferenceEquals(cur, menu) || cur is ContextMenu || cur is MenuItem)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsInsideInteractiveControl(object? source)
    {
        for (Visual? cur = source as Visual; cur != null; cur = cur.GetVisualParent())
        {
            if (cur is Button or ComboBox or ComboBoxItem or Slider or TextBox or TabItem or ListBox or ScrollBar or ScrollViewer or SelectableTextBlock or Expander or Thumb)
            {
                return true;
            }
        }
        return false;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Deactivated -= OnWindowDeactivated;
        if (_vm != null)
        {
            DetachViewModel(_vm);
            _vm.Dispose();
            _vm = null;
        }
        Viewport.Pipeline = null;
    }
}

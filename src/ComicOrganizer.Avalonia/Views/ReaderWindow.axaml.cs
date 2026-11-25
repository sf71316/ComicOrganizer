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
    private bool _isSyncingFullscreen;

    public ReaderWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnWindowPropertyChanged;
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
        => ApplyFullscreenState(isFullscreen);

    internal void ApplyFullscreenState(bool isFullscreen)
    {
        if (_isSyncingFullscreen)
        {
            return;
        }

        _isSyncingFullscreen = true;
        try
        {
            ApplyFullscreenTransition(
                isFullscreen,
                () => WindowState,
                state => WindowState = state,
                () => IsTitleBarVisible,
                visible => IsTitleBarVisible = visible,
                ref _preFullscreenState);
        }
        finally
        {
            _isSyncingFullscreen = false;
        }
    }

    internal static void ApplyFullscreenTransition(
        bool isFullscreen,
        Func<WindowState> getWindowState,
        Action<WindowState> setWindowState,
        Func<bool> getIsTitleBarVisible,
        Action<bool> setIsTitleBarVisible,
        ref WindowState preFullscreenState)
    {
        if (isFullscreen)
        {
            var currentState = getWindowState();
            preFullscreenState = currentState == WindowState.FullScreen ? WindowState.Normal : currentState;
            // 必須先讓 SukiWindow 在 WindowState 切換進全螢幕時記錄到 IsTitleBarVisible == true，
            // 否則 _wasTitleBarVisibleBeforeFullScreen 會誤記為 false，退出全螢幕後會把標題列再蓋回隱藏
            if (!getIsTitleBarVisible())
            {
                setIsTitleBarVisible(true);
            }

            setWindowState(WindowState.FullScreen);
            setIsTitleBarVisible(false);
        }
        else
        {
            var restoreState = preFullscreenState == WindowState.FullScreen ? WindowState.Normal : preFullscreenState;
            setWindowState(restoreState);
            setIsTitleBarVisible(true);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != WindowStateProperty || _isSyncingFullscreen)
        {
            return;
        }

        var newState = (WindowState)(e.NewValue ?? WindowState.Normal);
        var oldState = (WindowState)(e.OldValue ?? WindowState.Normal);
        if (newState == WindowState.FullScreen)
        {
            _preFullscreenState = oldState == WindowState.FullScreen ? WindowState.Normal : oldState;
            IsTitleBarVisible = false;
            SyncVmFullscreen(true);
        }
        else if (oldState == WindowState.FullScreen)
        {
            IsTitleBarVisible = true;
            SyncVmFullscreen(false);
        }
    }

    private void SyncVmFullscreen(bool isFullscreen)
    {
        if (_vm == null || _vm.IsFullscreen == isFullscreen)
        {
            return;
        }

        _isSyncingFullscreen = true;
        try
        {
            _vm.IsFullscreen = isFullscreen;
        }
        finally
        {
            _isSyncingFullscreen = false;
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
        PropertyChanged -= OnWindowPropertyChanged;
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

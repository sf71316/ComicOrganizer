using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views.Controls;

public partial class DslSearchBox : UserControl
{
    public static readonly StyledProperty<bool> ShowQuickFieldBarProperty =
        AvaloniaProperty.Register<DslSearchBox, bool>(nameof(ShowQuickFieldBar), defaultValue: true);

    private DslSearchBoxViewModel? _boundViewModel;

    public DslSearchBox()
    {
        InitializeComponent();
        InnerTextBox.AddHandler(KeyDownEvent, OnInnerTextBoxKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public bool ShowQuickFieldBar
    {
        get => GetValue(ShowQuickFieldBarProperty);
        set => SetValue(ShowQuickFieldBarProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowQuickFieldBarProperty && QuickFieldBar is not null)
        {
            QuickFieldBar.IsVisible = ShowQuickFieldBar;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        AttachViewModel();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (QuickFieldBar is not null)
        {
            QuickFieldBar.IsVisible = ShowQuickFieldBar;
        }
        AttachViewModel();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachViewModel();
    }

    private void AttachViewModel()
    {
        if (DataContext is not DslSearchBoxViewModel vm)
        {
            return;
        }

        if (!ReferenceEquals(_boundViewModel, vm))
        {
            _boundViewModel = vm;
            vm.FocusInputEndRequested += OnFocusInputEndRequested;
        }
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.FocusInputEndRequested -= OnFocusInputEndRequested;
            _boundViewModel = null;
        }
    }

    private void OnFocusInputEndRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            InnerTextBox.Focus();
            InnerTextBox.CaretIndex = InnerTextBox.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
    }

    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!InnerTextBox.IsFocused)
        {
            InnerTextBox.Focus();
            InnerTextBox.CaretIndex = InnerTextBox.Text?.Length ?? 0;
        }
    }

    private void OnInnerTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                if (_boundViewModel.IsSuggestionPopupOpen)
                {
                    _boundViewModel.MoveSelectionDown();
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (_boundViewModel.IsSuggestionPopupOpen)
                {
                    _boundViewModel.MoveSelectionUp();
                    e.Handled = true;
                }
                break;

            case Key.Tab:
                if (_boundViewModel.IsSuggestionPopupOpen && _boundViewModel.TryHandleEnterOrTab(isTab: true))
                {
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                if (_boundViewModel.TryHandleEnterOrTab(isTab: false))
                {
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                if (_boundViewModel.IsSuggestionPopupOpen)
                {
                    _boundViewModel.ClosePopup();
                    e.Handled = true;
                }
                break;

            case Key.Back:
                if ((InnerTextBox.CaretIndex == 0 || string.IsNullOrEmpty(InnerTextBox.Text)) &&
                    _boundViewModel.TryHandleBackspaceWhenInputEmpty())
                {
                    e.Handled = true;
                }
                break;
        }
    }
}

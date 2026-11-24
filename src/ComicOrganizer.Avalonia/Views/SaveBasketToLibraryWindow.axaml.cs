using Avalonia.Input;
using ComicOrganizer.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class SaveBasketToLibraryWindow : SukiWindow
{
    private WorkBasketSaveViewModel? _vm;

    public SaveBasketToLibraryWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnWindowClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestClose -= OnRequestClose;
        }

        _vm = DataContext as WorkBasketSaveViewModel;
        if (_vm != null)
        {
            _vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose(bool confirmed) => Close(confirmed);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _vm == null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestClose -= OnRequestClose;
            _vm = null;
        }
    }
}

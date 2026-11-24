using Avalonia.Input;
using ComicOrganizer.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class WorkPreviewWindow : SukiWindow
{
    private WorkPreviewViewModel? _vm;

    public WorkPreviewWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnWindowClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestClose -= OnVmRequestClose;
        }

        _vm = DataContext as WorkPreviewViewModel;
        if (_vm != null)
        {
            _vm.RequestClose += OnVmRequestClose;
        }
    }

    private void OnVmRequestClose() => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || _vm == null)
        {
            return;
        }

        if (_vm.HasZoomPage)
        {
            _vm.CollapseZoomCommand.Execute(null);
        }
        else
        {
            Close();
        }
        e.Handled = true;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestClose -= OnVmRequestClose;
            _vm.Dispose();
            _vm = null;
        }
    }
}

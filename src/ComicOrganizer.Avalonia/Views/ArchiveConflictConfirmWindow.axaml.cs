using Avalonia.Input;
using ComicOrganizer.Avalonia.Services;
using SukiUI.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class ArchiveConflictConfirmWindow : SukiWindow
{
    private ArchiveConflictConfirmViewModel? _vm;

    public ArchiveConflictDecision SelectedDecision { get; private set; } = ArchiveConflictDecision.Skip;

    public ArchiveConflictConfirmWindow()
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

        _vm = DataContext as ArchiveConflictConfirmViewModel;
        if (_vm != null)
        {
            _vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose(ArchiveConflictDecision decision)
    {
        SelectedDecision = decision;
        Close(decision);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _vm == null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _vm.SkipCommand.Execute(null);
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

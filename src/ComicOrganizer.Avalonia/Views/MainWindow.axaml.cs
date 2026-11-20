using Avalonia.Threading;
using ComicOrganizer.Avalonia.ViewModels;
using SukiUI.Controls;

namespace ComicOrganizer.Avalonia.Views;

public partial class MainWindow : SukiWindow
{
    private MainWindowViewModel? _boundViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnMainDataContextChanged;
        Opened += OnWindowOpened;
        Closed += (_, _) => DetachViewModel();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            Dispatcher.UIThread.Post(_boundViewModel.StartDeferredInitialization, DispatcherPriority.Background);
        }
    }

    private void OnMainDataContextChanged(object? sender, EventArgs e)
    {
        DetachViewModel();
        if (DataContext is MainWindowViewModel vm)
        {
            _boundViewModel = vm;
            vm.NavigateToPageRequested += OnNavigateToPageRequested;
        }
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.NavigateToPageRequested -= OnNavigateToPageRequested;
            _boundViewModel = null;
        }
    }

    private void OnNavigateToPageRequested(ViewModelBase pageVm)
    {
        foreach (var item in MainSideMenu.Items)
        {
            if (item is SukiSideMenuItem menuItem && ReferenceEquals(menuItem.PageContent, pageVm))
            {
                MainSideMenu.SelectedItem = menuItem;
                break;
            }
        }
    }
}

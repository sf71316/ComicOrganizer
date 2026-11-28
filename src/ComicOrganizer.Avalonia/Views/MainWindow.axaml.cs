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

        // 封面依顯示大小解碼需要知道螢幕縮放比例；視窗移到不同縮放的螢幕時再回報一次
        ScalingChanged += (_, _) => ReportDisplayScale();

        // 保留頁面：選取項目改變時切換 PageHost 顯示的頁面（不再由 SukiSideMenu 重建 View）
        MainSideMenu.SelectionChanged += (_, _) => SyncPageHost();
    }

    /// <summary>依目前選取的選單項目，讓 PageHost 顯示對應頁面。</summary>
    private void SyncPageHost()
    {
        if (MainSideMenu.SelectedItem is SukiSideMenuItem item)
        {
            _boundViewModel?.NotifyPageShown(item.PageContent);
            PageHost.ShowPage(item.PageContent);
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        // 啟動時 SukiSideMenu 可能在 PageContent 繫結完成前就已選取第一項，這裡補同步一次
        SyncPageHost();

        // 視窗實際放到螢幕上後再回報一次縮放比例（多螢幕時可能與建立時不同）
        ReportDisplayScale();

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

            // 盡早回報，讓頁面第一次載入封面時就用正確的縮放比例
            ReportDisplayScale();
        }
    }

    private void ReportDisplayScale()
    {
        _boundViewModel?.ReportDisplayScale(RenderScaling);
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

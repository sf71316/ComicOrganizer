using Avalonia.Controls;
using Avalonia.Layout;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views;

public partial class ReadingTrackPageView : UserControl
{
    public ReadingTrackPageView()
    {
        InitializeComponent();
    }

    /// <summary>虛擬化的近期紀錄牆建立（或回收重用）一張卡片時，載入該卡片的封面。</summary>
    private void OnRecordElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (DataContext is ReadingTrackPageViewModel vm && e.Element.DataContext is ReadingWorkItemVm item)
        {
            _ = vm.RequestCoverAsync(item);
        }
    }

    private void OnWorkItemEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0)
        {
            return;
        }

        if (DataContext is ReadingTrackPageViewModel vm &&
            sender is Control { DataContext: ReadingWorkItemVm item })
        {
            _ = vm.RequestCoverAsync(item);
        }
    }
}

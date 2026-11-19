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

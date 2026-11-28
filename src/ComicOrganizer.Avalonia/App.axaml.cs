using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views;

namespace ComicOrganizer.Avalonia;

public partial class App : Application
{
    private AppDataContext? _dataContext;
    private MainWindowViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            GlobalPopupLightDismissHelper.Install();
            _dataContext = new AppDataContext(args: Program.StartupArgs);
            _dataContext.ApplyInitialTheme();

            _mainViewModel = new MainWindowViewModel(_dataContext, deferStartupLoads: true);
            desktop.MainWindow = new MainWindow
            {
                DataContext = _mainViewModel,
            };

            desktop.Exit += (_, _) =>
            {
                _mainViewModel?.Dispose();
                _dataContext?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

namespace ComicOrganizer.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        
        // 啟動唯一的 MainForm (AntdUI 版本)
        Application.Run(new MainForm());
    }
}

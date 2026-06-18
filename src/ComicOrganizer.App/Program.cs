namespace ComicOrganizer.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();

        // AntdUI 內建字串(右鍵選單等)改繁中：AntdUI 預設簡中，掛上 provider 翻譯已知 key
        AntdUI.Localization.Provider = new Core.AntdLocalizationProvider();

        // 啟動唯一的 MainForm (AntdUI 版本)
        Application.Run(new MainForm());
    }
}

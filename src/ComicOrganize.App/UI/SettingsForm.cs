using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;

namespace ComicOrganize.App.UI;

public partial class SettingsForm : Window
{
    private readonly ConfigManager _configManager;
    public AppConfig Config { get; private set; }

    public SettingsForm(ConfigManager configManager, AppConfig config)
    {
        InitializeComponent();
        _configManager = configManager;
        Config = config;

        LoadConfig();
        WireEvents();

        ThemeManager.ThemeChanged += (s, e) => ThemeManager.ApplyTheme(this);
        LocalizationManager.LanguageChanged += (s, e) => ApplyLocalization();

        ThemeManager.ApplyTheme(this);
        ApplyLocalization();
    }

    private void LoadConfig()
    {
        txtSource.Text = Config.UnorganizedDirectory;
        txtTarget.Text = Config.RootDirectory;
        txtImageViewer.Text = Config.ImageViewerPath;
        cbDebug.Checked = Config.DebugMode;

        // Theme selector - 簡化 Items 避免轉型錯誤
        selectTheme.Items.Clear();
        selectTheme.Items.Add(LocalizationManager.GetString("ThemeLight"));
        selectTheme.Items.Add(LocalizationManager.GetString("ThemeDark"));
        selectTheme.Items.Add(LocalizationManager.GetString("ThemeSystem"));
        selectTheme.SelectedIndex = (int)Config.Theme;

        // Language selector
        selectLang.Items.Clear();
        selectLang.Items.Add("繁體中文");
        selectLang.Items.Add("English");
        selectLang.SelectedIndex = Config.Language == "zh-TW" ? 0 : 1;
    }

    private void WireEvents()
    {
        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnBrowseViewer.Click += (s, e) => BrowseFile(txtImageViewer, "執行檔 (*.exe)|*.exe|所有檔案 (*.*)|*.*");
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        selectTheme.SelectedValueChanged += (s, e) =>
        {
            if (selectTheme.SelectedIndex >= 0)
            {
                ThemeManager.CurrentTheme = (AppTheme)selectTheme.SelectedIndex;
            }
        };

        selectLang.SelectedValueChanged += (s, e) =>
        {
            if (selectLang.SelectedIndex >= 0)
            {
                string lang = selectLang.SelectedIndex == 0 ? "zh-TW" : "en-US";
                LocalizationManager.SetLanguage(lang);
            }
        };
    }

    private void ApplyLocalization()
    {
        windowBar.Text = LocalizationManager.GetString("SettingsTitle");
        label1.Text = LocalizationManager.GetString("SettingsSourcePath");
        label2.Text = LocalizationManager.GetString("SettingsTargetPath");
        label3.Text = LocalizationManager.GetString("SettingsImageViewer");
        labelTheme.Text = LocalizationManager.GetString("ThemeLabel") ?? "主題";
        labelLang.Text = LocalizationManager.GetString("LanguageLabel") ?? "語言";
        btnSave.Text = LocalizationManager.GetString("SettingsSave");
        btnCancel.Text = LocalizationManager.GetString("SettingsCancel");
        btnBrowseSource.Text = btnBrowseTarget.Text = btnBrowseViewer.Text = LocalizationManager.GetString("Browse") ?? "瀏覽";
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        DraggableMouseDown();
        base.OnMouseDown(e);
    }

    private void BrowseFolder(AntdUI.Input input)
    {
        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
        {
            if (!string.IsNullOrEmpty(folderBrowserDialog.DirectoryPath))
            {
                input.Text = folderBrowserDialog.DirectoryPath;
            }
        }
    }

    private void BrowseFile(AntdUI.Input input, string filter)
    {
        using var ofd = new OpenFileDialog { Filter = filter };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            input.Text = ofd.FileName;
        }
    }

    private void SaveAndClose()
    {
        Config.UnorganizedDirectory = txtSource.Text;
        Config.RootDirectory = txtTarget.Text;
        Config.ImageViewerPath = txtImageViewer.Text;
        Config.DebugMode = cbDebug.Checked;

        // 修正轉型問題：使用 Index 而非直接轉型對象
        if (selectTheme.SelectedIndex >= 0)
            Config.Theme = (AppTheme)selectTheme.SelectedIndex;

        if (selectLang.SelectedIndex >= 0)
            Config.Language = selectLang.SelectedIndex == 0 ? "zh-TW" : "en-US";

        _configManager.Save(Config);
        AntdUI.Message.info(this, LocalizationManager.GetString("SettingsSaved") ?? "設定已儲存");
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnSave_Click(object sender, EventArgs e)
    {
        SaveAndClose();
    }
}

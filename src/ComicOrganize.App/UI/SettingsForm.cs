using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;

namespace ComicOrganize.App.UI;

public partial class SettingsForm : Window, IThemedControl
{
    private readonly ConfigManager _configManager;
    public AppConfig Config { get; private set; }

    // 從 Designer.cs 搬到這裡 inline 初始化，避免 VS Designer regenerate 重複把 new 那行吃掉
    private readonly AntdUI.FolderBrowserDialog folderBrowserDialog = new AntdUI.FolderBrowserDialog();

    public SettingsForm(ConfigManager configManager, AppConfig config)
    {
        InitializeComponent();
        _configManager = configManager;
        Config = config;

        LoadConfig();
        WireEvents();

        this.WireThemeAndLocalization();
    }

    public void ApplyThemeColors()
    {
        windowBar.BackColor = Core.ThemeColors.Primary;
        content.BackColor = Core.ThemeColors.Surface;
        leftMenu.BackColor = Core.ThemeColors.Background;
        footer.BackColor = Core.ThemeColors.Surface;
        this.BackColor = Core.ThemeColors.Background;
        btnBrowseSource.DefaultBack = Core.ThemeColors.Primary;
        btnBrowseTarget.DefaultBack = Core.ThemeColors.Primary;
        btnBrowseViewer.DefaultBack = Core.ThemeColors.Primary;
        btnSave.BackColor = Core.ThemeColors.Primary;
        this.Refresh();
    }

    private void LoadConfig()
    {
        txtSource.Text = Config.UnorganizedDirectory;
        txtTarget.Text = Config.RootDirectory;
        txtImageViewer.Text = Config.ImageViewerPath;
        txtImageViewerArgs.Text = Config.ImageViewerArgs;
        cbDebug.Checked = Config.DebugMode;
        numMaxScanDepth.Value = Config.MaxScanDepth;

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

        // 左側選單切換 sub-panel
        btnMenuGeneral.Click += (s, e) => ShowSection(SettingsSection.General);
        btnMenuLibrary.Click += (s, e) => ShowSection(SettingsSection.Library);

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

    private enum SettingsSection { General, Library }

    private void ShowSection(SettingsSection section)
    {
        bool isGeneral = section == SettingsSection.General;
        generalPanel.Visible = isGeneral;
        libraryPanel.Visible = !isGeneral;
        // 選中的按鈕用 Primary 樣式，未選中用 Default
        btnMenuGeneral.Type = isGeneral ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
        btnMenuLibrary.Type = isGeneral ? AntdUI.TTypeMini.Default : AntdUI.TTypeMini.Primary;
    }

    public void ApplyLocalization()
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

        // 左側選單分類
        btnMenuGeneral.Text = LocalizationManager.GetString("SettingsMenuGeneral") ?? "一般設定";
        btnMenuLibrary.Text = LocalizationManager.GetString("SettingsMenuLibrary") ?? "漫畫庫管理";

        // 漫畫庫管理 - 搜尋層數
        labelMaxScanDepth.Text = LocalizationManager.GetString("SettingsMaxScanDepth") ?? "搜尋層數限制 (封面遞迴)";
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
        Config.ImageViewerArgs = string.IsNullOrWhiteSpace(txtImageViewerArgs.Text) ? "\"{path}\"" : txtImageViewerArgs.Text;
        Config.DebugMode = cbDebug.Checked;
        Config.MaxScanDepth = (int)numMaxScanDepth.Value;

        // 修正轉型問題：使用 Index 而非直接轉型對象
        if (selectTheme.SelectedIndex >= 0)
            Config.Theme = (AppTheme)selectTheme.SelectedIndex;

        if (selectLang.SelectedIndex >= 0)
            Config.Language = selectLang.SelectedIndex == 0 ? "zh-TW" : "en-US";

        _configManager.Save(Config);
        // 訊息留給 caller (MainForm.OpenSettings) 顯示，否則 Close() 後 message 隨 modal 消失看不到
        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnSave_Click(object sender, EventArgs e)
    {
        SaveAndClose();
    }
}

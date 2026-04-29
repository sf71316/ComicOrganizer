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

        txtSource.Text = config.UnorganizedDirectory;
        txtTarget.Text = config.RootDirectory;
        txtImageViewer.Text = config.ImageViewerPath;
        cbDebug.Checked = config.DebugMode;

        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnBrowseViewer.Click += (s, e) => BrowseFile(txtImageViewer, "執行檔 (*.exe)|*.exe|所有檔案 (*.*)|*.*");
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.Click += (s, e) => SaveAndClose();
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
        _configManager.Save(Config);
        AntdUI.Message.info(this, "設定已成功儲存");
        DialogResult = DialogResult.OK;
        Close();
    }
}

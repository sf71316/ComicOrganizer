using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;
using System.ComponentModel;

namespace ComicOrganize.App;

public partial class MainForm : Window
{
    private readonly ConfigManager _configManager;
    private AppConfig _config;
    private readonly MappingIntelligence _intelligence;
    private readonly ArchiveOrganizer _organizer;

    public MainForm()
    {
        InitializeComponent();

        _configManager = new ConfigManager();
        _config = _configManager.Load();
        _intelligence = new MappingIntelligence();
        _organizer = new ArchiveOrganizer(_intelligence, _config);
        var coverManager = new CoverManager(_config);

        InitializeUI();
        // 等 Form Load 後再啟動 GalleryControl（此時 handle 已建立、Invoke 才能用）
        this.Load += (s, e) => galleryControl.Initialize(_config, coverManager);

        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnBrowseViewer.Click += (s, e) => BrowseFile(txtImageViewer, "執行檔 (*.exe)|*.exe|所有檔案 (*.*)|*.*");
        btnSaveConfig.Click += (s, e) => SaveConfig();
        btnScan.Click += async (s, e) => await ScanAsync();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        DraggableMouseDown();
        base.OnMouseDown(e);
    }

    private void InitializeUI()
    {
        txtSource.Text = _config.UnorganizedDirectory;
        txtTarget.Text = _config.RootDirectory;
        txtImageViewer.Text = _config.ImageViewerPath;
        cbDebug.Checked = _config.DebugMode;
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
        using (var ofd = new OpenFileDialog { Filter = filter })
        {
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                input.Text = ofd.FileName;
            }
        }
    }

    private void SaveConfig()
    {
        _config.UnorganizedDirectory = txtSource.Text;
        _config.RootDirectory = txtTarget.Text;
        _config.ImageViewerPath = txtImageViewer.Text;
        _config.DebugMode = cbDebug.Checked;
        _configManager.Save(_config);
        AntdUI.Message.info(this, "設定已成功儲存");
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.UnorganizedDirectory) || !Directory.Exists(_config.UnorganizedDirectory))
        {
            AntdUI.Message.error(this, "無效的來源路徑");
            return;
        }

        btnScan.Loading = true;
        lblStatus.Text = "正在掃描匹配...";

        try
        {
            var targetFolders = Directory.GetDirectories(_config.RootDirectory)
                                         .Select(Path.GetFileName)
                                         .Where(n => n != null).Cast<string>().ToList();
            var sourceItems = Directory.GetFileSystemEntries(_config.UnorganizedDirectory);

            var list = new List<ArchiveItem>();

            await Task.Run(() =>
            {
                foreach (var itemPath in sourceItems)
                {
                    string name = Path.GetFileName(itemPath);
                    var result = _organizer.FindBestMatch(name, targetFolders);
                    string targetFolder = result?.FolderName ?? "未匹配";
                    string destPath = string.IsNullOrEmpty(result?.FolderName) ? "" : Path.Combine(_config.RootDirectory, result.FolderName);

                    list.Add(new ArchiveItem
                    {
                        Source = name,
                        FullPath = itemPath,
                        TargetFolder = targetFolder,
                        DestinationPath = destPath,
                        Confidence = result?.Confidence.ToString("F1") ?? "0",
                        Status = result != null && result.IsLearned ? "知識庫" : "模糊比對",
                        Checked = result != null && result.Confidence >= 85,
                        Action = new AntdUI.CellLink[] { new AntdUI.CellButton("Delete", "刪除", AntdUI.TTypeMini.Error)
                        { Ghost = true, IconSvg="DeleteOutlined",IconHoverSvg="DeleteFilled" } }
                    });
                }
            });

            var sortedList = new BindingList<ArchiveItem>(
                list.OrderByDescending(x =>
                {
                    double.TryParse(x.Confidence, out double val);
                    return val;
                }).ToList()
            );

            tableArchive.DataSource = sortedList;
            lblStatus.Text = $"掃描完成，共 {list.Count} 個項目";
            if (sortedList.Count > 0)
            {
                btnExecute.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            AntdUI.Message.error(this, ex.Message);
        }
        finally
        {
            btnScan.Loading = false;
        }
    }
}

public class ArchiveItem : INotifyPropertyChanged
{
    private bool _checked;
    public bool Checked
    {
        get => _checked;
        set { _checked = value; OnPropertyChanged(nameof(Checked)); }
    }

    public string Source { get; set; } = "";
    public string FullPath { get; set; } = "";

    private string _targetFolder = "";
    public string TargetFolder
    {
        get => _targetFolder;
        set { _targetFolder = value; OnPropertyChanged(nameof(TargetFolder)); }
    }

    private string _destinationPath = "";
    public string DestinationPath
    {
        get => _destinationPath;
        set { _destinationPath = value; OnPropertyChanged(nameof(DestinationPath)); }
    }

    public string Confidence { get; set; } = "";

    private string _status = "";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    public AntdUI.CellLink[]? Action { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

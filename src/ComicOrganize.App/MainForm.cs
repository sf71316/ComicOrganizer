using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;
using ComicOrganize.App.UI;
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

        InitializeUI();

        // 業務邏輯綁定
        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnSaveConfig.Click += (s, e) => SaveConfig();
        btnScan.Click += async (s, e) => await ScanAsync();

        // 點擊「預覽歸檔計畫」
        btnExecute.Click += (s, e) =>
        {
            var data = tableArchive.DataSource as BindingList<ArchiveItem>;
            var checkedItems = data?.Where(x => x.Checked).ToList();

            if (checkedItems == null || !checkedItems.Any())
            {
                AntdUI.Message.warn(this, "請先勾選要處理的項目");
                return;
            }

            using (var preview = new PreviewForm(checkedItems, _config.RootDirectory, _intelligence, _organizer))
            {
                preview.ShowDialog(this);
                // 執行完後重新掃描以刷新狀態
                _ = ScanAsync();
            }
        };

        // 雙擊修改目標路徑邏輯
        tableArchive.CellDoubleClick += (s, e) =>
        {
            var record = e.Record;
            if (record is ArchiveItem currentItem)
            {
                if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
                {
                    string newPath = folderBrowserDialog.SelectedPath;
                    string newFolderName = Path.GetFileName(newPath);

                    var data = tableArchive.DataSource as BindingList<ArchiveItem>;
                    var checkedItems = data?.Where(x => x.Checked).ToList();

                    if (checkedItems != null && checkedItems.Count > 1 && checkedItems.Contains(currentItem))
                    {
                        var diagResult = MessageBox.Show(
                            $"是否將已勾選的 {checkedItems.Count} 個項目全部修改為此目標？",
                            "批次修改確認",
                            MessageBoxButtons.YesNo);

                        if (diagResult == DialogResult.Yes)
                        {
                            foreach (var item in checkedItems)
                            {
                                UpdateItemTarget(item, newFolderName, newPath);
                            }
                        }
                        else
                        {
                            UpdateItemTarget(currentItem, newFolderName, newPath);
                        }
                    }
                    else
                    {
                        UpdateItemTarget(currentItem, newFolderName, newPath);
                    }

                    tableArchive.Invalidate();
                }
            }
        };
    }

    private void UpdateItemTarget(ArchiveItem item, string folderName, string fullPath)
    {
        item.TargetFolder = folderName;
        item.DestinationPath = fullPath;
        item.Status = "手動修正";
        item.Checked = true;
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
        cbDebug.Checked = _config.DebugMode;

        tableArchive.Columns = new ColumnCollection {
            new ColumnCheck("Checked"),
            new Column("Source", "來源名稱") { SortOrder = true },
            new Column("TargetFolder", "匹配目標") { Width = "150", SortOrder = true },
            new Column("DestinationPath", "目的路徑"),
            new Column("Confidence", "信心度") { Width = "80", SortOrder = true },
            new Column("Status", "狀態") { Width = "100" }
        };
    }

    private void BrowseFolder(AntdUI.Input input)
    {
        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
        {
            input.Text = folderBrowserDialog.SelectedPath;
        }
    }

    private void SaveConfig()
    {
        _config.UnorganizedDirectory = txtSource.Text;
        _config.RootDirectory = txtTarget.Text;
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

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

        InitializeUI();

        // 業務邏輯綁定
        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnSaveConfig.Click += (s, e) => SaveConfig();
        btnScan.Click += async (s, e) => await ScanAsync();
        btnExecute.Click += async (s, e) => await ExecuteAsync();
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

        // 設定 Table 欄位
        tableArchive.Columns = new ColumnCollection {
            new ColumnCheck("Checked"),
            new Column("Source", "來源名稱") { Width = "40%" },
            new Column("TargetFolder", "匹配目標") { Width = "20%" },
            new Column("Confidence", "信心度") { Width = "10%" },
            new Column("Status", "狀態") { Width = "20%" }
        };
        
        Config.Font = new Font("Microsoft JhengHei UI", 12F);
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

            var list = new BindingList<ArchiveItem>();

            await Task.Run(() => {
                foreach (var itemPath in sourceItems)
                {
                    string name = Path.GetFileName(itemPath);
                    var result = _organizer.FindBestMatch(name, targetFolders);
                    
                    list.Add(new ArchiveItem {
                        Source = name,
                        FullPath = itemPath,
                        TargetFolder = result?.FolderName ?? "未匹配",
                        Confidence = result?.Confidence.ToString("F1") ?? "0",
                        Status = result != null && result.IsLearned ? "知識庫" : "模糊比對",
                        Checked = result != null && result.Confidence >= 85
                    });
                }
            });

            tableArchive.DataSource = list;
            lblStatus.Text = $"掃描完成，共 {list.Count} 個項目";
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

    private async Task ExecuteAsync()
    {
        var data = tableArchive.DataSource as BindingList<ArchiveItem>;
        var checkedItems = data?.Where(x => x.Checked).ToList();

        if (checkedItems == null || !checkedItems.Any())
        {
            AntdUI.Message.warn(this, "請先勾選項目");
            return;
        }

        btnExecute.Loading = true;

        await Task.Run(() => {
            foreach (var item in checkedItems)
            {
                string targetPath = Path.Combine(_config.RootDirectory, item.TargetFolder, item.Source);
                try
                {
                    string destDir = Path.GetDirectoryName(targetPath)!;
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                    if (Directory.Exists(item.FullPath)) Directory.Move(item.FullPath, targetPath);
                    else File.Move(item.FullPath, targetPath);

                    var kws = _organizer.ExtractAllKeywords(item.Source);
                    foreach (var kw in kws)
                    {
                        if (_organizer.IsKeywordValidForTarget(kw, item.TargetFolder))
                        {
                            _intelligence.Learn(kw, item.TargetFolder);
                        }
                    }

                    this.Invoke(() => item.Status = "成功");
                }
                catch (Exception ex)
                {
                    this.Invoke(() => item.Status = "失敗: " + ex.Message);
                }
            }
        });

        tableArchive.Invalidate();
        btnExecute.Loading = false;
        AntdUI.Message.success(this, "歸檔處理完畢");
    }
}

public class ArchiveItem : INotifyPropertyChanged
{
    public bool Checked { get; set; }
    public string Source { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string TargetFolder { get; set; } = "";
    public string Confidence { get; set; } = "";
    
    private string _status = "";
    public string Status 
    { 
        get => _status; 
        set { _status = value; OnPropertyChanged(nameof(Status)); } 
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

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
    private readonly CoverManager _coverManager;

    public MainForm()
    {
        InitializeComponent();

        _configManager = new ConfigManager();
        _config = _configManager.Load();
        _intelligence = new MappingIntelligence();
        _organizer = new ArchiveOrganizer(_intelligence, _config);
        _coverManager = new CoverManager(_config);

        InitializeUI();

        // 業務邏輯綁定
        btnBrowseSource.Click += (s, e) => BrowseFolder(txtSource);
        btnBrowseTarget.Click += (s, e) => BrowseFolder(txtTarget);
        btnBrowseViewer.Click += (s, e) => BrowseFile(txtImageViewer, "執行檔 (*.exe)|*.exe|所有檔案 (*.*)|*.*");
        btnSaveConfig.Click += (s, e) => SaveConfig();
        btnScan.Click += async (s, e) => await ScanAsync();

        // 漫畫庫管理事件
        treeFolders.SelectChanged += (s, e) =>
        {
            if (e.Item.Tag is string path) OpenFolderTab(path, false);
        };
        treeFolders.NodeMouseDoubleClick += (s, e) =>
        {
            if (e.Item.Tag is string path) OpenFolderTab(path, true);
        };

        // 確保視窗載入完成後才開始載入樹狀目錄，避免 Handle 尚未建立的錯誤
        this.Load += (s, e) => _ = LoadTreeAsync();
    }

    private async Task LoadTreeAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory)) return;

        treeFolders.Items.Clear();
        await Task.Run(() =>
        {
            var rootDir = new DirectoryInfo(_config.RootDirectory);
            var rootNode = CreateFolderNode(rootDir);

            this.Invoke(() =>
            {
                treeFolders.Items.Add(rootNode);
                rootNode.Expand = true;
            });
        });
    }

    private AntdUI.TreeItem CreateFolderNode(DirectoryInfo dir)
    {
        var node = new AntdUI.TreeItem(dir.Name)
        {
            Tag = dir.FullName,
            IconSvg = "FolderOpenOutlined"
        };

        try
        {
            foreach (var subDir in dir.GetDirectories())
            {
                if ((subDir.Attributes & FileAttributes.Hidden) != 0) continue;
                node.Sub.Add(new AntdUI.TreeItem(subDir.Name)
                {
                    Tag = subDir.FullName,
                    IconSvg = "FolderOutlined"
                });
            }
        }
        catch { }

        return node;
    }

    private void OpenFolderTab(string path, bool forceNew)
    {
        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        // 尋找是否已有同路徑的頁籤
        var existingTab = tabsContent.Pages.Cast<AntdUI.TabPage>().FirstOrDefault(p => (string?)p.Tag == path);

        if (existingTab != null && !forceNew)
        {
            tabsContent.SelectedTab = existingTab;
            return;
        }

        var newPage = new AntdUI.TabPage
        {
            Text = folderName,
            Tag = path,
            Padding = new Padding(10)
        };

        // 實作縮圖面板容器
        var flowLayout = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White
        };
        newPage.Controls.Add(flowLayout);

        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        // 非同步載入內容
        _ = LoadGalleryAsync(flowLayout, path);
    }

    private async Task LoadGalleryAsync(FlowLayoutPanel container, string path)
    {
        container.Controls.Clear();
        var dirInfo = new DirectoryInfo(path);

        // 取得該目錄下的所有子目錄與檔案 (過濾隱藏檔)
        var items = dirInfo.GetFileSystemInfos()
                           .Where(i => (i.Attributes & FileAttributes.Hidden) == 0)
                           .ToList();

        foreach (var item in items)
        {
            // 為每個項目建立卡片
            var card = new AntdUI.Panel
            {
                Size = new Size(180, 240),
                Radius = 8,
                Padding = new Padding(5),
                Margin = new Padding(10),
                BackColor = Color.FromArgb(250, 250, 250)
            };

            var pic = new System.Windows.Forms.PictureBox
            {
                Dock = DockStyle.Top,
                Height = 180,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(235, 235, 235)
            };

            var lbl = new AntdUI.Label
            {
                Dock = DockStyle.Fill,
                Text = item.Name,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft JhengHei UI", 9F)
            };

            var btnOpen = new AntdUI.Button
            {
                Text = "👁 閱讀",
                Dock = DockStyle.Bottom,
                Height = 35,
                Type = TTypeMini.Default,
                Ghost = true
            };

            btnOpen.Click += (s, e) => OpenWithExternalViewer(item.FullName);

            card.Controls.Add(lbl);
            card.Controls.Add(btnOpen);
            card.Controls.Add(pic);
            container.Controls.Add(card);

            // 非同步載入封面
            _ = Task.Run(async () =>
            {
                var cover = await _coverManager.GetCoverAsync(item.FullName, 180);
                if (cover != null)
                {
                    this.Invoke(() => pic.Image = cover);
                }
            });
        }
    }

    private void OpenWithExternalViewer(string filePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(_config.ImageViewerPath) && File.Exists(_config.ImageViewerPath))
            {
                System.Diagnostics.Process.Start(_config.ImageViewerPath, $"\"{filePath}\"");
            }
            else
            {
                // 若未設定或路徑無效，使用系統預設
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AntdUI.Message.error(this, "開啟失敗: " + ex.Message);
        }
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
        txtImageViewer.Text = _config.ImageViewerPath;
        cbDebug.Checked = _config.DebugMode;
    }

    private void BrowseFolder(AntdUI.Input input)
    {
        if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
        {
            input.Text = folderBrowserDialog.SelectedPath;
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

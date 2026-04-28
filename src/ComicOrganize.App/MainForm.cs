using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;
using ComicOrganize.App.UI;
using System.ComponentModel;

namespace ComicOrganize.App;

public partial class MainForm : Window
{
    public enum GalleryViewMode { Small, Medium, Large, List }
    private GalleryViewMode _currentViewMode = GalleryViewMode.Medium;

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

        // 檢視模式按鈕：點擊彈下拉選單
        btnViewMode.Click += (s, e) =>
        {
            AntdUI.ContextMenuStrip.open(btnViewMode, mi =>
            {
                if (mi.Text == "縮圖(小)") _currentViewMode = GalleryViewMode.Small;
                else if (mi.Text == "縮圖(中)") _currentViewMode = GalleryViewMode.Medium;
                else if (mi.Text == "縮圖(大)") _currentViewMode = GalleryViewMode.Large;
                else if (mi.Text == "列表模式") _currentViewMode = GalleryViewMode.List;
                else return;
                RefreshCurrentTab();
            }, new AntdUI.IContextMenuStripItem[]
            {
                new AntdUI.ContextMenuStripItem("縮圖(小)"),
                new AntdUI.ContextMenuStripItem("縮圖(中)"),
                new AntdUI.ContextMenuStripItem("縮圖(大)"),
                new AntdUI.ContextMenuStripItem("列表模式"),
            });
        };

        // 重新整理按鈕 + F5
        btnRefresh.Click += (s, e) => RefreshCurrentTab();
        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5) { RefreshCurrentTab(); e.Handled = true; }
        };

        // 漫畫庫管理事件
        treeFolders.NodeMouseClick += (s, e) =>
        {
            if (e.Item.Tag is string path && e.Button == MouseButtons.Right)
            {
                AntdUI.ContextMenuStrip.open(treeFolders, (menuItem) =>
                {
                    if (menuItem.Text == "另開頁籤")
                    {
                        OpenFolderTab(path, true);
                    }
                    else if (menuItem.Text != null && menuItem.Text.Contains("重新整理"))
                    {
                        e.Item.Sub.Clear();
                        e.Item.Sub.Add(new AntdUI.TreeItem("Loading...") { Tag = LazyPlaceholderTag });
                        LoadSubDirectories(e.Item, path);
                    }
                }, new AntdUI.IContextMenuStripItem[]
                {
                    new AntdUI.ContextMenuStripItem("另開頁籤"),
                    new AntdUI.ContextMenuStripItem("🔄 重新整理"),
                });
            }
        };


        // 處理頁籤關閉事件
        tabsContent.ClosingPage += (s, e) =>
        {
            return true; // 允許關閉
        };

        // 切換 Tab 時同步麵包屑
        tabsContent.SelectedIndexChanged += (s, e) =>
        {
            if (tabsContent.SelectedTab?.Tag is string p) UpdateBreadcrumb(p);
            else breadcrumb.Items.Clear();
        };

        // 麵包屑點擊：在當前 Tab 跳到該層
        breadcrumb.ItemClick += (s, e) =>
        {
            if (e.Item.Tag is string p) OpenFolderTab(p, false);
        };

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
                LoadSubDirectories(rootNode, rootDir.FullName); // 初始載入第一層
                UpdateTreeWidth();
            });
        });
    }

    private void UpdateBreadcrumb(string path)
    {
        breadcrumb.Items.Clear();
        if (string.IsNullOrEmpty(_config.RootDirectory) || string.IsNullOrEmpty(path)) return;

        string root = _config.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rootName = string.IsNullOrEmpty(Path.GetFileName(root)) ? root : Path.GetFileName(root);
        // AntdUI Breadcrumb 沒有 separator/gap 屬性，文字緊貼分隔符 → 前後補空白拉開
        breadcrumb.Items.Add(new AntdUI.BreadcrumbItem
        {
            Text = " " + rootName + " ",
            Tag = root,
            IconSvg = "HomeOutlined"
        });

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;

        string relative = path.Substring(root.Length)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(relative)) return;

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = relative.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        string accum = root;
        foreach (var seg in segments)
        {
            accum = Path.Combine(accum, seg);
            breadcrumb.Items.Add(new AntdUI.BreadcrumbItem { Text = " " + seg + " ", Tag = accum });
        }
    }

    private void RefreshCurrentTab()
    {
        if (tabsContent.SelectedTab?.Tag is not string path) return;
        var flow = tabsContent.SelectedTab.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (flow != null) _ = LoadGalleryAsync(flow, path);
    }

    private const string LazyPlaceholderTag = "__LAZY__";

    private void LoadSubDirectories(AntdUI.TreeItem node, string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var subDirs = dir.GetDirectories()
                             .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                             .OrderBy(d => d.Name)
                             .ToList();

            // 仍是 placeholder 或實際數量不符 → 重建
            bool isPlaceholder = node.Sub.Count == 1
                                 && (node.Sub[0].Tag as string) == LazyPlaceholderTag;
            if (isPlaceholder || node.Sub.Count != subDirs.Count)
            {
                node.Sub.Clear();
                foreach (var subDir in subDirs)
                {
                    node.Sub.Add(CreateFolderNode(subDir));
                }
                UpdateTreeWidth(); // 內容增加，重新計算寬度
            }
        }
        catch { }
    }

    private void UpdateTreeWidth()
    {
        if (treeFolders.Items.Count == 0) return;

        int maxWidth = 250; // 預設最小寬度
        using (var g = treeFolders.CreateGraphics())
        {
            foreach (var item in treeFolders.Items)
            {
                int itemWidth = MeasureNodeWidth(g, item, 0);
                if (itemWidth > maxWidth) maxWidth = itemWidth;
            }
        }

        // 設定寬度，加上一點緩衝 (Padding)
        treeFolders.Width = maxWidth + 40;
    }

    private int MeasureNodeWidth(Graphics g, AntdUI.TreeItem item, int level)
    {
        // 公式：(層級 * 縮排) + 圖示(約24) + 文字寬度
        var size = g.MeasureString(item.Text, treeFolders.Font);
        int currentWidth = (level * 20) + 30 + (int)size.Width;

        int maxSubWidth = currentWidth;
        if (item.Expand && item.Sub.Count > 0)
        {
            foreach (var subItem in item.Sub)
            {
                int subWidth = MeasureNodeWidth(g, subItem, level + 1);
                if (subWidth > maxSubWidth) maxSubWidth = subWidth;
            }
        }
        return maxSubWidth;
    }

    private AntdUI.TreeItem CreateFolderNode(DirectoryInfo dir)
    {
        var node = new AntdUI.TreeItem(dir.Name)
        {
            Tag = dir.FullName,
            IconSvg = "FolderOpenOutlined"
        };

        // 為了讓 TreeView 顯示展開箭頭，我們預設檢查是否含有子目錄
        try
        {
            if (dir.GetDirectories().Any(d => (d.Attributes & FileAttributes.Hidden) == 0))
            {
                // 先加一個空的佔位，確保呈現可展開狀態
                node.Sub.Add(new AntdUI.TreeItem("Loading...") { Tag = LazyPlaceholderTag });
            }
        }
        catch { }

        return node;
    }

    private void OpenFolderTab(string path, bool forceNew)
    {
        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        UpdateBreadcrumb(path);

        // 如果不是強制另開，且目前有選中的頁籤，則更新目前頁籤
        if (!forceNew && tabsContent.SelectedTab != null)
        {
            var currentTab = tabsContent.SelectedTab;
            // 避免重複載入同一個路徑
            if ((string?)currentTab.Tag == path) return;

            currentTab.Text = folderName;
            currentTab.Tag = path;
            var flow = currentTab.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (flow != null)
            {
                _ = LoadGalleryAsync(flow, path);
                return;
            }
        }

        // 否則，尋找是否已有同路徑的頁籤 (避免重複另開)
        var existingTab = tabsContent.Pages.Cast<AntdUI.TabPage>().FirstOrDefault(p => (string?)p.Tag == path);
        if (existingTab != null)
        {
            tabsContent.SelectedTab = existingTab;
            return;
        }

        var newPage = new AntdUI.TabPage
        {
            Text = folderName,
            Tag = path,
            Padding = new Padding(10),
            BackColor = Color.White
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
                           .OrderBy(i => i is DirectoryInfo ? 0 : 1) // 資料夾排前面
                           .ThenBy(i => i.Name)
                           .ToList();

        // 根據模式設定參數
        Size cardSize;
        int picHeight = 200;
        float fontSize = 9F;
        bool isListMode = _currentViewMode == GalleryViewMode.List;

        switch (_currentViewMode)
        {
            case GalleryViewMode.Small:
                cardSize = new Size(120, 180);
                picHeight = 145;
                fontSize = 8.5F;
                break;
            case GalleryViewMode.Large:
                cardSize = new Size(240, 360);
                picHeight = 310;
                fontSize = 10F;
                break;
            case GalleryViewMode.List:
                cardSize = new Size(container.Width - 40, 60);
                break;
            case GalleryViewMode.Medium:
            default:
                cardSize = new Size(160, 240);
                picHeight = 200;
                break;
        }

        foreach (var item in items)
        {
            bool isFolderNode = false;
            if (item is DirectoryInfo subDir)
            {
                isFolderNode = subDir.GetDirectories().Length > 0;
            }

            // 1. 建立卡片
            var card = new AntdUI.Panel
            {
                Size = cardSize,
                Radius = isListMode ? 4 : 8,
                Margin = isListMode ? new Padding(5, 2, 5, 2) : new Padding(12),
                BackColor = Color.White,
                Shadow = isListMode ? 2 : 4,
                Cursor = Cursors.Hand
            };

            var pic = new System.Windows.Forms.PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(245, 245, 245),
                Cursor = Cursors.Hand
            };

            var lbl = new AntdUI.Label
            {
                Text = item.Name,
                Font = new Font("Microsoft JhengHei UI", fontSize),
                Cursor = Cursors.Hand
            };

            if (isListMode)
            {
                pic.Dock = DockStyle.Left;
                pic.Width = 50;
                lbl.Dock = DockStyle.Fill;
                lbl.TextAlign = ContentAlignment.MiddleLeft;
                lbl.Padding = new Padding(10, 0, 0, 0);
            }
            else
            {
                pic.Dock = DockStyle.Top;
                pic.Height = picHeight;
                lbl.Dock = DockStyle.Fill;
                lbl.TextAlign = ContentAlignment.MiddleCenter;
            }

            // 2. 統一點擊處理
            MouseEventHandler clickHandler = (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (isFolderNode) OpenFolderTab(item.FullName, false); // 進入子目錄
                    else OpenWithExternalViewer(item.FullName); // 閱讀漫畫
                }
                else if (e.Button == MouseButtons.Right)
                {
                    // 右鍵選單
                    var menuItems = new List<AntdUI.IContextMenuStripItem>();
                    if (isFolderNode) menuItems.Add(new AntdUI.ContextMenuStripItem("📂 進入目錄"));
                    else menuItems.Add(new AntdUI.ContextMenuStripItem("👁 閱讀"));

                    menuItems.Add(new AntdUI.ContextMenuStripItem("另開頁籤"));
                    menuItems.Add(new AntdUI.ContextMenuStripItemDivider());
                    menuItems.Add(new AntdUI.ContextMenuStripItem("📂 在檔案總管中開啟"));

                    AntdUI.ContextMenuStrip.open((Control)s!, (menuItem) =>
                    {
                        if (menuItem.Text != null && (menuItem.Text.Contains("進入") || menuItem.Text.Contains("閱讀")))
                        {
                            if (isFolderNode) OpenFolderTab(item.FullName, false);
                            else OpenWithExternalViewer(item.FullName);
                        }
                        else if (menuItem.Text == "另開頁籤")
                        {
                            OpenFolderTab(item.FullName, true);
                        }
                        else if (menuItem.Text != null && menuItem.Text.Contains("檔案總管"))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullName}\"");
                        }
                    }, menuItems.ToArray());
                }
            };

            card.MouseClick += clickHandler;
            pic.MouseClick += clickHandler;
            lbl.MouseClick += clickHandler;

            // 3. Hover 效果處理
            Action<bool> setHoverState = (hover) =>
            {
                if (hover)
                {
                    card.Back = Color.FromArgb(240, 245, 255); // 極淺藍色
                    card.Shadow = isListMode ? 3 : 8; // 加深陰影
                    lbl.BringToFront(); // 確保文字不被遮蓋
                }
                else
                {
                    var clientPos = card.PointToClient(Control.MousePosition);
                    if (!card.ClientRectangle.Contains(clientPos))
                    {
                        card.Back = Color.White;
                        card.Shadow = isListMode ? 2 : 4;
                    }
                }
            };

            card.MouseEnter += (s, e) => setHoverState(true);
            pic.MouseEnter += (s, e) => setHoverState(true);
            lbl.MouseEnter += (s, e) => setHoverState(true);
            card.MouseLeave += (s, e) => setHoverState(false);
            pic.MouseLeave += (s, e) => setHoverState(false);
            lbl.MouseLeave += (s, e) => setHoverState(false);

            lbl.BackColor = Color.Transparent;

            card.Controls.Add(lbl);
            card.Controls.Add(pic);
            container.Controls.Add(card);

            // 4. 異步載入封面
            if (isFolderNode)
            {
                pic.Image = null;
                pic.BackColor = Color.FromArgb(230, 244, 255);
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    int thumbWidth = isListMode ? 80 : cardSize.Width;
                    var cover = await _coverManager.GetCoverAsync(item.FullName, thumbWidth);
                    if (cover != null)
                    {
                        this.Invoke(() => pic.Image = cover);
                    }
                });
            }
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

        // 設定內容頁籤樣式：開啟關閉按鈕與變更配色
        if (tabsContent.Style is AntdUI.Tabs.StyleCard style)
        {
            style.Closable = true;
        }
        tabsContent.CloseDisposePage = true; // 關閉頁籤時自動移除
        tabsContent.FillActive = Color.FromArgb(22, 119, 255); // 使用主題藍色作為選中色
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

    private void treeFolders_BeforeExpand(object sender, TreeExpandEventArgs e)
    {
        if (e.Item.Tag is string path)
        {
            LoadSubDirectories(e.Item, path);
        }
        e.CanExpand = true;
    }

    private void treeFolders_NodeMouseDoubleClick(object sender, TreeSelectEventArgs e)
    {
        if (e.Item.Tag is string path)
        {
            OpenFolderTab(path, false);
        }
    }

    private async void MainForm_Load(object sender, EventArgs e)
    {
        await LoadTreeAsync();
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

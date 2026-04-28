using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;

namespace ComicOrganize.App.UI;

public partial class GalleryControl : UserControl
{
    public enum GalleryViewMode { Small, Medium, Large, List }
    private GalleryViewMode _currentViewMode = GalleryViewMode.Medium;

    private AppConfig _config = null!;
    private CoverManager _coverManager = null!;

    private const string LazyPlaceholderTag = "__LAZY__";

    public GalleryControl()
    {
        InitializeComponent();

        // 設定內容頁籤樣式
        if (tabsContent.Style is AntdUI.Tabs.StyleCard style)
        {
            style.Closable = true;
        }
        tabsContent.CloseDisposePage = true;
        tabsContent.FillActive = Color.FromArgb(22, 119, 255);

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

        // 重新整理按鈕
        btnRefresh.Click += (s, e) => RefreshCurrentTab();

        // Tree 右鍵選單
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
            return true;
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

    public void Initialize(AppConfig config, CoverManager coverManager)
    {
        _config = config;
        _coverManager = coverManager;
        _ = LoadTreeAsync();
    }

    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            RefreshCurrentTab();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
                LoadSubDirectories(rootNode, rootDir.FullName);
                UpdateTreeWidth();
            });
        });
    }

    private void UpdateBreadcrumb(string path)
    {
        // 大字目錄名稱：顯示當前 Tab 對應資料夾名（路徑最後一段）
        if (!string.IsNullOrEmpty(path))
        {
            string name = Path.GetFileName(path);
            lblFolderName.Text = string.IsNullOrEmpty(name) ? path : name;
        }
        else
        {
            lblFolderName.Text = "";
        }

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
                UpdateTreeWidth();
            }
        }
        catch { }
    }

    private void UpdateTreeWidth()
    {
        if (treeFolders.Items.Count == 0) return;

        int maxWidth = 250;
        using (var g = treeFolders.CreateGraphics())
        {
            foreach (var item in treeFolders.Items)
            {
                int itemWidth = MeasureNodeWidth(g, item, 0);
                if (itemWidth > maxWidth) maxWidth = itemWidth;
            }
        }

        treeFolders.Width = maxWidth + 40;
    }

    private int MeasureNodeWidth(Graphics g, AntdUI.TreeItem item, int level)
    {
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

        try
        {
            if (dir.GetDirectories().Any(d => (d.Attributes & FileAttributes.Hidden) == 0))
            {
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

        if (!forceNew && tabsContent.SelectedTab != null)
        {
            var currentTab = tabsContent.SelectedTab;
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

        var flowLayout = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White
        };
        newPage.Controls.Add(flowLayout);

        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        _ = LoadGalleryAsync(flowLayout, path);
    }

    private async Task LoadGalleryAsync(FlowLayoutPanel container, string path)
    {
        container.Controls.Clear();
        var dirInfo = new DirectoryInfo(path);

        var items = dirInfo.GetFileSystemInfos()
                           .Where(i => (i.Attributes & FileAttributes.Hidden) == 0)
                           .OrderBy(i => i is DirectoryInfo ? 0 : 1)
                           .ThenBy(i => i.Name)
                           .ToList();

        bool isListMode = _currentViewMode == GalleryViewMode.List;

        foreach (var item in items)
        {
            bool isFolderNode = item is DirectoryInfo subDir
                                 && subDir.GetDirectories().Length > 0;

            var card = new ComicCardControl();
            card.SetData(item, isFolderNode);
            card.ApplyViewMode(_currentViewMode);
            card.RequestOpenFolder += p => OpenFolderTab(p, false);
            card.RequestOpenViewer += p => OpenWithExternalViewer(p);
            card.RequestNewTab += (p, forceNew) => OpenFolderTab(p, forceNew);
            card.RequestExplorer += p =>
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{p}\"");
            container.Controls.Add(card);

            if (!isFolderNode)
            {
                int thumbWidth = isListMode ? 80 : card.Width;
                _ = Task.Run(async () =>
                {
                    var cover = await _coverManager.GetCoverAsync(item.FullName, thumbWidth);
                    if (cover != null)
                    {
                        this.Invoke(() => card.SetCover(cover));
                    }
                });
            }
        }

        await Task.CompletedTask;
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "開啟失敗: " + ex.Message);
        }
    }

    private void treeFolders_BeforeExpand(object sender, AntdUI.TreeExpandEventArgs e)
    {
        if (e.Item.Tag is string path)
        {
            LoadSubDirectories(e.Item, path);
        }
        e.CanExpand = true;
    }

    private void treeFolders_NodeMouseDoubleClick(object sender, AntdUI.TreeSelectEventArgs e)
    {
        if (e.Item.Tag is string path)
        {
            OpenFolderTab(path, false);
        }
    }
}

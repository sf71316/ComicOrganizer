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
        tabsContent.FillActive = Core.ThemeColors.Border;

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

        // 每個 Tab 自帶 breadcrumb，內容在 OpenFolderTab 建立 / 更新當前 Tab 時填好，
        // 切 Tab 不需重填，因此不綁 SelectedIndexChanged。
    }

    public void Initialize(AppConfig config, CoverManager coverManager)
    {
        _config = config;
        _coverManager = coverManager;
        _ = LoadTreeAsync();
    }

    public GalleryViewMode CurrentViewMode => _currentViewMode;

    public void SetViewMode(GalleryViewMode mode)
    {
        if (_currentViewMode == mode) return;
        _currentViewMode = mode;
        RefreshCurrentTab();
    }

    public void OnConfigUpdated(AppConfig config)
    {
        _config = config;
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

    // 找出當前 Tab 內的 breadcrumb / FlowLayout（依控件 Name 找）
    private AntdUI.Breadcrumb? FindTabBreadcrumb(AntdUI.TabPage? page)
    {
        if (page == null) return null;
        return page.Controls.Find("tabBreadcrumb", true).OfType<AntdUI.Breadcrumb>().FirstOrDefault();
    }

    private FlowLayoutPanel? FindTabFlowLayout(AntdUI.TabPage? page)
    {
        if (page == null) return null;
        return page.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
    }

    private void UpdateBreadcrumb(string path)
    {
        var bc = FindTabBreadcrumb(tabsContent.SelectedTab);
        if (bc == null) return;

        bc.Items.Clear();
        if (string.IsNullOrEmpty(_config.RootDirectory) || string.IsNullOrEmpty(path)) return;

        string root = _config.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rootName = string.IsNullOrEmpty(Path.GetFileName(root)) ? root : Path.GetFileName(root);
        // AntdUI Breadcrumb 沒有 separator/gap 屬性，文字緊貼分隔符 → 前後補空白拉開
        bc.Items.Add(new AntdUI.BreadcrumbItem
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
            bc.Items.Add(new AntdUI.BreadcrumbItem { Text = " " + seg + " ", Tag = accum });
        }
    }

    private void RefreshCurrentTab()
    {
        if (tabsContent.SelectedTab?.Tag is not string path) return;
        var flow = FindTabFlowLayout(tabsContent.SelectedTab);
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

        if (!forceNew && tabsContent.SelectedTab != null)
        {
            var currentTab = tabsContent.SelectedTab;
            if ((string?)currentTab.Tag == path) return;

            currentTab.Text = folderName;
            currentTab.Tag = path;
            var flow = FindTabFlowLayout(currentTab);
            if (flow != null)
            {
                UpdateBreadcrumb(path);
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

        // 新 Tab 結構：headerPanel (Dock=Top, 麵包屑 + refresh) + FlowLayoutPanel (Dock=Fill, 卡片區)
        var newPage = new AntdUI.TabPage
        {
            Text = folderName,
            Tag = path,
            BackColor = Core.ThemeColors.Surface
        };

        var headerPanel = new System.Windows.Forms.Panel
        {
            Name = "tabHeader",
            Dock = DockStyle.Top,
            Height = 33,
            BackColor = Core.ThemeColors.Background,
        };

        var bc = new AntdUI.Breadcrumb
        {
            Name = "tabBreadcrumb",
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 0, 8, 0),
            Font = new Font("Microsoft JhengHei UI", 12F),
        };
        bc.ItemClick += (s, e) =>
        {
            if (e.Item.Tag is string p) OpenFolderTab(p, false);
        };

        var btnRefresh = new AntdUI.Button
        {
            Name = "tabBtnRefresh",
            Dock = DockStyle.Right,
            Ghost = true,
            IconSvg = "RedoOutlined",
            IconRatio = 0.9F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = new Size(40, 32),
        };
        btnRefresh.Click += (s, e) => RefreshCurrentTab();

        headerPanel.Controls.Add(bc);
        headerPanel.Controls.Add(btnRefresh);

        var flowLayout = new System.Windows.Forms.FlowLayoutPanel
        {
            Name = "tabFlowLayout",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Core.ThemeColors.Surface
        };

        newPage.Controls.Add(flowLayout);
        newPage.Controls.Add(headerPanel);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        UpdateBreadcrumb(path);
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

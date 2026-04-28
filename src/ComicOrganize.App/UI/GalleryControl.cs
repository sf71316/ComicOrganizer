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

            MouseEventHandler clickHandler = (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (isFolderNode) OpenFolderTab(item.FullName, false);
                    else OpenWithExternalViewer(item.FullName);
                }
                else if (e.Button == MouseButtons.Right)
                {
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

            Action<bool> setHoverState = (hover) =>
            {
                if (hover)
                {
                    card.Back = Color.FromArgb(240, 245, 255);
                    card.Shadow = isListMode ? 3 : 8;
                    lbl.BringToFront();
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

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
        SyncTabVisibility(tabsContent.SelectedTab);
        RefreshCurrentTab();
    }

    private void SyncTabVisibility(AntdUI.TabPage? page)
    {
        if (page == null) return;
        var flow = FindTabFlowLayout(page);
        var table = FindTabTable(page);
        bool isList = _currentViewMode == GalleryViewMode.List;
        if (flow != null) flow.Visible = !isList;
        if (table != null) table.Visible = isList;
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
        return page.Controls.Find("tabFlowLayout", true).OfType<FlowLayoutPanel>().FirstOrDefault();
    }

    private AntdUI.Table? FindTabTable(AntdUI.TabPage? page)
    {
        if (page == null) return null;
        return page.Controls.Find("tabTableList", true).OfType<AntdUI.Table>().FirstOrDefault();
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
        LoadCurrentViewMode(tabsContent.SelectedTab, path);
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
            UpdateBreadcrumb(path);
            LoadCurrentViewMode(currentTab, path);
            return;
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

        var contentHost = new System.Windows.Forms.Panel
        {
            Name = "tabContentHost",
            Dock = DockStyle.Fill,
            BackColor = Core.ThemeColors.Surface,
        };

        bool isList = _currentViewMode == GalleryViewMode.List;

        var flowLayout = new System.Windows.Forms.FlowLayoutPanel
        {
            Name = "tabFlowLayout",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Core.ThemeColors.Surface,
            Visible = !isList,
        };

        var tableList = CreateTabTable();
        tableList.Visible = isList;

        // 縮圖 hover → 滑鼠附近 floating preview popup（只在縮圖欄觸發）
        // column 順序：Action(44) | Thumbnail(80) | Name | Size | CreatedAt
        const int thumbColStart = 44;
        const int thumbColEnd = 44 + 80;
        string? currentPreviewPath = null;
        tableList.MouseMove += (s, e) =>
        {
            int headerH = tableList.RowHeightHeader ?? 40;
            int rowH = tableList.RowHeight ?? 64;
            if (e.X < thumbColStart || e.X >= thumbColEnd || e.Y < headerH
                || tableList.DataSource is not System.ComponentModel.BindingList<GalleryListItem> list)
            {
                HidePreviewPopup();
                currentPreviewPath = null;
                return;
            }
            int rowIdx = (e.Y - headerH) / rowH;
            if (rowIdx < 0 || rowIdx >= list.Count || string.IsNullOrEmpty(list[rowIdx].FullPath))
            {
                HidePreviewPopup();
                currentPreviewPath = null;
                return;
            }
            var item = list[rowIdx];
            UpdatePreviewPopupPosition();
            if (currentPreviewPath == item.FullPath) return;
            currentPreviewPath = item.FullPath;
            _ = LoadPreviewPopupAsync(item.FullPath);
        };
        tableList.MouseLeave += (s, e) =>
        {
            currentPreviewPath = null;
            HidePreviewPopup();
        };

        // 「進入下一層」icon button click → 進入該資料夾
        tableList.CellButtonClick += (object s, AntdUI.TableButtonEventArgs args) =>
        {
            if (args.Btn?.Id != "OpenFolder") return;
            GalleryListItem? item = args.Record as GalleryListItem;
            if (item == null && tableList.DataSource is System.ComponentModel.BindingList<GalleryListItem> list
                && args.RowIndex >= 0 && args.RowIndex < list.Count)
            {
                item = list[args.RowIndex];
            }
            if (item != null && Directory.Exists(item.FullPath))
            {
                OpenFolderTab(item.FullPath, false);
            }
        };

        // 右鍵選單（原 hoverToolbar 功能）
        tableList.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            int headerH = tableList.RowHeightHeader ?? 40;
            int rowH = tableList.RowHeight ?? 64;
            if (e.Y < headerH || tableList.DataSource is not System.ComponentModel.BindingList<GalleryListItem> list)
                return;
            int rowIdx = (e.Y - headerH) / rowH;
            if (rowIdx < 0 || rowIdx >= list.Count) return;
            var item = list[rowIdx];
            if (string.IsNullOrEmpty(item.FullPath)) return;

            var items = new List<AntdUI.IContextMenuStripItem>
            {
                new AntdUI.ContextMenuStripItem("📖 閱讀"),
                new AntdUI.ContextMenuStripItem("📂 檔案總管中開啟"),
            };
            if (Directory.Exists(item.FullPath))
            {
                items.Insert(0, new AntdUI.ContextMenuStripItem("🔗 另開頁籤"));
            }
            AntdUI.ContextMenuStrip.open(tableList, mi =>
            {
                if (mi.Text == "📖 閱讀") OpenWithExternalViewer(item.FullPath);
                else if (mi.Text == "📂 檔案總管中開啟")
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
                else if (mi.Text == "🔗 另開頁籤") OpenFolderTab(item.FullPath, true);
            }, items.ToArray());
        };

        contentHost.Controls.Add(flowLayout);
        contentHost.Controls.Add(tableList);

        newPage.Controls.Add(contentHost);
        newPage.Controls.Add(headerPanel);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        UpdateBreadcrumb(path);
        LoadCurrentViewMode(newPage, path);
    }

    private AntdUI.Table CreateTabTable()
    {
        var table = new AntdUI.Table
        {
            Name = "tabTableList",
            Dock = DockStyle.Fill,
            Bordered = false,
            RowHeight = (int?)64,
            AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill,
            Font = new Font("Microsoft JhengHei UI", 10F),
            BackColor = Core.ThemeColors.Surface,
        };

        table.Columns = new AntdUI.ColumnCollection
        {
            new AntdUI.Column("Action", "", AntdUI.ColumnAlign.Center) { Width = "44" },
            new AntdUI.Column("Thumbnail", "縮圖", AntdUI.ColumnAlign.Center) { Width = "80" },
            new AntdUI.Column("Name", "名稱") { SortOrder = true },
            new AntdUI.Column("Size", "大小", AntdUI.ColumnAlign.Right) { Width = "100", SortOrder = true },
            new AntdUI.Column("CreatedAt", "建立時間", AntdUI.ColumnAlign.Center) { Width = "160", SortOrder = true },
        };
        table.EnableHeaderResizing = true;

        // 點擊 row highlight
        table.RowSelectedBg = Core.ThemeColors.Secondary;

        // zebra：奇偶行不同底色
        table.SetRowStyle += (object s, AntdUI.TableSetRowStyleEventArgs e) =>
        {
            return new AntdUI.Table.CellStyleInfo
            {
                BackColor = e.Index % 2 == 0 ? Color.White : Core.ThemeColors.RowAlternateBg,
            };
        };

        return table;
    }

    private async Task LoadPreviewAsync(System.Windows.Forms.PictureBox pic, string path)
    {
        var img = await _coverManager.GetCoverAsync(path, 300);
        if (img == null || pic.IsDisposed) return;
        this.Invoke(() => { pic.Image = img; });
    }

    // ===== 滑鼠附近 floating preview popup =====
    private HoverPreviewForm? _previewForm;
    private System.Windows.Forms.PictureBox? _previewPic;

    private void EnsurePreviewPopup()
    {
        if (_previewForm != null) return;
        _previewForm = new HoverPreviewForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            Size = new Size(240, 320),
            BackColor = Color.White,
        };
        _previewPic = new System.Windows.Forms.PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _previewForm.Controls.Add(_previewPic);
    }

    private async Task LoadPreviewPopupAsync(string path)
    {
        EnsurePreviewPopup();
        var img = await _coverManager.GetCoverAsync(path, 300);
        if (img == null || _previewForm == null || _previewPic == null) return;
        this.Invoke(() =>
        {
            _previewPic.Image = img;
            UpdatePreviewPopupPosition();
            if (!_previewForm.Visible) _previewForm.Show();
        });
    }

    private void UpdatePreviewPopupPosition()
    {
        if (_previewForm == null) return;
        var cur = Cursor.Position;
        var screen = Screen.FromPoint(cur).WorkingArea;
        int x = cur.X + 20;
        int y = cur.Y + 20;
        if (x + _previewForm.Width > screen.Right) x = cur.X - _previewForm.Width - 20;
        if (y + _previewForm.Height > screen.Bottom) y = screen.Bottom - _previewForm.Height - 5;
        _previewForm.Location = new Point(x, y);
    }

    private void HidePreviewPopup()
    {
        if (_previewForm != null && _previewForm.Visible) _previewForm.Hide();
    }

    private class HoverPreviewForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }
    }


    private void LoadCurrentViewMode(AntdUI.TabPage page, string path)
    {
        if (_currentViewMode == GalleryViewMode.List)
        {
            var table = FindTabTable(page);
            if (table != null) _ = LoadGalleryListAsync(table, path);
        }
        else
        {
            var flow = FindTabFlowLayout(page);
            if (flow != null) _ = LoadGalleryAsync(flow, path);
        }
    }

    private async Task LoadGalleryListAsync(AntdUI.Table table, string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var fsInfos = dirInfo.GetFileSystemInfos()
                             .Where(i => (i.Attributes & FileAttributes.Hidden) == 0)
                             .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                             .ToList();

        var list = new System.ComponentModel.BindingList<GalleryListItem>();
        var thumbTasks = new List<Task>();

        foreach (var fs in fsInfos)
        {
            bool isDir = fs is DirectoryInfo;
            // IsFolder 語意：「純導航資料夾」（子目錄裡還有子目錄），跟卡片模式對齊。
            bool isPureFolderNode = isDir && ((DirectoryInfo)fs).GetDirectories().Length > 0;
            long size = isDir ? 0 : ((FileInfo)fs).Length;
            var item = new GalleryListItem
            {
                Name = fs.Name,
                Size = isDir ? "" : FormatSize(size),
                CreatedAt = fs.CreationTime.ToString("yyyy-MM-dd HH:mm"),
                IsFolder = isPureFolderNode,
                FullPath = fs.FullName,
                Action = isDir
                    ? new AntdUI.CellLink[]
                    {
                        new AntdUI.CellButton("OpenFolder", "", AntdUI.TTypeMini.Default)
                        {
                            Ghost = true,
                            IconSvg = "RightOutlined",
                        }
                    }
                    : null,
            };
            list.Add(item);

            // 純導航節點不抓縮圖
            if (!isPureFolderNode)
            {
                var path2 = fs.FullName;
                thumbTasks.Add(Task.Run(async () =>
                {
                    var img = await _coverManager.GetCoverAsync(path2, 100);
                    if (img != null) item.Thumbnail = new AntdUI.CellImage(img, 60) { Radius = 4 };
                }));
            }
        }

        // 先 set 不含縮圖的列表（使用者可立即看到資料）
        table.DataSource = list;

        if (thumbTasks.Count == 0) return;

        // AntdUI Table 對 ListChanged/PropertyChanged 不 hook，唯一可靠重繪辦法是
        // 重 set DataSource。等所有縮圖抓完一次重 set，cell 才會顯示 CellImage。
        await Task.WhenAll(thumbTasks);
        if (this.IsDisposed) return;
        this.Invoke(() =>
        {
            table.DataSource = null;
            table.DataSource = list;
        });
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
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

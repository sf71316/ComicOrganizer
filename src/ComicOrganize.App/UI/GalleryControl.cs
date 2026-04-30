using AntdUI;
using ComicOrganize.App.Core;
using ComicOrganize.App.Models;

namespace ComicOrganize.App.UI;

public partial class GalleryControl : UserControl, IThemedControl
{
    public enum GalleryViewMode { Small, Medium, Large, List }
    private GalleryViewMode _currentViewMode = GalleryViewMode.Medium;

    private AppConfig _config = null!;
    private CoverManager _coverManager = null!;

    private const string LazyPlaceholderTag = "__LAZY__";

    public GalleryControl()
    {
        InitializeComponent();

        // GalleryControl 自己開 DoubleBuffer：tabsContent 切換 / resize 時減抖
        DoubleBuffered = true;

        // 設定內容頁籤樣式
        if (tabsContent.Style is AntdUI.Tabs.StyleCard style)
        {
            style.Closable = true;
        }
        tabsContent.CloseDisposePage = true;
        tabsContent.FillActive = Core.ThemeColors.Border;

        this.WireThemeAndLocalization();

        // Tree 右鍵選單
        treeFolders.NodeMouseClick += (s, e) =>
        {
            if (e.Item.Tag is string path && e.Button == MouseButtons.Right)
            {
                AntdUI.ContextMenuStrip.open(treeFolders, (menuItem) =>
                {
                    if (menuItem.Text == LocalizationManager.GetString("MenuOpenTab"))
                    {
                        OpenFolderTab(path, true);
                    }
                    else if (menuItem.Text != null && menuItem.Text.Contains(LocalizationManager.GetString("MenuRefresh")))
                    {
                        e.Item.Sub.Clear();
                        e.Item.Sub.Add(new AntdUI.TreeItem(LocalizationManager.GetString("Loading")) { Tag = LazyPlaceholderTag });
                        LoadSubDirectories(e.Item, path);
                    }
                }, new AntdUI.IContextMenuStripItem[]
                {
                    new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenTab")),
                    new AntdUI.ContextMenuStripItem("🔄 " + LocalizationManager.GetString("MenuRefresh")),
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
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        // 更新 Tree 中的 "Loading..." 節點文字（如果存在）
        UpdateTreeLoadingText(treeFolders.Items);
    }

    public void ApplyThemeColors()
    {
        splitter2.Panel1.BackColor = Core.ThemeColors.Border;
        splitter2.Panel2.BackColor = Core.ThemeColors.Border;
        treeFolders.BackColor = Core.ThemeColors.TreeViewSurface;
        tabsContent.ScrollBack = Core.ThemeColors.Text;
        tabsContent.FillActive = Core.ThemeColors.Border;

        // 動態建立的 Tab 內 panel 也要重套
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            page.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabContentHost", true).OfType<System.Windows.Forms.Panel>())
                p.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabHeader", true).OfType<System.Windows.Forms.Panel>())
                p.BackColor = Core.ThemeColors.Background;
            foreach (var f in page.Controls.Find("tabFlowLayout", true).OfType<FlowLayoutPanel>())
                f.BackColor = Core.ThemeColors.Surface;
            foreach (var t in page.Controls.Find("tabTableList", true).OfType<AntdUI.Table>())
                t.BackColor = Core.ThemeColors.Surface;
        }
        this.Refresh();
    }

    private void UpdateTreeLoadingText(AntdUI.TreeItemCollection items)
    {
        foreach (var item in items)
        {
            if (item.Tag as string == LazyPlaceholderTag)
            {
                item.Text = LocalizationManager.GetString("Loading");
            }
            if (item.Sub.Count > 0)
            {
                UpdateTreeLoadingText(item.Sub);
            }
        }
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
        var oldMode = _currentViewMode;
        _currentViewMode = mode;
        SyncTabVisibility(tabsContent.SelectedTab);

        // 卡片模式之間切換 (Small/Medium/Large) 不重 load — 只 iterate 現有卡片改尺寸。
        // 涉及 List 才需要 reload (FlowLayoutPanel ↔ Table 不同資料結構)。
        bool oldIsCards = oldMode != GalleryViewMode.List;
        bool newIsCards = mode != GalleryViewMode.List;
        if (oldIsCards && newIsCards)
        {
            ResizeCardsOnCurrentTab();
        }
        else
        {
            RefreshCurrentTab();
        }
    }

    private void ResizeCardsOnCurrentTab()
    {
        var page = tabsContent.SelectedTab;
        var flow = FindTabFlowLayout(page);
        if (flow == null) return;
        flow.SuspendLayout();
        foreach (var c in flow.Controls.OfType<ComicCardControl>())
        {
            c.ApplyViewMode(_currentViewMode);
        }
        flow.ResumeLayout(true);
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

    private Task LoadTreeAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory)) return Task.CompletedTask;

        treeFolders.Items.Clear();
        AntdUI.Spin.open(splitter2.Panel1, async config =>
        {
            var rootDir = new DirectoryInfo(_config.RootDirectory);
            var rootNode = await Task.Run(() => CreateFolderNode(rootDir));
            this.Invoke(() =>
            {
                treeFolders.Items.Add(rootNode);
                rootNode.Expand = true;
                LoadSubDirectories(rootNode, rootDir.FullName);
                UpdateTreeWidth();
            });
        });
        return Task.CompletedTask;
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
        // 已展開過就不重掃（外部變動靠右鍵「重新整理」手動觸發）
        bool isPlaceholder = node.Sub.Count == 1
                             && (node.Sub[0].Tag as string) == LazyPlaceholderTag;
        if (!isPlaceholder) return;

        // 改 async：UI thread 不阻塞，使用者雙擊 tree node 觸發 expand 不卡頓
        Task.Run(() =>
        {
            List<DirectoryInfo> subDirs;
            try
            {
                subDirs = new DirectoryInfo(path).GetDirectories()
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d.Name)
                    .ToList();
            }
            catch { return; }

            if (this.IsDisposed) return;
            try
            {
                this.Invoke(() =>
                {
                    if (treeFolders.IsDisposed) return;
                    node.Sub.Clear();
                    foreach (var subDir in subDirs)
                    {
                        node.Sub.Add(CreateFolderNode(subDir));
                    }
                    UpdateTreeWidth();
                });
            }
            catch { /* form/tree 已 dispose 等競態忽略 */ }
        });
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
            Height = LogicalToDeviceUnits(33),  // DPI 150% 等高 → 50px，避免麵包屑文字裁切
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
            Size = LogicalToDeviceUnits(new Size(40, 32)),
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

        var flowLayout = new BufferedFlowLayoutPanel
        {
            Name = "tabFlowLayout",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Core.ThemeColors.Surface,
            Visible = !isList,
        };

        var tableList = CreateTabTable();
        tableList.Visible = isList;

        // 縮圖 hover → 滑鼠附近 floating preview popup
        // 用 AntdUI.Table.HitTest 取 cell；
        //  - 資料：cell.Row.RECORD 直接讀綁定物件，避開 i_row 索引偏移
        //  - 欄位判斷：cell.COLUMN.Key 比對欄位 key，避開 i_cel 索引偏移
        //    （HitTest 的 i_row/i_cel 對 header/scroll/hidden row 的計法跟 BindingList 索引可能不一致）
        // 只在 Thumbnail 欄觸發 hover preview，避免整列都跳大圖太擾人
        string? currentPreviewPath = null;
        tableList.MouseMove += (s, e) =>
        {
            var cell = tableList.HitTest(e.X, e.Y, out _, out _);
            var item = cell?.Row?.RECORD as GalleryListItem;
            bool onThumbnailCol = cell?.COLUMN?.Key == "Thumbnail";
            if (!onThumbnailCol || item == null || string.IsNullOrEmpty(item.FullPath))
            {
                HidePreviewPopup();
                currentPreviewPath = null;
                return;
            }
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
            var cell = tableList.HitTest(e.X, e.Y, out _, out _);
            // 同 hover 邏輯：直接從 cell.Row.RECORD 取資料，避開 i_row 索引偏移
            var item = cell?.Row?.RECORD as GalleryListItem;
            if (item == null || string.IsNullOrEmpty(item.FullPath)) return;

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
                    OpenInExplorer(item.FullPath);
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
            RowHeight = (int?)80,
            AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill,
            Font = new Font("Microsoft JhengHei UI", 10F),
            BackColor = Core.ThemeColors.Surface,
            GapCell = 2,           // 單元格內間距：預設約 8-12 太大，縮圖欄留白多；改 2px 讓縮圖貼齊
            VirtualMode = true,    // 大量 rows 時只繪 viewport 內，捲動流暢度大幅提升
        };

        table.Columns = new AntdUI.ColumnCollection
        {
            new AntdUI.Column("Action", "", AntdUI.ColumnAlign.Center) { Width = "44" },
            new AntdUI.Column("Thumbnail", "縮圖", AntdUI.ColumnAlign.Center) { Width = "72" },
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

    private Task LoadGalleryListAsync(AntdUI.Table table, string path)
    {
        // [DEBUG] 計時：找出哪一段卡 (移除前先確認瓶頸)
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[List] T+0ms : Spin.open called for {path}");

        AntdUI.Spin.open(this, async config =>
        {
            System.Diagnostics.Debug.WriteLine($"[List] T+{swTotal.ElapsedMilliseconds}ms : Spin callback start");

            // ===== Phase 1: 檔案掃描 =====
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var fsInfos = await Task.Run(() =>
                new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(i => (i.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());
            System.Diagnostics.Debug.WriteLine($"[List] T+{swTotal.ElapsedMilliseconds}ms : scan done ({fsInfos.Count} items, took {sw.ElapsedMilliseconds}ms)");

            sw.Restart();
            var prepared = await Task.Run(() => fsInfos.Select(fs =>
            {
                bool isDir = fs is DirectoryInfo;
                long size = isDir ? 0 : ((FileInfo)fs).Length;
                return (fs, isDir, size);
            }).ToList());
            System.Diagnostics.Debug.WriteLine($"[List] T+{swTotal.ElapsedMilliseconds}ms : prepared (took {sw.ElapsedMilliseconds}ms)");

            sw.Restart();
            var list = new System.ComponentModel.BindingList<GalleryListItem>();
            var thumbTasks = new List<Task>();

            foreach (var (fs, isDir, size) in prepared)
            {
                var item = new GalleryListItem
                {
                    Name = fs.Name,
                    Size = isDir ? "" : FormatSize(size),
                    CreatedAt = fs.CreationTime.ToString("yyyy-MM-dd HH:mm"),
                    IsFolder = isDir,
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

                // 縮圖任務 fire-and-forget — 不在 Spin 內 await，讓 Spin 早點關
                var path2 = fs.FullName;
                thumbTasks.Add(Task.Run(async () =>
                {
                    var img = await _coverManager.GetCoverAsync(path2, 120);
                    // CellImage(img, int) 第二個參數是 Radius 不是 Size！要靠 Size 屬性設邊長。
                    // 60×80 (3:4) 貼合漫畫封面比例；ImageFit.Cover = 等比縮放填滿並切多餘部分（少量上下裁）
                    if (img != null) item.Thumbnail = new AntdUI.CellImage(img)
                    {
                        Size = new Size(60, 80),
                        Radius = 4,
                        ImageFit = AntdUI.TFit.Cover,
                    };
                }));
            }

            System.Diagnostics.Debug.WriteLine($"[List] T+{swTotal.ElapsedMilliseconds}ms : list build done (took {sw.ElapsedMilliseconds}ms)");

            // 第一次 set：文字列表立即可見（縮圖欄空白）
            sw.Restart();
            this.Invoke(() => { table.DataSource = list; });
            System.Diagnostics.Debug.WriteLine($"[List] T+{swTotal.ElapsedMilliseconds}ms : DataSource set (Invoke took {sw.ElapsedMilliseconds}ms) <- USER SHOULD SEE LIST NOW");

            // ===== Phase 2: 縮圖背景載入（Spin 已關閉、列表可見） =====
            if (thumbTasks.Count == 0) return;
            _ = Task.Run(async () =>
            {
                var swThumb = System.Diagnostics.Stopwatch.StartNew();
                await Task.WhenAll(thumbTasks);
                System.Diagnostics.Debug.WriteLine($"[List] thumbnails done in {swThumb.ElapsedMilliseconds}ms");
                if (this.IsDisposed) return;
                try
                {
                    this.Invoke(() =>
                    {
                        if (table.IsDisposed) return;
                        var swReset = System.Diagnostics.Stopwatch.StartNew();
                        table.DataSource = null;
                        table.DataSource = list;
                        System.Diagnostics.Debug.WriteLine($"[List] thumbnail reset DataSource took {swReset.ElapsedMilliseconds}ms");
                    });
                }
                catch { /* form/table 已 dispose 等競態忽略 */ }
            });
        });

        return Task.CompletedTask;
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

    private Task LoadGalleryAsync(FlowLayoutPanel container, string path)
    {
        container.Controls.Clear();

        // [DEBUG] 計時：找出哪一段卡
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[Cards] T+0ms : Spin.open called for {path}");

        AntdUI.Spin.open(container, async config =>
        {
            System.Diagnostics.Debug.WriteLine($"[Cards] T+{swTotal.ElapsedMilliseconds}ms : Spin callback start");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var items = await Task.Run(() =>
                new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(i => (i.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(i => i is DirectoryInfo ? 0 : 1)
                    .ThenBy(i => i.Name)
                    .ToList());
            System.Diagnostics.Debug.WriteLine($"[Cards] T+{swTotal.ElapsedMilliseconds}ms : scan done ({items.Count} items, took {sw.ElapsedMilliseconds}ms)");

            bool isListMode = _currentViewMode == GalleryViewMode.List;

            // 把 foreach 拆 chunks，批與批之間 await Task.Delay(1) 讓出 UI thread 給 Spin 動畫 paint。
            // 整體時間幾乎相同 (Add HWND 時間不變) 但使用者看得到 Spin 在轉。
            // 順便：使用者也能看到 cards 一批批冒出來，比一次全出來感覺更有 progress。
            sw.Restart();
            int coverLoaded = 0, coverNull = 0, coverDone = 0;
            var swCover = System.Diagnostics.Stopwatch.StartNew();
            int totalCards = items.Count;
            const int ChunkSize = 8;
            long ctorMs = 0, setDataMs = 0, applyViewModeMs = 0, eventsMs = 0, addMs = 0;
            for (int i = 0; i < items.Count; i += ChunkSize)
            {
                var chunk = items.Skip(i).Take(ChunkSize).ToList();
                this.Invoke(() =>
                {
                    container.SuspendLayout();
                    foreach (var item in chunk)
                    {
                        bool isFolderNode = item is DirectoryInfo;

                        var swStep = System.Diagnostics.Stopwatch.StartNew();
                        var card = new ComicCardControl();
                        ctorMs += swStep.ElapsedTicks;

                        swStep.Restart();
                        card.SetData(item, isFolderNode);
                        setDataMs += swStep.ElapsedTicks;

                        swStep.Restart();
                        card.ApplyViewMode(_currentViewMode);
                        applyViewModeMs += swStep.ElapsedTicks;

                        swStep.Restart();
                        card.RequestOpenFolder += p => OpenFolderTab(p, false);
                        card.RequestOpenViewer += p => OpenWithExternalViewer(p);
                        card.RequestNewTab += (p, forceNew) => OpenFolderTab(p, forceNew);
                        card.RequestExplorer += p => OpenInExplorer(p);
                        eventsMs += swStep.ElapsedTicks;

                        swStep.Restart();
                        container.Controls.Add(card);
                        addMs += swStep.ElapsedTicks;

                        int thumbWidth = isListMode ? 80 : card.Width;
                        _ = Task.Run(async () =>
                        {
                            var cover = await _coverManager.GetCoverAsync(item.FullName, thumbWidth);
                            if (cover != null)
                            {
                                System.Threading.Interlocked.Increment(ref coverLoaded);
                                this.Invoke(() => card.SetCover(cover));
                            }
                            else
                            {
                                System.Threading.Interlocked.Increment(ref coverNull);
                            }
                            if (System.Threading.Interlocked.Increment(ref coverDone) == totalCards)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[Cards] T+{swTotal.ElapsedMilliseconds}ms : covers done — " +
                                    $"{coverLoaded} loaded / {coverNull} no-cover / {totalCards} total " +
                                    $"(took {swCover.ElapsedMilliseconds}ms after Spin callback start)");
                            }
                        });
                    }
                    container.ResumeLayout(true);   // 該 chunk 的 cards 立刻 layout + paint
                });
                await Task.Delay(1);   // 讓 UI thread 喘口氣 paint Spin animation frame
            }
            double tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            System.Diagnostics.Debug.WriteLine(
                $"[Cards] per-card breakdown: ctor={ctorMs * tickToMs:F1}ms, " +
                $"SetData={setDataMs * tickToMs:F1}ms, ApplyViewMode={applyViewModeMs * tickToMs:F1}ms, " +
                $"events={eventsMs * tickToMs:F1}ms, Add={addMs * tickToMs:F1}ms");
            System.Diagnostics.Debug.WriteLine($"[Cards] T+{swTotal.ElapsedMilliseconds}ms : all cards added (chunked) <- USER SHOULD SEE CARDS NOW");
            System.Diagnostics.Debug.WriteLine($"[Cards] T+{swTotal.ElapsedMilliseconds}ms : Spin callback returning (Spin will close)");
        });

        return Task.CompletedTask;
    }

    private static readonly HashSet<string> _imageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

    /// <summary>
    /// 「在檔案總管中開啟」共用邏輯：
    /// 目錄 → explorer 直接開該目錄、看裡面內容
    /// 檔案 → explorer /select 開父目錄 + highlight 該檔
    /// </summary>
    private static void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
        catch { /* 忽略無法開啟的狀況 */ }
    }

    private void OpenWithExternalViewer(string filePath)
    {
        try
        {
            // Honeyview 對「資料夾路徑」只會把它當單一 item 開（看到第一張就停）。
            // 改傳該資料夾內第一張圖 → viewer 載入後使用者用 PgDn/滾輪切換同目錄其它圖。
            string targetPath = filePath;
            if (Directory.Exists(filePath))
            {
                var firstImage = Directory.EnumerateFiles(filePath)
                    .Where(f => _imageExts.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (firstImage != null) targetPath = firstImage;
            }

            if (!string.IsNullOrEmpty(_config.ImageViewerPath) && File.Exists(_config.ImageViewerPath))
            {
                string template = string.IsNullOrWhiteSpace(_config.ImageViewerArgs) ? "\"{path}\"" : _config.ImageViewerArgs;
                string args = template.Replace("{path}", targetPath);
                System.Diagnostics.Process.Start(_config.ImageViewerPath, args);
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetPath) { UseShellExecute = true });
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

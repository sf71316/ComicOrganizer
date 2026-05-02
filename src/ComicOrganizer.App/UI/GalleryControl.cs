using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

public partial class GalleryControl : UserControl, IThemedControl
{
    public enum GalleryViewMode { Small, Medium, Large, List }

    private AppConfig _config = null!;
    private CoverManager _coverManager = null!;
    private IAssetMetaStore? _metaStore;   // 可選 — null 時 card 走原本 filename 顯示

    private const string LazyPlaceholderTag = "__LAZY__";

    // 區分 tree 右鍵點在節點 vs 空白：NodeMouseClick 時設 true，MouseUp 看到 true 就跳過 blank menu
    private bool _treeRightOnNode;

    // 卡片封面統一以 Large card 寬度載入（= ComicCardControl Large mode 的 cardSize.Width）。
    // 切換 S/M/L 時 pic.ImageFit=Contain 自動等比縮小顯示，不需重新載圖。
    // 代價：每張 bitmap 多 ~250KB，100 張多 ~25MB，可接受。
    private const int MaxCardCoverWidth = 330;

    // Gallery 顯示的檔案副檔名 (目錄一律顯示)：圖片 + 壓縮檔 + pdf
    private static readonly HashSet<string> GalleryAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
        ".zip", ".cbz",
        ".pdf",
    };

    private static bool ShouldShowInGallery(FileSystemInfo fi)
    {
        if ((fi.Attributes & FileAttributes.Hidden) != 0) return false;
        if (fi is DirectoryInfo) return true;
        return GalleryAllowedExtensions.Contains(fi.Extension);
    }

    /// <summary>
    /// 每個 tab 自己的 view mode（per-tab 獨立）。
    /// 新 tab 預設 Medium；tab close 時 entry 會清掉避免 dispose 後 reference 殘留。
    /// </summary>
    private readonly Dictionary<AntdUI.TabPage, GalleryViewMode> _tabViewModes = new();

    /// <summary>
    /// 共用 TooltipComponent — attach 到動態建的 button (例：每個 tab 的 btnRefresh)，
    /// 跟主視窗生命週期一致；ArrowAlign 跟 Segmented TooltipConfig 一致 (Bottom)
    /// </summary>
    private readonly AntdUI.TooltipComponent _btnTooltip = new() { ArrowAlign = AntdUI.TAlign.Bottom };

    // 純相框 SVG（移除 <text> — AntdUI SVG renderer 不渲染 text 元素，字母改用 SegmentedItem.Text 顯示）

    private GalleryViewMode GetTabMode(AntdUI.TabPage? page)
    {
        if (page == null) return GalleryViewMode.Medium;
        return _tabViewModes.TryGetValue(page, out var mode) ? mode : GalleryViewMode.Medium;
    }

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

        // Tree 點擊處理：
        //  右鍵 → 開啟 context menu（另開頁籤 / 重新整理）
        //  中鍵 → 直接開新頁籤（取代原本的雙擊行為）
        //  右鍵節點 → 三項選單 (檔案總管 / 目前頁籤 / 另開頁籤)
        //  右鍵空白 → 一項選單 (重新整理)；用 _treeRightOnNode 旗標區分
        treeFolders.NodeMouseClick += (s, e) =>
        {
            if (e.Item.Tag is not string path) return;

            if (e.Button == MouseButtons.Middle)
            {
                OpenFolderTab(path, true);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _treeRightOnNode = true;
                ShowTreeNodeContextMenu(path);
            }
        };

        // 空白處右鍵 → 重新整理 (整棵 tree 重新載入根目錄)
        treeFolders.MouseUp += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            if (_treeRightOnNode)
            {
                _treeRightOnNode = false;
                return;
            }
            ShowTreeBlankContextMenu();
        };

        // 處理頁籤關閉事件 — 清掉 _tabViewModes 內對應的 entry 避免 dispose 後 reference 殘留
        tabsContent.ClosingPage += (s, e) =>
        {
            if (e.Value != null) _tabViewModes.Remove(e.Value);
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

        // 更新所有已開 Tab 的 Segmented Tooltip + btnRefresh Tooltip（語言切換時同步）
        string[] tipKeys = { "ViewSmall", "ViewMedium", "ViewLarge", "ViewList" };
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            var seg = page.Controls.Find("tabViewModeSeg", true).OfType<AntdUI.Segmented>().FirstOrDefault();
            if (seg != null)
            {
                for (int i = 0; i < seg.Items.Count && i < tipKeys.Length; i++)
                {
                    seg.Items[i].Tooltip = LocalizationManager.GetString(tipKeys[i]);
                }
            }
            foreach (var btn in page.Controls.Find("tabBtnRefresh", true).OfType<AntdUI.Button>())
            {
                _btnTooltip.SetTip(btn, LocalizationManager.GetString("MenuRefresh"));
            }
        }
    }

    public void ApplyThemeColors()
    {
        splitter2.Panel1.BackColor = Core.ThemeColors.Border;
        splitter2.Panel2.BackColor = Core.ThemeColors.Border;
        treeFolders.BackColor = Core.ThemeColors.TreeViewSurface;
        // tabsContent 自己的 BackColor 預設繼承父 (Panel2=Border)，沒開 tab 時整片露 Border 色 → 強制設 Surface
        tabsContent.BackColor = Core.ThemeColors.Surface;
        tabsContent.ScrollBack = Core.ThemeColors.Text;
        tabsContent.FillActive = Core.ThemeColors.Border;

        // 動態建立的 Tab 內 panel 也要重套
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            page.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabContentHost", true).OfType<System.Windows.Forms.Panel>())
                p.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabHeader", true).OfType<AntdUI.Panel>())
                p.Back = Core.ThemeColors.TabHeader;
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

    public void Initialize(AppConfig config, CoverManager coverManager, IAssetMetaStore? metaStore = null)
    {
        _config = config;
        _coverManager = coverManager;
        _metaStore = metaStore;
        _ = LoadTreeAsync();
    }

    /// <summary>
    /// 切換指定 tab 的檢視模式（per-tab 獨立）。
    /// 卡片模式之間切換只 iterate 現有卡片改尺寸；涉及 List 才需要 reload。
    /// </summary>
    public void SetTabViewMode(AntdUI.TabPage page, GalleryViewMode mode)
    {
        if (page == null) return;
        var oldMode = GetTabMode(page);
        if (oldMode == mode) return;
        _tabViewModes[page] = mode;
        SyncTabVisibility(page);

        bool oldIsCards = oldMode != GalleryViewMode.List;
        bool newIsCards = mode != GalleryViewMode.List;
        if (oldIsCards && newIsCards)
        {
            ResizeCardsOnTab(page, mode);
        }
        else
        {
            // 切到/離 List 必須重 load（FlowLayoutPanel ↔ Table 結構不同）
            if (page.Tag is string path) LoadCurrentViewMode(page, path);
        }
    }

    private void ResizeCardsOnTab(AntdUI.TabPage page, GalleryViewMode mode)
    {
        var flow = FindTabFlowLayout(page);
        if (flow == null) return;
        flow.SuspendLayout();
        foreach (var c in flow.Controls.OfType<ComicCardControl>())
        {
            c.ApplyViewMode(mode);
        }
        flow.ResumeLayout(true);
    }

    private void SyncTabVisibility(AntdUI.TabPage? page)
    {
        if (page == null) return;
        var flow = FindTabFlowLayout(page);
        var table = FindTabTable(page);
        bool isList = GetTabMode(page) == GalleryViewMode.List;
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

    /// <summary>
    /// Tree 節點右鍵 → 三項選單（檔案總管 / 目前頁籤 / 另開頁籤），各帶 SVG icon。
    /// </summary>
    private void ShowTreeNodeContextMenu(string path)
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInExplorer") ?? "檔案總管開啟")
                { IconSvg = "FolderOpenOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInCurrentTab") ?? "目前頁籤開啟")
                { IconSvg = "FileOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInNewTab") ?? "另開新頁籤")
                { IconSvg = "PlusOutlined" },
        };

        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            string? t = mi.Text;
            if (t == null) return;
            if (t.Contains("檔案總管") || t.Contains("Explorer")) OpenInExplorer(path);
            else if (t.Contains("目前頁籤") || t.Contains("Current")) OpenFolderTab(path, false);
            else if (t.Contains("另開") || t.Contains("New Tab")) OpenFolderTab(path, true);
        }, items);
    }

    /// <summary>
    /// Tree 空白處右鍵 → 一項選單（重新整理整棵 tree）。
    /// </summary>
    private void ShowTreeBlankContextMenu()
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuRefresh") ?? "重新整理")
                { IconSvg = "RedoOutlined" },
        };
        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            _ = LoadTreeAsync();
        }, items);
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

    // Tab 內子控件 lookup 共用方法。Tab 各自含 breadcrumb / flowLayout / tableList，
    // 用 Name + 型別找到對應控件。null page 視為找不到。
    private static T? FindInTab<T>(AntdUI.TabPage? page, string name) where T : Control
    {
        if (page == null) return null;
        return page.Controls.Find(name, true).OfType<T>().FirstOrDefault();
    }

    private AntdUI.Breadcrumb? FindTabBreadcrumb(AntdUI.TabPage? page) => FindInTab<AntdUI.Breadcrumb>(page, "tabBreadcrumb");
    private FlowLayoutPanel? FindTabFlowLayout(AntdUI.TabPage? page) => FindInTab<FlowLayoutPanel>(page, "tabFlowLayout");
    private AntdUI.Table? FindTabTable(AntdUI.TabPage? page) => FindInTab<AntdUI.Table>(page, "tabTableList");

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

        // 新 Tab 結構：headerPanel (Dock=Top, 麵包屑 + refresh + view mode) + FlowLayoutPanel (Dock=Fill, 卡片區)
        var newPage = new AntdUI.TabPage
        {
            Text = folderName,
            Tag = path,
            BackColor = Core.ThemeColors.Surface
        };

        // 新 tab 預設 Medium。先存到 dictionary，後續控件用 GetTabMode(newPage) 取
        _tabViewModes[newPage] = GalleryViewMode.Medium;

        var headerPanel = new AntdUI.Panel
        {
            Name = "tabHeader",
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(33),  // DPI 150% 等高 → 50px，避免麵包屑文字裁切
            BackColor = Color.Transparent,
            Back = Core.ThemeColors.TabHeader,  // 跟主題切換
            Radius = 0,
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
            IconRatio = 1F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(40, 32)),
        };
        btnRefresh.Click += (s, e) => RefreshCurrentTab();
        _btnTooltip.SetTip(btnRefresh, LocalizationManager.GetString("MenuRefresh"));

        // 4-segment view mode 切換器（Small / Medium / Large / List）
        var segViewMode = new AntdUI.Segmented
        {
            Name = "tabViewModeSeg",
            Dock = DockStyle.Right,
            Size = LogicalToDeviceUnits(new Size(160, 28)),
            IconRatio = 1F,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),  // S/M/L 字粗體
            // ArrowAlign=Top → 箭頭在 tooltip 頂端朝上 → tooltip 顯示在 segmented 下方
            TooltipConfig = new AntdUI.TooltipConfig { ArrowAlign = AntdUI.TAlign.Bottom },
        };
        // S/M/L 共用相框 SVG，字母用 SegmentedItem.Text 屬性顯示（AntdUI 自己 layout icon + text）
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = "S", Tooltip = LocalizationManager.GetString("ViewSmall") });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = "M", Tooltip = LocalizationManager.GetString("ViewMedium") });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = "L", Tooltip = LocalizationManager.GetString("ViewLarge") });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { IconSvg = "UnorderedListOutlined", Tooltip = LocalizationManager.GetString("ViewList") });
        segViewMode.SelectIndex = (int)GalleryViewMode.Medium;
        segViewMode.SelectIndexChanged += (s, e) =>
        {
            SetTabViewMode(newPage, (GalleryViewMode)segViewMode.SelectIndex);
        };

        // Dock=Right 多控件時最後加的在最右邊：先加 segViewMode 再加 btnRefresh → segViewMode 在 btnRefresh 左
        headerPanel.Controls.Add(bc);
        headerPanel.Controls.Add(segViewMode);
        headerPanel.Controls.Add(btnRefresh);

        var contentHost = new System.Windows.Forms.Panel
        {
            Name = "tabContentHost",
            Dock = DockStyle.Fill,
            BackColor = Core.ThemeColors.Surface,
        };

        bool isList = false;   // 新 tab 預設 Medium 不是 List

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
        var mode = GetTabMode(page);
        if (mode == GalleryViewMode.List)
        {
            var table = FindTabTable(page);
            if (table != null) _ = LoadGalleryListAsync(table, path);
        }
        else
        {
            var flow = FindTabFlowLayout(page);
            if (flow != null) _ = LoadGalleryAsync(flow, path, mode);
        }
    }

    /// <summary>
    /// 限制同時 decode + resize 的 task 數，避免 ThreadPool 把全部核心都塞滿、
    /// 多 task 互搶 GDI+ 與 memory bandwidth 把每張 wall-clock 從 30ms 膨脹到 200ms+。
    /// 設成 ProcessorCount/2 讓 UI thread 與其他 IO 還有餘裕；最少保留 2 個。
    /// </summary>
    private static readonly SemaphoreSlim _coverSemaphore =
        new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>
    /// 共用：背景抓 cover、抓到後在 UI thread 呼叫 onLoaded。
    /// 對 dispose 競態做防護。回傳 Task 給呼叫端等待完成（或 fire-and-forget 不接）。
    /// 透過 _coverSemaphore 限制平行度。
    /// </summary>
    private Task LoadCoverInBackgroundAsync(string path, int width, Action<Image> onLoaded)
    {
        return Task.Run(async () =>
        {
            await _coverSemaphore.WaitAsync();
            try
            {
                var img = await _coverManager.GetCoverAsync(path, width);
                if (img == null || this.IsDisposed) return;
                try { this.Invoke(() => onLoaded(img)); }
                catch { /* form 已 dispose 等競態忽略 */ }
            }
            finally
            {
                _coverSemaphore.Release();
            }
        });
    }

    private Task LoadGalleryListAsync(AntdUI.Table table, string path)
    {
        AntdUI.Spin.open(this, async config =>
        {
            // ===== Phase 1: 檔案掃描 + 列表 binding (Spin 遮蔽期間) =====
            var fsInfos = await Task.Run(() =>
                new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            var prepared = await Task.Run(() => fsInfos.Select(fs =>
            {
                bool isDir = fs is DirectoryInfo;
                long size = isDir ? 0 : ((FileInfo)fs).Length;
                return (fs, isDir, size);
            }).ToList());

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

                // 縮圖任務 fire-and-forget — Thumbnail 是純資料賦值，不需 Invoke；
                // 全部完成後再 reset DataSource 一次觸發整表重繪
                var capturedItem = item;
                thumbTasks.Add(Task.Run(async () =>
                {
                    var img = await _coverManager.GetCoverAsync(capturedItem.FullPath, 120);
                    // CellImage(img, int) 第二個參數是 Radius 不是 Size！要靠 Size 屬性設邊長
                    // 60×80 (3:4) 貼合漫畫封面比例
                    if (img != null) capturedItem.Thumbnail = new AntdUI.CellImage(img)
                    {
                        Size = new Size(60, 80),
                        Radius = 4,
                        ImageFit = AntdUI.TFit.Cover,
                    };
                }));
            }

            // 第一次 set：文字列表立即可見（縮圖欄空白）
            this.Invoke(() => { table.DataSource = list; });

            // ===== Phase 2: 縮圖背景載入（Spin 已關閉、列表可見） =====
            if (thumbTasks.Count == 0) return;
            _ = Task.Run(async () =>
            {
                await Task.WhenAll(thumbTasks);
                if (this.IsDisposed) return;
                try
                {
                    this.Invoke(() =>
                    {
                        if (table.IsDisposed) return;
                        // AntdUI Table 對 ListChanged/PropertyChanged 不 hook，唯一可靠重繪辦法是 reset DataSource
                        table.DataSource = null;
                        table.DataSource = list;
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

    private Task LoadGalleryAsync(FlowLayoutPanel container, string path, GalleryViewMode mode)
    {
        container.Controls.Clear();

        AntdUI.Spin.open(container, async config =>
        {
            var items = await Task.Run(() =>
                new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .OrderBy(i => i is DirectoryInfo ? 0 : 1)
                    .ThenBy(i => i.Name)
                    .ToList());

            // 把 foreach 拆 chunks，批與批之間 await Task.Delay(1) 讓出 UI thread 給 Spin 動畫 paint。
            // 整體時間幾乎相同 (Add HWND 時間不變) 但使用者看得到 Spin 在轉 + cards 分批冒出有 progress 感
            const int ChunkSize = 8;
            for (int i = 0; i < items.Count; i += ChunkSize)
            {
                var chunk = items.Skip(i).Take(ChunkSize).ToList();
                this.Invoke(() =>
                {
                    container.SuspendLayout();
                    foreach (var item in chunk)
                    {
                        bool isFolderNode = item is DirectoryInfo;
                        var card = new ComicCardControl();
                        card.SetData(item, isFolderNode);
                        // #47 metadata 副標：DB 有 title / author 就覆寫 card 顯示
                        if (_metaStore != null)
                        {
                            try
                            {
                                var view = _metaStore.GetMetadataView(item.FullName);
                                if (view != null) card.SetMetadata(view);
                            }
                            catch { /* DB 失敗不阻塞 card 顯示 */ }
                        }
                        card.ApplyViewMode(mode);
                        card.RequestOpenFolder += p => OpenFolderTab(p, false);
                        card.RequestOpenViewer += p => OpenWithExternalViewer(p);
                        card.RequestNewTab += (p, forceNew) => OpenFolderTab(p, forceNew);
                        card.RequestExplorer += p => OpenInExplorer(p);
                        container.Controls.Add(card);

                        // 目錄與檔案都試著載封面：CoverManager 對目錄會遞迴往下找首張圖檔（受 MaxScanDepth 限制）
                        // 統一以 Large 寬度載入，切換 view mode 時 pic.ImageFit=Contain 縮放顯示，不需重新載
                        var capturedCard = card;
                        _ = LoadCoverInBackgroundAsync(item.FullName, MaxCardCoverWidth, img => capturedCard.SetCover(img));
                    }
                    container.ResumeLayout(true);   // 該 chunk 的 cards 立刻 layout + paint
                });
                await Task.Delay(1);   // 讓 UI thread 喘口氣 paint Spin animation frame
            }
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
        // 改用滑鼠中鍵開新頁籤，這裡留空讓 AntdUI Tree 走預設行為（toggle expand）
    }
}

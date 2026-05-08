using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

/// <summary>
/// GalleryControl tab 管理 — open/create/find/refresh + breadcrumb + per-tab S/M/L/List 切換。
/// 從 GalleryControl.cs 抽出 — folder mode 跟虛擬庫共用 tab 結構，CreateNewTabPage 是核心 helper。
/// </summary>
public partial class GalleryControl
{
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

    /// <summary>開 tab 顯示一組 work paths（虛擬庫共用）。
    /// 沒有開啟中的 tab 時自動建一個（CreateNewTabPage helper），不再像之前靜默 return。</summary>
    private void OpenVirtualWorksTab(string title, IReadOnlyList<string> paths, bool forceNew = false)
    {
        var page = (!forceNew ? tabsContent.SelectedTab : null) ?? CreateNewTabPage(title);
        var flow = FindTabFlowLayout(page);
        if (flow == null) return;
        // 更新 tab 標題、清掉 folder Tag（避免後續 OpenFolderTab 把同 path 認成已開）
        page.Text = title;
        page.Tag = null;
        var bc = FindTabBreadcrumb(page);
        if (bc != null)
        {
            bc.Items.Clear();
            bc.Items.Add(new AntdUI.BreadcrumbItem { Text = " " + title + " ", IconSvg = "TagOutlined" });
        }
        _ = LoadGalleryFromPathsAsync(flow, paths, GetTabMode(page));
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

        var newPage = CreateNewTabPage(folderName);
        newPage.Tag = path;
        UpdateBreadcrumb(path);
        LoadCurrentViewMode(newPage, path);
    }

    /// <summary>建一個空 TabPage（含 header / breadcrumb / refresh / S/M/L/List segView / contentHost / flow / table 完整結構），
    /// 加進 tabsContent 並選中。caller 自行填 Tag / breadcrumb / 內容。
    /// virtual lib（無實體 path 的 tab）跟 folder mode 共用此 helper，避免「沒開過 tab → click 沒反應」。</summary>
    private AntdUI.TabPage CreateNewTabPage(string title)
    {
        var newPage = new AntdUI.TabPage
        {
            Text = title,
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

        // List view 表格的 mouse / click 行為（hover preview / OpenFolder button / 右鍵選單）
        WireTabTableHandlers(tableList);

        contentHost.Controls.Add(flowLayout);
        contentHost.Controls.Add(tableList);

        newPage.Controls.Add(contentHost);
        newPage.Controls.Add(headerPanel);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;
        return newPage;
    }

    /// <summary>List view 表格的 mouse handler 連線（hover preview popup / OpenFolder cell button / 右鍵選單）。
    /// 從 CreateNewTabPage 抽出 — 純設定 event，不持有狀態（currentPreviewPath 用 closure local var）。</summary>
    private void WireTabTableHandlers(AntdUI.Table tableList)
    {
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
                if (mi.Text == "📖 閱讀") OpenForReading(item.FullPath);
                else if (mi.Text == "📂 檔案總管中開啟")
                    OpenInExplorer(item.FullPath);
                else if (mi.Text == "🔗 另開頁籤") OpenFolderTab(item.FullPath, true);
            }, items.ToArray());
        };
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
}

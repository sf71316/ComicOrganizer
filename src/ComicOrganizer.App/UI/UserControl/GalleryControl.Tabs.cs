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
    private void OpenVirtualWorksTab(
        string title,
        IReadOnlyList<string> paths,
        bool forceNew = false,
        string iconSvg = "TagOutlined",
        Func<IReadOnlyList<string>>? refreshResolver = null,
        long? virtualLibraryId = null,
        string? searchDsl = null)
    {
        var page = (!forceNew && tabsContent.SelectedTab != null && !WorkContextMenuHelper.IsDashboardTag(tabsContent.SelectedTab.Tag as string)
                    ? tabsContent.SelectedTab
                    : null)
                   ?? CreateNewTabPage(title);
        var entry = TabNavigationEntry.ForVirtualWorks(title, paths, iconSvg, refreshResolver, virtualLibraryId, searchDsl);
        ApplyNavigationEntryToTab(page, entry, recordHistory: true);
    }

    private void ApplyNavigationEntryToTab(AntdUI.TabPage page, TabNavigationEntry entry, bool recordHistory)
    {
        if (page == null || page.IsDisposed || WorkContextMenuHelper.IsDashboardTag(page.Tag as string)) return;

        if (recordHistory)
        {
            GetOrCreateTabNavHistory(page).Push(entry);
        }

        if (entry.Kind == TabNavigationEntryKind.Folder)
        {
            string folderPath = entry.FolderPath ?? "";
            string title = string.IsNullOrWhiteSpace(entry.Title)
                ? (Path.GetFileName(folderPath) is { Length: > 0 } name ? name : folderPath)
                : entry.Title;

            _tabVirtualPaths.Remove(page);
            _tabVirtualRefreshers.Remove(page);
            _tabVirtualLibraryIds.Remove(page);
            _tabSearchDsls.Remove(page);
            page.Text = title;
            page.Tag = folderPath;
            UpdateBreadcrumbForPage(page, folderPath);
        }
        else
        {
            _tabVirtualPaths[page] = entry.VirtualPaths.ToList();
            if (entry.RefreshResolver != null) _tabVirtualRefreshers[page] = entry.RefreshResolver;
            else _tabVirtualRefreshers.Remove(page);
            if (entry.VirtualLibraryId.HasValue) _tabVirtualLibraryIds[page] = entry.VirtualLibraryId.Value;
            else _tabVirtualLibraryIds.Remove(page);
            if (!string.IsNullOrWhiteSpace(entry.SearchDsl)) _tabSearchDsls[page] = entry.SearchDsl;
            else _tabSearchDsls.Remove(page);

            page.Text = entry.Title;
            page.Tag = null;

            var bc = FindTabBreadcrumb(page);
            if (bc != null)
            {
                bc.Items.Clear();
                bc.Items.Add(new AntdUI.BreadcrumbItem { Text = " " + entry.Title + " ", IconSvg = entry.IconSvg });
            }
        }

        var btnSaveVLib = FindInTab<AntdUI.Button>(page, "tabBtnSaveVLib");
        if (btnSaveVLib != null && !btnSaveVLib.IsDisposed)
        {
            btnSaveVLib.Visible = entry.Kind == TabNavigationEntryKind.VirtualWorks && !string.IsNullOrWhiteSpace(entry.SearchDsl);
        }

        SyncTabVisibility(page);
        UpdateTabNavButtons(page);
        LoadCurrentViewMode(page);
    }

    private void NavigateTabHistory(AntdUI.TabPage? page, int delta)
    {
        if (page == null || page.IsDisposed || WorkContextMenuHelper.IsDashboardTag(page.Tag as string)) return;
        var history = GetOrCreateTabNavHistory(page);
        TabNavigationEntry? target = null;
        bool moved = delta < 0 ? history.TryGoBack(out target) : (delta > 0 && history.TryGoForward(out target));
        if (!moved || target == null)
        {
            UpdateTabNavButtons(page);
            return;
        }
        ApplyNavigationEntryToTab(page, target, recordHistory: false);
    }

    private void NavigateBackCurrentTab() => NavigateTabHistory(tabsContent.SelectedTab, -1);

    private void NavigateForwardCurrentTab() => NavigateTabHistory(tabsContent.SelectedTab, +1);

    private void UpdateTabNavButtons(AntdUI.TabPage? page)
    {
        if (page == null || page.IsDisposed) return;
        _tabNavHistories.TryGetValue(page, out var history);
        var btnBack = FindInTab<AntdUI.Button>(page, "tabBtnBack");
        if (btnBack != null && !btnBack.IsDisposed) btnBack.Enabled = history?.CanGoBack ?? false;
        var btnForward = FindInTab<AntdUI.Button>(page, "tabBtnForward");
        if (btnForward != null && !btnForward.IsDisposed) btnForward.Enabled = history?.CanGoForward ?? false;
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

    private void UpdateBreadcrumb(string path) => UpdateBreadcrumbForPage(tabsContent.SelectedTab, path);

    private void UpdateBreadcrumbForPage(AntdUI.TabPage? page, string path)
    {
        var bc = FindTabBreadcrumb(page);
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
        var page = tabsContent.SelectedTab;
        if (page == null) return;
        RefreshTab(page);
    }

    private void CloseCurrentTab()
    {
        var page = tabsContent.SelectedTab;
        if (page == null || page.IsDisposed || WorkContextMenuHelper.IsDashboardTag(page.Tag as string)) return;
        CloseTabPages(new[] { page });
    }

    private void SwitchAdjacentTab(bool reverse)
    {
        int count = tabsContent.Pages.Count;
        int next = WindowLayoutHelper.GetNextTabIndex(tabsContent.SelectedIndex, count, reverse);
        if (next >= 0 && next < count)
        {
            tabsContent.SelectedIndex = next;
        }
    }

    private bool HandleQuickFilterEscape(AntdUI.TabPage? page)
    {
        if (page == null || page.IsDisposed || WorkContextMenuHelper.IsDashboardTag(page.Tag as string))
            return false;

        var txtFilter = FindInTab<AntdUI.Input>(page, "tabTxtQuickFilter");
        if (txtFilter == null || txtFilter.IsDisposed || (!txtFilter.Focused && !txtFilter.ContainsFocus))
            return false;

        var sortState = GetOrCreateTabSortFilterState(page);
        if (!string.IsNullOrEmpty(sortState.FilterKeyword) || sortState.ReadFilter != GalleryReadFilter.All)
        {
            _syncingSortUi = true;
            try
            {
                sortState.FilterKeyword = "";
                sortState.ReadFilter = GalleryReadFilter.All;
                txtFilter.Text = "";
                var seg = FindInTab<AntdUI.Segmented>(page, "tabFilterStatusSeg");
                if (seg != null && !seg.IsDisposed) seg.SelectIndex = 0;
            }
            finally
            {
                _syncingSortUi = false;
            }
            ApplySortAndFilterToTab(page);
        }

        Control? galleryTarget = GetTabMode(page) == GalleryViewMode.List
            ? FindTabTable(page)
            : FindTabFlowLayout(page);
        galleryTarget?.Focus();
        return true;
    }

    private void RefreshTab(AntdUI.TabPage? page)
    {
        if (page == null || page.IsDisposed) return;
        if (WorkContextMenuHelper.IsDashboardTag(page.Tag as string))
        {
            _ = LoadDashboardDataAsync(page);
            return;
        }
        if (_tabVirtualRefreshers.TryGetValue(page, out var resolver))
        {
            try
            {
                _tabVirtualPaths[page] = resolver().ToList();
            }
            catch
            {
                // 若虛擬查詢暫時失敗則沿用既有路徑清單重載
            }
        }
        LoadCurrentViewMode(page);
    }

    private void CleanupTabState(AntdUI.TabPage page)
    {
        _tabViewModes.Remove(page);
        _tabVirtualPaths.Remove(page);
        _tabVirtualRefreshers.Remove(page);
        _tabVirtualLibraryIds.Remove(page);
        _tabSearchDsls.Remove(page);
        _tabSortFilterStates.Remove(page);
        _tabFullListItems.Remove(page);
        _tabNavHistories.Remove(page);
        var flow = FindTabFlowLayout(page);
        if (flow != null) _loadGenerations.TryRemove(flow, out _);
        var table = FindTabTable(page);
        if (table != null) _loadGenerations.TryRemove(table, out _);
    }

    private void CloseTabPages(IEnumerable<AntdUI.TabPage> targets)
    {
        var toClose = targets
            .Where(p => p != null && !p.IsDisposed && !WorkContextMenuHelper.IsDashboardTag(p.Tag as string))
            .Distinct()
            .ToList();
        if (toClose.Count == 0) return;

        bool closeDispose = tabsContent.CloseDisposePage;
        foreach (var page in toClose)
        {
            // 須在 Pages.Remove 之前清掉 state（Pages.Remove 在 CloseDisposePage=true 時會直接釋放子控件，且不會觸發 ClosingPage）
            CleanupTabState(page);
            tabsContent.Pages.Remove(page);
            if (!closeDispose && !page.IsDisposed)
            {
                page.Dispose();
            }
        }
    }

    private void ShowTabHeaderContextMenu(AntdUI.TabPage targetPage)
    {
        var pages = tabsContent.Pages.Cast<AntdUI.TabPage>().ToList();
        var menuItems = WorkContextMenuHelper.BuildTabHeaderMenuItems(pages, targetPage);

        AntdUI.ContextMenuStrip.open(tabsContent, mi =>
        {
            switch (mi.ID)
            {
                case WorkContextMenuHelper.IdTabRefresh:
                    RefreshTab(targetPage);
                    break;
                case WorkContextMenuHelper.IdTabCopyPath:
                    if (targetPage.Tag is string copyPath && !WorkContextMenuHelper.IsDashboardTag(copyPath))
                    {
                        WorkContextMenuHelper.TryCopyText(copyPath);
                    }
                    break;
                case WorkContextMenuHelper.IdTabOpenExplorer:
                    if (targetPage.Tag is string folderPath && !WorkContextMenuHelper.IsDashboardTag(folderPath))
                    {
                        OpenInExplorer(folderPath);
                    }
                    break;
                case WorkContextMenuHelper.IdTabCloseSelf:
                    CloseTabPages(WorkContextMenuHelper.GetTabCloseTargets(pages, targetPage, TabCloseScope.Self));
                    break;
                case WorkContextMenuHelper.IdTabCloseOthers:
                    CloseTabPages(WorkContextMenuHelper.GetTabCloseTargets(pages, targetPage, TabCloseScope.Others));
                    break;
                case WorkContextMenuHelper.IdTabCloseRight:
                    CloseTabPages(WorkContextMenuHelper.GetTabCloseTargets(pages, targetPage, TabCloseScope.Right));
                    break;
            }
        }, menuItems);
    }

    private void OpenFolderTab(string path, bool forceNew)
    {
        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        if (!forceNew && tabsContent.SelectedTab != null)
        {
            var currentTab = tabsContent.SelectedTab;
            if (string.Equals(currentTab.Tag as string, path, StringComparison.OrdinalIgnoreCase)) return;

            if (!WorkContextMenuHelper.IsDashboardTag(currentTab.Tag as string))
            {
                ApplyNavigationEntryToTab(
                    currentTab,
                    TabNavigationEntry.ForFolder(path, folderName),
                    recordHistory: true);
                return;
            }
        }

        var existingTab = tabsContent.Pages.Cast<AntdUI.TabPage>()
            .FirstOrDefault(p => string.Equals(p.Tag as string, path, StringComparison.OrdinalIgnoreCase));
        if (existingTab != null)
        {
            tabsContent.SelectedTab = existingTab;
            return;
        }

        var newPage = CreateNewTabPage(folderName);
        ApplyNavigationEntryToTab(
            newPage,
            TabNavigationEntry.ForFolder(path, folderName),
            recordHistory: true);
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

        var defaultMode = GetDefaultGalleryViewMode();
        // 新 tab 套用預設檢視模式。先存到 dictionary，後續控件用 GetTabMode(newPage) 取
        _tabViewModes[newPage] = defaultMode;
        var sortState = GetOrCreateTabSortFilterState(newPage);

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
        btnRefresh.Click += (s, e) => RefreshTab(newPage);
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
        segViewMode.SelectIndex = (int)defaultMode;
        segViewMode.SelectIndexChanged += (s, e) =>
        {
            SetTabViewMode(newPage, (GalleryViewMode)segViewMode.SelectIndex);
        };

        var btnSortDir = new AntdUI.Button
        {
            Name = "tabSortDir",
            Dock = DockStyle.Right,
            Ghost = true,
            IconSvg = sortState.SortDescending ? "SortDescendingOutlined" : "SortAscendingOutlined",
            IconRatio = 1F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(36, 32)),
        };
        _btnTooltip.SetTip(btnSortDir, sortState.SortDescending ? "降冪排序" : "升冪排序");

        var selSortField = new AntdUI.Select
        {
            Name = "tabSortField",
            Dock = DockStyle.Right,
            Size = LogicalToDeviceUnits(new Size(116, 28)),
            BackColor = Core.ThemeColors.Background,
            ForeColor = Core.ThemeColors.Text,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
        };
        selSortField.Items.AddRange(new object[] { "名稱", "修改時間", "檔案大小", "最近閱讀", "閱讀次數" });
        selSortField.SelectedIndex = (int)sortState.SortField;

        selSortField.SelectedIndexChanged += (s, e) =>
        {
            if (_syncingSortUi || selSortField.SelectedIndex < 0) return;
            sortState.SortField = (Models.GallerySortField)selSortField.SelectedIndex;
            SyncTableSortHeader(FindTabTable(newPage), sortState);
            ApplySortAndFilterToTab(newPage);
        };

        btnSortDir.Click += (s, e) =>
        {
            sortState.SortDescending = !sortState.SortDescending;
            UpdateSortDirectionButton(btnSortDir, sortState.SortDescending);
            SyncTableSortHeader(FindTabTable(newPage), sortState);
            ApplySortAndFilterToTab(newPage);
        };

        var btnBack = new AntdUI.Button
        {
            Name = "tabBtnBack",
            Dock = DockStyle.Left,
            Ghost = true,
            IconSvg = "LeftOutlined",
            IconRatio = 0.9F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(32, 32)),
            Enabled = false,
        };
        btnBack.Click += (s, e) => NavigateTabHistory(newPage, -1);
        _btnTooltip.SetTip(btnBack, "上一頁 (Alt+Left)");

        var btnForward = new AntdUI.Button
        {
            Name = "tabBtnForward",
            Dock = DockStyle.Left,
            Ghost = true,
            IconSvg = "RightOutlined",
            IconRatio = 0.9F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(32, 32)),
            Enabled = false,
        };
        btnForward.Click += (s, e) => NavigateTabHistory(newPage, +1);
        _btnTooltip.SetTip(btnForward, "下一頁 (Alt+Right)");

        var btnSaveVLib = new AntdUI.Button
        {
            Name = "tabBtnSaveVLib",
            Dock = DockStyle.Right,
            Ghost = true,
            IconSvg = "StarOutlined",
            IconRatio = 0.85F,
            Text = "存為清單",
            Font = new Font("Microsoft JhengHei UI", 9F),
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(96, 32)),
            Cursor = Cursors.Hand,
            Visible = false,
        };
        _btnTooltip.SetTip(btnSaveVLib, "將目前搜尋條件存為自訂閱讀清單");
        btnSaveVLib.Click += (s, e) => SaveSearchTabAsVirtualLibrary(newPage);

        // Dock=Right/Left 多控件時最後加的在最外側：
        // 左→右視覺：[btnBack] [btnForward] [bc] [btnSaveVLib] [selSortField] [btnSortDir] [segViewMode] [btnRefresh]
        headerPanel.Controls.Add(bc);
        headerPanel.Controls.Add(btnSaveVLib);
        headerPanel.Controls.Add(selSortField);
        headerPanel.Controls.Add(btnSortDir);
        headerPanel.Controls.Add(segViewMode);
        headerPanel.Controls.Add(btnRefresh);
        headerPanel.Controls.Add(btnForward);
        headerPanel.Controls.Add(btnBack);

        // 常駐就地過濾列（狀態 Segmented + 關鍵字輸入框 + 計數標籤 + 清除鈕）
        var quickFilterBar = new AntdUI.Panel
        {
            Name = "tabQuickFilterBar",
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(36),
            BackColor = Color.Transparent,
            Back = Core.ThemeColors.Surface,
            Padding = new Padding(8, 3, 8, 3),
            Radius = 0,
        };

        var segFilterStatus = new AntdUI.Segmented
        {
            Name = "tabFilterStatusSeg",
            Dock = DockStyle.Left,
            Size = LogicalToDeviceUnits(new Size(240, 28)),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
        };
        segFilterStatus.Items.Add(new AntdUI.SegmentedItem { Text = "全部" });
        segFilterStatus.Items.Add(new AntdUI.SegmentedItem { Text = "未讀" });
        segFilterStatus.Items.Add(new AntdUI.SegmentedItem { Text = "進行中" });
        segFilterStatus.Items.Add(new AntdUI.SegmentedItem { Text = "已完成" });
        segFilterStatus.SelectIndex = (int)sortState.ReadFilter;

        var lblFilterCount = new System.Windows.Forms.Label
        {
            Name = "tabLblFilterCount",
            Dock = DockStyle.Right,
            AutoSize = false,
            Width = LogicalToDeviceUnits(120),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Core.ThemeColors.Text,
            Font = new Font("Microsoft JhengHei UI", 9F),
            Text = "",
        };

        var txtQuickFilter = new AntdUI.Input
        {
            Name = "tabTxtQuickFilter",
            Dock = DockStyle.Right,
            Width = LogicalToDeviceUnits(220),
            PlaceholderText = LocalizationManager.GetString("QuickFilterPlaceholder") ?? "本頁過濾（檔名／標題）...",
            PrefixSvg = "SearchOutlined",
            AllowClear = true,
            BackColor = Core.ThemeColors.Background,
            ForeColor = Core.ThemeColors.Text,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
        };

        var btnClearFilter = new AntdUI.Button
        {
            Name = "tabBtnClearFilter",
            Dock = DockStyle.Right,
            Ghost = true,
            IconSvg = "CloseOutlined",
            IconRatio = 0.85F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(32, 28)),
        };
        _btnTooltip.SetTip(btnClearFilter, LocalizationManager.GetString("FilterClearTooltip") ?? "清除過濾條件");

        segFilterStatus.SelectIndexChanged += (s, e) =>
        {
            if (_syncingSortUi) return;
            sortState.ReadFilter = (Models.GalleryReadFilter)segFilterStatus.SelectIndex;
            ApplySortAndFilterToTab(newPage);
        };
        txtQuickFilter.TextChanged += (s, e) =>
        {
            if (_syncingSortUi) return;
            sortState.FilterKeyword = txtQuickFilter.Text ?? "";
            ApplySortAndFilterToTab(newPage);
        };
        btnClearFilter.Click += (s, e) =>
        {
            _syncingSortUi = true;
            try
            {
                sortState.ReadFilter = Models.GalleryReadFilter.All;
                sortState.FilterKeyword = "";
                segFilterStatus.SelectIndex = 0;
                txtQuickFilter.Text = "";
            }
            finally
            {
                _syncingSortUi = false;
            }
            ApplySortAndFilterToTab(newPage);
        };

        quickFilterBar.Controls.Add(segFilterStatus);
        quickFilterBar.Controls.Add(lblFilterCount);
        quickFilterBar.Controls.Add(txtQuickFilter);
        quickFilterBar.Controls.Add(btnClearFilter);

        var contentHost = new System.Windows.Forms.Panel
        {
            Name = "tabContentHost",
            Dock = DockStyle.Fill,
            BackColor = Core.ThemeColors.Surface,
        };

        bool isList = defaultMode == GalleryViewMode.List;

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

        // List view 表格的 mouse / click 行為（hover preview / OpenFolder button / 排序 / 右鍵選單）
        WireTabTableHandlers(newPage, tableList);
        SyncTableSortHeader(tableList, sortState);

        contentHost.Controls.Add(flowLayout);
        contentHost.Controls.Add(tableList);

        // Top-to-bottom 停靠反序：Fill 最先加，headerPanel 最後加在最上方
        newPage.Controls.Add(contentHost);
        newPage.Controls.Add(quickFilterBar);
        newPage.Controls.Add(headerPanel);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;
        return newPage;
    }

    private void SaveSearchTabAsVirtualLibrary(AntdUI.TabPage page)
    {
        if (_metaStore == null || page.IsDisposed) return;
        if (!_tabSearchDsls.TryGetValue(page, out var dsl) || string.IsNullOrWhiteSpace(dsl)) return;

        var form = this.FindForm();
        if (form == null || form.IsDisposed) return;

        using var dlg = new SaveVirtualLibraryDialog(_metaStore, initialDsl: dsl);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;

        _metaStore.CreateVirtualLibrary(dlg.ResultName, dlg.ResultDsl, dlg.ResultIconSvg);
        _ = LoadTreeAsync();
        AntdUI.Message.success(form, $"已儲存閱讀清單「{dlg.ResultName}」");
    }

    /// <summary>List view 表格的 mouse handler 連線（hover preview popup / OpenFolder cell button / 右鍵選單）。
    /// 從 CreateNewTabPage 抽出 — 純設定 event，不持有狀態（currentPreviewPath 用 closure local var）。</summary>
    private void WireTabTableHandlers(AntdUI.TabPage page, AntdUI.Table tableList)
    {
        // 表頭排序切換：攔截 AntdUI 預設字串排序（return true），改用統一的 GallerySortFilterHelper 數值/時間/自然排序
        tableList.SortModeChanged += (object s, AntdUI.TableSortModeEventArgs e) =>
        {
            Models.GallerySortField? mappedField = e.Column?.Key switch
            {
                "Name" => Models.GallerySortField.Name,
                "Size" => Models.GallerySortField.Size,
                "CreatedAt" => Models.GallerySortField.ModifiedTime,
                _ => null
            };
            if (mappedField == null) return false;

            var sortState = GetOrCreateTabSortFilterState(page);
            bool nextDesc = e.SortMode switch
            {
                AntdUI.SortMode.DESC => true,
                AntdUI.SortMode.ASC => false,
                _ => false, // NONE 循環回到升冪，不清空排序狀態
            };
            sortState.SortField = mappedField.Value;
            sortState.SortDescending = nextDesc;

            _syncingSortUi = true;
            try
            {
                var sel = page.Controls.Find("tabSortField", true).OfType<AntdUI.Select>().FirstOrDefault();
                if (sel != null) sel.SelectedIndex = (int)sortState.SortField;
                var btnDir = page.Controls.Find("tabSortDir", true).OfType<AntdUI.Button>().FirstOrDefault();
                if (btnDir != null) UpdateSortDirectionButton(btnDir, sortState.SortDescending);
                SyncTableSortHeader(tableList, sortState);
            }
            finally
            {
                _syncingSortUi = false;
            }

            ApplySortAndFilterToTab(page);
            return true;
        };
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

        // 點擊事件（中鍵另開分頁、右鍵選單）
        tableList.MouseClick += (s, e) =>
        {
            var cell = tableList.HitTest(e.X, e.Y, out _, out _);
            // 同 hover 邏輯：直接從 cell.Row.RECORD 取資料，避開 i_row 索引偏移
            var item = cell?.Row?.RECORD as GalleryListItem;
            if (item == null || string.IsNullOrEmpty(item.FullPath)) return;

            if (e.Button == MouseButtons.Middle)
            {
                EnqueueReadIfEnabled(item.FullPath);
                if (Directory.Exists(item.FullPath)) OpenFolderTab(item.FullPath, true);
                else OpenForReading(item.FullPath);
                return;
            }

            if (e.Button != MouseButtons.Right) return;

            var extInfo = GetExtendedSearchInfo(item.FullPath);
            var ctx = new WorkMenuContext
            {
                Path = item.FullPath,
                IsFolderNode = item.IsFolder,
                CurrentStatus = _metaStore?.GetReadStatus(item.FullPath),
                VirtualLibraries = _metaStore?.ListVirtualLibraries(),
                WorkLibraryIds = _metaStore?.ListVirtualLibraryIdsForWork(item.FullPath),
                AuthorName = extInfo.AuthorName,
                Series = extInfo.Series,
                TranslationGroup = extInfo.TranslationGroup,
            };
            WorkContextMenuHelper.OpenWorkContextMenu(tableList, ctx, CreateWorkMenuActions());
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
            new AntdUI.Column("CreatedAt", "修改時間", AntdUI.ColumnAlign.Center) { Width = "160", SortOrder = true },
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

    private void LoadCurrentViewMode(AntdUI.TabPage page)
    {
        var mode = GetTabMode(page);
        if (page.Tag is string path)
        {
            if (string.Equals(path, "DASHBOARD", StringComparison.Ordinal)) return;
            if (mode == GalleryViewMode.List)
            {
                var table = FindTabTable(page);
                if (table != null)
                {
                    InvalidateLoadGeneration(FindTabFlowLayout(page));
                    _ = LoadGalleryListAsync(table, path, page);
                }
            }
            else
            {
                var flow = FindTabFlowLayout(page);
                if (flow != null)
                {
                    InvalidateLoadGeneration(FindTabTable(page));
                    _ = LoadGalleryAsync(flow, path, () => GetTabMode(page), page);
                }
            }
            return;
        }

        if (_tabVirtualPaths.TryGetValue(page, out var vPaths))
        {
            if (mode == GalleryViewMode.List)
            {
                var table = FindTabTable(page);
                if (table != null)
                {
                    InvalidateLoadGeneration(FindTabFlowLayout(page));
                    _ = LoadGalleryListFromPathsAsync(table, vPaths, page);
                }
            }
            else
            {
                var flow = FindTabFlowLayout(page);
                if (flow != null)
                {
                    InvalidateLoadGeneration(FindTabTable(page));
                    _ = LoadGalleryFromPathsAsync(flow, vPaths, () => GetTabMode(page), page);
                }
            }
        }
    }
}

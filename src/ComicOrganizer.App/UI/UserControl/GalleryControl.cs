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
    private ReadRecordQueue? _readRecordQueue;   // 延遲提交閱讀紀錄（30s 門檻），#49 Phase 1.0
    private UserPreferences? _prefs;   // reader / 閱讀方向偏好，#49

    private const string LazyPlaceholderTag = "__LAZY__";

    public event Action<string>? StatusChanged;

    /// <summary>偏好設定在 GalleryControl 內部變更（如拖曳調整 Drawer 寬度）時請求宿主持久化。</summary>
    public event Action? RequestSavePreferences;

    private int _activeBackgroundJobs = 0;

    private void ReportJobStarted(string message = "正在載入項目與封面...")
    {
        if (Interlocked.Increment(ref _activeBackgroundJobs) == 1)
        {
            try { this.BeginInvoke(new Action(() => StatusChanged?.Invoke(message))); }
            catch { StatusChanged?.Invoke(message); }
        }
    }

    private void ReportJobFinished()
    {
        if (Interlocked.Decrement(ref _activeBackgroundJobs) <= 0)
        {
            Interlocked.Exchange(ref _activeBackgroundJobs, 0);
            try { this.BeginInvoke(new Action(() => StatusChanged?.Invoke("就緒"))); }
            catch { StatusChanged?.Invoke("就緒"); }
        }
    }

    // 區分 tree 右鍵點在節點 vs 空白：NodeMouseClick 時設 true，MouseUp 看到 true 就跳過 blank menu
    private bool _treeRightOnNode;

    // 卡片封面統一以 Large card 寬度載入（= ComicCardControl Large mode 的 cardSize.Width）。
    // 切換 S/M/L 時 pic.ImageFit=Contain 自動等比縮小顯示，不需重新載圖。
    // 代價：每張 bitmap 多 ~250KB，100 張多 ~25MB，可接受。
    private const int MaxCardCoverWidth = 330;

    private static bool ShouldShowInGallery(FileSystemInfo fi)
    {
        if ((fi.Attributes & FileAttributes.Hidden) != 0) return false;
        if (fi is DirectoryInfo) return true;
        return SupportedExtensions.Gallery.Contains(fi.Extension);
    }

    /// <summary>
    /// 每個 tab 自己的 view mode（per-tab 獨立）。
    /// 新 tab 預設 Medium；tab close 時 entry 會清掉避免 dispose 後 reference 殘留。
    /// </summary>
    private readonly Dictionary<AntdUI.TabPage, GalleryViewMode> _tabViewModes = new();

    /// <summary>
    /// 每個虛擬分頁（ByAuthor、ByTag、MyLib、ReadingTrack、Search）目前顯示的 work paths 清單。
    /// 切換 S/M/L/List 檢視模式與重新整理時依此重載；tab 關閉或切換回實體資料夾時清掉避免殘留。
    /// </summary>
    private readonly Dictionary<AntdUI.TabPage, IReadOnlyList<string>> _tabVirtualPaths = new();

    /// <summary>虛擬分頁重新整理時用來重新計算路徑清單的回呼（無則沿用 _tabVirtualPaths）。</summary>
    private readonly Dictionary<AntdUI.TabPage, Func<IReadOnlyList<string>>> _tabVirtualRefreshers = new();

    /// <summary>虛擬閱讀清單分頁對應的 VirtualLibrary.Id（增減作品後自動連動刷新）。</summary>
    private readonly Dictionary<AntdUI.TabPage, long> _tabVirtualLibraryIds = new();

    /// <summary>搜尋／作者等可儲存虛擬分頁對應的 DSL 條件（供分頁工具列「存為清單」按鈕使用）。</summary>
    private readonly Dictionary<AntdUI.TabPage, string> _tabSearchDsls = new();

    /// <summary>每個分頁獨立的排序與就地過濾狀態。</summary>
    private sealed class TabSortFilterState
    {
        public GallerySortField SortField { get; set; } = GallerySortField.Name;
        public bool SortDescending { get; set; }
        public GalleryReadFilter ReadFilter { get; set; } = GalleryReadFilter.All;
        public string FilterKeyword { get; set; } = "";
    }

    private readonly Dictionary<AntdUI.TabPage, TabSortFilterState> _tabSortFilterStates = new();

    /// <summary>每個分頁的上一頁／下一頁導航歷史。</summary>
    private readonly Dictionary<AntdUI.TabPage, TabNavigationHistory> _tabNavHistories = new();

    /// <summary>列表模式各分頁完整（未過濾）的 GalleryListItem 清單，供記憶體內即時過濾與排序。</summary>
    private readonly Dictionary<AntdUI.TabPage, List<GalleryListItem>> _tabFullListItems = new();

    private bool _syncingSortUi;
    private XButtonMessageFilter? _xButtonFilter;

    /// <summary>
    /// 每個容器（FlowLayoutPanel / Table）的非同步載入世代號，用於連續快速切換檢視模式或重新整理時防止舊結果覆蓋新結果。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Control, int> _loadGenerations = new();

    /// <summary>
    /// 共用 TooltipComponent — attach 到動態建的 button (例：每個 tab 的 btnRefresh)，
    /// 跟主視窗生命週期一致；ArrowAlign 跟 Segmented TooltipConfig 一致 (Bottom)
    /// </summary>
    private readonly AntdUI.TooltipComponent _btnTooltip = new() { ArrowAlign = AntdUI.TAlign.Bottom };

    // 純相框 SVG（移除 <text> — AntdUI SVG renderer 不渲染 text 元素，字母改用 SegmentedItem.Text 顯示）

    private GalleryViewMode GetTabMode(AntdUI.TabPage? page)
    {
        if (page == null) return GetDefaultGalleryViewMode();
        return _tabViewModes.TryGetValue(page, out var mode) ? mode : GetDefaultGalleryViewMode();
    }

    private GalleryViewMode GetDefaultGalleryViewMode()
    {
        return _prefs?.DefaultGalleryViewMode switch
        {
            "Small" => GalleryViewMode.Small,
            "Large" => GalleryViewMode.Large,
            "List" => GalleryViewMode.List,
            _ => GalleryViewMode.Medium,
        };
    }

    private TabSortFilterState GetOrCreateTabSortFilterState(AntdUI.TabPage page)
    {
        if (!_tabSortFilterStates.TryGetValue(page, out var state))
        {
            state = new TabSortFilterState
            {
                SortField = _prefs?.DefaultGallerySortField ?? GallerySortField.Name,
                SortDescending = _prefs?.DefaultGallerySortDescending ?? false,
            };
            _tabSortFilterStates[page] = state;
        }
        return state;
    }

    private TabNavigationHistory GetOrCreateTabNavHistory(AntdUI.TabPage page)
    {
        if (!_tabNavHistories.TryGetValue(page, out var history))
        {
            history = new TabNavigationHistory();
            _tabNavHistories[page] = history;
        }
        return history;
    }

    /// <summary>當前開著的 Drawer Form ref；ShowAuthorInfo / ShowWorkDetail 時 open，CloseRequested 時 Close。</summary>
    private Form? _detailDrawerForm;

    /// <summary>當前 drawer 的點擊外部偵測 filter（drawer 開時 Add，關時 Remove）。</summary>
    private DrawerOutsideClickFilter? _drawerClickFilter;

    /// <summary>tree 顯示模式（#48 Phase 1）— Folder 是現有 file system tree；其他 4 種是虛擬庫。</summary>
    public enum TreeViewMode { Folder, ByAuthor, ByTag, MyLib, ReadingTrack }

    private TreeViewMode _currentViewMode = TreeViewMode.Folder;

    /// <summary>Tag mode 多選 chip 累積的 tag id（AND filter）。</summary>
    private readonly HashSet<long> _selectedTagIds = new();

    /// <summary>Tag mode chip → tag name 對應（chip remove 時用）。</summary>
    private readonly Dictionary<long, string> _selectedTagNames = new();

    private int? _pendingSidebarDistance;
    private bool _userMovedSidebarSplitter;
    private bool _applyingSidebarDistance;

    // 開書非同步化、防重複開窗與 380ms 延遲載入氣泡
    private readonly Dictionary<string, ReaderForm> _openReadersByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openingReaderPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly DelayedLoadingTracker _readerOpenTracker = new();
    private System.Windows.Forms.Timer? _readerOpenTimer;
    private long _readerOpenToken;
    private AntdUI.Panel? _readerLoadingOverlay;
    private AntdUI.Button? _readerLoadingSpinner;
    private System.Windows.Forms.Label? _readerLoadingLabel;

    public GalleryControl()
    {
        InitializeComponent();

        // GalleryControl 自己開 DoubleBuffer：tabsContent 切換 / resize 時減抖
        DoubleBuffered = true;

        splitter2.SplitterMoving += (s, e) =>
        {
            if (!_applyingSidebarDistance) _userMovedSidebarSplitter = true;
        };
        splitter2.SizeChanged += (s, e) => TryApplyPendingSidebarSplitterDistance();

        InitReaderLoadingIndicator();

        WireDrawerEvents();
        WireViewModeToggle();
        WireSearchBox();

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
            // MyLib mode 空白右鍵 → 「新增閱讀清單」選單；其餘 mode → refresh-only
            if (_currentViewMode == TreeViewMode.MyLib) ShowMyLibBlankContextMenu();
            else ShowTreeBlankContextMenu();
        };

        // 處理頁籤關閉事件 — 清掉 _tabViewModes / _tabVirtualPaths / _loadGenerations 內對應的 entry 避免 dispose 後 reference 殘留
        tabsContent.ClosingPage += (s, e) =>
        {
            if (e.Value != null)
            {
                if (WorkContextMenuHelper.IsDashboardTag(e.Value.Tag as string)) return false;
                CleanupTabState(e.Value);
            }
            return true;
        };

        // 分頁標頭右鍵選單
        tabsContent.TabClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.Item != null)
            {
                e.Cancel = true;
                ShowTabHeaderContextMenu(e.Item);
            }
        };

        // 每個 Tab 自帶 breadcrumb，內容在 OpenFolderTab 建立 / 更新當前 Tab 時填好，
        // 切 Tab 不需重填，因此不綁 SelectedIndexChanged。
        ApplyLocalization();
        Disposed += (_, _) =>
        {
            UnregisterXButtonFilter();
            CleanupReaderOpenLoading();
        };
    }

    private void InitReaderLoadingIndicator()
    {
        _readerLoadingSpinner = new AntdUI.Button
        {
            Name = "readerLoadingSpinner",
            Dock = DockStyle.Left,
            Loading = true,
            Ghost = true,
            Size = LogicalToDeviceUnits(new Size(28, 28)),
            ForeColor = Core.ThemeColors.Primary,
            Enabled = true,
        };
        _readerLoadingLabel = new System.Windows.Forms.Label
        {
            Name = "readerLoadingLabel",
            Dock = DockStyle.Fill,
            Text = "正在載入閱讀器...",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft JhengHei UI", 9.5F),
            ForeColor = Core.ThemeColors.Text,
            BackColor = Color.Transparent,
        };
        _readerLoadingOverlay = new AntdUI.Panel
        {
            Name = "readerLoadingOverlay",
            Size = LogicalToDeviceUnits(new Size(210, 44)),
            Radius = 10,
            BorderWidth = 1,
            BorderColor = Core.ThemeColors.Border,
            Back = Core.ThemeColors.Surface,
            BackColor = Core.ThemeColors.Surface,
            Padding = new Padding(12, 8, 12, 8),
            Visible = false,
        };
        _readerLoadingOverlay.Controls.Add(_readerLoadingLabel);
        _readerLoadingOverlay.Controls.Add(_readerLoadingSpinner);

        splitter2.Panel2.Controls.Add(_readerLoadingOverlay);
        _readerLoadingOverlay.BringToFront();
        tabsContent.SizeChanged += (_, _) => CenterReaderLoadingOverlay();
        tabsContent.LocationChanged += (_, _) => CenterReaderLoadingOverlay();

        _readerOpenTimer = new System.Windows.Forms.Timer { Interval = DelayedLoadingTracker.DefaultDelayMs };
        _readerOpenTimer.Tick += (_, _) =>
        {
            _readerOpenTimer?.Stop();
            if (!IsDisposed && _readerOpenTracker.MarkDelayElapsed(_readerOpenToken))
            {
                ShowReaderLoadingOverlay();
            }
        };
    }

    private void CenterReaderLoadingOverlay()
    {
        if (_readerLoadingOverlay == null || _readerLoadingOverlay.IsDisposed || tabsContent.IsDisposed) return;
        int x = tabsContent.Left + Math.Max(0, (tabsContent.Width - _readerLoadingOverlay.Width) / 2);
        int y = tabsContent.Top + Math.Max(0, (tabsContent.Height - _readerLoadingOverlay.Height) / 2);
        _readerLoadingOverlay.Location = new Point(x, y);
    }

    private long BeginReaderLoadingIndicator()
    {
        long token = _readerOpenTracker.Begin();
        _readerOpenToken = token;
        if (_readerLoadingOverlay != null && !_readerLoadingOverlay.IsDisposed)
            _readerLoadingOverlay.Visible = false;
        if (_readerOpenTimer != null)
        {
            _readerOpenTimer.Stop();
            _readerOpenTimer.Start();
        }
        return token;
    }

    private void ShowReaderLoadingOverlay()
    {
        if (_readerLoadingOverlay == null || _readerLoadingOverlay.IsDisposed) return;
        CenterReaderLoadingOverlay();
        _readerLoadingOverlay.Visible = true;
        _readerLoadingOverlay.BringToFront();
    }

    private void CompleteReaderLoadingIndicator(long token)
    {
        if (_readerOpenTracker.Complete(token))
        {
            _readerOpenTimer?.Stop();
            if (_readerLoadingOverlay != null && !_readerLoadingOverlay.IsDisposed)
                _readerLoadingOverlay.Visible = false;
        }
    }

    private void CleanupReaderOpenLoading()
    {
        _readerOpenTracker.Reset();
        if (_readerOpenTimer != null)
        {
            try { _readerOpenTimer.Stop(); _readerOpenTimer.Dispose(); } catch { }
            _readerOpenTimer = null;
        }
        if (_readerLoadingOverlay != null && !_readerLoadingOverlay.IsDisposed)
        {
            _readerLoadingOverlay.Visible = false;
        }
    }

    public void ApplyLocalization()
    {
        // 搜尋 box placeholder（DSL 範例）
        txtSearchDsl.PlaceholderText = LocalizationManager.GetString("SearchDslPlaceholder")
            ?? "搜尋…  例：artist:flugel tag:xx -tag:DL版";

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
            foreach (var btnBack in page.Controls.Find("tabBtnBack", true).OfType<AntdUI.Button>())
            {
                _btnTooltip.SetTip(btnBack, "上一頁 (Alt+Left)");
            }
            foreach (var btnFwd in page.Controls.Find("tabBtnForward", true).OfType<AntdUI.Button>())
            {
                _btnTooltip.SetTip(btnFwd, "下一頁 (Alt+Right)");
            }
            foreach (var btnDbRefresh in page.Controls.Find("dbBtnRefresh", true).OfType<AntdUI.Button>())
            {
                _btnTooltip.SetTip(btnDbRefresh, LocalizationManager.GetString("MenuRefresh") ?? "重新整理");
            }
            foreach (var btnDir in page.Controls.Find("tabSortDir", true).OfType<AntdUI.Button>())
            {
                var st = GetOrCreateTabSortFilterState(page);
                _btnTooltip.SetTip(btnDir, st.SortDescending ? "降冪排序" : "升冪排序");
            }
            foreach (var btnClear in page.Controls.Find("tabBtnClearFilter", true).OfType<AntdUI.Button>())
            {
                _btnTooltip.SetTip(btnClear, LocalizationManager.GetString("FilterClearTooltip") ?? "清除過濾條件");
            }
            foreach (var txtFilter in page.Controls.Find("tabTxtQuickFilter", true).OfType<AntdUI.Input>())
            {
                txtFilter.PlaceholderText = LocalizationManager.GetString("QuickFilterPlaceholder") ?? "本頁過濾（檔名／標題）...";
            }
            foreach (var btnSaveVLib in page.Controls.Find("tabBtnSaveVLib", true).OfType<AntdUI.Button>())
            {
                btnSaveVLib.Text = "存為清單";
                _btnTooltip.SetTip(btnSaveVLib, "將目前搜尋條件存為自訂閱讀清單");
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

        // segViewMode（虛擬庫 ViewMode toggle）— 高亮色用主題 Primary
        // segViewModeHost 用 Padding+BackColor 露 1px 邊技法做上下框線
        segViewModeHost.BackColor = Core.ThemeColors.Border;
        segViewMode.BackColor = Core.ThemeColors.Surface;
        segViewMode.BackActive = Core.ThemeColors.Primary;
        segViewMode.ForeActive = Core.ThemeColors.HeaderText;
        segViewMode.ForeColor = Core.ThemeColors.Text;
        flowChipBar.BackColor = Core.ThemeColors.Surface;

        // 搜尋 input 區
        searchBoxPanel.BackColor = Core.ThemeColors.Surface;
        txtSearchDsl.BackColor = Core.ThemeColors.Background;
        txtSearchDsl.ForeColor = Core.ThemeColors.Text;
        // searchHintBarHost：AntdUI.Panel 圓角白底 + 1px 邊框
        searchHintBarHost.BackColor = Color.White;
        searchHintBarHost.Back = Color.White;
        searchHintBarHost.BorderColor = Core.ThemeColors.Border;
        searchHintBar.BackColor = Color.Transparent;
        // chip 內部會在 OnPaint 取 ThemeColors，這裡只重畫
        foreach (var c in searchHintBar.Controls.OfType<SearchHintChip>()) c.Invalidate();

        // 動態建立的 Tab 內 panel 也要重套
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            page.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabContentHost", true).OfType<System.Windows.Forms.Panel>())
                p.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabHeader", true).OfType<AntdUI.Panel>())
                p.Back = Core.ThemeColors.TabHeader;
            foreach (var p in page.Controls.Find("tabQuickFilterBar", true).OfType<AntdUI.Panel>())
                p.Back = Core.ThemeColors.Surface;
            foreach (var l in page.Controls.Find("tabLblFilterCount", true).OfType<System.Windows.Forms.Label>())
                l.ForeColor = Core.ThemeColors.Text;
            foreach (var inp in page.Controls.Find("tabTxtQuickFilter", true).OfType<AntdUI.Input>())
            {
                inp.BackColor = Core.ThemeColors.Background;
                inp.ForeColor = Core.ThemeColors.Text;
            }
            foreach (var btn in page.Controls.Find("tabSortDir", true).OfType<AntdUI.Button>())
            {
                btn.ForeColor = Core.ThemeColors.Text;
                btn.BackHover = Core.ThemeColors.Secondary;
            }
            foreach (var btnName in new[] { "tabBtnBack", "tabBtnForward" })
            {
                foreach (var btn in page.Controls.Find(btnName, true).OfType<AntdUI.Button>())
                {
                    btn.ForeColor = Core.ThemeColors.Text;
                    btn.BackHover = Core.ThemeColors.Secondary;
                }
            }
            foreach (var btn in page.Controls.Find("tabBtnClearFilter", true).OfType<AntdUI.Button>())
            {
                btn.ForeColor = Core.ThemeColors.Text;
                btn.BackHover = Core.ThemeColors.Secondary;
            }
            foreach (var btn in page.Controls.Find("tabBtnSaveVLib", true).OfType<AntdUI.Button>())
            {
                btn.ForeColor = Core.ThemeColors.Text;
                btn.BackHover = Core.ThemeColors.Secondary;
            }
            foreach (var sel in page.Controls.Find("tabSortField", true).OfType<AntdUI.Select>())
            {
                sel.BackColor = Core.ThemeColors.Background;
                sel.ForeColor = Core.ThemeColors.Text;
            }
            foreach (var f in page.Controls.Find("tabFlowLayout", true).OfType<FlowLayoutPanel>())
                f.BackColor = Core.ThemeColors.Surface;
            foreach (var t in page.Controls.Find("tabTableList", true).OfType<AntdUI.Table>())
                t.BackColor = Core.ThemeColors.Surface;

            if (page.Tag as string == "DASHBOARD")
            {
                var banner = page.Controls.Find("dbBanner", true).OfType<AntdUI.Panel>().FirstOrDefault();
                if (banner != null)
                {
                    banner.BackColor = Core.ThemeColors.Surface;
                    banner.Back = Core.ThemeColors.TabHeader;
                }
                var bannerFlow = banner?.Controls.OfType<BufferedFlowLayoutPanel>().FirstOrDefault();
                if (bannerFlow != null)
                {
                    bannerFlow.BackColor = Core.ThemeColors.TabHeader;
                }
                var flowStats = page.Controls.Find("dbFlowStats", true).OfType<FlowLayoutPanel>().FirstOrDefault();
                if (flowStats != null)
                {
                    flowStats.BackColor = Core.ThemeColors.TabHeader;
                }
                foreach (var btn in page.Controls.Find("dbBtnRefresh", true).OfType<AntdUI.Button>())
                {
                    btn.ForeColor = Core.ThemeColors.Text;
                    btn.BackHover = Core.ThemeColors.Secondary;
                }
                foreach (var lblName in new[] { "dbLblWelcome", "dbLblStatsPrefix", "dbLblStatsMid1", "dbLblStatsMid2", "dbLblStatsSuffix" })
                {
                    var lbl = page.Controls.Find(lblName, true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                    if (lbl != null)
                    {
                        lbl.ForeColor = Core.ThemeColors.Text;
                        lbl.BackColor = Core.ThemeColors.TabHeader;
                    }
                }
                foreach (var statName in new[] { "dbStatAll", "dbStatCompleted", "dbStatReading" })
                {
                    var lbl = page.Controls.Find(statName, true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                    if (lbl != null)
                    {
                        lbl.ForeColor = Core.ThemeColors.Primary;
                        lbl.BackColor = Core.ThemeColors.TabHeader;
                    }
                }
                var lblRecentlyReadTitle = page.Controls.Find("dbLblRecentlyReadTitle", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                if (lblRecentlyReadTitle != null) lblRecentlyReadTitle.ForeColor = Core.ThemeColors.Text;
                var lblRecentlyAddedTitle = page.Controls.Find("dbLblRecentlyAddedTitle", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                if (lblRecentlyAddedTitle != null) lblRecentlyAddedTitle.ForeColor = Core.ThemeColors.Text;
                foreach (var linkName in new[] { "dbLinkMoreRecentRead", "dbLinkMoreRecentAdded" })
                {
                    var link = page.Controls.Find(linkName, true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                    if (link != null) link.ForeColor = Core.ThemeColors.Primary;
                }
            }
        }
        if (_readerLoadingOverlay != null && !_readerLoadingOverlay.IsDisposed)
        {
            _readerLoadingOverlay.Back = Core.ThemeColors.Surface;
            _readerLoadingOverlay.BackColor = Core.ThemeColors.Surface;
            _readerLoadingOverlay.BorderColor = Core.ThemeColors.Border;
        }
        if (_readerLoadingLabel != null && !_readerLoadingLabel.IsDisposed)
        {
            _readerLoadingLabel.ForeColor = Core.ThemeColors.Text;
        }
        if (_readerLoadingSpinner != null && !_readerLoadingSpinner.IsDisposed)
        {
            _readerLoadingSpinner.ForeColor = Core.ThemeColors.Primary;
        }
        this.Refresh();
    }

    // Tree 相關方法（UpdateTreeLoadingText / Load*TreeAsync / ChipBar / 虛擬庫 click /
    // 右鍵選單 / treeFolders 事件）已抽到 GalleryControl.Tree.cs

    private TagTranslator? _tagTranslator;

    public void Initialize(AppConfig config, CoverManager coverManager,
                           IAssetMetaStore? metaStore = null,
                           ReadRecordQueue? readRecordQueue = null,
                           UserPreferences? prefs = null,
                           TagTranslator? tagTranslator = null)
    {
        _config = config;
        _coverManager = coverManager;
        _metaStore = metaStore;
        _readRecordQueue = readRecordQueue;
        _prefs = prefs;
        _tagTranslator = tagTranslator;

        // ctor 跑 WireDrawerEvents 時 _config 還沒設 → 按鈕可見性沒生效
        // 這邊 Initialize 完成後 sync 一次
        if (detailDrawerHost?.WorkPanel != null)
        {
            detailDrawerHost.WorkPanel.ShowFetchExternalButton = config.EnableExternalMetadata;
            detailDrawerHost.AuthorPanel.ShowBatchFetchButton = config.EnableExternalMetadata;
            detailDrawerHost.WorkPanel.SetTagTranslator(tagTranslator);
            detailDrawerHost.AuthorPanel.SetTagTranslator(tagTranslator);
            // schema v6: WorkDetailPanel chip 顯示譯名時優先讀 tag_translation
            detailDrawerHost.WorkPanel.SetMetaStore(metaStore);
        }

        // Recorded fired from background thread → marshal to UI 同步卡片 icon
        if (_readRecordQueue != null)
        {
            _readRecordQueue.Recorded += path =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(() => RefreshCardReadStatusByPath(path)); }
                catch { /* handle 還沒建好或已 disposed */ }
            };
        }

        _ = LoadTreeAsync();
        CreateDashboardTabPage();
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
            LoadCurrentViewMode(page);
        }
    }

    // ResizeCardsOnTab / SyncTabVisibility 已抽到 GalleryControl.Tabs.cs
    // RefreshCardReadStatusByPath 已抽到 GalleryControl.Gallery.cs

    public void OnConfigUpdated(AppConfig config)
    {
        _config = config;
        // 外部 metadata 啟用狀態變更 → 刷新 WorkDetail / AuthorInfo 按鈕可見性
        if (detailDrawerHost?.WorkPanel != null)
        {
            detailDrawerHost.WorkPanel.ShowFetchExternalButton = _config.EnableExternalMetadata;
            detailDrawerHost.AuthorPanel.ShowBatchFetchButton = _config.EnableExternalMetadata;
        }
        _ = LoadTreeAsync();
    }

    /// <summary>SettingsForm 儲存後 MainForm 呼叫，傳新 prefs 進來讓 reader 偏好即時生效。</summary>
    public void OnPrefsUpdated(UserPreferences prefs)
    {
        _prefs = prefs;
    }

    /// <summary>套用已儲存的側邊欄寬度（若容器尚未撐開則延後至 SizeChanged 安全套用，不覆蓋使用者手動拖曳）。</summary>
    public void ApplySavedSidebarSplitterDistance(int? savedDistance)
    {
        if (IsDisposed || splitter2.IsDisposed || savedDistance is not > 0) return;
        _pendingSidebarDistance = savedDistance;
        _userMovedSidebarSplitter = false;
        TryApplyPendingSidebarSplitterDistance();
    }

    private void TryApplyPendingSidebarSplitterDistance()
    {
        if (_pendingSidebarDistance is not int target || _userMovedSidebarSplitter || IsDisposed || splitter2.IsDisposed)
            return;

        int minReadyWidth = WindowLayoutHelper.MinSidebarWidth + WindowLayoutHelper.MinGalleryContentWidth + splitter2.SplitterWidth;
        if (splitter2.Width < minReadyWidth) return;

        int clamped = WindowLayoutHelper.ClampSplitterDistance(
            target,
            splitter2.Width,
            WindowLayoutHelper.MinSidebarWidth,
            WindowLayoutHelper.MinGalleryContentWidth);

        int minAllowed = Math.Max(splitter2.Panel1MinSize, WindowLayoutHelper.MinSidebarWidth);
        int maxAllowed = Math.Max(minAllowed, splitter2.Width - splitter2.SplitterWidth - Math.Max(splitter2.Panel2MinSize, WindowLayoutHelper.MinGalleryContentWidth));
        int finalDistance = Math.Clamp(clamped, minAllowed, maxAllowed);

        _applyingSidebarDistance = true;
        try
        {
            splitter2.SplitterDistance = finalDistance;
        }
        finally
        {
            _applyingSidebarDistance = false;
        }
        _pendingSidebarDistance = null;
    }

    /// <summary>取得目前側邊欄寬度供持久化。</summary>
    public int? GetSidebarSplitterDistance()
    {
        if (IsDisposed || splitter2.IsDisposed) return null;
        if (_pendingSidebarDistance.HasValue && !_userMovedSidebarSplitter) return _pendingSidebarDistance.Value;
        return splitter2.SplitterDistance > 0 ? splitter2.SplitterDistance : null;
    }

    /// <summary>聚焦左側 DSL 搜尋框並全選內容。</summary>
    public void FocusSearchBox()
    {
        if (IsDisposed || txtSearchDsl.IsDisposed) return;
        txtSearchDsl.Focus();
        txtSearchDsl.SelectAll();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterXButtonFilter();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterXButtonFilter();
        base.OnHandleDestroyed(e);
    }

    private void RegisterXButtonFilter()
    {
        if (_xButtonFilter != null || IsDisposed) return;
        _xButtonFilter = new XButtonMessageFilter(this);
        Application.AddMessageFilter(_xButtonFilter);
    }

    private void UnregisterXButtonFilter()
    {
        if (_xButtonFilter == null) return;
        Application.RemoveMessageFilter(_xButtonFilter);
        _xButtonFilter = null;
    }

    private sealed class XButtonMessageFilter : IMessageFilter
    {
        private const int WM_XBUTTONUP = 0x020C;
        private readonly GalleryControl _owner;

        public XButtonMessageFilter(GalleryControl owner)
        {
            _owner = owner;
        }

        public bool PreFilterMessage(ref System.Windows.Forms.Message m)
        {
            if (m.Msg != WM_XBUTTONUP) return false;
            if (_owner.IsDisposed || !_owner.IsHandleCreated || !_owner.Visible) return false;

            var hostForm = _owner.FindForm();
            if (hostForm == null || hostForm.IsDisposed || Form.ActiveForm != hostForm) return false;

            var target = Control.FromHandle(m.HWnd);
            if (target == null || (target != _owner && !_owner.Contains(target))) return false;

            int action = TabNavigationHistory.ParseXButtonAction(m.WParam.ToInt64());
            if (action < 0)
            {
                _owner.NavigateBackCurrentTab();
                return true;
            }
            if (action > 0)
            {
                _owner.NavigateForwardCurrentTab();
                return true;
            }
            return false;
        }
    }

    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            RefreshCurrentTab();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Left))
        {
            NavigateBackCurrentTab();
            return true;
        }
        if (keyData == (Keys.Alt | Keys.Right))
        {
            NavigateForwardCurrentTab();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F))
        {
            FocusSearchBox();
            return true;
        }
        if (keyData == (Keys.Control | Keys.W))
        {
            CloseCurrentTab();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Tab))
        {
            SwitchAdjacentTab(reverse: false);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SwitchAdjacentTab(reverse: true);
            return true;
        }
        if (keyData == Keys.Escape)
        {
            // 1. 優先關閉搜尋框的自動補全或 DSL 提示彈窗
            bool inSearchBox = !txtSearchDsl.IsDisposed && (txtSearchDsl.Focused || txtSearchDsl.ContainsFocus);
            if (inSearchBox)
            {
                bool closedSearchPopup = false;
                if (_suggestBox != null && _suggestBox.Visible)
                {
                    _suggestDebounceTimer?.Stop();
                    _suggestBox.HideSuggestions();
                    closedSearchPopup = true;
                }
                if (searchHintBarHost.Visible)
                {
                    searchHintBarHost.Visible = false;
                    closedSearchPopup = true;
                }
                if (closedSearchPopup) return true;
            }

            // 2. 若詳情抽屜開啟中，依未存檔防護關閉
            if (_detailDrawerForm != null && !_detailDrawerForm.IsDisposed)
            {
                RequestCloseDetailDrawer();
                return true;
            }

            // 3. 若就地過濾框聚焦，清除過濾並退回畫廊焦點
            if (HandleQuickFilterEscape(tabsContent.SelectedTab))
            {
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Drawer 相關方法（WireDrawerEvents / SaveAuthorEdit / SaveWorkEdit /
    // ConfirmAndResetWorkReading / ConfirmAndResetAuthorReading / RecordReadAndOpenViewer /
    // ShowAuthorInfo / ShowWorkDetail / LoadWorkCoverAsync / ExpandDrawerTo /
    // ToggleDrawerMaximize / SetDrawerWidth / EnsureDetailDrawerOpen）已抽到 GalleryControl.Drawer.cs



}

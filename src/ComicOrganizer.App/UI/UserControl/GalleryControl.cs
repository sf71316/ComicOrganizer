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

    public GalleryControl()
    {
        InitializeComponent();

        // GalleryControl 自己開 DoubleBuffer：tabsContent 切換 / resize 時減抖
        DoubleBuffered = true;

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

        // 處理頁籤關閉事件 — 清掉 _tabViewModes 內對應的 entry 避免 dispose 後 reference 殘留
        tabsContent.ClosingPage += (s, e) =>
        {
            if (e.Value != null)
            {
                if (e.Value.Tag as string == "DASHBOARD") return false;
                _tabViewModes.Remove(e.Value);
            }
            return true;
        };

        // 每個 Tab 自帶 breadcrumb，內容在 OpenFolderTab 建立 / 更新當前 Tab 時填好，
        // 切 Tab 不需重填，因此不綁 SelectedIndexChanged。
        ApplyLocalization();
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
                var lblWelcome = page.Controls.Find("dbLblWelcome", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                if (lblWelcome != null)
                {
                    lblWelcome.ForeColor = Core.ThemeColors.Text;
                    lblWelcome.BackColor = Core.ThemeColors.TabHeader;
                }
                var lblStats = page.Controls.Find("dbLblStats", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                if (lblStats != null)
                {
                    lblStats.ForeColor = Core.ThemeColors.Text;
                    lblStats.BackColor = Core.ThemeColors.TabHeader;
                }
                var lblRecentlyReadTitle = page.Controls.Find("dbLblRecentlyReadTitle", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                if (lblRecentlyReadTitle != null) lblRecentlyReadTitle.ForeColor = Core.ThemeColors.Text;
                var lblRecentlyAddedTitle = page.Controls.Find("dbLblRecentlyAddedTitle", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
                if (lblRecentlyAddedTitle != null) lblRecentlyAddedTitle.ForeColor = Core.ThemeColors.Text;
            }
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
            if (page.Tag is string path) LoadCurrentViewMode(page, path);
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

    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            RefreshCurrentTab();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Drawer 相關方法（WireDrawerEvents / SaveAuthorEdit / SaveWorkEdit /
    // ConfirmAndResetWorkReading / ConfirmAndResetAuthorReading / RecordReadAndOpenViewer /
    // ShowAuthorInfo / ShowWorkDetail / LoadWorkCoverAsync / ExpandDrawerTo /
    // ToggleDrawerMaximize / SetDrawerWidth / EnsureDetailDrawerOpen）已抽到 GalleryControl.Drawer.cs



}

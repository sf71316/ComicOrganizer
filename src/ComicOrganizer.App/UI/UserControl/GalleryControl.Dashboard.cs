using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using System.ComponentModel;

namespace ComicOrganizer.App.UI;

public partial class GalleryControl
{
    /// <summary>
    /// 建立首頁儀表板 TabPage，並加入 tabsContent 最左側。
    /// </summary>
    public AntdUI.TabPage CreateDashboardTabPage()
    {
        var newPage = new AntdUI.TabPage
        {
            Text = "🏠 首頁",
            Tag = "DASHBOARD",
            BackColor = Core.ThemeColors.Surface
        };

        // 外層垂直滾動 Panel，用來處理卡片與 Banner 的排版
        var flowMain = new BufferedFlowLayoutPanel
        {
            Name = "dbFlowMain",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Core.ThemeColors.Surface,
            Padding = new Padding(24, 16, 24, 16),
        };

        // Banner 橫幅：大標 + 可互動漫畫庫統計 + 右側重新整理按鈕
        var banner = new AntdUI.Panel
        {
            Name = "dbBanner",
            Width = 600,
            Height = LogicalToDeviceUnits(86),
            BackColor = Core.ThemeColors.Surface,
            Back = Core.ThemeColors.TabHeader,
            Radius = 8,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 16),
        };

        var btnDbRefresh = new AntdUI.Button
        {
            Name = "dbBtnRefresh",
            Dock = DockStyle.Right,
            Ghost = true,
            IconSvg = "RedoOutlined",
            IconRatio = 1F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(36, 36)),
            Cursor = Cursors.Hand,
        };
        btnDbRefresh.Click += (s, e) => _ = LoadDashboardDataAsync(newPage);
        _btnTooltip.SetTip(btnDbRefresh, LocalizationManager.GetString("MenuRefresh") ?? "重新整理");

        // 改用 BufferedFlowLayoutPanel 以啟用 DoubleBuffered，背景色設為與 banner 內底色一樣
        var bannerFlow = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Core.ThemeColors.TabHeader,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };

        var lblWelcome = new System.Windows.Forms.Label
        {
            Name = "dbLblWelcome",
            Text = "歡迎回來！",
            Font = new Font("Microsoft JhengHei UI", 16F, FontStyle.Bold),
            ForeColor = Core.ThemeColors.Text,
            BackColor = Core.ThemeColors.TabHeader,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };

        var flowStats = new BufferedFlowLayoutPanel
        {
            Name = "dbFlowStats",
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Core.ThemeColors.TabHeader,
            Padding = new Padding(0),
            Margin = new Padding(0, 2, 0, 0),
        };

        var lblStatsPrefix = CreateStatsTextLabel("dbLblStatsPrefix", "正在載入漫畫庫統計資訊...");
        var statAll = CreateStatsLinkLabel("dbStatAll", "", "點擊在新分頁開啟全部作品");
        statAll.Visible = false;
        statAll.Click += (s, e) => _ = OpenDashboardVirtualTabAsync(
            "📚 全部作品",
            "BookOutlined",
            () => _metaStore?.ListAllAssetPaths() ?? Array.Empty<string>());

        var lblStatsMid1 = CreateStatsTextLabel("dbLblStatsMid1", "，已看完了 ");
        lblStatsMid1.Visible = false;

        var statCompleted = CreateStatsLinkLabel("dbStatCompleted", "", "點擊在新分頁開啟已完成作品");
        statCompleted.Visible = false;
        statCompleted.Click += (s, e) => _ = OpenDashboardVirtualTabAsync(
            "✅ 已完成",
            "CheckCircleOutlined",
            () => _metaStore?.ListWorksByReadStatus(ReadStatus.Completed) ?? Array.Empty<string>());

        var lblStatsMid2 = CreateStatsTextLabel("dbLblStatsMid2", "，有 ");
        lblStatsMid2.Visible = false;

        var statReading = CreateStatsLinkLabel("dbStatReading", "", "點擊在新分頁開啟閱讀中作品");
        statReading.Visible = false;
        statReading.Click += (s, e) => _ = OpenDashboardVirtualTabAsync(
            "📖 閱讀中",
            "ReadOutlined",
            () => _metaStore?.ListWorksByReadStatus(ReadStatus.Reading) ?? Array.Empty<string>());

        var lblStatsSuffix = CreateStatsTextLabel("dbLblStatsSuffix", " 正在閱讀中。");
        lblStatsSuffix.Visible = false;

        flowStats.Controls.Add(lblStatsPrefix);
        flowStats.Controls.Add(statAll);
        flowStats.Controls.Add(lblStatsMid1);
        flowStats.Controls.Add(statCompleted);
        flowStats.Controls.Add(lblStatsMid2);
        flowStats.Controls.Add(statReading);
        flowStats.Controls.Add(lblStatsSuffix);

        bannerFlow.Controls.Add(lblWelcome);
        bannerFlow.Controls.Add(flowStats);
        banner.Controls.Add(bannerFlow);
        banner.Controls.Add(btnDbRefresh);

        // Section 1: Recently Read
        var headerRecentlyRead = CreateDashboardSectionHeader(
            "dbHeaderRecentlyRead",
            "dbLblRecentlyReadTitle",
            "🕒 最近看過",
            "dbLinkMoreRecentRead",
            () => _ = OpenDashboardVirtualTabAsync(
                "🕒 最近看過 (前 50 筆)",
                "HistoryOutlined",
                () => _metaStore?.ListRecentlyRead(50) ?? Array.Empty<string>()));

        var flowRecentlyRead = new BufferedFlowLayoutPanel
        {
            Name = "dbFlowRecentlyRead",
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        // Section 2: Recently Added
        var headerRecentlyAdded = CreateDashboardSectionHeader(
            "dbHeaderRecentlyAdded",
            "dbLblRecentlyAddedTitle",
            "✨ 最近新增",
            "dbLinkMoreRecentAdded",
            () => _ = OpenDashboardVirtualTabAsync(
                "✨ 最近新增 (前 50 筆)",
                "ClockCircleOutlined",
                () => _metaStore?.ListRecentlyAdded(50) ?? Array.Empty<string>()));

        var flowRecentlyAdded = new BufferedFlowLayoutPanel
        {
            Name = "dbFlowRecentlyAdded",
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        // 掛 Layout 事件：FlowLayoutPanel 每次完成排版後，強制修正 banner 與標題列寬度，避免縮放錯位
        flowMain.Layout += (s, e) =>
        {
            int targetWidth = flowMain.ClientSize.Width - flowMain.Padding.Left - flowMain.Padding.Right;
            if (flowMain.VerticalScroll.Visible)
                targetWidth -= SystemInformation.VerticalScrollBarWidth;
            if (targetWidth <= 0) return;

            if (banner.Width != targetWidth) banner.Width = targetWidth;
            if (headerRecentlyRead.Width != targetWidth) headerRecentlyRead.Width = targetWidth;
            if (headerRecentlyAdded.Width != targetWidth) headerRecentlyAdded.Width = targetWidth;
            if (flowRecentlyRead.MaximumSize.Width != targetWidth)
                flowRecentlyRead.MaximumSize = new Size(targetWidth, 0);
            if (flowRecentlyAdded.MaximumSize.Width != targetWidth)
                flowRecentlyAdded.MaximumSize = new Size(targetWidth, 0);
        };

        flowMain.Controls.Add(banner);
        flowMain.Controls.Add(headerRecentlyRead);
        flowMain.Controls.Add(flowRecentlyRead);
        flowMain.Controls.Add(headerRecentlyAdded);
        flowMain.Controls.Add(flowRecentlyAdded);

        newPage.Controls.Add(flowMain);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        // 異步載入資料
        _ = LoadDashboardDataAsync(newPage);

        return newPage;
    }

    private System.Windows.Forms.Label CreateStatsTextLabel(string name, string text)
        => new()
        {
            Name = name,
            Text = text,
            Font = new Font("Microsoft JhengHei UI", 10F),
            ForeColor = Core.ThemeColors.Text,
            BackColor = Core.ThemeColors.TabHeader,
            AutoSize = true,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };

    private System.Windows.Forms.Label CreateStatsLinkLabel(string name, string text, string tooltip)
    {
        var lbl = new System.Windows.Forms.Label
        {
            Name = name,
            Text = text,
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold | FontStyle.Underline),
            ForeColor = Core.ThemeColors.Primary,
            BackColor = Core.ThemeColors.TabHeader,
            Cursor = Cursors.Hand,
            AutoSize = true,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };
        _btnTooltip.SetTip(lbl, tooltip);
        return lbl;
    }

    private static readonly Font DashboardLinkFont = new("Microsoft JhengHei UI", 9.5F);
    private static readonly Font DashboardLinkHoverFont = new("Microsoft JhengHei UI", 9.5F, FontStyle.Underline);

    private System.Windows.Forms.Panel CreateDashboardSectionHeader(
        string panelName,
        string titleLabelName,
        string titleText,
        string linkName,
        Action onMoreClick)
    {
        var header = new System.Windows.Forms.Panel
        {
            Name = panelName,
            Width = 600,
            Height = LogicalToDeviceUnits(32),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 8),
        };

        var lblTitle = new System.Windows.Forms.Label
        {
            Name = titleLabelName,
            Text = titleText,
            Dock = DockStyle.Left,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = Core.ThemeColors.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
        };

        var linkMore = new System.Windows.Forms.Label
        {
            Name = linkName,
            Text = "查看更多 →",
            Dock = DockStyle.Right,
            Font = DashboardLinkFont,
            ForeColor = Core.ThemeColors.Primary,
            TextAlign = ContentAlignment.MiddleRight,
            Cursor = Cursors.Hand,
            AutoSize = true,
            Padding = new Padding(4, 0, 4, 0),
        };
        linkMore.Click += (s, e) => onMoreClick();
        linkMore.MouseEnter += (s, e) => linkMore.Font = DashboardLinkHoverFont;
        linkMore.MouseLeave += (s, e) => linkMore.Font = DashboardLinkFont;
        _btnTooltip.SetTip(linkMore, "在新分頁查看前 50 筆");

        header.Controls.Add(lblTitle);
        header.Controls.Add(linkMore);
        return header;
    }

    private async Task OpenDashboardVirtualTabAsync(
        string title,
        string iconSvg,
        Func<IReadOnlyList<string>> queryPaths)
    {
        if (_metaStore == null || IsDisposed) return;
        ReportJobStarted("正在載入清單...");
        try
        {
            var paths = await Task.Run(queryPaths);
            if (IsDisposed) return;
            OpenVirtualWorksTab(title, paths, forceNew: true, iconSvg: iconSvg, refreshResolver: queryPaths);
        }
        finally
        {
            ReportJobFinished();
        }
    }

    /// <summary>
    /// 異步載入 Dashboard 首頁所需的統計資訊與卡片資料。
    /// </summary>
    public async Task LoadDashboardDataAsync(AntdUI.TabPage page)
    {
        if (_metaStore == null || page.IsDisposed) return;

        var flowMain = page.Controls.Find("dbFlowMain", true).FirstOrDefault() as FlowLayoutPanel;
        var flowStats = page.Controls.Find("dbFlowStats", true).FirstOrDefault() as FlowLayoutPanel;
        var lblStatsPrefix = page.Controls.Find("dbLblStatsPrefix", true).FirstOrDefault() as System.Windows.Forms.Label;
        var statAll = page.Controls.Find("dbStatAll", true).FirstOrDefault() as System.Windows.Forms.Label;
        var lblStatsMid1 = page.Controls.Find("dbLblStatsMid1", true).FirstOrDefault() as System.Windows.Forms.Label;
        var statCompleted = page.Controls.Find("dbStatCompleted", true).FirstOrDefault() as System.Windows.Forms.Label;
        var lblStatsMid2 = page.Controls.Find("dbLblStatsMid2", true).FirstOrDefault() as System.Windows.Forms.Label;
        var statReading = page.Controls.Find("dbStatReading", true).FirstOrDefault() as System.Windows.Forms.Label;
        var lblStatsSuffix = page.Controls.Find("dbLblStatsSuffix", true).FirstOrDefault() as System.Windows.Forms.Label;
        var headerRecentlyRead = page.Controls.Find("dbHeaderRecentlyRead", true).FirstOrDefault() as System.Windows.Forms.Panel;
        var flowRecentlyRead = page.Controls.Find("dbFlowRecentlyRead", true).FirstOrDefault() as FlowLayoutPanel;
        var flowRecentlyAdded = page.Controls.Find("dbFlowRecentlyAdded", true).FirstOrDefault() as FlowLayoutPanel;

        if (flowMain == null || flowRecentlyRead == null || flowRecentlyAdded == null) return;

        int generation = NextLoadGeneration(flowMain);
        ReportJobStarted("正在更新首頁資料...");
        try
        {
            // 1. 載入統計資訊
            long total = 0;
            int completed = 0;
            int reading = 0;
            await Task.Run(() =>
            {
                total = _metaStore.TotalAssetCount();
                completed = _metaStore.ListWorksByReadStatus(ReadStatus.Completed).Count;
                reading = _metaStore.ListWorksByReadStatus(ReadStatus.Reading).Count;
            });

            if (!IsCurrentLoadGeneration(flowMain, generation)) return;

            if (flowStats != null && lblStatsPrefix != null && statAll != null && lblStatsMid1 != null
                && statCompleted != null && lblStatsMid2 != null && statReading != null && lblStatsSuffix != null)
            {
                flowStats.SuspendLayout();
                lblStatsPrefix.Text = "目前漫畫庫內共有 ";
                statAll.Text = $"{total} 部作品";
                statCompleted.Text = $"{completed} 部";
                statReading.Text = $"{reading} 部";
                statAll.Visible = true;
                lblStatsMid1.Visible = true;
                statCompleted.Visible = true;
                lblStatsMid2.Visible = true;
                statReading.Visible = true;
                lblStatsSuffix.Visible = true;
                flowStats.ResumeLayout(true);
            }

            // 2. 載入 Recently Read (最多 6 個)
            IReadOnlyList<string> recentlyReadPaths = Array.Empty<string>();
            await Task.Run(() =>
            {
                recentlyReadPaths = _metaStore.ListRecentlyRead(6);
            });

            if (!IsCurrentLoadGeneration(flowMain, generation)) return;

            if (recentlyReadPaths.Count == 0)
            {
                if (headerRecentlyRead != null) headerRecentlyRead.Visible = false;
                flowRecentlyRead.Visible = false;
                flowRecentlyRead.Controls.Clear();
            }
            else
            {
                if (headerRecentlyRead != null) headerRecentlyRead.Visible = true;
                flowRecentlyRead.Visible = true;
                flowRecentlyRead.Controls.Clear();

                flowRecentlyRead.SuspendLayout();
                foreach (var p in recentlyReadPaths)
                {
                    FileSystemInfo? fsi = Directory.Exists(p) ? new DirectoryInfo(p) : (File.Exists(p) ? new FileInfo(p) : null);
                    if (fsi == null) continue;
                    // Dashboard 卡片顯示 meta (showMetadata=true)
                    var card = BuildCardForGallery(fsi, GalleryViewMode.Medium, showMetadata: true);
                    flowRecentlyRead.Controls.Add(card);
                }
                flowRecentlyRead.ResumeLayout(true);
            }

            // 3. 載入 Recently Added (最多 12 個)
            IReadOnlyList<string> recentlyAddedPaths = Array.Empty<string>();
            await Task.Run(() =>
            {
                recentlyAddedPaths = _metaStore.ListRecentlyAdded(12);
            });

            if (!IsCurrentLoadGeneration(flowMain, generation)) return;

            flowRecentlyAdded.Controls.Clear();
            flowRecentlyAdded.SuspendLayout();
            foreach (var p in recentlyAddedPaths)
            {
                FileSystemInfo? fsi = Directory.Exists(p) ? new DirectoryInfo(p) : (File.Exists(p) ? new FileInfo(p) : null);
                if (fsi == null) continue;
                var card = BuildCardForGallery(fsi, GalleryViewMode.Medium, showMetadata: true);
                flowRecentlyAdded.Controls.Add(card);
            }
            flowRecentlyAdded.ResumeLayout(true);

            flowMain.PerformLayout();
        }
        finally
        {
            ReportJobFinished();
        }
    }
}

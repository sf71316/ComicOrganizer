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

        // Banner 橫幅：大標 + 漫畫庫統計
        var banner = new AntdUI.Panel
        {
            Name = "dbBanner",
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(80),
            BackColor = Color.Transparent,
            Back = Core.ThemeColors.TabHeader,
            Radius = 8,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 16),
        };

        var bannerFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
            Margin = new Padding(0),
        };

        var lblWelcome = new System.Windows.Forms.Label
        {
            Name = "dbLblWelcome",
            Text = "歡迎回來！",
            Font = new Font("Microsoft JhengHei UI", 16F, FontStyle.Bold),
            ForeColor = Core.ThemeColors.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        var lblStats = new System.Windows.Forms.Label
        {
            Name = "dbLblStats",
            Text = "正在載入漫畫庫統計資訊...",
            Font = new Font("Microsoft JhengHei UI", 10F),
            ForeColor = Core.ThemeColors.Text,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0),
        };
        bannerFlow.Controls.Add(lblWelcome);
        bannerFlow.Controls.Add(lblStats);
        banner.Controls.Add(bannerFlow);

        // Section 1: In Progress
        var lblInProgressTitle = new System.Windows.Forms.Label
        {
            Name = "dbLblInProgressTitle",
            Text = "📖 繼續閱讀",
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = Core.ThemeColors.Text,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8),
        };
        var flowInProgress = new BufferedFlowLayoutPanel
        {
            Name = "dbFlowInProgress",
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        // Section 2: Recently Added
        var lblRecentlyAddedTitle = new System.Windows.Forms.Label
        {
            Name = "dbLblRecentlyAddedTitle",
            Text = "🕒 最近新增",
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = Core.ThemeColors.Text,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8),
        };
        var flowRecentlyAdded = new BufferedFlowLayoutPanel
        {
            Name = "dbFlowRecentlyAdded",
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        flowMain.Controls.Add(banner);
        flowMain.Controls.Add(lblInProgressTitle);
        flowMain.Controls.Add(flowInProgress);
        flowMain.Controls.Add(lblRecentlyAddedTitle);
        flowMain.Controls.Add(flowRecentlyAdded);

        newPage.Controls.Add(flowMain);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        // 異步載入資料
        _ = LoadDashboardDataAsync(newPage);

        return newPage;
    }

    /// <summary>
    /// 異步載入 Dashboard 首頁所需的統計資訊與卡片資料。
    /// </summary>
    public async Task LoadDashboardDataAsync(AntdUI.TabPage page)
    {
        if (_metaStore == null) return;

        var flowMain = page.Controls.Find("dbFlowMain", true).FirstOrDefault() as FlowLayoutPanel;
        var banner = page.Controls.Find("dbBanner", true).FirstOrDefault() as AntdUI.Panel;
        var lblStats = page.Controls.Find("dbLblStats", true).FirstOrDefault() as System.Windows.Forms.Label;
        var lblInProgressTitle = page.Controls.Find("dbLblInProgressTitle", true).FirstOrDefault() as System.Windows.Forms.Label;
        var flowInProgress = page.Controls.Find("dbFlowInProgress", true).FirstOrDefault() as FlowLayoutPanel;
        var flowRecentlyAdded = page.Controls.Find("dbFlowRecentlyAdded", true).FirstOrDefault() as FlowLayoutPanel;

        if (flowMain == null || flowInProgress == null || flowRecentlyAdded == null) return;

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

        if (!IsDisposed && lblStats != null)
        {
            lblStats.Text = $"目前漫畫庫內共有 {total} 部作品，已看完了 {completed} 部，有 {reading} 部正在閱讀中。";
        }

        // 2. 載入 In Progress (最多 6 個)
        IReadOnlyList<string> inProgressPaths = Array.Empty<string>();
        await Task.Run(() =>
        {
            inProgressPaths = _metaStore.ListReadingInProgress();
        });

        if (inProgressPaths.Count == 0)
        {
            if (lblInProgressTitle != null) lblInProgressTitle.Visible = false;
            flowInProgress.Visible = false;
        }
        else
        {
            if (lblInProgressTitle != null) lblInProgressTitle.Visible = true;
            flowInProgress.Visible = true;
            flowInProgress.Controls.Clear();
            var displayPaths = inProgressPaths.Take(6).ToList();
            
            flowInProgress.SuspendLayout();
            foreach (var p in displayPaths)
            {
                FileSystemInfo? fsi = Directory.Exists(p) ? new DirectoryInfo(p) : (File.Exists(p) ? new FileInfo(p) : null);
                if (fsi == null) continue;
                // Dashboard 卡片顯示 meta (showMetadata=true)
                var card = BuildCardForGallery(fsi, GalleryViewMode.Medium, showMetadata: true);
                flowInProgress.Controls.Add(card);
            }
            flowInProgress.ResumeLayout(true);
        }

        // 3. 載入 Recently Added (最多 12 個)
        IReadOnlyList<string> recentlyAddedPaths = Array.Empty<string>();
        await Task.Run(() =>
        {
            recentlyAddedPaths = _metaStore.ListRecentlyAdded(12);
        });

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
}

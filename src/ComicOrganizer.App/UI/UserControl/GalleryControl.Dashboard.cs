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
            Width = 600,
            Height = LogicalToDeviceUnits(80),
            BackColor = Core.ThemeColors.Surface,
            Back = Core.ThemeColors.TabHeader,
            Radius = 8,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 16),
        };

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
        var lblStats = new System.Windows.Forms.Label
        {
            Name = "dbLblStats",
            Text = "正在載入漫畫庫統計資訊...",
            Font = new Font("Microsoft JhengHei UI", 10F),
            ForeColor = Core.ThemeColors.Text,
            BackColor = Core.ThemeColors.TabHeader,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0),
        };
        bannerFlow.Controls.Add(lblWelcome);
        bannerFlow.Controls.Add(lblStats);
        banner.Controls.Add(bannerFlow);

        // 掛 Layout 事件：FlowLayoutPanel 每次完成排版後，強制修正 banner 寬度
        // 使用 Layout 而非 Resize，是因為 FlowLayoutPanel 在 Resize 後還會再做一次 LayoutChildren
        // 那次 LayoutChildren 才是最終設定子控制項位置/大小的地方，此時才能有效覆寫 Width。
        flowMain.Layout += (s, e) =>
        {
            int targetWidth = flowMain.ClientSize.Width - flowMain.Padding.Left - flowMain.Padding.Right;
            if (flowMain.VerticalScroll.Visible)
                targetWidth -= SystemInformation.VerticalScrollBarWidth;
            if (targetWidth > 0 && banner.Width != targetWidth)
                banner.Width = targetWidth;
        };

        // Section 1: Recently Read
        var lblRecentlyReadTitle = new System.Windows.Forms.Label
        {
            Name = "dbLblRecentlyReadTitle",
            Text = "🕒 最近看過",
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
            ForeColor = Core.ThemeColors.Text,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8),
        };
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
        var lblRecentlyAddedTitle = new System.Windows.Forms.Label
        {
            Name = "dbLblRecentlyAddedTitle",
            Text = "✨ 最近新增",
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
        flowMain.Controls.Add(lblRecentlyReadTitle);
        flowMain.Controls.Add(flowRecentlyRead);
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
        var lblRecentlyReadTitle = page.Controls.Find("dbLblRecentlyReadTitle", true).FirstOrDefault() as System.Windows.Forms.Label;
        var flowRecentlyRead = page.Controls.Find("dbFlowRecentlyRead", true).FirstOrDefault() as FlowLayoutPanel;
        var flowRecentlyAdded = page.Controls.Find("dbFlowRecentlyAdded", true).FirstOrDefault() as FlowLayoutPanel;

        if (flowMain == null || flowRecentlyRead == null || flowRecentlyAdded == null) return;

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

        // 2. 載入 Recently Read (最多 6 個)
        IReadOnlyList<string> recentlyReadPaths = Array.Empty<string>();
        await Task.Run(() =>
        {
            recentlyReadPaths = _metaStore.ListRecentlyRead(6);
        });

        if (recentlyReadPaths.Count == 0)
        {
            if (lblRecentlyReadTitle != null) lblRecentlyReadTitle.Visible = false;
            flowRecentlyRead.Visible = false;
        }
        else
        {
            if (lblRecentlyReadTitle != null) lblRecentlyReadTitle.Visible = true;
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

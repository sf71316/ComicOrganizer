namespace ComicOrganizer.App.UI;

partial class ReaderForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private System.Windows.Forms.Panel toolbar;
    private System.Windows.Forms.FlowLayoutPanel toolbarLeft;
    private System.Windows.Forms.FlowLayoutPanel toolbarRight;
    private AntdUI.Segmented segViewMode;
    private AntdUI.Segmented segZoomMode;
    private AntdUI.Slider sliderZoom;       // 連續捲動模式時顯示，取代 segZoomMode
    private AntdUI.Switch switchSeamless;   // 連續捲動模式：開啟 = 圖與圖無隔間
    private AntdUI.Label lblSeamless;
    private AntdUI.Button btnPrevWork;
    private AntdUI.Button btnNextWork;
    private AntdUI.Button btnInstantTranslation;
    private AntdUI.Button btnAllTranslation;
    private AntdUI.Button btnDirection;
    private AntdUI.Button btnSpreadCover;
    private AntdUI.Button btnBookmarkToggle;
    private AntdUI.Button btnBookmarkList;
    private AntdUI.Button btnFullscreen;
    private System.Windows.Forms.Panel scrollHost;
    private System.Windows.Forms.PictureBox picPage;
    /// <summary>連續捲動模式專用 VScrollBar，繞過 Windows 控件高度 65535 限制（picPage 不能那麼高）。</summary>
    private System.Windows.Forms.VScrollBar vScrollBarContinuous;
    private System.Windows.Forms.Panel statusBar;
    private System.Windows.Forms.Label lblStatus;
    private AntdUI.Button btnStatusLoading;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        AntdUI.SegmentedItem segViewItem1 = new AntdUI.SegmentedItem();
        AntdUI.SegmentedItem segViewItem2 = new AntdUI.SegmentedItem();
        AntdUI.SegmentedItem segViewItem3 = new AntdUI.SegmentedItem();
        AntdUI.SegmentedItem segZoomItem1 = new AntdUI.SegmentedItem();
        AntdUI.SegmentedItem segZoomItem2 = new AntdUI.SegmentedItem();
        AntdUI.SegmentedItem segZoomItem3 = new AntdUI.SegmentedItem();
        windowBar = new AntdUI.PageHeader();
        toolbar = new System.Windows.Forms.Panel();
        toolbarLeft = new System.Windows.Forms.FlowLayoutPanel();
        toolbarRight = new System.Windows.Forms.FlowLayoutPanel();
        btnPrevWork = new AntdUI.Button();
        btnNextWork = new AntdUI.Button();
        btnBookmarkToggle = new AntdUI.Button();
        btnBookmarkList = new AntdUI.Button();
        btnFullscreen = new AntdUI.Button();
        segZoomMode = new AntdUI.Segmented();
        sliderZoom = new AntdUI.Slider();
        switchSeamless = new AntdUI.Switch();
        lblSeamless = new AntdUI.Label();
        btnInstantTranslation = new AntdUI.Button();
        btnAllTranslation = new AntdUI.Button();
        btnDirection = new AntdUI.Button();
        btnSpreadCover = new AntdUI.Button();
        segViewMode = new AntdUI.Segmented();
        scrollHost = new System.Windows.Forms.Panel();
        picPage = new System.Windows.Forms.PictureBox();
        vScrollBarContinuous = new System.Windows.Forms.VScrollBar();
        statusBar = new System.Windows.Forms.Panel();
        lblStatus = new System.Windows.Forms.Label();
        btnStatusLoading = new AntdUI.Button();
        toolbar.SuspendLayout();
        toolbarLeft.SuspendLayout();
        toolbarRight.SuspendLayout();
        scrollHost.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)picPage).BeginInit();
        statusBar.SuspendLayout();
        SuspendLayout();
        //
        // segViewItem1 / 2 / 3
        //
        segViewItem1.Text = "單頁";
        segViewItem2.Text = "雙頁";
        segViewItem3.Text = "捲動";
        //
        // segZoomItem1 / 2 / 3
        //
        segZoomItem1.Text = "適合頁";
        segZoomItem2.Text = "適合寬";
        segZoomItem3.Text = "100%";
        //
        // windowBar
        //
        // ColorScheme=Dark：PageHeader 內部按「深色風格」渲染（淺色文字、icon 等），
        //    跟 Primary 紫底搭配；Light 風格會強制深色文字看不清
        windowBar.ColorScheme = AntdUI.TAMode.Dark;
        windowBar.Dock = System.Windows.Forms.DockStyle.Top;
        windowBar.Name = "windowBar";
        windowBar.Padding = new System.Windows.Forms.Padding(3);
        windowBar.ShowButton = true;
        windowBar.Size = new System.Drawing.Size(1024, 48);
        windowBar.Text = "Reader";
        windowBar.UseSystemStyleColor = false;
        windowBar.UseTitleFont = true;
        //
        // toolbar
        //
        toolbar.Controls.Add(toolbarLeft);
        toolbar.Controls.Add(toolbarRight);
        toolbar.Dock = System.Windows.Forms.DockStyle.Top;
        toolbar.Name = "toolbar";
        toolbar.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
        toolbar.Size = new System.Drawing.Size(1024, 40);
        //
        // toolbarLeft
        //
        toolbarLeft.AutoSize = true;
        toolbarLeft.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        toolbarLeft.BackColor = System.Drawing.Color.Transparent;
        toolbarLeft.Controls.Add(btnPrevWork);
        toolbarLeft.Controls.Add(btnNextWork);
        toolbarLeft.Controls.Add(btnBookmarkToggle);
        toolbarLeft.Controls.Add(btnBookmarkList);
        toolbarLeft.Controls.Add(btnInstantTranslation);
        toolbarLeft.Controls.Add(btnAllTranslation);
        toolbarLeft.Controls.Add(btnDirection);
        toolbarLeft.Controls.Add(btnSpreadCover);
        toolbarLeft.Controls.Add(lblSeamless);
        toolbarLeft.Controls.Add(switchSeamless);
        toolbarLeft.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        toolbarLeft.Location = new System.Drawing.Point(8, 4);
        toolbarLeft.Margin = new System.Windows.Forms.Padding(0);
        toolbarLeft.Name = "toolbarLeft";
        toolbarLeft.WrapContents = true;
        //
        // toolbarRight
        //
        toolbarRight.AutoSize = true;
        toolbarRight.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        toolbarRight.BackColor = System.Drawing.Color.Transparent;
        toolbarRight.Controls.Add(segViewMode);
        toolbarRight.Controls.Add(segZoomMode);
        toolbarRight.Controls.Add(sliderZoom);
        toolbarRight.Controls.Add(btnFullscreen);
        toolbarRight.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        toolbarRight.Location = new System.Drawing.Point(668, 4);
        toolbarRight.Margin = new System.Windows.Forms.Padding(0);
        toolbarRight.Name = "toolbarRight";
        toolbarRight.WrapContents = false;
        //
        // btnPrevWork — 上一部作品
        //
        btnPrevWork.Enabled = false;
        btnPrevWork.IconSvg = "StepBackwardOutlined";
        btnPrevWork.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
        btnPrevWork.Name = "btnPrevWork";
        btnPrevWork.Size = new System.Drawing.Size(34, 32);
        //
        // btnNextWork — 下一部作品
        //
        btnNextWork.Enabled = false;
        btnNextWork.IconSvg = "StepForwardOutlined";
        btnNextWork.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnNextWork.Name = "btnNextWork";
        btnNextWork.Size = new System.Drawing.Size(34, 32);
        //
        // btnBookmarkToggle — 左側、toggle 當前頁書籤；icon 隨狀態切換
        //
        btnBookmarkToggle.IconSvg = "StarOutlined";
        btnBookmarkToggle.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
        btnBookmarkToggle.Name = "btnBookmarkToggle";
        btnBookmarkToggle.Size = new System.Drawing.Size(34, 32);
        //
        // btnBookmarkList — 左側、開書籤列表
        //
        btnBookmarkList.IconSvg = "UnorderedListOutlined";
        btnBookmarkList.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnBookmarkList.Name = "btnBookmarkList";
        btnBookmarkList.Size = new System.Drawing.Size(34, 32);
        //
        // btnFullscreen
        //
        btnFullscreen.IconSvg = "FullscreenOutlined";
        btnFullscreen.Margin = new System.Windows.Forms.Padding(0);
        btnFullscreen.Name = "btnFullscreen";
        btnFullscreen.Size = new System.Drawing.Size(34, 32);
        //
        // segZoomMode
        //
        segZoomMode.Full = true;
        segZoomMode.Items.Add(segZoomItem1);
        segZoomMode.Items.Add(segZoomItem2);
        segZoomMode.Items.Add(segZoomItem3);
        segZoomMode.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        segZoomMode.Name = "segZoomMode";
        segZoomMode.SelectIndex = 0;
        segZoomMode.Size = new System.Drawing.Size(144, 32);
        //
        // sliderZoom — Continuous 模式才顯示，預設 hidden 與 segZoomMode 共用位置
        //
        sliderZoom.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        sliderZoom.Name = "sliderZoom";
        sliderZoom.ShowValue = true;
        sliderZoom.Size = new System.Drawing.Size(144, 32);
        sliderZoom.Visible = false;
        //
        // switchSeamless — Continuous 模式才顯示
        //
        switchSeamless.Margin = new System.Windows.Forms.Padding(0, 2, 4, 0);
        switchSeamless.Name = "switchSeamless";
        switchSeamless.Size = new System.Drawing.Size(44, 28);
        switchSeamless.Visible = false;
        // 
        // btnInstantTranslation
        // 
        btnInstantTranslation.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
        btnInstantTranslation.Name = "btnInstantTranslation";
        btnInstantTranslation.Size = new System.Drawing.Size(78, 32);
        btnInstantTranslation.TabIndex = 10;
        btnInstantTranslation.Text = "即時翻譯";
        // 
        // btnAllTranslation
        // 
        btnAllTranslation.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnAllTranslation.Name = "btnAllTranslation";
        btnAllTranslation.Size = new System.Drawing.Size(78, 32);
        btnAllTranslation.TabIndex = 11;
        btnAllTranslation.Text = "全部翻譯";
        //
        // btnDirection — 即時切換 RTL / LTR 閱讀方向
        //
        btnDirection.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
        btnDirection.Name = "btnDirection";
        btnDirection.Size = new System.Drawing.Size(64, 32);
        btnDirection.Text = "右→左";
        //
        // btnSpreadCover — 雙頁封面獨立切換
        //
        btnSpreadCover.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnSpreadCover.Name = "btnSpreadCover";
        btnSpreadCover.Size = new System.Drawing.Size(74, 32);
        btnSpreadCover.Text = "封面獨立";
        //
        // lblSeamless
        //
        lblSeamless.AutoSize = false;
        lblSeamless.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F);
        lblSeamless.Margin = new System.Windows.Forms.Padding(2, 4, 2, 0);
        lblSeamless.Name = "lblSeamless";
        lblSeamless.Size = new System.Drawing.Size(52, 24);
        lblSeamless.Text = "無隔間";
        lblSeamless.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        lblSeamless.Visible = false;
        //
        // segViewMode
        //
        segViewMode.Full = true;
        segViewMode.Items.Add(segViewItem1);
        segViewMode.Items.Add(segViewItem2);
        segViewMode.Items.Add(segViewItem3);
        segViewMode.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        segViewMode.Name = "segViewMode";
        segViewMode.SelectIndex = 0;
        segViewMode.Size = new System.Drawing.Size(156, 32);
        //
        // scrollHost
        //
        scrollHost.AutoScroll = true;
        scrollHost.Controls.Add(picPage);
        scrollHost.Controls.Add(vScrollBarContinuous);
        scrollHost.Dock = System.Windows.Forms.DockStyle.Fill;
        scrollHost.Name = "scrollHost";
        //
        // vScrollBarContinuous — 只在連續捲動模式顯示，繞過 Windows 控件 65535 高度限制
        //
        vScrollBarContinuous.Dock = System.Windows.Forms.DockStyle.Right;
        vScrollBarContinuous.LargeChange = 800;
        vScrollBarContinuous.SmallChange = 60;
        vScrollBarContinuous.Visible = false;
        vScrollBarContinuous.Name = "vScrollBarContinuous";
        //
        // picPage
        //
        picPage.Location = new System.Drawing.Point(0, 0);
        picPage.Name = "picPage";
        picPage.Size = new System.Drawing.Size(100, 50);
        picPage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
        picPage.TabStop = false;
        //
        // statusBar
        //
        statusBar.Controls.Add(lblStatus);
        statusBar.Controls.Add(btnStatusLoading);
        statusBar.Dock = System.Windows.Forms.DockStyle.Bottom;
        statusBar.Name = "statusBar";
        statusBar.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        statusBar.Size = new System.Drawing.Size(1024, 24);
        // 
        // btnStatusLoading
        // 
        btnStatusLoading.Dock = System.Windows.Forms.DockStyle.Left;
        btnStatusLoading.Size = new System.Drawing.Size(32, 24);
        btnStatusLoading.Name = "btnStatusLoading";
        btnStatusLoading.Loading = true;
        btnStatusLoading.Visible = false;
        btnStatusLoading.Ghost = true;
        // 
        // lblStatus
        // 
        lblStatus.Dock = System.Windows.Forms.DockStyle.Fill;
        lblStatus.Font = new System.Drawing.Font("Microsoft JhengHei UI", 8.5F);
        lblStatus.Name = "lblStatus";
        lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // ReaderForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(1024, 768);
        Controls.Add(scrollHost);
        Controls.Add(statusBar);
        Controls.Add(toolbar);
        Controls.Add(windowBar);
        KeyPreview = true;
        Name = "ReaderForm";
        Text = "Reader";
        toolbar.ResumeLayout(false);
        toolbar.PerformLayout();
        toolbarLeft.ResumeLayout(false);
        toolbarLeft.PerformLayout();
        toolbarRight.ResumeLayout(false);
        toolbarRight.PerformLayout();
        scrollHost.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)picPage).EndInit();
        statusBar.ResumeLayout(false);
        ResumeLayout(false);
    }
}

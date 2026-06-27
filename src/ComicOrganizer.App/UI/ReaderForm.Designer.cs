namespace ComicOrganizer.App.UI;

partial class ReaderForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private System.Windows.Forms.Panel toolbar;
    private AntdUI.Segmented segViewMode;
    private AntdUI.Segmented segZoomMode;
    private AntdUI.Slider sliderZoom;       // 連續捲動模式時顯示，取代 segZoomMode
    private AntdUI.Switch switchSeamless;   // 連續捲動模式：開啟 = 圖與圖無隔間
    private AntdUI.Label lblSeamless;
    private AntdUI.Button btnInstantTranslation;
    private AntdUI.Button btnAllTranslation;
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
        btnBookmarkToggle = new AntdUI.Button();
        btnBookmarkList = new AntdUI.Button();
        btnFullscreen = new AntdUI.Button();
        segZoomMode = new AntdUI.Segmented();
        sliderZoom = new AntdUI.Slider();
        switchSeamless = new AntdUI.Switch();
        lblSeamless = new AntdUI.Label();
        btnInstantTranslation = new AntdUI.Button();
        btnAllTranslation = new AntdUI.Button();
        segViewMode = new AntdUI.Segmented();
        scrollHost = new System.Windows.Forms.Panel();
        picPage = new System.Windows.Forms.PictureBox();
        vScrollBarContinuous = new System.Windows.Forms.VScrollBar();
        statusBar = new System.Windows.Forms.Panel();
        lblStatus = new System.Windows.Forms.Label();
        btnStatusLoading = new AntdUI.Button();
        toolbar.SuspendLayout();
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
        toolbar.Controls.Add(btnFullscreen);
        toolbar.Controls.Add(btnBookmarkList);
        toolbar.Controls.Add(btnBookmarkToggle);
        toolbar.Controls.Add(switchSeamless);
        toolbar.Controls.Add(lblSeamless);
        toolbar.Controls.Add(btnInstantTranslation);
        toolbar.Controls.Add(btnAllTranslation);
        toolbar.Controls.Add(sliderZoom);
        toolbar.Controls.Add(segZoomMode);
        toolbar.Controls.Add(segViewMode);
        toolbar.Dock = System.Windows.Forms.DockStyle.Top;
        toolbar.Name = "toolbar";
        toolbar.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
        toolbar.Size = new System.Drawing.Size(1024, 40);
        //
        // btnBookmarkToggle — 左側、toggle 當前頁書籤；icon 隨狀態切換
        //
        btnBookmarkToggle.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
        btnBookmarkToggle.IconSvg = "StarOutlined";
        btnBookmarkToggle.Location = new System.Drawing.Point(8, 4);
        btnBookmarkToggle.Name = "btnBookmarkToggle";
        btnBookmarkToggle.Size = new System.Drawing.Size(40, 32);
        //
        // btnBookmarkList — 左側、開書籤列表
        //
        btnBookmarkList.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
        btnBookmarkList.IconSvg = "UnorderedListOutlined";
        btnBookmarkList.Location = new System.Drawing.Point(52, 4);
        btnBookmarkList.Name = "btnBookmarkList";
        btnBookmarkList.Size = new System.Drawing.Size(40, 32);
        //
        // btnFullscreen
        //
        btnFullscreen.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
        btnFullscreen.IconSvg = "FullscreenOutlined";
        btnFullscreen.Location = new System.Drawing.Point(976, 4);
        btnFullscreen.Name = "btnFullscreen";
        btnFullscreen.Size = new System.Drawing.Size(40, 32);
        //
        // segZoomMode
        //
        segZoomMode.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
        segZoomMode.Full = true;
        segZoomMode.Items.Add(segZoomItem1);
        segZoomMode.Items.Add(segZoomItem2);
        segZoomMode.Items.Add(segZoomItem3);
        segZoomMode.Location = new System.Drawing.Point(820, 4);
        segZoomMode.Name = "segZoomMode";
        segZoomMode.SelectIndex = 0;
        segZoomMode.Size = new System.Drawing.Size(150, 32);
        //
        // sliderZoom — Continuous 模式才顯示，預設 hidden 與 segZoomMode 重疊位置
        //
        sliderZoom.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
        sliderZoom.Location = new System.Drawing.Point(820, 4);
        sliderZoom.Name = "sliderZoom";
        sliderZoom.ShowValue = true;
        sliderZoom.Size = new System.Drawing.Size(150, 32);
        sliderZoom.Visible = false;
        //
        // switchSeamless — Continuous 模式才顯示
        //
        switchSeamless.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
        switchSeamless.Location = new System.Drawing.Point(366, 6);
        switchSeamless.Name = "switchSeamless";
        switchSeamless.Size = new System.Drawing.Size(44, 28);
        switchSeamless.Visible = false;
        // 
        // btnInstantTranslation
        // 
        btnInstantTranslation.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
        btnInstantTranslation.Location = new System.Drawing.Point(96, 4);
        btnInstantTranslation.Name = "btnInstantTranslation";
        btnInstantTranslation.Size = new System.Drawing.Size(90, 32);
        btnInstantTranslation.TabIndex = 10;
        btnInstantTranslation.Text = "即時翻譯";
        // 
        // btnAllTranslation
        // 
        btnAllTranslation.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
        btnAllTranslation.Location = new System.Drawing.Point(192, 4);
        btnAllTranslation.Name = "btnAllTranslation";
        btnAllTranslation.Size = new System.Drawing.Size(90, 32);
        btnAllTranslation.TabIndex = 11;
        btnAllTranslation.Text = "全部翻譯";
        //
        // lblSeamless
        //
        lblSeamless.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
        lblSeamless.AutoSize = false;
        lblSeamless.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F);
        lblSeamless.Location = new System.Drawing.Point(298, 8);
        lblSeamless.Name = "lblSeamless";
        lblSeamless.Size = new System.Drawing.Size(64, 24);
        lblSeamless.Text = "無隔間";
        lblSeamless.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        lblSeamless.Visible = false;
        //
        // segViewMode
        //
        segViewMode.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
        segViewMode.Full = true;
        segViewMode.Items.Add(segViewItem1);
        segViewMode.Items.Add(segViewItem2);
        segViewMode.Items.Add(segViewItem3);
        segViewMode.Location = new System.Drawing.Point(634, 4);
        segViewMode.Name = "segViewMode";
        segViewMode.SelectIndex = 0;
        segViewMode.Size = new System.Drawing.Size(180, 32);
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
        scrollHost.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)picPage).EndInit();
        statusBar.ResumeLayout(false);
        ResumeLayout(false);
    }
}

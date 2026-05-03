namespace ComicOrganizer.App.UI;

partial class ReaderForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private System.Windows.Forms.Panel toolbar;
    private AntdUI.Segmented segViewMode;
    private AntdUI.Segmented segZoomMode;
    private AntdUI.Button btnFullscreen;
    private System.Windows.Forms.Panel scrollHost;
    private System.Windows.Forms.PictureBox picPage;
    private System.Windows.Forms.Panel statusBar;
    private System.Windows.Forms.Label lblStatus;

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
        btnFullscreen = new AntdUI.Button();
        segZoomMode = new AntdUI.Segmented();
        segViewMode = new AntdUI.Segmented();
        scrollHost = new System.Windows.Forms.Panel();
        picPage = new System.Windows.Forms.PictureBox();
        statusBar = new System.Windows.Forms.Panel();
        lblStatus = new System.Windows.Forms.Label();
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
        toolbar.Controls.Add(segZoomMode);
        toolbar.Controls.Add(segViewMode);
        toolbar.Dock = System.Windows.Forms.DockStyle.Top;
        toolbar.Name = "toolbar";
        toolbar.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
        toolbar.Size = new System.Drawing.Size(1024, 40);
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
        scrollHost.Dock = System.Windows.Forms.DockStyle.Fill;
        scrollHost.Name = "scrollHost";
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
        statusBar.Dock = System.Windows.Forms.DockStyle.Bottom;
        statusBar.Name = "statusBar";
        statusBar.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
        statusBar.Size = new System.Drawing.Size(1024, 24);
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

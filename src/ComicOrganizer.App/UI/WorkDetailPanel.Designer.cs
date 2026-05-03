namespace ComicOrganizer.App.UI;

partial class WorkDetailPanel
{
    private System.ComponentModel.IContainer components = null;

    // Header
    private System.Windows.Forms.Panel headerPanel;
    private System.Windows.Forms.Label lblTitle;
    private AntdUI.Button btnClose;

    // 內容
    private System.Windows.Forms.Panel contentPanel;
    private System.Windows.Forms.TableLayoutPanel layout;

    // Cover + 主名
    private System.Windows.Forms.Panel coverPanel;
    private System.Windows.Forms.PictureBox picCover;
    private System.Windows.Forms.Label lblWorkTitle;
    private System.Windows.Forms.LinkLabel lnkAuthor;

    // metadata grid
    private AntdUI.Divider divider1;
    private System.Windows.Forms.Label lblMetaInfo;

    // 閱讀記錄
    private AntdUI.Divider divider2;
    private System.Windows.Forms.Label lblReadStatsHeader;
    private System.Windows.Forms.Label lblReadStats;

    // tags
    private AntdUI.Divider divider3;
    private System.Windows.Forms.Label lblTagsHeader;
    private System.Windows.Forms.FlowLayoutPanel flowTags;

    // 檔案資訊
    private AntdUI.Divider divider4;
    private System.Windows.Forms.Label lblFileInfoHeader;
    private System.Windows.Forms.Label lblFileInfo;

    // 操作
    private AntdUI.Divider divider5;
    private System.Windows.Forms.FlowLayoutPanel flowActions;
    private AntdUI.Button btnRead;
    private AntdUI.Button btnExplorer;
    private AntdUI.Button btnResetReading;

    // Empty
    private System.Windows.Forms.Label lblEmpty;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        headerPanel = new System.Windows.Forms.Panel();
        lblTitle = new System.Windows.Forms.Label();
        btnClose = new AntdUI.Button();
        contentPanel = new System.Windows.Forms.Panel();
        layout = new System.Windows.Forms.TableLayoutPanel();
        coverPanel = new System.Windows.Forms.Panel();
        picCover = new System.Windows.Forms.PictureBox();
        lblWorkTitle = new System.Windows.Forms.Label();
        lnkAuthor = new System.Windows.Forms.LinkLabel();
        divider1 = new AntdUI.Divider();
        lblMetaInfo = new System.Windows.Forms.Label();
        divider2 = new AntdUI.Divider();
        lblReadStatsHeader = new System.Windows.Forms.Label();
        lblReadStats = new System.Windows.Forms.Label();
        divider3 = new AntdUI.Divider();
        lblTagsHeader = new System.Windows.Forms.Label();
        flowTags = new System.Windows.Forms.FlowLayoutPanel();
        divider4 = new AntdUI.Divider();
        lblFileInfoHeader = new System.Windows.Forms.Label();
        lblFileInfo = new System.Windows.Forms.Label();
        divider5 = new AntdUI.Divider();
        flowActions = new System.Windows.Forms.FlowLayoutPanel();
        btnRead = new AntdUI.Button();
        btnExplorer = new AntdUI.Button();
        btnResetReading = new AntdUI.Button();
        lblEmpty = new System.Windows.Forms.Label();

        SuspendLayout();
        headerPanel.SuspendLayout();
        contentPanel.SuspendLayout();
        layout.SuspendLayout();
        coverPanel.SuspendLayout();

        //
        // headerPanel
        //
        headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
        headerPanel.Height = 36;
        headerPanel.Padding = new System.Windows.Forms.Padding(8, 4, 4, 4);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(btnClose);
        //
        // lblTitle
        //
        lblTitle.Dock = System.Windows.Forms.DockStyle.Fill;
        lblTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 11F, System.Drawing.FontStyle.Bold);
        lblTitle.Name = "lblTitle";
        lblTitle.Text = "作品詳細資訊";
        lblTitle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // btnClose
        //
        btnClose.Dock = System.Windows.Forms.DockStyle.Right;
        btnClose.Ghost = true;
        btnClose.IconRatio = 0.6F;
        btnClose.IconSvg = "CloseOutlined";
        btnClose.Name = "btnClose";
        btnClose.Size = new System.Drawing.Size(28, 28);
        btnClose.Text = "";

        //
        // contentPanel
        //
        contentPanel.AutoScroll = true;
        contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        contentPanel.Padding = new System.Windows.Forms.Padding(12, 8, 12, 8);
        contentPanel.Controls.Add(layout);

        //
        // layout
        //
        layout.AutoSize = true;
        layout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        layout.Dock = System.Windows.Forms.DockStyle.Top;
        for (int i = 0; i < 16; i++) layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        layout.Controls.Add(coverPanel, 0, 0);
        layout.Controls.Add(lblWorkTitle, 0, 1);
        layout.Controls.Add(lnkAuthor, 0, 2);
        layout.Controls.Add(divider1, 0, 3);
        layout.Controls.Add(lblMetaInfo, 0, 4);
        layout.Controls.Add(divider2, 0, 5);
        layout.Controls.Add(lblReadStatsHeader, 0, 6);
        layout.Controls.Add(lblReadStats, 0, 7);
        layout.Controls.Add(divider3, 0, 8);
        layout.Controls.Add(lblTagsHeader, 0, 9);
        layout.Controls.Add(flowTags, 0, 10);
        layout.Controls.Add(divider4, 0, 11);
        layout.Controls.Add(lblFileInfoHeader, 0, 12);
        layout.Controls.Add(lblFileInfo, 0, 13);
        layout.Controls.Add(divider5, 0, 14);
        layout.Controls.Add(flowActions, 0, 15);

        //
        // coverPanel - 容納 picCover 並置中
        //
        coverPanel.AutoSize = true;
        coverPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        coverPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        coverPanel.Controls.Add(picCover);
        //
        // picCover - 256x360 大封面，居中對齊
        //
        picCover.Anchor = System.Windows.Forms.AnchorStyles.Top;
        picCover.Size = new System.Drawing.Size(256, 360);
        picCover.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
        picCover.Cursor = System.Windows.Forms.Cursors.Hand;

        //
        // lblWorkTitle
        //
        lblWorkTitle.AutoSize = true;
        lblWorkTitle.MaximumSize = new System.Drawing.Size(340, 0);
        lblWorkTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 12F, System.Drawing.FontStyle.Bold);
        lblWorkTitle.Margin = new System.Windows.Forms.Padding(0, 0, 0, 2);
        lblWorkTitle.Name = "lblWorkTitle";
        lblWorkTitle.Text = "(Title)";
        //
        // lnkAuthor
        //
        lnkAuthor.AutoSize = true;
        lnkAuthor.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lnkAuthor.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lnkAuthor.Name = "lnkAuthor";
        lnkAuthor.Text = "";

        //
        // dividers
        //
        divider1.Dock = System.Windows.Forms.DockStyle.Fill;
        divider1.Height = 8;
        divider1.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider1.Name = "divider1";
        divider2.Dock = System.Windows.Forms.DockStyle.Fill;
        divider2.Height = 8;
        divider2.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider2.Name = "divider2";
        divider3.Dock = System.Windows.Forms.DockStyle.Fill;
        divider3.Height = 8;
        divider3.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider3.Name = "divider3";
        divider4.Dock = System.Windows.Forms.DockStyle.Fill;
        divider4.Height = 8;
        divider4.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider4.Name = "divider4";
        divider5.Dock = System.Windows.Forms.DockStyle.Fill;
        divider5.Height = 8;
        divider5.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
        divider5.Name = "divider5";

        //
        // lblMetaInfo - 多行 metadata
        //
        lblMetaInfo.AutoSize = true;
        lblMetaInfo.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lblMetaInfo.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblMetaInfo.Name = "lblMetaInfo";
        lblMetaInfo.Text = "";

        //
        // lblReadStatsHeader
        //
        lblReadStatsHeader.AutoSize = true;
        lblReadStatsHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblReadStatsHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblReadStatsHeader.Name = "lblReadStatsHeader";
        lblReadStatsHeader.Text = "閱讀記錄";
        //
        // lblReadStats
        //
        lblReadStats.AutoSize = true;
        lblReadStats.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lblReadStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblReadStats.Name = "lblReadStats";
        lblReadStats.Text = "";

        //
        // lblTagsHeader
        //
        lblTagsHeader.AutoSize = true;
        lblTagsHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblTagsHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblTagsHeader.Name = "lblTagsHeader";
        lblTagsHeader.Text = "標籤";
        //
        // flowTags
        //
        flowTags.AutoSize = true;
        flowTags.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowTags.Dock = System.Windows.Forms.DockStyle.Fill;
        flowTags.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowTags.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        flowTags.Name = "flowTags";
        flowTags.WrapContents = true;

        //
        // lblFileInfoHeader
        //
        lblFileInfoHeader.AutoSize = true;
        lblFileInfoHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblFileInfoHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblFileInfoHeader.Name = "lblFileInfoHeader";
        lblFileInfoHeader.Text = "檔案資訊";
        //
        // lblFileInfo
        //
        lblFileInfo.AutoSize = true;
        lblFileInfo.MaximumSize = new System.Drawing.Size(340, 0);
        lblFileInfo.Font = new System.Drawing.Font("Microsoft JhengHei UI", 8.5F);
        lblFileInfo.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblFileInfo.Name = "lblFileInfo";
        lblFileInfo.Text = "";

        //
        // flowActions
        //
        flowActions.AutoSize = true;
        flowActions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowActions.Dock = System.Windows.Forms.DockStyle.Fill;
        flowActions.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowActions.Margin = new System.Windows.Forms.Padding(0);
        flowActions.Name = "flowActions";
        flowActions.WrapContents = true;
        flowActions.Controls.Add(btnRead);
        flowActions.Controls.Add(btnExplorer);
        flowActions.Controls.Add(btnResetReading);
        //
        // btnRead
        //
        btnRead.IconSvg = "PlayCircleOutlined";
        btnRead.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnRead.Name = "btnRead";
        btnRead.Size = new System.Drawing.Size(96, 30);
        btnRead.Text = "閱讀";
        btnRead.Type = AntdUI.TTypeMini.Primary;
        //
        // btnExplorer
        //
        btnExplorer.IconSvg = "FolderOpenOutlined";
        btnExplorer.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnExplorer.Name = "btnExplorer";
        btnExplorer.Size = new System.Drawing.Size(96, 30);
        btnExplorer.Text = "開啟位置";
        //
        // btnResetReading
        //
        btnResetReading.IconSvg = "UndoOutlined";
        btnResetReading.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
        btnResetReading.Name = "btnResetReading";
        btnResetReading.Size = new System.Drawing.Size(120, 30);
        btnResetReading.Text = "重置閱讀記錄";
        btnResetReading.Type = AntdUI.TTypeMini.Default;

        //
        // lblEmpty
        //
        lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
        lblEmpty.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F);
        lblEmpty.Name = "lblEmpty";
        lblEmpty.Text = "";
        lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        lblEmpty.Visible = false;

        //
        // WorkDetailPanel
        //
        Controls.Add(contentPanel);
        Controls.Add(headerPanel);
        Controls.Add(lblEmpty);
        Name = "WorkDetailPanel";
        Size = new System.Drawing.Size(380, 600);

        coverPanel.ResumeLayout(false);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        headerPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}

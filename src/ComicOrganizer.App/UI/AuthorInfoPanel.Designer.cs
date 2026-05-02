namespace ComicOrganizer.App.UI;

partial class AuthorInfoPanel
{
    private System.ComponentModel.IContainer components = null;

    // Header
    private System.Windows.Forms.Panel headerPanel;
    private System.Windows.Forms.Label lblTitle;
    private AntdUI.Button btnClose;

    // 資料區（ShowEmpty 時隱藏）
    private System.Windows.Forms.Panel contentPanel;
    private System.Windows.Forms.TableLayoutPanel layout;
    private System.Windows.Forms.Label lblMainName;
    private System.Windows.Forms.Label lblAliases;
    private AntdUI.Divider divider1;
    private System.Windows.Forms.Label lblStats;
    private AntdUI.Divider divider2;
    private System.Windows.Forms.Label lblTagsHeader;
    private System.Windows.Forms.FlowLayoutPanel flowTags;
    private AntdUI.Divider divider3;
    private System.Windows.Forms.Label lblRecentHeader;
    private System.Windows.Forms.FlowLayoutPanel flowRecent;

    // ShowEmpty 訊息
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
        lblMainName = new System.Windows.Forms.Label();
        lblAliases = new System.Windows.Forms.Label();
        divider1 = new AntdUI.Divider();
        lblStats = new System.Windows.Forms.Label();
        divider2 = new AntdUI.Divider();
        lblTagsHeader = new System.Windows.Forms.Label();
        flowTags = new System.Windows.Forms.FlowLayoutPanel();
        divider3 = new AntdUI.Divider();
        lblRecentHeader = new System.Windows.Forms.Label();
        flowRecent = new System.Windows.Forms.FlowLayoutPanel();
        lblEmpty = new System.Windows.Forms.Label();

        SuspendLayout();
        headerPanel.SuspendLayout();
        contentPanel.SuspendLayout();
        layout.SuspendLayout();

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
        lblTitle.Text = "作者資訊";
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
        // layout (TableLayoutPanel — 每個 section 一行 AutoSize)
        //
        layout.AutoSize = true;
        layout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        layout.Dock = System.Windows.Forms.DockStyle.Top;
        // 9 行：MainName, Aliases, divider1, Stats, divider2, TagsHeader, flowTags, divider3, RecentHeader, flowRecent
        for (int i = 0; i < 10; i++) layout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        layout.Controls.Add(lblMainName, 0, 0);
        layout.Controls.Add(lblAliases, 0, 1);
        layout.Controls.Add(divider1, 0, 2);
        layout.Controls.Add(lblStats, 0, 3);
        layout.Controls.Add(divider2, 0, 4);
        layout.Controls.Add(lblTagsHeader, 0, 5);
        layout.Controls.Add(flowTags, 0, 6);
        layout.Controls.Add(divider3, 0, 7);
        layout.Controls.Add(lblRecentHeader, 0, 8);
        layout.Controls.Add(flowRecent, 0, 9);

        //
        // lblMainName
        //
        lblMainName.AutoSize = true;
        lblMainName.Font = new System.Drawing.Font("Microsoft JhengHei UI", 14F, System.Drawing.FontStyle.Bold);
        lblMainName.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblMainName.Name = "lblMainName";
        lblMainName.Text = "(Author)";
        //
        // lblAliases
        //
        lblAliases.AutoSize = true;
        lblAliases.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9F);
        lblAliases.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        lblAliases.Name = "lblAliases";
        lblAliases.Text = "";

        //
        // divider1 / divider2 / divider3
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

        //
        // lblStats
        //
        lblStats.AutoSize = true;
        lblStats.Font = new System.Drawing.Font("Microsoft JhengHei UI", 9.5F);
        lblStats.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblStats.Name = "lblStats";
        lblStats.Text = "";

        //
        // lblTagsHeader
        //
        lblTagsHeader.AutoSize = true;
        lblTagsHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblTagsHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblTagsHeader.Name = "lblTagsHeader";
        lblTagsHeader.Text = "主要標籤";
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
        // lblRecentHeader
        //
        lblRecentHeader.AutoSize = true;
        lblRecentHeader.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F, System.Drawing.FontStyle.Bold);
        lblRecentHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
        lblRecentHeader.Name = "lblRecentHeader";
        lblRecentHeader.Text = "最近閱讀";
        //
        // flowRecent
        //
        flowRecent.AutoSize = true;
        flowRecent.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowRecent.Dock = System.Windows.Forms.DockStyle.Fill;
        flowRecent.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
        flowRecent.Margin = new System.Windows.Forms.Padding(0);
        flowRecent.Name = "flowRecent";
        flowRecent.WrapContents = false;

        //
        // lblEmpty (ShowEmpty 時顯示)
        //
        lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
        lblEmpty.Font = new System.Drawing.Font("Microsoft JhengHei UI", 10F);
        lblEmpty.Name = "lblEmpty";
        lblEmpty.Text = "";
        lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        lblEmpty.Visible = false;

        //
        // AuthorInfoPanel
        //
        Controls.Add(contentPanel);   // Dock.Fill 後 add → 取剩餘
        Controls.Add(headerPanel);    // Dock.Top 先 add
        Controls.Add(lblEmpty);       // 最上層，預設隱藏；ShowEmpty 時 BringToFront
        Name = "AuthorInfoPanel";
        Size = new System.Drawing.Size(280, 600);

        layout.ResumeLayout(false);
        layout.PerformLayout();
        contentPanel.ResumeLayout(false);
        contentPanel.PerformLayout();
        headerPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}

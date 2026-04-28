namespace ComicOrganize.App.UI;

partial class GalleryControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Splitter splitter2;
    private AntdUI.Splitter splitter1;
    private AntdUI.Tree treeFolders;
    private AntdUI.Breadcrumb breadcrumb;
    private AntdUI.Button btnViewMode;
    private AntdUI.Button btnRefresh;
    private AntdUI.Tabs tabsContent;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        AntdUI.Tabs.StyleCard styleCard1 = new AntdUI.Tabs.StyleCard();
        splitter2 = new AntdUI.Splitter();
        treeFolders = new AntdUI.Tree();
        splitter1 = new AntdUI.Splitter();
        breadcrumb = new AntdUI.Breadcrumb();
        btnViewMode = new AntdUI.Button();
        btnRefresh = new AntdUI.Button();
        tabsContent = new AntdUI.Tabs();
        ((System.ComponentModel.ISupportInitialize)splitter2).BeginInit();
        splitter2.Panel1.SuspendLayout();
        splitter2.Panel2.SuspendLayout();
        splitter2.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitter1).BeginInit();
        splitter1.Panel1.SuspendLayout();
        splitter1.Panel2.SuspendLayout();
        splitter1.SuspendLayout();
        SuspendLayout();
        //
        // splitter2
        //
        splitter2.CollapsePanel = AntdUI.Splitter.ADCollapsePanel.Panel1;
        splitter2.Dock = DockStyle.Fill;
        splitter2.Location = new Point(0, 0);
        splitter2.Name = "splitter2";
        splitter2.Panel1.Controls.Add(treeFolders);
        splitter2.Panel1MinSize = 5;
        splitter2.Panel2.Controls.Add(splitter1);
        splitter2.Size = new Size(1280, 626);
        splitter2.SplitterDistance = 309;
        splitter2.SplitterSize = 80;
        splitter2.SplitterWidth = 10;
        splitter2.TabIndex = 0;
        //
        // treeFolders
        //
        treeFolders.BackColor = Color.FromArgb(255, 255, 192);
        treeFolders.BackgroundImageLayout = ImageLayout.None;
        treeFolders.Dock = DockStyle.Fill;
        treeFolders.EmptyText = "no data";
        treeFolders.Location = new Point(0, 0);
        treeFolders.Name = "treeFolders";
        treeFolders.Round = true;
        treeFolders.Size = new Size(309, 626);
        treeFolders.TabIndex = 0;
        treeFolders.Text = "tree1";
        treeFolders.BeforeExpand += treeFolders_BeforeExpand;
        treeFolders.NodeMouseDoubleClick += treeFolders_NodeMouseDoubleClick;
        //
        // splitter1
        //
        splitter1.Dock = DockStyle.Fill;
        splitter1.Location = new Point(0, 0);
        splitter1.Margin = new Padding(0);
        splitter1.Name = "splitter1";
        splitter1.Orientation = Orientation.Horizontal;
        splitter1.Panel1.BackColor = Color.WhiteSmoke;
        splitter1.Panel1.Controls.Add(breadcrumb);
        splitter1.Panel1.Controls.Add(btnViewMode);
        splitter1.Panel1.Controls.Add(btnRefresh);
        splitter1.Panel1.Padding = new Padding(3);
        splitter1.Panel1MinSize = 50;
        splitter1.Panel2.BackColor = Color.WhiteSmoke;
        splitter1.Panel2.Controls.Add(tabsContent);
        splitter1.Panel2.Padding = new Padding(0, 10, 0, 5);
        splitter1.Size = new Size(961, 626);
        splitter1.SplitterDistance = 69;
        splitter1.SplitterSize = 1;
        splitter1.SplitterWidth = 1;
        splitter1.TabIndex = 0;
        //
        // breadcrumb
        //
        breadcrumb.BackColor = Color.Transparent;
        breadcrumb.Dock = DockStyle.Left;
        breadcrumb.Location = new Point(3, 3);
        breadcrumb.Name = "breadcrumb";
        breadcrumb.Padding = new Padding(12, 0, 12, 0);
        breadcrumb.Size = new Size(527, 63);
        breadcrumb.TabIndex = 0;
        //
        // btnViewMode
        //
        btnViewMode.Dock = DockStyle.Right;
        btnViewMode.IconSvg = "AppstoreOutlined";
        btnViewMode.Name = "btnViewMode";
        btnViewMode.Size = new Size(46, 57);
        btnViewMode.TabIndex = 1;
        btnViewMode.Type = AntdUI.TTypeMini.Default;
        //
        // btnRefresh
        //
        btnRefresh.Dock = DockStyle.Right;
        btnRefresh.IconSvg = "RedoOutlined";
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new Size(46, 57);
        btnRefresh.TabIndex = 2;
        btnRefresh.Type = AntdUI.TTypeMini.Default;
        //
        // tabsContent
        //
        tabsContent.CloseDisposePage = true;
        tabsContent.Dock = DockStyle.Fill;
        tabsContent.Location = new Point(0, 10);
        tabsContent.Name = "tabsContent";
        tabsContent.Size = new Size(961, 541);
        tabsContent.Style = styleCard1;
        tabsContent.TabIndex = 0;
        tabsContent.Type = AntdUI.TabType.Card;
        //
        // GalleryControl
        //
        Controls.Add(splitter2);
        Name = "GalleryControl";
        Size = new Size(1280, 626);
        splitter2.Panel1.ResumeLayout(false);
        splitter2.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitter2).EndInit();
        splitter2.ResumeLayout(false);
        splitter1.Panel1.ResumeLayout(false);
        splitter1.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitter1).EndInit();
        splitter1.ResumeLayout(false);
        ResumeLayout(false);
    }
}

using AntdUI;

namespace ComicOrganize.App.UI;

partial class GalleryControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Splitter splitter2;
    private AntdUI.Splitter splitter1;
    private AntdUI.Tree treeFolders;
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
        Tabs.StyleCard styleCard1 = new Tabs.StyleCard();
        splitter2 = new AntdUI.Splitter();
        treeFolders = new Tree();
        splitter1 = new AntdUI.Splitter();
        btnViewMode = new AntdUI.Button();
        btnRefresh = new AntdUI.Button();
        tabsContent = new Tabs();
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
        // 
        // splitter2.Panel1
        // 
        splitter2.Panel1.BackColor = Color.FromArgb(192, 181, 207);
        splitter2.Panel1.Controls.Add(treeFolders);
        splitter2.Panel1.Padding = new Padding(1);
        splitter2.Panel1MinSize = 5;
        // 
        // splitter2.Panel2
        // 
        splitter2.Panel2.BackColor = Color.Transparent;
        splitter2.Panel2.Controls.Add(splitter1);
        splitter2.Panel2.Padding = new Padding(1);
        splitter2.Size = new Size(1047, 700);
        splitter2.SplitterDistance = 271;
        splitter2.SplitterSize = 80;
        splitter2.SplitterWidth = 10;
        splitter2.TabIndex = 0;
        // 
        // treeFolders
        // 
        treeFolders.BackColor = Color.FromArgb(241, 233, 249);
        treeFolders.BackgroundImageLayout = ImageLayout.None;
        treeFolders.Dock = DockStyle.Fill;
        treeFolders.EmptyText = "no data";
        treeFolders.Location = new Point(1, 1);
        treeFolders.Name = "treeFolders";
        treeFolders.Round = true;
        treeFolders.Size = new Size(269, 698);
        treeFolders.TabIndex = 0;
        treeFolders.Text = "tree1";
        treeFolders.BeforeExpand += treeFolders_BeforeExpand;
        treeFolders.NodeMouseDoubleClick += treeFolders_NodeMouseDoubleClick;
        // 
        // splitter1
        // 
        splitter1.Dock = DockStyle.Fill;
        splitter1.Location = new Point(1, 1);
        splitter1.Margin = new Padding(0);
        splitter1.Name = "splitter1";
        splitter1.Orientation = Orientation.Horizontal;
        // 
        // splitter1.Panel1
        // 
        splitter1.Panel1.Controls.Add(btnViewMode);
        splitter1.Panel1.Controls.Add(btnRefresh);
        splitter1.Panel1.Padding = new Padding(8, 4, 8, 4);
        splitter1.Panel1MinSize = 30;
        // 
        // splitter1.Panel2
        // 
        splitter1.Panel2.Controls.Add(tabsContent);
        splitter1.Panel2.Padding = new Padding(0, 10, 0, 5);
        splitter1.Size = new Size(764, 698);
        splitter1.SplitterBack = Color.FromArgb(212, 211, 211);
        splitter1.SplitterDistance = 49;
        splitter1.SplitterSize = 1;
        splitter1.SplitterWidth = 1;
        splitter1.TabIndex = 0;
        // 
        // btnViewMode
        // 
        btnViewMode.BackHover = Color.FromArgb(131, 117, 159);
        btnViewMode.Dock = DockStyle.Right;
        btnViewMode.Ghost = true;
        btnViewMode.IconRatio = 1F;
        btnViewMode.IconSvg = "AppstoreOutlined";
        btnViewMode.Location = new Point(676, 4);
        btnViewMode.Name = "btnViewMode";
        btnViewMode.Size = new Size(40, 41);
        btnViewMode.TabIndex = 1;
        // 
        // btnRefresh
        // 
        btnRefresh.BackHover = Color.FromArgb(131, 117, 159);
        btnRefresh.Dock = DockStyle.Right;
        btnRefresh.Ghost = true;
        btnRefresh.IconRatio = 1F;
        btnRefresh.IconSvg = "RedoOutlined";
        btnRefresh.Location = new Point(716, 4);
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new Size(40, 41);
        btnRefresh.TabIndex = 2;
        // 
        // tabsContent
        // 
        tabsContent.CloseDisposePage = true;
        tabsContent.Dock = DockStyle.Fill;
        tabsContent.Font = new Font("Microsoft JhengHei UI", 12F);
        tabsContent.Location = new Point(0, 10);
        tabsContent.Name = "tabsContent";
        tabsContent.ScrollBack = Color.FromArgb(102, 69, 146);
        tabsContent.Size = new Size(764, 633);
        tabsContent.Style = styleCard1;
        tabsContent.TabIndex = 0;
        tabsContent.Type = TabType.Card;
        // 
        // GalleryControl
        // 
        Controls.Add(splitter2);
        Name = "GalleryControl";
        Size = new Size(1047, 700);
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

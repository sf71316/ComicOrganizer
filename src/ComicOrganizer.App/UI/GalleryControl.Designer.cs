using AntdUI;

namespace ComicOrganizer.App.UI;

partial class GalleryControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Splitter splitter2;
    private AntdUI.Segmented segViewMode;
    private System.Windows.Forms.FlowLayoutPanel flowChipBar;   // Tag mode 多選 chip 區（其他 mode 隱藏）
    private AntdUI.Tree treeFolders;
    private AntdUI.Tabs tabsContent;
    private DetailDrawerHost detailDrawerHost;

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
        segViewMode = new AntdUI.Segmented();
        flowChipBar = new System.Windows.Forms.FlowLayoutPanel();
        treeFolders = new Tree();
        tabsContent = new Tabs();
        detailDrawerHost = new DetailDrawerHost();
        ((System.ComponentModel.ISupportInitialize)splitter2).BeginInit();
        splitter2.Panel1.SuspendLayout();
        splitter2.Panel2.SuspendLayout();
        splitter2.SuspendLayout();
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
        splitter2.Panel1.BackColor = Core.ThemeColors.Border;
        // 順序：treeFolders Fill 最先 add，flowChipBar Top 之後（顯示在 tree 上方），segViewMode Top 最後（在最頂）
        splitter2.Panel1.Controls.Add(treeFolders);
        splitter2.Panel1.Controls.Add(flowChipBar);
        splitter2.Panel1.Controls.Add(segViewMode);
        splitter2.Panel1.Padding = new Padding(1);
        splitter2.Panel1MinSize = 5;
        //
        // splitter2.Panel2
        //
        splitter2.Panel2.BackColor = Core.ThemeColors.Border;
        splitter2.Panel2.Controls.Add(tabsContent);
        // detailDrawerHost 不加進 splitter2.Panel2 — 用 AntdUI.Drawer.open 動態彈出，
        // Drawer 自己 host 在獨立 Form 內、有 Mask 半透明遮罩
        splitter2.Panel2.Padding = new Padding(1);
        splitter2.Size = new Size(1047, 700);
        splitter2.SplitterDistance = 271;
        splitter2.SplitterSize = 80;
        splitter2.SplitterWidth = 10;
        splitter2.TabIndex = 0;
        // 
        // treeFolders
        // 
        treeFolders.BackColor = Core.ThemeColors.TreeViewSurface;
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
        // segViewMode — sidebar 上方 ViewMode 切換（#48 Phase 1）
        //
        segViewMode.Dock = DockStyle.Top;
        segViewMode.Font = new Font("Microsoft JhengHei UI", 9F);
        segViewMode.Height = 32;
        segViewMode.Margin = new Padding(0);
        segViewMode.Name = "segViewMode";
        segViewMode.TabIndex = 1;
        // Items 由 GalleryControl.cs WireViewMode 動態填（要 i18n + ENUM 對應）
        //
        // flowChipBar — Tag mode 多選 chip 區（預設 Visible=false，只在 ByTag mode 顯示）
        //
        flowChipBar.AutoSize = true;
        flowChipBar.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
        flowChipBar.Dock = DockStyle.Top;
        flowChipBar.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        flowChipBar.WrapContents = true;
        flowChipBar.Padding = new Padding(4, 4, 4, 4);
        flowChipBar.Name = "flowChipBar";
        flowChipBar.Visible = false;
        //
        // detailDrawerHost — 不掛在 splitter2.Panel2 內，由 AntdUI.Drawer 動態彈出
        // 內含 AuthorInfoPanel + WorkDetailPanel，互斥顯示
        //
        detailDrawerHost.Width = 380;        // Drawer 寬度
        detailDrawerHost.Visible = true;
        detailDrawerHost.Name = "detailDrawerHost";
        //
        // tabsContent
        //
        tabsContent.CloseDisposePage = true;
        tabsContent.Dock = DockStyle.Fill;
        tabsContent.Font = new Font("Microsoft JhengHei UI", 12F);
        tabsContent.Location = new Point(0, 10);
        tabsContent.Name = "tabsContent";
        tabsContent.ScrollBack = Core.ThemeColors.Text;
        tabsContent.Size = new Size(766, 685);
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
        ResumeLayout(false);
    }
}

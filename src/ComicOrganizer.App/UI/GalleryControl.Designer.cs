using AntdUI;

namespace ComicOrganizer.App.UI;

partial class GalleryControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Splitter splitter2;
    private AntdUI.Tree treeFolders;
    private AntdUI.Tabs tabsContent;
    private AuthorInfoPanel authorInfoPanel;

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
        tabsContent = new Tabs();
        authorInfoPanel = new AuthorInfoPanel();
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
        splitter2.Panel1.Controls.Add(treeFolders);
        splitter2.Panel1.Padding = new Padding(1);
        splitter2.Panel1MinSize = 5;
        //
        // splitter2.Panel2
        //
        splitter2.Panel2.BackColor = Core.ThemeColors.Border;
        // tabsContent 先 add（Dock.Fill 占滿）+ authorInfoPanel 後 add（Anchor 右側浮動）
        // panel 有 BringToFront → 蓋在 tabsContent 之上，半透明背景可看到後面卡片
        splitter2.Panel2.Controls.Add(tabsContent);
        splitter2.Panel2.Controls.Add(authorInfoPanel);
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
        // authorInfoPanel — 浮動覆蓋（不是 Dock.Right，那會占位無法蓋住 tabsContent）
        //
        authorInfoPanel.Dock = DockStyle.None;
        authorInfoPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        authorInfoPanel.Width = 280;
        authorInfoPanel.Visible = false;
        authorInfoPanel.Name = "authorInfoPanel";
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

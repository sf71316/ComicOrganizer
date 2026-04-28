using AntdUI;
using FolderBrowserDialog = AntdUI.FolderBrowserDialog;

namespace ComicOrganize.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private AntdUI.PageHeader windowBar; 
    private AntdUI.Tabs tabs;
    private AntdUI.TabPage tabPage1;
    private AntdUI.TabPage tabPage2;
    private AntdUI.TabPage tabPage3;

    private AntdUI.Tree treeFolders;
    private AntdUI.Menu menuToolbar;
    private AntdUI.Breadcrumb breadcrumb;
    private AntdUI.Tabs tabsContent;
    
    private AntdUI.Table tableArchive;
    private AntdUI.Button btnScan;
    private AntdUI.Button btnExecute;
    private AntdUI.Label lblStatus;

    private AntdUI.Input txtSource;
    private AntdUI.Input txtTarget;
    private AntdUI.Input txtImageViewer;
    private AntdUI.Button btnBrowseSource;
    private AntdUI.Button btnBrowseTarget;
    private AntdUI.Button btnBrowseViewer;
    private AntdUI.Button btnSaveConfig;
    private AntdUI.Checkbox cbDebug;
    private AntdUI.Label label1;
    private AntdUI.Label label2;
    private AntdUI.Label label3;

    private AntdUI.FolderBrowserDialog folderBrowserDialog;

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
        folderBrowserDialog = new FolderBrowserDialog();
        Tabs.StyleCard styleCard2 = new Tabs.StyleCard();
        MenuItem menuItem1 = new MenuItem();
        MenuItem menuItem2 = new MenuItem();
        MenuItem menuItem3 = new MenuItem();
        MenuItem menuItem4 = new MenuItem();
        MenuItem menuItem5 = new MenuItem();
        MenuItem menuItem6 = new MenuItem();
        Tabs.StyleCard styleCard1 = new Tabs.StyleCard();
        windowBar = new PageHeader();
        tabs = new Tabs();
        tabPage1 = new AntdUI.TabPage();
        splitter2 = new AntdUI.Splitter();
        treeFolders = new Tree();
        splitter1 = new AntdUI.Splitter();
        breadcrumb = new Breadcrumb();
        menuToolbar = new Menu();
        tabsContent = new Tabs();
        tabPage2 = new AntdUI.TabPage();
        lblStatus = new AntdUI.Label();
        btnExecute = new AntdUI.Button();
        tableArchive = new Table();
        btnScan = new AntdUI.Button();
        tabPage3 = new AntdUI.TabPage();
        label3 = new AntdUI.Label();
        label2 = new AntdUI.Label();
        label1 = new AntdUI.Label();
        cbDebug = new Checkbox();
        btnSaveConfig = new AntdUI.Button();
        btnBrowseViewer = new AntdUI.Button();
        btnBrowseTarget = new AntdUI.Button();
        btnBrowseSource = new AntdUI.Button();
        txtImageViewer = new Input();
        txtTarget = new Input();
        txtSource = new Input();
        tabs.SuspendLayout();
        tabPage1.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitter2).BeginInit();
        splitter2.Panel1.SuspendLayout();
        splitter2.Panel2.SuspendLayout();
        splitter2.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitter1).BeginInit();
        splitter1.Panel1.SuspendLayout();
        splitter1.Panel2.SuspendLayout();
        splitter1.SuspendLayout();
        tabPage2.SuspendLayout();
        tabPage3.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.BackColor = Color.FromArgb(192, 192, 255);
        windowBar.Dock = DockStyle.Top;
        windowBar.IconSvg = "BookOutlined";
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.ShowButton = true;
        windowBar.ShowIcon = true;
        windowBar.Size = new Size(1280, 48);
        windowBar.TabIndex = 1;
        windowBar.Text = "Comic Organize";
        // 
        // tabs
        // 
        tabs.Controls.Add(tabPage1);
        tabs.Controls.Add(tabPage2);
        tabs.Controls.Add(tabPage3);
        tabs.Dock = DockStyle.Fill;
        tabs.Font = new Font("Microsoft JhengHei UI", 12F);
        tabs.Location = new Point(0, 48);
        tabs.Name = "tabs";
        tabs.Pages.Add(tabPage1);
        tabs.Pages.Add(tabPage2);
        tabs.Pages.Add(tabPage3);
        tabs.Size = new Size(1280, 672);
        tabs.Style = styleCard2;
        tabs.TabIndex = 0;
        tabs.Type = TabType.Card;
        // 
        // tabPage1
        // 
        tabPage1.Controls.Add(splitter2);
        tabPage1.Location = new Point(0, 46);
        tabPage1.Name = "tabPage1";
        tabPage1.Size = new Size(1280, 626);
        tabPage1.TabIndex = 0;
        tabPage1.Text = "漫畫庫管理";
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
        splitter2.Panel1.Controls.Add(treeFolders);
        splitter2.Panel1MinSize = 5;
        // 
        // splitter2.Panel2
        // 
        splitter2.Panel2.Controls.Add(splitter1);
        splitter2.Size = new Size(1280, 626);
        splitter2.SplitterDistance = 309;
        splitter2.SplitterSize = 80;
        splitter2.SplitterWidth = 10;
        splitter2.TabIndex = 5;
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
        treeFolders.TabIndex = 2;
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
        // 
        // splitter1.Panel1
        // 
        splitter1.Panel1.BackColor = Color.WhiteSmoke;
        splitter1.Panel1.Controls.Add(breadcrumb);
        splitter1.Panel1.Controls.Add(menuToolbar);
        splitter1.Panel1.Padding = new Padding(3);
        splitter1.Panel1MinSize = 50;
        // 
        // splitter1.Panel2
        // 
        splitter1.Panel2.BackColor = Color.WhiteSmoke;
        splitter1.Panel2.Controls.Add(tabsContent);
        splitter1.Panel2.Padding = new Padding(0, 10, 0, 5);
        splitter1.Size = new Size(961, 626);
        splitter1.SplitterDistance = 69;
        splitter1.SplitterSize = 1;
        splitter1.SplitterWidth = 1;
        splitter1.TabIndex = 4;
        // 
        // breadcrumb
        // 
        breadcrumb.BackColor = Color.Transparent;
        breadcrumb.Dock = DockStyle.Left;
        breadcrumb.Location = new Point(3, 3);
        breadcrumb.Name = "breadcrumb";
        breadcrumb.Padding = new Padding(12, 0, 12, 0);
        breadcrumb.Size = new Size(527, 63);
        breadcrumb.TabIndex = 3;
        // 
        // menuToolbar
        // 
        menuToolbar.BackColor = Color.Transparent;
        menuToolbar.Dock = DockStyle.Right;
        menuToolbar.Gap = 4;
        menuItem1.IconSvg = "AppstoreOutlined";
        menuItem2.Text = "縮圖(小)";
        menuItem3.Text = "縮圖(中)";
        menuItem4.Text = "縮圖(大)";
        menuItem5.Text = "列表模式";
        menuItem1.Sub.Add(menuItem2);
        menuItem1.Sub.Add(menuItem3);
        menuItem1.Sub.Add(menuItem4);
        menuItem1.Sub.Add(menuItem5);
        menuItem1.Text = "檢視模式";
        menuItem6.IconSvg = "RedoOutlined";
        menuItem6.ID = "refresh";
        menuItem6.Name = "refresh";
        menuItem6.Text = "重新整理";
        menuToolbar.Items.Add(menuItem1);
        menuToolbar.Items.Add(menuItem6);
        menuToolbar.Location = new Point(706, 3);
        menuToolbar.Mode = TMenuMode.Horizontal;
        menuToolbar.Name = "menuToolbar";
        menuToolbar.ScrollBarBlock = true;
        menuToolbar.Size = new Size(252, 63);
        menuToolbar.TabIndex = 0;
        menuToolbar.Trigger = Trigger.Click;
        menuToolbar.SelectChanged += menuToolbar_SelectChanged;
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
        tabsContent.Type = TabType.Card;
        // 
        // tabPage2
        // 
        tabPage2.Controls.Add(lblStatus);
        tabPage2.Controls.Add(btnExecute);
        tabPage2.Controls.Add(tableArchive);
        tabPage2.Controls.Add(btnScan);
        tabPage2.Location = new Point(-2000, -1412);
        tabPage2.Name = "tabPage2";
        tabPage2.Size = new Size(1000, 706);
        tabPage2.TabIndex = 1;
        tabPage2.Text = "自動歸檔";
        // 
        // lblStatus
        // 
        lblStatus.Location = new Point(471, 24);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(400, 35);
        lblStatus.TabIndex = 0;
        lblStatus.Text = "就緒";
        // 
        // btnExecute
        // 
        btnExecute.Enabled = false;
        btnExecute.Location = new Point(239, 20);
        btnExecute.Name = "btnExecute";
        btnExecute.Size = new Size(172, 45);
        btnExecute.TabIndex = 1;
        btnExecute.Text = "預覽歸檔計畫";
        btnExecute.Type = TTypeMini.Success;
        // 
        // tableArchive
        // 
        tableArchive.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tableArchive.AutoSizeColumnsMode = ColumnsMode.Fill;
        tableArchive.EmptyText = "no data";
        tableArchive.Gap = 4;
        tableArchive.Gaps = new Size(4, 4);
        tableArchive.Location = new Point(20, 85);
        tableArchive.Name = "tableArchive";
        tableArchive.Size = new Size(955, 588);
        tableArchive.TabIndex = 2;
        // 
        // btnScan
        // 
        btnScan.Location = new Point(20, 20);
        btnScan.Name = "btnScan";
        btnScan.Size = new Size(178, 45);
        btnScan.TabIndex = 3;
        btnScan.Text = "開始掃描匹配";
        btnScan.Type = TTypeMini.Primary;
        // 
        // tabPage3
        // 
        tabPage3.Controls.Add(label3);
        tabPage3.Controls.Add(label2);
        tabPage3.Controls.Add(label1);
        tabPage3.Controls.Add(cbDebug);
        tabPage3.Controls.Add(btnSaveConfig);
        tabPage3.Controls.Add(btnBrowseViewer);
        tabPage3.Controls.Add(btnBrowseTarget);
        tabPage3.Controls.Add(btnBrowseSource);
        tabPage3.Controls.Add(txtImageViewer);
        tabPage3.Controls.Add(txtTarget);
        tabPage3.Controls.Add(txtSource);
        tabPage3.Location = new Point(-2000, -1412);
        tabPage3.Name = "tabPage3";
        tabPage3.Size = new Size(1000, 706);
        tabPage3.TabIndex = 2;
        tabPage3.Text = "系統設定";
        // 
        // label3
        // 
        label3.Location = new Point(39, 237);
        label3.Name = "label3";
        label3.Size = new Size(112, 34);
        label3.TabIndex = 8;
        label3.Text = "圖片瀏覽器";
        // 
        // label2
        // 
        label2.Location = new Point(39, 127);
        label2.Name = "label2";
        label2.Size = new Size(112, 34);
        label2.TabIndex = 7;
        label2.Text = "歸檔目錄";
        // 
        // label1
        // 
        label1.Location = new Point(39, 17);
        label1.Name = "label1";
        label1.Size = new Size(112, 34);
        label1.TabIndex = 6;
        label1.Text = "來源目錄";
        // 
        // cbDebug
        // 
        cbDebug.Location = new Point(39, 351);
        cbDebug.Name = "cbDebug";
        cbDebug.Size = new Size(335, 30);
        cbDebug.TabIndex = 0;
        cbDebug.Text = "偵錯模式 (不移動檔案)";
        // 
        // btnSaveConfig
        // 
        btnSaveConfig.Location = new Point(39, 421);
        btnSaveConfig.Name = "btnSaveConfig";
        btnSaveConfig.Size = new Size(150, 45);
        btnSaveConfig.TabIndex = 1;
        btnSaveConfig.Text = "儲存設定";
        btnSaveConfig.Type = TTypeMini.Primary;
        // 
        // btnBrowseViewer
        // 
        btnBrowseViewer.Location = new Point(649, 277);
        btnBrowseViewer.Name = "btnBrowseViewer";
        btnBrowseViewer.Size = new Size(80, 45);
        btnBrowseViewer.TabIndex = 2;
        btnBrowseViewer.Text = "瀏覽";
        // 
        // btnBrowseTarget
        // 
        btnBrowseTarget.Location = new Point(649, 167);
        btnBrowseTarget.Name = "btnBrowseTarget";
        btnBrowseTarget.Size = new Size(80, 45);
        btnBrowseTarget.TabIndex = 2;
        btnBrowseTarget.Text = "瀏覽";
        // 
        // btnBrowseSource
        // 
        btnBrowseSource.Location = new Point(649, 57);
        btnBrowseSource.Name = "btnBrowseSource";
        btnBrowseSource.Size = new Size(80, 45);
        btnBrowseSource.TabIndex = 3;
        btnBrowseSource.Text = "瀏覽";
        // 
        // txtImageViewer
        // 
        txtImageViewer.Location = new Point(39, 277);
        txtImageViewer.Name = "txtImageViewer";
        txtImageViewer.PlaceholderText = "外部圖片瀏覽器執行檔路徑 (.exe)";
        txtImageViewer.Size = new Size(600, 45);
        txtImageViewer.TabIndex = 4;
        // 
        // txtTarget
        // 
        txtTarget.Location = new Point(39, 167);
        txtTarget.Name = "txtTarget";
        txtTarget.PlaceholderText = "漫畫庫總根目錄 (Root)";
        txtTarget.Size = new Size(600, 45);
        txtTarget.TabIndex = 4;
        // 
        // txtSource
        // 
        txtSource.Location = new Point(39, 57);
        txtSource.Name = "txtSource";
        txtSource.PlaceholderText = "來源目錄 (待整理檔案路徑)";
        txtSource.Size = new Size(600, 45);
        txtSource.TabIndex = 5;
        // 
        // MainForm
        // 
        ClientSize = new Size(1280, 720);
        Controls.Add(tabs);
        Controls.Add(windowBar);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Comic Organize - 專業版";
        Load += MainForm_Load;
        tabs.ResumeLayout(false);
        tabPage1.ResumeLayout(false);
        splitter2.Panel1.ResumeLayout(false);
        splitter2.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitter2).EndInit();
        splitter2.ResumeLayout(false);
        splitter1.Panel1.ResumeLayout(false);
        splitter1.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitter1).EndInit();
        splitter1.ResumeLayout(false);
        tabPage2.ResumeLayout(false);
        tabPage3.ResumeLayout(false);
        ResumeLayout(false);
    }

    private AntdUI.Splitter splitter1;
    private AntdUI.Splitter splitter2;
}

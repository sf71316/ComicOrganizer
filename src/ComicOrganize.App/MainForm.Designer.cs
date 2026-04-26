namespace ComicOrganize.App;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private AntdUI.PageHeader windowBar; 
    private AntdUI.Tabs tabs;
    private AntdUI.TabPage tabPage1;
    private AntdUI.TabPage tabPage2;
    private AntdUI.TabPage tabPage3;
    
    private AntdUI.Table tableArchive;
    private AntdUI.Button btnScan;
    private AntdUI.Button btnExecute;
    private AntdUI.Label lblStatus;

    private AntdUI.Input txtSource;
    private AntdUI.Input txtTarget;
    private AntdUI.Button btnBrowseSource;
    private AntdUI.Button btnBrowseTarget;
    private AntdUI.Button btnSaveConfig;
    private AntdUI.Checkbox cbDebug;

    private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;

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
        windowBar = new AntdUI.PageHeader();
        tabs = new AntdUI.Tabs();
        tabPage3 = new AntdUI.TabPage();
        label2 = new AntdUI.Label();
        label1 = new AntdUI.Label();
        cbDebug = new AntdUI.Checkbox();
        btnSaveConfig = new AntdUI.Button();
        btnBrowseTarget = new AntdUI.Button();
        btnBrowseSource = new AntdUI.Button();
        txtTarget = new AntdUI.Input();
        txtSource = new AntdUI.Input();
        tabPage2 = new AntdUI.TabPage();
        lblStatus = new AntdUI.Label();
        btnExecute = new AntdUI.Button();
        tableArchive = new AntdUI.Table();
        btnScan = new AntdUI.Button();
        tabPage1 = new AntdUI.TabPage();
        folderBrowserDialog = new FolderBrowserDialog();
        tabs.SuspendLayout();
        tabPage3.SuspendLayout();
        tabPage2.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.BackColor = Color.FromArgb(192, 192, 255);
        windowBar.Dock = DockStyle.Top;
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.ShowButton = true;
        windowBar.ShowIcon = true;
        windowBar.Size = new Size(1000, 48);
        windowBar.TabIndex = 1;
        windowBar.Text = "Comic Organize";
        // 
        // tabs
        // 
        tabs.Controls.Add(tabPage2);
        tabs.Controls.Add(tabPage3);
        tabs.Controls.Add(tabPage1);
        tabs.Dock = DockStyle.Fill;
        tabs.Font = new Font("Microsoft JhengHei UI", 12F);
        tabs.Location = new Point(0, 48);
        tabs.Name = "tabs";
        tabs.Pages.Add(tabPage1);
        tabs.Pages.Add(tabPage2);
        tabs.Pages.Add(tabPage3);
        tabs.SelectedIndex = 1;
        tabs.Size = new Size(1000, 752);
        tabs.Style = styleCard1;
        tabs.TabIndex = 0;
        tabs.Type = AntdUI.TabType.Card;
        // 
        // tabPage3
        // 
        tabPage3.Controls.Add(label2);
        tabPage3.Controls.Add(label1);
        tabPage3.Controls.Add(cbDebug);
        tabPage3.Controls.Add(btnSaveConfig);
        tabPage3.Controls.Add(btnBrowseTarget);
        tabPage3.Controls.Add(btnBrowseSource);
        tabPage3.Controls.Add(txtTarget);
        tabPage3.Controls.Add(txtSource);
        tabPage3.Location = new Point(-2000, -1412);
        tabPage3.Name = "tabPage3";
        tabPage3.Size = new Size(1000, 706);
        tabPage3.TabIndex = 2;
        tabPage3.Text = "系統設定";
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
        cbDebug.Location = new Point(39, 241);
        cbDebug.Name = "cbDebug";
        cbDebug.Size = new Size(335, 30);
        cbDebug.TabIndex = 0;
        cbDebug.Text = "偵錯模式 (不移動檔案)";
        // 
        // btnSaveConfig
        // 
        btnSaveConfig.Location = new Point(39, 311);
        btnSaveConfig.Name = "btnSaveConfig";
        btnSaveConfig.Size = new Size(150, 45);
        btnSaveConfig.TabIndex = 1;
        btnSaveConfig.Text = "儲存設定";
        btnSaveConfig.Type = AntdUI.TTypeMini.Primary;
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
        // tabPage2
        // 
        tabPage2.Controls.Add(lblStatus);
        tabPage2.Controls.Add(btnExecute);
        tabPage2.Controls.Add(tableArchive);
        tabPage2.Controls.Add(btnScan);
        tabPage2.Location = new Point(0, 46);
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
        btnExecute.Type = AntdUI.TTypeMini.Success;
        // 
        // tableArchive
        // 
        tableArchive.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tableArchive.AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill;
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
        btnScan.Type = AntdUI.TTypeMini.Primary;
        // 
        // tabPage1
        // 
        tabPage1.Location = new Point(-2000, -1412);
        tabPage1.Name = "tabPage1";
        tabPage1.Size = new Size(1000, 706);
        tabPage1.TabIndex = 0;
        tabPage1.Text = "封面管理";
        // 
        // MainForm
        // 
        ClientSize = new Size(1000, 800);
        Controls.Add(tabs);
        Controls.Add(windowBar);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Comic Organize - 專業版";
        tabs.ResumeLayout(false);
        tabPage3.ResumeLayout(false);
        tabPage2.ResumeLayout(false);
        ResumeLayout(false);
    }

    private AntdUI.Label label1;
    private AntdUI.Label label2;
}

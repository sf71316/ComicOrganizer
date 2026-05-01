using AntdUI;

namespace ComicOrganizer.App.UI;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private AntdUI.Panel content;
    private AntdUI.Panel footer;

    // 左側選單 + 兩個分類按鈕
    private AntdUI.Panel leftMenu;
    private AntdUI.Button btnMenuGeneral;
    private AntdUI.Button btnMenuLibrary;

    // 內容區兩個 sub-panel（toggle Visible 切換）
    private AntdUI.Panel generalPanel;
    private AntdUI.Panel libraryPanel;

    // generalPanel 控件
    private AntdUI.Label labelTheme;
    private AntdUI.Select selectTheme;
    private AntdUI.Label labelLang;
    private AntdUI.Select selectLang;
    private AntdUI.Checkbox cbDebug;

    // libraryPanel 控件
    private AntdUI.Label label1;
    private AntdUI.Label label2;
    private AntdUI.Label label3;
    private AntdUI.Input txtSource;
    private AntdUI.Input txtTarget;
    private AntdUI.Input txtImageViewer;
    private AntdUI.Input txtImageViewerArgs;
    private AntdUI.Label labelImageViewerArgs;
    private AntdUI.Button btnBrowseSource;
    private AntdUI.Button btnBrowseTarget;
    private AntdUI.Button btnBrowseViewer;
    private AntdUI.Label labelMaxScanDepth;
    private AntdUI.InputNumber numMaxScanDepth;

    // Footer 按鈕
    private AntdUI.Button btnSave;
    private AntdUI.Button btnCancel;

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
        windowBar = new PageHeader();
        footer = new AntdUI.Panel();
        leftMenu = new AntdUI.Panel();
        content = new AntdUI.Panel();
        btnMenuGeneral = new AntdUI.Button();
        btnMenuLibrary = new AntdUI.Button();
        generalPanel = new AntdUI.Panel();
        libraryPanel = new AntdUI.Panel();
        labelTheme = new AntdUI.Label();
        selectTheme = new Select();
        labelLang = new AntdUI.Label();
        selectLang = new Select();
        cbDebug = new Checkbox();
        label1 = new AntdUI.Label();
        label2 = new AntdUI.Label();
        label3 = new AntdUI.Label();
        txtSource = new Input();
        txtTarget = new Input();
        txtImageViewer = new Input();
        txtImageViewerArgs = new Input();
        labelImageViewerArgs = new AntdUI.Label();
        btnBrowseSource = new AntdUI.Button();
        btnBrowseTarget = new AntdUI.Button();
        btnBrowseViewer = new AntdUI.Button();
        labelMaxScanDepth = new AntdUI.Label();
        numMaxScanDepth = new InputNumber();
        btnSave = new AntdUI.Button();
        btnCancel = new AntdUI.Button();

        leftMenu.SuspendLayout();
        content.SuspendLayout();
        generalPanel.SuspendLayout();
        libraryPanel.SuspendLayout();
        footer.SuspendLayout();
        SuspendLayout();
        //
        // windowBar
        //
        windowBar.BackColor = Color.FromArgb(60, 60, 60);
        windowBar.Dock = DockStyle.Top;
        windowBar.ForeColor = Color.White;
        windowBar.IconSvg = "SettingOutlined";
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.Padding = new Padding(3);
        windowBar.ShowButton = true;
        windowBar.ShowIcon = true;
        windowBar.Size = new Size(900, 48);
        windowBar.TabIndex = 0;
        windowBar.Text = "系統設定";
        windowBar.UseSystemStyleColor = true;
        //
        // leftMenu
        //
        leftMenu.BackColor = Color.FromArgb(245, 245, 245);
        leftMenu.Controls.Add(btnMenuLibrary);
        leftMenu.Controls.Add(btnMenuGeneral);
        leftMenu.Dock = DockStyle.Left;
        leftMenu.Location = new Point(0, 48);
        leftMenu.Name = "leftMenu";
        leftMenu.Padding = new Padding(15, 20, 15, 20);
        leftMenu.Radius = 0;
        leftMenu.Size = new Size(180, 562);
        leftMenu.TabIndex = 1;
        //
        // btnMenuGeneral
        //
        btnMenuGeneral.Dock = DockStyle.Top;
        btnMenuGeneral.IconSvg = "SettingOutlined";
        btnMenuGeneral.Location = new Point(15, 20);
        btnMenuGeneral.Name = "btnMenuGeneral";
        btnMenuGeneral.Size = new Size(150, 45);
        btnMenuGeneral.TabIndex = 0;
        btnMenuGeneral.Text = "一般設定";
        btnMenuGeneral.TextAlign = ContentAlignment.MiddleLeft;
        btnMenuGeneral.Type = TTypeMini.Primary;
        //
        // btnMenuLibrary
        //
        btnMenuLibrary.Dock = DockStyle.Top;
        btnMenuLibrary.IconSvg = "DatabaseOutlined";
        btnMenuLibrary.Location = new Point(15, 65);
        btnMenuLibrary.Name = "btnMenuLibrary";
        btnMenuLibrary.Size = new Size(150, 45);
        btnMenuLibrary.TabIndex = 1;
        btnMenuLibrary.Text = "漫畫庫管理";
        btnMenuLibrary.TextAlign = ContentAlignment.MiddleLeft;
        //
        // content
        //
        content.BackColor = Color.FromArgb(189, 189, 189);
        content.Controls.Add(generalPanel);
        content.Controls.Add(libraryPanel);
        content.Dock = DockStyle.Fill;
        content.Location = new Point(180, 48);
        content.Name = "content";
        content.Padding = new Padding(0);
        content.Radius = 0;
        content.Size = new Size(720, 562);
        content.TabIndex = 2;
        //
        // generalPanel
        //
        generalPanel.BackColor = Color.Transparent;
        generalPanel.Controls.Add(cbDebug);
        generalPanel.Controls.Add(selectLang);
        generalPanel.Controls.Add(labelLang);
        generalPanel.Controls.Add(selectTheme);
        generalPanel.Controls.Add(labelTheme);
        generalPanel.Dock = DockStyle.Fill;
        generalPanel.Location = new Point(0, 0);
        generalPanel.Name = "generalPanel";
        generalPanel.Padding = new Padding(40, 30, 40, 30);
        generalPanel.Radius = 0;
        generalPanel.Size = new Size(720, 562);
        generalPanel.TabIndex = 0;
        //
        // labelTheme
        //
        labelTheme.BackColor = Color.Transparent;
        labelTheme.Location = new Point(40, 30);
        labelTheme.Name = "labelTheme";
        labelTheme.Size = new Size(200, 30);
        labelTheme.TabIndex = 0;
        labelTheme.Text = "介面主題";
        //
        // selectTheme
        //
        selectTheme.Location = new Point(40, 65);
        selectTheme.Name = "selectTheme";
        selectTheme.Size = new Size(250, 40);
        selectTheme.TabIndex = 1;
        //
        // labelLang
        //
        labelLang.BackColor = Color.Transparent;
        labelLang.Location = new Point(40, 130);
        labelLang.Name = "labelLang";
        labelLang.Size = new Size(200, 30);
        labelLang.TabIndex = 2;
        labelLang.Text = "語言介面";
        //
        // selectLang
        //
        selectLang.Location = new Point(40, 165);
        selectLang.Name = "selectLang";
        selectLang.Size = new Size(250, 40);
        selectLang.TabIndex = 3;
        //
        // cbDebug
        //
        cbDebug.BackColor = Color.Transparent;
        cbDebug.Location = new Point(40, 240);
        cbDebug.Name = "cbDebug";
        cbDebug.Size = new Size(300, 30);
        cbDebug.TabIndex = 4;
        cbDebug.Text = "偵錯模式 (不移動檔案)";
        //
        // libraryPanel
        //
        libraryPanel.BackColor = Color.Transparent;
        libraryPanel.Controls.Add(numMaxScanDepth);
        libraryPanel.Controls.Add(labelMaxScanDepth);
        libraryPanel.Controls.Add(btnBrowseViewer);
        libraryPanel.Controls.Add(btnBrowseTarget);
        libraryPanel.Controls.Add(btnBrowseSource);
        libraryPanel.Controls.Add(txtImageViewerArgs);
        libraryPanel.Controls.Add(labelImageViewerArgs);
        libraryPanel.Controls.Add(txtImageViewer);
        libraryPanel.Controls.Add(txtTarget);
        libraryPanel.Controls.Add(txtSource);
        libraryPanel.Controls.Add(label3);
        libraryPanel.Controls.Add(label2);
        libraryPanel.Controls.Add(label1);
        libraryPanel.Dock = DockStyle.Fill;
        libraryPanel.Location = new Point(0, 0);
        libraryPanel.Name = "libraryPanel";
        libraryPanel.Padding = new Padding(40, 30, 40, 30);
        libraryPanel.Radius = 0;
        libraryPanel.Size = new Size(720, 562);
        libraryPanel.TabIndex = 1;
        libraryPanel.Visible = false;
        //
        // label1
        //
        label1.BackColor = Color.Transparent;
        label1.Location = new Point(40, 30);
        label1.Name = "label1";
        label1.Size = new Size(200, 30);
        label1.TabIndex = 0;
        label1.Text = "來源目錄";
        //
        // txtSource
        //
        txtSource.Location = new Point(40, 65);
        txtSource.Name = "txtSource";
        txtSource.PlaceholderText = "來源目錄 (待整理檔案路徑)";
        txtSource.Size = new Size(450, 45);
        txtSource.TabIndex = 1;
        //
        // btnBrowseSource
        //
        btnBrowseSource.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseSource.ForeColor = Color.White;
        btnBrowseSource.Location = new Point(500, 65);
        btnBrowseSource.Name = "btnBrowseSource";
        btnBrowseSource.Size = new Size(80, 45);
        btnBrowseSource.TabIndex = 2;
        btnBrowseSource.Text = "瀏覽";
        //
        // label2
        //
        label2.BackColor = Color.Transparent;
        label2.Location = new Point(40, 130);
        label2.Name = "label2";
        label2.Size = new Size(200, 30);
        label2.TabIndex = 3;
        label2.Text = "歸檔目錄";
        //
        // txtTarget
        //
        txtTarget.Location = new Point(40, 165);
        txtTarget.Name = "txtTarget";
        txtTarget.PlaceholderText = "漫畫庫總根目錄 (Root)";
        txtTarget.Size = new Size(450, 45);
        txtTarget.TabIndex = 4;
        //
        // btnBrowseTarget
        //
        btnBrowseTarget.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseTarget.ForeColor = Color.White;
        btnBrowseTarget.Location = new Point(500, 165);
        btnBrowseTarget.Name = "btnBrowseTarget";
        btnBrowseTarget.Size = new Size(80, 45);
        btnBrowseTarget.TabIndex = 5;
        btnBrowseTarget.Text = "瀏覽";
        //
        // label3
        //
        label3.BackColor = Color.Transparent;
        label3.Location = new Point(40, 230);
        label3.Name = "label3";
        label3.Size = new Size(200, 30);
        label3.TabIndex = 6;
        label3.Text = "圖片瀏覽器";
        //
        // txtImageViewer
        //
        txtImageViewer.Location = new Point(40, 265);
        txtImageViewer.Name = "txtImageViewer";
        txtImageViewer.PlaceholderText = "外部圖片瀏覽器執行檔路徑 (.exe)";
        txtImageViewer.Size = new Size(450, 45);
        txtImageViewer.TabIndex = 7;
        //
        // btnBrowseViewer
        //
        btnBrowseViewer.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseViewer.ForeColor = Color.White;
        btnBrowseViewer.Location = new Point(500, 265);
        btnBrowseViewer.Name = "btnBrowseViewer";
        btnBrowseViewer.Size = new Size(80, 45);
        btnBrowseViewer.TabIndex = 8;
        btnBrowseViewer.Text = "瀏覽";
        //
        // labelImageViewerArgs
        //
        labelImageViewerArgs.BackColor = Color.Transparent;
        labelImageViewerArgs.Location = new Point(40, 330);
        labelImageViewerArgs.Name = "labelImageViewerArgs";
        labelImageViewerArgs.Size = new Size(300, 30);
        labelImageViewerArgs.TabIndex = 9;
        labelImageViewerArgs.Text = "啟動參數（{path} = 路徑）";
        //
        // txtImageViewerArgs
        //
        txtImageViewerArgs.Location = new Point(40, 365);
        txtImageViewerArgs.Name = "txtImageViewerArgs";
        txtImageViewerArgs.PlaceholderText = "例如: \"{path}\"  或  -r \"{path}\"";
        txtImageViewerArgs.Size = new Size(540, 45);
        txtImageViewerArgs.TabIndex = 10;
        //
        // labelMaxScanDepth
        //
        labelMaxScanDepth.BackColor = Color.Transparent;
        labelMaxScanDepth.Location = new Point(40, 430);
        labelMaxScanDepth.Name = "labelMaxScanDepth";
        labelMaxScanDepth.Size = new Size(300, 30);
        labelMaxScanDepth.TabIndex = 11;
        labelMaxScanDepth.Text = "搜尋層數限制 (封面遞迴)";
        //
        // numMaxScanDepth
        //
        numMaxScanDepth.Location = new Point(40, 465);
        numMaxScanDepth.Name = "numMaxScanDepth";
        numMaxScanDepth.Minimum = 1;
        numMaxScanDepth.Maximum = 10;
        numMaxScanDepth.Value = 3;
        numMaxScanDepth.Size = new Size(150, 45);
        numMaxScanDepth.TabIndex = 12;
        //
        // footer
        //
        footer.BackColor = Color.FromArgb(189, 189, 189);
        footer.Controls.Add(btnSave);
        footer.Controls.Add(btnCancel);
        footer.Dock = DockStyle.Bottom;
        footer.Location = new Point(0, 610);
        footer.Name = "footer";
        footer.Padding = new Padding(20, 15, 20, 15);
        footer.Radius = 0;
        footer.Size = new Size(900, 70);
        footer.TabIndex = 3;
        //
        // btnSave
        //
        btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.BackColor = Color.FromArgb(60, 60, 60);
        btnSave.ForeColor = Color.White;
        btnSave.Location = new Point(760, 15);
        btnSave.Name = "btnSave";
        btnSave.Size = new Size(120, 40);
        btnSave.TabIndex = 1;
        btnSave.Text = "儲存設定";
        btnSave.Type = TTypeMini.Primary;
        btnSave.Click += btnSave_Click;
        //
        // btnCancel
        //
        btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnCancel.Location = new Point(640, 15);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(110, 40);
        btnCancel.TabIndex = 0;
        btnCancel.Text = "取消";
        //
        // SettingsForm
        //
        BackColor = Color.FromArgb(30, 30, 30);
        ClientSize = new Size(900, 680);
        Controls.Add(content);
        Controls.Add(leftMenu);
        Controls.Add(footer);
        Controls.Add(windowBar);
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "系統設定";
        leftMenu.ResumeLayout(false);
        content.ResumeLayout(false);
        generalPanel.ResumeLayout(false);
        libraryPanel.ResumeLayout(false);
        footer.ResumeLayout(false);
        ResumeLayout(false);
    }
}

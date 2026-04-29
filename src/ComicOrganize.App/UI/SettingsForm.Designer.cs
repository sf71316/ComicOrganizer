using AntdUI;
using FolderBrowserDialog = AntdUI.FolderBrowserDialog;

namespace ComicOrganize.App.UI;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private AntdUI.Panel content;
    private AntdUI.Panel footer;

    private AntdUI.Label label1;
    private AntdUI.Label label2;
    private AntdUI.Label label3;
    private AntdUI.Input txtSource;
    private AntdUI.Input txtTarget;
    private AntdUI.Input txtImageViewer;
    private AntdUI.Button btnBrowseSource;
    private AntdUI.Button btnBrowseTarget;
    private AntdUI.Button btnBrowseViewer;
    private AntdUI.Checkbox cbDebug;
    private AntdUI.Select selectTheme;
    private AntdUI.Select selectLang;
    private AntdUI.Label labelTheme;
    private AntdUI.Label labelLang;

    private AntdUI.Button btnSave;
    private AntdUI.Button btnCancel;

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
        windowBar = new PageHeader();
        content = new AntdUI.Panel();
        selectLang = new Select();
        labelLang = new AntdUI.Label();
        selectTheme = new Select();
        labelTheme = new AntdUI.Label();
        cbDebug = new Checkbox();
        btnBrowseViewer = new AntdUI.Button();
        btnBrowseTarget = new AntdUI.Button();
        btnBrowseSource = new AntdUI.Button();
        txtImageViewer = new Input();
        txtTarget = new Input();
        txtSource = new Input();
        label3 = new AntdUI.Label();
        label2 = new AntdUI.Label();
        label1 = new AntdUI.Label();
        footer = new AntdUI.Panel();
        btnSave = new AntdUI.Button();
        btnCancel = new AntdUI.Button();
        content.SuspendLayout();
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
        windowBar.Size = new Size(800, 48);
        windowBar.TabIndex = 0;
        windowBar.Text = "系統設定";
        windowBar.UseSystemStyleColor = true;
        // 
        // content
        // 
        content.BackColor = Color.FromArgb(189, 189, 189);
        content.Controls.Add(selectLang);
        content.Controls.Add(labelLang);
        content.Controls.Add(selectTheme);
        content.Controls.Add(labelTheme);
        content.Controls.Add(cbDebug);
        content.Controls.Add(btnBrowseViewer);
        content.Controls.Add(btnBrowseTarget);
        content.Controls.Add(btnBrowseSource);
        content.Controls.Add(txtImageViewer);
        content.Controls.Add(txtTarget);
        content.Controls.Add(txtSource);
        content.Controls.Add(label3);
        content.Controls.Add(label2);
        content.Controls.Add(label1);
        content.Dock = DockStyle.Fill;
        content.Location = new Point(0, 48);
        content.Name = "content";
        content.Padding = new Padding(40, 25, 40, 25);
        content.Radius = 0;
        content.Size = new Size(800, 432);
        content.TabIndex = 1;
        // 
        // selectLang
        // 
        selectLang.Location = new Point(260, 360);
        selectLang.Name = "selectLang";
        selectLang.Size = new Size(200, 40);
        selectLang.TabIndex = 13;
        // 
        // labelLang
        // 
        labelLang.BackColor = Color.Transparent;
        labelLang.Location = new Point(260, 325);
        labelLang.Name = "labelLang";
        labelLang.Size = new Size(112, 34);
        labelLang.TabIndex = 12;
        labelLang.Text = "語言";
        // 
        // selectTheme
        // 
        selectTheme.Location = new Point(40, 360);
        selectTheme.Name = "selectTheme";
        selectTheme.Size = new Size(200, 40);
        selectTheme.TabIndex = 11;
        // 
        // labelTheme
        // 
        labelTheme.BackColor = Color.Transparent;
        labelTheme.Location = new Point(40, 325);
        labelTheme.Name = "labelTheme";
        labelTheme.Size = new Size(112, 34);
        labelTheme.TabIndex = 10;
        labelTheme.Text = "介面主題";
        // 
        // cbDebug
        // 
        cbDebug.BackColor = Color.Transparent;
        cbDebug.Location = new Point(500, 365);
        cbDebug.Name = "cbDebug";
        cbDebug.Size = new Size(230, 30);
        cbDebug.TabIndex = 9;
        cbDebug.Text = "偵錯模式 (不移動檔案)";
        // 
        // btnBrowseViewer
        // 
        btnBrowseViewer.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseViewer.ForeColor = Color.White;
        btnBrowseViewer.Location = new Point(650, 260);
        btnBrowseViewer.Name = "btnBrowseViewer";
        btnBrowseViewer.Size = new Size(80, 45);
        btnBrowseViewer.TabIndex = 8;
        btnBrowseViewer.Text = "瀏覽";
        // 
        // btnBrowseTarget
        // 
        btnBrowseTarget.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseTarget.ForeColor = Color.White;
        btnBrowseTarget.Location = new Point(650, 155);
        btnBrowseTarget.Name = "btnBrowseTarget";
        btnBrowseTarget.Size = new Size(80, 45);
        btnBrowseTarget.TabIndex = 5;
        btnBrowseTarget.Text = "瀏覽";
        // 
        // btnBrowseSource
        // 
        btnBrowseSource.DefaultBack = Color.FromArgb(60, 60, 60);
        btnBrowseSource.ForeColor = Color.White;
        btnBrowseSource.Location = new Point(650, 50);
        btnBrowseSource.Name = "btnBrowseSource";
        btnBrowseSource.Size = new Size(80, 45);
        btnBrowseSource.TabIndex = 2;
        btnBrowseSource.Text = "瀏覽";
        // 
        // txtImageViewer
        // 
        txtImageViewer.Location = new Point(40, 260);
        txtImageViewer.Name = "txtImageViewer";
        txtImageViewer.PlaceholderText = "外部圖片瀏覽器執行檔路徑 (.exe)";
        txtImageViewer.Size = new Size(600, 45);
        txtImageViewer.TabIndex = 7;
        // 
        // txtTarget
        // 
        txtTarget.Location = new Point(40, 155);
        txtTarget.Name = "txtTarget";
        txtTarget.PlaceholderText = "漫畫庫總根目錄 (Root)";
        txtTarget.Size = new Size(600, 45);
        txtTarget.TabIndex = 4;
        // 
        // txtSource
        // 
        txtSource.Location = new Point(40, 50);
        txtSource.Name = "txtSource";
        txtSource.PlaceholderText = "來源目錄 (待整理檔案路徑)";
        txtSource.Size = new Size(600, 45);
        txtSource.TabIndex = 1;
        // 
        // label3
        // 
        label3.BackColor = Color.Transparent;
        label3.Location = new Point(40, 225);
        label3.Name = "label3";
        label3.Size = new Size(112, 34);
        label3.TabIndex = 6;
        label3.Text = "圖片瀏覽器";
        // 
        // label2
        // 
        label2.BackColor = Color.Transparent;
        label2.Location = new Point(40, 120);
        label2.Name = "label2";
        label2.Size = new Size(112, 34);
        label2.TabIndex = 3;
        label2.Text = "歸檔目錄";
        // 
        // label1
        // 
        label1.BackColor = Color.Transparent;
        label1.Location = new Point(40, 15);
        label1.Name = "label1";
        label1.Size = new Size(112, 34);
        label1.TabIndex = 0;
        label1.Text = "來源目錄";
        // 
        // footer
        // 
        footer.BackColor = Color.FromArgb(189, 189, 189);
        footer.Controls.Add(btnSave);
        footer.Controls.Add(btnCancel);
        footer.Dock = DockStyle.Bottom;
        footer.Location = new Point(0, 480);
        footer.Name = "footer";
        footer.Padding = new Padding(20, 15, 20, 15);
        footer.Radius = 0;
        footer.Size = new Size(800, 70);
        footer.TabIndex = 2;
        // 
        // btnSave
        // 
        btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.BackColor = Color.FromArgb(60, 60, 60);
        btnSave.ForeColor = Color.White;
        btnSave.Location = new Point(660, 15);
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
        btnCancel.Location = new Point(540, 15);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(110, 40);
        btnCancel.TabIndex = 0;
        btnCancel.Text = "取消";
        // 
        // SettingsForm
        // 
        BackColor = Color.FromArgb(30, 30, 30);
        ClientSize = new Size(800, 550);
        Controls.Add(content);
        Controls.Add(footer);
        Controls.Add(windowBar);
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "系統設定";
        content.ResumeLayout(false);
        footer.ResumeLayout(false);
        ResumeLayout(false);
    }
}

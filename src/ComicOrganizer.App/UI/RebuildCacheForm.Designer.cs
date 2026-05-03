namespace ComicOrganizer.App.UI;

partial class RebuildCacheForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private AntdUI.Panel content;
    private AntdUI.Button btnFullRebuild;
    private AntdUI.Label lblFullDesc;
    private AntdUI.Button btnUpdateIndex;
    private AntdUI.Label lblUpdateDesc;
    private AntdUI.Button btnClearCache;
    private AntdUI.Label lblClearDesc;
    private AntdUI.Button btnResume;
    private AntdUI.Label lblResumeDesc;
    private AntdUI.Divider divider;
    private AntdUI.Progress progressBar;
    private AntdUI.Label lblStatus;
    private AntdUI.Label lblCurrent;
    private AntdUI.Button btnCancel;
    private AntdUI.Button btnClose;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        windowBar = new AntdUI.PageHeader();
        content = new AntdUI.Panel();
        btnClose = new AntdUI.Button();
        btnCancel = new AntdUI.Button();
        lblCurrent = new AntdUI.Label();
        lblStatus = new AntdUI.Label();
        progressBar = new AntdUI.Progress();
        divider = new AntdUI.Divider();
        lblUpdateDesc = new AntdUI.Label();
        btnUpdateIndex = new AntdUI.Button();
        lblFullDesc = new AntdUI.Label();
        btnFullRebuild = new AntdUI.Button();
        lblClearDesc = new AntdUI.Label();
        btnClearCache = new AntdUI.Button();
        lblResumeDesc = new AntdUI.Label();
        btnResume = new AntdUI.Button();
        content.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.ColorScheme = AntdUI.TAMode.Dark;
        windowBar.Dock = DockStyle.Top;
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.ShowButton = true;
        windowBar.Size = new Size(546, 50);
        windowBar.TabIndex = 1;
        windowBar.Text = "重建索引資料庫";
        windowBar.UseTitleFont = true;
        // 
        // content
        // 
        content.BackColor = Color.Transparent;
        content.Controls.Add(btnClose);
        content.Controls.Add(btnCancel);
        content.Controls.Add(lblCurrent);
        content.Controls.Add(lblStatus);
        content.Controls.Add(progressBar);
        content.Controls.Add(divider);
        content.Controls.Add(lblUpdateDesc);
        content.Controls.Add(btnUpdateIndex);
        content.Controls.Add(lblFullDesc);
        content.Controls.Add(btnFullRebuild);
        content.Controls.Add(lblClearDesc);
        content.Controls.Add(btnClearCache);
        content.Controls.Add(lblResumeDesc);
        content.Controls.Add(btnResume);
        content.Dock = DockStyle.Fill;
        content.Location = new Point(0, 50);
        content.Name = "content";
        content.Padding = new Padding(30);
        content.Size = new Size(546, 367);
        content.TabIndex = 0;
        // 
        // btnClose
        // 
        btnClose.Location = new Point(422, 311);
        btnClose.Name = "btnClose";
        btnClose.Size = new Size(85, 35);
        btnClose.TabIndex = 0;
        btnClose.Text = "關閉";
        btnClose.Type = AntdUI.TTypeMini.Info;
        // 
        // btnCancel
        // 
        btnCancel.Location = new Point(327, 311);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(85, 35);
        btnCancel.TabIndex = 1;
        btnCancel.Text = "取消";
        btnCancel.Visible = false;
        // 
        // lblCurrent
        // 
        lblCurrent.BackColor = Color.Transparent;
        lblCurrent.Location = new Point(27, 262);
        lblCurrent.Name = "lblCurrent";
        lblCurrent.Size = new Size(480, 25);
        lblCurrent.TabIndex = 2;
        lblCurrent.Text = "";
        // 
        // lblStatus
        // 
        lblStatus.BackColor = Color.Transparent;
        lblStatus.Location = new Point(27, 282);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(480, 25);
        lblStatus.TabIndex = 3;
        lblStatus.Text = "";
        // 
        // progressBar
        // 
        progressBar.Location = new Point(27, 240);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(480, 25);
        progressBar.TabIndex = 4;
        progressBar.Visible = false;
        // 
        // divider
        // 
        divider.Location = new Point(27, 214);
        divider.Name = "divider";
        divider.Size = new Size(480, 20);
        divider.TabIndex = 5;
        // 
        // lblUpdateDesc
        // 
        lblUpdateDesc.BackColor = Color.Transparent;
        lblUpdateDesc.Location = new Point(217, 80);
        lblUpdateDesc.Name = "lblUpdateDesc";
        lblUpdateDesc.Size = new Size(290, 30);
        lblUpdateDesc.TabIndex = 6;
        lblUpdateDesc.Text = "更新漫畫庫最新資料與刪除舊有資料";
        // 
        // btnUpdateIndex
        // 
        btnUpdateIndex.Location = new Point(27, 76);
        btnUpdateIndex.Name = "btnUpdateIndex";
        btnUpdateIndex.Size = new Size(180, 40);
        btnUpdateIndex.TabIndex = 7;
        btnUpdateIndex.Text = "更新索引資料庫";
        btnUpdateIndex.Type = AntdUI.TTypeMini.Primary;
        // 
        // lblFullDesc
        // 
        lblFullDesc.BackColor = Color.Transparent;
        lblFullDesc.Location = new Point(217, 33);
        lblFullDesc.Name = "lblFullDesc";
        lblFullDesc.Size = new Size(290, 30);
        lblFullDesc.TabIndex = 8;
        lblFullDesc.Text = "清空所有 cache，從根目錄重新掃描";
        // 
        // btnFullRebuild
        // 
        btnFullRebuild.Location = new Point(27, 29);
        btnFullRebuild.Name = "btnFullRebuild";
        btnFullRebuild.Size = new Size(180, 40);
        btnFullRebuild.TabIndex = 9;
        btnFullRebuild.Text = "完整重建";
        btnFullRebuild.Type = AntdUI.TTypeMini.Primary;
        // 
        // lblClearDesc
        // 
        lblClearDesc.BackColor = Color.Transparent;
        lblClearDesc.Location = new Point(215, 127);
        lblClearDesc.Margin = new Padding(2);
        lblClearDesc.Name = "lblClearDesc";
        lblClearDesc.Size = new Size(290, 30);
        lblClearDesc.TabIndex = 13;
        lblClearDesc.Text = "選擇要清除的快取資料範圍（縮圖 / 完整）";
        // 
        // btnClearCache
        // 
        btnClearCache.Location = new Point(27, 123);
        btnClearCache.Margin = new Padding(2);
        btnClearCache.Name = "btnClearCache";
        btnClearCache.Size = new Size(180, 40);
        btnClearCache.TabIndex = 12;
        btnClearCache.Text = "清除快取資料 ▾";
        btnClearCache.Type = AntdUI.TTypeMini.Error;
        // 
        // lblResumeDesc
        // 
        lblResumeDesc.BackColor = Color.Transparent;
        lblResumeDesc.Location = new Point(215, 174);
        lblResumeDesc.Margin = new Padding(2);
        lblResumeDesc.Name = "lblResumeDesc";
        lblResumeDesc.Size = new Size(290, 30);
        lblResumeDesc.TabIndex = 10;
        lblResumeDesc.Text = "上次掃描被中斷，繼續未完成的部分（已 cache 的會 skip）";
        // 
        // btnResume
        // 
        btnResume.Location = new Point(27, 170);
        btnResume.Margin = new Padding(2);
        btnResume.Name = "btnResume";
        btnResume.Size = new Size(180, 40);
        btnResume.TabIndex = 11;
        btnResume.Text = "繼續未完成的掃描";
        btnResume.Type = AntdUI.TTypeMini.Primary;
        // 
        // RebuildCacheForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(546, 417);
        Controls.Add(content);
        Controls.Add(windowBar);
        Name = "RebuildCacheForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "重建索引資料庫";
        content.ResumeLayout(false);
        ResumeLayout(false);
    }
}

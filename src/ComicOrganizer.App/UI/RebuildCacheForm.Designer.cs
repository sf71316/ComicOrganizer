namespace ComicOrganizer.App.UI;

partial class RebuildCacheForm
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.PageHeader windowBar;
    private AntdUI.Panel content;
    private AntdUI.Button btnFullRebuild;
    private AntdUI.Label lblFullDesc;
    private AntdUI.Button btnPartialRebuild;
    private AntdUI.Label lblPartialDesc;
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
        lblPartialDesc = new AntdUI.Label();
        btnPartialRebuild = new AntdUI.Button();
        lblFullDesc = new AntdUI.Label();
        btnFullRebuild = new AntdUI.Button();
        lblResumeDesc = new AntdUI.Label();
        btnResume = new AntdUI.Button();
        content.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.Dock = DockStyle.Top;
        windowBar.Location = new Point(0, 0);
        windowBar.Margin = new Padding(5);
        windowBar.Name = "windowBar";
        windowBar.ShowButton = true;
        windowBar.Size = new Size(849, 77);
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
        content.Controls.Add(lblPartialDesc);
        content.Controls.Add(btnPartialRebuild);
        content.Controls.Add(lblFullDesc);
        content.Controls.Add(btnFullRebuild);
        content.Controls.Add(lblResumeDesc);
        content.Controls.Add(btnResume);
        content.Dock = DockStyle.Fill;
        content.Location = new Point(0, 77);
        content.Margin = new Padding(5);
        content.Name = "content";
        content.Padding = new Padding(47, 46, 47, 46);
        content.Size = new Size(849, 504);
        content.TabIndex = 0;
        // 
        // btnClose
        // 
        btnClose.Location = new Point(663, 405);
        btnClose.Margin = new Padding(5);
        btnClose.Name = "btnClose";
        btnClose.Size = new Size(134, 54);
        btnClose.TabIndex = 0;
        btnClose.Text = "關閉";
        btnClose.Type = AntdUI.TTypeMini.Info;
        // 
        // btnCancel
        // 
        btnCancel.Location = new Point(514, 405);
        btnCancel.Margin = new Padding(5);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(134, 54);
        btnCancel.TabIndex = 1;
        btnCancel.Text = "取消";
        btnCancel.Visible = false;
        // 
        // lblCurrent
        // 
        lblCurrent.BackColor = Color.Transparent;
        lblCurrent.Location = new Point(42, 323);
        lblCurrent.Margin = new Padding(5);
        lblCurrent.Name = "lblCurrent";
        lblCurrent.Size = new Size(754, 38);
        lblCurrent.TabIndex = 2;
        lblCurrent.Text = "";
        // 
        // lblStatus
        // 
        lblStatus.BackColor = Color.Transparent;
        lblStatus.Location = new Point(42, 368);
        lblStatus.Margin = new Padding(5);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(314, 38);
        lblStatus.TabIndex = 3;
        lblStatus.Text = "";
        // 
        // progressBar
        // 
        progressBar.Location = new Point(42, 282);
        progressBar.Margin = new Padding(5);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(754, 38);
        progressBar.TabIndex = 4;
        progressBar.Visible = false;
        // 
        // divider
        // 
        divider.Location = new Point(42, 265);
        divider.Margin = new Padding(5);
        divider.Name = "divider";
        divider.Size = new Size(754, 31);
        divider.TabIndex = 5;
        // 
        // lblPartialDesc
        // 
        lblPartialDesc.BackColor = Color.Transparent;
        lblPartialDesc.Location = new Point(341, 125);
        lblPartialDesc.Margin = new Padding(5);
        lblPartialDesc.Name = "lblPartialDesc";
        lblPartialDesc.Size = new Size(456, 46);
        lblPartialDesc.TabIndex = 6;
        lblPartialDesc.Text = "選擇子目錄重新掃描";
        // 
        // btnPartialRebuild
        // 
        btnPartialRebuild.Location = new Point(42, 118);
        btnPartialRebuild.Margin = new Padding(5);
        btnPartialRebuild.Name = "btnPartialRebuild";
        btnPartialRebuild.Size = new Size(283, 61);
        btnPartialRebuild.TabIndex = 7;
        btnPartialRebuild.Text = "部分重建...";
        btnPartialRebuild.Type = AntdUI.TTypeMini.Success;
        // 
        // lblFullDesc
        // 
        lblFullDesc.BackColor = Color.Transparent;
        lblFullDesc.Location = new Point(341, 51);
        lblFullDesc.Margin = new Padding(5);
        lblFullDesc.Name = "lblFullDesc";
        lblFullDesc.Size = new Size(456, 46);
        lblFullDesc.TabIndex = 8;
        lblFullDesc.Text = "清空所有 cache，從根目錄重新掃描";
        // 
        // btnFullRebuild
        // 
        btnFullRebuild.Location = new Point(42, 44);
        btnFullRebuild.Margin = new Padding(5);
        btnFullRebuild.Name = "btnFullRebuild";
        btnFullRebuild.Size = new Size(283, 61);
        btnFullRebuild.TabIndex = 9;
        btnFullRebuild.Text = "完整重建";
        btnFullRebuild.Type = AntdUI.TTypeMini.Primary;
        // 
        // lblResumeDesc
        // 
        lblResumeDesc.BackColor = Color.Transparent;
        lblResumeDesc.Location = new Point(341, 200);
        lblResumeDesc.Name = "lblResumeDesc";
        lblResumeDesc.Size = new Size(307, 46);
        lblResumeDesc.TabIndex = 10;
        lblResumeDesc.Text = "上次掃描被中斷，繼續未完成的部分（已 cache 的會 skip）";
        // 
        // btnResume
        // 
        btnResume.Location = new Point(42, 195);
        btnResume.Name = "btnResume";
        btnResume.Size = new Size(283, 61);
        btnResume.TabIndex = 11;
        btnResume.Text = "繼續未完成的掃描";
        btnResume.Type = AntdUI.TTypeMini.Primary;
        btnResume.Visible = false;
        // 
        // RebuildCacheForm
        // 
        AutoScaleDimensions = new SizeF(11F, 23F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(849, 581);
        Controls.Add(content);
        Controls.Add(windowBar);
        Margin = new Padding(5);
        Name = "RebuildCacheForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "重建索引資料庫";
        content.ResumeLayout(false);
        ResumeLayout(false);
    }
}

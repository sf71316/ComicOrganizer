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
        content.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.Dock = DockStyle.Top;
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.ShowButton = true;
        windowBar.Size = new Size(548, 50);
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
        content.Dock = DockStyle.Fill;
        content.Location = new Point(0, 50);
        content.Name = "content";
        content.Padding = new Padding(30);
        content.Size = new Size(548, 326);
        content.TabIndex = 0;
        // 
        // btnClose
        // 
        btnClose.Location = new Point(427, 270);
        btnClose.Name = "btnClose";
        btnClose.Size = new Size(85, 35);
        btnClose.TabIndex = 0;
        btnClose.Text = "關閉";
        // 
        // btnCancel
        // 
        btnCancel.Location = new Point(332, 270);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(85, 35);
        btnCancel.TabIndex = 1;
        btnCancel.Text = "取消";
        btnCancel.Visible = false;
        // 
        // lblCurrent
        // 
        lblCurrent.BackColor = Color.Transparent;
        lblCurrent.Location = new Point(30, 295);
        lblCurrent.Name = "lblCurrent";
        lblCurrent.Size = new Size(480, 25);
        lblCurrent.TabIndex = 2;
        lblCurrent.Text = "";
        // 
        // lblStatus
        // 
        lblStatus.BackColor = Color.Transparent;
        lblStatus.Location = new Point(30, 270);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(200, 25);
        lblStatus.TabIndex = 3;
        lblStatus.Text = "";
        // 
        // progressBar
        // 
        progressBar.Location = new Point(30, 235);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(480, 25);
        progressBar.TabIndex = 4;
        // 
        // divider
        // 
        divider.Location = new Point(30, 200);
        divider.Name = "divider";
        divider.Size = new Size(480, 20);
        divider.TabIndex = 5;
        // 
        // lblPartialDesc
        // 
        lblPartialDesc.BackColor = Color.Transparent;
        lblPartialDesc.Location = new Point(220, 145);
        lblPartialDesc.Name = "lblPartialDesc";
        lblPartialDesc.Size = new Size(290, 30);
        lblPartialDesc.TabIndex = 6;
        lblPartialDesc.Text = "選擇子目錄重新掃描";
        // 
        // btnPartialRebuild
        // 
        btnPartialRebuild.Location = new Point(30, 140);
        btnPartialRebuild.Name = "btnPartialRebuild";
        btnPartialRebuild.Size = new Size(180, 40);
        btnPartialRebuild.TabIndex = 7;
        btnPartialRebuild.Text = "部分重建...";
        // 
        // lblFullDesc
        // 
        lblFullDesc.BackColor = Color.Transparent;
        lblFullDesc.Location = new Point(220, 85);
        lblFullDesc.Name = "lblFullDesc";
        lblFullDesc.Size = new Size(290, 30);
        lblFullDesc.TabIndex = 8;
        lblFullDesc.Text = "清空所有 cache，從根目錄重新掃描";
        // 
        // btnFullRebuild
        // 
        btnFullRebuild.Location = new Point(30, 80);
        btnFullRebuild.Name = "btnFullRebuild";
        btnFullRebuild.Size = new Size(180, 40);
        btnFullRebuild.TabIndex = 9;
        btnFullRebuild.Text = "完整重建";
        btnFullRebuild.Type = AntdUI.TTypeMini.Primary;
        // 
        // RebuildCacheForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(548, 376);
        Controls.Add(content);
        Controls.Add(windowBar);
        Name = "RebuildCacheForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "重建索引資料庫";
        content.ResumeLayout(false);
        ResumeLayout(false);
    }
}

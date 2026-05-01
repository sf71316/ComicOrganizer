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
        btnFullRebuild = new AntdUI.Button();
        lblFullDesc = new AntdUI.Label();
        btnPartialRebuild = new AntdUI.Button();
        lblPartialDesc = new AntdUI.Label();
        divider = new AntdUI.Divider();
        progressBar = new AntdUI.Progress();
        lblStatus = new AntdUI.Label();
        lblCurrent = new AntdUI.Label();
        btnCancel = new AntdUI.Button();
        btnClose = new AntdUI.Button();
        content.SuspendLayout();
        SuspendLayout();
        //
        // windowBar
        //
        windowBar.Dock = DockStyle.Top;
        windowBar.Size = new Size(540, 50);
        windowBar.Text = "重建索引資料庫";
        windowBar.UseTitleFont = true;
        windowBar.ShowButton = true;
        //
        // content
        //
        content.Dock = DockStyle.Fill;
        content.BackColor = Color.Transparent;
        content.Padding = new Padding(30);
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
        //
        // btnFullRebuild
        //
        btnFullRebuild.Location = new Point(30, 80);
        btnFullRebuild.Size = new Size(180, 40);
        btnFullRebuild.Type = AntdUI.TTypeMini.Primary;
        btnFullRebuild.Text = "完整重建";
        //
        // lblFullDesc
        //
        lblFullDesc.Location = new Point(220, 85);
        lblFullDesc.Size = new Size(290, 30);
        lblFullDesc.Text = "清空所有 cache，從根目錄重新掃描";
        lblFullDesc.BackColor = Color.Transparent;
        //
        // btnPartialRebuild
        //
        btnPartialRebuild.Location = new Point(30, 140);
        btnPartialRebuild.Size = new Size(180, 40);
        btnPartialRebuild.Text = "部分重建...";
        //
        // lblPartialDesc
        //
        lblPartialDesc.Location = new Point(220, 145);
        lblPartialDesc.Size = new Size(290, 30);
        lblPartialDesc.Text = "選擇子目錄重新掃描";
        lblPartialDesc.BackColor = Color.Transparent;
        //
        // divider
        //
        divider.Location = new Point(30, 200);
        divider.Size = new Size(480, 20);
        //
        // progressBar
        //
        progressBar.Location = new Point(30, 235);
        progressBar.Size = new Size(480, 25);
        progressBar.Value = 0f;
        //
        // lblStatus
        //
        lblStatus.Location = new Point(30, 270);
        lblStatus.Size = new Size(200, 25);
        lblStatus.Text = "等待中";
        lblStatus.BackColor = Color.Transparent;
        //
        // lblCurrent
        //
        lblCurrent.Location = new Point(30, 295);
        lblCurrent.Size = new Size(480, 25);
        lblCurrent.Text = "";
        lblCurrent.BackColor = Color.Transparent;
        //
        // btnCancel
        //
        btnCancel.Location = new Point(330, 340);
        btnCancel.Size = new Size(85, 35);
        btnCancel.Type = AntdUI.TTypeMini.Default;
        btnCancel.Text = "取消";
        btnCancel.Visible = false;
        //
        // btnClose
        //
        btnClose.Location = new Point(425, 340);
        btnClose.Size = new Size(85, 35);
        btnClose.Type = AntdUI.TTypeMini.Default;
        btnClose.Text = "關閉";
        //
        // RebuildCacheForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(540, 400);
        Controls.Add(content);
        Controls.Add(windowBar);
        Name = "RebuildCacheForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "重建索引資料庫";
        content.ResumeLayout(false);
        ResumeLayout(false);
    }
}

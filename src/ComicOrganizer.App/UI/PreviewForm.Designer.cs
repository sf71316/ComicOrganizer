namespace ComicOrganizer.App.UI;

partial class PreviewForm
{
    private System.ComponentModel.IContainer components = null;
    private AntdUI.PageHeader windowBar;
    private AntdUI.Panel footer;
    private AntdUI.Button btnExecute;
    private AntdUI.Button btnCancel;
    private AntdUI.Progress progress;
    private AntdUI.Label lblProgress;
    private AntdUI.Checkbox cbFetchMetadata;

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
        windowBar = new AntdUI.PageHeader();
        footer = new AntdUI.Panel();
        lblProgress = new AntdUI.Label();
        progress = new AntdUI.Progress();
        btnCancel = new AntdUI.Button();
        btnExecute = new AntdUI.Button();
        cbFetchMetadata = new AntdUI.Checkbox();
        splitter1 = new AntdUI.Splitter();
        collapseMain = new AntdUI.Collapse();
        footer.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitter1).BeginInit();
        splitter1.Panel2.SuspendLayout();
        splitter1.SuspendLayout();
        SuspendLayout();
        // 
        // windowBar
        // 
        windowBar.BackColor = Color.FromArgb(245, 245, 245);
        windowBar.Dock = DockStyle.Top;
        windowBar.Location = new Point(0, 0);
        windowBar.Name = "windowBar";
        windowBar.ShowButton = true;
        windowBar.ShowIcon = true;
        windowBar.Size = new Size(1048, 50);
        windowBar.TabIndex = 2;
        windowBar.Text = "預覽歸檔計畫";
        // 
        // footer
        // 
        footer.Controls.Add(lblProgress);
        footer.Controls.Add(progress);
        footer.Controls.Add(cbFetchMetadata);
        footer.Controls.Add(btnCancel);
        footer.Controls.Add(btnExecute);
        footer.Dock = DockStyle.Bottom;
        footer.Location = new Point(0, 900);
        footer.Name = "footer";
        footer.Size = new Size(1048, 100);
        footer.TabIndex = 1;
        // 
        // lblProgress
        // 
        lblProgress.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        lblProgress.Location = new Point(20, 40);
        lblProgress.Name = "lblProgress";
        lblProgress.Size = new Size(400, 25);
        lblProgress.TabIndex = 0;
        lblProgress.Text = "準備就緒";
        // 
        // progress
        // 
        progress.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        progress.Location = new Point(20, 10);
        progress.Name = "progress";
        progress.Size = new Size(708, 20);
        progress.TabIndex = 1;
        progress.Visible = false;
        // 
        // btnCancel
        // 
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.Location = new Point(748, 30);
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(120, 45);
        btnCancel.TabIndex = 2;
        btnCancel.Text = "取消";
        //
        // btnExecute
        //
        btnExecute.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnExecute.Enabled = false;
        btnExecute.Location = new Point(878, 30);
        btnExecute.Name = "btnExecute";
        btnExecute.Size = new Size(150, 45);
        btnExecute.TabIndex = 3;
        btnExecute.Text = "執行歸檔";
        btnExecute.Type = AntdUI.TTypeMini.Success;
        //
        // cbFetchMetadata — 同時抓 nhentai metadata（依 EnableExternalMetadata 啟用）
        //
        cbFetchMetadata.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        cbFetchMetadata.Location = new Point(490, 40);
        cbFetchMetadata.Name = "cbFetchMetadata";
        cbFetchMetadata.Size = new Size(250, 25);
        cbFetchMetadata.TabIndex = 4;
        cbFetchMetadata.Text = "搬完抓 metadata";
        cbFetchMetadata.Enabled = false;
        // 
        // splitter1
        // 
        splitter1.Dock = DockStyle.Fill;
        splitter1.Location = new Point(0, 50);
        splitter1.Name = "splitter1";
        splitter1.Orientation = Orientation.Horizontal;
        // 
        // splitter1.Panel2
        // 
        splitter1.Panel2.Controls.Add(collapseMain);
        splitter1.Size = new Size(1048, 850);
        splitter1.SplitterDistance = 81;
        splitter1.TabIndex = 3;
        // 
        // collapseMain
        // 
        collapseMain.ContentPadding = new Size(5, 5);
        collapseMain.Dock = DockStyle.Fill;
        collapseMain.FontExpand = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 136);
        collapseMain.HeaderBg = Color.FromArgb(194, 193, 192);
        collapseMain.Location = new Point(0, 0);
        collapseMain.Name = "collapseMain";
        collapseMain.Size = new Size(1048, 765);
        collapseMain.TabIndex = 0;
        collapseMain.Text = "collapse1";
        collapseMain.Unique = true;
        // 
        // PreviewForm
        // 
        ClientSize = new Size(1048, 1000);
        Controls.Add(splitter1);
        Controls.Add(footer);
        Controls.Add(windowBar);
        Name = "PreviewForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "預覽歸檔計畫";
        footer.ResumeLayout(false);
        splitter1.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitter1).EndInit();
        splitter1.ResumeLayout(false);
        ResumeLayout(false);
    }

    private AntdUI.Splitter splitter1;
    private AntdUI.Collapse collapseMain;
}

namespace ComicOrganize.App.UI;

partial class PreviewForm
{
    private System.ComponentModel.IContainer components = null;
    private AntdUI.PageHeader windowBar;
    private System.Windows.Forms.Panel container; // 使用原生 Panel 作為根容器，提高相容性
    private AntdUI.Panel footer;
    private AntdUI.Button btnExecute;
    private AntdUI.Button btnCancel;
    private AntdUI.Progress progress;
    private AntdUI.Label lblProgress;

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
        container = new Panel();
        footer = new AntdUI.Panel();
        lblProgress = new AntdUI.Label();
        progress = new AntdUI.Progress();
        btnCancel = new AntdUI.Button();
        btnExecute = new AntdUI.Button();
        footer.SuspendLayout();
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
        // container
        // 
        container.AutoScroll = true;
        container.BackColor = Color.White;
        container.Dock = DockStyle.Fill;
        container.Location = new Point(0, 50);
        container.Name = "container";
        container.Padding = new Padding(10);
        container.Size = new Size(1048, 850);
        container.TabIndex = 0;
        // 
        // footer
        // 
        footer.Controls.Add(lblProgress);
        footer.Controls.Add(progress);
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
        // PreviewForm
        // 
        ClientSize = new Size(1048, 1000);
        Controls.Add(container);
        Controls.Add(footer);
        Controls.Add(windowBar);
        Name = "PreviewForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "預覽歸檔計畫";
        footer.ResumeLayout(false);
        ResumeLayout(false);
    }
}

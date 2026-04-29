using AntdUI;

namespace ComicOrganize.App.UI;

partial class ArchiveControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Panel panelToolbar;
    private AntdUI.Button btnScan;
    private AntdUI.Button btnExecute;
    private AntdUI.Label lblStatus;
    private AntdUI.Table tableArchive;

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
        panelToolbar = new AntdUI.Panel();
        btnScan = new AntdUI.Button();
        btnExecute = new AntdUI.Button();
        lblStatus = new AntdUI.Label();
        tableArchive = new Table();
        panelToolbar.SuspendLayout();
        SuspendLayout();
        //
        // panelToolbar
        //
        panelToolbar.Controls.Add(lblStatus);
        panelToolbar.Controls.Add(btnExecute);
        panelToolbar.Controls.Add(btnScan);
        panelToolbar.Dock = DockStyle.Top;
        panelToolbar.Location = new Point(0, 0);
        panelToolbar.Name = "panelToolbar";
        panelToolbar.Padding = new Padding(20, 15, 20, 15);
        panelToolbar.Radius = 0;
        panelToolbar.Size = new Size(1000, 75);
        panelToolbar.TabIndex = 0;
        //
        // btnScan
        //
        btnScan.Location = new Point(20, 15);
        btnScan.Name = "btnScan";
        btnScan.Size = new Size(178, 45);
        btnScan.TabIndex = 0;
        btnScan.Text = "開始掃描匹配";
        btnScan.Type = TTypeMini.Primary;
        //
        // btnExecute
        //
        btnExecute.Enabled = false;
        btnExecute.Location = new Point(218, 15);
        btnExecute.Name = "btnExecute";
        btnExecute.Size = new Size(172, 45);
        btnExecute.TabIndex = 1;
        btnExecute.Text = "預覽歸檔計畫";
        btnExecute.Type = TTypeMini.Success;
        //
        // lblStatus
        //
        lblStatus.Location = new Point(410, 22);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(550, 30);
        lblStatus.TabIndex = 2;
        lblStatus.Text = "就緒";
        //
        // tableArchive
        //
        tableArchive.AutoSizeColumnsMode = ColumnsMode.Fill;
        tableArchive.Dock = DockStyle.Fill;
        tableArchive.EmptyText = "no data";
        tableArchive.Gap = 4;
        tableArchive.Gaps = new Size(4, 4);
        tableArchive.Location = new Point(0, 75);
        tableArchive.Margin = new Padding(20);
        tableArchive.Name = "tableArchive";
        tableArchive.Padding = new Padding(20, 0, 20, 20);
        tableArchive.Size = new Size(1000, 631);
        tableArchive.TabIndex = 1;
        //
        // ArchiveControl
        //
        Controls.Add(tableArchive);
        Controls.Add(panelToolbar);
        Name = "ArchiveControl";
        Size = new Size(1000, 706);
        panelToolbar.ResumeLayout(false);
        ResumeLayout(false);
    }
}

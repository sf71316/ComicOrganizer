using AntdUI;

namespace ComicOrganizer.App.UI;

partial class AuthorWorksListPanel
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Panel toolbarPanel;
    private System.Windows.Forms.Label lblHeader;
    private AntdUI.Input txtSearch;
    private AntdUI.Button btnSelectAll;
    private AntdUI.Button btnSelectEligible;
    private AntdUI.Button btnDeselectAll;
    private AntdUI.Button btnSyncSelected;
    private System.Windows.Forms.Label lblStatus;

    private AntdUI.Table tableWorks;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        toolbarPanel = new System.Windows.Forms.Panel();
        lblStatus = new System.Windows.Forms.Label();
        btnSyncSelected = new AntdUI.Button();
        btnDeselectAll = new AntdUI.Button();
        btnSelectEligible = new AntdUI.Button();
        btnSelectAll = new AntdUI.Button();
        txtSearch = new Input();
        lblHeader = new System.Windows.Forms.Label();
        tableWorks = new Table();
        toolbarPanel.SuspendLayout();
        SuspendLayout();
        // 
        // toolbarPanel
        // 
        toolbarPanel.Controls.Add(lblStatus);
        toolbarPanel.Controls.Add(btnSyncSelected);
        toolbarPanel.Controls.Add(btnDeselectAll);
        toolbarPanel.Controls.Add(btnSelectEligible);
        toolbarPanel.Controls.Add(btnSelectAll);
        toolbarPanel.Controls.Add(txtSearch);
        toolbarPanel.Controls.Add(lblHeader);
        toolbarPanel.Dock = DockStyle.Top;
        toolbarPanel.Location = new Point(0, 0);
        toolbarPanel.Name = "toolbarPanel";
        toolbarPanel.Padding = new Padding(16, 12, 16, 8);
        toolbarPanel.Size = new Size(690, 100);
        toolbarPanel.TabIndex = 1;
        // 
        // lblStatus
        // 
        lblStatus.AutoSize = true;
        lblStatus.Location = new Point(16, 78);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new Size(0, 15);
        lblStatus.TabIndex = 0;
        // 
        // btnSyncSelected
        // 
        btnSyncSelected.Enabled = false;
        btnSyncSelected.IconSvg = "ApiOutlined";
        btnSyncSelected.Location = new Point(536, 42);
        btnSyncSelected.Name = "btnSyncSelected";
        btnSyncSelected.Size = new Size(140, 30);
        btnSyncSelected.TabIndex = 1;
        btnSyncSelected.Text = "同步選取作品";
        btnSyncSelected.Type = TTypeMini.Primary;
        // 
        // btnDeselectAll
        // 
        btnDeselectAll.Location = new Point(456, 42);
        btnDeselectAll.Name = "btnDeselectAll";
        btnDeselectAll.Size = new Size(70, 30);
        btnDeselectAll.TabIndex = 2;
        btnDeselectAll.Text = "清空";
        // 
        // btnSelectEligible
        // 
        btnSelectEligible.Location = new Point(320, 42);
        btnSelectEligible.Name = "btnSelectEligible";
        btnSelectEligible.Size = new Size(130, 30);
        btnSelectEligible.TabIndex = 3;
        btnSelectEligible.Text = "自動選未同步";
        // 
        // btnSelectAll
        // 
        btnSelectAll.Location = new Point(244, 42);
        btnSelectAll.Name = "btnSelectAll";
        btnSelectAll.Size = new Size(70, 30);
        btnSelectAll.TabIndex = 4;
        btnSelectAll.Text = "全選";
        // 
        // txtSearch
        // 
        txtSearch.Location = new Point(16, 42);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "搜尋作品標題...";
        txtSearch.PrefixSvg = "SearchOutlined";
        txtSearch.Size = new Size(220, 30);
        txtSearch.TabIndex = 5;
        // 
        // lblHeader
        // 
        lblHeader.AutoSize = true;
        lblHeader.Font = new Font("Microsoft JhengHei UI", 11.5F, FontStyle.Bold);
        lblHeader.Location = new Point(16, 12);
        lblHeader.Name = "lblHeader";
        lblHeader.Size = new Size(153, 20);
        lblHeader.TabIndex = 6;
        lblHeader.Text = "選擇作者以檢視作品";
        // 
        // tableWorks
        // 
        tableWorks.AutoSizeColumnsMode = ColumnsMode.Fill;
        tableWorks.BorderColor = Color.FromArgb(220, 220, 220);
        tableWorks.Dock = DockStyle.Fill;
        tableWorks.EmptyText = "no data";
        tableWorks.EnableHeaderResizing = true;
        tableWorks.Gap = 12;
        tableWorks.Location = new Point(0, 100);
        tableWorks.Name = "tableWorks";
        tableWorks.RowHeight = 36;
        tableWorks.Size = new Size(690, 514);
        tableWorks.TabIndex = 0;
        // 
        // AuthorWorksListPanel
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(tableWorks);
        Controls.Add(toolbarPanel);
        Name = "AuthorWorksListPanel";
        Size = new Size(690, 614);
        toolbarPanel.ResumeLayout(false);
        toolbarPanel.PerformLayout();
        ResumeLayout(false);
    }
}

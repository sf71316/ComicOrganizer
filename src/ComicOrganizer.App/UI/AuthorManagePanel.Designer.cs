using AntdUI;

namespace ComicOrganizer.App.UI;

partial class AuthorManagePanel
{
    private System.ComponentModel.IContainer components = null;

    // 左：作者表
    private System.Windows.Forms.SplitContainer splitContainer;
    private System.Windows.Forms.Panel leftPanel;
    private System.Windows.Forms.Panel toolbarPanel;
    private System.Windows.Forms.Label lblTitle;
    private AntdUI.Input txtSearch;
    private AntdUI.Select selectFilter;
    private AntdUI.Button btnRefresh;
    private System.Windows.Forms.Label lblCount;
    private AntdUI.Table tableAuthors;

    // 右：作品清單
    private AuthorWorksListPanel worksPanel;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void  InitializeComponent()
    {
        splitContainer = new System.Windows.Forms.SplitContainer();
        leftPanel = new System.Windows.Forms.Panel();
        toolbarPanel = new System.Windows.Forms.Panel();
        lblTitle = new System.Windows.Forms.Label();
        txtSearch = new AntdUI.Input();
        selectFilter = new AntdUI.Select();
        btnRefresh = new AntdUI.Button();
        lblCount = new System.Windows.Forms.Label();
        tableAuthors = new AntdUI.Table();
        worksPanel = new AuthorWorksListPanel();

        ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        leftPanel.SuspendLayout();
        toolbarPanel.SuspendLayout();
        SuspendLayout();
        //
        // splitContainer
        //
        splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
        splitContainer.Orientation = System.Windows.Forms.Orientation.Vertical;
        splitContainer.SplitterDistance = 420;
        splitContainer.SplitterWidth = 4;
        splitContainer.Panel1.Controls.Add(leftPanel);
        splitContainer.Panel2.Controls.Add(worksPanel);
        //
        // leftPanel
        //
        leftPanel.Controls.Add(tableAuthors);
        leftPanel.Controls.Add(toolbarPanel);
        leftPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        //
        // toolbarPanel
        //
        toolbarPanel.Controls.Add(lblCount);
        toolbarPanel.Controls.Add(btnRefresh);
        toolbarPanel.Controls.Add(selectFilter);
        toolbarPanel.Controls.Add(txtSearch);
        toolbarPanel.Controls.Add(lblTitle);
        toolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
        toolbarPanel.Padding = new System.Windows.Forms.Padding(16, 12, 16, 8);
        toolbarPanel.Size = new System.Drawing.Size(540, 100);
        //
        // lblTitle
        //
        lblTitle.AutoSize = true;
        lblTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 11.5F, System.Drawing.FontStyle.Bold);
        lblTitle.Location = new System.Drawing.Point(16, 12);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new System.Drawing.Size(120, 21);
        lblTitle.Text = "作者管理";
        //
        // txtSearch
        //
        txtSearch.Location = new System.Drawing.Point(16, 42);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "搜尋作者主名 / 別名...";
        txtSearch.PrefixSvg = "SearchOutlined";
        txtSearch.Size = new System.Drawing.Size(220, 30);
        //
        // selectFilter
        //
        selectFilter.Location = new System.Drawing.Point(244, 42);
        selectFilter.Name = "selectFilter";
        selectFilter.PlaceholderText = "全部作者";
        selectFilter.Size = new System.Drawing.Size(160, 30);
        //
        // btnRefresh
        //
        btnRefresh.IconSvg = "ReloadOutlined";
        btnRefresh.Location = new System.Drawing.Point(412, 42);
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new System.Drawing.Size(95, 30);
        btnRefresh.Text = "重新整理";
        //
        // lblCount
        //
        lblCount.AutoSize = true;
        lblCount.Location = new System.Drawing.Point(16, 78);
        lblCount.Name = "lblCount";
        lblCount.Size = new System.Drawing.Size(60, 17);
        lblCount.Text = "共 0 位";
        //
        // tableAuthors
        //
        tableAuthors.AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill;
        tableAuthors.BorderColor = System.Drawing.Color.FromArgb(220, 220, 220);
        tableAuthors.Dock = System.Windows.Forms.DockStyle.Fill;
        tableAuthors.EnableHeaderResizing = true;
        tableAuthors.RowHeight = 36;
        //
        // worksPanel
        //
        worksPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        //
        // AuthorManagePanel
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(splitContainer);
        Name = "AuthorManagePanel";
        Padding = new System.Windows.Forms.Padding(0);
        Size = new System.Drawing.Size(1100, 614);

        toolbarPanel.ResumeLayout(false);
        toolbarPanel.PerformLayout();
        leftPanel.ResumeLayout(false);
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        ResumeLayout(false);
    }
}

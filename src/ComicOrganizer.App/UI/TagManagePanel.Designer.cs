using AntdUI;

namespace ComicOrganizer.App.UI;

partial class TagManagePanel
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Panel toolbarPanel;
    private System.Windows.Forms.Label lblTitle;
    private AntdUI.Input txtSearch;
    private AntdUI.Select selectCategory;
    private AntdUI.Button btnRefresh;
    private System.Windows.Forms.Label lblCount;

    private AntdUI.Table tableTags;

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
        toolbarPanel = new System.Windows.Forms.Panel();
        lblTitle = new System.Windows.Forms.Label();
        txtSearch = new AntdUI.Input();
        selectCategory = new AntdUI.Select();
        btnRefresh = new AntdUI.Button();
        lblCount = new System.Windows.Forms.Label();
        tableTags = new AntdUI.Table();

        toolbarPanel.SuspendLayout();
        SuspendLayout();
        //
        // toolbarPanel
        //
        toolbarPanel.Controls.Add(lblCount);
        toolbarPanel.Controls.Add(btnRefresh);
        toolbarPanel.Controls.Add(selectCategory);
        toolbarPanel.Controls.Add(txtSearch);
        toolbarPanel.Controls.Add(lblTitle);
        toolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
        toolbarPanel.Location = new System.Drawing.Point(0, 0);
        toolbarPanel.Name = "toolbarPanel";
        toolbarPanel.Padding = new System.Windows.Forms.Padding(20, 15, 20, 10);
        toolbarPanel.Size = new System.Drawing.Size(1100, 90);
        toolbarPanel.TabIndex = 0;
        //
        // lblTitle
        //
        lblTitle.AutoSize = true;
        lblTitle.Font = new System.Drawing.Font("Microsoft JhengHei UI", 13F, System.Drawing.FontStyle.Bold);
        lblTitle.Location = new System.Drawing.Point(20, 15);
        lblTitle.Name = "lblTitle";
        lblTitle.Size = new System.Drawing.Size(120, 25);
        lblTitle.TabIndex = 0;
        lblTitle.Text = "標籤管理";
        //
        // txtSearch
        //
        txtSearch.Location = new System.Drawing.Point(20, 50);
        txtSearch.Name = "txtSearch";
        txtSearch.PlaceholderText = "搜尋標籤名稱...";
        txtSearch.PrefixSvg = "SearchOutlined";
        txtSearch.Size = new System.Drawing.Size(280, 32);
        txtSearch.TabIndex = 1;
        //
        // selectCategory
        //
        selectCategory.Location = new System.Drawing.Point(310, 50);
        selectCategory.Name = "selectCategory";
        selectCategory.PlaceholderText = "全部分類";
        selectCategory.Size = new System.Drawing.Size(160, 32);
        selectCategory.TabIndex = 2;
        //
        // btnRefresh
        //
        btnRefresh.IconSvg = "ReloadOutlined";
        btnRefresh.Location = new System.Drawing.Point(480, 50);
        btnRefresh.Name = "btnRefresh";
        btnRefresh.Size = new System.Drawing.Size(100, 32);
        btnRefresh.TabIndex = 3;
        btnRefresh.Text = "重新整理";
        //
        // lblCount
        //
        lblCount.AutoSize = true;
        lblCount.Location = new System.Drawing.Point(600, 58);
        lblCount.Name = "lblCount";
        lblCount.Size = new System.Drawing.Size(60, 17);
        lblCount.TabIndex = 4;
        lblCount.Text = "共 0 個";
        //
        // tableTags
        //
        tableTags.BorderColor = System.Drawing.Color.FromArgb(220, 220, 220);
        tableTags.Dock = System.Windows.Forms.DockStyle.Fill;
        tableTags.EnableHeaderResizing = true;
        tableTags.Location = new System.Drawing.Point(0, 90);
        tableTags.Name = "tableTags";
        tableTags.RowHeight = 40;
        tableTags.Size = new System.Drawing.Size(1100, 524);
        tableTags.TabIndex = 1;
        //
        // TagManagePanel
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(tableTags);
        Controls.Add(toolbarPanel);
        Name = "TagManagePanel";
        Padding = new System.Windows.Forms.Padding(0);
        Size = new System.Drawing.Size(1100, 614);

        toolbarPanel.ResumeLayout(false);
        toolbarPanel.PerformLayout();
        ResumeLayout(false);
    }
}

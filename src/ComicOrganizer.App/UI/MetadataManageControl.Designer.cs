using AntdUI;

namespace ComicOrganizer.App.UI;

partial class MetadataManageControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Panel sidebarPanel;
    private AntdUI.Button btnNavTags;
    private AntdUI.Button btnNavAuthors;
    private AntdUI.Button btnNavTools;

    private System.Windows.Forms.Panel contentPanel;
    private TagManagePanel tagPanel;
    private AuthorManagePanel authorPanel;
    private MetadataToolsPanel toolsPanel;

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
        sidebarPanel = new AntdUI.Panel();
        btnNavTools = new AntdUI.Button();
        btnNavAuthors = new AntdUI.Button();
        btnNavTags = new AntdUI.Button();
        contentPanel = new System.Windows.Forms.Panel();
        tagPanel = new TagManagePanel();
        authorPanel = new AuthorManagePanel();
        toolsPanel = new MetadataToolsPanel();

        sidebarPanel.SuspendLayout();
        contentPanel.SuspendLayout();
        SuspendLayout();
        //
        // sidebarPanel
        //
        sidebarPanel.Controls.Add(btnNavTools);
        sidebarPanel.Controls.Add(btnNavAuthors);
        sidebarPanel.Controls.Add(btnNavTags);
        sidebarPanel.Dock = System.Windows.Forms.DockStyle.Left;
        sidebarPanel.Location = new System.Drawing.Point(0, 0);
        sidebarPanel.Name = "sidebarPanel";
        sidebarPanel.Padding = new System.Windows.Forms.Padding(15, 20, 15, 20);
        sidebarPanel.Radius = 0;
        sidebarPanel.Size = new System.Drawing.Size(180, 614);
        sidebarPanel.TabIndex = 0;
        //
        // btnNavTags
        //
        btnNavTags.Dock = System.Windows.Forms.DockStyle.Top;
        btnNavTags.IconSvg = "TagsOutlined";
        btnNavTags.Location = new System.Drawing.Point(15, 20);
        btnNavTags.Name = "btnNavTags";
        btnNavTags.Size = new System.Drawing.Size(150, 45);
        btnNavTags.TabIndex = 0;
        btnNavTags.Text = "標籤";
        btnNavTags.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnNavTags.Type = TTypeMini.Primary;
        //
        // btnNavAuthors
        //
        btnNavAuthors.Dock = System.Windows.Forms.DockStyle.Top;
        btnNavAuthors.IconSvg = "UserOutlined";
        btnNavAuthors.Location = new System.Drawing.Point(15, 65);
        btnNavAuthors.Name = "btnNavAuthors";
        btnNavAuthors.Size = new System.Drawing.Size(150, 45);
        btnNavAuthors.TabIndex = 1;
        btnNavAuthors.Text = "作者";
        btnNavAuthors.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // btnNavTools
        //
        btnNavTools.Dock = System.Windows.Forms.DockStyle.Top;
        btnNavTools.IconSvg = "ToolOutlined";
        btnNavTools.Location = new System.Drawing.Point(15, 110);
        btnNavTools.Name = "btnNavTools";
        btnNavTools.Size = new System.Drawing.Size(150, 45);
        btnNavTools.TabIndex = 2;
        btnNavTools.Text = "工具";
        btnNavTools.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // contentPanel
        //
        contentPanel.Controls.Add(toolsPanel);
        contentPanel.Controls.Add(authorPanel);
        contentPanel.Controls.Add(tagPanel);
        contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        contentPanel.Location = new System.Drawing.Point(180, 0);
        contentPanel.Name = "contentPanel";
        contentPanel.Size = new System.Drawing.Size(1100, 614);
        contentPanel.TabIndex = 1;
        //
        // tagPanel
        //
        tagPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        tagPanel.Location = new System.Drawing.Point(0, 0);
        tagPanel.Name = "tagPanel";
        tagPanel.Size = new System.Drawing.Size(1100, 614);
        tagPanel.TabIndex = 0;
        //
        // authorPanel
        //
        authorPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        authorPanel.Location = new System.Drawing.Point(0, 0);
        authorPanel.Name = "authorPanel";
        authorPanel.Size = new System.Drawing.Size(1100, 614);
        authorPanel.TabIndex = 1;
        authorPanel.Visible = false;
        //
        // toolsPanel
        //
        toolsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        toolsPanel.Location = new System.Drawing.Point(0, 0);
        toolsPanel.Name = "toolsPanel";
        toolsPanel.Size = new System.Drawing.Size(1100, 614);
        toolsPanel.TabIndex = 2;
        toolsPanel.Visible = false;
        //
        // MetadataManageControl
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        Controls.Add(contentPanel);
        Controls.Add(sidebarPanel);
        Name = "MetadataManageControl";
        Size = new System.Drawing.Size(1280, 614);

        sidebarPanel.ResumeLayout(false);
        contentPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}

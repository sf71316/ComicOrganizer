using AntdUI;

namespace ComicOrganizer.App.UI;

partial class MetadataManageControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Panel sidebarPanel;
    private AntdUI.Button btnNavTags;
    private AntdUI.Button btnNavAuthors;

    private System.Windows.Forms.Panel contentPanel;
    private TagManagePanel tagPanel;

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
        btnNavAuthors = new AntdUI.Button();
        btnNavTags = new AntdUI.Button();
        contentPanel = new System.Windows.Forms.Panel();
        tagPanel = new TagManagePanel();

        sidebarPanel.SuspendLayout();
        contentPanel.SuspendLayout();
        SuspendLayout();
        //
        // sidebarPanel
        //
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
        btnNavAuthors.Enabled = false;   // Phase 5 才啟用
        btnNavAuthors.IconSvg = "UserOutlined";
        btnNavAuthors.Location = new System.Drawing.Point(15, 65);
        btnNavAuthors.Name = "btnNavAuthors";
        btnNavAuthors.Size = new System.Drawing.Size(150, 45);
        btnNavAuthors.TabIndex = 1;
        btnNavAuthors.Text = "作者";
        btnNavAuthors.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // contentPanel
        //
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

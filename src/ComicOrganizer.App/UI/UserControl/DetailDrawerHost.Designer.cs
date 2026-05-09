namespace ComicOrganizer.App.UI;

partial class DetailDrawerHost
{
    private System.ComponentModel.IContainer components = null;

    private AuthorInfoPanel authorPanel;
    private WorkDetailPanel workPanel;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        authorPanel = new AuthorInfoPanel();
        workPanel = new WorkDetailPanel();

        SuspendLayout();

        //
        // authorPanel
        //
        authorPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        authorPanel.Name = "authorPanel";
        authorPanel.Visible = false;
        //
        // workPanel
        //
        workPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        workPanel.Name = "workPanel";
        workPanel.Visible = false;

        //
        // DetailDrawerHost
        //
        Controls.Add(workPanel);
        Controls.Add(authorPanel);
        Name = "DetailDrawerHost";
        Size = new System.Drawing.Size(380, 600);

        ResumeLayout(false);
    }
}

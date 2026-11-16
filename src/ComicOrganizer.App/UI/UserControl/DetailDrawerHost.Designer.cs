namespace ComicOrganizer.App.UI;

partial class DetailDrawerHost
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Panel leftResizeGrip;
    private AuthorInfoPanel authorPanel;
    private WorkDetailPanel workPanel;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        leftResizeGrip = new System.Windows.Forms.Panel();
        authorPanel = new AuthorInfoPanel();
        workPanel = new WorkDetailPanel();

        SuspendLayout();

        //
        // leftResizeGrip
        //
        leftResizeGrip.Cursor = System.Windows.Forms.Cursors.SizeWE;
        leftResizeGrip.Dock = System.Windows.Forms.DockStyle.Left;
        leftResizeGrip.Name = "leftResizeGrip";
        leftResizeGrip.Size = new System.Drawing.Size(5, 600);
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
        // (Dock.Fill 先加、Dock.Left 後加，確保 leftResizeGrip 永遠貼齊最左側且不被遮蓋)
        //
        Controls.Add(workPanel);
        Controls.Add(authorPanel);
        Controls.Add(leftResizeGrip);
        Name = "DetailDrawerHost";
        Size = new System.Drawing.Size(380, 600);

        ResumeLayout(false);
    }
}

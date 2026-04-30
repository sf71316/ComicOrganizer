namespace ComicOrganize.App.UI;

partial class ComicCardControl
{
    private System.ComponentModel.IContainer components = null;

    // rootPanel 改 Forms.Panel：拿掉 AntdUI 自繪 pipeline (圓角 anti-alias) 大幅減少捲動時 paint 成本
    // 視覺差異：卡片整體變直角，但 pic/typeStrip 圓角還在
    private System.Windows.Forms.Panel rootPanel;
    private AntdUI.Image3D pic;
    private System.Windows.Forms.Label lbl;
    private AntdUI.Panel hoverToolbar;
    private AntdUI.Button btnRead;
    private AntdUI.Button btnExplorer;

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
        rootPanel = new System.Windows.Forms.Panel();
        lbl = new Label();
        pic = new AntdUI.Image3D();
        hoverToolbar = new AntdUI.Panel();
        btnRead = new AntdUI.Button();
        btnExplorer = new AntdUI.Button();
        rootPanel.SuspendLayout();
        hoverToolbar.SuspendLayout();
        SuspendLayout();
        //
        // rootPanel (Forms.Panel — 沒 Radius / Back 屬性，靠 BackColor + 直角矩形)
        //
        rootPanel.BackColor = Color.White;
        rootPanel.Controls.Add(lbl);
        rootPanel.Controls.Add(pic);
        rootPanel.Controls.Add(hoverToolbar);
        rootPanel.Dock = DockStyle.Fill;
        rootPanel.Location = new Point(0, 0);
        rootPanel.Name = "rootPanel";
        rootPanel.Padding = new Padding(5);
        rootPanel.Size = new Size(160, 259);
        rootPanel.TabIndex = 0;
        // 
        // lbl
        // 
        lbl.BackColor = Color.Transparent;
        lbl.Cursor = Cursors.Hand;
        lbl.Dock = DockStyle.Bottom;
        lbl.Font = new Font("Microsoft JhengHei UI", 12F);
        lbl.Location = new Point(5, 205);
        lbl.Name = "lbl";
        lbl.Padding = new Padding(4);
        lbl.Size = new Size(150, 49);
        lbl.TabIndex = 0;
        lbl.TextAlign = ContentAlignment.TopCenter;
        // 
        // pic
        // 
        pic.BackColor = Color.Transparent;
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Location = new Point(5, 5);
        pic.Margin = new Padding(0, 10, 0, 0);
        pic.Name = "pic";
        pic.Radius = 8;
        pic.Size = new Size(150, 200);
        pic.TabIndex = 1;
        // 
        // hoverToolbar
        // 
        hoverToolbar.Back = Color.FromArgb(140, 127, 124, 128);
        hoverToolbar.BackColor = Color.Transparent;
        hoverToolbar.Controls.Add(btnRead);
        hoverToolbar.Controls.Add(btnExplorer);
        hoverToolbar.Location = new Point(0, 159);
        hoverToolbar.Name = "hoverToolbar";
        hoverToolbar.Radius = 10;
        hoverToolbar.Size = new Size(128, 41);
        hoverToolbar.TabIndex = 2;
        hoverToolbar.Visible = false;
        // 
        // btnRead
        // 
        btnRead.BackHover = Color.FromArgb(255, 255, 255);
        btnRead.Dock = DockStyle.Left;
        btnRead.ForeColor = Color.FromArgb(128, 54, 145);
        btnRead.Ghost = true;
        btnRead.IconRatio = 0.9F;
        btnRead.IconSvg = "BookOutlined";
        btnRead.Location = new Point(41, 0);
        btnRead.Name = "btnRead";
        btnRead.Size = new Size(41, 41);
        btnRead.TabIndex = 0;
        // 
        // btnExplorer
        // 
        btnExplorer.BackHover = Color.FromArgb(255, 255, 255);
        btnExplorer.Dock = DockStyle.Left;
        btnExplorer.ForeColor = Color.FromArgb(128, 54, 145);
        btnExplorer.Ghost = true;
        btnExplorer.IconRatio = 0.9F;
        btnExplorer.IconSvg = "FolderOpenOutlined";
        btnExplorer.Location = new Point(0, 0);
        btnExplorer.Name = "btnExplorer";
        btnExplorer.Size = new Size(41, 41);
        btnExplorer.TabIndex = 1;
        // 
        // ComicCardControl
        // 
        Controls.Add(rootPanel);
        DoubleBuffered = true;
        Name = "ComicCardControl";
        Size = new Size(160, 259);
        rootPanel.ResumeLayout(false);
        hoverToolbar.ResumeLayout(false);
        ResumeLayout(false);
    }
}

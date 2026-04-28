namespace ComicOrganize.App.UI;

partial class ComicCardControl
{
    private System.ComponentModel.IContainer components = null;

    private AntdUI.Panel rootPanel;
    private System.Windows.Forms.PictureBox pic;
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
        rootPanel = new AntdUI.Panel();
        pic = new System.Windows.Forms.PictureBox();
        lbl = new System.Windows.Forms.Label();
        hoverToolbar = new AntdUI.Panel();
        btnRead = new AntdUI.Button();
        btnExplorer = new AntdUI.Button();
        rootPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)pic).BeginInit();
        hoverToolbar.SuspendLayout();
        SuspendLayout();
        //
        // rootPanel — 卡片底（圓角 + 陰影）
        //
        // 加入順序決定 z-order：先加的在底層
        // hoverToolbar 最後加 → z-order 最高 → 蓋在 pic 下緣
        rootPanel.BackColor = Color.White;
        rootPanel.Dock = DockStyle.Fill;
        rootPanel.Name = "rootPanel";
        rootPanel.Radius = 0;       // 直角，避免 GDI Region 圓角鋸齒
        rootPanel.Shadow = 0;       // 不需陰影，避免動畫造成的視覺干擾
        rootPanel.Controls.Add(lbl);
        rootPanel.Controls.Add(pic);
        rootPanel.Controls.Add(hoverToolbar);
        //
        // pic — 封面
        //
        pic.BackColor = Color.FromArgb(245, 245, 245);
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Height = 200;
        pic.Name = "pic";
        pic.SizeMode = PictureBoxSizeMode.Zoom;
        pic.TabStop = false;
        //
        // lbl — 檔名（多行 wrap）
        //
        lbl.AutoSize = false;
        lbl.BackColor = Color.Transparent;
        lbl.Cursor = Cursors.Hand;
        lbl.Dock = DockStyle.Fill;
        lbl.Font = new Font("Microsoft JhengHei UI", 9F);
        lbl.Name = "lbl";
        lbl.Padding = new Padding(4, 4, 4, 4);
        lbl.TextAlign = ContentAlignment.TopCenter;
        //
        // hoverToolbar — 半透明浮動工具列（AntdUI.Panel 用 Back ARGB 自繪達到透明）
        //
        hoverToolbar.BackColor = Color.Transparent;
        hoverToolbar.Back = Color.FromArgb(140, 0, 0, 0);   // 黑 ~55% alpha
        hoverToolbar.Radius = 0;                            // 直角，避免 GDI Region 鋸齒
        // Dock=Left 順序：先加的佔最左 → btnRead 在最左、btnExplorer 在其右
        hoverToolbar.Controls.Add(btnRead);
        hoverToolbar.Controls.Add(btnExplorer);
        hoverToolbar.Location = new Point(0, 159);
        hoverToolbar.Name = "hoverToolbar";
        hoverToolbar.Size = new Size(128, 41);
        hoverToolbar.Visible = false;
        //
        // btnRead — 閱讀（用已驗證有效的 BookOutlined）
        //
        btnRead.Dock = DockStyle.Left;
        btnRead.IconSvg = "BookOutlined";
        btnRead.IconRatio = 0.9F;
        btnRead.Name = "btnRead";
        btnRead.Ghost = true;
        btnRead.ForeColor = Color.FromArgb(192, 182, 222);
        btnRead.BackHover= Color.FromArgb(255, 255, 255);   // 白色，覆蓋在 hoverToolbar 的半透明黑上
        btnRead.Size = new Size(41, 41);
        btnRead.TabIndex = 0;
        
        //btnRead.Type = AntdUI.TTypeMini.Primary;
        //
        // btnExplorer — 檔案總管
        //
        btnExplorer.Dock = DockStyle.Left;
        btnExplorer.IconSvg = "FolderOpenOutlined";
        btnExplorer.IconRatio = 0.9F;
        btnExplorer.Name = "btnExplorer";
        btnExplorer.Ghost = true;
        btnExplorer.ForeColor = Color.FromArgb(192, 182, 222);
        btnExplorer.BackHover = Color.FromArgb(255, 255, 255);   // 白色，覆蓋在 hoverToolbar 的半透明黑上
        btnExplorer.Size = new Size(41, 41);
        btnExplorer.TabIndex = 1;
        //btnExplorer.Type = AntdUI.TTypeMini.Primary;
        //
        // ComicCardControl
        //
        Controls.Add(rootPanel);
        Name = "ComicCardControl";
        Size = new Size(160, 240);
        rootPanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)pic).EndInit();
        hoverToolbar.ResumeLayout(false);
        ResumeLayout(false);
    }
}

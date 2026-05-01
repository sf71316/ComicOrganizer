namespace ComicOrganizer.App.UI;

partial class ComicCardControl
{
    private System.ComponentModel.IContainer components = null;

    // 拿掉 rootPanel：pic + lbl 直接掛 UserControl 自己。
    // 每張卡少 1 個 HWND（4 個 → 3 個），Add 時間預期掉 25%。
    // hover 狀態色直接設 this.BackColor。
    // hoverToolbar / btnRead / btnExplorer / typeStrip / typeIconLbl 仍 lazy 建立 (首次 hover 才創 HWND)
    // pic = AntdUI.Image3D 換回（試 PictureBox 後 Add 時間沒明顯改善，換回拿原生 anti-aliased 圓角）
    private AntdUI.Image3D pic;
    private System.Windows.Forms.Label lbl;

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
        lbl = new Label();
        pic = new AntdUI.Image3D();
        SuspendLayout();
        // 
        // lbl
        // 
        lbl.BackColor = Color.Transparent;
        lbl.Cursor = Cursors.Hand;
        lbl.Dock = DockStyle.Fill;
        lbl.Font = new Font("Microsoft JhengHei UI", 12F);
        lbl.Location = new Point(10, 210);
        lbl.Name = "lbl";
        lbl.Padding = new Padding(4);
        lbl.Size = new Size(140, 39);
        lbl.TabIndex = 0;
        lbl.TextAlign = ContentAlignment.TopCenter;
        // 
        // pic
        // 
        pic.BackColor = Color.Transparent;
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Duration = 0;
        pic.ImageFit = AntdUI.TFit.Contain;
        pic.Location = new Point(10, 10);
        pic.Name = "pic";
        pic.Radius = 8;
        pic.Size = new Size(140, 200);
        pic.TabIndex = 1;
        // 
        // ComicCardControl
        // 
        BackColor = Color.White;
        Controls.Add(lbl);
        Controls.Add(pic);
        DoubleBuffered = true;
        Name = "ComicCardControl";
        Padding = new Padding(10);
        Size = new Size(160, 259);
        ResumeLayout(false);
    }
}

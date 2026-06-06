namespace ComicOrganizer.App.UI;

partial class ComicCardControl
{
    private System.ComponentModel.IContainer components = null;

    // pic + lbl 直接掛 UserControl 自己 (不再包 borderPanel)，少 1 HWND/卡，
    // 大量卡片 fast scroll 時 WM_PAINT 數降回，避免捲動扭曲。
    // 圓角 + 1px 邊框 + bg 由 UserControl.OnPaint 自繪 (見 ComicCardControl.cs)。
    // hoverToolbar / btnRead / btnExplorer / typeStrip / typeIconLbl 仍 lazy 建立。
    private AntdUI.Image3D pic;
    private System.Windows.Forms.Label lbl;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            DisposeGdiResources();
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
        lbl.BackColor = Color.White;
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
        pic.Back = Color.White;
        pic.BackColor = Color.White;
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Duration = 0;
        pic.ImageFit = AntdUI.TFit.Contain;
        pic.Location = new Point(10, 10);
        pic.Name = "pic";
        pic.Radius = 8;
        // pic.Round 不要設 true — 那是「強制橢圓」，不是「啟用圓角」。
        // Image3D 的 Radius 在 Round=false 時即生效（rounded rect clip）。
        pic.Size = new Size(140, 200);
        pic.TabIndex = 1;
        // 
        // ComicCardControl
        // 
        Controls.Add(lbl);
        Controls.Add(pic);
        DoubleBuffered = true;
        Name = "ComicCardControl";
        Padding = new Padding(10);
        Size = new Size(160, 259);
        ResumeLayout(false);
    }
}

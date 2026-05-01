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
        lbl.BackColor = Color.White;   // 不透明 → scroll 走 BitBlt；hover 時 SetHoverState 改色
        lbl.Cursor = Cursors.Hand;
        lbl.Dock = DockStyle.Fill;
        lbl.Font = new Font("Microsoft JhengHei UI", 12F);
        lbl.Name = "lbl";
        lbl.Padding = new Padding(4);
        lbl.TabIndex = 0;
        lbl.TextAlign = ContentAlignment.TopCenter;
        //
        // pic
        //
        pic.BackColor = Color.White;   // 不透明 → scroll 時 WinForms 可走 BitBlt 快路徑，不全 invalidate
        pic.Back = Color.White;        // AntdUI 內部 fill (跟 BackColor 同色)
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Duration = 0;
        pic.ImageFit = AntdUI.TFit.Contain;
        pic.Name = "pic";
        pic.Radius = 8;
        pic.Size = new Size(140, 200);
        pic.TabIndex = 1;
        //
        // ComicCardControl
        //
        // BackColor 在 ctor 設成 ThemeColors.Surface 配合 parent；不在 Designer 寫死避免多餘賦值
        Controls.Add(lbl);
        Controls.Add(pic);
        DoubleBuffered = true;
        Name = "ComicCardControl";
        Padding = new Padding(10);          // 內距 — 卡邊到 pic/lbl
        Size = new Size(160, 259);
        ResumeLayout(false);
    }
}

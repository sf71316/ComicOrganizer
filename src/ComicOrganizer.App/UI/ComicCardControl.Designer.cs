namespace ComicOrganizer.App.UI;

partial class ComicCardControl
{
    private System.ComponentModel.IContainer components = null;

    // borderPanel = AntdUI.Panel 包整張卡，靠它畫圓角 + 1px 邊框 (BorderWidth + BorderColor)
    // 跟自己 OnPaint 比多 1 HWND/卡，但 GDI+ AA 邊框對齊問題交給 AntdUI 處理穩定。
    // pic + lbl 移進 borderPanel，UserControl 只當 sizing/events host。
    // hoverToolbar / btnRead / btnExplorer / typeStrip / typeIconLbl 仍 lazy 建立 (首次 hover 才創 HWND)
    private AntdUI.Panel borderPanel;
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
        borderPanel = new AntdUI.Panel();
        lbl = new Label();
        pic = new AntdUI.Image3D();
        borderPanel.SuspendLayout();
        SuspendLayout();
        //
        // lbl
        //
        lbl.BackColor = Color.Transparent;
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
        pic.BackColor = Color.Transparent;
        pic.Back = Color.White;
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Duration = 0;
        pic.ImageFit = AntdUI.TFit.Contain;
        pic.Name = "pic";
        pic.Radius = 8;
        pic.Size = new Size(140, 200);
        pic.TabIndex = 1;
        //
        // borderPanel — 圓角 + 1px 邊框；pic + lbl 都在這層
        //
        borderPanel.BackColor = Color.Transparent;
        borderPanel.Back = Color.White;     // 卡片底色，hover 時動態切 Secondary
        borderPanel.Radius = 8;
        borderPanel.BorderWidth = 1;
        borderPanel.BorderColor = ComicOrganizer.App.Core.ThemeColors.Border;
        borderPanel.Dock = DockStyle.Fill;
        borderPanel.Padding = new Padding(0);
        borderPanel.Name = "borderPanel";
        borderPanel.Controls.Add(lbl);
        borderPanel.Controls.Add(pic);
        //
        // ComicCardControl
        //
        BackColor = Color.Transparent;
        Controls.Add(borderPanel);
        DoubleBuffered = true;
        Name = "ComicCardControl";
        Padding = new Padding(0);
        Size = new Size(160, 259);
        borderPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}

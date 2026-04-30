namespace ComicOrganize.App.UI;

partial class ComicCardControl
{
    private System.ComponentModel.IContainer components = null;

    // 拿掉 rootPanel：pic + lbl 直接掛 UserControl 自己。
    // 每張卡少 1 個 HWND（4 個 → 3 個），Add 時間預期掉 25%。
    // hover 狀態色直接設 this.BackColor。
    // hoverToolbar / btnRead / btnExplorer / typeStrip / typeIconLbl 仍 lazy 建立 (首次 hover 才創 HWND)
    private System.Windows.Forms.PictureBox pic;
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
        pic = new System.Windows.Forms.PictureBox();
        SuspendLayout();
        //
        // lbl (Dock=Fill 在 ApplyViewMode override 為 Fill；Designer 先給 Bottom)
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
        // pic (Forms.PictureBox — Zoom 模式等比縮放置中；Radius 屬性不存在，圓角不裁)
        //
        pic.BackColor = Color.Transparent;
        pic.Cursor = Cursors.Hand;
        pic.Dock = DockStyle.Top;
        pic.Name = "pic";
        pic.SizeMode = PictureBoxSizeMode.Zoom;
        pic.Size = new Size(140, 200);
        pic.TabIndex = 1;
        //
        // ComicCardControl
        //
        BackColor = Color.White;
        Padding = new Padding(10);
        Controls.Add(lbl);
        Controls.Add(pic);
        DoubleBuffered = true;
        Name = "ComicCardControl";
        Size = new Size(160, 259);
        ResumeLayout(false);
    }
}

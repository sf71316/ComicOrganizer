using System.Drawing.Drawing2D;

namespace ComicOrganize.App.UI;

public partial class ComicCardControl : UserControl
{
    public string ItemPath { get; private set; } = "";
    public bool IsFolderNode { get; private set; }

    public event Action<string>? RequestOpenFolder;
    public event Action<string>? RequestOpenViewer;
    public event Action<string, bool>? RequestNewTab;
    public event Action<string>? RequestExplorer;

    public ComicCardControl()
    {
        InitializeComponent();
        this.BackColor = Color.Transparent;
        
        // 把 hoverToolbar reparent 到 pic 之下，讓 alpha-blend 真的疊到封面圖上
        // （Designer 維持加在 rootPanel，方便視覺編輯時看見元件）
        if (hoverToolbar.Parent != pic)
        {
            hoverToolbar.Parent?.Controls.Remove(hoverToolbar);
            pic.Controls.Add(hoverToolbar);

            // pic 是 PictureBox（非 ContainerControl），AutoScale chain 斷在這。
            // 補一次 Scale → hoverToolbar 跟所有子 button 的 Size/Location/Font 都自動套 DPI。
            if (this.DeviceDpi != 96)
            {
                float factor = this.DeviceDpi / 96f;
                hoverToolbar.Scale(new SizeF(factor, factor));
            }
        }

        WireUp();
    }

    private void WireUp()
    {
        // hover 偵測：只改 Back 色當回饋，Shadow 固定（避免 AntdUI Shadow 漸變動畫造成「陰影飄離」視覺）
        Action<bool> setHover = show =>
        {
            if (show)
            {
                hoverToolbar.Visible = true;
                hoverToolbar.BringToFront();   // 確保 z-order 在最上、不被 pic 蓋掉
                rootPanel.Back = ComicOrganize.App.Core.ThemeColors.Secondary;
            }
            else
            {
                var pos = this.PointToClient(Control.MousePosition);
                if (!this.ClientRectangle.Contains(pos))
                {
                    hoverToolbar.Visible = false;
                    rootPanel.Back = ComicOrganize.App.Core.ThemeColors.Surface;
                }
            }
        };

        EventHandler enter = (s, e) => setHover(true);
        EventHandler leave = (s, e) => setHover(false);
        foreach (var c in new Control[] { this, rootPanel, pic, lbl, hoverToolbar, btnRead, btnExplorer })
        {
            c.MouseEnter += enter;
            c.MouseLeave += leave;
        }

        // 點擊
        MouseEventHandler clickHandler = (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                if (IsFolderNode) RequestOpenFolder?.Invoke(ItemPath);
                else RequestOpenViewer?.Invoke(ItemPath);
            }
            else if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu();
            }
        };
        pic.MouseClick += clickHandler;
        lbl.MouseClick += clickHandler;
        rootPanel.MouseClick += clickHandler;

        // 工具列按鈕
        btnRead.Click += (s, e) =>
        {
            if (IsFolderNode) RequestOpenFolder?.Invoke(ItemPath);
            else RequestOpenViewer?.Invoke(ItemPath);
        };
        btnExplorer.Click += (s, e) => RequestExplorer?.Invoke(ItemPath);

        // PictureBox 圓角裁切（Resize 後重建）
        pic.Resize += (s, e) => UpdatePicCornerRegion();
    }

    public void SetData(FileSystemInfo item, bool isFolderNode)
    {
        ItemPath = item.FullName;
        IsFolderNode = isFolderNode;
        lbl.Text = item.Name;

        if (isFolderNode)
        {
            pic.Image = null;
            pic.BackColor = Color.FromArgb(230, 244, 255);
        }
    }

    public void SetCover(Image? cover)
    {
        if (cover != null) pic.Image = cover;
    }

    public void ApplyViewMode(GalleryControl.GalleryViewMode mode)
    {
        bool isList = mode == GalleryControl.GalleryViewMode.List;
        Size cardSize;
        int picHeight;
        float fontSize;

        switch (mode)
        {
            case GalleryControl.GalleryViewMode.Small:
                cardSize = new Size(120, 200);
                picHeight = 145;
                fontSize = 8.5F;
                break;
            case GalleryControl.GalleryViewMode.Large:
                cardSize = new Size(240, 400);
                picHeight = 310;
                fontSize = 10F;
                break;
            case GalleryControl.GalleryViewMode.List:
                cardSize = new Size((this.Parent?.ClientSize.Width ?? 600) - 40, 60);
                picHeight = 60;
                fontSize = 9F;
                break;
            case GalleryControl.GalleryViewMode.Medium:
            default:
                cardSize = new Size(160, 280);
                picHeight = 200;
                fontSize = 9F;
                break;
        }

        this.Size = cardSize;
        this.Margin = isList ? new Padding(5, 2, 5, 2) : new Padding(12);

        if (isList)
        {
            pic.Dock = DockStyle.Left;
            pic.Width = 50;
            pic.Height = 60;
            lbl.Dock = DockStyle.Fill;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Padding = new Padding(10, 0, 0, 0);
            hoverToolbar.Visible = false;
        }
        else
        {
            pic.Dock = DockStyle.Top;
            pic.Height = picHeight;
            lbl.Dock = DockStyle.Fill;       // 占 pic 下方所有剩餘空間（取代 Bottom + 固定 49px）
            lbl.TextAlign = ContentAlignment.TopCenter;
            lbl.Padding = new Padding(4, 4, 4, 4);
        }

        lbl.AutoEllipsis = false;            // 不顯示截斷 ...
        lbl.Font = new Font("Microsoft JhengHei UI", fontSize);
        // rootPanel 改 Forms.Panel 後沒有 Radius / Shadow 屬性 — 純直角矩形

        // hoverToolbar 浮在 pic 下緣中央（pic 內座標、直角矩形）
        // Size 已在 ctor 透過 hoverToolbar.Scale 套 DPI，這裡只算 layout
        if (!isList)
        {
            // 兩顆 Dock=Left 的 button 在 toolbar 內水平置中：依當前寬度動態算 Padding
            int totalBtnWidth = btnRead.Width + btnExplorer.Width;
            int sidePad = Math.Max(0, (hoverToolbar.Width - totalBtnWidth) / 2);
            hoverToolbar.Padding = new Padding(sidePad, 0, sidePad, 0);

            // toolbar 整體在 pic 下緣居中
            int bottomGap = LogicalToDeviceUnits(8);
            hoverToolbar.Location = new Point(
                (pic.Width - hoverToolbar.Width) / 2,
                pic.Height - hoverToolbar.Height - bottomGap
            );
            hoverToolbar.Region = null;
        }

        UpdatePicCornerRegion();
    }

    private void UpdatePicCornerRegion()
    {
        // 卡片改直角（避免 GDI Region 鋸齒），pic 不再裁圓角
        pic.Region = null;
    }


    private void ShowContextMenu()
    {
        var menuItems = new List<AntdUI.IContextMenuStripItem>();
        if (IsFolderNode) menuItems.Add(new AntdUI.ContextMenuStripItem("📂 進入目錄"));
        else menuItems.Add(new AntdUI.ContextMenuStripItem("👁 閱讀"));
        menuItems.Add(new AntdUI.ContextMenuStripItem("另開頁籤"));
        menuItems.Add(new AntdUI.ContextMenuStripItemDivider());
        menuItems.Add(new AntdUI.ContextMenuStripItem("📂 在檔案總管中開啟"));

        AntdUI.ContextMenuStrip.open(this, menuItem =>
        {
            if (menuItem.Text != null && (menuItem.Text.Contains("進入") || menuItem.Text.Contains("閱讀")))
            {
                if (IsFolderNode) RequestOpenFolder?.Invoke(ItemPath);
                else RequestOpenViewer?.Invoke(ItemPath);
            }
            else if (menuItem.Text == "另開頁籤")
            {
                RequestNewTab?.Invoke(ItemPath, true);
            }
            else if (menuItem.Text != null && menuItem.Text.Contains("檔案總管"))
            {
                RequestExplorer?.Invoke(ItemPath);
            }
        }, menuItems.ToArray());
    }
}

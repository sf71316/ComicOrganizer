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

    // Per-instance ToolTip：static 共用會脫離 form 生命週期，AntdUI.Panel 也不一定 raise WinForms hover
    private readonly System.Windows.Forms.ToolTip _tooltip = new()
    {
        AutoPopDelay = 8000,
        InitialDelay = 400,
        ReshowDelay = 150,
        ShowAlways = true,
    };

    // typeStrip 從 sibling 改成 pic 上的半透明 overlay：pic 不被推下、圓角不被遮，
    // 卡片整體一個 rounded box，typeStrip 是浮在封面圖頂的 24px 半透明 header
    private readonly AntdUI.Panel typeStrip = new()
    {
        Dock = DockStyle.Top,
        Height = 24,
        BackColor = Color.Transparent,           // Forms 層不繪 — alpha 才能透出 pic 的圖
        Back = Color.FromArgb(120, 0, 0, 0),     // 47% 黑半透明（封面圖在後面透出）
        Radius = 8,
        RadiusAlign = AntdUI.TAlignRound.Top,
    };
    private readonly System.Windows.Forms.Label typeIconLbl = new()
    {
        Dock = DockStyle.Left,
        Width = 28,
        Text = "📄",
        Font = new Font("Segoe UI Emoji", 11F),
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.Transparent,
        ForeColor = Color.White,                // 半透明黑底配白字（emoji color font 仍照系統色，但設了不吃虧）
    };

    public ComicCardControl()
    {
        InitializeComponent();
        this.BackColor = Color.Transparent;

        // hoverToolbar + typeStrip 都 reparent 到 pic 之下：
        //  - hoverToolbar：alpha-blend 疊到封面圖上
        //  - typeStrip：浮在封面圖頂，半透明 header
        // （Designer 維持把 hoverToolbar 加在 rootPanel，方便視覺編輯時看見；typeStrip 純 inline）
        typeStrip.Controls.Add(typeIconLbl);
        if (hoverToolbar.Parent != pic)
        {
            hoverToolbar.Parent?.Controls.Remove(hoverToolbar);
            pic.Controls.Add(hoverToolbar);
        }
        pic.Controls.Add(typeStrip);
        typeStrip.BringToFront();           // Dock=Top 在 pic 內最上方
        hoverToolbar.BringToFront();        // hoverToolbar 在 typeStrip 之上（hover 時兩者都顯示）

        // pic 是 Image3D（非 ContainerControl），AutoScale chain 斷在這。
        // 補一次 Scale → typeStrip / hoverToolbar 跟內部子控件 Size/Location/Font 都自動套 DPI。
        if (this.DeviceDpi != 96)
        {
            float factor = this.DeviceDpi / 96f;
            hoverToolbar.Scale(new SizeF(factor, factor));
            typeStrip.Scale(new SizeF(factor, factor));
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
                    rootPanel.Back = Color.White;  // 離開恢復白底（不是 Surface 淺紫，那會跟父 flowLayout 同色看不出邊界）
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

        // typeStrip 左側小 icon 區分檔案/目錄
        typeIconLbl.Text = isFolderNode ? "📁" : "📄";

        // Tooltip 兜底：lbl wrap 截斷時 hover 仍看得到完整檔名
        _tooltip.SetToolTip(rootPanel, item.Name);
        _tooltip.SetToolTip(lbl, item.Name);
        _tooltip.SetToolTip(typeStrip, item.Name);

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
            // lbl 留 ~60px (2 行 12pt + padding) — 超過就截，hover Tooltip 顯示完整
            case GalleryControl.GalleryViewMode.Small:
                cardSize = new Size(160, 270);
                picHeight = 200;
                fontSize = 12F;
                break;
            case GalleryControl.GalleryViewMode.Large:
                cardSize = new Size(320, 470);
                picHeight = 400;
                fontSize = 12F;
                break;
            case GalleryControl.GalleryViewMode.List:
                cardSize = new Size((this.Parent?.ClientSize.Width ?? 600) - 40, 60);
                picHeight = 60;
                fontSize = 12F;
                break;
            case GalleryControl.GalleryViewMode.Medium:
            default:
                cardSize = new Size(240, 370);
                picHeight = 300;
                fontSize = 12F;
                break;
        }

        this.Size = cardSize;
        this.Margin = isList ? new Padding(5, 2, 5, 2) : new Padding(12);

        // List mode 高度只 60px 放不下 24px typeStrip → 隱藏
        typeStrip.Visible = !isList;

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

        lbl.AutoSize = false;                // 確保 Dock=Fill 控制 size，不被 AutoSize 縮回單行
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

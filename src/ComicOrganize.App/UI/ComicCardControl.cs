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

    // 手動觸發 AntdUI.Tooltip.open + 自管 delay：
    // TooltipComponent.SetTip 對 AntdUI.Panel 不可靠（AntdUI 自繪 control 的 hover events 不一定走 IExtenderProvider），
    // 改用 hover→Timer→Tooltip.open 模式：1 秒內 leave 取消，不顯示；stay 1s+ 才顯示
    private string _itemName = "";
    private readonly System.Windows.Forms.Timer _tooltipDelayTimer = new() { Interval = 2000 };
    private bool _tooltipShown = false;

    // typeStrip 從 sibling 改成 pic 上的半透明 overlay：pic 不被推下、圓角不被遮。
    // 改 hover-only（跟 hoverToolbar 一起 toggle）：平常不繪製，省下 alpha-blend 5760px / pipeline cost。
    // 平常檔案/目錄區分靠 pic 本身（檔案 = 封面、目錄 = 資料夾 SVG icon）；hover 才秀精確 emoji。
    private readonly AntdUI.Panel typeStrip = new()
    {
        Dock = DockStyle.Top,
        Height = 24,
        BackColor = Color.Transparent,           // Forms 層不繪 — alpha 才能透出 pic 的圖
        Back = Color.FromArgb(120, 0, 0, 0),     // 47% 黑半透明（封面圖在後面透出）
        Radius = 8,
        RadiusAlign = AntdUI.TAlignRound.Top,
        Visible = false,                          // 跟 hoverToolbar 一樣 hover-only
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

        // pic 的 BackColor 改 Transparent — Designer 預設 (245,245,245) 淺灰會在
        // hover 時（rootPanel.Back=Secondary 紫）讓 image 圓角外的矩形區出現灰色小區塊
        pic.BackColor = Color.Transparent;

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

        // tooltip delay timer：到時才開 tooltip，期間若 leave 已 Stop 取消
        _tooltipDelayTimer.Tick += (s, e) =>
        {
            _tooltipDelayTimer.Stop();
            if (!string.IsNullOrEmpty(_itemName) && !_tooltipShown)
            {
                AntdUI.Tooltip.open(rootPanel, _itemName, AntdUI.TAlign.Bottom);
                _tooltipShown = true;
            }
        };

        WireUp();
    }

    private void WireUp()
    {
        // hover 偵測：只改 Back 色當回饋，Shadow 固定（避免 AntdUI Shadow 漸變動畫造成「陰影飄離」視覺）
        // Tooltip 用 delay timer 防抖：enter 啟動 timer、leave 取消 + 關 tooltip
        Action<bool> setHover = show =>
        {
            if (show)
            {
                typeStrip.Visible = true;
                hoverToolbar.Visible = true;
                typeStrip.BringToFront();
                hoverToolbar.BringToFront();
                rootPanel.BackColor = ComicOrganize.App.Core.ThemeColors.Secondary;

                // 還沒顯示才需要重啟 timer（已顯示就不必，避免在子控件間移動時重啟動畫）
                if (!_tooltipShown && !_tooltipDelayTimer.Enabled)
                {
                    _tooltipDelayTimer.Start();
                }
            }
            else
            {
                var pos = this.PointToClient(Control.MousePosition);
                if (!this.ClientRectangle.Contains(pos))
                {
                    typeStrip.Visible = false;
                    hoverToolbar.Visible = false;
                    rootPanel.BackColor = Color.White;  // 離開恢復白底（不是 Surface 淺紫，那會跟父 flowLayout 同色看不出邊界）
                    _tooltipDelayTimer.Stop();      // 取消還沒 fire 的 delay
                    _tooltipShown = false;          // 真正離開卡片才 reset，下次 hover 才重新計時
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

        // Tooltip：存檔名給 delay timer fire 時用（手動 Tooltip.open）
        _itemName = item.Name;

        // 目錄/檔案類型已透過 typeStrip 的 📁/📄 區分，pic.BackColor 維持 Transparent
        // 沒封面的目錄卡片 pic 區會顯示 rootPanel.Back（白底/hover 紫底），typeStrip 的 📁 提示這是目錄
    }

    public void SetCover(Image? cover)
    {
        if (cover != null) pic.Image = cover;
    }


    public void ApplyViewMode(GalleryControl.GalleryViewMode mode)
    {
        // List 模式不會建 ComicCardControl（GalleryControl.LoadCurrentViewMode 走 LoadGalleryListAsync 用 AntdUI.Table），
        // 所以 ApplyViewMode 只需處理 Small/Medium/Large 三個卡片模式
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
            case GalleryControl.GalleryViewMode.Medium:
            default:
                cardSize = new Size(240, 370);
                picHeight = 300;
                fontSize = 12F;
                break;
        }

        this.Size = cardSize;
        this.Margin = new Padding(12);

        pic.Dock = DockStyle.Top;
        pic.Height = picHeight;
        lbl.Dock = DockStyle.Fill;
        lbl.TextAlign = ContentAlignment.TopCenter;
        lbl.Padding = new Padding(4, 4, 4, 4);

        lbl.AutoSize = false;                // 確保 Dock=Fill 控制 size，不被 AutoSize 縮回單行
        lbl.AutoEllipsis = false;            // 不顯示截斷 ...
        lbl.Font = new Font("Microsoft JhengHei UI", fontSize);

        // hoverToolbar 浮在 pic 下緣中央（pic 內座標、直角矩形）
        // Size 已在 ctor 透過 hoverToolbar.Scale 套 DPI，這裡只算 layout
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

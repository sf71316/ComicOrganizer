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

    // hoverToolbar / typeStrip / btnRead / btnExplorer / typeIconLbl 全部 lazy 建立：
    // 首次 hover 才 new + Add 到 pic.Controls，HWND 才實體化。
    // 大量卡片捲動進場時，每張卡的 Controls.Add 從 ~6.5ms (9 HWNDs) 降到 ~3ms (4 HWNDs)
    private AntdUI.Panel? hoverToolbar;
    private AntdUI.Button? btnRead;
    private AntdUI.Button? btnExplorer;
    private AntdUI.Panel? typeStrip;
    private System.Windows.Forms.Label? typeIconLbl;
    private bool _hoverControlsCreated = false;

    public ComicCardControl()
    {
        InitializeComponent();
        // BackColor = White / Padding(10) 已在 Designer 設定（取代以前 rootPanel）
        // pic.BackColor = Transparent 在 Designer 設定 — 讓 hover 時 this.BackColor=Secondary 透出

        // tooltip delay timer：到時才開 tooltip，期間若 leave 已 Stop 取消
        _tooltipDelayTimer.Tick += (s, e) =>
        {
            _tooltipDelayTimer.Stop();
            if (!string.IsNullOrEmpty(_itemName) && !_tooltipShown)
            {
                AntdUI.Tooltip.open(this, _itemName, AntdUI.TAlign.Bottom);
                _tooltipShown = true;
            }
        };

        WireUp();
    }

    /// <summary>
    /// 首次 hover 才建立 typeStrip + hoverToolbar + 內部 buttons。
    /// 之前在 ctor 建會讓每張卡 Controls.Add 進 container 時實體化 9 個 HWND（含此 5 個）。
    /// 改 lazy 後平常只有 4 個 HWND（this + rootPanel + pic + lbl），首次 hover 補建剩下 5 個。
    /// </summary>
    private void EnsureHoverControls()
    {
        if (_hoverControlsCreated) return;
        _hoverControlsCreated = true;

        typeStrip = new AntdUI.Panel
        {
            Dock = DockStyle.Top,
            Height = 24,
            BackColor = Color.Transparent,
            Back = Color.FromArgb(120, 0, 0, 0),
            Radius = 8,
            RadiusAlign = AntdUI.TAlignRound.Top,
            Visible = false,
        };
        typeIconLbl = new System.Windows.Forms.Label
        {
            Dock = DockStyle.Left,
            Width = 28,
            Text = IsFolderNode ? "📁" : "📄",
            Font = new Font("Segoe UI Emoji", 11F),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            ForeColor = Color.White,
        };
        typeStrip.Controls.Add(typeIconLbl);

        hoverToolbar = new AntdUI.Panel
        {
            Back = Color.FromArgb(140, 127, 124, 128),
            BackColor = Color.Transparent,
            Radius = 10,
            Size = new Size(128, 41),
            Visible = false,
        };
        btnRead = new AntdUI.Button
        {
            BackHover = Color.FromArgb(255, 255, 255),
            Dock = DockStyle.Left,
            ForeColor = Color.FromArgb(128, 54, 145),
            Ghost = true,
            IconRatio = 0.9F,
            IconSvg = "ReadOutlined",
            Size = new Size(41, 41),
        };
        btnExplorer = new AntdUI.Button
        {
            BackHover = Color.FromArgb(255, 255, 255),
            Dock = DockStyle.Left,
            ForeColor = Color.FromArgb(128, 54, 145),
            Ghost = true,
            IconRatio = 0.9F,
            IconSvg = "FolderOpenOutlined",
            Size = new Size(41, 41),
        };
        hoverToolbar.Controls.Add(btnRead);
        hoverToolbar.Controls.Add(btnExplorer);

        pic.Controls.Add(hoverToolbar);
        pic.Controls.Add(typeStrip);
        typeStrip.BringToFront();
        hoverToolbar.BringToFront();

        // pic 是 Image3D（非 ContainerControl），AutoScale chain 斷在這。補一次 Scale 套 DPI。
        if (this.DeviceDpi != 96)
        {
            float factor = this.DeviceDpi / 96f;
            hoverToolbar.Scale(new SizeF(factor, factor));
            typeStrip.Scale(new SizeF(factor, factor));
        }

        // 套用 hoverToolbar 在 pic 內的 layout（依當前 pic 尺寸）
        ApplyHoverToolbarLayout();

        // 對 hover 控件本身 hook MouseEnter/Leave 維持 hover 狀態（mouse 在 toolbar 上不算 leave）
        HookHover(hoverToolbar);
        HookHover(btnRead);
        HookHover(btnExplorer);

        // 📖 「閱讀」按鈕一律呼叫 viewer（不管 folder 還是 file）：
        // viewer 對目錄會挑首張圖開啟。「進入目錄」操作交給卡片左鍵點擊 / 縮圖點擊
        btnRead.Click += (s, e) => RequestOpenViewer?.Invoke(ItemPath);
        btnExplorer.Click += (s, e) => RequestExplorer?.Invoke(ItemPath);
    }

    /// <summary>
    /// 在 control 上 hook MouseEnter/Leave 連到 SetHoverState。
    /// 平常 control（this/pic/lbl）跟 hover-only control（hoverToolbar/btnRead/btnExplorer）共用。
    /// </summary>
    private void HookHover(Control c)
    {
        c.MouseEnter += (s, e) => SetHoverState(true);
        c.MouseLeave += (s, e) => SetHoverState(false);
    }

    /// <summary>
    /// 卡片主要動作 dispatch：目錄 → 進入該目錄；檔案 → 開外部閱讀器。
    /// 卡片左鍵點擊 + ContextMenu「進入/閱讀」項都用此邏輯。
    /// </summary>
    private void OpenPrimary()
    {
        if (IsFolderNode) RequestOpenFolder?.Invoke(ItemPath);
        else RequestOpenViewer?.Invoke(ItemPath);
    }

    private void ApplyHoverToolbarLayout()
    {
        if (!_hoverControlsCreated || hoverToolbar == null || btnRead == null || btnExplorer == null) return;

        int totalBtnWidth = btnRead.Width + btnExplorer.Width;
        int sidePad = Math.Max(0, (hoverToolbar.Width - totalBtnWidth) / 2);
        hoverToolbar.Padding = new Padding(sidePad, 0, sidePad, 0);

        int bottomGap = LogicalToDeviceUnits(8);
        hoverToolbar.Location = new Point(
            (pic.Width - hoverToolbar.Width) / 2,
            pic.Height - hoverToolbar.Height - bottomGap
        );
        hoverToolbar.Region = null;
    }

    private void SetHoverState(bool show)
    {
        if (show)
        {
            EnsureHoverControls();   // 首次 hover 補建 controls
            typeStrip!.Visible = true;
            hoverToolbar!.Visible = true;
            typeStrip.BringToFront();
            hoverToolbar.BringToFront();
            this.BackColor = ComicOrganize.App.Core.ThemeColors.Secondary;

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
                if (typeStrip != null) typeStrip.Visible = false;
                if (hoverToolbar != null) hoverToolbar.Visible = false;
                this.BackColor = Color.White;
                _tooltipDelayTimer.Stop();
                _tooltipShown = false;
            }
        }
    }

    private void WireUp()
    {
        // 平常只在 3 個 control 上 hook hover：this / pic / lbl
        // hoverToolbar / btnRead / btnExplorer 的 hover 事件在 EnsureHoverControls 內補綁
        HookHover(this);
        HookHover(pic);
        HookHover(lbl);

        // 點擊
        MouseEventHandler clickHandler = (s, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenPrimary();
            else if (e.Button == MouseButtons.Right) ShowContextMenu();
        };
        pic.MouseClick += clickHandler;
        lbl.MouseClick += clickHandler;
        this.MouseClick += clickHandler;
    }

    public void SetData(FileSystemInfo item, bool isFolderNode)
    {
        ItemPath = item.FullName;
        IsFolderNode = isFolderNode;
        lbl.Text = item.Name;

        // typeIconLbl 還沒建（lazy）— EnsureHoverControls 建立時讀 IsFolderNode 設 emoji
        if (typeIconLbl != null) typeIconLbl.Text = isFolderNode ? "📁" : "📄";

        // Tooltip：存檔名給 delay timer fire 時用（手動 Tooltip.open）
        _itemName = item.Name;
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
            // 卡片高度 +10 (lbl 是 Dock=Fill，pic 高度不變 → lbl 自動 +10 多容納一行/留白)
            case GalleryControl.GalleryViewMode.Small:
                cardSize = new Size(170, 280);
                picHeight = 200;
                fontSize = 9F;
                break;
            case GalleryControl.GalleryViewMode.Large:
                cardSize = new Size(330, 480);
                picHeight = 400;
                fontSize = 9F;
                break;
            case GalleryControl.GalleryViewMode.Medium:
            default:
                cardSize = new Size(250, 380);
                picHeight = 300;
                fontSize = 9F;
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
        lbl.UseCompatibleTextRendering = true;  // GDI+ DrawString 對 CJK 無空格的情況可字元級斷行
        lbl.Font = new Font("Microsoft JhengHei UI", fontSize);

        // hoverToolbar layout 只在已建立時重算；lazy 模式下首次 hover 後 EnsureHoverControls 會呼叫一次
        ApplyHoverToolbarLayout();
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
                OpenPrimary();
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

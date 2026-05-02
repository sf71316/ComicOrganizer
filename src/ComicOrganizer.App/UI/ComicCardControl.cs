using AntdUI;
using ComicOrganizer.App.Core;
using System.Drawing.Drawing2D;

namespace ComicOrganizer.App.UI;

public partial class ComicCardControl : UserControl
{
    public string ItemPath { get; private set; } = "";
    public bool IsFolderNode { get; private set; }

    public event Action<string>? RequestOpenFolder;
    public event Action<string>? RequestOpenViewer;
    public event Action<string, bool>? RequestNewTab;
    public event Action<string>? RequestExplorer;
    public event Action<string>? RequestAuthorInfo;
    public event Action<string, ReadStatus>? RequestSetReadStatus;
    private const int CornerRadius = 8;
    private Color _currentBgColor = Color.White;
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
        // 不開 SupportsTransparentBackColor (BackColor=Transparent) — 那會讓 WinForms 在
        // parent scroll 時強制 invalidate 所有透明子控件，BitBlt 快路徑被取消，scroll 卡頓。
        // 改成 BackColor = parent (FlowLayoutPanel) 的 Surface 色：WM_ERASEBKGND 直接填 Surface，
        // OnPaint 在中央畫圓角白底；圓角外的「四角」剛好就是 Surface 色 = 視覺上跟 parent 連續。
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = ComicOrganizer.App.Core.ThemeColors.Surface;

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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    /// <summary>
    /// 二段法畫圓角邊框：圓角弧線走 AA、4 個直邊用 FillRectangle 走 pixel-perfect。
    /// 之前共用一條 AA path 的 stroke 方式對 right/bottom 邊有 GDI+ AA 對齊誤差。
    /// 直線改 FillRectangle 各邊保證精確 1px。
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 0 || Height <= 0) return;
        var g = e.Graphics;
        int r = CornerRadius;
        int d = r * 2;

        // === 步驟 1：圓角 bg fill (AA) ===
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bgPath = new GraphicsPath())
        {
            bgPath.AddArc(0, 0, d, d, 180, 90);
            bgPath.AddArc(Width - d, 0, d, d, 270, 90);
            bgPath.AddArc(Width - d, Height - d, d, d, 0, 90);
            bgPath.AddArc(0, Height - d, d, d, 90, 90);
            bgPath.CloseFigure();
            using var bgBrush = new SolidBrush(_currentBgColor);
            g.FillPath(bgBrush, bgPath);
        }

        // === 步驟 2：4 個圓角弧 stroke (AA，1px) ===
        var borderColor = ComicOrganizer.App.Core.ThemeColors.Border;
        using (var pen = new Pen(borderColor, 1))
        {
            g.DrawArc(pen, 0, 0, d, d, 180, 90);                         // top-left
            g.DrawArc(pen, Width - d - 1, 0, d, d, 270, 90);              // top-right
            g.DrawArc(pen, Width - d - 1, Height - d - 1, d, d, 0, 90);   // bottom-right
            g.DrawArc(pen, 0, Height - d - 1, d, d, 90, 90);              // bottom-left
        }

        // === 步驟 3：4 條直邊 (NO AA, pixel-perfect 1px) ===
        g.SmoothingMode = SmoothingMode.None;
        using (var brush = new SolidBrush(borderColor))
        {
            g.FillRectangle(brush, r, 0, Width - d, 1);                   // top
            g.FillRectangle(brush, r, Height - 1, Width - d, 1);          // bottom
            g.FillRectangle(brush, 0, r, 1, Height - d);                  // left
            g.FillRectangle(brush, Width - 1, r, 1, Height - d);          // right
        }
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
            _currentBgColor = ComicOrganizer.App.Core.ThemeColors.Secondary;
            lbl.BackColor = _currentBgColor;   // lbl 不透明，需手動同步 hover 色
            pic.BackColor = _currentBgColor;   // 同上 — pic.BackColor 矩形 fill，不同步會在 hover 看到白色矩形
            Invalidate();

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
                _currentBgColor = Color.White;
                lbl.BackColor = Color.White;
                pic.BackColor = Color.White;
                Invalidate();
                _tooltipDelayTimer.Stop();
                _tooltipShown = false;
            }
        }
    }

    private void WireUp()
    {
        // 平常 hook hover：this / pic / lbl
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

    /// <summary>
    /// 套用 #47 metadata（title 取代 filename、加作者副標）。view 為 null 或無資料時 fallback 到 SetData 設的檔名。
    /// 沒解析到 title 也保留檔名；只有 author 可秀就秀 "filename / by author"。
    /// </summary>
    public void SetMetadata(AssetMetadataView? view)
    {
        if (view == null) return;
        var displayTitle = !string.IsNullOrWhiteSpace(view.Title) ? view.Title : lbl.Text;
        if (!string.IsNullOrEmpty(view.AuthorMainName))
        {
            // 兩行：title \n — Author（em-dash 視覺區隔，同字級避免增加 layout）
            lbl.Text = $"{displayTitle}\n— {view.AuthorMainName}";
        }
        else if (!string.IsNullOrWhiteSpace(view.Title))
        {
            lbl.Text = view.Title;
        }
        // tooltip 用「真實 title + author」更有意義（hover delay timer fire 時用此值）
        _itemName = !string.IsNullOrEmpty(view.AuthorMainName)
            ? $"{displayTitle}\nby {view.AuthorMainName}"
            : displayTitle;
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
        menuItems.Add(new AntdUI.ContextMenuStripItem("👤 作者資訊"));
        menuItems.Add(new AntdUI.ContextMenuStripItemDivider());
        menuItems.Add(new AntdUI.ContextMenuStripItem("✅ 標為已完成"));
        menuItems.Add(new AntdUI.ContextMenuStripItem("⏹ 標為放棄"));
        menuItems.Add(new AntdUI.ContextMenuStripItem("↺ 重置為未讀"));

        AntdUI.ContextMenuStrip.open(this, menuItem =>
        {
            var t = menuItem.Text;
            if (t == null) return;
            if (t.Contains("進入") || t.Contains("閱讀"))
            {
                OpenPrimary();
            }
            else if (t == "另開頁籤")
            {
                RequestNewTab?.Invoke(ItemPath, true);
            }
            else if (t.Contains("檔案總管"))
            {
                RequestExplorer?.Invoke(ItemPath);
            }
            else if (t.Contains("作者") || t.Contains("Author"))
            {
                RequestAuthorInfo?.Invoke(ItemPath);
            }
            else if (t.Contains("已完成") || t.Contains("Completed"))
            {
                RequestSetReadStatus?.Invoke(ItemPath, ReadStatus.Completed);
            }
            else if (t.Contains("放棄") || t.Contains("Dropped"))
            {
                RequestSetReadStatus?.Invoke(ItemPath, ReadStatus.Dropped);
            }
            else if (t.Contains("未讀") || t.Contains("Unread"))
            {
                RequestSetReadStatus?.Invoke(ItemPath, ReadStatus.Unread);
            }
        }, menuItems.ToArray());
    }
}

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

    /// <summary>使用者按右鍵「詳細資訊」 — 觸發 WorkDetailPanel。</summary>
    public event Action<string>? RequestWorkDetail;

    /// <summary>使用者按右鍵「使用外部閱讀器/程式開啟」 — 漫畫格式走設定的外部 viewer，
    /// 非漫畫格式（PDF/EPUB 等）走系統預設程式。</summary>
    public event Action<string>? RequestOpenWithExternal;
    public event Action<string, ReadStatus>? RequestSetReadStatus;
    private const int CornerRadius = 8;
    private Color _currentBgColor = Color.White;
    // 手動觸發 AntdUI.Tooltip.open + 自管 delay：
    // TooltipComponent.SetTip 對 AntdUI.Panel 不可靠（AntdUI 自繪 control 的 hover events 不一定走 IExtenderProvider），
    // 改用 hover→Timer→Tooltip.open 模式：1 秒內 leave 取消，不顯示；stay 1s+ 才顯示
    private string _itemName = "";
    private readonly System.Windows.Forms.Timer _tooltipDelayTimer = new() { Interval = 2000 };
    private bool _tooltipShown = false;

    /// <summary>
    /// hover overlay 安全網 timer。MouseLeave 在 cursor 從子控件直接跨出卡片時偶爾不觸發，
    /// 這個 timer 在 hover 期間每 150ms polling cursor 位置，若已離開卡片則強制 hide。
    /// </summary>
    private readonly System.Windows.Forms.Timer _hoverPollTimer = new() { Interval = 150 };

    // hoverToolbar / typeStrip / btnRead / btnExplorer / typeIconLbl 全部 lazy 建立：
    // 首次 hover 才 new + Add 到 pic.Controls，HWND 才實體化。
    // 大量卡片捲動進場時，每張卡的 Controls.Add 從 ~6.5ms (9 HWNDs) 降到 ~3ms (4 HWNDs)
    private AntdUI.Panel? hoverToolbar;
    private AntdUI.Button? btnRead;
    private AntdUI.Button? btnExplorer;
    private AntdUI.Panel? typeStrip;
    private System.Windows.Forms.Label? typeIconLbl;
    private System.Windows.Forms.Label? readStatusLbl;   // typeStrip 右側的閱讀狀態 icon
    private bool _hoverControlsCreated = false;

    /// <summary>查詢卡片當前 read_status 的 callback（由 GalleryControl 注入）。</summary>
    public Func<string, ReadStatus?>? ReadStatusProvider { get; set; }
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

        // hover safety net：每 150ms 檢查 cursor 是否仍在卡片內，否則 force hide overlay
        _hoverPollTimer.Tick += (s, e) =>
        {
            if (IsDisposed) { _hoverPollTimer.Stop(); return; }
            // RectangleToScreen 把 client coords 轉 screen coords，比對 Cursor.Position（screen）
            if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position))
            {
                _hoverPollTimer.Stop();
                ForceHideHoverOverlay();
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
        // 改用 AntdUI 內建 SVG icon set + 各別著色（彩色 emoji 在 GDI/GDI+ 兩條 WinForms 路徑都不可靠）。
        // 每個 icon 單色，但顏色組合（黃資料夾、綠勾、紅停）視覺上接近彩色 emoji 的差異性
        typeIconLbl = new System.Windows.Forms.Label
        {
            Dock = DockStyle.Left,
            Width = 28,
            BackColor = Color.Transparent,
            ImageAlign = ContentAlignment.MiddleCenter,
            Image = RenderTypeIcon(IsFolderNode),
        };
        readStatusLbl = new System.Windows.Forms.Label
        {
            Dock = DockStyle.Right,
            Width = 28,
            BackColor = Color.Transparent,
            ImageAlign = ContentAlignment.MiddleCenter,
            // Image 由 RefreshReadStatusIcon 填
        };
        typeStrip.Controls.Add(readStatusLbl);
        typeStrip.Controls.Add(typeIconLbl);
        RefreshReadStatusIcon();

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
        // 重要：所有 hover 期間「會 visible」的子控件都要 hook，否則 cursor 直接從子控件跨出卡片時
        //      只有該子控件的 MouseLeave 觸發、其他人不會再觸發，造成 overlay 殘留
        HookHover(hoverToolbar);
        HookHover(btnRead);
        HookHover(btnExplorer);
        HookHover(typeStrip);
        HookHover(typeIconLbl);
        HookHover(readStatusLbl);

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

    /// <summary>讀 ReadStatusProvider 結果，更新 typeStrip 右側 status icon。GalleryControl 設完 status 後也呼叫一次。</summary>
    public void RefreshReadStatusIcon()
    {
        if (readStatusLbl == null) return;
        var status = ReadStatusProvider?.Invoke(ItemPath);
        var oldImg = readStatusLbl.Image;
        readStatusLbl.Image = RenderStatusIcon(status);
        if (oldImg != null && !ReferenceEquals(oldImg, readStatusLbl.Image)) oldImg.Dispose();
    }

    private const int IconSize = 16;
    private const int SuperSampleFactor = 3;   // SVG 在 16×16 太小 anti-alias 不夠 → 3× 渲染再 HQ 下縮

    // typeStrip 是半透明黑底（120,0,0,0），icon 顏色挑高飽和度的避免被吃進背景
    private static Image? RenderTypeIcon(bool isFolder)
    {
        return isFolder
            ? RenderSmoothSvg("FolderFilled", Color.FromArgb(255, 197, 0))      // amber 黃
            : RenderSmoothSvg("FileFilled", Color.FromArgb(255, 255, 255));
    }
    private static Image? RenderStatusIcon(ReadStatus? status)
    {
        return status switch
        {
            ReadStatus.Reading   => RenderSmoothSvg("EyeFilled",      Color.FromArgb(166, 204, 110)),
            ReadStatus.Completed => RenderSmoothSvg("CheckCircleFilled", Color.FromArgb(82, 196, 26)),
            ReadStatus.Dropped   => RenderSmoothSvg("StopFilled",        Color.FromArgb(245, 34, 45)),
            _ => null,
        };
    }

    /// <summary>
    /// SVG → 高品質 Bitmap：先以 IconSize × SuperSampleFactor 渲染，再用 HighQualityBicubic
    /// 縮回 IconSize 取得平滑邊緣。直接在 16×16 渲染 anti-alias 樣本太少邊緣會鋸齒。
    /// </summary>
    private static Bitmap? RenderSmoothSvg(string key, Color color)
    {
        int big = IconSize * SuperSampleFactor;
        using var hi = AntdUI.SvgExtend.SvgToBmp(key, big, big, color);
        if (hi == null) return null;
        var bmp = new Bitmap(IconSize, IconSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(hi, new Rectangle(0, 0, IconSize, IconSize));
        return bmp;
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
            // 啟動 polling 安全網（防 MouseLeave 漏觸發殘留 overlay）
            if (!_hoverPollTimer.Enabled) _hoverPollTimer.Start();
        }
        else
        {
            var pos = this.PointToClient(Control.MousePosition);
            if (!this.ClientRectangle.Contains(pos))
            {
                ForceHideHoverOverlay();
            }
        }
    }

    /// <summary>不檢查 cursor 位置，直接收掉 hover overlay。Timer 安全網與 MouseLeave 都會呼叫。</summary>
    private void ForceHideHoverOverlay()
    {
        if (typeStrip != null) typeStrip.Visible = false;
        if (hoverToolbar != null) hoverToolbar.Visible = false;
        _currentBgColor = Color.White;
        lbl.BackColor = Color.White;
        pic.BackColor = Color.White;
        Invalidate();
        _tooltipDelayTimer.Stop();
        _tooltipShown = false;
        _hoverPollTimer.Stop();
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
        // 強制走外部開啟 — 漫畫格式用設定的 image viewer；其他格式（PDF/EPUB 等）走系統預設程式
        var externalLabel = CanUseBuiltInReader(ItemPath)
            ? "🔗 使用外部閱讀器開啟"
            : "🔗 使用外部程式開啟";
        menuItems.Add(new AntdUI.ContextMenuStripItem(externalLabel));
        menuItems.Add(new AntdUI.ContextMenuStripItem("另開頁籤"));
        menuItems.Add(new AntdUI.ContextMenuStripItemDivider());
        menuItems.Add(new AntdUI.ContextMenuStripItem("📂 在檔案總管中開啟"));
        menuItems.Add(new AntdUI.ContextMenuStripItem("👤 作者資訊"));
        // 詳細資訊只對 Work 顯示（其他層級沒 metadata 可看）
        if (IsWorkAsset(ItemPath))
        {
            menuItems.Add(new AntdUI.ContextMenuStripItem("ℹ 詳細資訊"));
        }
        // 閱讀狀態三項只對 Work 顯示（壓縮檔 / pdf / 純圖檔資料夾≥2張無子目錄）
        if (IsWorkAsset(ItemPath))
        {
            // 當前 status 對應的選項要 disable（已是該狀態，不能重複設定）
            var current = ReadStatusProvider?.Invoke(ItemPath);
            menuItems.Add(new AntdUI.ContextMenuStripItemDivider());
            menuItems.Add(new AntdUI.ContextMenuStripItem("✅ 標為已完成") { Enabled = current != ReadStatus.Completed });
            menuItems.Add(new AntdUI.ContextMenuStripItem("⏹ 標為放棄") { Enabled = current != ReadStatus.Dropped });
            menuItems.Add(new AntdUI.ContextMenuStripItem("↺ 重置為未讀") { Enabled = current != ReadStatus.Unread && current != null });
        }

        AntdUI.ContextMenuStrip.open(this, menuItem =>
        {
            var t = menuItem.Text;
            if (t == null) return;
            if (t.Contains("外部") || t.Contains("External"))
            {
                RequestOpenWithExternal?.Invoke(ItemPath);
            }
            else if (t.Contains("進入") || t.Contains("閱讀"))
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
            else if (t.Contains("詳細") || t.Contains("Detail"))
            {
                RequestWorkDetail?.Invoke(ItemPath);
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

    /// <summary>
    /// 是否能用內建閱讀器開啟。reader 支援：壓縮檔（.zip/.cbz/.rar/.cbr/.7z）/ 單張圖檔 / 含圖資料夾。
    /// PDF / EPUB 等 Phase 2+ 才支援，目前 false。
    /// </summary>
    private static bool CanUseBuiltInReader(string path)
    {
        if (Directory.Exists(path))
        {
            // 含至少 1 張圖即可（FolderReaderSource enumerate 直接子檔）
            foreach (var f in Directory.EnumerateFiles(path))
            {
                var e = Path.GetExtension(f).ToLowerInvariant();
                if (e is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif")
                    return true;
            }
            return false;
        }
        if (!File.Exists(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".zip" or ".cbz" or ".rar" or ".cbr" or ".7z") return true;
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif") return true;
        return false;
    }

    /// <summary>
    /// 是否為「Work」asset（可被閱讀、可被標 read_status）。判定條件：
    /// (1) 檔案副檔名為 archive/pdf：.zip/.cbz/.cbr/.rar/.7z/.pdf
    /// (2) 目錄無子目錄且含 ≥2 張圖檔
    /// 其他（Author folder / Library / Category / 純空目錄）→ false
    /// </summary>
    private static bool IsWorkAsset(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            if (File.Exists(path))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                return ext is ".zip" or ".cbz" or ".cbr" or ".rar" or ".7z" or ".pdf";
            }
            if (Directory.Exists(path))
            {
                // 含子目錄 → 不是 Work（章節集 / 作者夾）
                if (Directory.EnumerateDirectories(path).Any()) return false;
                int imgCount = 0;
                foreach (var f in Directory.EnumerateFiles(path))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif")
                    {
                        if (++imgCount >= 2) return true;   // 達標早退
                    }
                }
                return false;
            }
        }
        catch { /* 路徑被刪、權限不足等 */ }
        return false;
    }
}

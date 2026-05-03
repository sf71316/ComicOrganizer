using AntdUI;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 內建漫畫 reader（#49）。獨立視窗、單頁/雙頁顯示、全螢幕、鍵盤/滾輪導覽。
/// 預讀 ±2 頁、視窗外的 Bitmap dispose 回收。RTL/LTR 影響 ←/→ 鍵語意 + 雙頁左右順序。
///
/// 連續捲動 / 進度保存 / 自動 Completed — Phase 1.2c / 1.3。
/// </summary>
public partial class ReaderForm : Window, IThemedControl
{
    private readonly IReaderSource _source;
    private readonly bool _rtl;
    private readonly bool _spreadFromCover;   // true: 第 1 頁獨立、2+3 併排；false: 1+2、3+4 併排
    private int _currentIndex;
    private ReaderViewMode _viewMode = ReaderViewMode.SinglePage;
    private ReaderZoomMode _zoomMode = ReaderZoomMode.FitPage;
    private float _customZoom = 1.0f;
    private const float MinZoom = 0.1f, MaxZoom = 8.0f;

    // 預讀 cache：頁 index → 已 decode bitmap（單頁原圖）
    private readonly Dictionary<int, Image> _cache = new();
    private readonly HashSet<int> _loading = new();
    private readonly object _cacheLock = new();
    private const int PreloadRadius = 2;

    // 雙頁 composite 暫存：避免每次重畫；spread 更新時 dispose 舊的
    private Bitmap? _composite;

    // 全螢幕狀態回復用
    private FormBorderStyle _prevBorderStyle;
    private FormWindowState _prevWindowState;
    private bool _isFullscreen;

    public ReaderForm(IReaderSource source, bool rightToLeft = true,
                      bool dualPageSpreadFromCover = false)
    {
        InitializeComponent();
        _source = source;
        _rtl = rightToLeft;
        _spreadFromCover = dualPageSpreadFromCover;
        _currentIndex = 0;

        Text = source.DisplayName;
        // PageHeader：Text = 分頁資訊（UpdatePageInfo 會更新），SubText = 作品名（固定）
        windowBar.SubText = source.DisplayName;

        ApplyThemeColors();
        this.WireThemeAndLocalization();

        WireEvents();

        if (source.PageCount > 0)
            GoToPage(_currentIndex);
        else
            lblStatus.Text = "(無圖檔)";
    }

    /// <summary>閱讀器內容區固定中性深灰 — 不論主題都用同色，避免主題色干擾頁面色彩感受。</summary>
    private static readonly Color ReaderContentBg = Color.FromArgb(99, 98, 99);

    public void ApplyThemeColors()
    {
        // toolbar / statusBar 走 TabHeader 區分層次；picPage / scrollHost 用固定中性灰
        BackColor = ReaderContentBg;
        windowBar.BackColor = ThemeColors.Primary;
        windowBar.ForeColor = ThemeColors.HeaderText;
        toolbar.BackColor = ThemeColors.TabHeader;
        statusBar.BackColor = ThemeColors.TabHeader;
        scrollHost.BackColor = ReaderContentBg;
        picPage.BackColor = ReaderContentBg;
        lblStatus.ForeColor = ThemeColors.Text;
        // dual mode composite 的 gutter 把 bg 烙進 bitmap → 主題切換時重 render 才會跟著變
        if (_viewMode == ReaderViewMode.DualPage && SpreadSize(_currentIndex) == 2)
            TryRenderCurrentSpread();
    }

    public void ApplyLocalization()
    {
        // reader 目前文字都寫死 zh-TW；後續加 i18n key 再實作
    }

    private void WireEvents()
    {
        btnFullscreen.Click += (s, e) => ToggleFullscreen();
        segViewMode.SelectIndexChanged += (s, e) => OnViewModeChanged(e.Value);
        segZoomMode.SelectIndexChanged += (s, e) => OnZoomModeChanged(e.Value);

        KeyDown += OnKeyDown;
        // wheel 處理：Ctrl+wheel 縮放；FitPage 模式 plain wheel 翻頁；其他模式讓 panel 處理捲動
        scrollHost.MouseWheel += OnContentMouseWheel;
        picPage.MouseWheel += OnContentMouseWheel;

        FormClosing += (s, e) => DisposeAllPages();
        scrollHost.Resize += (s, e) => LayoutPic();
    }

    private void OnContentMouseWheel(object? sender, MouseEventArgs e)
    {
        if ((Control.ModifierKeys & Keys.Control) != 0)
        {
            AdjustCustomZoom(e.Delta > 0 ? 1.1f : 1f / 1.1f);
            return;
        }
        if (_zoomMode == ReaderZoomMode.FitPage)
        {
            // 只有 FitPage 整頁顯示時 wheel 才當「翻頁」用，其他模式 wheel 應該捲動內容
            if (e.Delta > 0) StepSpread(-1);
            else StepSpread(+1);
        }
        // 其他 zoom mode：不做事，讓 ScrollableControl 預設行為（已先 scroll）保留
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl 組合先處理（zoom 快捷鍵）
        if (e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.D0: case Keys.NumPad0: ResetZoom(); e.Handled = true; return;
                case Keys.Oemplus: case Keys.Add: AdjustCustomZoom(1.1f); e.Handled = true; return;
                case Keys.OemMinus: case Keys.Subtract: AdjustCustomZoom(1f / 1.1f); e.Handled = true; return;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_isFullscreen) ToggleFullscreen();
                else Close();
                break;
            case Keys.F11: case Keys.F: case Keys.Enter: ToggleFullscreen(); break;
            case Keys.Left: StepSpread(_rtl ? +1 : -1); break;
            case Keys.Right: StepSpread(_rtl ? -1 : +1); break;
            case Keys.PageUp: case Keys.Back: StepSpread(-1); break;
            case Keys.PageDown: case Keys.Space: StepSpread(+1); break;
            case Keys.Home: GoToPage(0); break;
            case Keys.End: GoToPage(_source.PageCount - 1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ResetZoom()
    {
        _customZoom = 1.0f;
        _zoomMode = ReaderZoomMode.FitPage;
        segZoomMode.SelectIndex = 0;
        LayoutPic();
        UpdateStatusBar();
    }

    private void OnZoomModeChanged(int idx)
    {
        var mode = idx switch
        {
            0 => ReaderZoomMode.FitPage,
            1 => ReaderZoomMode.FitWidth,
            2 => ReaderZoomMode.Actual,
            _ => ReaderZoomMode.FitPage,
        };
        if (mode == _zoomMode) return;
        _zoomMode = mode;
        _customZoom = 1.0f;   // 切換內建模式時重置 custom zoom
        LayoutPic();
        UpdateStatusBar();
    }

    private void AdjustCustomZoom(float factor)
    {
        var newZoom = Math.Clamp(_customZoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _customZoom) < 0.001f) return;
        _customZoom = newZoom;
        _zoomMode = ReaderZoomMode.Custom;
        // segZoomMode 不對應 Custom — 取消 highlight：先設成 100% 接近用 -1（AntdUI 可能不支援）；保留現狀
        LayoutPic();
        UpdateStatusBar();
    }

    /// <summary>依 _zoomMode + _customZoom 計算 picPage 顯示尺寸與位置（捲動容器內居中）。</summary>
    private void LayoutPic()
    {
        var img = picPage.Image;
        if (img == null) return;
        if (scrollHost.ClientSize.Width <= 0 || scrollHost.ClientSize.Height <= 0) return;

        // AutoScroll 計算 viewport 排除 scrollbar — 用 ClientSize 即可
        int hostW = scrollHost.ClientSize.Width;
        int hostH = scrollHost.ClientSize.Height;

        int targetW, targetH;
        switch (_zoomMode)
        {
            case ReaderZoomMode.FitPage:
                {
                    float sx = (float)hostW / img.Width;
                    float sy = (float)hostH / img.Height;
                    float s = Math.Min(sx, sy);
                    targetW = Math.Max(1, (int)(img.Width * s));
                    targetH = Math.Max(1, (int)(img.Height * s));
                }
                break;
            case ReaderZoomMode.FitWidth:
                targetW = hostW;
                targetH = (int)(img.Height * (float)hostW / img.Width);
                break;
            case ReaderZoomMode.Actual:
                targetW = img.Width;
                targetH = img.Height;
                break;
            case ReaderZoomMode.Custom:
                targetW = Math.Max(1, (int)(img.Width * _customZoom));
                targetH = Math.Max(1, (int)(img.Height * _customZoom));
                break;
            default:
                targetW = img.Width;
                targetH = img.Height;
                break;
        }

        picPage.Size = new Size(targetW, targetH);
        // 居中對齊：尺寸小於容器時 padding 兩側；大於時靠左上由 scroll 處理
        int x = Math.Max(0, (hostW - targetW) / 2);
        int y = Math.Max(0, (hostH - targetH) / 2);
        picPage.Location = new Point(x, y);
    }

    private void OnViewModeChanged(int idx)
    {
        var newMode = idx switch
        {
            0 => ReaderViewMode.SinglePage,
            1 => ReaderViewMode.DualPage,
            2 => ReaderViewMode.ContinuousScroll,
            _ => ReaderViewMode.SinglePage,
        };
        if (newMode == _viewMode) return;
        if (newMode == ReaderViewMode.ContinuousScroll)
        {
            // Phase 1.2b 才實作 — 暫時 fallback 回上一個 mode
            segViewMode.SelectIndex = _viewMode == ReaderViewMode.SinglePage ? 0 : 1;
            AntdUI.Message.info(this, "連續捲動模式 Phase 1.2b 實作中");
            return;
        }
        _viewMode = newMode;
        // 切換 mode 後重新對齊 spread 起始 + 重 render
        _currentIndex = SpreadStart(_currentIndex);
        GoToPage(_currentIndex);
    }

    /// <summary>取得 spread 的起始頁（雙頁模式對齊用）。</summary>
    private int SpreadStart(int pageIndex)
    {
        if (_viewMode != ReaderViewMode.DualPage) return pageIndex;
        if (_spreadFromCover)
        {
            // cover (page 0) 獨立，pages 1+2、3+4… 對齊到奇數
            if (pageIndex == 0) return 0;
            return pageIndex - ((pageIndex - 1) % 2);
        }
        // 預設：0+1、2+3… 對齊到偶數
        return pageIndex - (pageIndex % 2);
    }

    /// <summary>當前 spread 的頁數（1 或 2）— 影響 step 步長與 evict window。</summary>
    private int SpreadSize(int spreadStart)
    {
        if (_viewMode != ReaderViewMode.DualPage) return 1;
        // cover-first 模式下 page 0 自己一頁
        if (_spreadFromCover && spreadStart == 0) return 1;
        // 最後一頁若沒有 partner 也是單頁
        if (spreadStart + 1 >= _source.PageCount) return 1;
        return 2;
    }

    private void StepSpread(int direction)
    {
        var current = SpreadStart(_currentIndex);
        if (direction > 0)
        {
            var next = current + SpreadSize(current);
            if (next >= _source.PageCount) return;
            GoToPage(next);
        }
        else
        {
            if (current == 0) return;
            // 上一 spread = 從 current-1 反推 SpreadStart
            var prev = SpreadStart(current - 1);
            GoToPage(prev);
        }
    }

    private void GoToPage(int index)
    {
        if (index < 0 || index >= _source.PageCount) return;
        _currentIndex = SpreadStart(index);

        UpdatePageInfo();

        // 預讀 + evict
        var spreadSize = SpreadSize(_currentIndex);
        EvictOutsideWindow(spreadSize);
        for (int d = 0; d < spreadSize; d++) _ = LoadPageAsync(_currentIndex + d);
        for (int d = 1; d <= PreloadRadius; d++)
        {
            int forward = _currentIndex + spreadSize - 1 + d;
            int backward = _currentIndex - d;
            if (forward < _source.PageCount) _ = LoadPageAsync(forward);
            if (backward >= 0) _ = LoadPageAsync(backward);
        }

        // 立刻嘗試 render（cache hit 即時顯示；miss 等 LoadPageAsync 完成後 fire 回來再 render）
        TryRenderCurrentSpread();
    }

    private async Task LoadPageAsync(int index)
    {
        lock (_cacheLock)
        {
            if (_cache.ContainsKey(index)) return;
            if (!_loading.Add(index)) return;
        }

        Image? img = null;
        try
        {
            img = await Task.Run(() =>
            {
                using var s = _source.OpenPage(index);
                return ReaderImageLoader.Decode(s, _source.GetPageExtension(index));
            });
        }
        catch
        {
            lock (_cacheLock) _loading.Remove(index);
            return;
        }

        if (IsDisposed) { img.Dispose(); return; }

        lock (_cacheLock)
        {
            _loading.Remove(index);
            _cache[index] = img;
        }

        // 永遠檢查：載完的頁如果屬於當前 spread 就觸發重 render
        // （原本用 makeVisible 旗標區分 preload vs visible 載入會 race —
        //  preload 在背景跑時若使用者翻到該頁，dedup 後不再啟新 task、
        //  原 preload 完成卻不 fire UI，UI 卡在「讀取中」）
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                int start = SpreadStart(_currentIndex);
                int size = SpreadSize(start);
                if (index >= start && index < start + size) TryRenderCurrentSpread();
            });
        }
        catch { /* handle gone */ }
    }

    /// <summary>把當前 spread 套到 picPage（cache hit 即時顯示；缺頁顯示 loading）。</summary>
    private void TryRenderCurrentSpread()
    {
        int start = _currentIndex;
        int size = SpreadSize(start);

        Image? p0, p1;
        lock (_cacheLock)
        {
            _cache.TryGetValue(start, out p0);
            p1 = size == 2 ? (_cache.TryGetValue(start + 1, out var v) ? v : null) : null;
        }

        // 任一頁未到 → 顯示 loading
        if (p0 == null || (size == 2 && p1 == null))
        {
            picPage.Image = null;
            lblStatus.Text = "讀取中…";
            return;
        }

        // 釋放舊 composite
        var oldComposite = _composite;
        _composite = null;

        if (size == 1)
        {
            picPage.Image = p0;
        }
        else
        {
            // 雙頁併排：RTL → 右邊放 start (主)、左邊放 start+1；LTR 反之
            var leftImg = _rtl ? p1! : p0;
            var rightImg = _rtl ? p0 : p1!;
            _composite = ComposePair(leftImg, rightImg);
            picPage.Image = _composite;
        }

        if (oldComposite != null && !ReferenceEquals(oldComposite, picPage.Image))
            oldComposite.Dispose();

        LayoutPic();
        UpdateStatusBar();
    }

    /// <summary>把兩張圖左右拼接成一張 Bitmap。高度取較高者；矮的居中對齊。gutter 用 reader 中性灰。</summary>
    private static Bitmap ComposePair(Image left, Image right)
    {
        int totalW = left.Width + right.Width;
        int totalH = Math.Max(left.Height, right.Height);
        var bmp = new Bitmap(totalW, totalH);
        using var g = Graphics.FromImage(bmp);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(ReaderContentBg);
        g.DrawImage(left, 0, (totalH - left.Height) / 2);
        g.DrawImage(right, left.Width, (totalH - right.Height) / 2);
        return bmp;
    }

    /// <summary>釋放 spread 視窗 ± PreloadRadius 之外的 cache。</summary>
    private void EvictOutsideWindow(int spreadSize)
    {
        int lo = _currentIndex - PreloadRadius;
        int hi = _currentIndex + spreadSize - 1 + PreloadRadius;
        List<int> toEvict;
        lock (_cacheLock)
        {
            toEvict = _cache.Keys.Where(k => k < lo || k > hi).ToList();
            foreach (var k in toEvict)
            {
                if (_cache.TryGetValue(k, out var img))
                {
                    img.Dispose();
                    _cache.Remove(k);
                }
            }
        }
    }

    private void DisposeAllPages()
    {
        var oldComposite = _composite;
        picPage.Image = null;
        _composite = null;
        oldComposite?.Dispose();
        lock (_cacheLock)
        {
            foreach (var img in _cache.Values) img.Dispose();
            _cache.Clear();
            _loading.Clear();
        }
        _source.Dispose();
    }

    /// <summary>F11 / F 切換全螢幕。隱藏 windowBar / toolbar / statusBar，picPage 佔滿。</summary>
    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            _prevBorderStyle = FormBorderStyle;
            _prevWindowState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            // 強制走 Normal → Maximized 才能讓 None 模式吃 fullscreen size（直接 Maximized 不能切 None）
            WindowState = FormWindowState.Normal;
            WindowState = FormWindowState.Maximized;
            windowBar.Visible = false;
            toolbar.Visible = false;
            statusBar.Visible = false;
            _isFullscreen = true;
        }
        else
        {
            FormBorderStyle = _prevBorderStyle;
            WindowState = _prevWindowState;
            windowBar.Visible = true;
            toolbar.Visible = true;
            statusBar.Visible = true;
            _isFullscreen = false;
        }
    }

    private void UpdatePageInfo()
    {
        int start = _currentIndex;
        int size = SpreadSize(start);
        var pageStr = size == 1
            ? $"{start + 1} / {_source.PageCount}"
            : $"{start + 1}-{start + 2} / {_source.PageCount}";
        // PageHeader 主標 = 分頁資訊；SubText（DisplayName）在 ctor 已設好不變
        windowBar.Text = pageStr;
    }

    private void UpdateStatusBar()
    {
        var name = _source.GetPageName(_currentIndex);
        var img = picPage.Image;
        var dim = img != null ? $"{img.Width}×{img.Height}" : "-";
        var dir = _rtl ? "RTL" : "LTR";
        var mode = _viewMode switch
        {
            ReaderViewMode.SinglePage => "單頁",
            ReaderViewMode.DualPage => "雙頁",
            ReaderViewMode.ContinuousScroll => "捲動",
            _ => "",
        };
        var zoomLabel = _zoomMode switch
        {
            ReaderZoomMode.FitPage => "適合頁",
            ReaderZoomMode.FitWidth => "適合寬",
            ReaderZoomMode.Actual => "100%",
            ReaderZoomMode.Custom => $"{(int)(_customZoom * 100)}%",
            _ => "",
        };
        lblStatus.Text = $"{name}    {dim}    {dir}    {mode}    {zoomLabel}";
    }
}

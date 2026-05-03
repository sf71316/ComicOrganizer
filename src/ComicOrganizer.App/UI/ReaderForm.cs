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
    private readonly bool _spreadFromCover;
    private readonly IAssetMetaStore? _metaStore;   // 進度保存用，可選
    private int _currentIndex;
    private int _lastSavedPage = -1;   // 上次寫進 DB 的頁碼，避免重覆寫
    private ReaderViewMode _viewMode = ReaderViewMode.SinglePage;
    private ReaderZoomMode _zoomMode = ReaderZoomMode.FitPage;
    private float _customZoom = 1.0f;
    private const float MinZoom = 0.1f, MaxZoom = 8.0f;

    // 預讀 cache：頁 index → 已 decode bitmap（單頁原圖）
    private readonly Dictionary<int, Image> _cache = new();
    private readonly HashSet<int> _loading = new();
    private readonly object _cacheLock = new();
    private const int PreloadRadius = 2;

    // 當前要顯示的圖（直接 ref _cache 的 bitmap，不複製）：
    // _leftImage 必有值；_rightImage 只有雙頁模式 spread size=2 才設
    // 翻頁時只是換 ref，picPage.Invalidate 重畫，零 allocation
    private Image? _leftImage;
    private Image? _rightImage;

    // 全螢幕狀態回復用
    private FormBorderStyle _prevBorderStyle;
    private FormWindowState _prevWindowState;
    private bool _isFullscreen;

    public ReaderForm(IReaderSource source, bool rightToLeft = true,
                      bool dualPageSpreadFromCover = false,
                      IAssetMetaStore? metaStore = null)
    {
        InitializeComponent();
        _source = source;
        _rtl = rightToLeft;
        _spreadFromCover = dualPageSpreadFromCover;
        _metaStore = metaStore;

        // 從 DB 撈 last_page 接續閱讀；沒紀錄或越界回 0
        var resume = _metaStore?.GetLastPage(source.RootPath);
        _currentIndex = (resume.HasValue && resume.Value >= 0 && resume.Value < source.PageCount)
            ? resume.Value : 0;

        Text = source.DisplayName;
        // PageHeader：Text = 分頁資訊（UpdatePageInfo 會更新），SubText = 作品名（固定）
        windowBar.SubText = source.DisplayName;

        ApplyThemeColors();
        this.WireThemeAndLocalization();

        // picPage 不走預設 Image stretch，改自畫；TryRenderCurrentSpread 只設 ref 不 allocate
        picPage.Image = null;
        picPage.Paint += OnPicPagePaint;

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
        // 自畫不烙 bg，主題切換時 picPage.BackColor 自動 trigger 重畫，無需手動重 render
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

        FormClosing += (s, e) =>
        {
            SaveProgress();   // 關閉時保存最終頁碼
            DisposeAllPages();
        };
        scrollHost.Resize += (s, e) => LayoutPic();
    }

    /// <summary>把 _currentIndex 寫進 asset_meta.last_page（僅在頁碼變動時寫入）。</summary>
    private void SaveProgress()
    {
        if (_metaStore == null) return;
        if (_currentIndex == _lastSavedPage) return;
        try
        {
            _metaStore.SetLastPage(_source.RootPath, _currentIndex);
            _lastSavedPage = _currentIndex;
        }
        catch { /* DB 寫失敗不致命 */ }
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
            // ←/→ 翻頁只在 Single/Dual 生效；Continuous 模式留給捲動 / ↑↓ 不攔截走 scrollHost 預設
            case Keys.Left:
                if (_viewMode == ReaderViewMode.ContinuousScroll) return;
                StepSpread(_rtl ? +1 : -1);
                break;
            case Keys.Right:
                if (_viewMode == ReaderViewMode.ContinuousScroll) return;
                StepSpread(_rtl ? -1 : +1);
                break;
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

    /// <summary>依 _zoomMode + _customZoom 計算 picPage 顯示尺寸與位置（捲動容器內居中）。
    /// 自然大小取 _leftImage + _rightImage 的併排寬度；單頁時 _rightImage 為 null。</summary>
    private void LayoutPic()
    {
        if (_leftImage == null) return;
        if (scrollHost.ClientSize.Width <= 0 || scrollHost.ClientSize.Height <= 0) return;

        int naturalW = _leftImage.Width + (_rightImage?.Width ?? 0);
        int naturalH = _rightImage != null
            ? Math.Max(_leftImage.Height, _rightImage.Height)
            : _leftImage.Height;

        // AutoScroll 計算 viewport 排除 scrollbar — 用 ClientSize 即可
        int hostW = scrollHost.ClientSize.Width;
        int hostH = scrollHost.ClientSize.Height;

        int targetW, targetH;
        switch (_zoomMode)
        {
            case ReaderZoomMode.FitPage:
                {
                    float sx = (float)hostW / naturalW;
                    float sy = (float)hostH / naturalH;
                    float s = Math.Min(sx, sy);
                    targetW = Math.Max(1, (int)(naturalW * s));
                    targetH = Math.Max(1, (int)(naturalH * s));
                }
                break;
            case ReaderZoomMode.FitWidth:
                targetW = hostW;
                targetH = (int)(naturalH * (float)hostW / naturalW);
                break;
            case ReaderZoomMode.Actual:
                targetW = naturalW;
                targetH = naturalH;
                break;
            case ReaderZoomMode.Custom:
                targetW = Math.Max(1, (int)(naturalW * _customZoom));
                targetH = Math.Max(1, (int)(naturalH * _customZoom));
                break;
            default:
                targetW = naturalW;
                targetH = naturalH;
                break;
        }

        picPage.Size = new Size(targetW, targetH);
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

        // 每次翻頁存進度 — SQLite UPDATE 單筆，ms 等級不會卡 UI
        SaveProgress();
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

    /// <summary>把當前 spread 套到 picPage（cache hit 即時顯示；缺頁顯示 loading）。
    /// 不做 composite — 只更新 _leftImage / _rightImage ref，讓 OnPicPagePaint 直接畫。
    /// 翻頁的 hot path = 兩個 ref 賦值 + LayoutPic + Invalidate，~ms 等級。</summary>
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
            _leftImage = null;
            _rightImage = null;
            picPage.Invalidate();
            lblStatus.Text = "讀取中…";
            return;
        }

        if (size == 1)
        {
            _leftImage = p0;
            _rightImage = null;
        }
        else
        {
            // 雙頁併排：RTL → 右邊放 start (主)、左邊放 start+1；LTR 反之
            _leftImage = _rtl ? p1! : p0;
            _rightImage = _rtl ? p0 : p1!;
        }

        LayoutPic();
        picPage.Invalidate();
        UpdateStatusBar();
    }

    /// <summary>picPage 自畫：依 _leftImage / _rightImage 直接 DrawImage 縮放，
    /// 不需事先 composite 整張 bitmap，避免大圖每翻一頁都 alloc/dispose 數百 MB。</summary>
    private void OnPicPagePaint(object? sender, PaintEventArgs e)
    {
        if (_leftImage == null) return;
        var g = e.Graphics;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        // BackColor 已在 OnPaintBackground 填好

        if (_rightImage == null)
        {
            g.DrawImage(_leftImage, new Rectangle(0, 0, picPage.Width, picPage.Height));
            return;
        }

        // 雙頁：依原圖寬度比例分配 picPage 的寬度，垂直依高度比例置中（不同高度時矮的不被拉伸）
        int totalNaturalW = _leftImage.Width + _rightImage.Width;
        int totalNaturalH = Math.Max(_leftImage.Height, _rightImage.Height);
        int leftW = (int)((double)_leftImage.Width / totalNaturalW * picPage.Width);
        int rightW = picPage.Width - leftW;
        int leftH = (int)((double)_leftImage.Height / totalNaturalH * picPage.Height);
        int rightH = (int)((double)_rightImage.Height / totalNaturalH * picPage.Height);
        int leftY = (picPage.Height - leftH) / 2;
        int rightY = (picPage.Height - rightH) / 2;
        g.DrawImage(_leftImage, new Rectangle(0, leftY, leftW, leftH));
        g.DrawImage(_rightImage, new Rectangle(leftW, rightY, rightW, rightH));
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
        _leftImage = null;
        _rightImage = null;
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
        // 顯示「主」頁尺寸（雙頁模式 = leftImg；single mode 也是 leftImg）
        var dim = _leftImage != null ? $"{_leftImage.Width}×{_leftImage.Height}" : "-";
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

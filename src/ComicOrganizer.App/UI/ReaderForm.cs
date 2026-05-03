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

    // 雙條件閱讀計數（Phase 1.3c）：時間 + 翻頁數都過門檻才算一次有效閱讀
    private readonly DateTime _openedAt = DateTime.UtcNow;
    private int _pagesVisited;        // 包含 GoToPage 抵達的初始頁
    private bool _readRecorded;       // 已 RecordRead 過此 session 一次
    private bool _completedRecorded;  // 自動 Completed 已觸發過
    private readonly int _readThresholdSeconds;
    private readonly int _readThresholdPages;
    private readonly bool _trackReadingRecord;   // false 時 TryRecordRead / TryAutoComplete 都跳過

    /// <summary>定期觸發 TryRecordRead — 使用者只停在一頁不翻時也能在時間到後觸發記錄。</summary>
    private readonly System.Windows.Forms.Timer _recordCheckTimer = new() { Interval = 5000 };
    private ReaderViewMode _viewMode = ReaderViewMode.SinglePage;
    private ReaderZoomMode _zoomMode = ReaderZoomMode.FitPage;
    private float _customZoom = 1.0f;
    private const float MinZoom = 0.1f, MaxZoom = 8.0f;

    // Continuous mode 狀態（_viewMode == ContinuousScroll 時才有效）
    private ContinuousLayout? _layout;
    private readonly System.Windows.Forms.Timer _scrollDebounce = new() { Interval = 500 };
    private bool IsContinuous => _viewMode == ReaderViewMode.ContinuousScroll;

    // 連續捲動模式採用「虛擬 scroll」— picPage 保持 viewport 大小（避開 Windows 控件 65535 高度上限），
    // _virtualScrollY 為 [0, layout.TotalHeight - viewH] 的邏輯 scroll 偏移，DrawContinuous 用此偏移平移繪製。
    private int _virtualScrollY;

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
                      IAssetMetaStore? metaStore = null,
                      ReadRecordQueue? readRecordQueue = null,
                      int readThresholdSeconds = 30,
                      int readThresholdPages = 2,
                      bool trackReadingRecord = true)
    {
        InitializeComponent();
        _source = source;
        _rtl = rightToLeft;
        _spreadFromCover = dualPageSpreadFromCover;
        _metaStore = metaStore;
        _readThresholdSeconds = Math.Max(1, readThresholdSeconds);
        _readThresholdPages = Math.Max(1, readThresholdPages);
        _trackReadingRecord = trackReadingRecord;

        // 取消外層（card 點擊）已 enqueue 的 30s 自動計數 — 內建 reader 走自己的雙條件邏輯
        readRecordQueue?.Cancel(source.RootPath);

        // 啟動定期檢查 timer：每 5s 觸發 TryRecordRead 確保時間條件能在不翻頁時也被命中
        _recordCheckTimer.Tick += (s, e) =>
        {
            if (IsDisposed) { _recordCheckTimer.Stop(); return; }
            if (_readRecorded) { _recordCheckTimer.Stop(); return; }   // 記錄完不再 polling
            TryRecordRead();
        };
        _recordCheckTimer.Start();

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
        // sliderZoom 範圍直接引用 ContinuousLayout 的 MinZoom/MaxZoom 常數，避免兩處不同步
        sliderZoom.SetMinMax(
            (int)(ContinuousLayout.MinZoom * 100),
            (int)(ContinuousLayout.MaxZoom * 100));
        sliderZoom.Value = (sliderZoom.MinValue + sliderZoom.MaxValue) / 2;   // 預設 slider 中央

        btnFullscreen.Click += (s, e) => ToggleFullscreen();
        segViewMode.SelectIndexChanged += (s, e) => OnViewModeChanged(e.Value);
        segZoomMode.SelectIndexChanged += (s, e) => OnZoomModeChanged(e.Value);
        sliderZoom.ValueChanged += (s, e) => OnSliderZoomChanged();
        switchSeamless.CheckedChanged += (s, e) => OnSeamlessChanged();

        KeyDown += OnKeyDown;
        // wheel 處理：Ctrl+wheel 縮放；FitPage 模式 plain wheel 翻頁；其他模式讓 panel 處理捲動
        scrollHost.MouseWheel += OnContentMouseWheel;
        picPage.MouseWheel += OnContentMouseWheel;

        // Continuous 模式：使用獨立 vScrollBarContinuous 處理大於 65535px 的虛擬 scroll
        vScrollBarContinuous.ValueChanged += (s, e) =>
        {
            if (!IsContinuous) return;
            _virtualScrollY = vScrollBarContinuous.Value;
            picPage.Invalidate();
            UpdateContinuousState();
        };
        // 拖捲軸停止 500ms 後寫進度（避免每個 scroll event 都打 DB）
        _scrollDebounce.Tick += (s, e) =>
        {
            _scrollDebounce.Stop();
            if (!IsDisposed && IsContinuous) SaveProgress();
        };

        FormClosing += (s, e) =>
        {
            SaveProgress();   // 關閉時保存最終頁碼
            DisposeAllPages();
        };
        scrollHost.Resize += (s, e) =>
        {
            if (IsContinuous && _layout != null)
            {
                int oldScrollY = _virtualScrollY;
                int focusPage = _layout.FindPageAtY(oldScrollY);
                LayoutPic();
                if (focusPage >= 0) ScrollContinuousTo(_layout.GetPageY(focusPage));
                else UpdateContinuousState();
                return;
            }
            LayoutPic();
        };
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

    /// <summary>
    /// 雙條件計數（Phase 1.3c）：停留時間「或」翻頁數任一達標就算一次有效閱讀。
    ///   - 停留 ≥ N 秒 → 視為慢讀（停在一頁細看）
    ///   - 翻過 ≥ M 頁 → 視為快瀏覽（看圖為主）
    /// 任一達成即記錄；一個 reader session 最多觸發一次（read_count++ 一次）。
    /// </summary>
    private void TryRecordRead()
    {
        if (!_trackReadingRecord) return;
        if (_readRecorded || _metaStore == null) return;
        var elapsed = (DateTime.UtcNow - _openedAt).TotalSeconds;
        if (elapsed < _readThresholdSeconds && _pagesVisited < _readThresholdPages) return;
        try
        {
            _metaStore.RecordRead(_source.RootPath);
            _readRecorded = true;
            _recordCheckTimer.Stop();
        }
        catch { /* DB 寫失敗不致命 */ }
    }

    /// <summary>
    /// 自動 Completed（Phase 1.3b）：當前 spread 涵蓋最後一頁就標 Completed。
    /// 一個 reader session 最多觸發一次。
    /// </summary>
    private void TryAutoComplete()
    {
        if (!_trackReadingRecord) return;
        if (_completedRecorded || _metaStore == null) return;
        int lastShown = _currentIndex + SpreadSize(_currentIndex) - 1;
        if (lastShown < _source.PageCount - 1) return;
        try
        {
            _metaStore.SetReadStatus(_source.RootPath, ReadStatus.Completed);
            _completedRecorded = true;
        }
        catch { }
    }

    private void OnContentMouseWheel(object? sender, MouseEventArgs e)
    {
        if ((Control.ModifierKeys & Keys.Control) != 0)
        {
            // Continuous 模式 Ctrl+wheel 走 slider 而非 _customZoom
            if (IsContinuous && _layout != null)
            {
                int step = e.Delta > 0 ? 5 : -5;
                int v = Math.Clamp(sliderZoom.Value + step, sliderZoom.MinValue, sliderZoom.MaxValue);
                sliderZoom.Value = v;
                return;
            }
            AdjustCustomZoom(e.Delta > 0 ? 1.1f : 1f / 1.1f);
            return;
        }
        if (IsContinuous)
        {
            // 自繪 scroll：wheel 直接調整 _virtualScrollY 並同步 vScrollBarContinuous.Value
            int newY = _virtualScrollY - e.Delta;   // wheel up (Delta>0) → scrollY 減
            ScrollContinuousTo(newY);
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
                if (IsContinuous) return;
                StepSpread(_rtl ? +1 : -1);
                break;
            case Keys.Right:
                if (IsContinuous) return;
                StepSpread(_rtl ? -1 : +1);
                break;
            case Keys.PageUp: case Keys.Back:
                if (IsContinuous) SmartPageUp();
                else StepSpread(-1);
                break;
            case Keys.PageDown: case Keys.Space:
                if (IsContinuous) SmartPageDown();
                else StepSpread(+1);
                break;
            case Keys.Home:
                if (IsContinuous) ScrollContinuousTo(0);
                else GoToPage(0);
                break;
            case Keys.End:
                if (IsContinuous) ScrollContinuousTo(_layout?.TotalHeight ?? 0);
                else GoToPage(_source.PageCount - 1);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnSliderZoomChanged()
    {
        if (_layout == null) return;
        float zoom = sliderZoom.Value / 100f;
        int focusPage = _layout.FindPageAtY(_virtualScrollY + picPage.Height / 2);
        _layout.SetZoom(zoom);
        LayoutPic();
        if (focusPage >= 0) ScrollContinuousTo(_layout.GetPageY(focusPage));
        picPage.Invalidate();
        UpdateStatusBar();
    }

    private void ScrollContinuousTo(int y)
    {
        if (_layout == null) return;
        int maxScroll = Math.Max(0, _layout.TotalHeight - picPage.Height);
        y = Math.Max(0, Math.Min(y, maxScroll));
        // 改 vScrollBarContinuous.Value 會觸發 ValueChanged → 更新 _virtualScrollY + Invalidate + UpdateContinuousState
        if (vScrollBarContinuous.Value != y)
        {
            int clampedForScrollBar = Math.Min(y, vScrollBarContinuous.Maximum);
            vScrollBarContinuous.Value = clampedForScrollBar;
        }
        else
        {
            // Value 未變動 → 仍要主動觸發 cache 同步（GoToPage(currentIndex) 重 render 等場景）
            _virtualScrollY = y;
            picPage.Invalidate();
            UpdateContinuousState();
        }
    }

    private void SmartPageDown()
    {
        if (_layout == null) return;
        int scrollY = _virtualScrollY;
        int viewH = picPage.Height;
        int next = _layout.NextPageBoundary(scrollY);
        int target = (next - scrollY) <= (int)(viewH * 1.1)
            ? next
            : scrollY + (int)(viewH * 0.9);
        ScrollContinuousTo(target);
    }

    private void SmartPageUp()
    {
        if (_layout == null) return;
        int scrollY = _virtualScrollY;
        int viewH = picPage.Height;
        int prev = _layout.PrevPageBoundary(scrollY);
        int target = (scrollY - prev) <= (int)(viewH * 1.1)
            ? prev
            : Math.Max(0, scrollY - (int)(viewH * 0.9));
        ScrollContinuousTo(target);
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
        if (IsContinuous && _layout != null)
        {
            // 扣掉 vScrollBarContinuous 寬度 — 自繪 scroll 所以 scrollHost 不長 AutoScroll bar
            int contW = scrollHost.ClientSize.Width - vScrollBarContinuous.Width;
            int contH = scrollHost.ClientSize.Height;
            if (contW <= 0 || contH <= 0) return;
            _layout.SetViewportWidth(contW);
            // picPage 保持 viewport 大小，避開 Windows 控件 65535 高度限制；繪製時用 _virtualScrollY 平移
            picPage.Size = new Size(contW, contH);
            picPage.Location = new Point(0, 0);
            // 設定 vScrollBarContinuous 的 range — Windows scrollbar 用 32-bit Maximum，可表達 long 內容
            int maxScroll = Math.Max(0, _layout.TotalHeight - contH);
            vScrollBarContinuous.LargeChange = Math.Max(1, contH);
            vScrollBarContinuous.Maximum = maxScroll + vScrollBarContinuous.LargeChange - 1;
            _virtualScrollY = Math.Max(0, Math.Min(_virtualScrollY, maxScroll));
            if (vScrollBarContinuous.Value != _virtualScrollY) vScrollBarContinuous.Value = _virtualScrollY;
            return;
        }
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

        var prevMode = _viewMode;
        _viewMode = newMode;

        // toolbar 切換：Continuous 隱藏 segZoomMode、顯示 sliderZoom + switchSeamless；反之亦然
        bool toContinuous = newMode == ReaderViewMode.ContinuousScroll;
        segZoomMode.Visible = !toContinuous;
        sliderZoom.Visible = toContinuous;
        switchSeamless.Visible = toContinuous;
        lblSeamless.Visible = toContinuous;

        if (toContinuous)
        {
            // 進 Continuous：建立 layout、釋放 single/dual 的 image ref（cache 仍保留）、停掉 AutoScroll、顯示自繪 vScrollBar
            _leftImage = null;
            _rightImage = null;
            scrollHost.AutoScroll = false;   // 禁用 AutoScroll（picPage 維持 viewport 大小，不會觸發）
            vScrollBarContinuous.Visible = true;
            _virtualScrollY = 0;
            _layout = new ContinuousLayout(_source.PageCount, Math.Max(1, scrollHost.ClientSize.Width - vScrollBarContinuous.Width));
            _layout.Gap = switchSeamless.Checked ? 0 : ContinuousLayout.PageGap;
            lock (_cacheLock)
            {
                foreach (var (idx2, img) in _cache) _layout.SetMeasured(idx2, img.Width, img.Height);
            }
            int defaultVal = (sliderZoom.MinValue + sliderZoom.MaxValue) / 2;
            sliderZoom.Value = defaultVal;
            _layout.SetZoom(defaultVal / 100f);
            LayoutPic();
            ScrollContinuousTo(_layout.GetPageY(_currentIndex));
            picPage.Invalidate();
            UpdateStatusBar();
        }
        else
        {
            // 離 Continuous：丟掉 layout、隱藏 vScrollBar、恢復 AutoScroll、回 single/dual
            _layout = null;
            vScrollBarContinuous.Visible = false;
            scrollHost.AutoScroll = true;
            _virtualScrollY = 0;
            _currentIndex = SpreadStart(_currentIndex);
            GoToPage(_currentIndex);
        }
    }

    private void OnSeamlessChanged()
    {
        if (_layout == null) return;
        int focusPage = _layout.FindPageAtY(_virtualScrollY + picPage.Height / 2);
        _layout.Gap = switchSeamless.Checked ? 0 : ContinuousLayout.PageGap;
        LayoutPic();
        if (focusPage >= 0) ScrollContinuousTo(_layout.GetPageY(focusPage));
        picPage.Invalidate();
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
        if (IsContinuous && _layout != null)
        {
            _currentIndex = index;
            ScrollContinuousTo(_layout.GetPageY(index));
            // UpdateContinuousState 會在 ScrollContinuousTo 內更新 cache 與 _currentIndex
            return;
        }
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
        _pagesVisited++;
        TryRecordRead();
        TryAutoComplete();
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

        // 先把尺寸抓出來：UpdateContinuousState 可能在 BeginInvoke 之前 evict 並 Dispose 掉 img，
        // 此時於 marshaled 回 UI thread 後再讀 img.Width 會拋 ArgumentException。
        int natW = img.Width;
        int natH = img.Height;

        lock (_cacheLock)
        {
            _loading.Remove(index);
            _cache[index] = img;
        }

        // Continuous 模式：通知 layout 真實尺寸，TotalHeight 可能變動 → 重設 picPage 大小 + Invalidate 該頁區域
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (IsContinuous && _layout != null)
                {
                    int oldTotal = _layout.TotalHeight;
                    _layout.SetMeasured(index, natW, natH);
                    int newTotal = _layout.TotalHeight;
                    // TotalHeight 變動 → 更新 vScrollBarContinuous.Maximum 反映新範圍
                    int maxScroll = Math.Max(0, newTotal - picPage.Height);
                    int newMax = maxScroll + vScrollBarContinuous.LargeChange - 1;
                    if (vScrollBarContinuous.Maximum != newMax) vScrollBarContinuous.Maximum = newMax;
                    // _virtualScrollY 可能因 totalH 縮小而越界，clamp 並同步 scrollbar
                    if (_virtualScrollY > maxScroll)
                    {
                        _virtualScrollY = maxScroll;
                        vScrollBarContinuous.Value = Math.Min(_virtualScrollY, vScrollBarContinuous.Maximum);
                    }
                    UpdateContinuousState();
                    picPage.Invalidate();
                    return;
                }
                // Single/Dual：載完的頁屬於當前 spread 就觸發重 render
                // （原本用 makeVisible 旗標區分 preload vs visible 載入會 race —
                //  preload 在背景跑時若使用者翻到該頁，dedup 後不再啟新 task、
                //  原 preload 完成卻不 fire UI，UI 卡在「讀取中」）
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
    /// 不需事先 composite 整張 bitmap，避免大圖每翻一頁都 alloc/dispose 數百 MB。
    /// Continuous 模式走 DrawContinuous 分支多頁堆疊。</summary>
    private void OnPicPagePaint(object? sender, PaintEventArgs e)
    {
        if (IsContinuous && _layout != null)
        {
            DrawContinuous(e);
            return;
        }
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

        // 雙頁：扣掉中間 5px 白色分隔條，剩餘寬度依原圖寬度比例分配
        const int Gap = 5;
        int contentW = picPage.Width - Gap;
        int totalNaturalW = _leftImage.Width + _rightImage.Width;
        int totalNaturalH = Math.Max(_leftImage.Height, _rightImage.Height);
        int leftW = (int)((double)_leftImage.Width / totalNaturalW * contentW);
        int rightW = contentW - leftW;
        int leftH = (int)((double)_leftImage.Height / totalNaturalH * picPage.Height);
        int rightH = (int)((double)_rightImage.Height / totalNaturalH * picPage.Height);
        int leftY = (picPage.Height - leftH) / 2;
        int rightY = (picPage.Height - rightH) / 2;
        g.DrawImage(_leftImage, new Rectangle(0, leftY, leftW, leftH));
        g.FillRectangle(Brushes.White, leftW, 0, Gap, picPage.Height);
        g.DrawImage(_rightImage, new Rectangle(leftW + Gap, rightY, rightW, rightH));
    }

    /// <summary>Continuous 模式繪製：viewport 內的可見頁，用 _virtualScrollY 平移到 picPage 局部座標。</summary>
    private void DrawContinuous(PaintEventArgs e)
    {
        if (_layout == null) return;
        var g = e.Graphics;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        int viewTop = _virtualScrollY;
        int viewBottom = _virtualScrollY + picPage.Height;
        var (first, last) = _layout.GetVisibleRange(viewTop, viewBottom);
        if (first < 0) return;
        int contentW = _layout.ContentWidth;
        // 水平居中：contentW < picPage.Width 時 xOffset > 0（兩側留白）；
        // contentW > picPage.Width 時 xOffset < 0（圖左右各削掉一半，視覺仍居中）
        int xOffset = (picPage.Width - contentW) / 2;
        for (int i = first; i <= last; i++)
        {
            Image? img;
            lock (_cacheLock) { _cache.TryGetValue(i, out img); }
            int layoutY = _layout.GetPageY(i);
            int h = _layout.GetPageHeight(i);
            int localY = layoutY - _virtualScrollY;
            if (img == null)
            {
                using var brush = new SolidBrush(Color.FromArgb(80, 80, 80));
                g.FillRectangle(brush, xOffset, localY, contentW, h);
                using var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                g.DrawString("讀取中…", lblStatus.Font, Brushes.White, new RectangleF(xOffset, localY, contentW, h), sf);
                continue;
            }
            g.DrawImage(img, new Rectangle(xOffset, localY, contentW, h));
        }
    }

    /// <summary>scroll / zoom / GoToPage 後同步狀態：更新 _currentIndex、cache window、TryRecord。</summary>
    private void UpdateContinuousState()
    {
        if (_layout == null) return;
        int scrollY = _virtualScrollY;
        int viewH = picPage.Height;
        int center = scrollY + viewH / 2;
        int pageIdx = _layout.FindPageAtY(center);
        var (first, last) = _layout.GetVisibleRange(scrollY, scrollY + viewH);
        if (pageIdx >= 0 && pageIdx != _currentIndex)
        {
            _currentIndex = pageIdx;
            _pagesVisited++;
            UpdatePageInfo();
            UpdateStatusBar();
            TryRecordRead();
        }
        // visible window cache（含 ±PreloadRadius）
        if (first >= 0) UpdateContinuousCacheWindow(first, last);
        // 看到末頁 → Auto Completed
        if (last >= _source.PageCount - 1) TryAutoComplete();
        // SaveProgress 走 debounce
        _scrollDebounce.Stop();
        _scrollDebounce.Start();
    }

    private void UpdateContinuousCacheWindow(int first, int last)
    {
        int lo = Math.Max(0, first - PreloadRadius);
        int hi = Math.Min(_source.PageCount - 1, last + PreloadRadius);
        List<int> toEvict;
        lock (_cacheLock)
        {
            toEvict = _cache.Keys.Where(k => k < lo || k > hi).ToList();
            foreach (var k in toEvict)
            {
                if (_cache.TryGetValue(k, out var img)) { img.Dispose(); _cache.Remove(k); }
            }
        }
        for (int i = lo; i <= hi; i++) _ = LoadPageAsync(i);
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
        _recordCheckTimer.Stop();
        _recordCheckTimer.Dispose();
        _scrollDebounce.Stop();
        _scrollDebounce.Dispose();
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
        // Continuous 永遠單頁顯示
        int size = IsContinuous ? 1 : SpreadSize(start);
        var pageStr = size == 1
            ? $"{start + 1} / {_source.PageCount}"
            : $"{start + 1}-{start + 2} / {_source.PageCount}";
        // PageHeader 主標 = 分頁資訊；SubText（DisplayName）在 ctor 已設好不變
        windowBar.Text = pageStr;
    }

    private void UpdateStatusBar()
    {
        var name = _source.GetPageName(_currentIndex);
        var dir = _rtl ? "RTL" : "LTR";
        var mode = _viewMode switch
        {
            ReaderViewMode.SinglePage => "單頁",
            ReaderViewMode.DualPage => "雙頁",
            ReaderViewMode.ContinuousScroll => "捲動",
            _ => "",
        };
        if (IsContinuous)
        {
            var zoomPct = sliderZoom.Value;
            lblStatus.Text = $"{name}    {dir}    {mode}    {zoomPct}%";
            return;
        }
        // 顯示「主」頁尺寸（雙頁模式 = leftImg；single mode 也是 leftImg）
        var dim = _leftImage != null ? $"{_leftImage.Width}×{_leftImage.Height}" : "-";
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


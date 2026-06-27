using System;
using System.Drawing;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// ReaderForm 的 SkiaSharp/GPU 繪製整合（並存+開關策略）。
/// 現有 GDI+ picPage 路徑保留當預設/後備；GPU 開關開 + GL 起得來 + 非譯文 時，改顯示 SKGLControl 走 GPU。
/// GL 失敗 → 自動關旗標、永遠退回 GDI+。
///
/// Phase 1 單頁 / Phase 2 雙頁(皆限 FitPage) / Phase 3 連續捲動(純 Skia、不雙重解碼)。
/// 譯文(Phase 4)仍退回 GDI+。FontSizeOptimizer/BubbleEnvelope/TextEraser 一行不改。
/// </summary>
public partial class ReaderForm
{
    private const int GpuPreloadRadius = 4;
    private const int GpuCacheCap = 24;       // 連續模式可見窗大，需較大快取(對齊 GDI+ MaxCacheSize)
    private static readonly SKSamplingOptions GpuSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private bool _useGpuPref;                 // AppConfig.UseGpuRendering（建構式設定）
    private SKGLControl? _skCanvas;           // GPU 畫布；null = 未建(開關關或建立失敗前)
    private SkiaPageRenderer? _skRenderer;    // 解碼/LRU/預載
    private bool _glAvailable;                // GL context 成功建立
    private string _glFailReason = "";

    /// <summary>目前是否該用 GPU 畫布（非譯文 + (單/雙頁且 FitPage) 或 連續）。</summary>
    private bool UseGpuNow =>
        _useGpuPref && _glAvailable && !_enableTranslation
        && (
            ((_viewMode == ReaderViewMode.SinglePage || _viewMode == ReaderViewMode.DualPage)
                && _zoomMode == ReaderZoomMode.FitPage)
            || _viewMode == ReaderViewMode.ContinuousScroll
        );

    /// <summary>在 Form Load(handle 已建)後呼叫：建 SKGLControl + renderer，GL 失敗則退回 GDI+。</summary>
    private void InitGpuCanvas()
    {
        if (!_useGpuPref || _skCanvas != null) return;
        try
        {
            var gl = new SKGLControl { Dock = DockStyle.Fill, Visible = false, TabStop = false };
            gl.PaintSurface += OnGpuPaintSurface;
            scrollHost.Controls.Add(gl);
            gl.CreateControl();              // 強制建 GL context，當場攔截失敗
            _skCanvas = gl;
            _glAvailable = true;
        }
        catch (Exception ex)
        {
            _glFailReason = ex.Message;
            _glAvailable = false;
            if (_skCanvas != null) { scrollHost.Controls.Remove(_skCanvas); _skCanvas.Dispose(); _skCanvas = null; }
            return;
        }

        // 解碼/快取/預載（與 PoC 共用元件）。一般模式 producer = 從來源解碼。
        _skRenderer = new SkiaPageRenderer(_source.PageCount, i => SkiaPageRenderer.DecodeFromSource(_source, i), GpuCacheCap);
        _skRenderer.PageReady += OnRendererPageReady;

        // GPU 畫布滑鼠輸入直接重用現有 handler（滾輪翻頁/連續捲動 / 左鍵拖曳移動視窗）。
        // 鍵盤導覽由 ProcessCmdKey 全域攔截，不需在此綁定(不受 focus 影響)。
        _skCanvas.MouseWheel += OnContentMouseWheel;
        _skCanvas.MouseDown += OnContentMouseDown;
        _skCanvas.MouseMove += OnContentMouseMove;
        _skCanvas.MouseUp += OnContentMouseUp;

        base.FormClosed += delegate { _skRenderer?.Dispose(); _skRenderer = null; };
        UpdateCanvasMode();
    }

    /// <summary>依目前狀態切換 GPU 畫布 / GDI+ picPage 的可見性。冪等、可隨時呼叫。</summary>
    private void UpdateCanvasMode()
    {
        if (_skCanvas == null) return;       // 未建 GPU(開關關/建立失敗) → 永遠走 picPage
        if (UseGpuNow)
        {
            if (!_skCanvas.Visible)
            {
                picPage.Visible = false;
                _skCanvas.Visible = true;
                _skCanvas.BringToFront();
            }
            if (IsContinuous)
            {
                if (vScrollBarContinuous.Visible) vScrollBarContinuous.BringToFront();  // 捲軸需在 GL 畫布之上
                RequestGpuContinuousAroundCurrent();
                _skCanvas.Invalidate();
            }
            else SyncGpuPage();
        }
        else
        {
            if (_skCanvas.Visible)
            {
                _skCanvas.Visible = false;
                picPage.Visible = true;
                EnsureGdiCurrentLoaded();   // GPU→GDI+：GPU 模式時略過了 GDI+ 載入，這裡惰性補回當前頁
            }
        }
    }

    /// <summary>GPU 可見 → invalidate 畫布；否則 picPage。連續捲動重繪共用此入口。</summary>
    private void InvalidateContentCanvas()
    {
        if (_skCanvas != null && _skCanvas.Visible) _skCanvas.Invalidate();
        else picPage.Invalidate();
    }

    /// <summary>切回 GDI+ 時確保當前 spread 已在 GDI+ 快取(否則觸發載入，載完自動 TryRenderCurrentSpread)。</summary>
    private void EnsureGdiCurrentLoaded()
    {
        if (IsContinuous) return;
        int start = SpreadStart(_currentIndex);
        int size = SpreadSize(start);
        bool needLoad = false;
        lock (_cacheLock)
            for (int i = 0; i < size; i++)
                if (!_cache.ContainsKey(start + i)) { needLoad = true; break; }
        if (needLoad)
            for (int i = 0; i < size; i++) _ = LoadPageAsync(start + i);
        else
            TryRenderCurrentSpread();
    }

    /// <summary>
    /// 導覽/快捷鍵不受 focus 影響：工具列(AntdUI 控件)或 GL 畫布獲焦時，form 的 KeyDown 收不到方向鍵，
    /// 故在此攔截、統一轉給 OnKeyDown。非 GPU 專屬，整個閱讀器受惠。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var e = new KeyEventArgs(keyData);
        OnKeyDown(this, e);
        if (e.Handled) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── 單頁/雙頁 ─────────────────────────────────────────

    /// <summary>同步當前 spread 到 renderer：設 Current、確保整個 spread 已解碼(避免空白)、預載鄰頁、重繪。</summary>
    private void SyncGpuPage()
    {
        if (_skRenderer == null || _skCanvas == null) return;
        int start = SpreadStart(_currentIndex);
        _skRenderer.Current = start;
        int size = (_viewMode == ReaderViewMode.DualPage) ? SpreadSize(start) : 1;
        for (int i = 0; i < size; i++)
        {
            int p = start + i;
            if (p < _source.PageCount && !_skRenderer.Contains(p)) _skRenderer.GetOrProduce(p);  // 同步解碼避免空白
        }
        _skRenderer.RequestAround(start, GpuPreloadRadius);
        _skCanvas.Invalidate();
    }

    private void OnRendererPageReady(int idx)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (IsContinuous)
        {
            try { BeginInvoke(() => OnGpuContinuousPageDecoded(idx)); } catch { /* 關閉中 */ }
            return;
        }
        int start = SpreadStart(_currentIndex);
        int size = (_viewMode == ReaderViewMode.DualPage) ? SpreadSize(start) : 1;
        if (idx < start || idx >= start + size) return;   // 只在「可見 spread」內的頁解好才重繪
        try { BeginInvoke(() => { if (UseGpuNow) _skCanvas?.Invalidate(); }); } catch { /* 關閉中 */ }
    }

    /// <summary>GPU 繪製：依模式分派 連續/雙頁/單頁。</summary>
    private void OnGpuPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(ReaderContentBg.R, ReaderContentBg.G, ReaderContentBg.B));
        if (!UseGpuNow || _skRenderer == null) return;

        int cw = e.Info.Width, ch = e.Info.Height;
        if (cw <= 0 || ch <= 0) return;

        if (IsContinuous) { DrawGpuContinuous(canvas, cw, ch); return; }

        int start = SpreadStart(_currentIndex);
        int size = (_viewMode == ReaderViewMode.DualPage) ? SpreadSize(start) : 1;
        if (size == 2) DrawGpuDual(canvas, cw, ch, start);
        else { var img = GetGpuImage(start); if (img != null) DrawFitCentered(canvas, img, cw, ch); }
    }

    /// <summary>雙頁：左右並排，依 RTL 決定左右頁；整組 fit-to-window 置中，各自垂直置中。</summary>
    private void DrawGpuDual(SKCanvas canvas, int cw, int ch, int start)
    {
        int idxL = _rtl ? start + 1 : start;
        int idxR = _rtl ? start : start + 1;
        var limg = GetGpuImage(idxL);
        var rimg = GetGpuImage(idxR);
        if (limg == null || rimg == null)
        {
            var any = limg ?? rimg;                       // 其一未解好 → 暫畫單張避免空白
            if (any != null) DrawFitCentered(canvas, any, cw, ch);
            return;
        }
        const float gap = 4f;
        int combW = limg.Width + rimg.Width;
        int combH = Math.Max(limg.Height, rimg.Height);
        if (combW <= 0 || combH <= 0) return;
        float s = Math.Min((cw - gap) / combW, (float)ch / combH);
        float lw = limg.Width * s, lh = limg.Height * s;
        float rw = rimg.Width * s, rh = rimg.Height * s;
        float ox = (cw - (lw + gap + rw)) / 2f;
        canvas.DrawImage(limg, SKRect.Create(ox, (ch - lh) / 2f, lw, lh), GpuSampling);
        canvas.DrawImage(rimg, SKRect.Create(ox + lw + gap, (ch - rh) / 2f, rw, rh), GpuSampling);
    }

    private void DrawFitCentered(SKCanvas canvas, SKImage img, int cw, int ch)
    {
        if (img.Width <= 0 || img.Height <= 0) return;
        float s = Math.Min((float)cw / img.Width, (float)ch / img.Height);
        float w = img.Width * s, h = img.Height * s;
        canvas.DrawImage(img, SKRect.Create((cw - w) / 2f, (ch - h) / 2f, w, h), GpuSampling);
    }

    private SKImage? GetGpuImage(int idx)
    {
        if (_skRenderer == null || idx < 0 || idx >= _source.PageCount) return null;
        return _skRenderer.Peek(idx) ?? _skRenderer.GetOrProduce(idx);
    }

    // ── 連續捲動（純 Skia：版面量測由 SKImage 尺寸驅動，不經 GDI+ _cache）──

    /// <summary>連續繪製：可視範圍各頁依 layout 位置疊圖；未解好的頁畫佔位底色並由 PageReady 補。</summary>
    private void DrawGpuContinuous(SKCanvas canvas, int cw, int ch)
    {
        if (_layout == null || _skRenderer == null) return;
        var (first, last) = _layout.GetVisibleRange(_virtualScrollY, _virtualScrollY + ch);
        if (first < 0) return;

        int contentWidth = _layout.ContentWidth;
        int sbw = vScrollBarContinuous.Visible ? vScrollBarContinuous.Width : 0;
        int x = Math.Max(0, (cw - sbw - contentWidth) / 2);

        using var phPaint = new SKPaint { Color = new SKColor(LoadingPlaceholderBg.R, LoadingPlaceholderBg.G, LoadingPlaceholderBg.B) };
        for (int i = first; i <= last; i++)
        {
            int y = _layout.GetPageY(i) - _virtualScrollY;
            int ph = _layout.GetPageHeight(i);
            var img = _skRenderer.Peek(i);
            if (img == null)
            {
                canvas.DrawRect(SKRect.Create(x, y, contentWidth, ph), phPaint);
                continue;
            }
            canvas.DrawImage(img, SKRect.Create(x, y, contentWidth, ph), GpuSampling);
        }
    }

    /// <summary>連續：向 renderer 要 [first-6, last+6] 可見窗解碼。</summary>
    private void RequestGpuContinuousWindow(int first, int last)
    {
        if (_skRenderer == null) return;
        int from = Math.Max(0, first - 6);
        int to = Math.Min(_source.PageCount - 1, last + 6);
        _skRenderer.RequestRange(from, to);
    }

    /// <summary>連續：依當前捲動位置算可見窗並請求解碼(進連續模式/切到 GPU 時呼叫)。</summary>
    private void RequestGpuContinuousAroundCurrent()
    {
        if (_skRenderer == null || _layout == null) return;
        _skRenderer.Current = _currentIndex;
        int vh = picPage.Height > 0 ? picPage.Height : scrollHost.ClientSize.Height;
        var (first, last) = _layout.GetVisibleRange(_virtualScrollY, _virtualScrollY + vh);
        if (first < 0)
        {
            _skRenderer.RequestRange(Math.Max(0, _currentIndex - 3), Math.Min(_source.PageCount - 1, _currentIndex + 3));
            return;
        }
        RequestGpuContinuousWindow(first, last);
    }

    /// <summary>連續模式某頁 Skia 解好(UI 執行緒)：用 SKImage 尺寸量測 layout + 更新捲軸 max + 重繪。</summary>
    private void OnGpuContinuousPageDecoded(int idx)
    {
        if (_layout == null || _skRenderer == null) return;
        var img = _skRenderer.Peek(idx);
        if (img != null && !_layout.IsMeasured(idx))
        {
            _layout.SetMeasured(idx, img.Width, img.Height);
            int vh = picPage.Height;
            int maxY = Math.Max(0, _layout.TotalHeight - vh);
            int newMax = maxY + vScrollBarContinuous.LargeChange - 1;
            if (vScrollBarContinuous.Maximum != newMax) vScrollBarContinuous.Maximum = newMax;
            if (_virtualScrollY > maxY)
            {
                _virtualScrollY = maxY;
                vScrollBarContinuous.Value = Math.Min(_virtualScrollY, vScrollBarContinuous.Maximum);
            }
        }
        if (UseGpuNow && IsContinuous) _skCanvas?.Invalidate();
    }
}

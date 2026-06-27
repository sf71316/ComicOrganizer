using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

    /// <summary>目前是否該用 GPU 畫布（(單/雙頁且 FitPage) 或 連續；含譯文模式）。</summary>
    private bool UseGpuNow =>
        _useGpuPref && _glAvailable
        && (
            ((_viewMode == ReaderViewMode.SinglePage || _viewMode == ReaderViewMode.DualPage)
                && _zoomMode == ReaderZoomMode.FitPage)
            || _viewMode == ReaderViewMode.ContinuousScroll
        );

    // 譯文模式：底圖+抹除+overlay 合成成 natural-size SKImage 快取(鍵=頁索引)；
    // 譯文套用(line 2076)時 DropTranslatedGpu 失效該頁、重合成。底圖仍由 GDI+ 管線載入(_cache)供 OCR+合成。
    private readonly Dictionary<int, SKImage> _gpuTransCache = new();
    // 譯文 worker 完成(erase 後、line 2078 hook)的頁 → 此時對底圖的 GDI+ 存取已結束，可安全在 UI 合成。
    // 未在此集合的頁：worker 可能正背景讀同一 bitmap，合成會撞「generic error in GDI+」→ 改顯示 Skia 獨立解碼底圖。
    private readonly HashSet<int> _gpuComposeReady = new();
    private readonly object _gpuTransLock = new();
    private const int GpuTransCacheCap = 16;

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

        base.FormClosed += delegate { _skRenderer?.Dispose(); _skRenderer = null; ClearTranslatedGpu(); };
        UpdateCanvasMode();
    }

    /// <summary>依目前狀態切換 GPU 畫布 / GDI+ picPage 的可見性。冪等、可隨時呼叫。</summary>
    private void UpdateCanvasMode()
    {
        if (_skCanvas == null) return;       // 未建 GPU(開關關/建立失敗) → 永遠走 picPage
        if (!_enableTranslation) ClearTranslatedGpu();   // 譯文關 → 丟棄譯文合成快取
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
                if (!_enableTranslation) RequestGpuContinuousAroundCurrent();  // 譯文時底圖由 GDI+ 管線載入
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

    /// <summary>同步當前 spread：非譯文走 Skia renderer(解碼/預載)；譯文則確保 GDI+ _cache 底圖已載(供合成)。</summary>
    private void SyncGpuPage()
    {
        if (_skRenderer == null || _skCanvas == null) return;
        int start = SpreadStart(_currentIndex);
        int size = (_viewMode == ReaderViewMode.DualPage) ? SpreadSize(start) : 1;
        if (_enableTranslation)
        {
            // 譯文：底圖由 GDI+ 管線載入(OCR+合成都需要)；缺則觸發載入，載完 TryRenderCurrentSpread→重繪→合成。
            for (int i = 0; i < size; i++)
            {
                int p = start + i;
                bool has;
                lock (_cacheLock) has = _cache.ContainsKey(p);
                if (!has && p < _source.PageCount) _ = LoadPageAsync(p);
            }
            _skCanvas.Invalidate();
            return;
        }
        _skRenderer.Current = start;
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
        else { var img = GpuImageFor(start); if (img != null) DrawFitCentered(canvas, img, cw, ch); }
    }

    /// <summary>雙頁：左右並排，依 RTL 決定左右頁；整組 fit-to-window 置中，各自垂直置中。</summary>
    private void DrawGpuDual(SKCanvas canvas, int cw, int ch, int start)
    {
        int idxL = _rtl ? start + 1 : start;
        int idxR = _rtl ? start : start + 1;
        var limg = GpuImageFor(idxL);
        var rimg = GpuImageFor(idxR);
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

    /// <summary>單/雙頁取圖：譯文走合成、否則 raw 解碼。</summary>
    private SKImage? GpuImageFor(int idx) =>
        _enableTranslation ? GetTranslatedGpuImage(idx) : GetGpuImage(idx);

    /// <summary>連續取圖：譯文走合成；非譯文只 Peek(不在 paint 同步解碼，缺則佔位、由預載補)。</summary>
    private SKImage? GpuContinuousImage(int idx) =>
        _enableTranslation ? GetTranslatedGpuImage(idx) : _skRenderer?.Peek(idx);

    // ── Phase 4：譯文合成 ─────────────────────────────────

    /// <summary>
    /// 譯文取圖：頁譯文已完成(worker 結束、_gpuComposeReady) → GDI+ 合成(快取)；
    /// 未完成 → 啟動譯文 + 顯示 Skia 獨立解碼的原始底圖(不碰 GDI+ 共用 bitmap，避免並發 GDI+ 錯誤)。
    /// </summary>
    private SKImage? GetTranslatedGpuImage(int idx)
    {
        if (idx < 0 || idx >= _source.PageCount) return null;

        bool ready;
        lock (_gpuTransLock)
        {
            if (_gpuTransCache.TryGetValue(idx, out var hit)) return hit;
            ready = _gpuComposeReady.Contains(idx);
        }

        if (ready)
        {
            Image? baseImg;
            lock (_cacheLock) _cache.TryGetValue(idx, out baseImg);
            if (baseImg != null)
            {
                var sk = CompositeTranslatedPage(idx, baseImg);   // worker 已結束 → 無並發
                if (sk != null)
                {
                    lock (_gpuTransLock)
                    {
                        if (_gpuTransCache.TryGetValue(idx, out var existed)) { sk.Dispose(); return existed; }
                        if (_gpuTransCache.Count >= GpuTransCacheCap) EvictTranslatedGpu(idx);
                        _gpuTransCache[idx] = sk;
                    }
                    return sk;
                }
            }
        }

        // 未完成 → 確保譯文已啟動 + 暫顯示 Skia 原始底圖
        EnsurePageTranslated(idx);
        return _skRenderer?.Peek(idx) ?? _skRenderer?.GetOrProduce(idx);
    }

    /// <summary>譯文 worker 完成某頁(erase 後)：標記可合成、丟棄舊合成、重繪。line 2078 hook 呼叫(UI 執行緒)。</summary>
    private void OnGpuPageTranslationApplied(int idx)
    {
        lock (_gpuTransLock) _gpuComposeReady.Add(idx);
        DropTranslatedGpu(idx);            // 之前若快取過原圖合成 → 丟棄、下次重合成含譯文
        InvalidateContentCanvas();
    }

    /// <summary>把「底圖(或抹除圖)+譯文 overlay」用現有 GDI+ 路徑合成到 natural-size bitmap → SKImage。</summary>
    private SKImage? CompositeTranslatedPage(int idx, Image baseImg)
    {
        try
        {
            Image painted = PageImageForPaint(idx, baseImg);     // 抹除圖(路線2)或原圖
            int w = painted.Width, h = painted.Height;
            if (w <= 0 || h <= 0) return null;
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(painted, new Rectangle(0, 0, w, h));
                RenderTranslationOverlay(g, painted, new Rectangle(0, 0, w, h), idx);  // 1:1，overlay 用原座標
            }
            return SKImageFromBitmap(bmp);
        }
        catch { return null; }
    }

    /// <summary>GDI+ Bitmap(Format32bppArgb) → SKImage(複製像素，可立即 dispose bmp)。合成頁全不透明、premul/straight 同值。</summary>
    private static SKImage SKImageFromBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var pixmap = new SKPixmap(info, data.Scan0, data.Stride);
            return SKImage.FromPixelCopy(pixmap);
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>該頁譯文/抹除狀態變更 → 丟棄合成快取使其重合成（line 2076 hook）。</summary>
    private void DropTranslatedGpu(int idx)
    {
        SKImage? old = null;
        lock (_gpuTransLock) if (_gpuTransCache.Remove(idx, out old)) { }
        old?.Dispose();
    }

    private void ClearTranslatedGpu()
    {
        List<SKImage> imgs;
        lock (_gpuTransLock)
        {
            _gpuComposeReady.Clear();
            if (_gpuTransCache.Count == 0) return;
            imgs = new List<SKImage>(_gpuTransCache.Values);
            _gpuTransCache.Clear();
        }
        foreach (var i in imgs) i.Dispose();
    }

    /// <summary>容量滿時淘汰一個非保留頁(避開 keep)。需在 _gpuTransLock 內呼叫。</summary>
    private void EvictTranslatedGpu(int keep)
    {
        foreach (var k in _gpuTransCache.Keys)
        {
            if (k == keep || k == _currentIndex) continue;
            if (_gpuTransCache.Remove(k, out var img)) img?.Dispose();
            return;
        }
        // 全是保留頁(極少見) → 清最舊一個
        foreach (var k in _gpuTransCache.Keys)
        {
            if (_gpuTransCache.Remove(k, out var img)) img?.Dispose();
            return;
        }
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
            var img = GpuContinuousImage(i);
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

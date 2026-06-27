using SkiaSharp;
using SkiaSharp.Views.Desktop;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// SkiaSharp/GPU 翻頁 PoC 探測視窗（最小範圍，與正式 ReaderForm 完全隔離）。
/// 目的：回答兩個 go/no-go 問題 —— (1) GL context 在目標機起不起得來；(2) GPU 翻頁順不順。
/// 解碼 / LRU 快取 / 鄰頁預載 委由共用的 <see cref="SkiaPageRenderer"/>（與正式整合同一份）。
/// GL 失敗 → fallback 到 <see cref="SKControl"/>(CPU raster)，標題列標示模式。
/// </summary>
public sealed class SkiaReaderProbeForm : Form
{
    private const int PreloadRadius = 3;

    private readonly IReaderSource _source;
    private readonly SkiaPageRenderer _renderer;

    private Control _canvas = null!;         // SKGLControl(GPU) 或 SKControl(CPU)
    private bool _useGpu;
    private string _fallbackReason = "";

    private int _index;
    private bool _lastFlipHit;               // 上次翻頁是否命中快取(順暢度指標)

    private static readonly SKSamplingOptions Sampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    public SkiaReaderProbeForm(IReaderSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _renderer = new SkiaPageRenderer(source.PageCount, i => SkiaPageRenderer.DecodeFromSource(source, i));
        _renderer.PageReady += OnPageReady;

        Text = "Skia GPU 探測";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 800);
        BackColor = Color.FromArgb(32, 32, 32);
        KeyPreview = true;
        DoubleBuffered = true;

        // 在 Load 才建 canvas：此時 Form handle 已存在，子控件 CreateControl 會真正建出
        // GL context → GL 失敗能當場攔截並乾淨 fallback（建構式階段 handle 未建，攔不到）。
        Load += delegate
        {
            BuildCanvas();
            EnsureCurrentDecoded();
            _renderer.RequestAround(_index, PreloadRadius);
            UpdateTitle();
        };
        KeyDown += OnProbeKeyDown;
        FormClosed += delegate { _renderer.Dispose(); _source.Dispose(); };
    }

    /// <summary>嘗試建 SKGLControl(GPU)；GL 起不來 → 退回 SKControl(CPU)。</summary>
    private void BuildCanvas()
    {
        try
        {
            var gl = new SKGLControl { Dock = DockStyle.Fill };
            gl.PaintSurface += OnPaintGpu;
            Controls.Add(gl);
            gl.CreateControl();          // 強制建 handle/GL context，當場攔截 GL 失敗
            _canvas = gl;
            _useGpu = true;
        }
        catch (Exception ex)
        {
            _fallbackReason = ex.Message;
            for (int i = Controls.Count - 1; i >= 0; i--)
                if (Controls[i] is SKGLControl) { Controls[i].Dispose(); Controls.RemoveAt(i); }

            var sw = new SKControl { Dock = DockStyle.Fill };
            sw.PaintSurface += OnPaintCpu;
            Controls.Add(sw);
            _canvas = sw;
            _useGpu = false;
        }
        _canvas.MouseWheel += OnCanvasWheel;
    }

    private void OnPaintGpu(object? sender, SKPaintGLSurfaceEventArgs e)
        => DrawScene(e.Surface.Canvas, e.Info.Width, e.Info.Height);

    private void OnPaintCpu(object? sender, SKPaintSurfaceEventArgs e)
        => DrawScene(e.Surface.Canvas, e.Info.Width, e.Info.Height);

    /// <summary>fit-to-window 置中畫當前頁 + 左上角狀態 overlay。</summary>
    private void DrawScene(SKCanvas canvas, int cw, int ch)
    {
        canvas.Clear(new SKColor(32, 32, 32));
        var img = _renderer.Peek(_index);
        if (img != null && cw > 0 && ch > 0)
        {
            float scale = Math.Min((float)cw / img.Width, (float)ch / img.Height);
            float w = img.Width * scale, h = img.Height * scale;
            float x = (cw - w) / 2f, y = (ch - h) / 2f;
            canvas.DrawImage(img, SKRect.Create(x, y, w, h), Sampling);
        }
        DrawOverlay(canvas);
    }

    private void DrawOverlay(SKCanvas canvas)
    {
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { Color = SKColors.Lime, IsAntialias = true };
        string mode = _useGpu ? "GPU (SKGLControl)" : $"CPU fallback (SKControl) — {_fallbackReason}";
        canvas.DrawText(mode, 12, 24, font, paint);
        canvas.DrawText($"page {_index + 1}/{_source.PageCount}  {_source.GetPageName(_index)}", 12, 46, font, paint);
        canvas.DrawText($"flip: {(_lastFlipHit ? "HIT(快取)" : "MISS(現解)")}   cache: {_renderer.Count}/{_renderer.Capacity}", 12, 68, font, paint);
        canvas.DrawText("← → / 滾輪翻頁，Esc 關閉", 12, 90, font, paint);
    }

    // ── 翻頁 ──────────────────────────────────────────────

    private void Flip(int delta)
    {
        int next = _index + delta;
        if (next < 0 || next >= _source.PageCount) return;
        _index = next;
        EnsureCurrentDecoded();
        _renderer.RequestAround(_index, PreloadRadius);
        UpdateTitle();
    }

    /// <summary>當前頁：命中快取設 HIT；未命中同步 produce(設 MISS)避免空白。</summary>
    private void EnsureCurrentDecoded()
    {
        _renderer.Current = _index;
        _lastFlipHit = _renderer.Contains(_index);
        if (!_lastFlipHit) _renderer.GetOrProduce(_index);
        _canvas.Invalidate();
    }

    private void OnPageReady(int idx)
    {
        if (idx != _index || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => _canvas.Invalidate()); } catch { /* 關閉中 */ }
    }

    private void OnCanvasWheel(object? sender, MouseEventArgs e) => Flip(e.Delta > 0 ? -1 : 1);

    private void OnProbeKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.PageUp:
            case Keys.Up:
                Flip(-1); e.Handled = true; break;
            case Keys.Right:
            case Keys.PageDown:
            case Keys.Down:
            case Keys.Space:
                Flip(1); e.Handled = true; break;
            case Keys.Escape:
                Close(); break;
        }
    }

    private void UpdateTitle() =>
        Text = $"Skia GPU 探測 — {(_useGpu ? "GPU" : "CPU")} — {_source.DisplayName} [{_index + 1}/{_source.PageCount}]";
}

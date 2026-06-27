using System.Collections.Concurrent;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// SkiaSharp/GPU 翻頁 PoC 探測視窗（最小範圍，與正式 ReaderForm 完全隔離）。
/// 目的：回答兩個 go/no-go 問題 ——
///   (1) GL context 在目標機(含 RDP/VM)起不起得來；
///   (2) GPU 底圖繪製翻頁順不順。
/// 重用真實 <see cref="IReaderSource"/>(資料夾/壓縮檔) 解碼真實頁面。
/// 不含：譯文/OCR/連續捲動/雙頁/書籤/抹除 —— 那些維持 GDI+，不在 PoC 範圍。
/// GL 失敗 → fallback 到 <see cref="SKControl"/>(CPU raster)，標題列標示模式。
///
/// item 3：SKImage LRU 快取 + 背景 worker 預載鄰頁(前後各 <see cref="PreloadRadius"/> 頁)。
/// 翻頁命中快取直接畫(GPU texture 由 Skia 內部快取重用)；未命中才同步解碼避免空白。
/// </summary>
public sealed class SkiaReaderProbeForm : Form
{
    private const int PreloadRadius = 3;     // 前後各預載頁數
    private const int CacheCap = 9;          // SKImage LRU 上限(當前 + 前後各 ~4)

    private readonly IReaderSource _source;

    private Control _canvas = null!;         // SKGLControl(GPU) 或 SKControl(CPU)
    private bool _useGpu;
    private string _fallbackReason = "";

    private volatile int _index;
    private bool _lastFlipHit;               // 上次翻頁是否命中快取(順暢度指標)

    // SKImage LRU 快取（front=MRU、back=LRU）
    private readonly Dictionary<int, SKImage> _cache = new();
    private readonly LinkedList<int> _lru = new();
    private readonly object _cacheLock = new();

    // 背景預載 worker
    private readonly ConcurrentQueue<int> _decodeQueue = new();
    private readonly AutoResetEvent _decodeSignal = new(false);
    private Thread? _worker;
    private volatile bool _shuttingDown;

    private static readonly SKSamplingOptions Sampling =
        new(SKFilterMode.Linear, SKMipmapMode.None);

    public SkiaReaderProbeForm(IReaderSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));

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
            StartWorker();
            EnsureCurrentDecoded();
            RequestAround(_index);
            UpdateTitle();
        };
        KeyDown += OnProbeKeyDown;
        FormClosed += delegate { Shutdown(); };
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

        SKImage? img;
        int cacheCount;
        lock (_cacheLock)
        {
            _cache.TryGetValue(_index, out img);
            if (img != null) TouchLru(_index);
            cacheCount = _cache.Count;
        }

        if (img != null && cw > 0 && ch > 0)
        {
            float scale = Math.Min((float)cw / img.Width, (float)ch / img.Height);
            float w = img.Width * scale, h = img.Height * scale;
            float x = (cw - w) / 2f, y = (ch - h) / 2f;
            canvas.DrawImage(img, SKRect.Create(x, y, w, h), Sampling);
        }
        DrawOverlay(canvas, cacheCount);
    }

    private void DrawOverlay(SKCanvas canvas, int cacheCount)
    {
        using var font = new SKFont(SKTypeface.Default, 16);
        using var paint = new SKPaint { Color = SKColors.Lime, IsAntialias = true };
        string mode = _useGpu ? "GPU (SKGLControl)" : $"CPU fallback (SKControl) — {_fallbackReason}";
        canvas.DrawText(mode, 12, 24, font, paint);
        canvas.DrawText($"page {_index + 1}/{_source.PageCount}  {_source.GetPageName(_index)}", 12, 46, font, paint);
        canvas.DrawText($"flip: {(_lastFlipHit ? "HIT(快取)" : "MISS(現解)")}   cache: {cacheCount}/{CacheCap}", 12, 68, font, paint);
        canvas.DrawText("← → / 滾輪翻頁，Esc 關閉", 12, 90, font, paint);
    }

    // ── 翻頁 ──────────────────────────────────────────────

    private void Flip(int delta)
    {
        int next = _index + delta;
        if (next < 0 || next >= _source.PageCount) return;
        _index = next;
        EnsureCurrentDecoded();      // 命中→直接畫；未命中→同步解碼避免空白
        RequestAround(_index);       // 預載新位置鄰頁
        UpdateTitle();
    }

    /// <summary>當前頁：命中快取設 HIT；未命中則同步解碼(設 MISS)再插入，確保不空白。</summary>
    private void EnsureCurrentDecoded()
    {
        int idx = _index;
        bool hit;
        lock (_cacheLock) hit = _cache.ContainsKey(idx);
        _lastFlipHit = hit;
        if (!hit)
        {
            var img = DecodeToImage(idx);
            if (img != null) Insert(idx, img);
        }
        _canvas.Invalidate();
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

    // ── 背景預載 worker ───────────────────────────────────

    private void StartWorker()
    {
        _worker = new Thread(DecodeLoop) { IsBackground = true, Name = "SkiaProbePreload" };
        _worker.Start();
    }

    /// <summary>把 index 鄰頁(由近到遠)排進解碼佇列並喚醒 worker。</summary>
    private void RequestAround(int index)
    {
        for (int d = 0; d <= PreloadRadius; d++)
        {
            if (d == 0) { Enqueue(index); continue; }
            Enqueue(index + d);
            Enqueue(index - d);
        }
        _decodeSignal.Set();
    }

    private void Enqueue(int index)
    {
        if (index < 0 || index >= _source.PageCount) return;
        lock (_cacheLock) if (_cache.ContainsKey(index)) return;
        _decodeQueue.Enqueue(index);
    }

    private void DecodeLoop()
    {
        while (!_shuttingDown)
        {
            _decodeSignal.WaitOne();
            while (!_shuttingDown && _decodeQueue.TryDequeue(out int idx))
            {
                lock (_cacheLock) if (_cache.ContainsKey(idx)) continue;
                var img = DecodeToImage(idx);
                if (img == null) continue;
                if (_shuttingDown) { img.Dispose(); break; }
                Insert(idx, img);
                if (idx == _index) BeginInvokeInvalidate();
            }
        }
    }

    /// <summary>編碼 bytes → 完整解碼成 raster SKImage（背景執行緒亦可呼叫；GPU 上傳留待繪製）。</summary>
    private SKImage? DecodeToImage(int index)
    {
        try
        {
            using var stream = _source.OpenPage(index);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var data = SKData.CreateCopy(ms.GetBuffer(), (ulong)ms.Length);
            using var bmp = SKBitmap.Decode(data);   // 背景完整解碼像素(非惰性)
            return bmp == null ? null : SKImage.FromBitmap(bmp);
        }
        catch { return null; }
    }

    // ── LRU 快取 ──────────────────────────────────────────

    private void Insert(int index, SKImage img)
    {
        SKImage? toReplace = null;
        var evicted = new List<SKImage>();
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(index, out toReplace))
            {
                // 已有(理論上少見) → 用新的取代，舊的稍後 dispose
                _cache[index] = img;
                TouchLru(index);
            }
            else
            {
                _cache[index] = img;
                _lru.AddFirst(index);
            }
            // 淘汰：從 LRU 尾端移除，但永不淘汰當前頁
            while (_cache.Count > CacheCap)
            {
                var node = _lru.Last;
                while (node != null && node.Value == _index) node = node.Previous;
                if (node == null) break;
                _lru.Remove(node);
                if (_cache.Remove(node.Value, out var old)) evicted.Add(old);
            }
        }
        toReplace?.Dispose();
        foreach (var e in evicted) e.Dispose();
    }

    /// <summary>把 index 移到 LRU front（MRU）。需在 _cacheLock 內呼叫。</summary>
    private void TouchLru(int index)
    {
        var node = _lru.Find(index);
        if (node != null) { _lru.Remove(node); _lru.AddFirst(node); }
        else _lru.AddFirst(index);
    }

    // ── 雜項 ──────────────────────────────────────────────

    private void BeginInvokeInvalidate()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => _canvas.Invalidate()); } catch { /* 關閉中 */ }
    }

    private void UpdateTitle() =>
        Text = $"Skia GPU 探測 — {(_useGpu ? "GPU" : "CPU")} — {_source.DisplayName} [{_index + 1}/{_source.PageCount}]";

    private void Shutdown()
    {
        _shuttingDown = true;
        _decodeSignal.Set();
        try { _worker?.Join(500); } catch { }
        lock (_cacheLock)
        {
            foreach (var img in _cache.Values) img.Dispose();
            _cache.Clear();
            _lru.Clear();
        }
        _decodeSignal.Dispose();
        _source.Dispose();
    }
}

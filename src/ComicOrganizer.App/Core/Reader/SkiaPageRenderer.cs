using System.Collections.Concurrent;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// SkiaSharp/GPU 閱讀器的頁面供應器：解碼 / SKImage LRU 快取 / 背景鄰頁預載。
/// 不負責繪製或版面數學（縮放/pan/雙頁/連續各呼叫端不同），只保證「要畫某頁時拿得到 SKImage」。
///
/// 頁面內容由可插拔的 <c>produce</c> 委派決定：
///   - 一般模式：<see cref="DecodeFromSource"/>（來源 bytes → 完整解碼 raster SKImage）。
///   - 譯文模式(Phase 4)：改傳「GDI+ 合成 base+抹除+overlay → bitmap → SKImage」的委派，本類別不變。
///
/// 執行緒：背景 worker 完整解碼像素（GPU texture 上傳留到繪製，由 Skia 內部資源快取重用）；
/// 未命中時呼叫端可 <see cref="GetOrProduce"/> 同步產生避免空白。永不淘汰 <see cref="Current"/> 頁
/// （避免 dispose 正在繪製的 image）。本類別 thread-safe。
/// </summary>
public sealed class SkiaPageRenderer : IDisposable
{
    private readonly int _pageCount;
    private readonly Func<int, SKImage?> _produce;
    private readonly int _cacheCap;

    // SKImage LRU 快取（front=MRU、back=LRU）
    private readonly Dictionary<int, SKImage> _cache = new();
    private readonly LinkedList<int> _lru = new();
    private readonly object _cacheLock = new();

    // 背景預載 worker
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private volatile bool _shuttingDown;

    /// <summary>呼叫端在翻頁時設定的「當前頁」——永不被淘汰，避免 dispose 正在繪製的 image。</summary>
    public volatile int Current;

    /// <summary>背景 worker 完成某頁解碼後觸發（傳頁索引）。呼叫端若該頁可見則 Invalidate。</summary>
    public event Action<int>? PageReady;

    public SkiaPageRenderer(int pageCount, Func<int, SKImage?> produce, int cacheCap = 9)
    {
        _pageCount = pageCount;
        _produce = produce ?? throw new ArgumentNullException(nameof(produce));
        _cacheCap = Math.Max(3, cacheCap);
        _worker = new Thread(DecodeLoop) { IsBackground = true, Name = "SkiaPageRenderer" };
        _worker.Start();
    }

    /// <summary>來源 bytes → 完整解碼成 raster SKImage（背景執行緒亦可呼叫）。一般模式的預設 producer。</summary>
    public static SKImage? DecodeFromSource(IReaderSource source, int index)
    {
        try
        {
            using var stream = source.OpenPage(index);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var data = SKData.CreateCopy(ms.GetBuffer(), (ulong)ms.Length);
            using var bmp = SKBitmap.Decode(data);   // 完整解碼像素(非惰性)
            return bmp == null ? null : SKImage.FromBitmap(bmp);
        }
        catch { return null; }
    }

    /// <summary>取快取頁（命中才回，永不阻塞）。回 null 表示尚未解到。</summary>
    public SKImage? Peek(int index)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(index, out var img)) { TouchLru(index); return img; }
            return null;
        }
    }

    public bool Contains(int index)
    {
        lock (_cacheLock) return _cache.ContainsKey(index);
    }

    /// <summary>目前快取頁數（除錯/overlay 用）。</summary>
    public int Count
    {
        get { lock (_cacheLock) return _cache.Count; }
    }

    /// <summary>快取上限。</summary>
    public int Capacity => _cacheCap;

    /// <summary>取快取頁；未命中則同步 produce + 插入後回傳（呼叫端用於確保當前頁不空白）。</summary>
    public SKImage? GetOrProduce(int index)
    {
        lock (_cacheLock)
            if (_cache.TryGetValue(index, out var hit)) { TouchLru(index); return hit; }

        var img = _produce(index);
        if (img == null) return null;
        Insert(index, img);
        return img;
    }

    /// <summary>把 index 鄰頁(由近到遠)排進解碼佇列並喚醒 worker。</summary>
    public void RequestAround(int index, int radius)
    {
        for (int d = 0; d <= radius; d++)
        {
            if (d == 0) Enqueue(index);
            else { Enqueue(index + d); Enqueue(index - d); }
        }
        _signal.Set();
    }

    private void Enqueue(int index)
    {
        if (index < 0 || index >= _pageCount) return;
        lock (_cacheLock) if (_cache.ContainsKey(index)) return;
        _queue.Enqueue(index);
    }

    private void DecodeLoop()
    {
        while (!_shuttingDown)
        {
            _signal.WaitOne();
            while (!_shuttingDown && _queue.TryDequeue(out int idx))
            {
                lock (_cacheLock) if (_cache.ContainsKey(idx)) continue;
                var img = _produce(idx);
                if (img == null) continue;
                if (_shuttingDown) { img.Dispose(); break; }
                Insert(idx, img);
                PageReady?.Invoke(idx);
            }
        }
    }

    private void Insert(int index, SKImage img)
    {
        SKImage? replaced = null;
        var evicted = new List<SKImage>();
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(index, out replaced)) { _cache[index] = img; TouchLru(index); }
            else { _cache[index] = img; _lru.AddFirst(index); }

            while (_cache.Count > _cacheCap)
            {
                var node = _lru.Last;
                while (node != null && node.Value == Current) node = node.Previous;   // 永不淘汰當前頁
                if (node == null) break;
                _lru.Remove(node);
                if (_cache.Remove(node.Value, out var old)) evicted.Add(old);
            }
        }
        replaced?.Dispose();
        foreach (var e in evicted) e.Dispose();
    }

    /// <summary>把 index 移到 LRU front（MRU）。需在 _cacheLock 內呼叫。</summary>
    private void TouchLru(int index)
    {
        var node = _lru.Find(index);
        if (node != null) { _lru.Remove(node); _lru.AddFirst(node); }
        else _lru.AddFirst(index);
    }

    /// <summary>清空快取（譯文模式切換等情境，內容已失效時呼叫）。</summary>
    public void Clear()
    {
        lock (_cacheLock)
        {
            foreach (var img in _cache.Values) img.Dispose();
            _cache.Clear();
            _lru.Clear();
        }
        while (_queue.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        _shuttingDown = true;
        _signal.Set();
        try { _worker.Join(500); } catch { }
        Clear();
        _signal.Dispose();
    }
}

using System.Collections.Concurrent;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// <see cref="SkiaPageRenderer"/> 的影像參考計數租約（Lease）。
/// 呼叫端在繪製或讀取尺寸期間應以 <c>using var lease = ...</c> 持有，
/// 確保即使快取發生 LRU 淘汰或 <see cref="SkiaPageRenderer.Clear"/>，底層 <see cref="SKImage"/>
/// 仍會延後至所有租約釋放完畢才呼叫 <see cref="SKImage.Dispose"/>。
/// </summary>
public sealed class SkiaImageLease : IDisposable
{
    private SkiaPageRenderer.CacheSlot? _slot;
    private readonly SKImage _image;
    private int _disposed;

    internal SkiaImageLease(SkiaPageRenderer.CacheSlot slot)
    {
        _slot = slot;
        _image = slot.Image;
    }

    /// <summary>建立不參與快取生命週期的唯讀包裝（例如已由外部快取管理生命週期的譯文合成頁）。</summary>
    public SkiaImageLease(SKImage image)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
    }

    /// <summary>受租約保護的 <see cref="SKImage"/>。</summary>
    public SKImage Image => _image;

    /// <summary>釋放租約（冪等；僅在最後一個參考釋放時才處置底層影像）。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _slot?.Release();
        _slot = null;
    }
}

/// <summary>
/// SkiaSharp/GPU 閱讀器的頁面供應器：解碼 / SKImage LRU 快取 / 背景鄰頁預載。
/// 不負責繪製或版面數學（縮放/pan/雙頁/連續各呼叫端不同），只保證「要畫某頁時拿得到 SKImage」。
///
/// 頁面內容由可插拔的 <c>produce</c> 委派決定：
///   - 一般模式：<see cref="DecodeFromSource"/>（來源 bytes → 完整解碼 raster SKImage）。
///   - 譯文模式(Phase 4)：改傳「GDI+ 合成 base+抹除+overlay → bitmap → SKImage」的委派，本類別不變。
///
/// 執行緒：背景 worker 完整解碼像素（GPU texture 上傳留到繪製，由 Skia 內部資源快取重用）；
/// 未命中時呼叫端可 <see cref="AcquireOrProduce"/> / <see cref="GetOrProduce"/> 同步產生避免空白。
/// 透過 <see cref="SkiaImageLease"/> 參考計數與 <see cref="SetProtectedRange"/> 避免背景淘汰釋放繪製中影像。本類別 thread-safe。
/// </summary>
public sealed class SkiaPageRenderer : IDisposable
{
    internal sealed class CacheSlot
    {
        private int _refCount;
        public SKImage Image { get; }

        public CacheSlot(SKImage image)
        {
            Image = image ?? throw new ArgumentNullException(nameof(image));
            _refCount = 1; // 初始由快取本身持有 1 個參考
        }

        public void Retain()
        {
            while (true)
            {
                int current = Volatile.Read(ref _refCount);
                if (current <= 0)
                    throw new ObjectDisposedException(nameof(CacheSlot));
                if (Interlocked.CompareExchange(ref _refCount, current + 1, current) == current)
                    return;
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                Image.Dispose();
            }
        }
    }

    private readonly int _pageCount;
    private readonly Func<int, SKImage?> _produce;
    private readonly int _cacheCap;

    // SKImage LRU 快取（front=MRU、back=LRU）
    private readonly Dictionary<int, CacheSlot> _cache = new();
    private readonly LinkedList<int> _lru = new();
    private readonly object _cacheLock = new();
    private int _protectedFirst = -1;
    private int _protectedLast = -1;
    private int _clearGeneration;

    // 背景預載 worker
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly ConcurrentQueue<int> _priorityQueue = new();
    private readonly HashSet<int> _queuedNormal = new();
    private readonly HashSet<int> _queuedPriority = new();
    private readonly HashSet<int> _inFlight = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Thread _worker;
    private volatile bool _shuttingDown;
    private int _disposed;

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

    /// <summary>來源 bytes 或直接 bitmap 來源 → 完整解碼成 raster SKImage（背景執行緒亦可呼叫）。一般模式的預設 producer。</summary>
    public static SKImage? DecodeFromSource(IReaderSource source, int index)
    {
        try
        {
            if (source is ISkiaBitmapSource skiaSource)
            {
                using var directBmp = skiaSource.OpenPageBitmap(index);
                return directBmp == null ? null : SKImage.FromBitmap(directBmp);
            }

            using var stream = source.OpenPage(index);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            using var data = SKData.CreateCopy(ms.GetBuffer(), (ulong)ms.Length);
            using var bmp = SKBitmap.Decode(data);   // 完整解碼像素(非惰性)
            return bmp == null ? null : SKImage.FromBitmap(bmp);
        }
        catch { return null; }
    }

    /// <summary>設定受保護不被 LRU 優先淘汰的頁碼區間（含端點），例如雙頁或連續模式可視窗；傳負數代表清除區間。</summary>
    public void SetProtectedRange(int first, int last)
    {
        if (first < 0 || last < 0)
        {
            lock (_cacheLock)
            {
                _protectedFirst = -1;
                _protectedLast = -1;
            }
            return;
        }

        if (first > last) (first, last) = (last, first);
        lock (_cacheLock)
        {
            _protectedFirst = first;
            _protectedLast = last;
        }
    }

    /// <summary>取快取頁並取得參考計數租約（命中才回，永不阻塞）。回 null 表示尚未解到。</summary>
    public SkiaImageLease? AcquirePeek(int index)
    {
        lock (_cacheLock)
        {
            if (_shuttingDown) return null;
            if (_cache.TryGetValue(index, out var slot))
            {
                TouchLru(index);
                slot.Retain();
                return new SkiaImageLease(slot);
            }
            return null;
        }
    }

    /// <summary>取快取頁租約；未命中則同步 produce + 插入後保留租約回傳。</summary>
    public SkiaImageLease? AcquireOrProduce(int index)
    {
        lock (_cacheLock)
        {
            if (_shuttingDown) return null;
            if (_cache.TryGetValue(index, out var hit))
            {
                TouchLru(index);
                hit.Retain();
                return new SkiaImageLease(hit);
            }
        }

        var img = _produce(index);
        if (img == null) return null;

        var slot = Insert(index, img, retainForCaller: true);
        return slot == null ? null : new SkiaImageLease(slot);
    }

    /// <summary>取快取頁（命中才回，永不阻塞）。回 null 表示尚未解到。（保留相容既有呼叫端）</summary>
    public SKImage? Peek(int index)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(index, out var slot)) { TouchLru(index); return slot.Image; }
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

    /// <summary>取快取頁；未命中則同步 produce + 插入後回傳。（保留相容既有呼叫端）</summary>
    public SKImage? GetOrProduce(int index)
    {
        lock (_cacheLock)
            if (_cache.TryGetValue(index, out var hit)) { TouchLru(index); return hit.Image; }

        var img = _produce(index);
        if (img == null) return null;
        var slot = Insert(index, img, retainForCaller: false);
        return slot?.Image;
    }

    /// <summary>把指定頁排入前景優先解碼佇列並喚醒 worker（用於當前可見頁未命中快取時）。</summary>
    public void RequestPriority(int index)
    {
        if (_shuttingDown) return;
        Enqueue(index, priority: true);
        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// 將可見跨頁 <c>[start, start + size - 1]</c> 排入前景優先佇列，
    /// 其餘鄰頁（由近到遠 <c>1..radius</c>）排入一般背景預載佇列並喚醒 worker。
    /// </summary>
    public void RequestSpreadAndAround(int start, int size, int radius)
    {
        if (_shuttingDown) return;
        int effectiveSize = Math.Max(1, size);
        for (int i = 0; i < effectiveSize; i++)
        {
            Enqueue(start + i, priority: true);
        }
        for (int d = 1; d <= radius; d++)
        {
            Enqueue(start + effectiveSize - 1 + d, priority: false);
            Enqueue(start - d, priority: false);
        }
        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>把 index 鄰頁(由近到遠)排進解碼佇列並喚醒 worker。</summary>
    public void RequestAround(int index, int radius)
    {
        if (_shuttingDown) return;
        for (int d = 0; d <= radius; d++)
        {
            if (d == 0) Enqueue(index, priority: false);
            else { Enqueue(index + d, priority: false); Enqueue(index - d, priority: false); }
        }
        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }

    /// <summary>把 [from,to] 範圍各頁排進解碼佇列並喚醒 worker（連續模式的可見窗預載）。</summary>
    public void RequestRange(int from, int to)
    {
        if (_shuttingDown) return;
        if (from > to) (from, to) = (to, from);
        for (int i = from; i <= to; i++) Enqueue(i, priority: false);
        try { _signal.Set(); } catch (ObjectDisposedException) { }
    }

    private void Enqueue(int index, bool priority)
    {
        if (index < 0 || index >= _pageCount || _shuttingDown) return;
        lock (_cacheLock)
        {
            if (_shuttingDown || _cache.ContainsKey(index) || _inFlight.Contains(index))
                return;

            if (priority)
            {
                if (_queuedPriority.Add(index))
                    _priorityQueue.Enqueue(index);
            }
            else
            {
                if (!_queuedPriority.Contains(index) && _queuedNormal.Add(index))
                    _queue.Enqueue(index);
            }
        }
    }

    private void DecodeLoop()
    {
        while (!_shuttingDown)
        {
            int idx = -1;
            bool hasWork = false;
            int gen = 0;

            lock (_cacheLock)
            {
                if (_shuttingDown) break;

                while (_priorityQueue.TryDequeue(out int pIdx))
                {
                    bool removed = _queuedPriority.Remove(pIdx);
                    _queuedNormal.Remove(pIdx);
                    if (removed && !_cache.ContainsKey(pIdx) && !_inFlight.Contains(pIdx))
                    {
                        idx = pIdx;
                        hasWork = true;
                        break;
                    }
                }

                if (!hasWork)
                {
                    while (_queue.TryDequeue(out int nIdx))
                    {
                        if (_queuedNormal.Remove(nIdx) && !_cache.ContainsKey(nIdx) && !_inFlight.Contains(nIdx))
                        {
                            idx = nIdx;
                            hasWork = true;
                            break;
                        }
                    }
                }

                if (hasWork)
                {
                    _inFlight.Add(idx);
                    gen = Volatile.Read(ref _clearGeneration);
                }
            }

            if (!hasWork)
            {
                try { _signal.WaitOne(); }
                catch (ObjectDisposedException) { break; }
                continue;
            }

            SKImage? img;
            try
            {
                img = _produce(idx);
            }
            finally
            {
                lock (_cacheLock)
                {
                    _inFlight.Remove(idx);
                }
            }

            if (img == null) continue;
            if (_shuttingDown || gen != Volatile.Read(ref _clearGeneration))
            {
                img.Dispose();
                continue;
            }
            Insert(idx, img, retainForCaller: false);
            PageReady?.Invoke(idx);
        }
    }

    private CacheSlot? Insert(int index, SKImage img, bool retainForCaller)
    {
        CacheSlot? resultSlot = null;
        bool discardNewImg = false;
        var toRelease = new List<CacheSlot>();

        lock (_cacheLock)
        {
            if (_shuttingDown)
            {
                discardNewImg = true;
            }
            else if (_cache.TryGetValue(index, out var existing))
            {
                // 若 _cache 已有該 index，保留既有項目並將重複解出的新 img 釋放，避免取代並 Dispose 持有中的影像
                TouchLru(index);
                discardNewImg = true;
                resultSlot = existing;
                if (retainForCaller) resultSlot.Retain();
            }
            else
            {
                resultSlot = new CacheSlot(img);
                if (retainForCaller) resultSlot.Retain();
                _cache[index] = resultSlot;
                _lru.AddFirst(index);

                while (_cache.Count > _cacheCap)
                {
                    var node = _lru.Last;
                    while (node != null && (node.Value == index || IsProtectedLocked(node.Value)))
                        node = node.Previous;
                    if (node == null) break;

                    _lru.Remove(node);
                    if (_cache.Remove(node.Value, out var oldSlot))
                        toRelease.Add(oldSlot);
                }
            }
        }

        if (discardNewImg) img.Dispose();
        foreach (var s in toRelease) s.Release();
        return resultSlot;
    }

    private bool IsProtectedLocked(int index)
    {
        if (index == Current) return true;
        return _protectedFirst >= 0 && index >= _protectedFirst && index <= _protectedLast;
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
        List<CacheSlot> toRelease;
        lock (_cacheLock)
        {
            Interlocked.Increment(ref _clearGeneration);
            toRelease = new List<CacheSlot>(_cache.Values);
            _cache.Clear();
            _lru.Clear();
            _queuedPriority.Clear();
            _queuedNormal.Clear();
            _inFlight.Clear();
            while (_priorityQueue.TryDequeue(out _)) { }
            while (_queue.TryDequeue(out _)) { }
        }
        foreach (var s in toRelease) s.Release();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shuttingDown = true;
        _signal.Set();
        try { _worker.Join(500); } catch { }
        Clear();
        _signal.Dispose();
    }
}

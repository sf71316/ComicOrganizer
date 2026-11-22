using System.Diagnostics;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 封面點陣圖被擠出記憶體快取後，原生像素記憶體要等 GC 終結才會釋放；但點陣圖在 .NET 堆積上只佔幾十 bytes，
/// GC 感受不到壓力，原生記憶體會累積數百 MB（本機拖捲軸後約 500 MB）。
/// 這裡累計「放掉的像素量」，超過門檻就觸發一次背景 GC（不阻塞呼叫端），讓終結程序早點把原生記憶體還回去。
/// <para>
/// 刻意不直接 Dispose：點陣圖可能仍顯示在卡片或詳細資訊抽屜上，Dispose 後繪製會丟例外。
/// </para>
/// </summary>
public sealed class BitmapReleaseGcTrigger
{
    /// <summary>預設門檻：放掉的像素累計 256 MB 觸發一次。</summary>
    public const long DefaultThresholdBytes = 256L * 1024 * 1024;

    /// <summary>預設兩次觸發的最短間隔，避免連續大量捲動時接連觸發。</summary>
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromSeconds(2);

    private readonly long _thresholdBytes;
    private readonly long _minIntervalTicks;
    private readonly Action _collect;
    private readonly Func<long> _nowTimestamp;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly object _lock = new();
    private long _pendingBytes;
    private long _lastCollectTimestamp;
    private bool _hasCollected;
    private bool _retryScheduled;
    private int _triggerCount;

    /// <param name="collect">實際觸發 GC 的動作（測試用替換）；預設為背景第 2 代 GC。</param>
    /// <param name="nowTimestamp">目前時間（<see cref="Stopwatch"/> 時戳，測試用替換）。</param>
    /// <param name="delay">等待用（測試用替換）；預設 <see cref="Task.Delay(TimeSpan)"/>。</param>
    public BitmapReleaseGcTrigger(
        long thresholdBytes = DefaultThresholdBytes,
        TimeSpan? minInterval = null,
        Action? collect = null,
        Func<long>? nowTimestamp = null,
        Func<TimeSpan, Task>? delay = null)
    {
        if (thresholdBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdBytes), "門檻必須大於 0。");
        }

        _thresholdBytes = thresholdBytes;
        _minIntervalTicks = (long)((minInterval ?? DefaultMinInterval).TotalSeconds * Stopwatch.Frequency);
        _collect = collect ?? CollectInBackground;
        _nowTimestamp = nowTimestamp ?? Stopwatch.GetTimestamp;
        _delay = delay ?? (wait => Task.Delay(wait));
    }

    /// <summary>已觸發的次數。</summary>
    public int TriggerCount => Volatile.Read(ref _triggerCount);

    /// <summary>目前累計、尚未觸發的位元組數。</summary>
    public long PendingBytes
    {
        get
        {
            lock (_lock)
            {
                return _pendingBytes;
            }
        }
    }

    /// <summary>回報放掉的像素量（任何執行緒皆可呼叫）。達門檻且距上次觸發夠久時觸發背景 GC。</summary>
    public void ReportReleased(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        lock (_lock)
        {
            _pendingBytes += bytes;
        }

        TryCollect();
    }

    private void TryCollect()
    {
        bool shouldCollect = false;
        TimeSpan retryAfter = TimeSpan.Zero;
        lock (_lock)
        {
            if (_pendingBytes < _thresholdBytes)
            {
                return;
            }

            long now = _nowTimestamp();
            long elapsed = now - _lastCollectTimestamp;
            if (!_hasCollected || elapsed >= _minIntervalTicks)
            {
                _pendingBytes = 0;
                _lastCollectTimestamp = now;
                _hasCollected = true;
                shouldCollect = true;
            }
            else if (!_retryScheduled)
            {
                // 間隔未到：排一次稍後重試，避免捲動剛好在這時停下、累計的量一直等不到下一次回報
                _retryScheduled = true;
                retryAfter = TimeSpan.FromSeconds((_minIntervalTicks - elapsed) / (double)Stopwatch.Frequency);
            }
        }

        if (shouldCollect)
        {
            Interlocked.Increment(ref _triggerCount);
            _collect();
        }
        else if (retryAfter > TimeSpan.Zero)
        {
            _ = RetryLaterAsync(retryAfter);
        }
    }

    private async Task RetryLaterAsync(TimeSpan wait)
    {
        try
        {
            await _delay(wait).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _retryScheduled = false;
            }

            TryCollect();
        }
    }

    private static void CollectInBackground()
    {
        // blocking: false 在預設的並行 GC 下會進行背景 GC，不讓呼叫端等整個回收完成；不壓縮堆積
        GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
    }
}

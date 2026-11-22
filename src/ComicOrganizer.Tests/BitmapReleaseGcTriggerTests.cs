using System.Diagnostics;
using ComicOrganizer.Avalonia.Services;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 放掉封面點陣圖時「累積到門檻觸發背景 GC」（<see cref="BitmapReleaseGcTrigger"/>）。
/// 以假的 GC 動作與時鐘測試，不真的觸發 GC。
/// </summary>
public class BitmapReleaseGcTriggerTests
{
    private long _now = 1_000_000;
    private int _collects;

    private BitmapReleaseGcTrigger Create(long threshold, TimeSpan interval, Func<TimeSpan, Task>? delay = null)
        => new(threshold, interval, () => _collects++, () => _now, delay ?? (_ => new TaskCompletionSource().Task));

    private void Advance(TimeSpan span) => _now += (long)(span.TotalSeconds * Stopwatch.Frequency);

    [Fact]
    public void BelowThreshold_DoesNotCollect()
    {
        var trigger = Create(1000, TimeSpan.FromSeconds(2));
        trigger.ReportReleased(400);
        trigger.ReportReleased(599);

        Assert.Equal(0, _collects);
        Assert.Equal(999, trigger.PendingBytes);
    }

    [Fact]
    public void ReachingThreshold_CollectsOnceAndResets()
    {
        var trigger = Create(1000, TimeSpan.FromSeconds(2));
        trigger.ReportReleased(600);
        trigger.ReportReleased(400);

        Assert.Equal(1, _collects);
        Assert.Equal(1, trigger.TriggerCount);
        Assert.Equal(0, trigger.PendingBytes);
    }

    [Fact]
    public void WithinMinInterval_DoesNotCollectAgain_UntilIntervalPasses()
    {
        var trigger = Create(1000, TimeSpan.FromSeconds(2));
        trigger.ReportReleased(1000);
        Assert.Equal(1, _collects);

        Advance(TimeSpan.FromSeconds(1));
        trigger.ReportReleased(5000);
        Assert.Equal(1, _collects);
        Assert.Equal(5000, trigger.PendingBytes);

        Advance(TimeSpan.FromSeconds(1.5));
        trigger.ReportReleased(1);
        Assert.Equal(2, _collects);
        Assert.Equal(0, trigger.PendingBytes);
    }

    [Fact]
    public async Task SuppressedByInterval_RetriesAfterDelay_WithoutFurtherReports()
    {
        var delayGate = new TaskCompletionSource();
        TimeSpan requested = TimeSpan.Zero;
        var trigger = Create(1000, TimeSpan.FromSeconds(2), wait =>
        {
            requested = wait;
            return delayGate.Task;
        });

        trigger.ReportReleased(1000);
        Advance(TimeSpan.FromSeconds(0.5));
        trigger.ReportReleased(2000);
        Assert.Equal(1, _collects);
        Assert.InRange(requested.TotalSeconds, 1.49, 1.51);

        // 捲動停了、不再回報：等待結束後自己補觸發一次
        Advance(TimeSpan.FromSeconds(1.5));
        delayGate.SetResult();
        await Task.Yield();
        for (int i = 0; i < 50 && _collects < 2; i++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(2, _collects);
        Assert.Equal(0, trigger.PendingBytes);
    }

    [Fact]
    public void NonPositiveReport_IsIgnored()
    {
        var trigger = Create(1000, TimeSpan.FromSeconds(2));
        trigger.ReportReleased(0);
        trigger.ReportReleased(-5);

        Assert.Equal(0, trigger.PendingBytes);
        Assert.Equal(0, _collects);
    }
}

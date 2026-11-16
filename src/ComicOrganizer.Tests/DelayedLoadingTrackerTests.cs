using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 B（DelayedLoadingTracker 狀態機與 NormalizePathKey）單元測試。
/// </summary>
public class DelayedLoadingTrackerTests
{
    [Fact]
    public void FastCompletion_BeforeDelay_NeverShowsIndicator()
    {
        var tracker = new DelayedLoadingTracker();
        Assert.False(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);
        Assert.Equal(0, tracker.Generation);

        var token = tracker.Begin();
        Assert.Equal(1, token);
        Assert.Equal(1, tracker.Generation);
        Assert.True(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);

        Assert.True(tracker.Complete(token));
        Assert.False(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);

        // 重複 Complete 或過遲到來的 MarkDelayElapsed 不應生效
        Assert.False(tracker.Complete(token));
        Assert.False(tracker.MarkDelayElapsed(token));
        Assert.False(tracker.IsIndicatorVisible);
    }

    [Fact]
    public void SlowCompletion_AfterDelay_ShowsAndHidesIndicator()
    {
        var tracker = new DelayedLoadingTracker();
        var token = tracker.Begin();

        Assert.True(tracker.MarkDelayElapsed(token));
        Assert.True(tracker.IsBusy);
        Assert.True(tracker.IsIndicatorVisible);

        // 同一 token 再次 MarkDelayElapsed 應回 false（已顯示）
        Assert.False(tracker.MarkDelayElapsed(token));

        Assert.True(tracker.Complete(token));
        Assert.False(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);
    }

    [Fact]
    public void StaleToken_FromEarlierBegin_CannotTriggerOrClearCurrentLoad()
    {
        var tracker = new DelayedLoadingTracker();
        var token1 = tracker.Begin();
        var token2 = tracker.Begin();

        Assert.Equal(2, token2);
        Assert.True(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);

        // 舊世代 timer 到期或舊作業完成，不可影響目前世代
        Assert.False(tracker.MarkDelayElapsed(token1));
        Assert.False(tracker.IsIndicatorVisible);
        Assert.False(tracker.Complete(token1));
        Assert.True(tracker.IsBusy);

        // 只有 token2 可以推進與完成
        Assert.True(tracker.MarkDelayElapsed(token2));
        Assert.True(tracker.IsIndicatorVisible);
        Assert.False(tracker.Complete(token1));
        Assert.True(tracker.IsIndicatorVisible);

        Assert.True(tracker.Complete(token2));
        Assert.False(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);
    }

    [Fact]
    public void Reset_ClearsBusyAndVisibility_AndReportsStateChange()
    {
        var tracker = new DelayedLoadingTracker();
        Assert.False(tracker.Reset());

        var token = tracker.Begin();
        tracker.MarkDelayElapsed(token);
        Assert.True(tracker.IsBusy);
        Assert.True(tracker.IsIndicatorVisible);

        Assert.True(tracker.Reset());
        Assert.False(tracker.IsBusy);
        Assert.False(tracker.IsIndicatorVisible);
        Assert.False(tracker.Reset());
        // Reset 後舊 token 的 MarkDelayElapsed / Complete 也不得生效
        Assert.False(tracker.MarkDelayElapsed(token));
        Assert.False(tracker.Complete(token));
    }

    [Fact]
    public void NormalizePathKey_NormalizesTrailingSlashesRelativePathsAndNullWhitespace()
    {
        Assert.Equal(string.Empty, DelayedLoadingTracker.NormalizePathKey(null));
        Assert.Equal(string.Empty, DelayedLoadingTracker.NormalizePathKey(""));
        Assert.Equal(string.Empty, DelayedLoadingTracker.NormalizePathKey("   "));

        // 根目錄保留結尾反斜線
        Assert.Equal(@"C:\", DelayedLoadingTracker.NormalizePathKey(@"C:\"));

        // 一般路徑移除結尾斜線並轉換正反斜線
        var k1 = DelayedLoadingTracker.NormalizePathKey(@"C:\Comics\AuthorA\Work1\");
        var k2 = DelayedLoadingTracker.NormalizePathKey(@"c:/Comics/AuthorA/Work1");
        Assert.Equal(@"C:\Comics\AuthorA\Work1", k1);
        Assert.Equal(k1, k2, ignoreCase: true);

        // 相對路徑轉完整路徑且移除結尾斜線
        var rel = DelayedLoadingTracker.NormalizePathKey(@".\SubDir\..\Work2\");
        var expected = Path.Combine(Environment.CurrentDirectory, "Work2");
        Assert.Equal(expected, rel);
    }
}

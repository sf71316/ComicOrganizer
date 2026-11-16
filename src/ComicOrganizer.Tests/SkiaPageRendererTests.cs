using System.Collections.Concurrent;
using ComicOrganizer.App.Core;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 D（SkiaPageRenderer 參考計數租約、SetProtectedRange 與高併發安全釋放）單元測試。
/// </summary>
public class SkiaPageRendererTests
{
    private static SKImage CreateTestImage(int width = 16, int height = 16)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.SteelBlue);
        return surface.Snapshot();
    }

    [Fact]
    public async Task ConcurrentBackgroundEvictionAndDuplicateInsert_WithLeases_NeverUsesDisposedImageAndReleasesAll()
    {
        const int pageCount = 40;
        var createdImages = new ConcurrentBag<SKImage>();

        using var renderer = new SkiaPageRenderer(
            pageCount,
            idx =>
            {
                var img = CreateTestImage(20 + (idx % 5), 30 + (idx % 7));
                createdImages.Add(img);
                // 刻意引入微小延遲製造 UI 讀端與背景寫端同頁競爭
                Thread.SpinWait(200);
                return img;
            },
            cacheCap: 3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var exceptions = new ConcurrentQueue<Exception>();

        // 多個讀取執行緒：模擬快速滾輪切頁、同時 AcquirePeek / AcquireOrProduce 並持有一段時間繪製
        var readerTasks = Enumerable.Range(0, 4).Select(workerId => Task.Run(() =>
        {
            var rng = new Random(100 + workerId);
            try
            {
                for (int step = 0; step < 80 && !cts.IsCancellationRequested; step++)
                {
                    int idx = rng.Next(0, pageCount);
                    renderer.Current = idx;
                    renderer.SetProtectedRange(Math.Max(0, idx - 1), Math.Min(pageCount - 1, idx + 1));
                    renderer.RequestAround(idx, 3);

                    using var lease = (step % 2 == 0)
                        ? renderer.AcquireOrProduce(idx)
                        : (renderer.AcquirePeek(idx) ?? renderer.AcquireOrProduce(idx));

                    if (lease != null)
                    {
                        // 持鎖繪製期間模擬讀取 Width/Height/Handle，期間其他執行緒會持續觸發 LRU 淘汰與 Clear
                        Assert.NotEqual(IntPtr.Zero, lease.Image.Handle);
                        Assert.True(lease.Image.Width >= 20);
                        Assert.True(lease.Image.Height >= 30);
                        Thread.Sleep(1);
                        Assert.NotEqual(IntPtr.Zero, lease.Image.Handle);
                    }

                    if (step % 25 == 24 && workerId == 0)
                    {
                        renderer.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        })).ToArray();

        await Task.WhenAll(readerTasks);
        Assert.Empty(exceptions);

        renderer.Dispose();

        // 所有曾建立出來的 SKImage 在所有租約與 renderer 釋放後，都必須已被確實 Dispose
        Assert.NotEmpty(createdImages);
        Assert.All(createdImages, img => Assert.Equal(IntPtr.Zero, img.Handle));
    }

    [Fact]
    public void SetProtectedRange_ProtectsMultiplePagesFromLruEviction()
    {
        var created = new List<SKImage>();
        using var renderer = new SkiaPageRenderer(
            pageCount: 12,
            idx =>
            {
                var img = CreateTestImage();
                created.Add(img);
                return img;
            },
            cacheCap: 4);

        renderer.Current = 2;
        renderer.SetProtectedRange(2, 4); // 保護第 2、3、4 頁（如雙頁或連續可視區）

        // 先載入受保護的 2, 3, 4
        using (renderer.AcquireOrProduce(2)) { }
        using (renderer.AcquireOrProduce(3)) { }
        using (renderer.AcquireOrProduce(4)) { }

        // 再載入多張非保護頁觸發 LRU 淘汰
        foreach (var idx in new[] { 0, 1, 5, 6, 7, 8 })
        {
            using (renderer.AcquireOrProduce(idx)) { }
        }

        Assert.True(renderer.Contains(2));
        Assert.True(renderer.Contains(3));
        Assert.True(renderer.Contains(4));
        Assert.Equal(4, renderer.Count);

        // 切換保護區間到 8..10 後再載入新頁，舊的 2, 3, 4 應可正常被淘汰
        renderer.Current = 9;
        renderer.SetProtectedRange(8, 10);
        foreach (var idx in new[] { 9, 10, 11 })
        {
            using (renderer.AcquireOrProduce(idx)) { }
        }

        Assert.True(renderer.Contains(9));
        Assert.True(renderer.Contains(10));
        Assert.True(renderer.Contains(11));
        Assert.False(renderer.Contains(2));
        Assert.Equal(4, renderer.Count);

        renderer.Dispose();
        Assert.All(created, img => Assert.Equal(IntPtr.Zero, img.Handle));
    }

    [Fact]
    public async Task DuplicateConcurrentProduce_KeepsFirstInstance_And_ClearDefersDisposeUntilAllLeasesReleased()
    {
        var created = new ConcurrentBag<SKImage>();
        using var bothEnteredProduce = new CountdownEvent(2);
        using var allowProduceFinish = new ManualResetEventSlim(false);

        using var renderer = new SkiaPageRenderer(
            pageCount: 5,
            idx =>
            {
                if (idx == 0)
                {
                    bothEnteredProduce.Signal();
                    allowProduceFinish.Wait(TimeSpan.FromSeconds(5));
                }
                var img = CreateTestImage();
                created.Add(img);
                return img;
            },
            cacheCap: 4);

        var t1 = Task.Run(() => renderer.AcquireOrProduce(0));
        var t2 = Task.Run(() => renderer.AcquireOrProduce(0));

        Assert.True(bothEnteredProduce.Wait(TimeSpan.FromSeconds(5)));
        allowProduceFinish.Set();

        var lease1 = await t1;
        var lease2 = await t2;
        Assert.NotNull(lease1);
        Assert.NotNull(lease2);

        // 兩者必須共用同一份存活的快取 SKImage，而另一份重複產生的新圖應已被即時 Dispose
        Assert.Same(lease1!.Image, lease2!.Image);
        Assert.NotEqual(IntPtr.Zero, lease1.Image.Handle);
        Assert.Equal(2, created.Count);
        Assert.Single(created, img => img.Handle != IntPtr.Zero);
        Assert.Single(created, img => img.Handle == IntPtr.Zero);

        // Clear() 期間仍有兩份租約持有中，底層 SKImage 不得提前 Dispose
        renderer.Clear();
        Assert.NotEqual(IntPtr.Zero, lease1.Image.Handle);

        // 釋放第一份租約（冪等重複呼叫不影響第二份租約）
        lease1.Dispose();
        lease1.Dispose();
        Assert.NotEqual(IntPtr.Zero, lease2.Image.Handle);

        // 最後一份租約釋放後才真正 Dispose
        lease2.Dispose();
        Assert.All(created, img => Assert.Equal(IntPtr.Zero, img.Handle));
    }
}

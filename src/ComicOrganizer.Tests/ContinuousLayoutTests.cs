using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// ContinuousLayout 的單元測試（#49 Phase 1.2c-A）。
/// 純計算類別 — 測 layout / lazy fill / smart paging 邊界查詢 / zoom / resize。
/// </summary>
public class ContinuousLayoutTests
{
    [Fact]
    public void EmptyLayout_HasZeroTotalHeight()
    {
        var layout = new ContinuousLayout(0, 800);
        Assert.Equal(0, layout.PageCount);
        Assert.Equal(0, layout.TotalHeight);
        Assert.Equal(-1, layout.FindPageAtY(0));
    }

    [Fact]
    public void UnmeasuredPages_UseEstimateAspect()
    {
        var layout = new ContinuousLayout(3, 800);
        // 估計值：800 × 1.4 = 1120 / 頁
        Assert.Equal(1120, layout.GetPageHeight(0));
        Assert.Equal(0, layout.GetPageY(0));
        Assert.Equal(1120 + ContinuousLayout.PageGap, layout.GetPageY(1));
        // TotalHeight = 3 × 1120 + 2 × gap
        Assert.Equal(1120 * 3 + ContinuousLayout.PageGap * 2, layout.TotalHeight);
    }

    [Fact]
    public void SetMeasured_ScalesByContentWidthAndRecalcs()
    {
        var layout = new ContinuousLayout(3, 800);
        // 自然 1000×1500 → scale = 800/1000 = 0.8 → 顯示高 1200
        layout.SetMeasured(0, 1000, 1500);
        Assert.Equal(1200, layout.GetPageHeight(0));
        Assert.True(layout.IsMeasured(0));
        Assert.False(layout.IsMeasured(1));

        // 第二頁自然 800×400（橫圖）→ scale=1 → 顯示高 400
        layout.SetMeasured(1, 800, 400);
        Assert.Equal(400, layout.GetPageHeight(1));

        // 第 2 頁未量測仍走估計
        Assert.Equal(1120, layout.GetPageHeight(2));

        // yOffset 應反映實際高
        Assert.Equal(0, layout.GetPageY(0));
        Assert.Equal(1200 + ContinuousLayout.PageGap, layout.GetPageY(1));
        Assert.Equal(1200 + 400 + ContinuousLayout.PageGap * 2, layout.GetPageY(2));
    }

    [Fact]
    public void FindPageAtY_BinarySearchHitsRightPage()
    {
        var layout = new ContinuousLayout(5, 800);
        // 全部用估計 1120 + gap=8 → 每頁佔 1128
        layout.SetMeasured(0, 800, 1000);   // 第 0 頁高 1000
        // page 0: y 0..1000
        // page 1: y 1008..2128 (估計 1120)
        // page 2: y 2136..3256
        Assert.Equal(0, layout.FindPageAtY(500));
        Assert.Equal(1, layout.FindPageAtY(1500));
        Assert.Equal(2, layout.FindPageAtY(3000));
        Assert.Equal(0, layout.FindPageAtY(0));     // 起點
        Assert.Equal(0, layout.FindPageAtY(-100));  // 負值 clamp
        Assert.Equal(4, layout.FindPageAtY(99999)); // 超過底端 clamp
    }

    [Fact]
    public void GetVisibleRange_CoversAllPagesInViewport()
    {
        var layout = new ContinuousLayout(5, 800);
        // 全部估計 1120 + gap 8 → 每頁區間 1128
        // viewport [0, 2300) 應含 page 0, 1, 2 (page 2 起點 2256)
        var (first, last) = layout.GetVisibleRange(0, 2300);
        Assert.Equal(0, first);
        Assert.Equal(2, last);
    }

    [Fact]
    public void SetViewportWidth_AffectsMeasuredAndEstimatedHeights()
    {
        var layout = new ContinuousLayout(3, 800);
        layout.SetMeasured(0, 1000, 1500);   // h = 1200
        Assert.Equal(1200, layout.GetPageHeight(0));

        // 改 viewport 至 400 → contentW 400 → measured: 400/1000 = 0.4 → h = 600
        layout.SetViewportWidth(400);
        Assert.Equal(600, layout.GetPageHeight(0));
        // 估計頁也要跟著縮：400 × 1.4 = 560
        Assert.Equal(560, layout.GetPageHeight(1));
    }

    [Fact]
    public void SetZoom_ScalesContentWidthAndAllHeights()
    {
        var layout = new ContinuousLayout(2, 800);
        layout.SetMeasured(0, 1000, 1500);   // h = 1200 at zoom 1.0

        layout.SetZoom(0.5f);
        // ContentWidth = 800 × 0.5 = 400 → h = 1500 × 400/1000 = 600
        Assert.Equal(400, layout.ContentWidth);
        Assert.Equal(600, layout.GetPageHeight(0));

        layout.SetZoom(2.0f);
        Assert.Equal(1600, layout.ContentWidth);
        Assert.Equal(2400, layout.GetPageHeight(0));

        // 範圍 clamp
        layout.SetZoom(10f);
        Assert.Equal(2.0f, layout.Zoom);
        layout.SetZoom(0.01f);
        Assert.Equal(0.5f, layout.Zoom);
    }

    [Fact]
    public void NextPageBoundary_FindsNextStrictlyGreater()
    {
        var layout = new ContinuousLayout(3, 800);
        // page 0 估計 1120, page 1 起點 1128
        Assert.Equal(1128, layout.NextPageBoundary(0));
        Assert.Equal(1128, layout.NextPageBoundary(500));
        Assert.Equal(1128, layout.NextPageBoundary(1127));   // 嚴格大於
        Assert.Equal(2256, layout.NextPageBoundary(1128));   // 在 page 1 起點上 → 找 page 2 起點
        // 沒下一頁時回 TotalHeight
        Assert.Equal(layout.TotalHeight, layout.NextPageBoundary(layout.TotalHeight));
    }

    [Fact]
    public void PrevPageBoundary_FindsPrevStrictlyLess()
    {
        var layout = new ContinuousLayout(3, 800);
        // page 0 起點 0, page 1 起點 1128, page 2 起點 2256
        Assert.Equal(0, layout.PrevPageBoundary(0));        // 沒上一頁
        Assert.Equal(0, layout.PrevPageBoundary(500));      // 在 page 0 中 → 上一頁起點 0
        Assert.Equal(0, layout.PrevPageBoundary(1128));     // 在 page 1 起點 → 上一頁起點 0
        Assert.Equal(1128, layout.PrevPageBoundary(1500));  // 在 page 1 中 → 上一頁起點 1128
        Assert.Equal(2256, layout.PrevPageBoundary(3000));  // 在 page 2 中 → 上一頁起點 2256
    }

    [Fact]
    public void LongVerticalPage_DoesNotBreakBinarySearch()
    {
        // 韓漫長條：一頁 = 6000px 自然高
        var layout = new ContinuousLayout(2, 800);
        layout.SetMeasured(0, 800, 6000);  // 不縮放，h = 6000
        layout.SetMeasured(1, 800, 1000);  // h = 1000
        // page 0: y 0..6000；page 1: y 6008..7008
        Assert.Equal(0, layout.FindPageAtY(3000));
        Assert.Equal(0, layout.FindPageAtY(5999));
        Assert.Equal(1, layout.FindPageAtY(6500));
        Assert.Equal(6008, layout.NextPageBoundary(0));
    }
}

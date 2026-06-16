using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

// FontSizeOptimizer.RemoveTinyColumns 回歸：濾掉 OCR 噪訊小欄(雜點/furigana)，
// 避免它讓相鄰欄間距變極小、把直排字級壓到極小（007「青鸞宮」框 7x13 雜點 → 11pt 的 bug）。
public class FontLayoutTests
{
    private static RectangleF C(float x, float w, float h) => new RectangleF(x, 100, w, h);

    [Fact]
    public void RemoveTinyColumns_DropsNoiseSpeck()
    {
        // 007 真實案例：5 個正常欄(w38~44,h128~239) + 1 個 7x13 雜點
        var cols = new List<RectangleF>
        {
            C(234, 7, 13),   // 雜點 → 應濾除
            C(201, 44, 215),
            C(171, 44, 215),
            C(139, 38, 239),
            C(108, 38, 128),
            C(74, 44, 215),
        };
        var kept = FontSizeOptimizer.RemoveTinyColumns(cols);
        Assert.Equal(5, kept.Count);
        Assert.DoesNotContain(kept, c => c.Width == 7); // 雜點不在
    }

    [Fact]
    public void RemoveTinyColumns_KeepsNormalSet()
    {
        var cols = new List<RectangleF> { C(200, 44, 200), C(160, 44, 210), C(120, 40, 190) };
        var kept = FontSizeOptimizer.RemoveTinyColumns(cols);
        Assert.Equal(3, kept.Count);
    }

    [Fact]
    public void RemoveTinyColumns_FewerThanThree_Unchanged()
    {
        var cols = new List<RectangleF> { C(200, 44, 200), C(100, 8, 20) };
        var kept = FontSizeOptimizer.RemoveTinyColumns(cols);
        Assert.Equal(2, kept.Count); // <3 欄不動，避免誤刪
    }
}

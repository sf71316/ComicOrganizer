using System.Collections.Generic;
using ComicOrganizer.App.Core;
using ComicOrganizer.Plugin.PaddleOcr;
using Xunit;

namespace ComicOrganizer.Tests;

// PaddleOcrService.ClusterChildrenByGap 拆分多氣泡誤併的回歸測試。
// 真實資料：單氣泡直書欄框略重疊→相鄰欄淨間距為「負」；兩氣泡併在一起→出現明顯「正」間距。
public class OcrMergeSplitTests
{
    private static OcrBlock Col(float x, float w, string t = "字") =>
        new OcrBlock { X = x, Y = 100, Width = w, Height = 120, Text = t };

    [Fact]
    public void SingleBubble_OverlappingColumns_NotSplit()
    {
        // 三欄，淨間距 -13、-14（欄框略重疊）→ 不拆
        var cols = new List<OcrBlock> { Col(100, 50), Col(137, 50), Col(173, 50) };
        var clusters = PaddleOcrService.ClusterChildrenByGap(cols, isVerticalFlow: true, gapRatio: 0.3f);
        Assert.Single(clusters);
    }

    [Fact]
    public void TwoBubbles_PositiveGap_Split()
    {
        // 005.jpg 真實案例量級：右簇 + 大間距 31 + 噪訊欄；avgW≈47，31>0.3*47=14 → 拆
        var cols = new List<OcrBlock>
        {
            Col(613, 51, "おいでですか"), Col(651, 57, "何をなさって"), // right=664,708 (重疊)
            Col(739, 32, "な")                                          // 651+57=708 → 739 縫隙 31
        };
        var clusters = PaddleOcrService.ClusterChildrenByGap(cols, isVerticalFlow: true, gapRatio: 0.3f);
        Assert.Equal(2, clusters.Count);
        // 由左至右分群：第一群 2 欄、第二群 1 欄（噪訊）
        Assert.Equal(2, clusters[0].Count);
        Assert.Single(clusters[1]);
    }

    [Fact]
    public void ThreeClusters_MultipleGaps_SplitThree()
    {
        var cols = new List<OcrBlock>
        {
            Col(100, 50), Col(137, 50),   // 群1（-13 重疊）
            Col(260, 50),                 // 群2（187→260 縫隙 73）
            Col(400, 50)                  // 群3（310→400 縫隙 90）
        };
        var clusters = PaddleOcrService.ClusterChildrenByGap(cols, isVerticalFlow: true, gapRatio: 0.3f);
        Assert.Equal(3, clusters.Count);
    }

    [Fact]
    public void FewerThanTwo_ReturnsSingleOrEmpty()
    {
        Assert.Empty(PaddleOcrService.ClusterChildrenByGap(new List<OcrBlock>(), true, 0.3f));
        Assert.Single(PaddleOcrService.ClusterChildrenByGap(new List<OcrBlock> { Col(0, 50) }, true, 0.3f));
    }
}

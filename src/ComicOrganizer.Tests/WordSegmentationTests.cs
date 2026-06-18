using ComicOrganizer.App.Core;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>繁體常用詞表載入（ChineseWordDict）—— 內嵌 gz 解壓、最大匹配查詢。</summary>
public class WordSegmentationTests
{
    private readonly ITestOutputHelper _o;
    public WordSegmentationTests(ITestOutputHelper o) { _o = o; }

    [Fact]
    public void Dict_Loads_FromEmbeddedGz()
    {
        var dict = ChineseWordDict.Shared;
        _o.WriteLine($"詞數={dict.Count} 最長={dict.MaxWordLen}");
        Assert.True(dict.Count > 50000, $"詞表應有數萬詞，實際 {dict.Count}");
        Assert.Equal(8, dict.MaxWordLen);
    }

    [Theory]
    [InlineData("眼睛")]
    [InlineData("監視")]
    [InlineData("許多")]
    [InlineData("太子")]
    [InlineData("皇上")]
    public void Dict_Contains_CommonWords(string w)
        => Assert.True(ChineseWordDict.Shared.Contains(w), $"應收錄常用詞「{w}」");

    [Fact]
    public void Dict_Excludes_RareProperNoun()
        => Assert.False(ChineseWordDict.Shared.Contains("青鸞宮"), "冷僻專名不應在 詞頻>=10 詞表");

    [Fact]
    public void LongestWordLength_MatchesWord_AndSkipsSingleChar()
    {
        var dict = ChineseWordDict.Shared;
        Assert.Equal(2, dict.LongestWordLength("眼睛", 0));
        Assert.Equal(2, dict.LongestWordLength("太子", 0));
        Assert.Equal(0, dict.LongestWordLength("妃", 0));   // 單字一律可斷
    }

    [Fact]
    public void ForbiddenBreaks_MarksWordInteriors()
    {
        // 眼睛 監視 太子 → 三個 2 字詞；內部 index 1/3/5 禁斷，詞邊界 0/2/4/6 可斷
        var f = WordBreakAdvisor.BuildForbiddenBreaks("眼睛監視太子");
        Assert.Equal(7, f.Length);
        Assert.True(f[1] && f[3] && f[5]);
        Assert.False(f[0] || f[2] || f[4] || f[6]);
    }

    [Fact]
    public void ForbiddenBreaks_AllSingleChars_NeverForbidden()
    {
        // 非詞單字串接 → 哪裡都能斷
        var f = WordBreakAdvisor.BuildForbiddenBreaks("妃妃妃");
        Assert.All(f, b => Assert.False(b));
    }

    [Fact]
    public void NearestAllowedBreak_SnapsToWordBoundary()
    {
        var f = WordBreakAdvisor.BuildForbiddenBreaks("眼睛監視太子");  // forbidden 1,3,5
        // target=3(切斷監視) → 半徑2內最近可斷=2
        Assert.Equal(2, WordBreakAdvisor.NearestAllowedBreak(f, 3, 2, 0, 6));
        // target=2 本身可斷 → 不動
        Assert.Equal(2, WordBreakAdvisor.NearestAllowedBreak(f, 2, 2, 0, 6));
        // 受 cap 範圍夾住：target=5 但只准 [4,4]，半徑1 → 4 可斷
        Assert.Equal(4, WordBreakAdvisor.NearestAllowedBreak(f, 5, 1, 4, 4));
        // 範圍內無可斷點 → -1（target=5 forbidden，僅准 [5,5]）
        Assert.Equal(-1, WordBreakAdvisor.NearestAllowedBreak(f, 5, 2, 5, 5));
    }

    [Fact]
    public void SnapColumnBreaks_MovesBreaksOffWordInteriors()
    {
        // 「眼睛監視太子妃」FMM=眼睛|監視|太子|妃。初始欄界 3、5 各切斷「監視」「太子」。
        // 吸附後應移到詞邊界 2、4 → 欄字數 [2,2,3]（眼睛|監視|太子妃），總字數不變。
        var counts = new[] { 3, 2, 2 };
        var caps = new[] { 3, 3, 3 };
        FontSizeOptimizer.SnapColumnBreaksToWords("眼睛監視太子妃", counts, caps);
        Assert.Equal(new[] { 2, 2, 3 }, counts);
        Assert.Equal(7, counts[0] + counts[1] + counts[2]);
    }

    [Fact]
    public void SnapColumnBreaks_NoLegalBoundary_KeepsOriginal()
    {
        // cap 全等於初始字數 → 無左右移動空間 → 維持原切點（不為避免切詞而扭曲版面）
        var counts = new[] { 3, 2, 2 };
        var caps = new[] { 3, 2, 2 };
        FontSizeOptimizer.SnapColumnBreaksToWords("眼睛監視太子妃", counts, caps);
        Assert.Equal(new[] { 3, 2, 2 }, counts);
    }
}

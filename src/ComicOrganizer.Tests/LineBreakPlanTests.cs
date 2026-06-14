using System.Linq;
using Xunit;
using Xunit.Abstractions;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Tests;

/// <summary>
/// 直排譯文分欄規劃（FontSizeOptimizer.PlanColumnCounts）回歸：欄位均分 + 基本禁則。
/// </summary>
public class LineBreakPlanTests
{
    private readonly ITestOutputHelper _output;
    public LineBreakPlanTests(ITestOutputHelper output) => _output = output;

    private static string Split(string text, int[] counts)
    {
        var parts = new System.Collections.Generic.List<string>();
        int idx = 0;
        foreach (var c in counts) { parts.Add(text.Substring(idx, c)); idx += c; }
        return string.Join(" | ", parts);
    }

    [Fact]
    public void Balanced_NoOrphanColumn()
    {
        // 10 字、每欄容量 4 → 需 3 欄；應均分為 4/3/3，而非貪婪填滿的 4/4/2
        var counts = FontSizeOptimizer.PlanColumnCounts("0123456789", new[] { 4, 4, 4 });
        _output.WriteLine(Split("0123456789", counts));
        Assert.Equal(new[] { 4, 3, 3 }, counts);
        Assert.Equal(10, counts.Sum());
    }

    [Fact]
    public void UsesFewestColumnsThatFit()
    {
        // 6 字、容量充足 → 用最少欄數（2 欄，3/3），不會散到 3 欄
        var counts = FontSizeOptimizer.PlanColumnCounts("012345", new[] { 5, 5, 5 });
        _output.WriteLine(Split("012345", counts));
        Assert.Equal(new[] { 3, 3 }, counts);
    }

    [Fact]
    public void Kinsoku_LineStartPunctuation_PulledBackToPreviousColumn()
    {
        // 「。」落在某欄欄首是行頭禁則；應被拉回上一欄欄尾。
        // 10 字、容量 3 → useCols=4、均分 3/3/2/2 → 第4欄首為 text[8]='。'（禁則）→ 拉回第3欄。
        string text = "12345678。9";
        var counts = FontSizeOptimizer.PlanColumnCounts(text, new[] { 3, 3, 3, 3 });
        string split = Split(text, counts);
        _output.WriteLine(split);

        // 驗證：沒有任何一欄（除第一欄外）以行頭禁則字開頭
        int idx = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (i > 0 && counts[i] > 0)
                Assert.DoesNotContain(text[idx], "。、，！？）」』…");
            idx += counts[i];
        }
        Assert.Equal(text.Length, counts.Sum());
    }

    [Fact]
    public void Kinsoku_LineEndOpenBracket_PushedToNextColumn()
    {
        // 起始括號「（」落在某欄欄尾是行尾禁則；應被推到下一欄欄首。
        string text = "1234（5678"; // （ 在 index 4
        var counts = FontSizeOptimizer.PlanColumnCounts(text, new[] { 5, 5 });
        string split = Split(text, counts);
        _output.WriteLine(split);

        // 第一欄欄尾不應是「（」
        if (counts.Length > 1 && counts[0] > 0)
            Assert.NotEqual('（', text[counts[0] - 1]);
        Assert.Equal(text.Length, counts.Sum());
    }

    [Fact]
    public void NoTextLost_LongTextClampedToLastColumn()
    {
        // 容量總和不足以容納全文時，仍不可漏字（總和需等於長度）。
        var counts = FontSizeOptimizer.PlanColumnCounts("0123456789", new[] { 2, 2 });
        Assert.Equal(10, counts.Sum());
    }
}

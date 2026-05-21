using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class SelfLearningTests
{
    private readonly ITestOutputHelper _output;

    public SelfLearningTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_JapaneseKanjiMatch_ShouldWork()
    {
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, new AppConfig());
        var targets = new List<string> { "鬼滅の刃", "呪術廻戦", "チェンソーマン" };
        var source = "[吾峠呼世晴] 鬼滅の刃 第01巻";

        var result = organizer.FindBestMatch(source, targets);

        _output.WriteLine($"Japanese Match: {result?.FolderName}, Score: {result?.Confidence}");
        Assert.NotNull(result);
        Assert.Equal("鬼滅の刃", result.FolderName);
    }

    [Fact]
    public void Test_EnglishJapaneseMix_ShouldWork()
    {
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, new AppConfig());
        var targets = new List<string> { "One Punch Man (ワンパンマン)" };
        var source = "[ONE×村田雄介] ワンパンマン 第01巻";

        var result = organizer.FindBestMatch(source, targets);

        _output.WriteLine($"Mix Match: {result?.FolderName}, Score: {result?.Confidence}");
        Assert.NotNull(result);
        Assert.Equal("One Punch Man (ワンパンマン)", result.FolderName);
    }

    [Fact]
    public void Test_JapaneseAuthorLearning_ShouldWork()
    {
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, new AppConfig());
        var targets = new List<string> { "新海誠" };
        
        // Step 1: 學習日文作者標籤
        var firstSource = "[新海誠] すずめの戸締まり";
        var firstResult = organizer.FindBestMatch(firstSource, targets);
        Assert.NotNull(firstResult);
        
        // 模擬 PreviewForm 歸檔成功時的 Learn 邏輯
        var kws = organizer.ExtractAllKeywords(firstSource);
        foreach (var kw in kws)
        {
            if (organizer.IsKeywordValidForTarget(kw, firstResult.FolderName))
            {
                intel.Learn(kw, firstResult.FolderName);
            }
        }

        // Step 2: 測試另一個完全不同的日文檔名，但包含相同作者標籤
        var secondSource = "[新海誠] 君の名は。"; 
        var result = organizer.FindBestMatch(secondSource, targets);

        _output.WriteLine($"Learned Japanese Match: {result?.FolderName}, Score: {result?.Confidence}");
        Assert.NotNull(result);
        Assert.Equal("新海誠", result.FolderName);
        Assert.True(result.IsLearned);
    }

    [Fact]
    public void Test_EnglishProjectMatching_ShouldWork()
    {
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, new AppConfig());
        var targets = new List<string> { "Marvel Comics", "DC Comics" };
        var source = "[Marvel] Avengers Issue #001";

        var result = organizer.FindBestMatch(source, targets);

        _output.WriteLine($"English Match: {result?.FolderName}, Score: {result?.Confidence}");
        Assert.NotNull(result);
        Assert.Equal("Marvel Comics", result.FolderName);
    }
}

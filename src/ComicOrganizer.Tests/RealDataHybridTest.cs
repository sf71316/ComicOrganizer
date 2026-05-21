using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class RealDataHybridTest
{
    private readonly ITestOutputHelper _output;

    public RealDataHybridTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_StepByStep_DeepLearning()
    {
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, new AppConfig());
        var targetLibrary = new List<string> { "あずまゆき", "村田雄介" };

        _output.WriteLine("=== Step 1: Initial Learning ===");
        
        var richSource = "[いもむや - Singleton (あずまゆき)] 塾の上のマッサージ屋さん";
        var firstResult = organizer.FindBestMatch(richSource, targetLibrary);
        Assert.NotNull(firstResult);

        // 模擬歸檔確認後，將いもむや與あずまゆき的映射學習進知識庫
        intel.Learn("いもむや", firstResult.FolderName);
        
        // 印出學習到的內容
        _output.WriteLine("Knowledge Base Content:");
        foreach (var mapping in intel.GetAllMappings())
        {
            _output.WriteLine($"  {mapping.Key} -> {mapping.Value}");
        }

        _output.WriteLine("\n=== Step 2: Indirect Matching via Learned Keywords ===");
        
        var clubOnlySource = "[いもむや] 塾の上のマッサージ屋さん‐道連れ快樂墮ち編‐";
        var res2 = organizer.FindBestMatch(clubOnlySource, targetLibrary);

        _output.WriteLine($"Processed: {clubOnlySource}");
        _output.WriteLine($"Matched To: {res2?.FolderName}, Confidence: {res2?.Confidence}, IsLearned: {res2?.IsLearned}");

        // Assert
        Assert.Equal("あずまゆき", res2?.FolderName);
        Assert.True(res2?.IsLearned);
    }

    [Fact]
    public void Test_NoiseFiltering_AIGenerated()
    {
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, new AppConfig());
        var targetLibrary = new List<string> { "水夜座", "むほー地帯" };

        var source = "[水夜座] 散雪 (オリジナル) [AI Generated] [繁中機翻]";
        var res = organizer.FindBestMatch(source, targetLibrary);

        _output.WriteLine($"\nNoise Filter Test:");
        _output.WriteLine($"Source: {source}");
        _output.WriteLine($"Matched: {res?.FolderName}, Score: {res?.Confidence}");
        
        Assert.Equal("水夜座", res?.FolderName);
    }
}

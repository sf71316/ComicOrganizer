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

    [Fact]
    public void Test_SubfolderCategoryDiscovery_And_ShinjitaiCanonicalMatch_ShouldWork()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ComicOrganizer_SubCat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // _Legacy -> 尾崎未來 (Author)、EmptyPending (空資料夾也應保留為目標)、黑澤 (Author)
            var ozakiDir = Path.Combine(tempRoot, "_Legacy", "尾崎未來");
            Directory.CreateDirectory(ozakiDir);
            File.WriteAllBytes(Path.Combine(ozakiDir, "work1.zip"), [0x50, 0x4B, 0x05, 0x06]);

            var kurosawaDir = Path.Combine(tempRoot, "_Legacy", "黑澤");
            Directory.CreateDirectory(kurosawaDir);
            File.WriteAllBytes(Path.Combine(kurosawaDir, "work2.zip"), [0x50, 0x4B, 0x05, 0x06]);

            var emptyPendingDir = Path.Combine(tempRoot, "_Legacy", "EmptyPending");
            Directory.CreateDirectory(emptyPendingDir);

            // _SmallCollection -> KAROMIX(karory) (Author)
            var karomixDir = Path.Combine(tempRoot, "_SmallCollection", "KAROMIX(karory)");
            Directory.CreateDirectory(karomixDir);
            File.WriteAllBytes(Path.Combine(karomixDir, "work3.zip"), [0x50, 0x4B, 0x05, 0x06]);

            // Root 單層 Author
            var cuvieDir = Path.Combine(tempRoot, "Cuvie");
            Directory.CreateDirectory(cuvieDir);
            File.WriteAllBytes(Path.Combine(cuvieDir, "work4.zip"), [0x50, 0x4B, 0x05, 0x06]);

            // Root 下的散落單本 Work 資料夾（不應被當成可歸檔作者目錄）
            var strayWorkDir = Path.Combine(tempRoot, "[Standalone] Stray Work (C99) [DL版]");
            Directory.CreateDirectory(strayWorkDir);
            File.WriteAllBytes(Path.Combine(strayWorkDir, "01.jpg"), [0xFF, 0xD8, 0xFF]);

            // 排除目錄
            var excludedDir = Path.Combine(tempRoot, "_Excluded", "HiddenArtist");
            Directory.CreateDirectory(excludedDir);
            File.WriteAllBytes(Path.Combine(excludedDir, "work5.zip"), [0x50, 0x4B, 0x05, 0x06]);

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_Excluded" };
            var targets = ArchiveOrganizer.DiscoverTargetAuthorFolders(tempRoot, excluded, maxDepth: 3);

            Assert.Contains(Path.Combine("_Legacy", "尾崎未來"), targets);
            Assert.Contains(Path.Combine("_Legacy", "黑澤"), targets);
            Assert.Contains(Path.Combine("_Legacy", "EmptyPending"), targets);
            Assert.Contains(Path.Combine("_SmallCollection", "KAROMIX(karory)"), targets);
            Assert.Contains("Cuvie", targets);
            Assert.DoesNotContain("_Legacy", targets);
            Assert.DoesNotContain("_SmallCollection", targets);
            Assert.DoesNotContain("[Standalone] Stray Work (C99) [DL版]", targets);
            Assert.DoesNotContain(Path.Combine("_Excluded", "HiddenArtist"), targets);

            var intel = new MappingIntelligence(Path.Combine(tempRoot, "mappings.json"));
            var organizer = new ArchiveOrganizer(intel, new AppConfig { MinMatchConfidence = 75.0 });

            // 1. 日文新字體 [BEAT-POP (尾崎未来)] 應直接命中 _Legacy\尾崎未來，且不誤建議新建根目錄
            var ozakiMatch = organizer.FindBestMatch("[BEAT-POP (尾崎未来)] 放課後ラブモード2 [DL版]", targets);
            Assert.NotNull(ozakiMatch);
            Assert.Equal(Path.Combine("_Legacy", "尾崎未來"), ozakiMatch.FolderName);
            Assert.True(ozakiMatch.Confidence >= 95.0);
            Assert.Null(organizer.TrySuggestNewAuthorFolder("[BEAT-POP (尾崎未来)] 放課後ラブモード2 [DL版]", targets));

            // 2. 日文新字體 黒沢 應對齊 _Legacy\黑澤
            var kurosawaMatch = organizer.FindBestMatch("[黒沢] 夜想曲 [DL版]", targets);
            Assert.NotNull(kurosawaMatch);
            Assert.Equal(Path.Combine("_Legacy", "黑澤"), kurosawaMatch.FolderName);
            Assert.True(kurosawaMatch.Confidence >= 95.0);

            // 3. 搜尋下拉排序輸入「尾崎未来」第一名應為 _Legacy\尾崎未來
            var ranked = ArchiveOrganizer.RankTargetFolders("尾崎未来", targets);
            Assert.NotEmpty(ranked);
            Assert.Equal(Path.Combine("_Legacy", "尾崎未來"), ranked[0].FolderName);
            Assert.True(ranked[0].Score >= 950);

            // 4. 舊知識庫若存有葉目錄 "尾崎未來"（後來搬入 _Legacy），以 "尾崎未来" 查詢也能延續命中 _Legacy\尾崎未來
            intel.Learn("尾崎未來", "尾崎未來");
            var learnedMatch = organizer.FindBestMatch("[某社團 (尾崎未来)] 特別篇.zip", targets);
            Assert.NotNull(learnedMatch);
            Assert.Equal(Path.Combine("_Legacy", "尾崎未來"), learnedMatch.FolderName);
            Assert.True(learnedMatch.IsLearned);
            Assert.True(organizer.IsKeywordValidForTarget("尾崎未来", Path.Combine("_Legacy", "尾崎未來")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

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

    [Fact]
    public void Test_ScreenshotRegression_AIGeneratedAndShortCollisions_MustReturnNull()
    {
        var intel = new MappingIntelligence();
        // 模擬舊學習庫即使曾誤學 "ai" 與 "no"，也不得污染正常檔案
        intel.Learn("ai", "みずゆきRenainou");
        intel.Learn("no", "Deadnoodles");

        var config = new AppConfig();
        // 模擬舊版 appsettings.json 尚未包含 "AI Translate" 等新黑名單詞的情況
        config.Blacklist = new List<string> { "AI Generated", "DL版" };
        var organizer = new ArchiveOrganizer(intel, config);

        var targets = new List<string>
        {
            "みずゆきRenainou",
            "ピロコボ (Piro)",
            "粉織Koori",
            "Deadnoodles",
            "きい",
            "みたらし倶楽部",
            "一ノ宮(Rosapersica)",
            "[MM]",
            "睦月",
            "CREAYUS (嵐月)"
        };

        // 1. 截圖真實案例：[EchicalH] ... [AI Generated][AI Translate] 絕不可被 "Ren[ai]nou" 吸走
        Assert.Null(organizer.FindBestMatch("[EchicalH] えちえち学園チア部 [AI Generated] [AI Translate]", targets));
        Assert.Null(organizer.FindBestMatch("[EchicalH] ホロえっちvol.1 [AI絵図][AI Translate].zip", targets));

        // 2. 分詞與子字串碰撞案例：'x' 不得拆 "Pixiv"；"ko"/"no"/"DL"/"倶楽部"/"Rosa" 不得誤中他人目錄
        Assert.Null(organizer.FindBestMatch("[Pixiv] Truly_Beary Artist Collection", targets));
        Assert.Null(organizer.FindBestMatch("[Ocha no ko] Sample Title [DL版]", targets));
        Assert.Null(organizer.FindBestMatch("[Yoru no Bukiya-san] Fantasy Sword [DL].zip", targets));
        Assert.Null(organizer.FindBestMatch("[無名作者] Story Title (ANGEL 倶楽部 2025年6月号) [中国翻訳]", targets));
        Assert.Null(organizer.FindBestMatch("PureTone - メイ(Rosa) [中国翻訳]", targets));

        // 3. 短目標目錄（きい）不得子字串命中「むらさきいろのよる」，無對應作者（雨宮ミズキ）應回 null 而非 38%
        Assert.Null(organizer.FindBestMatch("[むらさきいろのよる (むらさき＊)] 夜之物語 [DL版]", targets));
        Assert.Null(organizer.FindBestMatch("[雨宮ミズキ] センセへのお礼はオフパコです [DL版].zip", targets));

        // 4. 正向命中：別名與短目標精確作者應仍能高信心正確配對
        var hitRenainou = organizer.FindBestMatch("[みずゆき] 新作本 [AI Generated][AI Translate].zip", targets);
        Assert.NotNull(hitRenainou);
        Assert.Equal("みずゆきRenainou", hitRenainou.FolderName);
        Assert.True(hitRenainou.Confidence >= 95.0);

        var hitShortExact = organizer.FindBestMatch("[きい] 真實作品.zip", targets);
        Assert.NotNull(hitShortExact);
        Assert.Equal("きい", hitShortExact.FolderName);

        var hitMM = organizer.FindBestMatch("[MM] Sample Comic [DL版].zip", targets);
        Assert.NotNull(hitMM);
        Assert.Equal("[MM]", hitMM.FolderName);

        var hitAlias = organizer.FindBestMatch("[嵐月] CREAYUS 畫集 [中文].zip", targets);
        Assert.NotNull(hitAlias);
        Assert.Equal("CREAYUS (嵐月)", hitAlias.FolderName);
    }
}

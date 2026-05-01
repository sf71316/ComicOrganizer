using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class FullRealDataBatchTest
{
    private readonly ITestOutputHelper _output;

    public FullRealDataBatchTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_FullBatch_FromDirectory()
    {
        var config = new AppConfig(); // 使用預設的優化黑名單
        var intel = new MappingIntelligence();
        var organizer = new ArchiveOrganizer(intel, config);

        string testPath = @"E:\Project\ComicOrganizer\src\ComicOrganizer.Tests\Tests\H";
        var files = Directory.GetFileSystemEntries(testPath);
        
        // 建立模擬目標庫 (從檔名中提取可能的作者名)
        var targetLibrary = new List<string> 
        { 
            "あずまゆき", "新海誠", "村田雄介", "夏尾", "星名めいと", 
            "あおやまきいろ", "ゆずソフト", "琵琶麻呂", "仁堂ジョー", 
            "ツクヨミ", "水夜座", "むほー地帯", "吾峠呼世晴", "しにま",
            "六堂犬彦", "仲原", "真冬", "ひさぎ", "茜", "倉田むと", "壱河イチタ",
            "灰綿たゆた", "草野ゆぅ", "丸新", "風花ゆらり", "餅田こゆび", "もとび",
            "オウカ", "子パンダ", "ずり太", "野村輝弥", "ユズハ", "アイスのあ",
            "北原亜希", "だにまる", "水上凛香", "東條土筆", "森ぐる太", "へたれん", "ムシ", "MK", "鯖野筆置", "紅村かる"
        };

        _output.WriteLine($"Testing {files.Length} items...");
        _output.WriteLine($"{"Source File",-60} | {"Matched Folder",-20} | {"Score"}");
        _output.WriteLine(new string('-', 100));

        int matchedCount = 0;
        foreach (var path in files)
        {
            string name = Path.GetFileName(path);
            var result = organizer.FindBestMatch(name, targetLibrary);

            if (result != null && result.Confidence > 60)
            {
                matchedCount++;
                _output.WriteLine($"{Truncate(name, 58),-60} | {result.FolderName,-20} | {result.Confidence:F1}");
            }
        }

        _output.WriteLine(new string('-', 100));
        _output.WriteLine($"Total Items: {files.Length}, Matched (>60 score): {matchedCount}");
        _output.WriteLine($"Knowledge Base Size: {intel.KnowledgeCount}");
    }

    private string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value.Substring(0, maxChars - 3) + "...";
    }
}

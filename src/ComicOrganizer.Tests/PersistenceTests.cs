using ComicOrganizer.App.Core;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

public class PersistenceTests : IDisposable
{
    private readonly string _testPath = "test_mappings.json";

    public void Dispose()
    {
        if (File.Exists(_testPath)) File.Delete(_testPath);
    }

    [Fact]
    public void Test_Persistence_ShouldSaveAndLoad()
    {
        // 1. 第一個 Session：學習
        var intel1 = new MappingIntelligence(_testPath);
        intel1.Learn("TEST_KEY", "TARGET_FOLDER");
        // 自動儲存已觸發

        // 2. 第二個 Session：載入並驗證
        var intel2 = new MappingIntelligence(_testPath);
        var result = intel2.TryGetMappedFolder("Prefix_TEST_KEY_Suffix");

        Assert.Equal("TARGET_FOLDER", result);
        Assert.Equal(1, intel2.KnowledgeCount);
    }
}

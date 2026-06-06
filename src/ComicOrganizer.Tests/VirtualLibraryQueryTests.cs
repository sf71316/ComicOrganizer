using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 虛擬庫 DB 查詢測試（#48 Phase 1）：
/// ListWorksByAuthor / ListWorksByTags(AND/OR) / ListLanguageBuckets /
/// ListWorksByLanguage / ListRecentlyRead / ListReadingInProgress /
/// ListMostReadAuthors / ListMostReadTags
/// </summary>
public class VirtualLibraryQueryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public VirtualLibraryQueryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_vlib_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    /// <summary>建立 1 筆 asset + asset_meta，支援 author / language / read_count / read_status。</summary>
    private long SeedAsset(string path, long? authorId = null, string? language = null,
        int readCount = 0, ReadStatus status = ReadStatus.Unread, long? lastRead = null)
    {
        _store.Upsert(new AssetMeta { Path = path, Name = Path.GetFileName(path), IsFolder = false, FileSize = 1, Mtime = 1 });
        var id = _store.GetAssetId(path)!.Value;
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = id, AuthorId = authorId, Title = Path.GetFileNameWithoutExtension(path),
            Language = language, ReadCount = readCount, ReadStatus = status, LastRead = lastRead, ParsedAt = 1,
        });
        return id;
    }

    // ===== ListWorksByAuthor =====

    [Fact]
    public void ListWorksByAuthor_ReturnsOnlyThatAuthorWorks()
    {
        var a1 = _store.UpsertAuthor("AuthorA");
        var a2 = _store.UpsertAuthor("AuthorB");
        SeedAsset(@"C:\L\a1.zip", a1);
        SeedAsset(@"C:\L\a2.zip", a1);
        SeedAsset(@"C:\L\b1.zip", a2);

        var aResults = _store.ListWorksByAuthor(a1);
        Assert.Equal(2, aResults.Count);
        Assert.Contains(@"C:\L\a1.zip", aResults);
        Assert.Contains(@"C:\L\a2.zip", aResults);
    }

    // ===== ListWorksByTags（AND / OR）=====

    [Fact]
    public void ListWorksByTags_AndIntersection()
    {
        var id1 = SeedAsset(@"C:\L\w1.zip");
        var id2 = SeedAsset(@"C:\L\w2.zip");
        var id3 = SeedAsset(@"C:\L\w3.zip");
        // w1 = [tag1, tag2]; w2 = [tag1]; w3 = [tag2]
        _store.SetAssetTags(id1, new[] { "tag1", "tag2" });
        _store.SetAssetTags(id2, new[] { "tag1" });
        _store.SetAssetTags(id3, new[] { "tag2" });

        var t1 = _store.SearchTags("tag1", 5).First().Id;
        var t2 = _store.SearchTags("tag2", 5).First().Id;

        // AND tag1 + tag2 → 只 w1
        var both = _store.ListWorksByTags(new[] { t1, t2 }, intersect: true);
        Assert.Single(both);
        Assert.Equal(@"C:\L\w1.zip", both[0]);

        // OR tag1 + tag2 → w1 / w2 / w3 全部
        var any = _store.ListWorksByTags(new[] { t1, t2 }, intersect: false);
        Assert.Equal(3, any.Count);
    }

    [Fact]
    public void ListWorksByTags_EmptyInput_ReturnsEmpty()
    {
        var results = _store.ListWorksByTags(Array.Empty<long>());
        Assert.Empty(results);
    }

    // ===== ListLanguageBuckets / ListWorksByLanguage =====

    [Fact]
    public void ListLanguageBuckets_GroupsByLanguageIncludingNull()
    {
        SeedAsset(@"C:\L\zh1.zip", language: "zh-CN");
        SeedAsset(@"C:\L\zh2.zip", language: "zh-CN");
        SeedAsset(@"C:\L\ja1.zip", language: "ja");
        SeedAsset(@"C:\L\unknown.zip", language: null);

        var buckets = _store.ListLanguageBuckets();
        Assert.Equal(3, buckets.Count);

        var zh = buckets.First(b => b.Language == "zh-CN");
        Assert.Equal(2, zh.WorkCount);

        var unknown = buckets.First(b => b.Language == null);
        Assert.Equal(1, unknown.WorkCount);
    }

    [Fact]
    public void ListWorksByLanguage_NullPicksUnlabeled()
    {
        SeedAsset(@"C:\L\zh1.zip", language: "zh-CN");
        SeedAsset(@"C:\L\unknown.zip", language: null);

        var unlabeled = _store.ListWorksByLanguage(null);
        Assert.Single(unlabeled);
        Assert.Equal(@"C:\L\unknown.zip", unlabeled[0]);

        var zh = _store.ListWorksByLanguage("zh-CN");
        Assert.Single(zh);
        Assert.Equal(@"C:\L\zh1.zip", zh[0]);
    }

    // ===== ListRecentlyRead =====

    [Fact]
    public void ListRecentlyRead_OrdersByLastReadDesc()
    {
        SeedAsset(@"C:\L\old.zip", readCount: 1, lastRead: 1000);
        SeedAsset(@"C:\L\mid.zip", readCount: 1, lastRead: 2000);
        SeedAsset(@"C:\L\new.zip", readCount: 1, lastRead: 3000);
        SeedAsset(@"C:\L\never.zip");   // last_read=null 不應出現

        var recent = _store.ListRecentlyRead(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal(@"C:\L\new.zip", recent[0]);
        Assert.Equal(@"C:\L\mid.zip", recent[1]);
        Assert.Equal(@"C:\L\old.zip", recent[2]);
    }

    [Fact]
    public void ListRecentlyRead_RespectsLimit()
    {
        for (int i = 0; i < 15; i++)
            SeedAsset($@"C:\L\w{i}.zip", readCount: 1, lastRead: 1000 + i);

        var top5 = _store.ListRecentlyRead(5);
        Assert.Equal(5, top5.Count);
    }

    // ===== ListReadingInProgress =====

    [Fact]
    public void ListReadingInProgress_OnlyReadingStatus()
    {
        SeedAsset(@"C:\L\a.zip", status: ReadStatus.Unread);
        SeedAsset(@"C:\L\b.zip", status: ReadStatus.Reading, lastRead: 100);
        SeedAsset(@"C:\L\c.zip", status: ReadStatus.Completed);
        SeedAsset(@"C:\L\d.zip", status: ReadStatus.Reading, lastRead: 200);

        var reading = _store.ListReadingInProgress();
        Assert.Equal(2, reading.Count);
        Assert.Equal(@"C:\L\d.zip", reading[0]);   // last_read DESC
        Assert.Equal(@"C:\L\b.zip", reading[1]);
    }

    // ===== ListMostReadAuthors =====

    [Fact]
    public void ListMostReadAuthors_SumsReadCountAcrossWorks()
    {
        var a1 = _store.UpsertAuthor("PopularAuthor");
        var a2 = _store.UpsertAuthor("RareAuthor");
        var a3 = _store.UpsertAuthor("UnreadAuthor");

        SeedAsset(@"C:\L\p1.zip", a1, readCount: 5);
        SeedAsset(@"C:\L\p2.zip", a1, readCount: 3);
        SeedAsset(@"C:\L\r1.zip", a2, readCount: 2);
        SeedAsset(@"C:\L\u1.zip", a3, readCount: 0);   // 不應出現

        var top = _store.ListMostReadAuthors(10);
        Assert.Equal(2, top.Count);
        Assert.Equal("PopularAuthor", top[0].Author.MainName);
        Assert.Equal(8, top[0].TotalReads);
        Assert.Equal("RareAuthor", top[1].Author.MainName);
        Assert.Equal(2, top[1].TotalReads);
    }

    // ===== ListMostReadTags =====

    [Fact]
    public void ListMostReadTags_SumsReadCountForWorksWithTag()
    {
        var id1 = SeedAsset(@"C:\L\w1.zip", readCount: 5);
        var id2 = SeedAsset(@"C:\L\w2.zip", readCount: 3);
        var id3 = SeedAsset(@"C:\L\w3.zip", readCount: 0);   // 不算
        _store.SetAssetTags(id1, new[] { "popular" });
        _store.SetAssetTags(id2, new[] { "popular", "minor" });
        _store.SetAssetTags(id3, new[] { "popular" });

        var top = _store.ListMostReadTags(10);
        Assert.Equal(2, top.Count);   // popular / minor，沒讀的不算
        var popular = top.First(t => t.Tag.Name == "popular");
        Assert.Equal(8, popular.TotalReads);   // w1 + w2 = 5 + 3
        var minor = top.First(t => t.Tag.Name == "minor");
        Assert.Equal(3, minor.TotalReads);
    }

    // ===== ListRecentlyAdded =====

    [Fact]
    public void ListRecentlyAdded_OrdersByAddedAtDesc()
    {
        // 利用 LastScanned 來控制 added_at
        _store.Upsert(new AssetMeta { Path = @"C:\L\a1.zip", Name = "a1.zip", IsFolder = false, FileSize = 1, Mtime = 1, LastScanned = 1000 });
        _store.Upsert(new AssetMeta { Path = @"C:\L\a2.zip", Name = "a2.zip", IsFolder = false, FileSize = 1, Mtime = 1, LastScanned = 3000 });
        _store.Upsert(new AssetMeta { Path = @"C:\L\a3.zip", Name = "a3.zip", IsFolder = false, FileSize = 1, Mtime = 1, LastScanned = 2000 });
        
        // 資料夾不應出現在最近新增作品列表中 (WHERE is_folder = 0)
        _store.Upsert(new AssetMeta { Path = @"C:\L\folder", Name = "folder", IsFolder = true, FileSize = 0, Mtime = 1, LastScanned = 4000 });

        var recent = _store.ListRecentlyAdded(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal(@"C:\L\a2.zip", recent[0]); // 3000
        Assert.Equal(@"C:\L\a3.zip", recent[1]); // 2000
        Assert.Equal(@"C:\L\a1.zip", recent[2]); // 1000
    }

    [Fact]
    public void ListRecentlyAdded_RespectsLimit()
    {
        for (int i = 0; i < 15; i++)
        {
            _store.Upsert(new AssetMeta { Path = $@"C:\L\w{i}.zip", Name = $"w{i}.zip", IsFolder = false, FileSize = 1, Mtime = 1, LastScanned = 1000 + i });
        }

        var top5 = _store.ListRecentlyAdded(5);
        Assert.Equal(5, top5.Count);
        Assert.Equal(@"C:\L\w14.zip", top5[0]); // 最新的
    }
}

using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 驗 v2 schema 的 author / tag / asset_meta CRUD：
///   - Author UPSERT（同名 reuse id）+ alias 多語
///   - Tag UPSERT（同名 reuse id）
///   - AssetMetadata 1:1 與 asset，含 ReadStatus enum / read_count / last_read
///   - cascade delete：刪 asset 應連帶刪 thumb/meta/tag 關聯
/// </summary>
public class MetadataPersistenceTest : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public MetadataPersistenceTest()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_meta_test_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void Author_Upsert_SameName_Reuses_Id()
    {
        var id1 = _store.UpsertAuthor("Flugel", folderPath: @"C:\Library\Flugel");
        var id2 = _store.UpsertAuthor("Flugel");   // 同名 → reuse
        Assert.Equal(id1, id2);

        var found = _store.FindAuthorByMainName("Flugel");
        Assert.NotNull(found);
        Assert.Equal(id1, found!.Id);
        Assert.Equal(@"C:\Library\Flugel", found.FolderPath);
    }

    [Fact]
    public void Author_Aliases_MultipleLangs()
    {
        var id = _store.UpsertAuthor("タイガー");
        _store.AddAuthorAlias(id, "Tiger Roman", "en-romaji");
        _store.AddAuthorAlias(id, "老虎", "zh-TW");
        _store.AddAuthorAlias(id, "Tiger Roman", "en-romaji");   // 重複應被 ignore

        var aliases = _store.ListAuthorAliases(id);
        Assert.Equal(2, aliases.Count);   // 重複那筆不算
        Assert.Contains(aliases, a => a.Name == "Tiger Roman" && a.Lang == "en-romaji");
        Assert.Contains(aliases, a => a.Name == "老虎" && a.Lang == "zh-TW");
    }

    [Fact]
    public void Tag_Upsert_SameName_Reuses_Id()
    {
        var t1 = _store.UpsertTag("中文", "language");
        var t2 = _store.UpsertTag("中文");   // 同名 reuse
        Assert.Equal(t1, t2);
    }

    [Fact]
    public void AssetMetadata_Upsert_RoundTrip()
    {
        // 先建 asset
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Library\Work1.zip",
            Name = "Work1.zip",
            IsFolder = false,
            Mtime = 100,
            LastScanned = 100,
            LastAccessed = 100,
        });
        // 取 asset_id：用 Get 取不到 id，但 UpsertAuthor 邏輯一樣用 RETURNING；
        // 直接用 SQL 抓
        var assetId = GetAssetId(@"C:\Library\Work1.zip");

        var authorId = _store.UpsertAuthor("Flugel");
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            AuthorId = authorId,
            Title = "くぎづけはーと",
            Series = "Pin Down Heart",
            ChapterNo = "Vol.1",
            Language = "ja",
            TranslationGroup = "無邪気漢化組",
            PageCount = 24,
            Rating = 4,
            ReadStatus = ReadStatus.Reading,
            ReadCount = 3,
            LastRead = 1700100000,
            ParsedAt = 1700200000,
        });

        var got = _store.GetMetadata(assetId);
        Assert.NotNull(got);
        Assert.Equal(authorId, got!.AuthorId);
        Assert.Equal("くぎづけはーと", got.Title);
        Assert.Equal("Pin Down Heart", got.Series);
        Assert.Equal("ja", got.Language);
        Assert.Equal(24, got.PageCount);
        Assert.Equal(ReadStatus.Reading, got.ReadStatus);
        Assert.Equal(3, got.ReadCount);
        Assert.Equal(1700100000L, got.LastRead);
    }

    [Fact]
    public void AssetTag_Association_AndListing()
    {
        _store.Upsert(new AssetMeta { Path = @"C:\Library\W.zip", Name = "W.zip", IsFolder = false, Mtime = 1, LastScanned = 1, LastAccessed = 1 });
        var assetId = GetAssetId(@"C:\Library\W.zip");
        var t1 = _store.UpsertTag("中文", "language");
        var t2 = _store.UpsertTag("校園", "genre");
        _store.AssociateTag(assetId, t1);
        _store.AssociateTag(assetId, t2);
        _store.AssociateTag(assetId, t1);   // 重複 ignore

        var tags = _store.ListTagsForAsset(assetId);
        Assert.Equal(2, tags.Count);
        Assert.Contains(tags, t => t.Name == "中文" && t.Category == "language");
    }

    [Fact]
    public void DeleteAsset_Cascades_ToThumbMetaAndTags()
    {
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Library\X.zip",
            Name = "X.zip",
            IsFolder = false,
            Mtime = 1,
            CoverThumbKey = "abc123",
            CoverWidth = 330,
            CoverHeight = 467,
            LastScanned = 1,
            LastAccessed = 1,
        });
        var assetId = GetAssetId(@"C:\Library\X.zip");
        _store.UpsertMetadata(new AssetMetadata { AssetId = assetId, Title = "X", ParsedAt = 1 });
        var tagId = _store.UpsertTag("test");
        _store.AssociateTag(assetId, tagId);

        // sanity
        Assert.NotNull(_store.Get(@"C:\Library\X.zip"));
        Assert.NotNull(_store.GetMetadata(assetId));
        Assert.Single(_store.ListTagsForAsset(assetId));

        // 刪除
        _store.Delete(@"C:\Library\X.zip");

        // catalog 不存在
        Assert.Null(_store.Get(@"C:\Library\X.zip"));
        // metadata cascaded
        Assert.Null(_store.GetMetadata(assetId));
        // tag 關聯被切（tag 自己可能還在）
        Assert.Empty(_store.ListTagsForAsset(assetId));
    }

    [Fact]
    public void ClearAllMetadata_KeepsCatalogAndThumb()
    {
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\Library\Y.zip",
            Name = "Y.zip",
            IsFolder = false,
            Mtime = 1,
            CoverThumbKey = "def",
            LastScanned = 1,
            LastAccessed = 1,
        });
        var assetId = GetAssetId(@"C:\Library\Y.zip");
        _store.UpsertMetadata(new AssetMetadata { AssetId = assetId, Title = "Y", ParsedAt = 1 });

        _store.ClearAllMetadata();

        // metadata 清掉
        Assert.Null(_store.GetMetadata(assetId));
        // catalog + thumb 還在
        var got = _store.Get(@"C:\Library\Y.zip");
        Assert.NotNull(got);
        Assert.Equal("def", got!.CoverThumbKey);
    }

    private long GetAssetId(string path)
    {
        // Test helper：直接 SQL 抓 id（不暴露在介面，避免 leak SQL 細節到 production code）
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM asset WHERE path = $p;";
        cmd.Parameters.AddWithValue("$p", path);
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }
}

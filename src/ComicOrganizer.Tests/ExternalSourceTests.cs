using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// asset_external_source 表 + IAssetMetaStore 外部 metadata API 測試（#52 Phase A）。
/// </summary>
public class ExternalSourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public ExternalSourceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_extsrc_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long SeedAsset(string path = @"C:\lib\[Author] work.zip")
    {
        _store.Upsert(new AssetMeta
        {
            Path = path,
            Name = Path.GetFileName(path),
            IsFolder = false,
            FileSize = 1,
            Mtime = 1,
        });
        return _store.GetAssetId(path)!.Value;
    }

    [Fact]
    public void V5_Schema_AddsExternalSourceTable()
    {
        // 透過 API 操作驗證表存在
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = id,
            Source = "nhentai",
            SourceId = "568742",
            RawJson = "{}",
            FetchedAt = 1,
        });
        Assert.NotNull(_store.GetExternalSource(id, "nhentai"));
    }

    [Fact]
    public void Upsert_GetByAssetIdAndSource()
    {
        var id = SeedAsset();
        var record = new AssetExternalSource
        {
            AssetId = id,
            Source = "nhentai",
            SourceId = "568742",
            MediaId = "3319522",
            UploadAt = 1744974299,
            RawJson = """{"id":568742,"title":{"pretty":"Kugizuke Heart"}}""",
            FetchedAt = 1700000000,
        };
        _store.UpsertExternalSource(record);

        var got = _store.GetExternalSource(id, "nhentai");

        Assert.NotNull(got);
        Assert.Equal(id, got!.AssetId);
        Assert.Equal("nhentai", got.Source);
        Assert.Equal("568742", got.SourceId);
        Assert.Equal("3319522", got.MediaId);
        Assert.Equal(1744974299L, got.UploadAt);
        Assert.Contains("Kugizuke Heart", got.RawJson);
        Assert.Equal(1700000000L, got.FetchedAt);
    }

    [Fact]
    public void Upsert_OverwritesExisting()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = id, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1,
        });
        _store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = id, Source = "nhentai", SourceId = "222", RawJson = "{\"new\":true}", FetchedAt = 2,
        });

        var got = _store.GetExternalSource(id, "nhentai");
        Assert.Equal("222", got!.SourceId);
        Assert.Equal(2L, got.FetchedAt);
        Assert.Contains("new", got.RawJson);
    }

    [Fact]
    public void List_ReturnsAllSourcesForAsset()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "1", RawJson = "{}", FetchedAt = 1 });
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "ehentai", SourceId = "2", RawJson = "{}", FetchedAt = 2 });

        var list = _store.ListExternalSources(id);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, r => r.Source == "nhentai");
        Assert.Contains(list, r => r.Source == "ehentai");
    }

    [Fact]
    public void Delete_RemovesOnlySpecifiedSource()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "1", RawJson = "{}", FetchedAt = 1 });
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "ehentai", SourceId = "2", RawJson = "{}", FetchedAt = 2 });

        _store.DeleteExternalSource(id, "nhentai");

        Assert.Null(_store.GetExternalSource(id, "nhentai"));
        Assert.NotNull(_store.GetExternalSource(id, "ehentai"));
    }

    [Fact]
    public void FindAssetByExternalId_ReverseLookup()
    {
        var id1 = SeedAsset(@"C:\lib\[A] w1.zip");
        var id2 = SeedAsset(@"C:\lib\[A] w2.zip");
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id1, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1 });
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id2, Source = "nhentai", SourceId = "222", RawJson = "{}", FetchedAt = 2 });

        Assert.Equal(id1, _store.FindAssetByExternalId("nhentai", "111"));
        Assert.Equal(id2, _store.FindAssetByExternalId("nhentai", "222"));
        Assert.Null(_store.FindAssetByExternalId("nhentai", "999"));
        Assert.Null(_store.FindAssetByExternalId("ehentai", "111"));   // 同 source_id 不同 source
    }

    [Fact]
    public void Cascade_DeleteAsset_RemovesExternalSources()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1 });
        Assert.NotNull(_store.GetExternalSource(id, "nhentai"));

        _store.DeleteAssetById(id);

        Assert.Null(_store.GetExternalSource(id, "nhentai"));
        Assert.Null(_store.FindAssetByExternalId("nhentai", "111"));
    }

    [Fact]
    public void Clear_RemovesExternalSources()
    {
        var id = SeedAsset();
        _store.UpsertExternalSource(new AssetExternalSource { AssetId = id, Source = "nhentai", SourceId = "111", RawJson = "{}", FetchedAt = 1 });

        _store.Clear();

        Assert.Null(_store.FindAssetByExternalId("nhentai", "111"));
    }
}

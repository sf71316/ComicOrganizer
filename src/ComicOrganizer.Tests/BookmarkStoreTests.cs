using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>Reader 書籤 CRUD 測試（#51 Phase 2-B）— schema v8。</summary>
public class BookmarkStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private readonly long _assetId;

    public BookmarkStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_bookmark_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);

        // 建一個 asset 給 bookmark 掛上 — 走 Upsert 即可拿 id
        _store.Upsert(new AssetMeta
        {
            Path = @"C:\test\sample.zip",
            Name = "sample.zip",
            IsFolder = false,
            Mtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            FileSize = 1024,
        });
        _assetId = _store.GetAssetId(@"C:\test\sample.zip")!.Value;
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void Add_Then_List_RoundTrip()
    {
        var id = _store.AddBookmark(_assetId, pageIndex: 5, label: "重點頁");
        Assert.True(id > 0);

        var list = _store.ListBookmarks(_assetId);
        Assert.Single(list);
        Assert.Equal(id, list[0].Id);
        Assert.Equal(_assetId, list[0].AssetId);
        Assert.Equal(5, list[0].PageIndex);
        Assert.Equal("重點頁", list[0].Label);
        Assert.True(list[0].CreatedAt > 0);
    }

    [Fact]
    public void Add_NullLabel_AllowedAsPureBookmark()
    {
        var id = _store.AddBookmark(_assetId, 0, label: null);
        var list = _store.ListBookmarks(_assetId);
        Assert.Single(list);
        Assert.Null(list[0].Label);
        Assert.Equal(0, list[0].PageIndex);
    }

    [Fact]
    public void Add_DuplicateAssetPage_Throws()
    {
        _store.AddBookmark(_assetId, 3, "first");
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() =>
            _store.AddBookmark(_assetId, 3, "second"));
    }

    [Fact]
    public void DeleteBookmark_RemovesById()
    {
        var id = _store.AddBookmark(_assetId, 7, null);
        _store.DeleteBookmark(id);
        Assert.Empty(_store.ListBookmarks(_assetId));
    }

    [Fact]
    public void DeleteBookmarkByPage_TogglesOff()
    {
        _store.AddBookmark(_assetId, 10, "x");
        Assert.True(_store.HasBookmarkOnPage(_assetId, 10));

        _store.DeleteBookmarkByPage(_assetId, 10);
        Assert.False(_store.HasBookmarkOnPage(_assetId, 10));
    }

    [Fact]
    public void DeleteBookmarkByPage_NoRecord_NoOp()
    {
        // 沒書籤也不該 throw
        _store.DeleteBookmarkByPage(_assetId, 999);
        Assert.Empty(_store.ListBookmarks(_assetId));
    }

    [Fact]
    public void RenameBookmark_UpdatesLabel()
    {
        var id = _store.AddBookmark(_assetId, 2, "舊標題");
        _store.RenameBookmark(id, "新標題");
        var list = _store.ListBookmarks(_assetId);
        Assert.Equal("新標題", list[0].Label);
    }

    [Fact]
    public void RenameBookmark_NullClearsLabel()
    {
        var id = _store.AddBookmark(_assetId, 1, "有標題");
        _store.RenameBookmark(id, null);
        Assert.Null(_store.ListBookmarks(_assetId)[0].Label);
    }

    [Fact]
    public void List_OrdersByPageIndex()
    {
        _store.AddBookmark(_assetId, 10, "c");
        _store.AddBookmark(_assetId, 3, "a");
        _store.AddBookmark(_assetId, 7, "b");

        var list = _store.ListBookmarks(_assetId);
        Assert.Equal(3, list.Count);
        Assert.Equal(3, list[0].PageIndex);
        Assert.Equal(7, list[1].PageIndex);
        Assert.Equal(10, list[2].PageIndex);
    }

    [Fact]
    public void List_Empty_ReturnsEmpty()
    {
        Assert.Empty(_store.ListBookmarks(_assetId));
    }

    [Fact]
    public void HasBookmarkOnPage_FalseWhenAbsent()
    {
        Assert.False(_store.HasBookmarkOnPage(_assetId, 0));
    }

    [Fact]
    public void Cascade_DeleteAsset_RemovesBookmarks()
    {
        _store.AddBookmark(_assetId, 1, "x");
        _store.AddBookmark(_assetId, 2, "y");
        Assert.Equal(2, _store.ListBookmarks(_assetId).Count);

        _store.Delete(@"C:\test\sample.zip");
        // asset 走 Delete，bookmark 應 cascade
        Assert.Empty(_store.ListBookmarks(_assetId));
    }

    [Fact]
    public void Schema_VersionUpdatedTo8()
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt32(cmd.ExecuteScalar()!);
        Assert.Equal(9, v);
    }
}

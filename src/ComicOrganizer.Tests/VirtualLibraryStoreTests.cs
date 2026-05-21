using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>自訂虛擬庫 CRUD 測試（#54 Phase 1）— schema v7。</summary>
public class VirtualLibraryStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public VirtualLibraryStoreTests()
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

    [Fact]
    public void Create_Then_Get_RoundTrip()
    {
        var id = _store.CreateVirtualLibrary("Flugel 中文版", "artist:flugel language:zh", "BookOutlined");
        Assert.True(id > 0);

        var got = _store.GetVirtualLibrary(id);
        Assert.NotNull(got);
        Assert.Equal("Flugel 中文版", got!.Name);
        Assert.Equal("artist:flugel language:zh", got.Dsl);
        Assert.Equal("BookOutlined", got.IconSvg);
        Assert.False(got.Pinned);
        Assert.Equal(0, got.SortOrder);
        Assert.True(got.CreatedAt > 0);
        Assert.Equal(got.CreatedAt, got.UpdatedAt);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        Assert.Null(_store.GetVirtualLibrary(99999));
    }

    [Fact]
    public void Update_ChangesFields_AndUpdatesTimestamp()
    {
        var id = _store.CreateVirtualLibrary("舊名", "tag:foo", null);
        var before = _store.GetVirtualLibrary(id)!;
        Thread.Sleep(5);   // 確保 updated_at 跟 created_at 差至少 1ms

        _store.UpdateVirtualLibrary(id, "新名", "tag:bar tag:baz", "StarOutlined");
        var after = _store.GetVirtualLibrary(id)!;

        Assert.Equal("新名", after.Name);
        Assert.Equal("tag:bar tag:baz", after.Dsl);
        Assert.Equal("StarOutlined", after.IconSvg);
        Assert.True(after.UpdatedAt > before.UpdatedAt);
        Assert.Equal(before.CreatedAt, after.CreatedAt);   // created_at 不變
    }

    [Fact]
    public void Delete_RemovesRecord()
    {
        var id = _store.CreateVirtualLibrary("待刪", "x", null);
        Assert.NotNull(_store.GetVirtualLibrary(id));

        _store.DeleteVirtualLibrary(id);
        Assert.Null(_store.GetVirtualLibrary(id));
    }

    [Fact]
    public void SetPinned_Toggles()
    {
        var id = _store.CreateVirtualLibrary("a", "x", null);
        Assert.False(_store.GetVirtualLibrary(id)!.Pinned);

        _store.SetVirtualLibraryPinned(id, true);
        Assert.True(_store.GetVirtualLibrary(id)!.Pinned);

        _store.SetVirtualLibraryPinned(id, false);
        Assert.False(_store.GetVirtualLibrary(id)!.Pinned);
    }

    [Fact]
    public void List_OrdersByPinnedThenName()
    {
        var idC = _store.CreateVirtualLibrary("C 一般", "x", null);
        var idA = _store.CreateVirtualLibrary("A 一般", "x", null);
        var idB = _store.CreateVirtualLibrary("B 釘選", "x", null);
        _store.SetVirtualLibraryPinned(idB, true);

        var list = _store.ListVirtualLibraries();
        Assert.Equal(3, list.Count);
        // 釘選的排第一
        Assert.Equal(idB, list[0].Id);
        Assert.True(list[0].Pinned);
        // 後面依名稱字典序：A 在 C 前
        Assert.Equal(idA, list[1].Id);
        Assert.Equal(idC, list[2].Id);
    }

    [Fact]
    public void List_Empty_ReturnsEmptyList()
    {
        Assert.Empty(_store.ListVirtualLibraries());
    }

    [Fact]
    public void Schema_VersionUpdatedTo7()
    {
        // 用 raw connection 讀 schema_version
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt32(cmd.ExecuteScalar()!);
        Assert.Equal(8, v);
    }
}


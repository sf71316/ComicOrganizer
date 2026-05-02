using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 驗 SqliteAssetMetaStore 的 author 聚合查詢：ResolveAuthorIdByPath + GetAuthorInfo。
/// 用 in-memory 假資料：1 個作者 + 5 個 work（不同 read_status）+ 一些 tag。
/// </summary>
public class AuthorAggregationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public AuthorAggregationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_authoragg_{Guid.NewGuid():N}.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        SeedFixture();
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private long _authorId;
    private string _authorFolderPath = @"C:\Library\Flugel";
    private string[] _workPaths = new[]
    {
        @"C:\Library\Flugel\Work1.zip",
        @"C:\Library\Flugel\Work2.zip",
        @"C:\Library\Flugel\Work3.zip",
        @"C:\Library\Flugel\Work4.zip",
        @"C:\Library\Flugel\Work5.zip",
    };

    private void SeedFixture()
    {
        // 建作者
        _authorId = _store.UpsertAuthor("Flugel", _authorFolderPath);
        _store.AddAuthorAlias(_authorId, "Flugel-en", "en");
        _store.AddAuthorAlias(_authorId, "フルーゲル", "ja");

        // 建 5 個 asset (catalog) + asset_meta (含不同 read_status)
        var statuses = new[] {
            ReadStatus.Unread,
            ReadStatus.Unread,
            ReadStatus.Reading,
            ReadStatus.Completed,
            ReadStatus.Completed,
        };
        var titles = new[] { "Apple", "Banana", "Cherry", "Date", "Elderberry" };
        // last_read：work3 (Reading) 1 天前；work4 (Completed) 2 天前；work5 (Completed) 從沒讀過
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long[] lastReads = { 0, 0, now - 86400, now - 172800, 0 };
        int[] readCounts = { 0, 0, 1, 5, 0 };

        for (int i = 0; i < 5; i++)
        {
            var path = _workPaths[i];
            _store.Upsert(new AssetMeta
            {
                Path = path, Name = $"Work{i + 1}.zip", IsFolder = false, Mtime = 100,
                LastScanned = 100, LastAccessed = 100,
            });
            // 取 asset_id
            var asset = _store.Get(path);
            Assert.NotNull(asset);
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM asset WHERE path = $p;";
            cmd.Parameters.AddWithValue("$p", path);
            var assetId = Convert.ToInt64(cmd.ExecuteScalar()!);

            _store.UpsertMetadata(new AssetMetadata
            {
                AssetId = assetId,
                AuthorId = _authorId,
                Title = titles[i],
                ReadStatus = statuses[i],
                ReadCount = readCounts[i],
                LastRead = lastReads[i] > 0 ? lastReads[i] : null,
                ParsedAt = now,
            });

            // tag：work1 / work2 都標 [中文]，work3 標 [中文][DL版]，work4/5 不標
            if (i < 2)
            {
                var tagId = _store.UpsertTag("中文", "language");
                _store.AssociateTag(assetId, tagId);
            }
            else if (i == 2)
            {
                var t1 = _store.UpsertTag("中文", "language");
                var t2 = _store.UpsertTag("DL版", null);
                _store.AssociateTag(assetId, t1);
                _store.AssociateTag(assetId, t2);
            }
        }
    }

    // ===== ResolveAuthorIdByPath =====

    [Fact]
    public void Resolve_FromWorkPath_ReturnsAuthorId()
    {
        var id = _store.ResolveAuthorIdByPath(_workPaths[0]);
        Assert.Equal(_authorId, id);
    }

    [Fact]
    public void Resolve_FromAuthorFolderPath_ReturnsAuthorId()
    {
        var id = _store.ResolveAuthorIdByPath(_authorFolderPath);
        Assert.Equal(_authorId, id);
    }

    [Fact]
    public void Resolve_FromUnknownPath_ReturnsNull()
    {
        var id = _store.ResolveAuthorIdByPath(@"C:\Library\NotAuthor\NoSuchWork.zip");
        Assert.Null(id);
    }

    // ===== GetAuthorInfo =====

    [Fact]
    public void GetAuthorInfo_BasicData()
    {
        var info = _store.GetAuthorInfo(_authorId);
        Assert.NotNull(info);
        Assert.Equal("Flugel", info!.Author.MainName);
        Assert.Equal(_authorFolderPath, info.Author.FolderPath);
    }

    [Fact]
    public void GetAuthorInfo_Aliases()
    {
        var info = _store.GetAuthorInfo(_authorId)!;
        Assert.Equal(2, info.Aliases.Count);
        Assert.Contains(info.Aliases, a => a.Name == "Flugel-en" && a.Lang == "en");
        Assert.Contains(info.Aliases, a => a.Name == "フルーゲル" && a.Lang == "ja");
    }

    [Fact]
    public void GetAuthorInfo_ReadStats()
    {
        var info = _store.GetAuthorInfo(_authorId)!;
        Assert.Equal(5, info.TotalWorks);
        Assert.Equal(2, info.UnreadCount);
        Assert.Equal(1, info.ReadingCount);
        Assert.Equal(2, info.CompletedCount);
        Assert.Equal(0, info.DroppedCount);
    }

    [Fact]
    public void GetAuthorInfo_TopTags()
    {
        var info = _store.GetAuthorInfo(_authorId)!;
        Assert.Equal(2, info.TopTags.Count);
        // 中文 (3 個 work) 應該在前
        Assert.Equal("中文", info.TopTags[0].Name);
        Assert.Equal(3, info.TopTags[0].Count);
        // DL版 (1 個 work) 後
        Assert.Equal("DL版", info.TopTags[1].Name);
        Assert.Equal(1, info.TopTags[1].Count);
    }

    [Fact]
    public void GetAuthorInfo_AllWorks_OrderedByTitle()
    {
        var info = _store.GetAuthorInfo(_authorId)!;
        Assert.Equal(5, info.AllWorks.Count);
        // 字母排序
        Assert.Equal("Apple", info.AllWorks[0].Title);
        Assert.Equal("Banana", info.AllWorks[1].Title);
        Assert.Equal("Cherry", info.AllWorks[2].Title);
        Assert.Equal("Date", info.AllWorks[3].Title);
        Assert.Equal("Elderberry", info.AllWorks[4].Title);
        // AuthorMainName 都應有值
        Assert.All(info.AllWorks, w => Assert.Equal("Flugel", w.AuthorMainName));
    }

    [Fact]
    public void GetAuthorInfo_RecentlyRead_OrderedByLastReadDesc_WithReadCount()
    {
        var info = _store.GetAuthorInfo(_authorId)!;
        // 只有 work3 (1 天前) + work4 (2 天前) 有 last_read，work5 沒讀過不應出現
        Assert.Equal(2, info.RecentlyRead.Count);
        // 最新在前 → work3 (Cherry) 應 index 0
        Assert.Equal("Cherry", info.RecentlyRead[0].Title);
        Assert.Equal(1, info.RecentlyRead[0].ReadCount);
        Assert.Equal("Date", info.RecentlyRead[1].Title);
        Assert.Equal(5, info.RecentlyRead[1].ReadCount);
    }

    [Fact]
    public void GetAuthorInfo_NonExistentId_ReturnsNull()
    {
        var info = _store.GetAuthorInfo(999999);
        Assert.Null(info);
    }
}

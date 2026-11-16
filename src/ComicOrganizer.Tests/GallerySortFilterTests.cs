using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 2 Core / Models / 純邏輯單元測試：
/// - GallerySortFilterHelper 五欄升/降冪排序（含自然排序、資料夾優先、LastRead null 殿後）
/// - MatchesFilter 狀態與關鍵字（Name / DisplayTitle）過濾
/// - UserPreferences 畫廊預設欄位與 JSON 序列化
/// - SqliteAssetMetaStore.GetGalleryStatsByPaths 批次查詢（含 >999 筆分批、未入庫與各 ReadStatus）
/// </summary>
public class GallerySortFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;

    public GallerySortFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_wi2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "cache.db");
        _store = new SqliteAssetMetaStore(_dbPath);
    }

    public void Dispose()
    {
        _store.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static GalleryItemSortMetadata MakeItem(
        string name,
        bool isFolder = false,
        long size = 100,
        long mtime = 1000,
        ReadStatus status = ReadStatus.Unread,
        int readCount = 0,
        long? lastRead = null,
        string? displayTitle = null)
        => new(
            Path: $@"C:\Lib\{name}",
            Name: name,
            DisplayTitle: displayTitle,
            IsFolder: isFolder,
            SizeBytes: size,
            ModifiedTicks: mtime,
            ReadStatus: status,
            ReadCount: readCount,
            LastRead: lastRead);

    // ===== 1. Name 排序（資料夾永遠優先 + 自然排序） =====

    [Fact]
    public void SortByName_FoldersAlwaysFirst_NaturalOrderBothDirections()
    {
        var items = new[]
        {
            MakeItem("Ch10.zip", isFolder: false),
            MakeItem("Ch2.zip", isFolder: false),
            MakeItem("FolderB", isFolder: true),
            MakeItem("FolderA", isFolder: true),
        };

        var asc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.Name, descending: false);
        Assert.Equal(new[] { "FolderA", "FolderB", "Ch2.zip", "Ch10.zip" }, asc.Select(x => x.Name));

        var desc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.Name, descending: true);
        // 即使降冪，資料夾仍優先於檔案，且各自內部採 Z→A 自然降冪
        Assert.Equal(new[] { "FolderB", "FolderA", "Ch10.zip", "Ch2.zip" }, desc.Select(x => x.Name));
    }

    // ===== 2. ModifiedTime / Size / ReadCount 排序 =====

    [Fact]
    public void SortByModifiedTime_AscAndDesc_WithNameTieBreaker()
    {
        var items = new[]
        {
            MakeItem("B.zip", mtime: 200),
            MakeItem("A.zip", mtime: 200),
            MakeItem("Old.zip", mtime: 100),
            MakeItem("New.zip", mtime: 300),
        };

        var asc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.ModifiedTime, descending: false);
        Assert.Equal(new[] { "Old.zip", "A.zip", "B.zip", "New.zip" }, asc.Select(x => x.Name));

        var desc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.ModifiedTime, descending: true);
        Assert.Equal(new[] { "New.zip", "A.zip", "B.zip", "Old.zip" }, desc.Select(x => x.Name));
    }

    [Fact]
    public void SortBySize_AscAndDesc()
    {
        var items = new[]
        {
            MakeItem("Mid.zip", size: 500),
            MakeItem("Dir", isFolder: true, size: 0),
            MakeItem("Big.zip", size: 2000),
            MakeItem("Small.zip", size: 100),
        };

        var asc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.Size, descending: false);
        Assert.Equal(new[] { "Dir", "Small.zip", "Mid.zip", "Big.zip" }, asc.Select(x => x.Name));

        var desc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.Size, descending: true);
        Assert.Equal(new[] { "Big.zip", "Mid.zip", "Small.zip", "Dir" }, desc.Select(x => x.Name));
    }

    [Fact]
    public void SortByReadCount_AscAndDesc()
    {
        var items = new[]
        {
            MakeItem("Once.zip", readCount: 1),
            MakeItem("Zero.zip", readCount: 0),
            MakeItem("Many.zip", readCount: 9),
        };

        var asc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.ReadCount, descending: false);
        Assert.Equal(new[] { "Zero.zip", "Once.zip", "Many.zip" }, asc.Select(x => x.Name));

        var desc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.ReadCount, descending: true);
        Assert.Equal(new[] { "Many.zip", "Once.zip", "Zero.zip" }, desc.Select(x => x.Name));
    }

    // ===== 3. LastRead 排序（null / 0 永遠殿後） =====

    [Fact]
    public void SortByLastRead_NullAndZeroAlwaysLast_BothAscAndDesc()
    {
        var items = new[]
        {
            MakeItem("UnreadB.zip", lastRead: null),
            MakeItem("ReadOld.zip", lastRead: 1000),
            MakeItem("UnreadA.zip", lastRead: 0),
            MakeItem("ReadNew.zip", lastRead: 3000),
            MakeItem("ReadMid.zip", lastRead: 2000),
        };

        var desc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.LastRead, descending: true);
        Assert.Equal(
            new[] { "ReadNew.zip", "ReadMid.zip", "ReadOld.zip", "UnreadA.zip", "UnreadB.zip" },
            desc.Select(x => x.Name));

        var asc = GallerySortFilterHelper.SortItems(items, x => x, GallerySortField.LastRead, descending: false);
        Assert.Equal(
            new[] { "ReadOld.zip", "ReadMid.zip", "ReadNew.zip", "UnreadA.zip", "UnreadB.zip" },
            asc.Select(x => x.Name));
    }

    // ===== 4. MatchesFilter 狀態 + 關鍵字過濾 =====

    [Fact]
    public void MatchesFilter_StatusAndKeyword_ChecksNameAndDisplayTitle()
    {
        var unread = MakeItem("[Author] Book_01.zip", status: ReadStatus.Unread, displayTitle: "星夜物語");
        var reading = MakeItem("[Author] Book_02.zip", status: ReadStatus.Reading, displayTitle: "夏日海風");
        var completed = MakeItem("[Author] Book_03.zip", status: ReadStatus.Completed, displayTitle: "冬日戀曲");
        var dropped = MakeItem("[Author] Book_04.zip", status: ReadStatus.Dropped, displayTitle: "放棄之書");

        // All 顯示全部（含 Dropped）
        Assert.True(GallerySortFilterHelper.MatchesFilter(unread, GalleryReadFilter.All, null));
        Assert.True(GallerySortFilterHelper.MatchesFilter(dropped, GalleryReadFilter.All, "  "));

        // 精準狀態過濾
        Assert.True(GallerySortFilterHelper.MatchesFilter(unread, GalleryReadFilter.Unread, null));
        Assert.False(GallerySortFilterHelper.MatchesFilter(reading, GalleryReadFilter.Unread, null));
        Assert.False(GallerySortFilterHelper.MatchesFilter(dropped, GalleryReadFilter.Unread, null));

        Assert.True(GallerySortFilterHelper.MatchesFilter(reading, GalleryReadFilter.Reading, null));
        Assert.False(GallerySortFilterHelper.MatchesFilter(completed, GalleryReadFilter.Reading, null));

        Assert.True(GallerySortFilterHelper.MatchesFilter(completed, GalleryReadFilter.Completed, null));
        Assert.False(GallerySortFilterHelper.MatchesFilter(reading, GalleryReadFilter.Completed, null));

        // 關鍵字命中 Name（不分大小寫 + Trim）
        Assert.True(GallerySortFilterHelper.MatchesFilter(reading, GalleryReadFilter.All, "  book_02 "));
        // 關鍵字命中 DisplayTitle
        Assert.True(GallerySortFilterHelper.MatchesFilter(reading, GalleryReadFilter.Reading, "海風"));
        // 狀態符合但關鍵字不符 → false
        Assert.False(GallerySortFilterHelper.MatchesFilter(reading, GalleryReadFilter.Reading, "星夜"));
    }

    // ===== 5. UserPreferences 預設值與 JSON 往返 =====

    [Fact]
    public void UserPreferences_GalleryDefaults_And_JsonRoundTrip()
    {
        var defaults = new UserPreferences();
        Assert.Equal("Medium", defaults.DefaultGalleryViewMode);
        Assert.Equal(GallerySortField.Name, defaults.DefaultGallerySortField);
        Assert.False(defaults.DefaultGallerySortDescending);

        var prefsPath = Path.Combine(_tempDir, "preferences.json");
        var manager = new UserPreferencesManager(prefsPath);

        var custom = new UserPreferences
        {
            DefaultGalleryViewMode = "List",
            DefaultGallerySortField = GallerySortField.LastRead,
            DefaultGallerySortDescending = true,
        };
        manager.Save(custom);

        var loaded = manager.Load();
        Assert.Equal("List", loaded.DefaultGalleryViewMode);
        Assert.Equal(GallerySortField.LastRead, loaded.DefaultGallerySortField);
        Assert.True(loaded.DefaultGallerySortDescending);

        // 確認舊版不含新欄位的 JSON 仍可安全反序列化回預設值
        var legacyJson = JsonSerializer.Serialize(new { Language = "en-US" });
        var fromLegacy = JsonSerializer.Deserialize<UserPreferences>(legacyJson)!;
        Assert.Equal("Medium", fromLegacy.DefaultGalleryViewMode);
        Assert.Equal(GallerySortField.Name, fromLegacy.DefaultGallerySortField);
        Assert.False(fromLegacy.DefaultGallerySortDescending);
    }

    // ===== 6. SqliteAssetMetaStore.GetGalleryStatsByPaths =====

    [Fact]
    public void GetGalleryStatsByPaths_HandlesCatalogOnly_MetadataStatuses_And_MissingPaths()
    {
        var pathNoMeta = @"C:\Lib\NoMeta.zip";
        var pathReading = @"C:\Lib\Reading.zip";
        var pathCompleted = @"C:\Lib\Completed.zip";
        var pathDropped = @"C:\Lib\Dropped.zip";
        var pathUncataloged = @"C:\Lib\NotInDb.zip";

        foreach (var p in new[] { pathNoMeta, pathReading, pathCompleted, pathDropped })
        {
            _store.UpsertCatalogOnly(new AssetMeta
            {
                Path = p,
                Name = Path.GetFileName(p),
                IsFolder = false,
                FileSize = 1000,
                Mtime = 1700000000,
                LastScanned = 1700000000,
            });
        }

        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(pathReading)!.Value,
            Title = "進行中作品",
            ReadStatus = ReadStatus.Reading,
            ReadCount = 2,
            LastRead = 1700001234,
            ParsedAt = 1700000000,
        });
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(pathCompleted)!.Value,
            Title = "已完成作品",
            ReadStatus = ReadStatus.Completed,
            ReadCount = 5,
            LastRead = 1700005678,
            ParsedAt = 1700000000,
        });
        _store.UpsertMetadata(new AssetMetadata
        {
            AssetId = _store.GetAssetId(pathDropped)!.Value,
            Title = "已放棄作品",
            ReadStatus = ReadStatus.Dropped,
            ReadCount = 1,
            LastRead = null,
            ParsedAt = 1700000000,
        });

        Assert.Empty(_store.GetGalleryStatsByPaths(Array.Empty<string>()));

        var stats = _store.GetGalleryStatsByPaths(new[]
        {
            pathNoMeta,
            pathReading,
            pathCompleted,
            pathDropped,
            pathUncataloged,
            pathReading, // 重複路徑應安全處理
        });

        Assert.Equal(4, stats.Count);
        Assert.False(stats.ContainsKey(pathUncataloged));

        // 有 asset 但無 asset_meta → 預設 Unread / 0 / null
        Assert.Null(stats[pathNoMeta].Title);
        Assert.Equal(ReadStatus.Unread, stats[pathNoMeta].ReadStatus);
        Assert.Equal(0, stats[pathNoMeta].ReadCount);
        Assert.Null(stats[pathNoMeta].LastRead);

        // Reading（含 OrdinalIgnoreCase key 查找驗證）
        var readingStats = stats[pathReading.ToLowerInvariant()];
        Assert.Equal("進行中作品", readingStats.Title);
        Assert.Equal(ReadStatus.Reading, readingStats.ReadStatus);
        Assert.Equal(2, readingStats.ReadCount);
        Assert.Equal(1700001234L, readingStats.LastRead);

        // Completed
        Assert.Equal("已完成作品", stats[pathCompleted].Title);
        Assert.Equal(ReadStatus.Completed, stats[pathCompleted].ReadStatus);
        Assert.Equal(5, stats[pathCompleted].ReadCount);
        Assert.Equal(1700005678L, stats[pathCompleted].LastRead);

        // Dropped
        Assert.Equal(ReadStatus.Dropped, stats[pathDropped].ReadStatus);
        Assert.Equal(1, stats[pathDropped].ReadCount);
        Assert.Null(stats[pathDropped].LastRead);
    }

    [Fact]
    public void GetGalleryStatsByPaths_Over1200Paths_BatchesCleanlyWithoutSqliteVarLimitError()
    {
        const int total = 1250;
        var paths = new List<string>(total);

        _store.RunInTransaction(() =>
        {
            for (int i = 0; i < total; i++)
            {
                var p = $@"C:\Lib\Batch\Work_{i:D4}.zip";
                paths.Add(p);
                _store.UpsertCatalogOnly(new AssetMeta
                {
                    Path = p,
                    Name = $"Work_{i:D4}.zip",
                    IsFolder = false,
                    FileSize = 100 + i,
                    Mtime = 1700000000,
                    LastScanned = 1700000000,
                });
                if (i % 100 == 0)
                {
                    var id = _store.GetAssetId(p)!.Value;
                    _store.UpsertMetadata(new AssetMetadata
                    {
                        AssetId = id,
                        Title = $"Title {i}",
                        ReadStatus = ReadStatus.Completed,
                        ReadCount = i + 1,
                        LastRead = 1700000000 + i,
                        ParsedAt = 1700000000,
                    });
                }
            }
        });

        var stats = _store.GetGalleryStatsByPaths(paths);
        Assert.Equal(total, stats.Count);
        Assert.Equal("Title 0", stats[paths[0]].Title);
        Assert.Equal(ReadStatus.Completed, stats[paths[1200]].ReadStatus);
        Assert.Equal(1201, stats[paths[1200]].ReadCount);
        Assert.Equal(ReadStatus.Unread, stats[paths[1]].ReadStatus);
    }
}

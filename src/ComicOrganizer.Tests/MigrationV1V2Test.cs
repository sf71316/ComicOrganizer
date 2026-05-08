using ComicOrganizer.App.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 驗 schema v1 → v2 migration：
///   1. 建假 v1 schema + 寫測試資料
///   2. 用 SqliteAssetMetaStore 開啟（會自動 migrate）
///   3. 透過介面查 → 應該回得到剛才寫的資料（catalog + thumb）
///   4. v2 新表（asset_meta / author / tag）應該存在但空
/// </summary>
public class MigrationV1V2Test : IDisposable
{
    private readonly string _dbPath;

    public MigrationV1V2Test()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"comic_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void V1Database_AfterOpen_MigratedToV2()
    {
        // === Phase 1: 手動建 v1 schema ===
        CreateV1Schema(_dbPath);
        SeedV1Data(_dbPath);

        // === Phase 2: 用 store 開啟 → 觸發 migration ===
        using (var store = new SqliteAssetMetaStore(_dbPath))
        {
            // catalog 應該還在
            var meta1 = store.Get(@"C:\Library\AuthorA\Work1.zip");
            Assert.NotNull(meta1);
            Assert.Equal("Work1.zip", meta1!.Name);
            Assert.False(meta1.IsFolder);
            Assert.Equal(1234567L, meta1.FileSize);
            Assert.Equal(1700000000L, meta1.Mtime);
            // 這筆有 thumb
            Assert.Equal("abc123def456", meta1.CoverThumbKey);
            Assert.Equal(330, meta1.CoverWidth);

            var meta2 = store.Get(@"C:\Library\AuthorA");
            Assert.NotNull(meta2);
            Assert.True(meta2!.IsFolder);
            // folder 沒 thumb，但 catalog 還是要有
            Assert.Null(meta2.CoverThumbKey);
        }

        // === Phase 3: 驗 v2 表結構存在 ===
        AssertV2TablesExist(_dbPath);
        AssertSchemaVersionIs(_dbPath, 7);
    }

    [Fact]
    public void V0Database_AfterOpen_CreatedAsV2()
    {
        // 全新 DB（無檔），應該直接建成 v2
        using (var store = new SqliteAssetMetaStore(_dbPath))
        {
            // 寫一筆驗證 ok
            store.Upsert(new AssetMeta
            {
                Path = @"C:\foo\bar.zip",
                Name = "bar.zip",
                IsFolder = false,
                Mtime = 100,
                LastScanned = 100,
                LastAccessed = 100,
            });

            var got = store.Get(@"C:\foo\bar.zip");
            Assert.NotNull(got);
        }
        AssertV2TablesExist(_dbPath);
        AssertSchemaVersionIs(_dbPath, 7);
    }

    // ===== Helpers =====

    private static void CreateV1Schema(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE schema_version (version INTEGER PRIMARY KEY);
INSERT INTO schema_version (version) VALUES (1);

CREATE TABLE asset (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    is_folder INTEGER NOT NULL,
    file_size INTEGER,
    mtime INTEGER NOT NULL,
    cover_origin TEXT,
    cover_thumb_key TEXT,
    cover_width INTEGER,
    cover_height INTEGER,
    last_scanned INTEGER NOT NULL,
    last_accessed INTEGER NOT NULL
);

CREATE TABLE app_state (
    key TEXT PRIMARY KEY,
    value TEXT
);
";
        cmd.ExecuteNonQuery();
    }

    private static void SeedV1Data(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO asset (path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed)
VALUES
    ('C:\Library\AuthorA\Work1.zip', 'Work1.zip', 0, 1234567, 1700000000, 'inside zip 01.jpg', 'abc123def456', 330, 467, 1700100000, 1700100000),
    ('C:\Library\AuthorA', 'AuthorA', 1, NULL, 1700000000, NULL, NULL, NULL, NULL, 1700100000, 1700100000),
    ('C:\Library\AuthorB\Work2.zip', 'Work2.zip', 0, 2345678, 1700000001, NULL, 'xyz789', 330, 480, 1700100001, 1700100001);
";
        cmd.ExecuteNonQuery();
    }

    private static void AssertV2TablesExist(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        var expected = new[] { "asset", "asset_thumb", "asset_meta", "author", "author_alias", "tag", "tag_translation", "asset_tag", "author_tag", "asset_external_source", "virtual_library", "schema_version", "app_state", "folder_role" };
        foreach (var name in expected)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$n;";
            cmd.Parameters.AddWithValue("$n", name);
            var result = cmd.ExecuteScalar();
            Assert.NotNull(result);
        }
        // 舊表應該被 drop
        using var oldCheck = conn.CreateCommand();
        oldCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='asset_v1';";
        Assert.Null(oldCheck.ExecuteScalar());
    }

    private static void AssertSchemaVersionIs(string dbPath, int expectedVersion)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var actual = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(expectedVersion, actual);
    }
}

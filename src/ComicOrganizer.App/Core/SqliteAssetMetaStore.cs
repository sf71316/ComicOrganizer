using System.Data;
using Microsoft.Data.Sqlite;

namespace ComicOrganizer.App.Core;

/// <summary>
/// SQLite-backed metadata store。
/// 寫操作集中在 CacheWriter 單一 worker thread → 不需 lock；讀操作走獨立 connection 安全。
/// 為簡化實作這裡用單一 connection（多 thread 透過 SQLite 內建 mutex 序列化）。
/// </summary>
public class SqliteAssetMetaStore : IAssetMetaStore
{
    private readonly SqliteConnection _conn;
    private const int CurrentSchemaVersion = 1;

    public SqliteAssetMetaStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath};");
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY);

CREATE TABLE IF NOT EXISTS asset (
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

CREATE INDEX IF NOT EXISTS idx_asset_path ON asset(path);
CREATE INDEX IF NOT EXISTS idx_asset_last_accessed ON asset(last_accessed);

CREATE TABLE IF NOT EXISTS folder_role (
    path TEXT PRIMARY KEY,
    role TEXT NOT NULL,
    confidence REAL,
    template TEXT,
    learned_at INTEGER
);
";
            cmd.ExecuteNonQuery();
        }

        // version 紀錄
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
            var existing = cmd.ExecuteScalar();
            if (existing == null)
            {
                using var insert = _conn.CreateCommand();
                insert.CommandText = "INSERT INTO schema_version (version) VALUES ($v);";
                insert.Parameters.AddWithValue("$v", CurrentSchemaVersion);
                insert.ExecuteNonQuery();
            }
            // 未來 migration 邏輯：if (long)existing < CurrentSchemaVersion { ... }
        }
    }

    public AssetMeta? Get(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key,
                                   cover_width, cover_height, last_scanned, last_accessed
                            FROM asset WHERE path = $p LIMIT 1;";
        cmd.Parameters.AddWithValue("$p", path);
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return null;
        return ReadMeta(rdr);
    }

    public void Upsert(AssetMeta meta)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO asset (path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key,
                   cover_width, cover_height, last_scanned, last_accessed)
VALUES ($path, $name, $isFolder, $fileSize, $mtime, $coverOrigin, $coverThumbKey,
        $coverWidth, $coverHeight, $lastScanned, $lastAccessed)
ON CONFLICT(path) DO UPDATE SET
    name = excluded.name,
    is_folder = excluded.is_folder,
    file_size = excluded.file_size,
    mtime = excluded.mtime,
    cover_origin = excluded.cover_origin,
    cover_thumb_key = excluded.cover_thumb_key,
    cover_width = excluded.cover_width,
    cover_height = excluded.cover_height,
    last_scanned = excluded.last_scanned,
    last_accessed = excluded.last_accessed;";
        cmd.Parameters.AddWithValue("$path", meta.Path);
        cmd.Parameters.AddWithValue("$name", meta.Name);
        cmd.Parameters.AddWithValue("$isFolder", meta.IsFolder ? 1 : 0);
        cmd.Parameters.AddWithValue("$fileSize", (object?)meta.FileSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mtime", meta.Mtime);
        cmd.Parameters.AddWithValue("$coverOrigin", (object?)meta.CoverOrigin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$coverThumbKey", (object?)meta.CoverThumbKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$coverWidth", (object?)meta.CoverWidth ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$coverHeight", (object?)meta.CoverHeight ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lastScanned", meta.LastScanned);
        cmd.Parameters.AddWithValue("$lastAccessed", meta.LastAccessed);
        cmd.ExecuteNonQuery();
    }

    public void TouchLastAccessed(string path, long now)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE asset SET last_accessed = $now WHERE path = $p;";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<AssetMeta> ListChildren(string folderPath)
    {
        // 簡單前綴匹配：path LIKE '<folder>%' AND path <> folder。可改更嚴謹但對 cache 用途足夠。
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed FROM asset WHERE path LIKE $prefix;";
        cmd.Parameters.AddWithValue("$prefix", prefix + "%");
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) yield return ReadMeta(rdr);
    }

    public IEnumerable<AssetMeta> ListLruVictims(int count)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed FROM asset WHERE cover_thumb_key IS NOT NULL ORDER BY last_accessed ASC LIMIT $n;";
        cmd.Parameters.AddWithValue("$n", count);
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) yield return ReadMeta(rdr);
    }

    public void Delete(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM asset WHERE path = $p;";
        cmd.Parameters.AddWithValue("$p", path);
        cmd.ExecuteNonQuery();
    }

    public void DeleteSubtree(string folderPath)
    {
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM asset WHERE path = $exact OR path LIKE $prefix;";
        cmd.Parameters.AddWithValue("$exact", folderPath);
        cmd.Parameters.AddWithValue("$prefix", prefix + "%");
        cmd.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM asset;";
        cmd.ExecuteNonQuery();
    }

    public long TotalAssetCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM asset;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public long TotalThumbBytes()
    {
        // file_size 是原檔大小，不是 thumb 大小；thumb 真實大小由 IThumbStore.TotalSizeBytes() 算 fs
        // 這個方法保留欄位但回傳 0，由 ThumbStore 提供真實值
        return 0;
    }

    public IDbTransaction BeginTransaction()
    {
        return _conn.BeginTransaction();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }

    // ===== Helpers =====

    private static AssetMeta ReadMeta(SqliteDataReader r)
    {
        return new AssetMeta
        {
            Path = r.GetString(0),
            Name = r.GetString(1),
            IsFolder = r.GetInt64(2) != 0,
            FileSize = r.IsDBNull(3) ? null : r.GetInt64(3),
            Mtime = r.GetInt64(4),
            CoverOrigin = r.IsDBNull(5) ? null : r.GetString(5),
            CoverThumbKey = r.IsDBNull(6) ? null : r.GetString(6),
            CoverWidth = r.IsDBNull(7) ? null : r.GetInt32(7),
            CoverHeight = r.IsDBNull(8) ? null : r.GetInt32(8),
            LastScanned = r.GetInt64(9),
            LastAccessed = r.GetInt64(10),
        };
    }
}

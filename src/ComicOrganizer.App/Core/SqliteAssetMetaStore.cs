using Microsoft.Data.Sqlite;

namespace ComicOrganizer.App.Core;

/// <summary>
/// SQLite-backed metadata store。
/// SqliteConnection 不是 thread-safe，所有方法都用 _lock 序列化。lock 是 Monitor 為基礎，
/// 同 thread 可遞迴 → RunInTransaction 內呼叫的 Upsert/Touch 可重入無 deadlock。
/// </summary>
public class SqliteAssetMetaStore : IAssetMetaStore
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
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

-- 通用 key/value：rebuild 進行中旗標、階段名稱等跨 session 持久化
CREATE TABLE IF NOT EXISTS app_state (
    key TEXT PRIMARY KEY,
    value TEXT
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
        lock (_lock)
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
    }

    public void Upsert(AssetMeta meta)
    {
        lock (_lock)
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
    }

    public void TouchLastAccessed(string path, long now)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE asset SET last_accessed = $now WHERE path = $p;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<AssetMeta> ListChildren(string folderPath)
    {
        // 簡單前綴匹配：path LIKE '<folder>%' AND path <> folder。可改更嚴謹但對 cache 用途足夠。
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (_lock)
        {
            var list = new List<AssetMeta>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed FROM asset WHERE path LIKE $prefix;";
            cmd.Parameters.AddWithValue("$prefix", prefix + "%");
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) list.Add(ReadMeta(rdr));
            return list;
        }
    }

    public IReadOnlyList<AssetMeta> ListLruVictims(int count)
    {
        lock (_lock)
        {
            var list = new List<AssetMeta>(count);
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT path, name, is_folder, file_size, mtime, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed FROM asset WHERE cover_thumb_key IS NOT NULL ORDER BY last_accessed ASC LIMIT $n;";
            cmd.Parameters.AddWithValue("$n", count);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) list.Add(ReadMeta(rdr));
            return list;
        }
    }

    public void Delete(string path)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM asset WHERE path = $p;";
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteSubtree(string folderPath)
    {
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM asset WHERE path = $exact OR path LIKE $prefix;";
            cmd.Parameters.AddWithValue("$exact", folderPath);
            cmd.Parameters.AddWithValue("$prefix", prefix + "%");
            cmd.ExecuteNonQuery();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM asset;";
            cmd.ExecuteNonQuery();
        }
    }

    public long TotalAssetCount()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM asset;";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    public long TotalThumbBytes()
    {
        // file_size 是原檔大小，不是 thumb 大小；thumb 真實大小由 IThumbStore.TotalSizeBytes() 算 fs
        return 0;
    }

    public string? GetState(string key)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM app_state WHERE key = $k LIMIT 1;";
            cmd.Parameters.AddWithValue("$k", key);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }
    }

    public void SetState(string key, string? value)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            if (value == null)
            {
                cmd.CommandText = "DELETE FROM app_state WHERE key = $k;";
                cmd.Parameters.AddWithValue("$k", key);
            }
            else
            {
                cmd.CommandText = "INSERT INTO app_state (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                cmd.Parameters.AddWithValue("$k", key);
                cmd.Parameters.AddWithValue("$v", value);
            }
            cmd.ExecuteNonQuery();
        }
    }

    public void RunInTransaction(Action action)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            try
            {
                action();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _conn.Dispose();
        }
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

using Microsoft.Data.Sqlite;

namespace ComicOrganizer.App.Core;

/// <summary>
/// SQLite-backed metadata store。schema v2：catalog / thumb / metadata 拆三表 + author / tag。
/// SqliteConnection 不是 thread-safe，所有方法都用 _lock 序列化。
/// </summary>
public class SqliteAssetMetaStore : IAssetMetaStore
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private const int CurrentSchemaVersion = 2;

    public SqliteAssetMetaStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath};");
        _conn.Open();

        // 開啟外鍵約束（SQLite 預設關閉）
        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        // 第一次啟動：schema_version 表還沒建
        long currentVersion = 0;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY);";
            cmd.ExecuteNonQuery();
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
            var existing = cmd.ExecuteScalar();
            if (existing != null && existing != DBNull.Value)
                currentVersion = Convert.ToInt64(existing);
        }

        // v0：什麼都沒有 → 直接建 v2
        // v1：舊 schema → migrate 到 v2
        // v2：什麼都不做
        if (currentVersion == 0)
        {
            CreateSchemaV2();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO schema_version (version) VALUES ($v);";
            cmd.Parameters.AddWithValue("$v", CurrentSchemaVersion);
            cmd.ExecuteNonQuery();
        }
        else if (currentVersion == 1)
        {
            MigrateV1ToV2();
        }
        // currentVersion >= 2 → 已是最新，no-op
    }

    /// <summary>
    /// 直接建立 v2 schema（首次啟動或測試用）。
    /// </summary>
    private void CreateSchemaV2()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
-- catalog：目錄結構，永久保留
CREATE TABLE IF NOT EXISTS asset (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    path TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    is_folder INTEGER NOT NULL,
    file_size INTEGER,
    mtime INTEGER NOT NULL,
    added_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_asset_path ON asset(path);

-- 縮圖 cache：可清/可 LRU 淘汰
CREATE TABLE IF NOT EXISTS asset_thumb (
    asset_id INTEGER PRIMARY KEY,
    cover_origin TEXT,
    cover_thumb_key TEXT,
    cover_width INTEGER,
    cover_height INTEGER,
    last_scanned INTEGER NOT NULL,
    last_accessed INTEGER NOT NULL,
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_asset_thumb_last_accessed ON asset_thumb(last_accessed);

-- 漫畫 metadata：可獨立清/重 parse
CREATE TABLE IF NOT EXISTS asset_meta (
    asset_id INTEGER PRIMARY KEY,
    author_id INTEGER,
    title TEXT,
    series TEXT,
    chapter_no TEXT,
    language TEXT,
    translation_group TEXT,
    page_count INTEGER,
    rating INTEGER,
    read_status INTEGER NOT NULL DEFAULT 0,
    read_count INTEGER NOT NULL DEFAULT 0,
    last_read INTEGER,
    external_id TEXT,
    parsed_at INTEGER NOT NULL,
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE,
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_asset_meta_author_id ON asset_meta(author_id);

-- 作者
CREATE TABLE IF NOT EXISTS author (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    main_name TEXT NOT NULL UNIQUE,
    folder_path TEXT,
    learned_at INTEGER NOT NULL
);

-- 作者別名
CREATE TABLE IF NOT EXISTS author_alias (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    author_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    lang TEXT,
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE CASCADE,
    UNIQUE(author_id, name)
);
CREATE INDEX IF NOT EXISTS idx_author_alias_name ON author_alias(name);

-- 標籤
CREATE TABLE IF NOT EXISTS tag (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    category TEXT
);

-- 標籤多對多
CREATE TABLE IF NOT EXISTS asset_tag (
    asset_id INTEGER NOT NULL,
    tag_id INTEGER NOT NULL,
    PRIMARY KEY (asset_id, tag_id),
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_asset_tag_tag_id ON asset_tag(tag_id);

-- 預留：folder_role（#46 用）+ app_state（rebuild 旗標 / 跨 session）
CREATE TABLE IF NOT EXISTS folder_role (
    path TEXT PRIMARY KEY,
    role TEXT NOT NULL,
    confidence REAL,
    template TEXT,
    learned_at INTEGER
);

CREATE TABLE IF NOT EXISTS app_state (
    key TEXT PRIMARY KEY,
    value TEXT
);
";
        cmd.ExecuteNonQuery();
    }

    /// <summary>v1 → v2 migration：把舊 asset 表分流到 asset + asset_thumb，metadata 留空等 parser 填。</summary>
    private void MigrateV1ToV2()
    {
        System.Diagnostics.Debug.WriteLine("[SqliteStore] schema migration: v1 → v2 starting");
        using var tx = _conn.BeginTransaction();
        try
        {
            // 1. rename 舊表避開衝突
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "ALTER TABLE asset RENAME TO asset_v1;";
                cmd.ExecuteNonQuery();
            }

            // 2. 建 v2 schema
            CreateSchemaV2();

            long now = NowUnix();

            // 3. 搬 catalog 部分（保留 id）
            long catalogRows;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO asset (id, path, name, is_folder, file_size, mtime, added_at)
SELECT id, path, name, is_folder, file_size, mtime, COALESCE(last_scanned, $now)
FROM asset_v1;";
                cmd.Parameters.AddWithValue("$now", now);
                catalogRows = cmd.ExecuteNonQuery();
            }

            // 4. 搬 thumb 部分（只搬有 cover_thumb_key 的 row）
            long thumbRows;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO asset_thumb (asset_id, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed)
SELECT id, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed
FROM asset_v1
WHERE cover_thumb_key IS NOT NULL;";
                thumbRows = cmd.ExecuteNonQuery();
            }

            // 5. drop 舊表
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE asset_v1;";
                cmd.ExecuteNonQuery();
            }

            // 6. bump version
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES ($v);";
                cmd.Parameters.AddWithValue("$v", CurrentSchemaVersion);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            System.Diagnostics.Debug.WriteLine($"[SqliteStore] migration v1→v2 done: {catalogRows} catalog rows, {thumbRows} thumb rows");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            System.Diagnostics.Debug.WriteLine($"[SqliteStore] migration v1→v2 FAILED, rolled back: {ex.Message}");
            throw;
        }
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ============================================================
    // Catalog + Thumb (joined view) — AssetMeta DTO
    // ============================================================

    public AssetMeta? Get(string path)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       COALESCE(t.last_scanned, a.added_at) AS last_scanned,
       COALESCE(t.last_accessed, a.added_at) AS last_accessed
FROM asset a
LEFT JOIN asset_thumb t ON t.asset_id = a.id
WHERE a.path = $p
LIMIT 1;";
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
            // 1. catalog upsert（取 id）
            long assetId = UpsertCatalogInternal(meta);

            // 2. 有 thumb 資料才寫 asset_thumb；否則確保不存在
            if (meta.CoverThumbKey != null)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO asset_thumb (asset_id, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed)
VALUES ($id, $origin, $key, $w, $h, $scanned, $accessed)
ON CONFLICT(asset_id) DO UPDATE SET
    cover_origin = excluded.cover_origin,
    cover_thumb_key = excluded.cover_thumb_key,
    cover_width = excluded.cover_width,
    cover_height = excluded.cover_height,
    last_scanned = excluded.last_scanned,
    last_accessed = excluded.last_accessed;";
                cmd.Parameters.AddWithValue("$id", assetId);
                cmd.Parameters.AddWithValue("$origin", (object?)meta.CoverOrigin ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$key", meta.CoverThumbKey);
                cmd.Parameters.AddWithValue("$w", (object?)meta.CoverWidth ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$h", (object?)meta.CoverHeight ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$scanned", meta.LastScanned);
                cmd.Parameters.AddWithValue("$accessed", meta.LastAccessed);
                cmd.ExecuteNonQuery();
            }
            else
            {
                // CoverThumbKey 為 null：刪 thumb row（LRU 淘汰、整性 fix 路徑）
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = "DELETE FROM asset_thumb WHERE asset_id = $id;";
                cmd.Parameters.AddWithValue("$id", assetId);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private long UpsertCatalogInternal(AssetMeta meta)
    {
        // 試圖 INSERT；衝突時 UPDATE。RETURNING id 拿回主鍵。
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO asset (path, name, is_folder, file_size, mtime, added_at)
VALUES ($path, $name, $isFolder, $fileSize, $mtime, $addedAt)
ON CONFLICT(path) DO UPDATE SET
    name = excluded.name,
    is_folder = excluded.is_folder,
    file_size = excluded.file_size,
    mtime = excluded.mtime
RETURNING id;";
        cmd.Parameters.AddWithValue("$path", meta.Path);
        cmd.Parameters.AddWithValue("$name", meta.Name);
        cmd.Parameters.AddWithValue("$isFolder", meta.IsFolder ? 1 : 0);
        cmd.Parameters.AddWithValue("$fileSize", (object?)meta.FileSize ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mtime", meta.Mtime);
        cmd.Parameters.AddWithValue("$addedAt", meta.LastScanned > 0 ? meta.LastScanned : NowUnix());
        return Convert.ToInt64(cmd.ExecuteScalar()!);
    }

    public void TouchLastAccessed(string path, long now)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
UPDATE asset_thumb SET last_accessed = $now
WHERE asset_id = (SELECT id FROM asset WHERE path = $p);";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$p", path);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<AssetMeta> ListChildren(string folderPath)
    {
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (_lock)
        {
            var list = new List<AssetMeta>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       COALESCE(t.last_scanned, a.added_at), COALESCE(t.last_accessed, a.added_at)
FROM asset a
LEFT JOIN asset_thumb t ON t.asset_id = a.id
WHERE a.path LIKE $prefix;";
            cmd.Parameters.AddWithValue("$prefix", prefix + "%");
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) list.Add(ReadMeta(rdr));
            return list;
        }
    }

    public IEnumerable<AssetMeta> ListAll()
    {
        lock (_lock)
        {
            var list = new List<AssetMeta>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       COALESCE(t.last_scanned, a.added_at), COALESCE(t.last_accessed, a.added_at)
FROM asset a
LEFT JOIN asset_thumb t ON t.asset_id = a.id;";
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
            cmd.CommandText = @"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       t.last_scanned, t.last_accessed
FROM asset_thumb t
JOIN asset a ON a.id = t.asset_id
WHERE t.cover_thumb_key IS NOT NULL
ORDER BY t.last_accessed ASC
LIMIT $n;";
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
            cmd.CommandText = "DELETE FROM asset WHERE path = $p;";   // ON DELETE CASCADE 連帶 thumb/meta/tag
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
            cmd.CommandText = "DELETE FROM asset;";   // CASCADE 連帶 thumb/meta/tag
            cmd.ExecuteNonQuery();
        }
    }

    public void ClearThumbFields()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM asset_thumb;";
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

    public long TotalCachedCount()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM asset_thumb WHERE cover_thumb_key IS NOT NULL;";
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

    // ============================================================
    // Metadata (asset_meta)
    // ============================================================

    public AssetMetadata? GetMetadata(long assetId)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT asset_id, author_id, title, series, chapter_no, language, translation_group,
       page_count, rating, read_status, read_count, last_read, external_id, parsed_at
FROM asset_meta WHERE asset_id = $id LIMIT 1;";
            cmd.Parameters.AddWithValue("$id", assetId);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;
            return ReadMetadata(rdr);
        }
    }

    public void UpsertMetadata(AssetMetadata meta)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO asset_meta (asset_id, author_id, title, series, chapter_no, language, translation_group,
                        page_count, rating, read_status, read_count, last_read, external_id, parsed_at)
VALUES ($id, $author, $title, $series, $chapter, $lang, $tgroup, $pages, $rating,
        $rstatus, $rcount, $lread, $extId, $parsedAt)
ON CONFLICT(asset_id) DO UPDATE SET
    author_id = excluded.author_id,
    title = excluded.title,
    series = excluded.series,
    chapter_no = excluded.chapter_no,
    language = excluded.language,
    translation_group = excluded.translation_group,
    page_count = excluded.page_count,
    rating = excluded.rating,
    read_status = excluded.read_status,
    read_count = excluded.read_count,
    last_read = excluded.last_read,
    external_id = excluded.external_id,
    parsed_at = excluded.parsed_at;";
            cmd.Parameters.AddWithValue("$id", meta.AssetId);
            cmd.Parameters.AddWithValue("$author", (object?)meta.AuthorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$title", (object?)meta.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$series", (object?)meta.Series ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$chapter", (object?)meta.ChapterNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lang", (object?)meta.Language ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tgroup", (object?)meta.TranslationGroup ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pages", (object?)meta.PageCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rating", (object?)meta.Rating ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rstatus", (int)meta.ReadStatus);
            cmd.Parameters.AddWithValue("$rcount", meta.ReadCount);
            cmd.Parameters.AddWithValue("$lread", (object?)meta.LastRead ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$extId", (object?)meta.ExternalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parsedAt", meta.ParsedAt > 0 ? meta.ParsedAt : NowUnix());
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteMetadata(long assetId)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM asset_meta WHERE asset_id = $id;";
            cmd.Parameters.AddWithValue("$id", assetId);
            cmd.ExecuteNonQuery();
        }
    }

    public void ClearAllMetadata()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM asset_meta;";
            cmd.ExecuteNonQuery();
        }
    }

    // ============================================================
    // Author
    // ============================================================

    public Author? FindAuthorByMainName(string mainName)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, main_name, folder_path, learned_at FROM author WHERE main_name = $n LIMIT 1;";
            cmd.Parameters.AddWithValue("$n", mainName);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read()) return null;
            return new Author
            {
                Id = rdr.GetInt64(0),
                MainName = rdr.GetString(1),
                FolderPath = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                LearnedAt = rdr.GetInt64(3),
            };
        }
    }

    public long UpsertAuthor(string mainName, string? folderPath = null)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO author (main_name, folder_path, learned_at)
VALUES ($n, $fp, $now)
ON CONFLICT(main_name) DO UPDATE SET
    folder_path = COALESCE(excluded.folder_path, author.folder_path)
RETURNING id;";
            cmd.Parameters.AddWithValue("$n", mainName);
            cmd.Parameters.AddWithValue("$fp", (object?)folderPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            return Convert.ToInt64(cmd.ExecuteScalar()!);
        }
    }

    public void AddAuthorAlias(long authorId, string aliasName, string? lang = null)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO author_alias (author_id, name, lang) VALUES ($a, $n, $l);";
            cmd.Parameters.AddWithValue("$a", authorId);
            cmd.Parameters.AddWithValue("$n", aliasName);
            cmd.Parameters.AddWithValue("$l", (object?)lang ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<AuthorAlias> ListAuthorAliases(long authorId)
    {
        lock (_lock)
        {
            var list = new List<AuthorAlias>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT id, author_id, name, lang FROM author_alias WHERE author_id = $a;";
            cmd.Parameters.AddWithValue("$a", authorId);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                list.Add(new AuthorAlias
                {
                    Id = rdr.GetInt64(0),
                    AuthorId = rdr.GetInt64(1),
                    Name = rdr.GetString(2),
                    Lang = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                });
            }
            return list;
        }
    }

    // ============================================================
    // Tag
    // ============================================================

    public long UpsertTag(string name, string? category = null)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO tag (name, category) VALUES ($n, $c)
ON CONFLICT(name) DO UPDATE SET
    category = COALESCE(excluded.category, tag.category)
RETURNING id;";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$c", (object?)category ?? DBNull.Value);
            return Convert.ToInt64(cmd.ExecuteScalar()!);
        }
    }

    public void AssociateTag(long assetId, long tagId)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO asset_tag (asset_id, tag_id) VALUES ($a, $t);";
            cmd.Parameters.AddWithValue("$a", assetId);
            cmd.Parameters.AddWithValue("$t", tagId);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Tag> ListTagsForAsset(long assetId)
    {
        lock (_lock)
        {
            var list = new List<Tag>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
SELECT t.id, t.name, t.category
FROM tag t
JOIN asset_tag at ON at.tag_id = t.id
WHERE at.asset_id = $a;";
            cmd.Parameters.AddWithValue("$a", assetId);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                list.Add(new Tag
                {
                    Id = rdr.GetInt64(0),
                    Name = rdr.GetString(1),
                    Category = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                });
            }
            return list;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _conn.Dispose();
        }
    }

    // ============================================================
    // Helpers
    // ============================================================

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

    private static AssetMetadata ReadMetadata(SqliteDataReader r)
    {
        return new AssetMetadata
        {
            AssetId = r.GetInt64(0),
            AuthorId = r.IsDBNull(1) ? null : r.GetInt64(1),
            Title = r.IsDBNull(2) ? null : r.GetString(2),
            Series = r.IsDBNull(3) ? null : r.GetString(3),
            ChapterNo = r.IsDBNull(4) ? null : r.GetString(4),
            Language = r.IsDBNull(5) ? null : r.GetString(5),
            TranslationGroup = r.IsDBNull(6) ? null : r.GetString(6),
            PageCount = r.IsDBNull(7) ? null : r.GetInt32(7),
            Rating = r.IsDBNull(8) ? null : r.GetInt32(8),
            ReadStatus = (ReadStatus)r.GetInt32(9),
            ReadCount = r.GetInt32(10),
            LastRead = r.IsDBNull(11) ? null : r.GetInt64(11),
            ExternalId = r.IsDBNull(12) ? null : r.GetString(12),
            ParsedAt = r.GetInt64(13),
        };
    }
}

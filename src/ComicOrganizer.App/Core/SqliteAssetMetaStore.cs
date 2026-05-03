using Dapper;
using Microsoft.Data.Sqlite;

namespace ComicOrganizer.App.Core;

/// <summary>
/// SQLite-backed metadata store。schema v2：catalog / thumb / metadata 拆三表 + author / tag。
/// SqliteConnection 不是 thread-safe，所有方法都用 _lock 序列化。
/// 查詢層用 Dapper：snake_case ↔ PascalCase 自動對映、NULL → null 自動處理。
/// </summary>
public class SqliteAssetMetaStore : IAssetMetaStore
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private SqliteTransaction? _currentTx;
    private const int CurrentSchemaVersion = 3;

    static SqliteAssetMetaStore()
    {
        // 全域開啟「snake_case 欄位 → PascalCase 屬性」自動對映
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public SqliteAssetMetaStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath};");
        _conn.Open();

        // 開啟外鍵約束（SQLite 預設關閉）
        _conn.Execute("PRAGMA foreign_keys = ON;");

        EnsureSchema();
    }

    private void EnsureSchema()
    {
        _conn.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY);");
        var currentVersion = _conn.ExecuteScalar<long?>("SELECT version FROM schema_version LIMIT 1;") ?? 0;

        // v0：什麼都沒有 → 建最新 schema
        // v1：舊 schema → migrate 到 v2 → 接 v3
        // v2：補 v3 columns
        // v3：no-op
        if (currentVersion == 0)
        {
            CreateSchemaV2();
            ApplyV3Additions();
            _conn.Execute("INSERT INTO schema_version (version) VALUES (@v);", new { v = CurrentSchemaVersion });
            return;
        }
        if (currentVersion == 1)
        {
            MigrateV1ToV2();
            currentVersion = 2;
        }
        if (currentVersion == 2)
        {
            ApplyV3Additions();
            _conn.Execute("DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (@v);",
                new { v = CurrentSchemaVersion });
        }
    }

    /// <summary>v2 → v3：asset_meta 加 last_page 欄位（Phase 1.3 reader 進度保存用）。</summary>
    private void ApplyV3Additions()
    {
        _conn.Execute("ALTER TABLE asset_meta ADD COLUMN last_page INTEGER;");
    }

    /// <summary>直接建立 v2 schema（首次啟動或測試用）。</summary>
    private void CreateSchemaV2()
    {
        _conn.Execute(@"
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
");
    }

    /// <summary>v1 → v2 migration：把舊 asset 表分流到 asset + asset_thumb，metadata 留空等 parser 填。</summary>
    private void MigrateV1ToV2()
    {
        System.Diagnostics.Debug.WriteLine("[SqliteStore] schema migration: v1 → v2 starting");
        using var tx = _conn.BeginTransaction();
        try
        {
            _conn.Execute("ALTER TABLE asset RENAME TO asset_v1;", transaction: tx);

            // CreateSchemaV2 不能傳 tx（內部單獨 Execute），但 DDL 在 SQLite 是 auto-commit-friendly
            // 改用 inline DDL 並帶 tx：
            _conn.Execute(@"
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
CREATE TABLE IF NOT EXISTS asset_meta (
    asset_id INTEGER PRIMARY KEY,
    author_id INTEGER,
    title TEXT, series TEXT, chapter_no TEXT, language TEXT, translation_group TEXT,
    page_count INTEGER, rating INTEGER,
    read_status INTEGER NOT NULL DEFAULT 0,
    read_count INTEGER NOT NULL DEFAULT 0,
    last_read INTEGER, external_id TEXT, parsed_at INTEGER NOT NULL,
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE,
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE SET NULL
);
CREATE INDEX IF NOT EXISTS idx_asset_meta_author_id ON asset_meta(author_id);
CREATE TABLE IF NOT EXISTS author (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    main_name TEXT NOT NULL UNIQUE, folder_path TEXT, learned_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS author_alias (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    author_id INTEGER NOT NULL, name TEXT NOT NULL, lang TEXT,
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE CASCADE,
    UNIQUE(author_id, name)
);
CREATE INDEX IF NOT EXISTS idx_author_alias_name ON author_alias(name);
CREATE TABLE IF NOT EXISTS tag (
    id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE, category TEXT
);
CREATE TABLE IF NOT EXISTS asset_tag (
    asset_id INTEGER NOT NULL, tag_id INTEGER NOT NULL,
    PRIMARY KEY (asset_id, tag_id),
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_asset_tag_tag_id ON asset_tag(tag_id);
CREATE TABLE IF NOT EXISTS folder_role (
    path TEXT PRIMARY KEY, role TEXT NOT NULL,
    confidence REAL, template TEXT, learned_at INTEGER
);
CREATE TABLE IF NOT EXISTS app_state (key TEXT PRIMARY KEY, value TEXT);
", transaction: tx);

            long now = NowUnix();

            var catalogRows = _conn.Execute(@"
INSERT INTO asset (id, path, name, is_folder, file_size, mtime, added_at)
SELECT id, path, name, is_folder, file_size, mtime, COALESCE(last_scanned, @now)
FROM asset_v1;", new { now }, tx);

            var thumbRows = _conn.Execute(@"
INSERT INTO asset_thumb (asset_id, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed)
SELECT id, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed
FROM asset_v1
WHERE cover_thumb_key IS NOT NULL;", transaction: tx);

            _conn.Execute("DROP TABLE asset_v1;", transaction: tx);
            _conn.Execute("DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (@v);",
                new { v = CurrentSchemaVersion }, tx);

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

    public long? GetAssetId(string path)
    {
        lock (_lock)
            return _conn.ExecuteScalar<long?>(
                "SELECT id FROM asset WHERE path = @path LIMIT 1;",
                new { path }, _currentTx);
    }

    public AssetMeta? Get(string path)
    {
        lock (_lock)
            return _conn.QueryFirstOrDefault<AssetMeta>(@"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       COALESCE(t.last_scanned, a.added_at) AS last_scanned,
       COALESCE(t.last_accessed, a.added_at) AS last_accessed
FROM asset a
LEFT JOIN asset_thumb t ON t.asset_id = a.id
WHERE a.path = @path
LIMIT 1;", new { path }, _currentTx);
    }

    public void Upsert(AssetMeta meta)
    {
        lock (_lock)
        {
            long assetId = UpsertCatalogInternal(meta);

            if (meta.CoverThumbKey != null)
            {
                _conn.Execute(@"
INSERT INTO asset_thumb (asset_id, cover_origin, cover_thumb_key, cover_width, cover_height, last_scanned, last_accessed)
VALUES (@id, @origin, @key, @w, @h, @scanned, @accessed)
ON CONFLICT(asset_id) DO UPDATE SET
    cover_origin = excluded.cover_origin,
    cover_thumb_key = excluded.cover_thumb_key,
    cover_width = excluded.cover_width,
    cover_height = excluded.cover_height,
    last_scanned = excluded.last_scanned,
    last_accessed = excluded.last_accessed;",
                    new
                    {
                        id = assetId,
                        origin = meta.CoverOrigin,
                        key = meta.CoverThumbKey,
                        w = meta.CoverWidth,
                        h = meta.CoverHeight,
                        scanned = meta.LastScanned,
                        accessed = meta.LastAccessed,
                    }, _currentTx);
            }
            else
            {
                // CoverThumbKey 為 null：刪 thumb row（LRU 淘汰、整性 fix 路徑）
                _conn.Execute("DELETE FROM asset_thumb WHERE asset_id = @id;",
                    new { id = assetId }, _currentTx);
            }
        }
    }

    public void UpsertCatalogOnly(AssetMeta meta)
    {
        lock (_lock) UpsertCatalogInternal(meta);
    }

    private long UpsertCatalogInternal(AssetMeta meta)
    {
        return _conn.ExecuteScalar<long>(@"
INSERT INTO asset (path, name, is_folder, file_size, mtime, added_at)
VALUES (@path, @name, @isFolder, @fileSize, @mtime, @addedAt)
ON CONFLICT(path) DO UPDATE SET
    name = excluded.name,
    is_folder = excluded.is_folder,
    file_size = excluded.file_size,
    mtime = excluded.mtime
RETURNING id;",
            new
            {
                path = meta.Path,
                name = meta.Name,
                isFolder = meta.IsFolder ? 1 : 0,
                fileSize = meta.FileSize,
                mtime = meta.Mtime,
                addedAt = meta.LastScanned > 0 ? meta.LastScanned : NowUnix(),
            }, _currentTx);
    }

    public void TouchLastAccessed(string path, long now)
    {
        lock (_lock)
            _conn.Execute(@"
UPDATE asset_thumb SET last_accessed = @now
WHERE asset_id = (SELECT id FROM asset WHERE path = @path);",
                new { now, path }, _currentTx);
    }

    public IReadOnlyList<AssetMeta> ListChildren(string folderPath)
    {
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (_lock)
            return _conn.Query<AssetMeta>(@"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       COALESCE(t.last_scanned, a.added_at) AS last_scanned,
       COALESCE(t.last_accessed, a.added_at) AS last_accessed
FROM asset a
LEFT JOIN asset_thumb t ON t.asset_id = a.id
WHERE a.path LIKE @prefix;",
                new { prefix = prefix + "%" }, _currentTx).AsList();
    }

    public IEnumerable<AssetMeta> ListAll()
    {
        lock (_lock)
            return _conn.Query<AssetMeta>(@"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       COALESCE(t.last_scanned, a.added_at) AS last_scanned,
       COALESCE(t.last_accessed, a.added_at) AS last_accessed
FROM asset a
LEFT JOIN asset_thumb t ON t.asset_id = a.id;", transaction: _currentTx).AsList();
    }

    public IReadOnlyList<AssetMeta> ListLruVictims(int count)
    {
        lock (_lock)
            return _conn.Query<AssetMeta>(@"
SELECT a.path, a.name, a.is_folder, a.file_size, a.mtime,
       t.cover_origin, t.cover_thumb_key, t.cover_width, t.cover_height,
       t.last_scanned, t.last_accessed
FROM asset_thumb t
JOIN asset a ON a.id = t.asset_id
WHERE t.cover_thumb_key IS NOT NULL
ORDER BY t.last_accessed ASC
LIMIT @n;", new { n = count }, _currentTx).AsList();
    }

    public void Delete(string path)
    {
        lock (_lock)
            // ON DELETE CASCADE 連帶 thumb/meta/tag
            _conn.Execute("DELETE FROM asset WHERE path = @path;", new { path }, _currentTx);
    }

    public void DeleteSubtree(string folderPath)
    {
        var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        lock (_lock)
            _conn.Execute("DELETE FROM asset WHERE path = @exact OR path LIKE @prefix;",
                new { exact = folderPath, prefix = prefix + "%" }, _currentTx);
    }

    public void Clear()
    {
        lock (_lock) _conn.Execute("DELETE FROM asset;", transaction: _currentTx);
    }

    public void ClearThumbFields()
    {
        lock (_lock) _conn.Execute("DELETE FROM asset_thumb;", transaction: _currentTx);
    }

    public long TotalAssetCount()
    {
        lock (_lock) return _conn.ExecuteScalar<long>("SELECT COUNT(*) FROM asset;", transaction: _currentTx);
    }

    public long TotalCachedCount()
    {
        lock (_lock) return _conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM asset_thumb WHERE cover_thumb_key IS NOT NULL;", transaction: _currentTx);
    }

    public long TotalThumbBytes()
    {
        // file_size 是原檔大小，不是 thumb 大小；thumb 真實大小由 IThumbStore.TotalSizeBytes() 算 fs
        return 0;
    }

    public string? GetState(string key)
    {
        lock (_lock)
            return _conn.ExecuteScalar<string?>(
                "SELECT value FROM app_state WHERE key = @key LIMIT 1;",
                new { key }, _currentTx);
    }

    public void SetState(string key, string? value)
    {
        lock (_lock)
        {
            if (value == null)
                _conn.Execute("DELETE FROM app_state WHERE key = @key;", new { key }, _currentTx);
            else
                _conn.Execute(@"
INSERT INTO app_state (key, value) VALUES (@key, @value)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                    new { key, value }, _currentTx);
        }
    }

    public void RunInTransaction(Action action)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            _currentTx = tx;
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
            finally
            {
                _currentTx = null;
            }
        }
    }

    // ============================================================
    // Metadata (asset_meta)
    // ============================================================

    public AssetMetadata? GetMetadata(long assetId)
    {
        lock (_lock)
            return _conn.QueryFirstOrDefault<AssetMetadata>(@"
SELECT asset_id, author_id, title, series, chapter_no, language, translation_group,
       page_count, rating, read_status, read_count, last_read, external_id, parsed_at, last_page
FROM asset_meta WHERE asset_id = @id LIMIT 1;",
                new { id = assetId }, _currentTx);
    }

    public void UpsertMetadata(AssetMetadata meta)
    {
        lock (_lock)
            _conn.Execute(@"
INSERT INTO asset_meta (asset_id, author_id, title, series, chapter_no, language, translation_group,
                        page_count, rating, read_status, read_count, last_read, external_id, parsed_at, last_page)
VALUES (@AssetId, @AuthorId, @Title, @Series, @ChapterNo, @Language, @TranslationGroup,
        @PageCount, @Rating, @ReadStatus, @ReadCount, @LastRead, @ExternalId, @ParsedAt, @LastPage)
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
    parsed_at = excluded.parsed_at,
    last_page = excluded.last_page;",
                new
                {
                    meta.AssetId,
                    meta.AuthorId,
                    meta.Title,
                    meta.Series,
                    meta.ChapterNo,
                    meta.Language,
                    meta.TranslationGroup,
                    meta.PageCount,
                    meta.Rating,
                    ReadStatus = (int)meta.ReadStatus,
                    meta.ReadCount,
                    meta.LastRead,
                    meta.ExternalId,
                    ParsedAt = meta.ParsedAt > 0 ? meta.ParsedAt : NowUnix(),
                    meta.LastPage,
                }, _currentTx);
    }

    public AssetMetadataView? GetMetadataView(string path)
    {
        lock (_lock)
            return _conn.QueryFirstOrDefault<AssetMetadataView>(@"
SELECT a.path, am.title, au.main_name AS AuthorMainName,
       am.language, am.translation_group, am.page_count
FROM asset a
LEFT JOIN asset_meta am ON am.asset_id = a.id
LEFT JOIN author au ON au.id = am.author_id
WHERE a.path = @path
LIMIT 1;", new { path }, _currentTx);
    }

    public WorkDetail? GetWorkDetail(string path)
    {
        lock (_lock)
        {
            var detail = _conn.QueryFirstOrDefault<WorkDetail>(@"
SELECT a.id AS AssetId, a.path, a.name, a.is_folder, a.file_size, a.mtime,
       am.title, am.author_id, au.main_name AS AuthorMainName,
       am.series, am.chapter_no, am.language, am.translation_group,
       am.page_count, am.rating,
       am.read_status, am.read_count, am.last_read,
       am.external_id, am.parsed_at, am.last_page
FROM asset a
LEFT JOIN asset_meta am ON am.asset_id = a.id
LEFT JOIN author au ON au.id = am.author_id
WHERE a.path = @path
LIMIT 1;", new { path }, _currentTx);
            if (detail == null) return null;

            detail.Tags = _conn.Query<Tag>(@"
SELECT t.id, t.name, t.category
FROM tag t
JOIN asset_tag at ON at.tag_id = t.id
WHERE at.asset_id = @assetId
ORDER BY t.name;", new { assetId = detail.AssetId }, _currentTx).AsList();

            return detail;
        }
    }

    public void RecordRead(string path)
    {
        lock (_lock)
            // 一條 SQL 完成：count++ / last_read 更新 / Unread (=0) 升級到 Reading (=1)
            // affected=0 是 OK 的 — 該 path 沒 asset_meta 紀錄（Author/Library folder）
            _conn.Execute(@"
UPDATE asset_meta SET
    read_count = read_count + 1,
    last_read = @now,
    read_status = CASE WHEN read_status = 0 THEN 1 ELSE read_status END
WHERE asset_id = (SELECT id FROM asset WHERE path = @path);",
                new { now = NowUnix(), path }, _currentTx);
    }

    public void SetReadStatus(string path, ReadStatus status)
    {
        lock (_lock)
            _conn.Execute(@"
UPDATE asset_meta SET read_status = @status
WHERE asset_id = (SELECT id FROM asset WHERE path = @path);",
                new { status = (int)status, path }, _currentTx);
    }

    public ReadStatus? GetReadStatus(string path)
    {
        lock (_lock)
        {
            var v = _conn.ExecuteScalar<int?>(@"
SELECT am.read_status FROM asset_meta am
JOIN asset a ON a.id = am.asset_id
WHERE a.path = @path LIMIT 1;", new { path }, _currentTx);
            return v.HasValue ? (ReadStatus)v.Value : null;
        }
    }

    public int? GetLastPage(string path)
    {
        lock (_lock)
            return _conn.ExecuteScalar<int?>(@"
SELECT am.last_page FROM asset_meta am
JOIN asset a ON a.id = am.asset_id
WHERE a.path = @path LIMIT 1;", new { path }, _currentTx);
    }

    public void SetLastPage(string path, int pageIndex)
    {
        lock (_lock)
            _conn.Execute(@"
UPDATE asset_meta SET last_page = @page
WHERE asset_id = (SELECT id FROM asset WHERE path = @path);",
                new { page = pageIndex, path }, _currentTx);
            // affected=0 OK：沒 asset_meta 不寫入（reader 開非 work 路徑時不會炸）
    }

    // 共用 SET 子句 — 4 個欄位一次清回初始狀態
    private const string ResetReadingFieldsSet =
        "read_status = 0, read_count = 0, last_read = NULL, last_page = NULL";

    public void ResetReadingRecord(string path)
    {
        lock (_lock)
            _conn.Execute(@$"
UPDATE asset_meta SET {ResetReadingFieldsSet}
WHERE asset_id = (SELECT id FROM asset WHERE path = @path);",
                new { path }, _currentTx);
    }

    public int ResetReadingRecordsForAuthor(long authorId)
    {
        lock (_lock)
            return _conn.Execute(@$"
UPDATE asset_meta SET {ResetReadingFieldsSet}
WHERE author_id = @authorId;",
                new { authorId }, _currentTx);
    }

    public int ResetAllReadingRecords()
    {
        lock (_lock)
            return _conn.Execute(@$"UPDATE asset_meta SET {ResetReadingFieldsSet};",
                transaction: _currentTx);
    }

    public void DeleteMetadata(long assetId)
    {
        lock (_lock)
            _conn.Execute("DELETE FROM asset_meta WHERE asset_id = @id;",
                new { id = assetId }, _currentTx);
    }

    public void ClearAllMetadata()
    {
        lock (_lock) _conn.Execute("DELETE FROM asset_meta;", transaction: _currentTx);
    }

    // ============================================================
    // Author
    // ============================================================

    public Author? FindAuthorByMainName(string mainName)
    {
        lock (_lock)
            return _conn.QueryFirstOrDefault<Author>(
                "SELECT id, main_name, folder_path, learned_at FROM author WHERE main_name = @mainName LIMIT 1;",
                new { mainName }, _currentTx);
    }

    public long UpsertAuthor(string mainName, string? folderPath = null)
    {
        lock (_lock)
            return _conn.ExecuteScalar<long>(@"
INSERT INTO author (main_name, folder_path, learned_at)
VALUES (@mainName, @folderPath, @now)
ON CONFLICT(main_name) DO UPDATE SET
    folder_path = COALESCE(excluded.folder_path, author.folder_path)
RETURNING id;",
                new { mainName, folderPath, now = NowUnix() }, _currentTx);
    }

    public void AddAuthorAlias(long authorId, string aliasName, string? lang = null)
    {
        lock (_lock)
            _conn.Execute(@"
INSERT OR IGNORE INTO author_alias (author_id, name, lang) VALUES (@authorId, @aliasName, @lang);",
                new { authorId, aliasName, lang }, _currentTx);
    }

    public IReadOnlyList<AuthorAlias> ListAuthorAliases(long authorId)
    {
        lock (_lock)
            return _conn.Query<AuthorAlias>(
                "SELECT id, author_id, name, lang FROM author_alias WHERE author_id = @authorId;",
                new { authorId }, _currentTx).AsList();
    }

    public long? ResolveAuthorIdByPath(string path)
    {
        lock (_lock)
        {
            // 1. Work path → asset_meta.author_id
            var fromMeta = _conn.ExecuteScalar<long?>(@"
SELECT am.author_id
FROM asset a
JOIN asset_meta am ON am.asset_id = a.id
WHERE a.path = @path AND am.author_id IS NOT NULL
LIMIT 1;", new { path }, _currentTx);
            if (fromMeta.HasValue) return fromMeta;

            // 2. Author folder path → author.folder_path 比對
            return _conn.ExecuteScalar<long?>(
                "SELECT id FROM author WHERE folder_path = @path LIMIT 1;",
                new { path }, _currentTx);
        }
    }

    public AuthorInfo? GetAuthorInfo(long authorId)
    {
        lock (_lock)
        {
            // 1. Author 基本資料
            var author = _conn.QueryFirstOrDefault<Author>(
                "SELECT id, main_name, folder_path, learned_at FROM author WHERE id = @id LIMIT 1;",
                new { id = authorId }, _currentTx);
            if (author == null) return null;

            var info = new AuthorInfo { Author = author };

            // 2. 別名
            info.Aliases = _conn.Query<AuthorAlias>(
                "SELECT id, author_id, name, lang FROM author_alias WHERE author_id = @authorId;",
                new { authorId }, _currentTx).AsList();

            // 3. read_status 計數（GROUP BY read_status）+ TotalWorks（同次掃）
            var statusCounts = _conn.Query<(int ReadStatus, int Count)>(
                "SELECT read_status, COUNT(*) AS count FROM asset_meta WHERE author_id = @authorId GROUP BY read_status;",
                new { authorId }, _currentTx);
            foreach (var (status, count) in statusCounts)
            {
                info.TotalWorks += count;
                switch ((ReadStatus)status)
                {
                    case ReadStatus.Unread: info.UnreadCount = count; break;
                    case ReadStatus.Reading: info.ReadingCount = count; break;
                    case ReadStatus.Completed: info.CompletedCount = count; break;
                    case ReadStatus.Dropped: info.DroppedCount = count; break;
                }
            }

            // 3.5 全作品累計閱讀次數
            info.TotalReadCount = _conn.ExecuteScalar<int>(
                "SELECT COALESCE(SUM(read_count), 0) FROM asset_meta WHERE author_id = @authorId;",
                new { authorId }, _currentTx);

            // 4. Top 10 tags（依該作者作品的 tag 命中次數降序）
            info.TopTags = _conn.Query<TagFreq>(@"
SELECT t.name, COUNT(*) AS count
FROM tag t
JOIN asset_tag at ON at.tag_id = t.id
JOIN asset_meta am ON am.asset_id = at.asset_id
WHERE am.author_id = @authorId
GROUP BY t.name
ORDER BY count DESC
LIMIT 10;", new { authorId }, _currentTx).AsList();

            // 5. AllWorks — 全部作品依 title 排序（NULL 排最後）
            info.AllWorks = _conn.Query<AssetMetadataView>(@"
SELECT a.path, am.title, am.language, am.translation_group, am.page_count
FROM asset a
JOIN asset_meta am ON am.asset_id = a.id
WHERE am.author_id = @authorId
ORDER BY am.title IS NULL, am.title;", new { authorId }, _currentTx).AsList();
            // AuthorMainName 在 SQL 中沒撈（避免 parameter as column），統一在 C# 補
            foreach (var w in info.AllWorks) w.AuthorMainName = author.MainName;

            // 6. RecentlyRead — 最近 5 筆（last_read DESC，含 read_count）
            info.RecentlyRead = _conn.Query<RecentRead>(@"
SELECT a.path, am.title, am.last_read AS LastRead, am.read_count AS ReadCount
FROM asset a
JOIN asset_meta am ON am.asset_id = a.id
WHERE am.author_id = @authorId AND am.last_read IS NOT NULL
ORDER BY am.last_read DESC
LIMIT 5;", new { authorId }, _currentTx).AsList();

            return info;
        }
    }

    // ============================================================
    // Tag
    // ============================================================

    public long UpsertTag(string name, string? category = null)
    {
        lock (_lock)
            return _conn.ExecuteScalar<long>(@"
INSERT INTO tag (name, category) VALUES (@name, @category)
ON CONFLICT(name) DO UPDATE SET
    category = COALESCE(excluded.category, tag.category)
RETURNING id;",
                new { name, category }, _currentTx);
    }

    public void AssociateTag(long assetId, long tagId)
    {
        lock (_lock)
            _conn.Execute("INSERT OR IGNORE INTO asset_tag (asset_id, tag_id) VALUES (@assetId, @tagId);",
                new { assetId, tagId }, _currentTx);
    }

    public IReadOnlyList<Tag> ListTagsForAsset(long assetId)
    {
        lock (_lock)
            return _conn.Query<Tag>(@"
SELECT t.id, t.name, t.category
FROM tag t
JOIN asset_tag at ON at.tag_id = t.id
WHERE at.asset_id = @assetId;", new { assetId }, _currentTx).AsList();
    }

    public void Dispose()
    {
        lock (_lock) _conn.Dispose();
    }
}

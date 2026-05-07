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
    private const int CurrentSchemaVersion = 6;

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
        // v1～v3：逐步補 columns 到最新
        // v4：no-op
        if (currentVersion == 0)
        {
            CreateSchemaV2();
            ApplyV3Additions();
            ApplyV4Additions();
            ApplyV5Additions();
            ApplyV6Additions();
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
            currentVersion = 3;
        }
        if (currentVersion == 3)
        {
            ApplyV4Additions();
            currentVersion = 4;
        }
        if (currentVersion == 4)
        {
            ApplyV5Additions();
            currentVersion = 5;
        }
        if (currentVersion == 5)
        {
            ApplyV6Additions();
        }
        _conn.Execute("DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES (@v);",
            new { v = CurrentSchemaVersion });
    }

    /// <summary>v2 → v3：asset_meta 加 last_page 欄位（Phase 1.3 reader 進度保存用）。</summary>
    private void ApplyV3Additions()
    {
        _conn.Execute("ALTER TABLE asset_meta ADD COLUMN last_page INTEGER;");
    }

    /// <summary>v3 → v4：metadata 編輯機制（#45）。
    /// - asset_meta.manually_edited：保護使用者編輯不被 re-parse / 同步覆寫
    /// - author_tag table：tag 不只屬於 work，作者本身也能掛 tag（風格/類型描述）。
    /// </summary>
    private void ApplyV4Additions()
    {
        _conn.Execute(@"
ALTER TABLE asset_meta ADD COLUMN manually_edited INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS author_tag (
    author_id INTEGER NOT NULL,
    tag_id INTEGER NOT NULL,
    PRIMARY KEY (author_id, tag_id),
    FOREIGN KEY (author_id) REFERENCES author(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_author_tag_tag_id ON author_tag(tag_id);
");
    }

    /// <summary>v4 → v5：外部 metadata source 資料表（#52）。
    /// - 一個 asset 可從多個 source 拉資料（nhentai / ehentai / DLsite / ...）
    /// - source_id：對方平台的 gallery id（"568742"）
    /// - raw_json：完整 metadata JSON 存檔（剝 pages / related 後 ~1.5 KB/本）
    ///   未來換 source / 重 import 不必重打 API
    /// - PK (asset_id, source)：同 asset 同 source 只一筆，重抓覆蓋
    /// - idx_aes_source 給「source + source_id 反查 asset」用（批次 dedup）
    /// </summary>
    private void ApplyV5Additions()
    {
        _conn.Execute(@"
CREATE TABLE IF NOT EXISTS asset_external_source (
    asset_id    INTEGER NOT NULL,
    source      TEXT NOT NULL,
    source_id   TEXT NOT NULL,
    media_id    TEXT,
    upload_at   INTEGER,
    raw_json    TEXT NOT NULL,
    fetched_at  INTEGER NOT NULL,
    PRIMARY KEY (asset_id, source),
    FOREIGN KEY (asset_id) REFERENCES asset(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_aes_source ON asset_external_source(source, source_id);
");
    }

    /// <summary>v5 → v6：tag 多語譯文支援。
    /// - tag.name 仍是「原文 / canonical」（通常英文 / romaji），UNIQUE 不變
    /// - tag_translation 一個 tag 可有多筆譯文（zh-TW / zh-CN / ja / en / ...）
    /// - source 區分 'ehtag' (自動翻譯庫) / 'manual' (使用者手動編輯) / 'nhentai' (來源平台帶的)
    ///   refresh 翻譯庫時只覆蓋 source='ehtag'，'manual' 永遠保留。
    /// </summary>
    private void ApplyV6Additions()
    {
        _conn.Execute(@"
CREATE TABLE IF NOT EXISTS tag_translation (
    tag_id     INTEGER NOT NULL,
    lang       TEXT NOT NULL,
    name       TEXT NOT NULL,
    source     TEXT,
    updated_at INTEGER,
    PRIMARY KEY (tag_id, lang),
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_tag_translation_name ON tag_translation(name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_tag_translation_lang ON tag_translation(lang);
");
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
        // 「完整清除」= 回到全新狀態。除了 asset (cascade 帶走 asset_meta / asset_thumb / asset_tag)
        // 也要清 author / tag 系列，否則 stale 作者/標籤會殘留（Detector 修正後 rebuild 也修不到）。
        lock (_lock)
        {
            _conn.Execute(@"
DELETE FROM asset;
DELETE FROM author_tag;
DELETE FROM author_alias;
DELETE FROM author;
DELETE FROM tag;
DELETE FROM folder_role;
", transaction: _currentTx);
        }
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
       page_count, rating, read_status, read_count, last_read, external_id, parsed_at, last_page,
       manually_edited
FROM asset_meta WHERE asset_id = @id LIMIT 1;",
                new { id = assetId }, _currentTx);
    }

    public void UpsertMetadata(AssetMetadata meta)
    {
        lock (_lock)
            _conn.Execute(@"
INSERT INTO asset_meta (asset_id, author_id, title, series, chapter_no, language, translation_group,
                        page_count, rating, read_status, read_count, last_read, external_id, parsed_at, last_page,
                        manually_edited)
VALUES (@AssetId, @AuthorId, @Title, @Series, @ChapterNo, @Language, @TranslationGroup,
        @PageCount, @Rating, @ReadStatus, @ReadCount, @LastRead, @ExternalId, @ParsedAt, @LastPage,
        @ManuallyEdited)
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
    last_page = excluded.last_page,
    manually_edited = excluded.manually_edited;",
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
                    ManuallyEdited = meta.ManuallyEdited ? 1 : 0,
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
       am.external_id, am.parsed_at, am.last_page,
       am.manually_edited
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

            // 4.5 掛在作者本身的 tag — author_tag 表（給編輯模式用）
            info.AuthorTags = _conn.Query<Tag>(@"
SELECT t.id, t.name, t.category
FROM tag t
JOIN author_tag at ON at.tag_id = t.id
WHERE at.author_id = @authorId
ORDER BY t.name;", new { authorId }, _currentTx).AsList();

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

    public void DisassociateTag(long assetId, long tagId)
    {
        lock (_lock)
            _conn.Execute("DELETE FROM asset_tag WHERE asset_id = @assetId AND tag_id = @tagId;",
                new { assetId, tagId }, _currentTx);
    }

    public void SetAssetTags(long assetId, IEnumerable<string> tagNames)
    {
        var names = tagNames.Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct().ToList();
        RunInTransaction(() =>
        {
            _conn.Execute("DELETE FROM asset_tag WHERE asset_id = @assetId;", new { assetId }, _currentTx);
            foreach (var name in names)
            {
                long tagId = UpsertTag(name);
                _conn.Execute("INSERT OR IGNORE INTO asset_tag (asset_id, tag_id) VALUES (@assetId, @tagId);",
                    new { assetId, tagId }, _currentTx);
            }
        });
    }

    // ===== Author tags =====

    public void AssociateAuthorTag(long authorId, long tagId)
    {
        lock (_lock)
            _conn.Execute("INSERT OR IGNORE INTO author_tag (author_id, tag_id) VALUES (@authorId, @tagId);",
                new { authorId, tagId }, _currentTx);
    }

    public void DisassociateAuthorTag(long authorId, long tagId)
    {
        lock (_lock)
            _conn.Execute("DELETE FROM author_tag WHERE author_id = @authorId AND tag_id = @tagId;",
                new { authorId, tagId }, _currentTx);
    }

    public void SetAuthorTags(long authorId, IEnumerable<string> tagNames)
    {
        var names = tagNames.Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct().ToList();
        RunInTransaction(() =>
        {
            _conn.Execute("DELETE FROM author_tag WHERE author_id = @authorId;", new { authorId }, _currentTx);
            foreach (var name in names)
            {
                long tagId = UpsertTag(name);
                _conn.Execute("INSERT OR IGNORE INTO author_tag (author_id, tag_id) VALUES (@authorId, @tagId);",
                    new { authorId, tagId }, _currentTx);
            }
        });
    }

    public IReadOnlyList<Tag> ListTagsForAuthor(long authorId)
    {
        lock (_lock)
            return _conn.Query<Tag>(@"
SELECT t.id, t.name, t.category
FROM tag t
JOIN author_tag at ON at.tag_id = t.id
WHERE at.author_id = @authorId
ORDER BY t.name;", new { authorId }, _currentTx).AsList();
    }

    // ===== Editing APIs (#45) =====

    public void RenameAuthor(long authorId, string newMainName)
    {
        if (string.IsNullOrWhiteSpace(newMainName))
            throw new ArgumentException("newMainName cannot be empty", nameof(newMainName));
        var trimmed = newMainName.Trim();
        lock (_lock)
        {
            // 衝突檢查 — 已有別人用此 main_name
            var conflictId = _conn.ExecuteScalar<long?>(
                "SELECT id FROM author WHERE main_name = @name AND id != @id LIMIT 1;",
                new { name = trimmed, id = authorId }, _currentTx);
            if (conflictId.HasValue)
                throw new InvalidOperationException(
                    $"Author with main_name '{trimmed}' already exists (id={conflictId}). Caller should use MergeAuthor instead.");
            _conn.Execute("UPDATE author SET main_name = @name WHERE id = @id;",
                new { name = trimmed, id = authorId }, _currentTx);
        }
    }

    public void MergeAuthor(long srcId, long destId)
    {
        if (srcId == destId) return;
        RunInTransaction(() =>
        {
            // 轉移 work（asset_meta.author_id）
            _conn.Execute("UPDATE asset_meta SET author_id = @dest WHERE author_id = @src;",
                new { dest = destId, src = srcId }, _currentTx);
            // 轉移別名（衝突就跳過 — UNIQUE(author_id, name)）
            _conn.Execute(@"
INSERT OR IGNORE INTO author_alias (author_id, name, lang)
SELECT @dest, name, lang FROM author_alias WHERE author_id = @src;",
                new { dest = destId, src = srcId }, _currentTx);
            _conn.Execute("DELETE FROM author_alias WHERE author_id = @src;", new { src = srcId }, _currentTx);
            // 轉移 author_tag（衝突 ignore）
            _conn.Execute(@"
INSERT OR IGNORE INTO author_tag (author_id, tag_id)
SELECT @dest, tag_id FROM author_tag WHERE author_id = @src;",
                new { dest = destId, src = srcId }, _currentTx);
            _conn.Execute("DELETE FROM author_tag WHERE author_id = @src;", new { src = srcId }, _currentTx);
            // 把 src 的主名變成 dest 的別名（保留歷史）
            _conn.Execute(@"
INSERT OR IGNORE INTO author_alias (author_id, name, lang)
SELECT @dest, main_name, NULL FROM author WHERE id = @src;",
                new { dest = destId, src = srcId }, _currentTx);
            _conn.Execute("DELETE FROM author WHERE id = @src;", new { src = srcId }, _currentTx);
        });
    }

    public void SetAuthorFolderPath(long authorId, string? folderPath)
    {
        lock (_lock)
            _conn.Execute("UPDATE author SET folder_path = @path WHERE id = @id;",
                new { path = folderPath, id = authorId }, _currentTx);
    }

    public void UpdateAuthorAlias(long aliasId, string name, string? lang)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name cannot be empty", nameof(name));
        lock (_lock)
            _conn.Execute("UPDATE author_alias SET name = @name, lang = @lang WHERE id = @id;",
                new { name = name.Trim(), lang, id = aliasId }, _currentTx);
    }

    public void DeleteAuthorAlias(long aliasId)
    {
        lock (_lock)
            _conn.Execute("DELETE FROM author_alias WHERE id = @id;", new { id = aliasId }, _currentTx);
    }

    public void SetWorkAuthor(long assetId, long? authorId)
    {
        lock (_lock)
            _conn.Execute("UPDATE asset_meta SET author_id = @authorId WHERE asset_id = @assetId;",
                new { authorId, assetId }, _currentTx);
    }

    public IReadOnlyList<Author> SearchAuthors(string prefix, int limit = 20)
    {
        var pattern = (prefix ?? "").Trim() + "%";
        lock (_lock)
            return _conn.Query<Author>(@"
SELECT DISTINCT a.id, a.main_name, a.folder_path, a.learned_at
FROM author a
LEFT JOIN author_alias aa ON aa.author_id = a.id
WHERE a.main_name LIKE @pattern OR aa.name LIKE @pattern
ORDER BY a.main_name
LIMIT @limit;", new { pattern, limit }, _currentTx).AsList();
    }

    public IReadOnlyList<Tag> SearchTags(string prefix, int limit = 20)
    {
        var pattern = (prefix ?? "").Trim() + "%";
        lock (_lock)
            return _conn.Query<Tag>(@"
SELECT id, name, category FROM tag
WHERE name LIKE @pattern
ORDER BY name
LIMIT @limit;", new { pattern, limit }, _currentTx).AsList();
    }

    public IReadOnlyList<TagUsage> ListTagsByUsage()
    {
        lock (_lock)
            return _conn.Query<TagUsage>(@"
SELECT t.id, t.name, t.category,
       (SELECT COUNT(*) FROM asset_tag WHERE tag_id = t.id) AS AssetCount,
       (SELECT COUNT(*) FROM author_tag WHERE tag_id = t.id) AS AuthorCount
FROM tag t
ORDER BY (AssetCount + AuthorCount) DESC, t.name;", transaction: _currentTx).AsList();
    }

    public IReadOnlyList<AuthorUsage> ListAuthorsByUsage()
    {
        lock (_lock)
        {
            // 一次撈：author + work_count + alias_count + tag_count
            var rows = _conn.Query<(long Id, string MainName, string? FolderPath, long LearnedAt,
                                    int WorkCount, int AliasCount, int TagCount)>(@"
SELECT a.id, a.main_name AS MainName, a.folder_path AS FolderPath, a.learned_at AS LearnedAt,
       (SELECT COUNT(*) FROM asset_meta WHERE author_id = a.id) AS WorkCount,
       (SELECT COUNT(*) FROM author_alias WHERE author_id = a.id) AS AliasCount,
       (SELECT COUNT(*) FROM author_tag WHERE author_id = a.id) AS TagCount
FROM author a
ORDER BY WorkCount DESC, a.main_name;", transaction: _currentTx).AsList();
            return rows.Select(r => new AuthorUsage
            {
                Author = new Author { Id = r.Id, MainName = r.MainName, FolderPath = r.FolderPath, LearnedAt = r.LearnedAt },
                WorkCount = r.WorkCount,
                AliasCount = r.AliasCount,
                TagCount = r.TagCount,
            }).ToList();
        }
    }

    public void RenameTag(long tagId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName cannot be empty", nameof(newName));
        var trimmed = newName.Trim();
        lock (_lock)
        {
            var conflictId = _conn.ExecuteScalar<long?>(
                "SELECT id FROM tag WHERE name = @name AND id != @id LIMIT 1;",
                new { name = trimmed, id = tagId }, _currentTx);
            if (conflictId.HasValue)
                throw new InvalidOperationException(
                    $"Tag with name '{trimmed}' already exists (id={conflictId}). Caller should use MergeTag instead.");
            _conn.Execute("UPDATE tag SET name = @name WHERE id = @id;",
                new { name = trimmed, id = tagId }, _currentTx);
        }
    }

    public void MergeTag(long srcId, long destId)
    {
        if (srcId == destId) return;
        RunInTransaction(() =>
        {
            _conn.Execute(@"
INSERT OR IGNORE INTO asset_tag (asset_id, tag_id)
SELECT asset_id, @dest FROM asset_tag WHERE tag_id = @src;",
                new { dest = destId, src = srcId }, _currentTx);
            _conn.Execute("DELETE FROM asset_tag WHERE tag_id = @src;", new { src = srcId }, _currentTx);
            _conn.Execute(@"
INSERT OR IGNORE INTO author_tag (author_id, tag_id)
SELECT author_id, @dest FROM author_tag WHERE tag_id = @src;",
                new { dest = destId, src = srcId }, _currentTx);
            _conn.Execute("DELETE FROM author_tag WHERE tag_id = @src;", new { src = srcId }, _currentTx);
            _conn.Execute("DELETE FROM tag WHERE id = @src;", new { src = srcId }, _currentTx);
        });
    }

    public void DeleteTag(long tagId)
    {
        lock (_lock)
            _conn.Execute("DELETE FROM tag WHERE id = @id;", new { id = tagId }, _currentTx);
        // ON DELETE CASCADE 會自動清 asset_tag / author_tag
    }

    public void SetTagCategory(long tagId, string? category)
    {
        lock (_lock)
            _conn.Execute("UPDATE tag SET category = @cat WHERE id = @id;",
                new { cat = category, id = tagId }, _currentTx);
    }

    public void SetMetadataManuallyEdited(long assetId, bool edited)
    {
        lock (_lock)
            _conn.Execute("UPDATE asset_meta SET manually_edited = @flag WHERE asset_id = @id;",
                new { flag = edited ? 1 : 0, id = assetId }, _currentTx);
    }

    public IReadOnlyList<AssetCatalogEntry> ListAssetEntries()
    {
        lock (_lock)
            return _conn.Query<AssetCatalogEntry>(@"
SELECT id AS Id, path AS Path, name AS Name, is_folder AS IsFolder, file_size AS FileSize, mtime AS Mtime
FROM asset
ORDER BY id;", transaction: _currentTx).AsList();
    }

    public void UpdateAssetPath(long assetId, string newPath)
    {
        lock (_lock)
            _conn.Execute("UPDATE asset SET path = @p WHERE id = @id;",
                new { p = newPath, id = assetId }, _currentTx);
    }

    public void DeleteAssetById(long assetId)
    {
        lock (_lock)
            // ON DELETE CASCADE 自動清 asset_meta / asset_thumb / asset_tag
            _conn.Execute("DELETE FROM asset WHERE id = @id;", new { id = assetId }, _currentTx);
    }

    public ParseStats GetParseStats()
    {
        lock (_lock)
        {
            var total = _conn.ExecuteScalar<int>("SELECT COUNT(*) FROM asset;", transaction: _currentTx);
            var parsed = _conn.ExecuteScalar<int>("SELECT COUNT(*) FROM asset_meta;", transaction: _currentTx);
            var manually = _conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM asset_meta WHERE manually_edited = 1;", transaction: _currentTx);
            return new ParseStats
            {
                TotalAssets = total,
                ParsedCount = parsed,
                ManuallyEditedCount = manually,
            };
        }
    }

    // ===== External metadata source (#52) =====

    public AssetExternalSource? GetExternalSource(long assetId, string source)
    {
        lock (_lock)
            return _conn.QuerySingleOrDefault<AssetExternalSource>(@"
SELECT asset_id AS AssetId, source AS Source, source_id AS SourceId, media_id AS MediaId,
       upload_at AS UploadAt, raw_json AS RawJson, fetched_at AS FetchedAt
FROM asset_external_source
WHERE asset_id = @id AND source = @s;",
                new { id = assetId, s = source }, _currentTx);
    }

    public IReadOnlyList<AssetExternalSource> ListExternalSources(long assetId)
    {
        lock (_lock)
            return _conn.Query<AssetExternalSource>(@"
SELECT asset_id AS AssetId, source AS Source, source_id AS SourceId, media_id AS MediaId,
       upload_at AS UploadAt, raw_json AS RawJson, fetched_at AS FetchedAt
FROM asset_external_source
WHERE asset_id = @id
ORDER BY source;",
                new { id = assetId }, _currentTx).AsList();
    }

    public void UpsertExternalSource(AssetExternalSource record)
    {
        lock (_lock)
            _conn.Execute(@"
INSERT INTO asset_external_source
    (asset_id, source, source_id, media_id, upload_at, raw_json, fetched_at)
VALUES
    (@AssetId, @Source, @SourceId, @MediaId, @UploadAt, @RawJson, @FetchedAt)
ON CONFLICT(asset_id, source) DO UPDATE SET
    source_id = excluded.source_id,
    media_id = excluded.media_id,
    upload_at = excluded.upload_at,
    raw_json = excluded.raw_json,
    fetched_at = excluded.fetched_at;",
                record, _currentTx);
    }

    public void DeleteExternalSource(long assetId, string source)
    {
        lock (_lock)
            _conn.Execute(@"
DELETE FROM asset_external_source WHERE asset_id = @id AND source = @s;",
                new { id = assetId, s = source }, _currentTx);
    }

    public long? FindAssetByExternalId(string source, string sourceId)
    {
        lock (_lock)
            return _conn.QuerySingleOrDefault<long?>(@"
SELECT asset_id FROM asset_external_source
WHERE source = @s AND source_id = @sid LIMIT 1;",
                new { s = source, sid = sourceId }, _currentTx);
    }

    // ===== Virtual Library queries (#48 Phase 1) =====

    public IReadOnlyList<string> ListWorksByAuthor(long authorId)
    {
        lock (_lock)
            return _conn.Query<string>(@"
SELECT a.path FROM asset a
JOIN asset_meta am ON a.id = am.asset_id
WHERE am.author_id = @id
ORDER BY am.title COLLATE NOCASE, a.path;",
                new { id = authorId }, _currentTx).AsList();
    }

    public IReadOnlyList<string> ListWorksByTags(IReadOnlyList<long> tagIds, bool intersect = true)
    {
        if (tagIds.Count == 0) return Array.Empty<string>();
        lock (_lock)
        {
            if (intersect)
            {
                // AND: 作品要同時帶所有 tag — GROUP BY + HAVING COUNT(distinct) = N
                return _conn.Query<string>(@"
SELECT a.path FROM asset a
JOIN asset_tag at ON a.id = at.asset_id
WHERE at.tag_id IN @ids
GROUP BY a.id
HAVING COUNT(DISTINCT at.tag_id) = @n
ORDER BY a.path;",
                    new { ids = tagIds, n = tagIds.Count }, _currentTx).AsList();
            }
            else
            {
                // OR: 帶任一 tag 即可
                return _conn.Query<string>(@"
SELECT DISTINCT a.path FROM asset a
JOIN asset_tag at ON a.id = at.asset_id
WHERE at.tag_id IN @ids
ORDER BY a.path;",
                    new { ids = tagIds }, _currentTx).AsList();
            }
        }
    }

    public IReadOnlyList<LanguageBucket> ListLanguageBuckets()
    {
        lock (_lock)
        {
            // Dapper 對 record 的 ctor signature 要嚴格 match，COUNT(*) 在 SQLite 回 long → 顯式 CAST 成 int
            var rows = _conn.Query<(string? Language, int WorkCount)>(@"
SELECT language AS Language, CAST(COUNT(*) AS INTEGER) AS WorkCount
FROM asset_meta
GROUP BY language
ORDER BY WorkCount DESC, language;",
                transaction: _currentTx).AsList();
            return rows.Select(r => new LanguageBucket(r.Language, r.WorkCount)).ToList();
        }
    }

    public IReadOnlyList<string> ListWorksByLanguage(string? language)
    {
        lock (_lock)
            return _conn.Query<string>(language == null ? @"
SELECT a.path FROM asset a
JOIN asset_meta am ON a.id = am.asset_id
WHERE am.language IS NULL
ORDER BY am.title COLLATE NOCASE, a.path;" : @"
SELECT a.path FROM asset a
JOIN asset_meta am ON a.id = am.asset_id
WHERE am.language = @lang
ORDER BY am.title COLLATE NOCASE, a.path;",
                new { lang = language }, _currentTx).AsList();
    }

    // ── 閱讀軌跡 ────────────────────────────────────────────

    public IReadOnlyList<string> ListRecentlyRead(int limit = 10)
    {
        lock (_lock)
            return _conn.Query<string>(@"
SELECT a.path FROM asset a
JOIN asset_meta am ON a.id = am.asset_id
WHERE am.last_read IS NOT NULL
ORDER BY am.last_read DESC
LIMIT @lim;",
                new { lim = limit }, _currentTx).AsList();
    }

    public IReadOnlyList<string> ListReadingInProgress()
    {
        lock (_lock)
            return _conn.Query<string>(@"
SELECT a.path FROM asset a
JOIN asset_meta am ON a.id = am.asset_id
WHERE am.read_status = @s
ORDER BY am.last_read DESC, am.title COLLATE NOCASE;",
                new { s = (int)ReadStatus.Reading }, _currentTx).AsList();
    }

    public IReadOnlyList<MostReadAuthor> ListMostReadAuthors(int limit = 10)
    {
        lock (_lock)
        {
            var rows = _conn.Query<(long Id, string MainName, string? FolderPath, long? LearnedAt, int TotalReads)>(@"
SELECT au.id AS Id, au.main_name AS MainName, au.folder_path AS FolderPath,
       au.learned_at AS LearnedAt, SUM(am.read_count) AS TotalReads
FROM author au
JOIN asset_meta am ON am.author_id = au.id
WHERE am.read_count > 0
GROUP BY au.id
ORDER BY TotalReads DESC, au.main_name
LIMIT @lim;",
                new { lim = limit }, _currentTx).AsList();
            return rows.Select(r => new MostReadAuthor(
                new Author { Id = r.Id, MainName = r.MainName, FolderPath = r.FolderPath, LearnedAt = r.LearnedAt ?? 0 },
                r.TotalReads)).ToList();
        }
    }

    public IReadOnlyList<MostReadTag> ListMostReadTags(int limit = 10)
    {
        lock (_lock)
        {
            var rows = _conn.Query<(long Id, string Name, string? Category, int TotalReads)>(@"
SELECT t.id AS Id, t.name AS Name, t.category AS Category,
       SUM(am.read_count) AS TotalReads
FROM tag t
JOIN asset_tag at ON at.tag_id = t.id
JOIN asset_meta am ON am.asset_id = at.asset_id
WHERE am.read_count > 0
GROUP BY t.id
ORDER BY TotalReads DESC, t.name
LIMIT @lim;",
                new { lim = limit }, _currentTx).AsList();
            return rows.Select(r => new MostReadTag(
                new Tag { Id = r.Id, Name = r.Name, Category = r.Category },
                r.TotalReads)).ToList();
        }
    }

    // ===== Tag translations (schema v6) =====

    public string? GetTagTranslation(long tagId, string lang)
    {
        lock (_lock)
            return _conn.QuerySingleOrDefault<string?>(
                "SELECT name FROM tag_translation WHERE tag_id = @id AND lang = @l;",
                new { id = tagId, l = lang }, _currentTx);
    }

    public IReadOnlyDictionary<long, string> GetTagTranslations(IReadOnlyList<long> tagIds, string lang)
    {
        if (tagIds.Count == 0) return new Dictionary<long, string>();
        lock (_lock)
        {
            var rows = _conn.Query<(long TagId, string Name)>(
                "SELECT tag_id AS TagId, name AS Name FROM tag_translation WHERE tag_id IN @ids AND lang = @l;",
                new { ids = tagIds, l = lang }, _currentTx);
            return rows.ToDictionary(r => r.TagId, r => r.Name);
        }
    }

    public void UpsertTagTranslation(long tagId, string lang, string translatedName, string source)
    {
        lock (_lock)
            _conn.Execute(@"
INSERT INTO tag_translation (tag_id, lang, name, source, updated_at)
VALUES (@id, @l, @n, @s, @t)
ON CONFLICT(tag_id, lang) DO UPDATE SET
    name = excluded.name,
    source = excluded.source,
    updated_at = excluded.updated_at;",
                new
                {
                    id = tagId,
                    l = lang,
                    n = translatedName,
                    s = source,
                    t = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                }, _currentTx);
    }

    public void DeleteTagTranslation(long tagId, string lang)
    {
        lock (_lock)
            _conn.Execute(
                "DELETE FROM tag_translation WHERE tag_id = @id AND lang = @l;",
                new { id = tagId, l = lang }, _currentTx);
    }

    public IReadOnlyList<long> SearchTagIdsByTranslatedName(string keyword, string? lang = null, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<long>();
        var pattern = "%" + keyword + "%";
        lock (_lock)
        {
            return _conn.Query<long>(lang == null ? @"
SELECT DISTINCT tag_id FROM tag_translation
WHERE name LIKE @p COLLATE NOCASE
LIMIT @lim;" : @"
SELECT tag_id FROM tag_translation
WHERE name LIKE @p COLLATE NOCASE AND lang = @l
LIMIT @lim;",
                new { p = pattern, l = lang, lim = limit }, _currentTx).AsList();
        }
    }

    public IReadOnlyList<TagTranslation> ListTagTranslations(long tagId)
    {
        lock (_lock)
            return _conn.Query<TagTranslation>(@"
SELECT tag_id AS TagId, lang AS Lang, name AS Name, source AS Source, updated_at AS UpdatedAt
FROM tag_translation
WHERE tag_id = @id
ORDER BY lang;",
                new { id = tagId }, _currentTx).AsList();
    }

    public IReadOnlyDictionary<string, long> GetTagIdsByNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return new Dictionary<string, long>();
        lock (_lock)
        {
            var rows = _conn.Query<(long Id, string Name)>(
                "SELECT id AS Id, name AS Name FROM tag WHERE name COLLATE NOCASE IN @names;",
                new { names }, _currentTx);
            // 用 user 輸入的 name 當 key（不論大小寫，以 DB 名為值）
            var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows) dict[r.Name] = r.Id;
            return dict;
        }
    }

    public void Dispose()
    {
        lock (_lock) _conn.Dispose();
    }
}

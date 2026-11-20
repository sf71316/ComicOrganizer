using Microsoft.Data.Sqlite;

namespace ComicOrganizer.VisualSearch;

public sealed record IndexedMangaRecord(long Id, string Path, string Title, string Fingerprint, int PageCount, long UpdatedAt);

public sealed record VisualVectorMeta(
    long VecId,
    int ShardId,
    long MangaId,
    string MangaTitle,
    string MangaPath,
    int PageNo,
    string EntryName,
    bool IsFullPage,
    float X,
    float Y,
    float W,
    float H,
    bool IsDeleted);

public sealed record VisualShardStat(int ShardId, int ActiveCount, int DeletedCount)
{
    public int TotalCount => ActiveCount + DeletedCount;
    public double DeletedRatio => TotalCount > 0 ? (double)DeletedCount / TotalCount : 0;
}

public sealed record VisualIndexOverview(
    int MangaCount,
    int TotalPages,
    int ActiveVectorCount,
    int DeletedVectorCount,
    long LastUpdatedUnixSec,
    IReadOnlyList<VisualShardStat> Shards);

/// <summary>
/// 視覺搜尋專用 SQLite（visual_index.db），與主程式 cache.db 完全隔離。
/// 支援多 shard 記錄、指紋搬移偵測與已刪除作品清理。
/// </summary>
public sealed class VisualIndexDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

    public VisualIndexDatabase(string dbPath, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
        };
        _conn = new SqliteConnection(builder.ToString());
        _conn.Open();

        if (!readOnly)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                CREATE TABLE IF NOT EXISTS mangas (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT NOT NULL UNIQUE,
                    title TEXT NOT NULL,
                    fingerprint TEXT NOT NULL,
                    page_count INTEGER NOT NULL DEFAULT 0,
                    updated_at INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_mangas_fp ON mangas(fingerprint);
                CREATE TABLE IF NOT EXISTS vectors (
                    vec_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    shard_id INTEGER NOT NULL DEFAULT 0,
                    manga_id INTEGER NOT NULL,
                    page_no INTEGER NOT NULL,
                    entry_name TEXT NOT NULL,
                    is_full_page INTEGER NOT NULL,
                    x REAL NOT NULL,
                    y REAL NOT NULL,
                    w REAL NOT NULL,
                    h REAL NOT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(manga_id) REFERENCES mangas(id)
                );
                CREATE INDEX IF NOT EXISTS idx_vectors_manga ON vectors(manga_id);
                CREATE INDEX IF NOT EXISTS idx_vectors_shard ON vectors(shard_id, is_deleted);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public IndexedMangaRecord? GetMangaByPath(string path)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, title, fingerprint, page_count, updated_at FROM mangas WHERE path = $p;";
        cmd.Parameters.AddWithValue("$p", path);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new IndexedMangaRecord(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt64(5))
            : null;
    }

    public List<IndexedMangaRecord> FindMangasByFingerprint(string fingerprint)
    {
        var list = new List<IndexedMangaRecord>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, title, fingerprint, page_count, updated_at FROM mangas WHERE fingerprint = $fp;";
        cmd.Parameters.AddWithValue("$fp", fingerprint);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new IndexedMangaRecord(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt64(5)));
        }
        return list;
    }

    public List<IndexedMangaRecord> ListAllMangas()
    {
        var list = new List<IndexedMangaRecord>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, title, fingerprint, page_count, updated_at FROM mangas;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new IndexedMangaRecord(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt64(5)));
        }
        return list;
    }

    /// <summary>
    /// 檔案被搬移或改名（指紋相同）時原地更新路徑與標題，完全無須重算向量。
    /// </summary>
    public void UpdateMangaLocation(long mangaId, string newPath, string newTitle)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE mangas SET path = $p, title = $t, updated_at = $ts WHERE id = $id;";
        cmd.Parameters.AddWithValue("$p", newPath);
        cmd.Parameters.AddWithValue("$t", newTitle);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$id", mangaId);
        cmd.ExecuteNonQuery();
    }

    public long UpsertManga(string path, string title, string fingerprint, int pageCount)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mangas(path, title, fingerprint, page_count, updated_at)
            VALUES ($p, $t, $fp, $pc, $ts)
            ON CONFLICT(path) DO UPDATE SET
                title = excluded.title,
                fingerprint = excluded.fingerprint,
                page_count = excluded.page_count,
                updated_at = excluded.updated_at
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$p", path);
        cmd.Parameters.AddWithValue("$t", title);
        cmd.Parameters.AddWithValue("$fp", fingerprint);
        cmd.Parameters.AddWithValue("$pc", pageCount);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public List<(long VecId, int ShardId)> MarkMangaVectorsDeleted(long mangaId, bool removeMangaRow = false)
    {
        var removed = new List<(long, int)>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT vec_id, shard_id FROM vectors WHERE manga_id = $m AND is_deleted = 0;";
            sel.Parameters.AddWithValue("$m", mangaId);
            using var r = sel.ExecuteReader();
            while (r.Read()) removed.Add((r.GetInt64(0), r.GetInt32(1)));
        }

        using var tx = _conn.BeginTransaction();
        using (var upd = _conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE vectors SET is_deleted = 1 WHERE manga_id = $m AND is_deleted = 0;";
            upd.Parameters.AddWithValue("$m", mangaId);
            upd.ExecuteNonQuery();
        }
        if (removeMangaRow)
        {
            using var del = _conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM vectors WHERE manga_id = $m; DELETE FROM mangas WHERE id = $m;";
            del.Parameters.AddWithValue("$m", mangaId);
            del.ExecuteNonQuery();
        }
        tx.Commit();
        return removed;
    }

    public List<long> InsertVectorsBatch(
        long mangaId,
        int shardId,
        IReadOnlyList<(int PageNo, string EntryName, bool IsFullPage, float X, float Y, float W, float H)> items)
    {
        var ids = new List<long>(items.Count);
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO vectors(shard_id, manga_id, page_no, entry_name, is_full_page, x, y, w, h, is_deleted)
            VALUES ($s, $m, $pn, $en, $fp, $x, $y, $w, $h, 0)
            RETURNING vec_id;
            """;
        var pS = cmd.Parameters.Add("$s", SqliteType.Integer);
        var pM = cmd.Parameters.Add("$m", SqliteType.Integer);
        var pPn = cmd.Parameters.Add("$pn", SqliteType.Integer);
        var pEn = cmd.Parameters.Add("$en", SqliteType.Text);
        var pFp = cmd.Parameters.Add("$fp", SqliteType.Integer);
        var pX = cmd.Parameters.Add("$x", SqliteType.Real);
        var pY = cmd.Parameters.Add("$y", SqliteType.Real);
        var pW = cmd.Parameters.Add("$w", SqliteType.Real);
        var pH = cmd.Parameters.Add("$h", SqliteType.Real);

        pS.Value = shardId;
        pM.Value = mangaId;
        foreach (var it in items)
        {
            pPn.Value = it.PageNo;
            pEn.Value = it.EntryName;
            pFp.Value = it.IsFullPage ? 1 : 0;
            pX.Value = it.X;
            pY.Value = it.Y;
            pW.Value = it.W;
            pH.Value = it.H;
            ids.Add(Convert.ToInt64(cmd.ExecuteScalar()));
        }

        tx.Commit();
        return ids;
    }

    public List<long> GetActiveVectorIdsInShard(int shardId)
    {
        var ids = new List<long>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT vec_id FROM vectors WHERE shard_id = $s AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$s", shardId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    public void PurgeDeletedVectorsInShard(int shardId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM vectors WHERE shard_id = $s AND is_deleted = 1;";
        cmd.Parameters.AddWithValue("$s", shardId);
        cmd.ExecuteNonQuery();
    }

    public Dictionary<long, VisualVectorMeta> GetVectorsByIds(IEnumerable<long> vecIds)
    {
        var result = new Dictionary<long, VisualVectorMeta>();
        var list = vecIds.Distinct().ToList();
        if (list.Count == 0) return result;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT v.vec_id, v.shard_id, v.manga_id, m.title, m.path, v.page_no, v.entry_name, v.is_full_page, v.x, v.y, v.w, v.h, v.is_deleted
            FROM vectors v
            JOIN mangas m ON m.id = v.manga_id
            WHERE v.vec_id IN ({string.Join(',', list)});
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rec = new VisualVectorMeta(
                r.GetInt64(0),
                r.GetInt32(1),
                r.GetInt64(2),
                r.GetString(3),
                r.GetString(4),
                r.GetInt32(5),
                r.GetString(6),
                r.GetInt32(7) != 0,
                r.GetFloat(8),
                r.GetFloat(9),
                r.GetFloat(10),
                r.GetFloat(11),
                r.GetInt32(12) != 0);
            result[rec.VecId] = rec;
        }
        return result;
    }

    public VisualIndexOverview GetOverview()
    {
        int mangaCount = 0, totalPages = 0, activeVecs = 0, deletedVecs = 0;
        long lastUpdated = 0;
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM mangas),
                    (SELECT COALESCE(SUM(page_count), 0) FROM mangas),
                    (SELECT COUNT(*) FROM vectors WHERE is_deleted = 0),
                    (SELECT COUNT(*) FROM vectors WHERE is_deleted = 1),
                    (SELECT COALESCE(MAX(updated_at), 0) FROM mangas);
                """;
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                mangaCount = r.GetInt32(0);
                totalPages = r.GetInt32(1);
                activeVecs = r.GetInt32(2);
                deletedVecs = r.GetInt32(3);
                lastUpdated = r.GetInt64(4);
            }
        }

        var shards = new List<VisualShardStat>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT shard_id,
                       SUM(CASE WHEN is_deleted = 0 THEN 1 ELSE 0 END),
                       SUM(CASE WHEN is_deleted = 1 THEN 1 ELSE 0 END)
                FROM vectors
                GROUP BY shard_id
                ORDER BY shard_id;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                shards.Add(new VisualShardStat(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)));
            }
        }

        return new VisualIndexOverview(mangaCount, totalPages, activeVecs, deletedVecs, lastUpdated, shards);
    }

    public void Dispose() => _conn.Dispose();
}

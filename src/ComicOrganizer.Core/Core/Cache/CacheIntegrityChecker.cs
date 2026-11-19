namespace ComicOrganizer.App.Core;

/// <summary>
/// Cache 整性檢查 — 對齊 DB / thumbs/ / source 三者：
///   OrphanDb     : DB 有 path 但 source 不存在（檔/目錄已被刪）
///   OrphanThumb  : thumb 檔在但無 DB 指向（孤兒，浪費空間）
///   StaleMtime   : DB mtime ≠ source 真實 mtime（cache 過期，自我修復 — Phase 3 fs 掃會觸發 cache miss）
///   MissingThumb : DB cover_thumb_key 有但 thumb 檔不見（自我修復 — 同上）
///
/// CacheRebuildService.UpdateIndexAsync 會：Scan → Fix(只清 OrphanDb / OrphanThumb) → ScanAsync(root)
/// 後兩種會由 ScanAsync 走 cache miss 自然修復，不必在 Fix 階段顯式處理。
/// </summary>
public class CacheIntegrityChecker
{
    private readonly IAssetMetaStore _meta;
    private readonly IThumbStore _thumbs;
    private readonly RebuildProgressTracker _tracker;

    public class IntegrityReport
    {
        public int OrphanDbRecords { get; set; }
        public int MissingThumbs { get; set; }
        public int OrphanThumbs { get; set; }
        public int StaleMtime { get; set; }
        public long TotalDbRecords { get; set; }
        public long TotalThumbFiles { get; set; }

        // Fix 用：實際 path / key 清單
        public List<string> OrphanDbPaths { get; } = new();
        public List<string> StalePaths { get; } = new();
        public List<string> MissingThumbPaths { get; } = new();
        public List<string> OrphanThumbKeys { get; } = new();

        public bool HasIssues =>
            OrphanDbRecords + MissingThumbs + OrphanThumbs + StaleMtime > 0;
    }

    public CacheIntegrityChecker(IAssetMetaStore meta, IThumbStore thumbs, RebuildProgressTracker tracker)
    {
        _meta = meta;
        _thumbs = thumbs;
        _tracker = tracker;
    }

    /// <summary>
    /// 執行整性掃描。在外部 tracker session 內執行，progress 透過 _tracker.Update 報告。
    /// 不自行管理 Start/NotifyEnd — 由呼叫端 (CacheRebuildService.UpdateIndexAsync) 負責。
    /// </summary>
    public IntegrityReport Scan(CancellationToken ct)
    {
        var rep = new IntegrityReport();
        var allMeta = _meta.ListAll().ToList();
        rep.TotalDbRecords = allMeta.Count;

        // Phase A：DB 對 source / thumb 比對
        var p = new RebuildProgress { Total = allMeta.Count };
        _tracker.Update(p);

        var expectedThumbKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < allMeta.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = allMeta[i];

            bool sourceExists = m.IsFolder ? Directory.Exists(m.Path) : File.Exists(m.Path);
            if (!sourceExists)
            {
                rep.OrphanDbRecords++;
                rep.OrphanDbPaths.Add(m.Path);
                p.Done = i + 1;
                p.CurrentPath = $"清理失效紀錄: {m.Path}";
                _tracker.Update(p);
                continue;
            }

            // mtime 檢查（folder mtime 不可靠 → 跳）
            if (!m.IsFolder)
            {
                try
                {
                    var fi = new FileInfo(m.Path);
                    var unix = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();
                    if (unix != m.Mtime)
                    {
                        rep.StaleMtime++;
                        rep.StalePaths.Add(m.Path);
                    }
                }
                catch { }
            }

            // thumb 檔
            if (!string.IsNullOrEmpty(m.CoverThumbKey))
            {
                expectedThumbKeys.Add(m.CoverThumbKey);
                using var s = _thumbs.OpenRead(m.CoverThumbKey);
                if (s == null)
                {
                    rep.MissingThumbs++;
                    rep.MissingThumbPaths.Add(m.Path);
                }
            }

            p.Done = i + 1;
            p.CurrentPath = m.Path;
            _tracker.Update(p);
        }

        // Phase B：thumbs/ 檔 vs DB expected
        ct.ThrowIfCancellationRequested();
        _tracker.Update(new RebuildProgress
        {
            Total = 0,
            Done = 0,
            CurrentPath = "掃描 thumbs/ 找孤兒...",
        });

        long thumbCount = 0;
        foreach (var key in _thumbs.EnumerateAllKeys())
        {
            ct.ThrowIfCancellationRequested();
            thumbCount++;
            if (!expectedThumbKeys.Contains(key))
            {
                rep.OrphanThumbs++;
                rep.OrphanThumbKeys.Add(key);
            }
        }
        rep.TotalThumbFiles = thumbCount;

        return rep;
    }

    /// <summary>
    /// 套用修復：
    ///   OrphanDb     → 刪 DB row + 對應 thumb 檔
    ///   MissingThumb → 把 DB 的 cover_thumb_key 清空（demote 成 evicted 狀態，使用者點開時 GetCoverAsync 會 decode + 重 cache）
    ///   OrphanThumb  → 直接刪檔
    /// StaleMtime 留給 Phase 3 fs 掃描處理（mtime 不一致就會 cache miss → re-decode）。
    /// </summary>
    public void Fix(IntegrityReport rep, CancellationToken ct)
    {
        var p = new RebuildProgress
        {
            Total = rep.OrphanDbPaths.Count + rep.MissingThumbPaths.Count + rep.OrphanThumbKeys.Count,
        };
        _tracker.Update(p);
        int done = 0;

        // OrphanDb：刪 DB row + 對應 thumb
        _meta.RunInTransaction(() =>
        {
            foreach (var path in rep.OrphanDbPaths)
            {
                ct.ThrowIfCancellationRequested();
                var m = _meta.Get(path);
                if (m?.CoverThumbKey != null) _thumbs.Delete(m.CoverThumbKey);
                _meta.Delete(path);
                done++;
                p.Done = done;
                p.CurrentPath = $"刪失效紀錄: {path}";
                _tracker.Update(p);
            }
        });

        // MissingThumb：清 cover_thumb_key（DB 與 thumbs/ 的不一致校正成「已淘汰」狀態）
        _meta.RunInTransaction(() =>
        {
            foreach (var path in rep.MissingThumbPaths)
            {
                ct.ThrowIfCancellationRequested();
                var m = _meta.Get(path);
                if (m == null) continue;
                m.CoverThumbKey = null;
                m.CoverWidth = null;
                m.CoverHeight = null;
                _meta.Upsert(m);
                done++;
                p.Done = done;
                p.CurrentPath = $"清空缺失縮圖紀錄: {path}";
                _tracker.Update(p);
            }
        });

        // OrphanThumb：直接刪檔
        foreach (var key in rep.OrphanThumbKeys)
        {
            ct.ThrowIfCancellationRequested();
            _thumbs.Delete(key);
            done++;
            p.Done = done;
            p.CurrentPath = $"刪孤兒縮圖: {key}";
            _tracker.Update(p);
        }
    }
}

using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 重建 cache 服務：
/// - FullRebuild  : Clear DB + thumbs/ → 從 RootDirectory 遞迴掃描，逐項 GetCoverAsync (cache miss 路徑寫 cache)
/// - UpdateIndex  : Smart sync — 整性掃描清孤兒（OrphanDb / OrphanThumb）→ 從 RootDirectory 走 fs，
///                  cache hit 跳、cache miss 補上新檔、StaleMtime 自然觸發 re-decode
/// - ResumeScan   : 不清不檢，只走 fs cache-miss-aware（中斷後接續用）
///
/// 每處理一筆 asset 呼叫 progress.Report，UI 端顯示進度。
/// 支援 CancellationToken 取消。
/// </summary>
public class CacheRebuildService
{
    private readonly AppConfig _config;
    private readonly CoverManager _coverManager;
    private readonly IAssetMetaStore _metaStore;
    private readonly IThumbStore _thumbStore;
    private readonly CacheIntegrityChecker _checker;
    private readonly CacheWriter? _cacheWriter;   // optional：用來抓 eviction 計數
    private readonly RebuildProgressTracker _tracker;
    private static readonly string[] AllowedExtensions =
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".zip", ".cbz", ".pdf" };

    public class Progress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public string CurrentPath { get; set; } = "";
    }

    /// <summary>UpdateIndexAsync 完成後的計數摘要，用於 UI Modal 顯示。</summary>
    public class UpdateIndexSummary
    {
        /// <summary>新增 — DB 沒紀錄的檔案，已 decode + 寫 cache。</summary>
        public int AddedCount { get; set; }
        /// <summary>更新 — mtime 不一致，已重新 decode + 覆寫 cache。</summary>
        public int UpdatedCount { get; set; }
        /// <summary>刪除 — source 已不存在的舊紀錄。</summary>
        public int RemovedCount { get; set; }
        /// <summary>淘汰 — LRU 因超過 cache 上限而清掉的 thumb。</summary>
        public int EvictedCount { get; set; }
        /// <summary>Cache 用量目前 / 上限（bytes）。</summary>
        public long CacheUsedBytes { get; set; }
        public long CacheLimitBytes { get; set; }
        /// <summary>是否被中斷（取消）。中斷時計數仍有效但不完整。</summary>
        public bool Cancelled { get; set; }
    }

    public CacheRebuildService(AppConfig config, CoverManager coverManager,
        IAssetMetaStore metaStore, IThumbStore thumbStore,
        CacheIntegrityChecker checker, RebuildProgressTracker tracker,
        CacheWriter? cacheWriter = null)
    {
        _config = config;
        _coverManager = coverManager;
        _metaStore = metaStore;
        _thumbStore = thumbStore;
        _checker = checker;
        _tracker = tracker;
        _cacheWriter = cacheWriter;
    }

    /// <summary>完整重建：清空 DB + thumbs/ → 從 RootDirectory 重新掃描。可被取消，
    /// 取消後 cache 部分已寫入；下次呼叫 ResumeScanAsync 可繼續未完成的部分。</summary>
    public Task FullRebuildAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return Task.CompletedTask;

        var ct = _tracker.Start("完整重建中");
        return Task.Run(async () =>
        {
            try
            {
                _metaStore.Clear();
                _thumbStore.Clear();
                await ScanAsync(_config.RootDirectory, ct);
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        });
    }

    /// <summary>更新索引：對齊 DB 與漫畫庫。
    /// Phase 1 整性掃描：找 OrphanDb / OrphanThumb
    /// Phase 2 修復：刪掉孤兒紀錄與孤兒縮圖
    /// Phase 3 fs 掃描：cache-miss-aware，補上新檔、stale mtime 自然 re-decode
    /// 回傳 summary 給 UI 顯示「新增/更新/刪除/淘汰」計數。
    /// </summary>
    public Task<UpdateIndexSummary?> UpdateIndexAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return Task.FromResult<UpdateIndexSummary?>(null);

        var ct = _tracker.Start("更新索引中");
        long evictedBefore = _cacheWriter?.TotalEvictedCount ?? 0;
        return Task.Run<UpdateIndexSummary?>(async () =>
        {
            var summary = new UpdateIndexSummary();
            try
            {
                // Phase 1+2：整性檢查 + 清孤兒
                var rep = _checker.Scan(ct);
                _checker.Fix(rep, ct);
                summary.RemovedCount = rep.OrphanDbRecords;

                // Phase 3：fs 掃描（cache hit skip / miss decode）
                await ScanAsync(_config.RootDirectory, ct, summary);

                // 等 CacheWriter drain 完才能準確算 evicted（背景 queue 可能還沒處理完）
                // 簡化：給 worker 一點時間處理剩餘 queue（最多 1s），抓最終 eviction delta
                await Task.Delay(500, ct);
                // 主動 trim — 這是必要的：使用者可能改小了 cache 上限但沒新寫入觸發
                // CacheWriter 的 batch eviction 機制 → 不主動清的話會看到 cache 超過 limit 卻沒淘汰
                _cacheWriter?.TrimToLimit();
                summary.EvictedCount = (int)((_cacheWriter?.TotalEvictedCount ?? 0) - evictedBefore);
                summary.CacheUsedBytes = _thumbStore.TotalSizeBytes();
                summary.CacheLimitBytes = _config.ThumbCacheMaxBytes;

                _tracker.NotifyEnd(cancelled: false);
                return summary;
            }
            catch (OperationCanceledException)
            {
                summary.Cancelled = true;
                summary.EvictedCount = (int)((_cacheWriter?.TotalEvictedCount ?? 0) - evictedBefore);
                summary.CacheUsedBytes = _thumbStore.TotalSizeBytes();
                summary.CacheLimitBytes = _config.ThumbCacheMaxBytes;
                _tracker.NotifyEnd(cancelled: true);
                return summary;
            }
        }, ct);
    }

    /// <summary>含縮圖紀錄的 row 數（用於 confirm 對話框顯示「將清除 N 張」）。</summary>
    public long GetThumbCount() => _metaStore.TotalCachedCount();

    /// <summary>asset 表總筆數（用於完整清除 confirm 對話框）。</summary>
    public long GetAssetCount() => _metaStore.TotalAssetCount();

    /// <summary>清除縮圖快取：刪 thumbs/ 全部檔 + UPDATE 把所有 row 的 thumb 欄位設 null。
    /// catalog（path/name/mtime）保留 — 下次 UpdateIndex 走 cache miss 自然重建。
    /// </summary>
    public Task ClearThumbCacheAsync()
    {
        var ct = _tracker.Start("清除縮圖快取中");
        return Task.Run(() =>
        {
            try
            {
                _thumbStore.Clear();
                _metaStore.ClearThumbFields();
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        }, ct);
    }

    /// <summary>完整清除：DELETE FROM asset + 刪 thumbs/。catalog + 縮圖都清掉，回到全新狀態。
    /// 不掃描 — 使用者自己決定何時跑 UpdateIndex 重建索引。
    /// </summary>
    public Task ClearAllAsync()
    {
        var ct = _tracker.Start("完整清除中");
        return Task.Run(() =>
        {
            try
            {
                _metaStore.Clear();
                _thumbStore.Clear();
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        }, ct);
    }

    /// <summary>掃描更新：不清空 DB / thumbs，從 RootDirectory 掃描；已 cache 的會 hit 直接 skip
    /// 未 cache 的會寫入。適合「繼續未完成的重建」與一般增量同步。</summary>
    public Task ResumeScanAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return Task.CompletedTask;

        var ct = _tracker.Start("掃描更新中");
        return Task.Run(async () =>
        {
            try
            {
                await ScanAsync(_config.RootDirectory, ct);
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                _tracker.NotifyEnd(cancelled: true);
            }
        });
    }

    /// <summary>cache 狀態：Hit = skip / MissNew = 新檔 / MissStale = mtime 不一致需重 decode。</summary>
    private enum CacheState { Hit, MissNew, MissStale }

    private async Task ScanAsync(string root, CancellationToken ct, UpdateIndexSummary? summary = null)
    {
        // Phase 1: 列出所有候選 path（folder + 支援副檔名）
        var paths = new List<string>();
        try
        {
            CollectPaths(root, paths, ct);
        }
        catch (OperationCanceledException) { throw; }

        var p = new Progress { Total = paths.Count };
        _tracker.Update(p);

        // Phase 2: cache hit 直接 skip（快速 DB 查詢），cache miss 才真的 decode + 寫 cache
        for (int i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = paths[i];

            var state = CheckCacheState(path);
            if (state != CacheState.Hit)
            {
                try
                {
                    using var img = await _coverManager.GetCoverAsync(path, CoverManager.ThumbWidth);
                }
                catch { /* 單筆失敗不中斷整體 */ }

                if (summary != null)
                {
                    if (state == CacheState.MissNew) summary.AddedCount++;
                    else if (state == CacheState.MissStale) summary.UpdatedCount++;
                }
            }

            p.Done = i + 1;
            p.CurrentPath = path;
            _tracker.Update(p);
        }
    }

    /// <summary>
    /// DB 已記錄 + mtime 一致 → Hit（skip）。
    /// 注意：thumb 檔不見不算 miss — 那可能是 LRU 淘汰過的，重 decode 只會造成 thrashing。
    /// 真要看那張 cover，使用者點開時 GetCoverAsync 自己會 decode + 重新 cache。
    /// </summary>
    private CacheState CheckCacheState(string path)
    {
        try
        {
            var meta = _metaStore.Get(path);
            if (meta == null) return CacheState.MissNew;

            long currentMtime;
            try
            {
                DateTime t = Directory.Exists(path)
                    ? Directory.GetLastWriteTimeUtc(path)
                    : File.GetLastWriteTimeUtc(path);
                currentMtime = new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            catch { return CacheState.MissNew; }

            return meta.Mtime == currentMtime ? CacheState.Hit : CacheState.MissStale;
        }
        catch
        {
            return CacheState.MissNew;
        }
    }

    private static void CollectPaths(string folder, List<string> output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // 子目錄一定收
            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                output.Add(sub);
                CollectPaths(sub, output, ct);
            }
            // 檔案：只收支援副檔名
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (AllowedExtensions.Contains(ext)) output.Add(f);
            }
        }
        catch (UnauthorizedAccessException) { /* 跳過存取拒絕的目錄 */ }
        catch (DirectoryNotFoundException) { /* 已被刪除 */ }
    }
}

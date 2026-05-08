using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 比對 RootDirectory 檔案系統與 asset 索引，產生 ReconcileReport。
///
/// 三類發現：
///  1. PathChange   — DB 有 / FS 找不到舊 path / 但兩階段配對找到唯一新位置 → 安全更新 path
///  2. Orphan       — DB 有 / FS 找不到舊 path / 兩階段都找不到 → 候選刪除（人工確認）
///  3. Ambiguous    — DB 有 / FS 找不到舊 path / 配對 key 多重命中 → 跳過讓人工解
///
/// 「FS 有但 DB 沒有」的新檔案不在此 service 範圍 — 由 ResumeScanAsync 處理。
///
/// === 兩階段配對 ===
///  Pass 1: (lower-case basename, file_size) — 抓「同檔名搬到別目錄」
///  Pass 2: (file_size, mtime)                — 抓「rename」（mtime rename 不變）
///
/// === 關鍵設計 ===
/// - Apply 跟 Analyze 完全分離：Analyze 不寫 DB，使用者確認後才 ApplyPathChanges + DeleteOrphans。
/// - asset.id 在 PathChange 時保留 → asset_meta / asset_thumb / asset_tag 透過 FK 自動接續。
/// - folder file_size 在 catalog 是 NULL，FS 端也視為 0 → key 一致。
/// - mtime 第二輪：file 的 mtime rename 後不變；folder mtime rename 後也不變（NTFS 是父目錄 mtime 才變）。
/// </summary>
public class CatalogReconcileService
{
    private readonly AppConfig _config;
    private readonly IAssetMetaStore _metaStore;

    public CatalogReconcileService(AppConfig config, IAssetMetaStore metaStore)
    {
        _config = config;
        _metaStore = metaStore;
    }

    /// <summary>掃 RootDirectory，比對 DB，回 report。不寫 DB。</summary>
    public Task<ReconcileReport> AnalyzeAsync(IProgress<(int done, int total)>? progress = null,
                                               CancellationToken ct = default)
    {
        return Task.Run(() => Analyze(progress, ct), ct);
    }

    public ReconcileReport Analyze(IProgress<(int done, int total)>? progress = null,
                                   CancellationToken ct = default)
    {
        var report = new ReconcileReport();

        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory))
            return report;

        // 1. 列 DB 全部 asset
        var dbEntries = _metaStore.ListAssetEntries();
        report.DbAssetsScanned = dbEntries.Count;

        // 2. 區分 in-FS / missing
        var dbInFsPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dbMissing = new List<AssetCatalogEntry>();
        foreach (var e in dbEntries)
        {
            ct.ThrowIfCancellationRequested();
            bool exists = e.IsFolder ? Directory.Exists(e.Path) : File.Exists(e.Path);
            if (exists) dbInFsPaths.Add(NormalizePath(e.Path));
            else dbMissing.Add(e);
        }

        // 3. 列 FS 所有 work-role item
        var detector = new FolderRoleDetector(_config.RootDirectory);
        var fsWorks = new List<FsWork>();
        ScanFsRecursive(_config.RootDirectory, detector, fsWorks, ct);
        report.FsWorksScanned = fsWorks.Count;

        // 4. 標記 FS 哪些不在 DB → 給 PathChange / Orphan 配對
        var fsNotInDb = fsWorks.Where(w => !dbInFsPaths.Contains(NormalizePath(w.Path))).ToList();

        // 5a. 第一輪 key：(lower-basename, size) — 抓「同檔名搬到別目錄」
        var byNameSize = new Dictionary<(string name, long size), List<FsWork>>();
        foreach (var fs in fsNotInDb)
        {
            var key = (Path.GetFileName(fs.Path).ToLowerInvariant(), fs.Size);
            if (!byNameSize.TryGetValue(key, out var list))
            {
                list = new List<FsWork>();
                byNameSize[key] = list;
            }
            list.Add(fs);
        }

        // 5b. 第二輪 key：(size, mtime) — 抓「rename」
        // 只 index 還沒被第一輪 consume 的；但因為消費跟建 index 是同一 pass，這裡先全建，
        // 配對時靠 consumed set 過濾
        var bySizeMtime = new Dictionary<(long size, long mtime), List<FsWork>>();
        foreach (var fs in fsNotInDb)
        {
            var key = (fs.Size, fs.Mtime);
            if (!bySizeMtime.TryGetValue(key, out var list))
            {
                list = new List<FsWork>();
                bySizeMtime[key] = list;
            }
            list.Add(fs);
        }

        var consumedFsPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 6. 對 dbMissing 找配對：先試 (name, size)；不中再試 (size, mtime)
        int processed = 0;
        foreach (var miss in dbMissing)
        {
            ct.ThrowIfCancellationRequested();
            var size = miss.FileSize ?? 0;

            // Pass 1: (name, size)
            var key1 = (miss.Name.ToLowerInvariant(), size);
            if (byNameSize.TryGetValue(key1, out var c1) &&
                c1.Where(f => !consumedFsPaths.Contains(NormalizePath(f.Path))).ToList() is { Count: > 0 } available1)
            {
                if (available1.Count == 1)
                {
                    var newPath = available1[0].Path;
                    if (!string.Equals(NormalizePath(newPath), NormalizePath(miss.Path), StringComparison.OrdinalIgnoreCase))
                    {
                        report.PathChanges.Add(new PathChange(miss.Id, miss.Path, newPath, size));
                        consumedFsPaths.Add(NormalizePath(newPath));
                    }
                    processed++; progress?.Report((processed, dbMissing.Count));
                    continue;
                }
                else
                {
                    report.Ambiguous.Add(new AmbiguousMatch(miss.Id, miss.Path, size, available1.Select(f => f.Path).ToList()));
                    processed++; progress?.Report((processed, dbMissing.Count));
                    continue;
                }
            }

            // Pass 2: (size, mtime) — rename 偵測
            var key2 = (size, miss.Mtime);
            if (bySizeMtime.TryGetValue(key2, out var c2) &&
                c2.Where(f => !consumedFsPaths.Contains(NormalizePath(f.Path))).ToList() is { Count: > 0 } available2)
            {
                if (available2.Count == 1)
                {
                    var newPath = available2[0].Path;
                    if (!string.Equals(NormalizePath(newPath), NormalizePath(miss.Path), StringComparison.OrdinalIgnoreCase))
                    {
                        report.PathChanges.Add(new PathChange(miss.Id, miss.Path, newPath, size));
                        consumedFsPaths.Add(NormalizePath(newPath));
                    }
                    processed++; progress?.Report((processed, dbMissing.Count));
                    continue;
                }
                else
                {
                    report.Ambiguous.Add(new AmbiguousMatch(miss.Id, miss.Path, size, available2.Select(f => f.Path).ToList()));
                    processed++; progress?.Report((processed, dbMissing.Count));
                    continue;
                }
            }

            // 兩輪都沒中 → orphan
            report.Orphans.Add(new OrphanedAsset(miss.Id, miss.Path, miss.FileSize));
            processed++; progress?.Report((processed, dbMissing.Count));
        }

        // 7. Post-process: cascade — orphan 中 path 是 PathChange 父層子孫的，跟著父層搬。
        // 解決：legacy DB 殘留（舊 detector 把 chapter 當 Work 留下的 entry）+ 父層 rename 時
        // 子目錄自動跟搬，保留 metadata。新路徑必須在 FS 存在才 cascade。
        var primaryChanges = report.PathChanges.ToList();
        var keptOrphans = new List<OrphanedAsset>();
        foreach (var orphan in report.Orphans)
        {
            var match = FindBestAncestorChange(orphan.Path, primaryChanges);
            if (match != null)
            {
                var newPath = RewritePathPrefix(orphan.Path, match.OldPath, match.NewPath);
                bool fsExists = File.Exists(newPath) || Directory.Exists(newPath);
                if (fsExists)
                {
                    report.PathChanges.Add(new PathChange(orphan.AssetId, orphan.Path, newPath, orphan.Size ?? 0));
                    continue;
                }
            }
            keptOrphans.Add(orphan);
        }
        report.Orphans.Clear();
        report.Orphans.AddRange(keptOrphans);

        return report;
    }

    private record FsWork(string Path, long Size, long Mtime, bool IsFolder);

    /// <summary>找 changes 中最長的、與 path 是祖先關係的。多重命中取最長 prefix（最近祖先）。</summary>
    private static PathChange? FindBestAncestorChange(string path, IReadOnlyList<PathChange> changes)
    {
        PathChange? best = null;
        int bestLen = -1;
        foreach (var c in changes)
        {
            if (IsDescendant(path, c.OldPath) && c.OldPath.Length > bestLen)
            {
                best = c;
                bestLen = c.OldPath.Length;
            }
        }
        return best;
    }

    private static bool IsDescendant(string descendantPath, string ancestorPath)
    {
        var prefix = ancestorPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        return descendantPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string RewritePathPrefix(string fullPath, string oldPrefix, string newPrefix)
    {
        var oldNorm = oldPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var newNorm = newPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return newNorm + fullPath.Substring(oldNorm.Length);
    }

    /// <summary>套用使用者確認的 path 變更。</summary>
    public int ApplyPathChanges(IEnumerable<PathChange> changes)
    {
        int n = 0;
        foreach (var c in changes)
        {
            _metaStore.UpdateAssetPath(c.AssetId, c.NewPath);
            n++;
        }
        return n;
    }

    /// <summary>刪除使用者確認的 orphan asset 記錄。</summary>
    public int DeleteOrphans(IEnumerable<long> assetIds)
    {
        int n = 0;
        foreach (var id in assetIds)
        {
            _metaStore.DeleteAssetById(id);
            n++;
        }
        return n;
    }

    // ── 內部：FS 遞迴掃描 ───────────────────────────────────────────────────

    private static void ScanFsRecursive(string root, FolderRoleDetector detector,
                                         List<FsWork> output,
                                         CancellationToken ct)
    {
        try
        {
            // 檔案：判斷 archive 為 work
            foreach (var f in Directory.EnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                var role = detector.Detect(f);
                if (role == FolderRole.Work)
                {
                    long size = 0;
                    long mtime = 0;
                    try
                    {
                        var fi = new FileInfo(f);
                        size = fi.Length;
                        mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds();
                    }
                    catch { }
                    output.Add(new FsWork(f, size, mtime, false));
                }
            }

            foreach (var d in Directory.EnumerateDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                var role = detector.Detect(d);
                if (role == FolderRole.Work)
                {
                    // CoverManager 寫 catalog 時 folder.file_size = NULL（只有 file 才有 size）
                    // 為了配對 key 一致，FS 端 folder size 也視為 0
                    long mtime = 0;
                    try
                    {
                        mtime = new DateTimeOffset(new DirectoryInfo(d).LastWriteTimeUtc).ToUnixTimeSeconds();
                    }
                    catch { }
                    output.Add(new FsWork(d, 0L, mtime, true));
                    // 不再往下掃 — work folder 內部是章節 / pages
                }
                else
                {
                    // Library / Category / Author → 繼續往下
                    ScanFsRecursive(d, detector, output, ct);
                }
            }
        }
        catch (UnauthorizedAccessException) { /* skip */ }
        catch (DirectoryNotFoundException) { /* skip */ }
    }

    private static string NormalizePath(string p) =>
        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

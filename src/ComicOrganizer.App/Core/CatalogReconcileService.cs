using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 比對 RootDirectory 檔案系統與 asset 索引，產生 ReconcileReport。
///
/// 三類發現：
///  1. PathChange   — DB 有 / FS 找不到舊 path / 但 (basename + size) 在 FS 唯一配對 → 安全更新 path
///  2. Orphan       — DB 有 / FS 找不到舊 path / 也無唯一配對 → 候選刪除（人工確認）
///  3. Ambiguous    — DB 有 / FS 找不到舊 path / FS 有多個同 (basename + size) 候選 → 跳過讓人工解
///
/// 「FS 有但 DB 沒有」的新檔案不在此 service 範圍 — 由 ResumeScanAsync 處理。
///
/// === 關鍵設計 ===
/// - Apply 跟 Analyze 完全分離：Analyze 不寫 DB，使用者確認後才 ApplyPathChanges + DeleteOrphans。
/// - 配對 key = (lower-case basename, file_size)。size=0 / NULL 也照配（zip vs folder 都覆蓋）。
/// - asset.id 在 PathChange 時保留 → asset_meta / asset_thumb / asset_tag 透過 FK 自動接續。
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
        var fsWorks = new List<(string path, long size, bool isFolder)>();
        ScanFsRecursive(_config.RootDirectory, detector, fsWorks, ct);
        report.FsWorksScanned = fsWorks.Count;

        // 4. 標記 FS 哪些不在 DB → 給 PathChange / Orphan 配對
        var fsNotInDb = fsWorks.Where(w => !dbInFsPaths.Contains(NormalizePath(w.path))).ToList();

        // 5. 把 fsNotInDb 索引成 (key → list of paths)，key = (lower-basename, size)
        var fsByKey = new Dictionary<(string name, long size), List<string>>();
        foreach (var (path, size, _) in fsNotInDb)
        {
            var key = (Path.GetFileName(path).ToLowerInvariant(), size);
            if (!fsByKey.TryGetValue(key, out var list))
            {
                list = new List<string>();
                fsByKey[key] = list;
            }
            list.Add(path);
        }

        // 6. 對 dbMissing 找配對
        int processed = 0;
        foreach (var miss in dbMissing)
        {
            ct.ThrowIfCancellationRequested();
            var size = miss.FileSize ?? 0;
            var key = (miss.Name.ToLowerInvariant(), size);

            if (fsByKey.TryGetValue(key, out var candidates) && candidates.Count > 0)
            {
                if (candidates.Count == 1)
                {
                    var newPath = candidates[0];
                    // 排除「同路徑」誤判（理論上 dbInFsPaths 已過濾，雙保險）
                    if (!string.Equals(NormalizePath(newPath), NormalizePath(miss.Path), StringComparison.OrdinalIgnoreCase))
                    {
                        report.PathChanges.Add(new PathChange(miss.Id, miss.Path, newPath, size));
                    }
                    // consumed → 移除避免下個 dbMissing 又拿到同樣 candidate
                    fsByKey.Remove(key);
                }
                else
                {
                    report.Ambiguous.Add(new AmbiguousMatch(miss.Id, miss.Path, size, candidates.ToList()));
                    // 多重配對保留在 fsByKey，等使用者人工解（後續 Apply 不動）
                }
            }
            else
            {
                report.Orphans.Add(new OrphanedAsset(miss.Id, miss.Path, miss.FileSize));
            }

            processed++;
            progress?.Report((processed, dbMissing.Count));
        }

        return report;
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
                                         List<(string path, long size, bool isFolder)> output,
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
                    try { size = new FileInfo(f).Length; } catch { }
                    output.Add((f, size, false));
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
                    output.Add((d, 0L, true));
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

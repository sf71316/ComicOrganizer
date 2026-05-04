namespace ComicOrganizer.App.Core;

/// <summary>
/// 比對檔案系統與索引（asset 表）的差異結果。Apply 之前唯讀，使用者確認後才寫 DB。
/// </summary>
public class ReconcileReport
{
    /// <summary>路徑變更（同 basename + size 的 asset 在新位置找到）— 唯一配對才進這列表。</summary>
    public List<PathChange> PathChanges { get; init; } = new();

    /// <summary>找不到對應 FS 檔案、也找不到唯一配對的 asset 記錄 — 候選刪除。</summary>
    public List<OrphanedAsset> Orphans { get; init; } = new();

    /// <summary>多重配對（多個 FS 檔案有同樣 basename+size，無法自動決定）— 跳過，列出來給使用者參考。</summary>
    public List<AmbiguousMatch> Ambiguous { get; init; } = new();

    /// <summary>檔案系統累積掃描幾筆 work（含 in-DB / not-in-DB）— 給「總共看了多少」統計。</summary>
    public int FsWorksScanned { get; set; }

    /// <summary>DB 累積看了幾筆 asset。</summary>
    public int DbAssetsScanned { get; set; }

    public int Total => PathChanges.Count + Orphans.Count + Ambiguous.Count;
    public bool IsEmpty => Total == 0;
}

/// <summary>asset 路徑變了，但 (basename, size) 在新位置唯一配對 → 可安全更新 path 保留 metadata。</summary>
public record PathChange(long AssetId, string OldPath, string NewPath, long Size);

/// <summary>DB 有但 FS 找不到的 asset，且沒有唯一配對 → 真孤兒。</summary>
public record OrphanedAsset(long AssetId, string Path, long? Size);

/// <summary>多個 FS 檔案有相同 (basename, size) → 無法自動決定要 update 到哪個。</summary>
public record AmbiguousMatch(long AssetId, string OldPath, long Size, IReadOnlyList<string> CandidateFsPaths);

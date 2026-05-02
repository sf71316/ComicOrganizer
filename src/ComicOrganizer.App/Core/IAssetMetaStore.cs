namespace ComicOrganizer.App.Core;

/// <summary>
/// Cache metadata 持久化介面。SQLite-backed 的 production 實作見 SqliteAssetMetaStore。
/// 操作主要由 CoverManager (讀) + CacheWriter (寫) 使用。
/// </summary>
public interface IAssetMetaStore : IDisposable
{
    AssetMeta? Get(string path);
    void Upsert(AssetMeta meta);
    void TouchLastAccessed(string path, long now);

    IReadOnlyList<AssetMeta> ListChildren(string folderPath);

    /// <summary>列舉所有 asset（整性檢查用，可能很大 → 用 IEnumerable 邊讀邊處理）。</summary>
    IEnumerable<AssetMeta> ListAll();

    /// <summary>取出 lastAccessed 最舊的 N 筆（用於 LRU eviction）。</summary>
    IReadOnlyList<AssetMeta> ListLruVictims(int count);

    void Delete(string path);

    /// <summary>刪除 path 下所有 asset（含自己）。部分重建用。</summary>
    void DeleteSubtree(string folderPath);

    /// <summary>清空整個 asset 表。完整清除用。</summary>
    void Clear();

    /// <summary>清空所有 row 的縮圖欄位（cover_thumb_key/width/height），保留 row 本身。
    /// 「清除縮圖快取」用 — 刪檔不刪 catalog。</summary>
    void ClearThumbFields();

    long TotalAssetCount();

    /// <summary>含縮圖紀錄的 row 數（cover_thumb_key IS NOT NULL）。用於 confirm 對話框。</summary>
    long TotalCachedCount();

    /// <summary>cache asset 加總 file_size（folder 不計），用於 LRU 大小判斷。</summary>
    long TotalThumbBytes();

    /// <summary>
    /// 批次寫入：action 內呼叫多次 Upsert/Touch/Delete 包在單一 SQLite transaction。
    /// 持鎖整段 action 期間，避免多執行緒對同一 connection 開 nested transaction。
    /// </summary>
    void RunInTransaction(Action action);

    /// <summary>取通用 key/value (app_state 表)。沒值時回 null。</summary>
    string? GetState(string key);

    /// <summary>寫通用 key/value。null value 視為刪除。</summary>
    void SetState(string key, string? value);
}

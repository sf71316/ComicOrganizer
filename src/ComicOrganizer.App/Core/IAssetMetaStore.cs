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

    /// <summary>取出 lastAccessed 最舊的 N 筆（用於 LRU eviction）。</summary>
    IReadOnlyList<AssetMeta> ListLruVictims(int count);

    void Delete(string path);

    /// <summary>刪除 path 下所有 asset（含自己）。部分重建用。</summary>
    void DeleteSubtree(string folderPath);

    /// <summary>清空整個 asset 表。完整重建用。</summary>
    void Clear();

    long TotalAssetCount();

    /// <summary>cache asset 加總 file_size（folder 不計），用於 LRU 大小判斷。</summary>
    long TotalThumbBytes();

    /// <summary>
    /// 批次寫入：action 內呼叫多次 Upsert/Touch/Delete 包在單一 SQLite transaction。
    /// 持鎖整段 action 期間，避免多執行緒對同一 connection 開 nested transaction。
    /// </summary>
    void RunInTransaction(Action action);
}

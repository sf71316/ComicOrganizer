namespace ComicOrganizer.App.Core;

/// <summary>
/// Cache metadata 持久化介面。SQLite-backed 的 production 實作見 SqliteAssetMetaStore。
///
/// === 資料分層（schema v2）===
/// 1. catalog (asset 表)         — 目錄結構，永久保留
/// 2. thumb cache (asset_thumb)  — 縮圖快取，可清/可 LRU 淘汰
/// 3. metadata (asset_meta)      — 漫畫 metadata，可獨立清除/重 parse
/// 4. author / author_alias      — 作者主表 + 多語別名
/// 5. tag / asset_tag            — booru-style 多對多標籤
///
/// AssetMeta DTO 是 catalog + thumb 的 JOINed 視圖，舊 caller 不需重寫。
/// 新功能直接呼叫 GetMetadata / UpsertMetadata / FindAuthor… 等專屬 API。
/// </summary>
public interface IAssetMetaStore : IDisposable
{
    // ===== Catalog + Thumb (joined view) =====

    /// <summary>取 path 對應的 catalog + thumb 合併視圖。沒紀錄回 null。</summary>
    AssetMeta? Get(string path);

    /// <summary>取 path 對應 asset.id（metadata write 需要 FK target）。沒紀錄回 null。</summary>
    long? GetAssetId(string path);

    /// <summary>寫入或更新 catalog 與 thumb（依 meta 的欄位是否有值決定 thumb row 是否寫入）。</summary>
    void Upsert(AssetMeta meta);

    /// <summary>只寫 catalog (asset 表)，不動 asset_thumb。Author 資料夾無 cover 時用。</summary>
    void UpsertCatalogOnly(AssetMeta meta);

    /// <summary>cache hit 路徑用：更新 last_accessed（asset_thumb 表）。</summary>
    void TouchLastAccessed(string path, long now);

    IReadOnlyList<AssetMeta> ListChildren(string folderPath);

    /// <summary>列舉所有 asset（整性檢查用，可能很大 → 用 IEnumerable 邊讀邊處理）。</summary>
    IEnumerable<AssetMeta> ListAll();

    /// <summary>取出 lastAccessed 最舊的 N 筆（用於 LRU eviction）。只 JOIN 有 thumb 的紀錄。</summary>
    IReadOnlyList<AssetMeta> ListLruVictims(int count);

    void Delete(string path);

    /// <summary>刪除 path 下所有 asset（含自己）。部分重建用。</summary>
    void DeleteSubtree(string folderPath);

    /// <summary>清空整個 asset 表（catalog + 連帶 thumb / metadata / tag 關聯）。完整清除用。</summary>
    void Clear();

    /// <summary>清空所有 thumb 紀錄（asset_thumb 全 DELETE）。catalog / metadata 保留。</summary>
    void ClearThumbFields();

    long TotalAssetCount();

    /// <summary>含縮圖紀錄的 row 數（asset_thumb COUNT）。用於 confirm 對話框。</summary>
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

    // ===== Metadata (asset_meta) =====

    /// <summary>取 asset 對應的漫畫 metadata（沒則回 null）。</summary>
    AssetMetadata? GetMetadata(long assetId);

    /// <summary>顯示用合併視圖：JOIN asset + asset_meta + author。沒 asset 紀錄回 null。</summary>
    AssetMetadataView? GetMetadataView(string path);

    /// <summary>寫入或更新 metadata（PK = asset_id，1:1 關係）。</summary>
    void UpsertMetadata(AssetMetadata meta);

    /// <summary>刪除 asset 的 metadata。catalog/thumb 保留。</summary>
    void DeleteMetadata(long assetId);

    /// <summary>清空所有 metadata（asset_meta 表 DELETE）。catalog 保留，將來重 parse 重新填入。</summary>
    void ClearAllMetadata();

    // ===== Author =====

    /// <summary>依 main_name 找作者；沒找到回 null。</summary>
    Author? FindAuthorByMainName(string mainName);

    /// <summary>取或建作者（main_name 主鍵 reuse）。folder_path / learned_at 在 row 不存在時才寫入。</summary>
    long UpsertAuthor(string mainName, string? folderPath = null);

    /// <summary>新增作者別名（已存在則 ignore）。</summary>
    void AddAuthorAlias(long authorId, string aliasName, string? lang = null);

    IReadOnlyList<AuthorAlias> ListAuthorAliases(long authorId);

    /// <summary>從 path 解析 author_id：先查 Work 的 asset_meta.author_id；fallback Author folder 的 author.folder_path。</summary>
    long? ResolveAuthorIdByPath(string path);

    /// <summary>聚合查詢：作者 + 別名 + 閱讀統計 + top tags + recent / all works。沒紀錄回 null。</summary>
    AuthorInfo? GetAuthorInfo(long authorId);

    /// <summary>
    /// 紀錄一次「讀過」：read_count++ / last_read=now / Unread→Reading。
    /// 沒 asset_meta 紀錄（如 Author / Library folder）no-op。
    /// </summary>
    void RecordRead(string path);

    /// <summary>手動設定 read_status（標已完成 / 放棄 / 重置未讀）。沒 asset_meta no-op。</summary>
    void SetReadStatus(string path, ReadStatus status);

    /// <summary>取 path 對應的 read_status（沒 asset_meta 紀錄回 null）。給卡片 icon / menu disable 用。</summary>
    ReadStatus? GetReadStatus(string path);

    // ===== Tag =====

    /// <summary>取或建 tag（name 主鍵 reuse）。</summary>
    long UpsertTag(string name, string? category = null);

    /// <summary>關聯 asset 與 tag（多對多）。已存在則 ignore。</summary>
    void AssociateTag(long assetId, long tagId);

    /// <summary>列出 asset 的所有 tag。</summary>
    IReadOnlyList<Tag> ListTagsForAsset(long assetId);
}

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

    /// <summary>取作品詳細資訊（catalog + metadata + author + tags）。沒 asset 紀錄回 null。</summary>
    WorkDetail? GetWorkDetail(string path);

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

    /// <summary>取 path 對應的 last_page（內建 reader 上次閱讀頁碼，0-based）。沒紀錄回 null。</summary>
    int? GetLastPage(string path);

    /// <summary>更新 path 對應的 last_page。沒 asset_meta no-op；有就只更新該欄位。</summary>
    void SetLastPage(string path, int pageIndex);

    // ===== 閱讀記錄重置（read_status / read_count / last_read / last_page 一次清掉）=====

    /// <summary>重置單一作品的閱讀記錄。metadata（title/author/tags 等）保留。</summary>
    void ResetReadingRecord(string path);

    /// <summary>重置某作者底下所有作品的閱讀記錄。回傳影響筆數。</summary>
    int ResetReadingRecordsForAuthor(long authorId);

    /// <summary>重置全庫所有作品的閱讀記錄。回傳影響筆數。</summary>
    int ResetAllReadingRecords();

    // ===== Tag =====

    /// <summary>取或建 tag（name 主鍵 reuse）。</summary>
    long UpsertTag(string name, string? category = null);

    /// <summary>關聯 asset 與 tag（多對多）。已存在則 ignore。</summary>
    void AssociateTag(long assetId, long tagId);

    /// <summary>移除 asset 與 tag 的關聯（其他 asset 仍可保留同一 tag）。</summary>
    void DisassociateTag(long assetId, long tagId);

    /// <summary>用 tagNames 整批 reset 一個 asset 的 tag 集合（會自動 UpsertTag 沒看過的 name）。</summary>
    void SetAssetTags(long assetId, IEnumerable<string> tagNames);

    /// <summary>列出 asset 的所有 tag。</summary>
    IReadOnlyList<Tag> ListTagsForAsset(long assetId);

    // ===== Author tags（#45：作者本身也能掛 tag — 風格 / 類型描述）=====

    void AssociateAuthorTag(long authorId, long tagId);
    void DisassociateAuthorTag(long authorId, long tagId);
    void SetAuthorTags(long authorId, IEnumerable<string> tagNames);
    IReadOnlyList<Tag> ListTagsForAuthor(long authorId);

    // ===== Editing APIs (#45) =====

    /// <summary>改作者主名。新名稱已存在 → throw InvalidOperationException（caller 走 MergeAuthor）。</summary>
    void RenameAuthor(long authorId, string newMainName);

    /// <summary>把 srcId 的所有 work / 別名 / author_tag 轉移到 destId，刪除 srcId。
    /// 衝突的別名（destId 已有同 name）保留 destId 的、刪 srcId 的。</summary>
    void MergeAuthor(long srcId, long destId);

    /// <summary>更新 author.folder_path（搬庫後修正用）。</summary>
    void SetAuthorFolderPath(long authorId, string? folderPath);

    /// <summary>更新單一別名（name + lang）。</summary>
    void UpdateAuthorAlias(long aliasId, string name, string? lang);

    /// <summary>刪除單一別名。</summary>
    void DeleteAuthorAlias(long aliasId);

    /// <summary>改 work 的歸屬作者（asset_meta.author_id）。authorId=null 時清空關聯。</summary>
    void SetWorkAuthor(long assetId, long? authorId);

    /// <summary>name 前綴搜尋作者（main_name + alias 都比對）。給編輯時的 autocomplete 用。</summary>
    IReadOnlyList<Author> SearchAuthors(string prefix, int limit = 20);

    /// <summary>name 前綴搜尋 tag。給編輯時的 autocomplete 用。</summary>
    IReadOnlyList<Tag> SearchTags(string prefix, int limit = 20);

    /// <summary>列所有 tag + 使用次數（asset_count + author_count）。給 Tag 列表頁用。</summary>
    IReadOnlyList<TagUsage> ListTagsByUsage();

    /// <summary>列所有 author + work_count + alias_count + tag_count。給作者列表頁用。</summary>
    IReadOnlyList<AuthorUsage> ListAuthorsByUsage();

    /// <summary>改 tag 名稱。新名稱已存在 → throw（caller 走 MergeTag）。</summary>
    void RenameTag(long tagId, string newName);

    /// <summary>把 srcId 的所有 asset_tag / author_tag 關聯轉到 destId，刪 srcId。</summary>
    void MergeTag(long srcId, long destId);

    /// <summary>刪除 tag（cascade 移除所有 asset_tag / author_tag 關聯）。</summary>
    void DeleteTag(long tagId);

    /// <summary>更新 tag.category（character / artist / parody / genre / language / meta 等自由字串）。</summary>
    void SetTagCategory(long tagId, string? category);

    /// <summary>標記 / 取消「使用者手動編輯過」flag（保護不被 re-parse 覆寫）。</summary>
    void SetMetadataManuallyEdited(long assetId, bool edited);

    // ===== Catalog reconcile (#45 Phase 6) =====

    /// <summary>列出所有 asset 的 (id, path, name, is_folder, file_size)。給 catalog reconcile 用。</summary>
    IReadOnlyList<AssetCatalogEntry> ListAssetEntries();

    /// <summary>更新 asset.path（搬檔後修正）。其他欄位 (asset_meta / asset_thumb / asset_tag) 保留 — 走 asset.id FK。</summary>
    void UpdateAssetPath(long assetId, string newPath);

    /// <summary>依 asset.id 刪除（cascade 帶走 asset_meta / asset_thumb / asset_tag）。</summary>
    void DeleteAssetById(long assetId);
}

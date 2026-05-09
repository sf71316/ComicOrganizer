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

    /// <summary>整庫 parse 狀態統計（總筆數 / 已 parse / manually_edited）。給工具面板顯示用。</summary>
    ParseStats GetParseStats();

    // ===== External metadata source (#52) =====

    /// <summary>取得 asset 在某 source 的外部 metadata 紀錄（沒有回 null）。</summary>
    AssetExternalSource? GetExternalSource(long assetId, string source);

    /// <summary>列出 asset 所有 source 的 metadata 紀錄。給 UI「來源」分頁用。</summary>
    IReadOnlyList<AssetExternalSource> ListExternalSources(long assetId);

    /// <summary>upsert 外部 metadata 紀錄（同 (asset_id, source) 已存在會覆蓋）。</summary>
    void UpsertExternalSource(AssetExternalSource record);

    /// <summary>刪除 asset 在某 source 的紀錄（移除使用者重抓也不要的紀錄）。</summary>
    void DeleteExternalSource(long assetId, string source);

    /// <summary>用 (source, source_id) 反查已綁定到哪個 asset（給批次 dedup 避免重複拉）。</summary>
    long? FindAssetByExternalId(string source, string sourceId);

    // ===== Virtual Library queries (#48 Phase 1) =====
    // 純 metadata-driven 查詢，不依物理目錄階層，給虛擬庫 ViewMode 用。
    // 設計刻意避開 _config.RootDirectory — 為未來 multi-source 架構保留路徑：
    // 全部從 asset / asset_meta / asset_tag / asset_author 推，DB 沒「root」概念。

    /// <summary>列該作者所有作品 path（依 title 排序）。給「依作者」mode 用。</summary>
    IReadOnlyList<string> ListWorksByAuthor(long authorId);

    /// <summary>多 tag 過濾。intersect=true → AND（所有 tag 都要有）；false → OR（任一即可）。
    /// 給「依標籤」mode 多選 chip bar 用。</summary>
    IReadOnlyList<string> ListWorksByTags(IReadOnlyList<long> tagIds, bool intersect = true);

    /// <summary>列所有出現的 language 碼 + 該語言作品數。null 表示「未標記」（asset_meta.language IS NULL）。</summary>
    IReadOnlyList<LanguageBucket> ListLanguageBuckets();

    /// <summary>列特定語言作品 path。null = 取未標記的。</summary>
    IReadOnlyList<string> ListWorksByLanguage(string? language);

    // ── 閱讀軌跡（#48 Phase 1）─────────────────────────────────

    /// <summary>最近 N 次讀過（依 last_read DESC）。</summary>
    IReadOnlyList<string> ListRecentlyRead(int limit = 10);

    /// <summary>未讀完的（read_status = Reading）。依 last_read DESC。</summary>
    IReadOnlyList<string> ListReadingInProgress();

    /// <summary>最常閱讀作者 top N（依該作者所有作品 SUM(read_count) DESC）。回 (Author, TotalReads)。</summary>
    IReadOnlyList<MostReadAuthor> ListMostReadAuthors(int limit = 10);

    /// <summary>最常閱讀 tag top N（依帶該 tag 的作品 SUM(read_count) DESC）。回 (Tag, TotalReads)。</summary>
    IReadOnlyList<MostReadTag> ListMostReadTags(int limit = 10);

    // ===== Tag translations (#48 schema v6) =====

    /// <summary>取 tag 在指定語言的譯文 — 沒就回 null（caller fallback 原文）。</summary>
    string? GetTagTranslation(long tagId, string lang);

    /// <summary>批次取多個 tag 在指定語言的譯文（給 UI 大量顯示用，避免 N+1 query）。
    /// 沒譯文的 tag 不出現在結果 dict。</summary>
    IReadOnlyDictionary<long, string> GetTagTranslations(IReadOnlyList<long> tagIds, string lang);

    /// <summary>upsert tag 譯文（同 tag_id + lang 已存在會覆蓋）。
    /// source = 'ehtag' (自動翻譯庫) / 'manual' (使用者) / 'nhentai' (平台來源)。</summary>
    void UpsertTagTranslation(long tagId, string lang, string translatedName, string source);

    /// <summary>刪除 tag 在指定語言的譯文（回退用原文）。</summary>
    void DeleteTagTranslation(long tagId, string lang);

    /// <summary>用譯名搜 tag id 列表（中文 / 日文搜尋的核心）。
    /// LIKE %keyword% case-insensitive；可指定 lang 過濾，null = 任何語言。</summary>
    IReadOnlyList<long> SearchTagIdsByTranslatedName(string keyword, string? lang = null, int limit = 50);

    /// <summary>列出 tag 的所有譯文（給 Tag 管理頁編輯用）。</summary>
    IReadOnlyList<TagTranslation> ListTagTranslations(long tagId);

    /// <summary>批次依 name 反查 tag id（精準 case-insensitive 比對；給 SetAssetTags 後補譯文用）。
    /// 沒找到的 name 不出現在結果 dict。</summary>
    IReadOnlyDictionary<string, long> GetTagIdsByNames(IReadOnlyList<string> names);

    // ===== 搜尋 DSL 後端（Phase C）— 給 SearchExecutor 用 =====

    /// <summary>列指定 read_status 的所有作品（依 last_read DESC, title）。</summary>
    IReadOnlyList<string> ListWorksByReadStatus(ReadStatus status);

    /// <summary>title / filename 子字串模糊搜尋（給 DSL 純文字 token 用）。
    /// asset_meta.title 跟 asset.path basename 任一含 keyword 即命中（case-insensitive）。</summary>
    IReadOnlyList<string> SearchWorksByTitleOrFilename(string keyword, int limit = 500);

    /// <summary>列所有 asset.path（給 DSL only-negative 查詢起始集合用）。
    /// 沒 asset_meta 的也列入；依 path 排序。</summary>
    IReadOnlyList<string> ListAllAssetPaths();

    // ===== 自訂虛擬庫（#54 Phase 1）=====

    /// <summary>建一個新自訂虛擬庫，回新 id。created_at / updated_at 用 now ms。</summary>
    long CreateVirtualLibrary(string name, string dsl, string? iconSvg);

    /// <summary>更新指定 id 的虛擬庫（name / dsl / icon_svg）。updated_at 自動刷成 now ms。</summary>
    void UpdateVirtualLibrary(long id, string name, string dsl, string? iconSvg);

    /// <summary>刪除指定 id 的虛擬庫。</summary>
    void DeleteVirtualLibrary(long id);

    /// <summary>切換釘選狀態（pinned 1/0）。pinned 的會排在最上方。</summary>
    void SetVirtualLibraryPinned(long id, bool pinned);

    /// <summary>列所有虛擬庫，依 (pinned DESC, sort_order, name) 排序。</summary>
    IReadOnlyList<VirtualLibrary> ListVirtualLibraries();

    /// <summary>取單一筆 — 編輯 dialog 用。找不到回 null。</summary>
    VirtualLibrary? GetVirtualLibrary(long id);

    // ===== Reader 書籤（#51 Phase 2-B）=====

    /// <summary>新增書籤。同 (asset_id, page_index) 已存在則 throw（caller 走 DeleteBookmarkByPage 先 toggle）。回新 id。</summary>
    long AddBookmark(long assetId, int pageIndex, string? label);

    /// <summary>依 id 刪書籤。</summary>
    void DeleteBookmark(long bookmarkId);

    /// <summary>依 (asset_id, page_index) 刪書籤 — toggle 用（reader 按書籤鍵移除當頁書籤）。
    /// 沒紀錄 no-op。</summary>
    void DeleteBookmarkByPage(long assetId, int pageIndex);

    /// <summary>更新書籤 label。null = 清空 label。</summary>
    void RenameBookmark(long bookmarkId, string? label);

    /// <summary>列 asset 所有書籤，依 page_index 升序。</summary>
    IReadOnlyList<Bookmark> ListBookmarks(long assetId);

    /// <summary>判斷 asset 某頁是否已有書籤（reader UI 顯示填色 icon 用）。</summary>
    bool HasBookmarkOnPage(long assetId, int pageIndex);
}

/// <summary>tag 譯文紀錄。</summary>
public record TagTranslation(long TagId, string Lang, string Name, string? Source, long? UpdatedAt);

// ===== Virtual Library DTOs =====

/// <summary>語言桶 — null Language 表示「未標記」。</summary>
public record LanguageBucket(string? Language, int WorkCount);

/// <summary>最常閱讀作者統計。</summary>
public record MostReadAuthor(Author Author, int TotalReads);

/// <summary>最常閱讀 tag 統計。</summary>
public record MostReadTag(Tag Tag, int TotalReads);

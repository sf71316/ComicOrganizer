namespace ComicOrganizer.App.Core;

/// <summary>
/// Reader 書籤紀錄（#51 Phase 2-B）。一個 asset 一頁最多 1 筆（UNIQUE(asset_id, page_index)）。
/// PageIndex 為 0-based（與 last_page 一致）。Label 可空 — 純頁碼書籤，使用者後補命名。
/// </summary>
public record Bookmark(
    long Id,
    long AssetId,
    int PageIndex,
    string? Label,
    long CreatedAt);

namespace ComicOrganizer.App.Core;

/// <summary>
/// 自訂虛擬庫紀錄（#54）— 使用者儲存的 DSL query。
///
/// 點 tree 節點時走：
///   SearchQueryParser.Parse(Dsl) → SearchExecutor.Execute → 顯示作品
///
/// 設計刻意不解析 / 不快取結果，每次點都即時跑 — 這樣新增作品自動納入。
/// </summary>
public record VirtualLibrary(
    long Id,
    string Name,
    string Dsl,
    string? IconSvg,
    int SortOrder,
    bool Pinned,
    long CreatedAt,
    long UpdatedAt);

namespace ComicOrganizer.App.Core;

/// <summary>
/// 虛擬庫節點抽象（#48 Phase 1）。每個 ViewMode 的 tree node 都實作此介面。
///
/// 為什麼抽介面：
///   1. 解耦 GalleryControl 跟 4 種 builtin mode 的具體類型
///   2. 為未來 #54「自訂虛擬庫 (predicate-based)」預留 — 自訂 node 同樣 implement IVirtualLibraryNode
///   3. 為未來多 source / Wishlist 預留 — node 不依賴 RootDirectory
/// </summary>
public interface IVirtualLibraryNode
{
    /// <summary>tree 顯示的名稱（含計數時 caller 自己拼）。</summary>
    string DisplayName { get; }

    /// <summary>AntdUI iconSvg 名（null = 不顯示 icon）。</summary>
    string? IconSvg { get; }

    /// <summary>node 對應的 work 數量（null = 不顯示計數，例如 group header）。</summary>
    int? WorkCount { get; }

    /// <summary>caller 點此 node 時呼叫 — 回傳該 node 對應的所有 work path（依 store 內部排序）。</summary>
    IReadOnlyList<string> ResolveWorks(IAssetMetaStore store);
}

// ── 4 種 builtin node ───────────────────────────────────────

/// <summary>「依作者」mode 的 tree node — 對應一個 author。</summary>
public class AuthorVirtualNode : IVirtualLibraryNode
{
    public long AuthorId { get; }
    public string MainName { get; }
    public int? WorkCount { get; }
    public AuthorVirtualNode(long authorId, string mainName, int workCount)
    {
        AuthorId = authorId;
        MainName = mainName;
        WorkCount = workCount;
    }
    public string DisplayName => MainName;
    public string? IconSvg => "UserOutlined";
    public IReadOnlyList<string> ResolveWorks(IAssetMetaStore store) => store.ListWorksByAuthor(AuthorId);
}

/// <summary>「我的閱讀清單」mode 的 tree node — 對應一個自訂虛擬庫（#54）。
/// ResolveWorks runtime parse + execute；新作品自動納入，不快取結果。</summary>
public class MyVirtualLibraryNode : IVirtualLibraryNode
{
    public VirtualLibrary Library { get; }
    public int? WorkCount => null;   // 不預先計數（要跑 query 才知道，太貴）
    public MyVirtualLibraryNode(VirtualLibrary library) { Library = library; }
    public string DisplayName => Library.Name;
    public string? IconSvg => Library.IconSvg ?? "BookOutlined";
    public IReadOnlyList<string> ResolveWorks(IAssetMetaStore store)
    {
        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var query = SearchQueryParser.Parse(Library.Dsl);
        if (!query.IsEmpty)
        {
            // 走預設 culture lang（DSL tag 譯名搜尋）— 跟 ExecuteDslSearch 一致
            var lang = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? "zh-TW" : null;
            foreach (var path in new SearchExecutor(store, lang).Execute(query))
            {
                if (seen.Add(path)) merged.Add(path);
            }
        }

        foreach (var path in store.ListManualVirtualLibraryWorks(Library.Id))
        {
            if (seen.Add(path)) merged.Add(path);
        }

        return merged;
    }
}

/// <summary>「依標籤」mode 的 tree node — 對應一個 tag。
/// 多選聚合是 caller 端做（chip bar 收集多 TagId 後直接呼 store.ListWorksByTags）。</summary>
public class TagVirtualNode : IVirtualLibraryNode
{
    public long TagId { get; }
    public string Name { get; }
    public string? Category { get; }
    public int? WorkCount { get; }
    public TagVirtualNode(long tagId, string name, string? category, int workCount)
    {
        TagId = tagId;
        Name = name;
        Category = category;
        WorkCount = workCount;
    }
    public string DisplayName => Name;
    public string? IconSvg => "TagOutlined";
    public IReadOnlyList<string> ResolveWorks(IAssetMetaStore store) => store.ListWorksByTags(new[] { TagId });
}

/// <summary>「閱讀軌跡」的 4 種固定 sub-node。</summary>
public class ReadingTrackVirtualNode : IVirtualLibraryNode
{
    public ReadingTrackKind Kind { get; }
    public int? WorkCount { get; }
    public string DisplayName { get; }
    public ReadingTrackVirtualNode(ReadingTrackKind kind, string displayName, int? workCount = null)
    {
        Kind = kind;
        DisplayName = displayName;
        WorkCount = workCount;
    }
    public string? IconSvg => Kind switch
    {
        ReadingTrackKind.RecentlyRead => "HistoryOutlined",
        ReadingTrackKind.InProgress => "ReadOutlined",
        ReadingTrackKind.MostReadAuthors => "UserOutlined",
        ReadingTrackKind.MostReadTags => "TagOutlined",
        _ => null,
    };
    public IReadOnlyList<string> ResolveWorks(IAssetMetaStore store) => Kind switch
    {
        ReadingTrackKind.RecentlyRead => store.ListRecentlyRead(10),
        ReadingTrackKind.InProgress => store.ListReadingInProgress(),
        // MostReadAuthors / MostReadTags 是「群組 header」— 自身不展開作品。
        // tree UI 應展開成多個 child node（Author top10 / Tag top10），caller 處理。
        _ => Array.Empty<string>(),
    };
}

public enum ReadingTrackKind
{
    RecentlyRead,
    InProgress,
    MostReadAuthors,
    MostReadTags,
}

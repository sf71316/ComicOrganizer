namespace ComicOrganizer.App.Core;

/// <summary>
/// 虛擬庫的 root nodes 工廠（#48 Phase 1）。
/// GalleryControl 切到某 ViewMode 時呼這個拿 tree 內容；之後點 node 用 IVirtualLibraryNode.ResolveWorks 拿作品。
/// </summary>
public static class VirtualLibraryProvider
{
    /// <summary>「依作者」mode：列出所有 author + 作品數，依作品數 DESC 排。</summary>
    public static IReadOnlyList<AuthorVirtualNode> GetAuthorNodes(IAssetMetaStore store)
    {
        var authors = store.ListAuthorsByUsage();
        return authors
            .Where(u => u.WorkCount > 0)   // 沒作品的作者不顯示（多半是 metadata-only / merge 殘留）
            .Select(u => new AuthorVirtualNode(u.Author.Id, u.Author.MainName, u.WorkCount))
            .ToList();
    }

    /// <summary>「依標籤」mode：列出所有 tag。caller 應自行依 category 分群顯示。</summary>
    public static IReadOnlyList<TagVirtualNode> GetTagNodes(IAssetMetaStore store)
    {
        var tags = store.ListTagsByUsage();
        return tags
            .Where(t => t.AssetCount > 0)
            .Select(t => new TagVirtualNode(t.Id, t.Name, t.Category, t.AssetCount))
            .ToList();
    }

    /// <summary>「我的閱讀清單」mode：列出所有自訂虛擬庫（已依 pinned + sort_order 排序）。</summary>
    public static IReadOnlyList<MyVirtualLibraryNode> GetMyLibraryNodes(IAssetMetaStore store)
    {
        return store.ListVirtualLibraries()
            .Select(v => new MyVirtualLibraryNode(v))
            .ToList();
    }

    /// <summary>「閱讀軌跡」mode：4 個 fixed sub-node。
    /// MostReadAuthors / MostReadTags 是 group header — caller 應展開 children。</summary>
    public static IReadOnlyList<ReadingTrackVirtualNode> GetReadingTrackNodes(IAssetMetaStore store)
    {
        var recent = store.ListRecentlyRead(10);
        var inProgress = store.ListReadingInProgress();
        return new List<ReadingTrackVirtualNode>
        {
            new ReadingTrackVirtualNode(ReadingTrackKind.RecentlyRead, "最近 10 次看過", recent.Count),
            new ReadingTrackVirtualNode(ReadingTrackKind.InProgress, "未讀完的", inProgress.Count),
            new ReadingTrackVirtualNode(ReadingTrackKind.MostReadAuthors, "最常閱讀作者 top 10"),
            new ReadingTrackVirtualNode(ReadingTrackKind.MostReadTags, "最常閱讀標籤 top 10"),
        };
    }
}

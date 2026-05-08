namespace ComicOrganizer.App.Core;

/// <summary>
/// 把 <see cref="SearchQuery"/> 跑到 path list（#48 Phase C）。
///
/// 多 term 組合語意：
///   positive 全部 AND（intersect）
///   negative 從結果集 except
///   只有 negative → 起始集合 = ListAllAssetPaths()
///   無 term → 回空
///
/// 各 field 的解析策略：
///   Artist  : SearchAuthors(prefix LIKE) → ListWorksByAuthor (union 所有 matched author)
///   Tag     : SearchTagIdsByTranslatedName(LIKE 譯文) ∪ SearchTags(prefix LIKE 原文) → ListWorksByTags(intersect=false)
///   Language: ListWorksByLanguage(精準 code 或 null=未標記)
///   ReadStatus: 解析為 enum → ListWorksByReadStatus
///   FreeText: SearchWorksByTitleOrFilename(LIKE)
/// </summary>
public class SearchExecutor
{
    private readonly IAssetMetaStore _store;
    private readonly string? _lang;

    public SearchExecutor(IAssetMetaStore store, string? lang = null)
    {
        _store = store;
        _lang = lang;
    }

    public IReadOnlyList<string> Execute(SearchQuery query)
    {
        if (query.IsEmpty) return Array.Empty<string>();

        HashSet<string>? positive = null;
        var negative = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in query.Terms)
        {
            var paths = ResolveTerm(term);
            if (term.Negated)
            {
                negative.UnionWith(paths);
            }
            else
            {
                if (positive == null)
                    positive = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
                else
                    positive.IntersectWith(paths);
            }
        }

        IEnumerable<string> result = positive != null
            ? (IEnumerable<string>)positive
            : _store.ListAllAssetPaths();
        if (negative.Count > 0) result = result.Where(p => !negative.Contains(p));

        // 排序：依 path 字典序穩定（避免 set 遍歷順序不一致 → UI 看起來「跳」）
        return result
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> ResolveTerm(SearchTerm term)
    {
        switch (term.Field)
        {
            case SearchField.Artist: return ResolveArtist(term.Value);
            case SearchField.Tag: return ResolveTag(term.Value);
            case SearchField.Language: return _store.ListWorksByLanguage(term.Value);
            case SearchField.ReadStatus:
                return TryParseReadStatus(term.Value, out var status)
                    ? _store.ListWorksByReadStatus(status)
                    : Array.Empty<string>();
            case SearchField.FreeText: return _store.SearchWorksByTitleOrFilename(term.Value);
            default: return Array.Empty<string>();
        }
    }

    private IReadOnlyList<string> ResolveArtist(string keyword)
    {
        var authors = _store.SearchAuthors(keyword, limit: 50);
        if (authors.Count == 0) return Array.Empty<string>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in authors)
            foreach (var p in _store.ListWorksByAuthor(a.Id))
                paths.Add(p);
        return paths.ToList();
    }

    private IReadOnlyList<string> ResolveTag(string keyword)
    {
        // 兩種命中方式 union：
        //   1. tag_translation.name LIKE %keyword% （中文 / 日文搜尋）
        //   2. tag.name 前綴 LIKE keyword% （英文 slug 搜尋）
        var tagIds = new HashSet<long>(_store.SearchTagIdsByTranslatedName(keyword, _lang, limit: 100));
        foreach (var t in _store.SearchTags(keyword, limit: 100))
            tagIds.Add(t.Id);

        if (tagIds.Count == 0) return Array.Empty<string>();
        return _store.ListWorksByTags(tagIds.ToList(), intersect: false);
    }

    private static bool TryParseReadStatus(string value, out ReadStatus status)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "unread": case "未讀": case "未读":
                status = ReadStatus.Unread; return true;
            case "reading": case "進行中": case "进行中": case "in-progress":
                status = ReadStatus.Reading; return true;
            case "completed": case "done": case "已完成":
                status = ReadStatus.Completed; return true;
            case "dropped": case "abandoned": case "放棄": case "放弃":
                status = ReadStatus.Dropped; return true;
            default:
                status = ReadStatus.Unread; return false;
        }
    }
}

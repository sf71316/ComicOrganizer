using System.Globalization;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Phase 1 視覺探索牆聚合服務：
/// 單趟批次載入語料並於記憶體內完成作者聚合、主題／原作牆、熱門雙組合、
/// 全標籤挑選器與隨機雙題材碰撞計算，避免 N+1 查詢並完全相容 v8 唯讀 DB 與 v9 DB。
/// </summary>
public sealed class ExploreWallService
{
    private static readonly HashSet<string> StructuralTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sole female", "sole male", "multi-work series", "tankoubon",
        "full censorship", "mosaic censorship", "uncensored", "translated",
        "chinese", "japanese", "english", "anthology", "sketch", "artbook",
        "color", "full color", "oneshot", "story arc", "incomplete", "rewrite",
    };

    private static readonly HashSet<string> ContentCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "tag", "virtual", "misc", "",
    };

    private static readonly HashSet<string> ParodyCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "parody", "character",
    };

    private readonly IAssetMetaStore _store;
    private readonly TagTranslator? _translator;
    private readonly string _translationLang;
    private readonly object _cacheLock = new();
    private CompiledCorpus? _corpus;

    public ExploreWallService(
        IAssetMetaStore store,
        TagTranslator? translator = null,
        string translationLang = "zh-TW")
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _translator = translator;
        _translationLang = string.IsNullOrWhiteSpace(translationLang) ? "zh-TW" : translationLang.Trim();
    }

    /// <summary>清除探索牆語料快取（資料更新或重新解析後呼叫）。</summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _corpus = null;
        }
    }

    public ExploreStatsSummary GetStatsSummary()
    {
        var corpus = EnsureCorpus();
        return corpus.Stats;
    }

    /// <summary>取得頂部單列熱門標籤（已選標籤釘選最前 ＋ 熱門前 N 顆非結構性題材標籤）。</summary>
    public IReadOnlyList<ExploreTagBadge> GetTopSpotlightTags(
        IReadOnlyCollection<long>? selectedTagIds = null,
        int topCount = 10)
    {
        var corpus = EnsureCorpus();
        var selectedSet = selectedTagIds != null && selectedTagIds.Count > 0
            ? new HashSet<long>(selectedTagIds)
            : new HashSet<long>();

        var result = new List<ExploreTagBadge>();
        var addedIds = new HashSet<long>();

        if (selectedTagIds != null)
        {
            foreach (var tid in selectedTagIds)
            {
                if (corpus.TagsById.TryGetValue(tid, out var badge) && addedIds.Add(tid))
                    result.Add(badge);
            }
        }

        // 先挑非結構的內容 / 虛擬 / 原作標籤
        var preferred = corpus.TagsByUsage
            .Where(t => t.WorkCount > 0
                        && !addedIds.Contains(t.TagId)
                        && !t.IsStructural
                        && (IsContentCategory(t.Category) || string.Equals(t.Category, "parody", StringComparison.OrdinalIgnoreCase)))
            .Take(Math.Max(0, topCount));

        foreach (var t in preferred)
        {
            if (addedIds.Add(t.TagId))
                result.Add(t);
        }

        // 若庫內標籤數較少，從其餘有效標籤補滿
        int desiredTotal = selectedSet.Count + Math.Max(0, topCount);
        if (result.Count < desiredTotal)
        {
            foreach (var t in corpus.TagsByUsage.Where(t => t.WorkCount > 0 && !addedIds.Contains(t.TagId)))
            {
                if (result.Count >= desiredTotal) break;
                addedIds.Add(t.TagId);
                result.Add(t);
            }
        }

        return result;
    }

    /// <summary>取得「🔍 全部標籤」挑選器清單（支援關鍵字同時比對原文、譯名與別名，以及分類過濾）。</summary>
    public IReadOnlyList<ExploreTagBadge> GetAllPickerTags(
        string? keyword = null,
        string? categoryFilter = null,
        int limit = 0)
    {
        var corpus = EnsureCorpus();
        var kw = keyword?.Trim();
        var cat = categoryFilter?.Trim();

        IEnumerable<ExploreTagBadge> query = corpus.TagsByUsage.Where(t => t.WorkCount > 0);

        if (!string.IsNullOrEmpty(cat) && !string.Equals(cat, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = cat.ToLowerInvariant() switch
            {
                "content" or "theme" => query.Where(t => IsContentCategory(t.Category)),
                "parody" => query.Where(t => string.Equals(t.Category, "parody", StringComparison.OrdinalIgnoreCase)),
                "character" => query.Where(t => string.Equals(t.Category, "character", StringComparison.OrdinalIgnoreCase)),
                "parody_character" => query.Where(t => IsParodyCategory(t.Category)),
                "artist_group" => query.Where(t =>
                    string.Equals(t.Category, "artist", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(t.Category, "group", StringComparison.OrdinalIgnoreCase)),
                _ => query.Where(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase)),
            };
        }

        if (!string.IsNullOrEmpty(kw))
        {
            query = query.Where(t => MatchesTagKeyword(corpus, t, kw));
        }

        if (limit > 0)
            query = query.Take(limit);

        return query.ToList();
    }

    /// <summary>作者探索牆卡片列表（支援多標籤 AND 過濾、關鍵字搜尋、未讀過濾與排序）。</summary>
    public IReadOnlyList<ExploreAuthorCard> GetAuthorCards(ExploreQueryOptions? options = null)
    {
        var opts = options ?? new ExploreQueryOptions();
        var corpus = EnsureCorpus();

        var selectedTags = NormalizeTagIds(opts.SelectedTagIds);
        var filteredWorks = FilterWorksByTags(corpus, selectedTags);

        var worksByAuthor = new Dictionary<long, List<ExploreWorkItem>>();
        foreach (var work in filteredWorks)
        {
            if (!work.AuthorId.HasValue) continue;
            if (!worksByAuthor.TryGetValue(work.AuthorId.Value, out var list))
            {
                list = new List<ExploreWorkItem>();
                worksByAuthor[work.AuthorId.Value] = list;
            }
            list.Add(work);
        }

        var kw = opts.SearchText?.Trim();
        var cards = new List<ExploreAuthorCard>(worksByAuthor.Count);

        foreach (var (authorId, authorWorks) in worksByAuthor)
        {
            if (!corpus.AuthorsById.TryGetValue(authorId, out var authorRow)) continue;

            int unreadCount = 0;
            int readingCount = 0;
            int completedCount = 0;
            foreach (var w in authorWorks)
            {
                switch (w.ReadStatus)
                {
                    case ReadStatus.Unread: unreadCount++; break;
                    case ReadStatus.Reading: readingCount++; break;
                    case ReadStatus.Completed: completedCount++; break;
                }
            }

            if (opts.UnreadOnly && unreadCount == 0) continue;

            var aliases = corpus.AuthorAliasesById.TryGetValue(authorId, out var aliasList)
                ? aliasList
                : Array.Empty<string>();

            var (topTags, distinctTagCount) = AggregateTopTagsForWorks(authorWorks, maxTopTags: 10);

            if (!string.IsNullOrEmpty(kw) && !MatchesAuthorKeyword(authorRow, aliases, authorWorks, topTags, kw))
                continue;

            int allLibraryWorks = corpus.AuthorWorkCounts.TryGetValue(authorId, out var total) ? total : authorWorks.Count;
            var representative = SelectRepresentativeWorks(authorWorks, maxCount: 3);

            cards.Add(new ExploreAuthorCard(
                AuthorId: authorId,
                MainName: authorRow.MainName,
                Aliases: aliases,
                FolderPath: authorRow.FolderPath,
                TotalWorks: authorWorks.Count,
                AllLibraryWorks: allLibraryWorks,
                UnreadWorks: unreadCount,
                ReadingWorks: readingCount,
                CompletedWorks: completedCount,
                DistinctTagCount: distinctTagCount,
                TopTags: topTags,
                RepresentativeWorks: representative));
        }

        return SortAndLimit(
            cards,
            opts.SortMode,
            opts.RandomSeed,
            opts.MaxCards,
            c => c.TotalWorks,
            c => c.UnreadWorks,
            c => c.MainName);
    }

    public ExploreAuthorCard? GetAuthorCard(long authorId, IReadOnlyList<long>? selectedTagIds = null)
    {
        var corpus = EnsureCorpus();
        if (!corpus.AuthorsById.TryGetValue(authorId, out var authorRow)) return null;

        var works = GetWorksForAuthor(authorId, selectedTagIds);
        if (works.Count == 0 && (selectedTagIds == null || selectedTagIds.Count == 0))
        {
            var aliasesEmpty = corpus.AuthorAliasesById.TryGetValue(authorId, out var al) ? al : Array.Empty<string>();
            return new ExploreAuthorCard(
                authorId, authorRow.MainName, aliasesEmpty, authorRow.FolderPath,
                0, 0, 0, 0, 0, 0, Array.Empty<ExploreTagBadge>(), Array.Empty<ExploreWorkItem>());
        }

        if (works.Count == 0) return null;

        int unreadCount = works.Count(w => w.ReadStatus == ReadStatus.Unread);
        int readingCount = works.Count(w => w.ReadStatus == ReadStatus.Reading);
        int completedCount = works.Count(w => w.ReadStatus == ReadStatus.Completed);
        var aliases = corpus.AuthorAliasesById.TryGetValue(authorId, out var aliasList)
            ? aliasList
            : Array.Empty<string>();
        var (topTags, distinctCount) = AggregateTopTagsForWorks(works, maxTopTags: 10);
        int allLibraryWorks = corpus.AuthorWorkCounts.TryGetValue(authorId, out var total) ? total : works.Count;

        return new ExploreAuthorCard(
            AuthorId: authorId,
            MainName: authorRow.MainName,
            Aliases: aliases,
            FolderPath: authorRow.FolderPath,
            TotalWorks: works.Count,
            AllLibraryWorks: allLibraryWorks,
            UnreadWorks: unreadCount,
            ReadingWorks: readingCount,
            CompletedWorks: completedCount,
            DistinctTagCount: distinctCount,
            TopTags: topTags,
            RepresentativeWorks: SelectRepresentativeWorks(works, maxCount: 3));
    }

    /// <summary>主題・原作牆：「內容主題」卡片列表。</summary>
    public IReadOnlyList<ExploreThemeCard> GetContentThemeCards(ExploreQueryOptions? options = null)
        => BuildThemeCards(options, t => IsContentCategory(t.Category));

    /// <summary>主題・原作牆：「原作・角色」卡片列表。</summary>
    public IReadOnlyList<ExploreThemeCard> GetParodyAndCharacterCards(ExploreQueryOptions? options = null)
        => BuildThemeCards(options, t => IsParodyCategory(t.Category));

    /// <summary>主題・原作牆：「熱門雙組合」卡片列表。</summary>
    public IReadOnlyList<ExploreComboCard> GetHotComboCards(ExploreQueryOptions? options = null, int minPairWorks = 2)
    {
        var opts = options ?? new ExploreQueryOptions();
        var corpus = EnsureCorpus();
        var selectedTags = NormalizeTagIds(opts.SelectedTagIds);
        var selectedSet = selectedTags.ToHashSet();
        var filteredWorks = FilterWorksByTags(corpus, selectedTags);
        if (filteredWorks.Count == 0) return Array.Empty<ExploreComboCard>();

        var candidateTags = SelectComboCandidateTagIds(corpus, maxCandidates: 48);
        if (candidateTags.Count < 2) return Array.Empty<ExploreComboCard>();

        var kw = opts.SearchText?.Trim();
        var pairWorks = new Dictionary<(long A, long B), List<ExploreWorkItem>>();

        foreach (var work in filteredWorks)
        {
            if (opts.UnreadOnly && work.ReadStatus != ReadStatus.Unread) continue;

            var relevantIds = work.Tags
                .Select(t => t.TagId)
                .Where(id => candidateTags.Contains(id) || selectedSet.Contains(id))
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            if (relevantIds.Count < 2) continue;

            for (int i = 0; i < relevantIds.Count; i++)
            {
                for (int j = i + 1; j < relevantIds.Count; j++)
                {
                    var key = (relevantIds[i], relevantIds[j]);
                    if (!pairWorks.TryGetValue(key, out var list))
                    {
                        list = new List<ExploreWorkItem>();
                        pairWorks[key] = list;
                    }
                    list.Add(work);
                }
            }
        }

        int effectiveMin = Math.Max(1, minPairWorks);
        var combos = new List<ExploreComboCard>();

        foreach (var (pair, works) in pairWorks)
        {
            if (works.Count < effectiveMin) continue;
            if (!corpus.TagsById.TryGetValue(pair.A, out var tagA) ||
                !corpus.TagsById.TryGetValue(pair.B, out var tagB))
                continue;

            // 若使用者選了多個標籤當篩選，避免列出與自身完全重複的單對，或優先排除「兩邊都已經是已選標籤」的無意義配對
            if (selectedSet.Count >= 2 && selectedSet.Contains(tagA.TagId) && selectedSet.Contains(tagB.TagId))
                continue;

            if (!string.IsNullOrEmpty(kw)
                && !MatchesTagKeyword(corpus, tagA, kw)
                && !MatchesTagKeyword(corpus, tagB, kw))
            {
                continue;
            }

            // 顯示順序將較熱門的放左邊
            var (first, second) = tagA.WorkCount >= tagB.WorkCount ? (tagA, tagB) : (tagB, tagA);
            int unreadCount = works.Count(w => w.ReadStatus == ReadStatus.Unread);

            combos.Add(new ExploreComboCard(
                FirstTag: first,
                SecondTag: second,
                WorkCount: works.Count,
                UnreadWorks: unreadCount,
                RepresentativeWorks: SelectRepresentativeWorks(works, maxCount: 3)));
        }

        return SortAndLimit(
            combos,
            opts.SortMode,
            opts.RandomSeed,
            opts.MaxCards,
            c => c.WorkCount,
            c => c.UnreadWorks,
            c => $"{c.FirstTag.DisplayName} × {c.SecondTag.DisplayName}");
    }

    /// <summary>⚡ 隨機雙題材碰撞：抽取一組保證有交集作品的 (TagA, TagB) 配對。</summary>
    public ExploreComboCard? PickRandomDoubleCombo(
        (long TagIdA, long TagIdB)? previousPair = null,
        int minWorks = 2,
        int? seed = null)
    {
        var corpus = EnsureCorpus();
        var candidatePairs = CollectDoubleComboCandidates(corpus, Math.Max(1, minWorks));
        if (candidatePairs.Count == 0 && minWorks > 1)
        {
            candidatePairs = CollectDoubleComboCandidates(corpus, minIntersection: 1);
        }

        if (candidatePairs.Count == 0) return null;

        List<(long A, long B, List<ExploreWorkItem> Works)> pool = candidatePairs;
        if (previousPair.HasValue && candidatePairs.Count > 1)
        {
            var prev = NormalizePair(previousPair.Value.TagIdA, previousPair.Value.TagIdB);
            var withoutPrev = candidatePairs.Where(p => (p.A, p.B) != prev).ToList();
            if (withoutPrev.Count > 0) pool = withoutPrev;
        }

        var rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        var chosen = pool[rng.Next(pool.Count)];

        var tagA = corpus.TagsById[chosen.A];
        var tagB = corpus.TagsById[chosen.B];
        var (first, second) = tagA.WorkCount >= tagB.WorkCount ? (tagA, tagB) : (tagB, tagA);
        int unread = chosen.Works.Count(w => w.ReadStatus == ReadStatus.Unread);

        return new ExploreComboCard(
            FirstTag: first,
            SecondTag: second,
            WorkCount: chosen.Works.Count,
            UnreadWorks: unread,
            RepresentativeWorks: SelectRepresentativeWorks(chosen.Works, maxCount: 6));
    }

    /// <summary>鑽取查詢：取得指定作者的作品清單（可搭配當前勾選的標籤 AND 過濾）。</summary>
    public IReadOnlyList<ExploreWorkItem> GetWorksForAuthor(long authorId, IReadOnlyList<long>? additionalTagIds = null)
    {
        var corpus = EnsureCorpus();
        if (!corpus.WorksByAuthorId.TryGetValue(authorId, out var authorWorks))
            return Array.Empty<ExploreWorkItem>();

        var requiredTags = NormalizeTagIds(additionalTagIds);
        IEnumerable<ExploreWorkItem> query = authorWorks;
        if (requiredTags.Count > 0)
            query = query.Where(w => WorkHasAllTags(corpus, w.AssetId, requiredTags));

        return query
            .OrderBy(w => w.ReadStatus == ReadStatus.Unread ? 0 : 1)
            .ThenBy(w => w.Title, NaturalStringComparer.Instance)
            .ThenBy(w => w.AssetId)
            .ToList();
    }

    /// <summary>鑽取查詢：取得符合標籤條件的作品清單（intersect=true 為 AND 交集，false 為 OR 聯集）。</summary>
    public IReadOnlyList<ExploreWorkItem> GetWorksForTags(IReadOnlyList<long> tagIds, bool intersect = true)
    {
        var corpus = EnsureCorpus();
        var ids = NormalizeTagIds(tagIds);
        if (ids.Count == 0) return Array.Empty<ExploreWorkItem>();

        IEnumerable<ExploreWorkItem> matched = intersect
            ? FilterWorksByTags(corpus, ids)
            : corpus.AllWorks.Where(w =>
                corpus.WorkTagIdSets.TryGetValue(w.AssetId, out var set) && ids.Any(set.Contains));

        return matched
            .OrderBy(w => string.IsNullOrEmpty(w.CoverThumbKey) ? 1 : 0)
            .ThenBy(w => w.ReadStatus == ReadStatus.Unread ? 0 : 1)
            .ThenBy(w => w.Title, NaturalStringComparer.Instance)
            .ThenBy(w => w.AssetId)
            .ToList();
    }

    // ===== 私有實作 =====

    private IReadOnlyList<ExploreThemeCard> BuildThemeCards(
        ExploreQueryOptions? options,
        Func<ExploreTagBadge, bool> categoryPredicate)
    {
        var opts = options ?? new ExploreQueryOptions();
        var corpus = EnsureCorpus();
        var selectedTags = NormalizeTagIds(opts.SelectedTagIds);
        var selectedSet = selectedTags.ToHashSet();
        var filteredWorks = FilterWorksByTags(corpus, selectedTags);
        var kw = opts.SearchText?.Trim();

        var worksByTag = new Dictionary<long, List<ExploreWorkItem>>();
        foreach (var work in filteredWorks)
        {
            if (opts.UnreadOnly && work.ReadStatus != ReadStatus.Unread) continue;

            foreach (var badge in work.Tags)
            {
                if (!categoryPredicate(badge)) continue;
                if (!worksByTag.TryGetValue(badge.TagId, out var list))
                {
                    list = new List<ExploreWorkItem>();
                    worksByTag[badge.TagId] = list;
                }
                list.Add(work);
            }
        }

        var cards = new List<ExploreThemeCard>();
        foreach (var (tagId, works) in worksByTag)
        {
            if (!corpus.TagsById.TryGetValue(tagId, out var badge)) continue;

            // 若已有選取篩選標籤且使用者沒搜尋該標籤，排除已被選作過濾條件的標籤避免重複顯示自己
            if (selectedSet.Contains(tagId) && string.IsNullOrEmpty(kw))
                continue;

            if (!string.IsNullOrEmpty(kw) && !MatchesTagKeyword(corpus, badge, kw))
                continue;

            int unread = works.Count(w => w.ReadStatus == ReadStatus.Unread);
            cards.Add(new ExploreThemeCard(
                TagId: tagId,
                Name: badge.Name,
                DisplayName: badge.DisplayName,
                Category: badge.Category,
                WorkCount: works.Count,
                UnreadWorks: unread,
                RepresentativeWorks: SelectRepresentativeWorks(works, maxCount: 3)));
        }

        // 若選擇了 WorkCountDesc，將非結構性主題優先排前面，結構性標籤降序至後方（仍保留不丟失）
        if (opts.SortMode == ExploreSortMode.WorkCountDesc)
        {
            var ordered = cards
                .OrderBy(c => corpus.TagsById[c.TagId].IsStructural ? 1 : 0)
                .ThenByDescending(c => c.WorkCount)
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase);
            return opts.MaxCards > 0 ? ordered.Take(opts.MaxCards).ToList() : ordered.ToList();
        }

        return SortAndLimit(
            cards,
            opts.SortMode,
            opts.RandomSeed,
            opts.MaxCards,
            c => c.WorkCount,
            c => c.UnreadWorks,
            c => c.DisplayName);
    }

    private List<(long A, long B, List<ExploreWorkItem> Works)> CollectDoubleComboCandidates(
        CompiledCorpus corpus,
        int minIntersection)
    {
        // 挑選富含題材意義的候選標籤（先排除結構標籤，若太少則放寬）
        var richTagIds = corpus.TagsByUsage
            .Where(t => t.WorkCount >= minIntersection
                        && !t.IsStructural
                        && (IsContentCategory(t.Category) || string.Equals(t.Category, "parody", StringComparison.OrdinalIgnoreCase)))
            .Take(60)
            .Select(t => t.TagId)
            .ToHashSet();

        if (richTagIds.Count < 2)
        {
            richTagIds = corpus.TagsByUsage
                .Where(t => t.WorkCount >= minIntersection
                            && (IsContentCategory(t.Category) || IsParodyCategory(t.Category)))
                .Take(60)
                .Select(t => t.TagId)
                .ToHashSet();
        }

        if (richTagIds.Count < 2) return new List<(long, long, List<ExploreWorkItem>)>();

        var pairs = new Dictionary<(long A, long B), List<ExploreWorkItem>>();
        foreach (var work in corpus.AllWorks)
        {
            var ids = work.Tags
                .Select(t => t.TagId)
                .Where(richTagIds.Contains)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            if (ids.Count < 2) continue;

            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = i + 1; j < ids.Count; j++)
                {
                    var key = (ids[i], ids[j]);
                    if (!pairs.TryGetValue(key, out var list))
                    {
                        list = new List<ExploreWorkItem>();
                        pairs[key] = list;
                    }
                    list.Add(work);
                }
            }
        }

        return pairs
            .Where(kv => kv.Value.Count >= minIntersection)
            .Select(kv => (kv.Key.A, kv.Key.B, kv.Value))
            .ToList();
    }

    private static HashSet<long> SelectComboCandidateTagIds(CompiledCorpus corpus, int maxCandidates)
    {
        var nonStructural = corpus.TagsByUsage
            .Where(t => t.WorkCount >= 2
                        && !t.IsStructural
                        && (IsContentCategory(t.Category) || string.Equals(t.Category, "parody", StringComparison.OrdinalIgnoreCase)))
            .Take(maxCandidates)
            .Select(t => t.TagId)
            .ToHashSet();

        if (nonStructural.Count >= 2) return nonStructural;

        return corpus.TagsByUsage
            .Where(t => t.WorkCount >= 2)
            .Take(maxCandidates)
            .Select(t => t.TagId)
            .ToHashSet();
    }

    private static IReadOnlyList<ExploreWorkItem> SelectRepresentativeWorks(
        IReadOnlyList<ExploreWorkItem> works,
        int maxCount)
    {
        if (works.Count == 0 || maxCount <= 0) return Array.Empty<ExploreWorkItem>();

        var prioritized = works
            .OrderBy(w => string.IsNullOrEmpty(w.CoverThumbKey) ? 1 : 0)
            .ThenBy(w => w.ReadStatus == ReadStatus.Unread ? 0 : 1)
            .ThenBy(w => string.IsNullOrEmpty(w.ExternalId) ? 1 : 0)
            .ThenBy(w => w.AssetId)
            .ToList();

        var chosen = new List<ExploreWorkItem>(Math.Min(maxCount, prioritized.Count));
        var chosenIds = new HashSet<long>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TakeMatches(bool requireThumb, bool requireUniqueTitle)
        {
            foreach (var w in prioritized)
            {
                if (chosen.Count >= maxCount) return;
                if (chosenIds.Contains(w.AssetId)) continue;
                if (requireThumb && string.IsNullOrEmpty(w.CoverThumbKey)) continue;

                var norm = NormalizeTitleForDedupe(w.Title);
                if (requireUniqueTitle && !seenTitles.Add(norm)) continue;

                seenTitles.Add(norm);
                chosenIds.Add(w.AssetId);
                chosen.Add(w);
            }
        }

        // 1. 有縮圖且不重複標題 → 2. 有縮圖但可重複標題 → 3. 無縮圖不重複標題 → 4. 剩餘作品
        TakeMatches(requireThumb: true, requireUniqueTitle: true);
        TakeMatches(requireThumb: true, requireUniqueTitle: false);
        TakeMatches(requireThumb: false, requireUniqueTitle: true);
        TakeMatches(requireThumb: false, requireUniqueTitle: false);

        return chosen;
    }

    private static (IReadOnlyList<ExploreTagBadge> TopTags, int DistinctTagCount) AggregateTopTagsForWorks(
        IReadOnlyList<ExploreWorkItem> works,
        int maxTopTags)
    {
        var freq = new Dictionary<long, (ExploreTagBadge Badge, int Count)>();
        foreach (var work in works)
        {
            foreach (var tag in work.Tags)
            {
                if (!IsContentCategory(tag.Category) && !IsParodyCategory(tag.Category))
                    continue;

                if (freq.TryGetValue(tag.TagId, out var existing))
                    freq[tag.TagId] = (existing.Badge, existing.Count + 1);
                else
                    freq[tag.TagId] = (tag, 1);
            }
        }

        var ordered = freq.Values
            .OrderBy(x => x.Badge.IsStructural ? 1 : 0)
            .ThenBy(x => IsContentCategory(x.Badge.Category) ? 0 : 1)
            .ThenByDescending(x => x.Count)
            .ThenByDescending(x => x.Badge.WorkCount)
            .ThenBy(x => x.Badge.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Badge)
            .Take(maxTopTags)
            .ToList();

        return (ordered, freq.Count);
    }

    private static IReadOnlyList<T> SortAndLimit<T>(
        List<T> items,
        ExploreSortMode sortMode,
        int randomSeed,
        int maxCards,
        Func<T, int> workCountSelector,
        Func<T, int> unreadCountSelector,
        Func<T, string> nameSelector)
    {
        if (items.Count == 0) return Array.Empty<T>();

        IEnumerable<T> ordered = sortMode switch
        {
            ExploreSortMode.UnreadDesc => items
                .OrderByDescending(unreadCountSelector)
                .ThenByDescending(workCountSelector)
                .ThenBy(nameSelector, StringComparer.OrdinalIgnoreCase),
            ExploreSortMode.NameAsc => items
                .OrderBy(nameSelector, NaturalStringComparer.Instance),
            ExploreSortMode.RandomShuffle => ShuffleItems(items, randomSeed),
            _ => items
                .OrderByDescending(workCountSelector)
                .ThenByDescending(unreadCountSelector)
                .ThenBy(nameSelector, StringComparer.OrdinalIgnoreCase),
        };

        return maxCards > 0 ? ordered.Take(maxCards).ToList() : ordered.ToList();
    }

    private static List<T> ShuffleItems<T>(List<T> items, int seed)
    {
        var copy = new List<T>(items);
        var rng = seed != 0 ? new Random(seed) : Random.Shared;
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    private static IReadOnlyList<ExploreWorkItem> FilterWorksByTags(
        CompiledCorpus corpus,
        IReadOnlyList<long> requiredTagIds)
    {
        if (requiredTagIds.Count == 0) return corpus.AllWorks;

        return corpus.AllWorks
            .Where(w => WorkHasAllTags(corpus, w.AssetId, requiredTagIds))
            .ToList();
    }

    private static bool WorkHasAllTags(CompiledCorpus corpus, long assetId, IReadOnlyList<long> requiredTagIds)
    {
        if (!corpus.WorkTagIdSets.TryGetValue(assetId, out var set)) return false;
        foreach (var id in requiredTagIds)
        {
            if (!set.Contains(id)) return false;
        }
        return true;
    }

    private static bool MatchesAuthorKeyword(
        ExploreAuthorRow author,
        IReadOnlyList<string> aliases,
        IReadOnlyList<ExploreWorkItem> works,
        IReadOnlyList<ExploreTagBadge> topTags,
        string keyword)
    {
        if (author.MainName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var al in aliases)
        {
            if (al.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (var tag in topTags)
        {
            if (tag.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || tag.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        foreach (var w in works)
        {
            if (w.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool MatchesTagKeyword(CompiledCorpus corpus, ExploreTagBadge tag, string keyword)
    {
        if (tag.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        if (tag.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        if (corpus.TagAliasesById.TryGetValue(tag.TagId, out var aliases))
        {
            foreach (var al in aliases)
            {
                if (al.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private CompiledCorpus EnsureCorpus()
    {
        lock (_cacheLock)
        {
            if (_corpus != null) return _corpus;
            _corpus = BuildCorpus(_store.GetExploreCorpusSnapshot(_translationLang));
            return _corpus;
        }
    }

    private CompiledCorpus BuildCorpus(ExploreCorpusSnapshot snapshot)
    {
        var workCountPerTag = new Dictionary<long, int>();
        var tagIdsPerWork = new Dictionary<long, HashSet<long>>();
        foreach (var link in snapshot.AssetTags)
        {
            if (!tagIdsPerWork.TryGetValue(link.AssetId, out var set))
            {
                set = new HashSet<long>();
                tagIdsPerWork[link.AssetId] = set;
            }
            if (set.Add(link.TagId))
            {
                workCountPerTag[link.TagId] = workCountPerTag.GetValueOrDefault(link.TagId) + 1;
            }
        }

        var tagsById = new Dictionary<long, ExploreTagBadge>(snapshot.Tags.Count);
        int translatedCount = 0;
        int contentTagCount = 0;
        int parodyCount = 0;

        foreach (var tr in snapshot.Tags)
        {
            var cat = NormalizeCategory(tr.Category);
            var displayName = ResolveDisplayName(tr, cat);
            int wCount = workCountPerTag.GetValueOrDefault(tr.Id);
            bool isStructural = StructuralTagNames.Contains(tr.Name);

            if (!string.IsNullOrWhiteSpace(tr.TranslatedName) ||
                !string.Equals(displayName, tr.Name, StringComparison.OrdinalIgnoreCase))
            {
                translatedCount++;
            }

            if (string.Equals(cat, "tag", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cat, "virtual", StringComparison.OrdinalIgnoreCase))
                contentTagCount++;
            else if (string.Equals(cat, "parody", StringComparison.OrdinalIgnoreCase))
                parodyCount++;

            tagsById[tr.Id] = new ExploreTagBadge(
                TagId: tr.Id,
                Name: tr.Name,
                DisplayName: displayName,
                Category: cat,
                WorkCount: wCount,
                IsStructural: isStructural);
        }

        var tagsByUsage = tagsById.Values
            .OrderByDescending(t => t.WorkCount)
            .ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tagAliasesById = new Dictionary<long, List<string>>();
        foreach (var al in snapshot.TagAliases)
        {
            if (!tagAliasesById.TryGetValue(al.TargetTagId, out var list))
            {
                list = new List<string>();
                tagAliasesById[al.TargetTagId] = list;
            }
            list.Add(al.AliasName);
        }

        var authorsById = snapshot.Authors.ToDictionary(a => a.Id);
        var authorAliasesById = new Dictionary<long, IReadOnlyList<string>>();
        foreach (var grp in snapshot.AuthorAliases.GroupBy(a => a.AuthorId))
        {
            authorAliasesById[grp.Key] = grp.Select(a => a.Name).ToList();
        }

        var allWorks = new List<ExploreWorkItem>(snapshot.Works.Count);
        var worksByAuthorId = new Dictionary<long, List<ExploreWorkItem>>();
        var authorWorkCounts = new Dictionary<long, int>();
        int unreadWorks = 0;
        int syncedWorks = 0;

        foreach (var row in snapshot.Works)
        {
            var workTags = tagIdsPerWork.TryGetValue(row.AssetId, out var tIds)
                ? tIds.Where(tagsById.ContainsKey)
                      .Select(id => tagsById[id])
                      .OrderBy(t => GetTagDisplayPriority(t.Category))
                      .ThenBy(t => t.IsStructural ? 1 : 0)
                      .ThenByDescending(t => t.WorkCount)
                      .ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                      .ToList()
                : (IReadOnlyList<ExploreTagBadge>)Array.Empty<ExploreTagBadge>();

            var readStatus = Enum.IsDefined(typeof(ReadStatus), row.ReadStatus)
                ? (ReadStatus)row.ReadStatus
                : ReadStatus.Unread;

            if (readStatus == ReadStatus.Unread) unreadWorks++;
            if (!string.IsNullOrWhiteSpace(row.ExternalId)) syncedWorks++;

            var title = !string.IsNullOrWhiteSpace(row.Title)
                ? row.Title.Trim()
                : Path.GetFileNameWithoutExtension(row.FileName);
            if (string.IsNullOrWhiteSpace(title)) title = row.FileName;

            var item = new ExploreWorkItem(
                AssetId: row.AssetId,
                Path: row.Path,
                FileName: row.FileName,
                Title: title,
                AuthorId: row.AuthorId,
                AuthorName: string.IsNullOrWhiteSpace(row.AuthorName) ? "未知作者" : row.AuthorName,
                Language: row.Language,
                TranslationGroup: row.TranslationGroup,
                PageCount: row.PageCount,
                ReadStatus: readStatus,
                ReadCount: row.ReadCount,
                LastRead: row.LastRead,
                AddedAt: row.AddedAt,
                ExternalId: row.ExternalId,
                CoverThumbKey: row.CoverThumbKey,
                Tags: workTags);

            allWorks.Add(item);

            if (row.AuthorId.HasValue)
            {
                if (!worksByAuthorId.TryGetValue(row.AuthorId.Value, out var list))
                {
                    list = new List<ExploreWorkItem>();
                    worksByAuthorId[row.AuthorId.Value] = list;
                }
                list.Add(item);
                authorWorkCounts[row.AuthorId.Value] = authorWorkCounts.GetValueOrDefault(row.AuthorId.Value) + 1;
            }
        }

        var stats = new ExploreStatsSummary(
            TotalWorks: allWorks.Count,
            TotalAuthors: authorsById.Count,
            TotalContentTags: contentTagCount,
            TotalParodies: parodyCount,
            TotalTagsAll: tagsById.Count,
            UnreadWorks: unreadWorks,
            SyncedWorks: syncedWorks,
            TranslatedTags: translatedCount);

        return new CompiledCorpus(
            Stats: stats,
            AllWorks: allWorks,
            AuthorsById: authorsById,
            AuthorAliasesById: authorAliasesById,
            WorksByAuthorId: worksByAuthorId,
            AuthorWorkCounts: authorWorkCounts,
            TagsById: tagsById,
            TagsByUsage: tagsByUsage,
            TagAliasesById: tagAliasesById,
            WorkTagIdSets: tagIdsPerWork);
    }

    private string ResolveDisplayName(ExploreTagRow row, string normalizedCategory)
    {
        if (!string.IsNullOrWhiteSpace(row.TranslatedName))
            return row.TranslatedName.Trim();

        if (_translator != null && _translator.IsAvailable)
        {
            string? translated = normalizedCategory is "tag" or "parody" or "character" or "artist" or "group"
                ? _translator.Translate(normalizedCategory, row.Name)
                : null;
            translated ??= _translator.TranslateAny(row.Name);

            if (!string.IsNullOrWhiteSpace(translated))
            {
                if (_translationLang.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                    || _translationLang.Contains("Hant", StringComparison.OrdinalIgnoreCase))
                {
                    return ZhConverter.ToTraditional(translated.Trim());
                }
                return translated.Trim();
            }
        }

        return row.Name;
    }

    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "tag";
        return category.Trim().ToLowerInvariant();
    }

    private static bool IsContentCategory(string category) => ContentCategories.Contains(category);

    private static bool IsParodyCategory(string category) => ParodyCategories.Contains(category);

    private static int GetTagDisplayPriority(string category) => category.ToLowerInvariant() switch
    {
        "virtual" => 0,
        "tag" => 1,
        "parody" => 2,
        "character" => 3,
        "artist" => 4,
        "group" => 5,
        _ => 6,
    };

    private static IReadOnlyList<long> NormalizeTagIds(IReadOnlyList<long>? ids)
    {
        if (ids == null || ids.Count == 0) return Array.Empty<long>();
        return ids.Distinct().ToList();
    }

    private static (long A, long B) NormalizePair(long id1, long id2)
        => id1 <= id2 ? (id1, id2) : (id2, id1);

    private static string NormalizeTitleForDedupe(string title)
    {
        return string.Join(" ", title.Trim().ToLower(CultureInfo.InvariantCulture)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record CompiledCorpus(
        ExploreStatsSummary Stats,
        IReadOnlyList<ExploreWorkItem> AllWorks,
        IReadOnlyDictionary<long, ExploreAuthorRow> AuthorsById,
        IReadOnlyDictionary<long, IReadOnlyList<string>> AuthorAliasesById,
        IReadOnlyDictionary<long, List<ExploreWorkItem>> WorksByAuthorId,
        IReadOnlyDictionary<long, int> AuthorWorkCounts,
        IReadOnlyDictionary<long, ExploreTagBadge> TagsById,
        IReadOnlyList<ExploreTagBadge> TagsByUsage,
        IReadOnlyDictionary<long, List<string>> TagAliasesById,
        IReadOnlyDictionary<long, HashSet<long>> WorkTagIdSets);
}

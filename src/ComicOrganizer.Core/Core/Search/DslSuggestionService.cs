namespace ComicOrganizer.App.Core;

public enum DslSuggestionKind
{
    /// <summary>第一階段：選擇欄位前綴（例如 @artist:、@tag:），選取後留在輸入框並自動展開該欄位的 DB 候選清單。</summary>
    FieldPrefix = 0,

    /// <summary>第二階段：選擇具體欄位值（例如作者 Flugel、標籤 女僕），選取後直接收斂為區塊化膠囊 Chip。</summary>
    TokenValue = 1,
}

public sealed record DslSuggestionItem(
    DslSuggestionKind Kind,
    SearchField Field,
    string CategoryBadge,
    string Icon,
    string DisplayText,
    string SubText,
    string InsertValue,
    bool Negated,
    int TokenStartIndex);

public sealed record DslFieldDescriptor(
    SearchField Field,
    string Key,
    string[] Aliases,
    string Label,
    string Icon,
    string Description);

/// <summary>
/// 提供支援 <c>@</c> / <c>/</c> 觸發的雙階段 DSL 自動完成與區塊化（Token Chip）解析服務。
/// </summary>
public static class DslSuggestionService
{
    public static readonly IReadOnlyList<DslFieldDescriptor> SupportedFields =
    [
        new(SearchField.Artist, "artist", ["artist", "author", "作者", "繪師"], "作者", "👤", "自動帶出資料庫作者與別名"),
        new(SearchField.Tag, "tag", ["tag", "標籤", "題材", "原作"], "標籤", "🏷️", "支援繁中譯名、英文原名與別名"),
        new(SearchField.Series, "series", ["series", "系列"], "系列", "📚", "搜尋作品系列名稱"),
        new(SearchField.TranslationGroup, "group", ["group", "translator", "scanlator", "漢化組", "社團"], "漢化組", "🌐", "搜尋漢化組或社團名稱"),
        new(SearchField.Language, "lang", ["lang", "language", "語言"], "語言", "🗣️", "依語言代碼過濾（zh / ja / en）"),
        new(SearchField.ReadStatus, "status", ["status", "read", "狀態", "未讀", "已讀"], "狀態", "📖", "未讀 / 閱讀中 / 已完成 / 放棄"),
    ];

    /// <summary>
    /// 依據目前輸入框文字計算下拉自動完成清單：
    /// 1. 若末端為 <c>@artist:xxx</c>、<c>artist:xxx</c>、<c>-@tag:xxx</c> 等已含冒號的欄位 token → 回傳 DB 內該欄位的值清單。
    /// 2. 若末端為 <c>@</c>、<c>-@</c>、<c>/</c> 或 <c>@關鍵字</c>（尚未輸入冒號）→ 回傳匹配的欄位提示清單；若已輸入關鍵字，額外附加跨類別（作者＋標籤）DB 直達候選。
    /// </summary>
    public static IReadOnlyList<DslSuggestionItem> GetSuggestions(
        IAssetMetaStore store,
        string? inputText,
        string? lang = "zh-TW",
        int maxResults = 12)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrEmpty(inputText))
        {
            return Array.Empty<DslSuggestionItem>();
        }

        // 1. 優先檢查是否已進入「欄位:值」第二階段（例如 @artist:fl 或 artist:fl）
        var activeField = SearchQueryParser.ExtractActiveFieldToken(inputText);
        if (activeField != null)
        {
            bool negated = activeField.Prefix.StartsWith('-');
            return FetchFieldValueSuggestions(
                store,
                activeField.Field,
                activeField.Value,
                negated,
                activeField.StartIndex,
                lang,
                maxResults);
        }

        // 2. 檢查最後一個 token 是否以 @、-@、/ 或 -/ 開頭（第一階段欄位選擇 + 跨類直達搜尋）
        int lastTokenStart = FindLastUnquotedTokenStart(inputText);
        if (lastTokenStart >= inputText.Length)
        {
            return Array.Empty<DslSuggestionItem>();
        }

        var rawToken = inputText[lastTokenStart..];
        bool isNegated = rawToken.StartsWith('-');
        var body = isNegated ? rawToken[1..] : rawToken;

        if (body.Length == 0 || (body[0] != '@' && body[0] != '/'))
        {
            return Array.Empty<DslSuggestionItem>();
        }

        char triggerChar = body[0];
        var partial = body[1..].Trim('"').Trim();
        if (partial.Contains(':'))
        {
            return Array.Empty<DslSuggestionItem>();
        }

        var results = new List<DslSuggestionItem>(maxResults);
        string negPrefix = isNegated ? "-" : string.Empty;

        foreach (var desc in SupportedFields)
        {
            bool matched = string.IsNullOrEmpty(partial)
                || desc.Key.StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                || desc.Label.Contains(partial, StringComparison.OrdinalIgnoreCase)
                || desc.Aliases.Any(a => a.StartsWith(partial, StringComparison.OrdinalIgnoreCase));

            if (matched)
            {
                string prefixToInsert = $"{negPrefix}{triggerChar}{desc.Key}:";
                string badge = isNegated ? $"排除{desc.Label}" : desc.Label;
                results.Add(new DslSuggestionItem(
                    DslSuggestionKind.FieldPrefix,
                    desc.Field,
                    badge,
                    desc.Icon,
                    $"{prefixToInsert}  （{desc.Label}）",
                    desc.Description,
                    prefixToInsert,
                    isNegated,
                    lastTokenStart));
            }
        }

        // 若使用者在 @ 後直接輸入了名稱（例如 @Flugel 或 @女僕），一併提供資料庫作者與標籤直達選項
        if (!string.IsNullOrWhiteSpace(partial) && results.Count < maxResults)
        {
            int remain = maxResults - results.Count;
            int authorQuota = Math.Max(2, remain / 2);
            var directAuthors = FetchArtistSuggestions(store, partial, isNegated, lastTokenStart, authorQuota);
            results.AddRange(directAuthors);

            int tagQuota = Math.Max(0, maxResults - results.Count);
            if (tagQuota > 0)
            {
                var directTags = FetchTagSuggestions(store, partial, lang, isNegated, lastTokenStart, tagQuota);
                results.AddRange(directTags);
            }
        }

        return results;
    }

    public static IReadOnlyList<DslSuggestionItem> FetchFieldValueSuggestions(
        IAssetMetaStore store,
        SearchField field,
        string? filterValue,
        bool negated,
        int tokenStartIndex,
        string? lang = "zh-TW",
        int maxResults = 12)
    {
        ArgumentNullException.ThrowIfNull(store);
        var cleanFilter = filterValue?.Trim() ?? string.Empty;

        return field switch
        {
            SearchField.Artist => FetchArtistSuggestions(store, cleanFilter, negated, tokenStartIndex, maxResults),
            SearchField.Tag => FetchTagSuggestions(store, cleanFilter, lang, negated, tokenStartIndex, maxResults),
            SearchField.Language => FetchLanguageSuggestions(store, cleanFilter, negated, tokenStartIndex, maxResults),
            SearchField.ReadStatus => FetchReadStatusSuggestions(cleanFilter, negated, tokenStartIndex),
            SearchField.Series => FetchSeriesSuggestions(store, cleanFilter, negated, tokenStartIndex, maxResults),
            SearchField.TranslationGroup => FetchGroupSuggestions(store, cleanFilter, negated, tokenStartIndex, maxResults),
            _ => Array.Empty<DslSuggestionItem>(),
        };
    }

    /// <summary>
    /// 將完整 DSL 字串拆解為「結構化條件區塊（Artist/Tag/Series/Group/Lang/Status）」與「剩餘自由文字」。
    /// </summary>
    public static (IReadOnlyList<SearchTerm> Blocks, string FreeText) SplitQueryIntoBlocksAndFreeText(string? rawDsl)
    {
        if (string.IsNullOrWhiteSpace(rawDsl))
        {
            return (Array.Empty<SearchTerm>(), string.Empty);
        }

        var parsed = SearchQueryParser.Parse(rawDsl);
        var blocks = new List<SearchTerm>();
        var freeParts = new List<string>();

        foreach (var term in parsed.Terms)
        {
            if (term.Field == SearchField.FreeText)
            {
                var formatted = SearchQueryParser.FormatTermToken(term);
                if (!string.IsNullOrEmpty(formatted))
                {
                    freeParts.Add(formatted);
                }
            }
            else
            {
                blocks.Add(term);
            }
        }

        return (blocks, string.Join(" ", freeParts));
    }

    /// <summary>
    /// 當使用者在輸入框結尾輸入空白（且引號已閉合）時，檢查最後一個完成 token 是否為結構化欄位（如 <c>@artist:Flugel </c>），
    /// 若是則將其提取為 <see cref="SearchTerm"/> 區塊並回傳剩餘輸入文字。
    /// </summary>
    public static bool TryExtractCompletedTrailingBlock(
        string? inputText,
        out SearchTerm? completedBlock,
        out string remainingInputText)
    {
        completedBlock = null;
        remainingInputText = inputText ?? string.Empty;

        if (string.IsNullOrEmpty(inputText) || !char.IsWhiteSpace(inputText[^1]))
        {
            return false;
        }

        var trimmedEnd = inputText.TrimEnd();
        if (string.IsNullOrEmpty(trimmedEnd))
        {
            return false;
        }

        // 若雙引號尚未閉合，不自動收斂
        int quoteCount = trimmedEnd.Count(c => c == '"');
        if (quoteCount % 2 != 0)
        {
            return false;
        }

        int lastTokenStart = FindLastUnquotedTokenStart(trimmedEnd);
        var lastToken = trimmedEnd[lastTokenStart..];
        var parsed = SearchQueryParser.Parse(lastToken);
        if (parsed.Terms.Count == 1 && parsed.Terms[0].Field != SearchField.FreeText)
        {
            completedBlock = parsed.Terms[0];
            remainingInputText = trimmedEnd[..lastTokenStart].TrimEnd();
            if (remainingInputText.Length > 0)
            {
                remainingInputText += " ";
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// 將已區塊化的條件膠囊與輸入框內殘留文字組合為標準 DSL 查詢字串。
    /// 若輸入框末端仍是不完整的欄位前綴（例如剛打完 <c>@</c> 或 <c>@artist:</c>），組裝查詢時自動忽略該未完成前綴。
    /// </summary>
    public static string ComposeDslQuery(IEnumerable<SearchTerm> blocks, string? trailingInputText)
    {
        var parts = new List<string>();
        foreach (var block in blocks)
        {
            var token = SearchQueryParser.FormatTermToken(block, includeAtPrefix: false);
            if (!string.IsNullOrEmpty(token))
            {
                parts.Add(token);
            }
        }

        var cleanInput = StripIncompleteTrailingTrigger(trailingInputText);
        if (!string.IsNullOrWhiteSpace(cleanInput))
        {
            var trailingParsed = SearchQueryParser.Parse(cleanInput);
            foreach (var term in trailingParsed.Terms)
            {
                var formatted = SearchQueryParser.FormatTermToken(term, includeAtPrefix: false);
                if (!string.IsNullOrEmpty(formatted))
                {
                    parts.Add(formatted);
                }
            }
        }

        return string.Join(" ", parts);
    }

    private static string StripIncompleteTrailingTrigger(string? inputText)
    {
        if (string.IsNullOrWhiteSpace(inputText))
        {
            return string.Empty;
        }

        var trimmed = inputText.TrimEnd();
        int lastStart = FindLastUnquotedTokenStart(trimmed);
        var lastToken = trimmed[lastStart..];

        // 1. 末端只是 @、-@、/ 或 -/（尚未選取欄位）
        if (lastToken is "@" or "-@" or "/" or "-/")
        {
            return trimmed[..lastStart].TrimEnd();
        }

        // 2. 末端是 @artist: 或 artist: 但冒號後還沒有值
        if (lastToken.EndsWith(':'))
        {
            var active = SearchQueryParser.ExtractActiveFieldToken(trimmed);
            if (active != null && string.IsNullOrWhiteSpace(active.Value))
            {
                return trimmed[..lastStart].TrimEnd();
            }
        }

        return trimmed;
    }

    internal static int FindLastUnquotedTokenStart(string text)
    {
        int lastTokenStart = 0;
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && char.IsWhiteSpace(text[i]))
            {
                lastTokenStart = i + 1;
            }
        }

        return lastTokenStart;
    }

    private static List<DslSuggestionItem> FetchArtistSuggestions(
        IAssetMetaStore store,
        string filterValue,
        bool negated,
        int tokenStartIndex,
        int maxResults)
    {
        var list = new List<DslSuggestionItem>();
        var usages = store.ListAuthorsByUsage();
        string badge = negated ? "排除作者" : "作者";

        if (string.IsNullOrWhiteSpace(filterValue))
        {
            foreach (var u in usages.Where(x => x.WorkCount > 0).Take(maxResults))
            {
                list.Add(new DslSuggestionItem(
                    DslSuggestionKind.TokenValue,
                    SearchField.Artist,
                    badge,
                    "👤",
                    u.Author.MainName,
                    $"{u.WorkCount:N0} 部作品",
                    u.Author.MainName,
                    negated,
                    tokenStartIndex));
            }
            return list;
        }

        var usageMap = usages.ToDictionary(u => u.Author.Id, u => u.WorkCount);
        var aliasesByAuthor = store.ListAllAuthorAliases()
            .GroupBy(a => a.AuthorId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        var matched = new List<(Author Author, string? MatchedAlias, int WorkCount, bool PrefixMatch)>();
        foreach (var u in usages)
        {
            bool mainMatch = u.Author.MainName.Contains(filterValue, StringComparison.OrdinalIgnoreCase);
            bool prefixMatch = u.Author.MainName.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase);
            string? hitAlias = null;

            if (!mainMatch && aliasesByAuthor.TryGetValue(u.Author.Id, out var aliases))
            {
                hitAlias = aliases.FirstOrDefault(al => al.Contains(filterValue, StringComparison.OrdinalIgnoreCase));
                if (hitAlias != null && hitAlias.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatch = true;
                }
            }

            if (mainMatch || hitAlias != null)
            {
                usageMap.TryGetValue(u.Author.Id, out int count);
                matched.Add((u.Author, hitAlias, count, prefixMatch));
            }
        }

        foreach (var item in matched
            .OrderByDescending(m => m.PrefixMatch)
            .ThenByDescending(m => m.WorkCount)
            .ThenBy(m => m.Author.MainName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults))
        {
            var display = item.MatchedAlias != null
                ? $"{item.Author.MainName} ({item.MatchedAlias})"
                : item.Author.MainName;

            list.Add(new DslSuggestionItem(
                DslSuggestionKind.TokenValue,
                SearchField.Artist,
                badge,
                "👤",
                display,
                $"{item.WorkCount:N0} 部作品",
                item.Author.MainName,
                negated,
                tokenStartIndex));
        }

        return list;
    }

    private static List<DslSuggestionItem> FetchTagSuggestions(
        IAssetMetaStore store,
        string filterValue,
        string? lang,
        bool negated,
        int tokenStartIndex,
        int maxResults)
    {
        var list = new List<DslSuggestionItem>();
        var targetLang = lang ?? "zh-TW";
        var allUsages = store.ListTagsByUsage().Where(t => t.AssetCount > 0).ToList();
        string badge = negated ? "排除標籤" : "標籤";

        if (allUsages.Count == 0)
        {
            return list;
        }

        if (string.IsNullOrWhiteSpace(filterValue))
        {
            var top = allUsages.Take(maxResults).ToList();
            var transMap = store.GetTagTranslations(top.Select(t => t.Id).ToList(), targetLang);
            foreach (var t in top)
            {
                transMap.TryGetValue(t.Id, out var trans);
                var display = !string.IsNullOrEmpty(trans) && !string.Equals(trans, t.Name, StringComparison.OrdinalIgnoreCase)
                    ? $"{trans} ({t.Name})"
                    : t.Name;
                var insert = !string.IsNullOrEmpty(trans) ? trans : t.Name;

                list.Add(new DslSuggestionItem(
                    DslSuggestionKind.TokenValue,
                    SearchField.Tag,
                    badge,
                    "🏷️",
                    display,
                    $"{t.AssetCount:N0} 部作品",
                    insert,
                    negated,
                    tokenStartIndex));
            }
            return list;
        }

        var transAll = store.GetTagTranslations(allUsages.Select(t => t.Id).ToList(), targetLang);
        var aliasesByTag = store.ListAllTagAliases()
            .GroupBy(a => a.TargetTagId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AliasName).ToList());

        var candidates = new List<(TagUsage Tag, string? Trans, bool PrefixMatch)>();
        foreach (var t in allUsages)
        {
            transAll.TryGetValue(t.Id, out var trans);
            bool nameHit = t.Name.Contains(filterValue, StringComparison.OrdinalIgnoreCase);
            bool transHit = !string.IsNullOrEmpty(trans) && trans.Contains(filterValue, StringComparison.OrdinalIgnoreCase);
            bool aliasHit = !nameHit && !transHit
                            && aliasesByTag.TryGetValue(t.Id, out var aliases)
                            && aliases.Any(a => a.Contains(filterValue, StringComparison.OrdinalIgnoreCase));

            if (nameHit || transHit || aliasHit)
            {
                bool prefix = t.Name.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase)
                              || (!string.IsNullOrEmpty(trans) && trans.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase));
                candidates.Add((t, trans, prefix));
            }
        }

        foreach (var c in candidates
            .OrderByDescending(x => x.PrefixMatch)
            .ThenByDescending(x => x.Tag.AssetCount)
            .ThenBy(x => x.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults))
        {
            var display = !string.IsNullOrEmpty(c.Trans) && !string.Equals(c.Trans, c.Tag.Name, StringComparison.OrdinalIgnoreCase)
                ? $"{c.Trans} ({c.Tag.Name})"
                : c.Tag.Name;
            var insert = !string.IsNullOrEmpty(c.Trans) ? c.Trans : c.Tag.Name;

            list.Add(new DslSuggestionItem(
                DslSuggestionKind.TokenValue,
                SearchField.Tag,
                badge,
                "🏷️",
                display,
                $"{c.Tag.AssetCount:N0} 部作品",
                insert,
                negated,
                tokenStartIndex));
        }

        return list;
    }

    private static List<DslSuggestionItem> FetchLanguageSuggestions(
        IAssetMetaStore store,
        string filterValue,
        bool negated,
        int tokenStartIndex,
        int maxResults)
    {
        var list = new List<DslSuggestionItem>();
        string badge = negated ? "排除語言" : "語言";

        foreach (var b in store.ListLanguageBuckets())
        {
            if (string.IsNullOrWhiteSpace(b.Language))
            {
                continue;
            }

            var friendly = GetFriendlyLangName(b.Language);
            if (string.IsNullOrWhiteSpace(filterValue)
                || b.Language.Contains(filterValue, StringComparison.OrdinalIgnoreCase)
                || friendly.Contains(filterValue, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new DslSuggestionItem(
                    DslSuggestionKind.TokenValue,
                    SearchField.Language,
                    badge,
                    "🗣️",
                    $"{friendly} ({b.Language})",
                    $"{b.WorkCount:N0} 部作品",
                    b.Language,
                    negated,
                    tokenStartIndex));

                if (list.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return list;
    }

    private static List<DslSuggestionItem> FetchReadStatusSuggestions(
        string filterValue,
        bool negated,
        int tokenStartIndex)
    {
        var statuses = new (string Code, string Name, string Hint)[]
        {
            ("unread", "未讀", "尚未開始閱讀的作品"),
            ("reading", "進行中", "閱讀中且保留上次頁碼的作品"),
            ("completed", "已完成", "已讀完的作品"),
            ("dropped", "放棄", "標記為放棄的作品"),
        };

        string badge = negated ? "排除狀態" : "狀態";
        var list = new List<DslSuggestionItem>();

        foreach (var (code, name, hint) in statuses)
        {
            if (string.IsNullOrWhiteSpace(filterValue)
                || code.Contains(filterValue, StringComparison.OrdinalIgnoreCase)
                || name.Contains(filterValue, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new DslSuggestionItem(
                    DslSuggestionKind.TokenValue,
                    SearchField.ReadStatus,
                    badge,
                    "📖",
                    $"{name} ({code})",
                    hint,
                    code,
                    negated,
                    tokenStartIndex));
            }
        }

        return list;
    }

    private static List<DslSuggestionItem> FetchGroupSuggestions(
        IAssetMetaStore store,
        string filterValue,
        bool negated,
        int tokenStartIndex,
        int maxResults)
    {
        var list = new List<DslSuggestionItem>();
        string badge = negated ? "排除漢化組" : "漢化組";

        var snapshot = store.GetExploreCorpusSnapshot();
        var groups = snapshot.Works
            .Where(w => !string.IsNullOrWhiteSpace(w.TranslationGroup))
            .GroupBy(w => w.TranslationGroup!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .Where(g => string.IsNullOrWhiteSpace(filterValue)
                        || g.Name.Contains(filterValue, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => !string.IsNullOrWhiteSpace(filterValue)
                                    && g.Name.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults);

        foreach (var g in groups)
        {
            list.Add(new DslSuggestionItem(
                DslSuggestionKind.TokenValue,
                SearchField.TranslationGroup,
                badge,
                "🌐",
                g.Name,
                $"{g.Count:N0} 部作品",
                g.Name,
                negated,
                tokenStartIndex));
        }

        return list;
    }

    private static List<DslSuggestionItem> FetchSeriesSuggestions(
        IAssetMetaStore store,
        string filterValue,
        bool negated,
        int tokenStartIndex,
        int maxResults)
    {
        var list = new List<DslSuggestionItem>();
        string badge = negated ? "排除系列" : "系列";

        // 透過 ListWorksBySeries 或全庫已標記 series 的作品統計
        var candidatePaths = string.IsNullOrWhiteSpace(filterValue)
            ? store.ListAllAssetPaths().Take(400).ToList()
            : store.ListWorksBySeries(filterValue);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidatePaths)
        {
            var detail = store.GetWorkDetail(path);
            if (!string.IsNullOrWhiteSpace(detail?.Series))
            {
                var s = detail.Series.Trim();
                if (string.IsNullOrWhiteSpace(filterValue) || s.Contains(filterValue, StringComparison.OrdinalIgnoreCase))
                {
                    counts[s] = counts.TryGetValue(s, out int c) ? c + 1 : 1;
                }
            }
        }

        foreach (var kv in counts
            .OrderByDescending(x => !string.IsNullOrWhiteSpace(filterValue)
                                    && x.Key.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults))
        {
            list.Add(new DslSuggestionItem(
                DslSuggestionKind.TokenValue,
                SearchField.Series,
                badge,
                "📚",
                kv.Key,
                $"{kv.Value:N0} 部作品",
                kv.Key,
                negated,
                tokenStartIndex));
        }

        return list;
    }

    private static string GetFriendlyLangName(string code) => code.ToLowerInvariant() switch
    {
        "chinese" or "zh" or "zh-tw" or "zh-cn" => "中文",
        "japanese" or "ja" => "日文",
        "english" or "en" => "英文",
        "korean" or "ko" => "韓文",
        _ => code,
    };
}

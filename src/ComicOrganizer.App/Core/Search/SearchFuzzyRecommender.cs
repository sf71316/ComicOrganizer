using FuzzySharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 搜尋相近建議項目。
/// </summary>
public record SearchRecommendation(
    string DisplayText,
    string ReplacementQuery,
    int Score
);

/// <summary>
/// 當 DSL 搜尋為 0 筆時，使用 FuzzySharp 比對資料庫中相近的作者、標籤與譯名。
/// </summary>
public static class SearchFuzzyRecommender
{
    public static IReadOnlyList<SearchRecommendation> FindRecommendations(
        IAssetMetaStore store,
        string rawQuery,
        SearchQuery query,
        string? lang = null,
        int maxResults = 3,
        int minScore = 65)
    {
        if (string.IsNullOrWhiteSpace(rawQuery) || query.IsEmpty)
            return Array.Empty<SearchRecommendation>();

        var candidates = new List<SearchRecommendation>();

        // 讀取資料庫現有作者、作者別名與標籤（以及批次載入標籤翻譯，避免 N+1 查詢）
        var authors = store.ListAuthorsByUsage();
        var aliasesByAuthor = store.ListAllAuthorAliases()
            .GroupBy(a => a.AuthorId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var tags = store.ListTagsByUsage();
        var targetLang = lang ?? "zh-TW";
        var tagTranslations = tags.Count > 0
            ? store.GetTagTranslations(tags.Select(t => t.Id).ToList(), targetLang)
            : new Dictionary<long, string>();

        foreach (var term in query.Terms)
        {
            if (term.Negated || string.IsNullOrWhiteSpace(term.Value))
                continue;

            switch (term.Field)
            {
                case SearchField.Artist:
                    foreach (var a in authors)
                    {
                        aliasesByAuthor.TryGetValue(a.Author.Id, out var aliasList);
                        int score = ScoreAuthor(term.Value, a.Author.MainName, aliasList, out bool isExact);
                        if (score >= minScore && !isExact)
                        {
                            var repVal = QuoteIfNeeded(a.Author.MainName);
                            var newPart = "artist:" + repVal;
                            var newQuery = ReplaceFieldTerm(rawQuery, ArtistFieldAliases, term.Value, newPart);
                            candidates.Add(new SearchRecommendation(newPart, newQuery, score));
                        }
                    }
                    break;

                case SearchField.Tag:
                    foreach (var t in tags)
                    {
                        tagTranslations.TryGetValue(t.Id, out var trans);
                        int scoreOrig = Fuzz.WeightedRatio(term.Value, t.Name);
                        int scoreTrans = !string.IsNullOrEmpty(trans) ? Fuzz.WeightedRatio(term.Value, trans) : 0;
                        int score = Math.Max(scoreOrig, scoreTrans);

                        if (score >= minScore)
                        {
                            var bestTarget = scoreTrans >= scoreOrig && !string.IsNullOrEmpty(trans) ? trans : t.Name;
                            if (!string.Equals(term.Value, t.Name, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(term.Value, trans, StringComparison.OrdinalIgnoreCase))
                            {
                                var repVal = QuoteIfNeeded(bestTarget);
                                var newPart = "tag:" + repVal;
                                var newQuery = ReplaceFieldTerm(rawQuery, TagFieldAliases, term.Value, newPart);
                                candidates.Add(new SearchRecommendation(newPart, newQuery, score));
                            }
                        }
                    }
                    break;

                case SearchField.FreeText:
                    // 1. 純文字接近某作者（主名或別名）→ 推薦改為 artist:xxx
                    foreach (var a in authors)
                    {
                        aliasesByAuthor.TryGetValue(a.Author.Id, out var aliasList);
                        int score = ScoreAuthor(term.Value, a.Author.MainName, aliasList, out _);
                        if (score >= minScore)
                        {
                            var repVal = QuoteIfNeeded(a.Author.MainName);
                            var newPart = "artist:" + repVal;
                            var newQuery = ReplaceFreeTextTerm(rawQuery, term.Value, newPart);
                            candidates.Add(new SearchRecommendation(newPart, newQuery, score));
                        }
                    }

                    // 2. 純文字接近某標籤或譯名 → 推薦改為 tag:xxx
                    foreach (var t in tags)
                    {
                        tagTranslations.TryGetValue(t.Id, out var trans);
                        int scoreOrig = Fuzz.WeightedRatio(term.Value, t.Name);
                        int scoreTrans = !string.IsNullOrEmpty(trans) ? Fuzz.WeightedRatio(term.Value, trans) : 0;
                        int score = Math.Max(scoreOrig, scoreTrans);

                        if (score >= minScore)
                        {
                            var bestTarget = scoreTrans >= scoreOrig && !string.IsNullOrEmpty(trans) ? trans : t.Name;
                            var repVal = QuoteIfNeeded(bestTarget);
                            var newPart = "tag:" + repVal;
                            var newQuery = ReplaceFreeTextTerm(rawQuery, term.Value, newPart);
                            candidates.Add(new SearchRecommendation(newPart, newQuery, score));
                        }
                    }
                    break;
            }
        }

        // 依相似度分數降序排列，去除重複建議
        return candidates
            .GroupBy(c => c.DisplayText, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(c => c.Score)
            .Take(maxResults)
            .ToList();
    }

    private static readonly string[] ArtistFieldAliases = { "artist", "author" };
    private static readonly string[] TagFieldAliases = { "tag" };

    private static int ScoreAuthor(string input, string mainName, List<AuthorAlias>? aliases, out bool isExact)
    {
        isExact = string.Equals(input, mainName, StringComparison.OrdinalIgnoreCase);
        int best = Fuzz.WeightedRatio(input, mainName);
        if (aliases != null)
        {
            foreach (var al in aliases)
            {
                if (string.Equals(input, al.Name, StringComparison.OrdinalIgnoreCase))
                    isExact = true;
                int s = Fuzz.WeightedRatio(input, al.Name);
                if (s > best) best = s;
            }
        }
        return best;
    }

    private static string QuoteIfNeeded(string val) =>
        val.Contains(' ') || val.Contains('\t') ? $"\"{val}\"" : val;

    private static string ReplaceFieldTerm(string rawQuery, string[] fieldAliases, string termValue, string replacement)
    {
        foreach (var alias in fieldAliases)
        {
            // 先嘗試有引號的寫法（如 author:"foo bar"），再嘗試無引號寫法（如 author:foo）
            var quotedTarget = $"{alias}:\"{termValue}\"";
            int pos = rawQuery.IndexOf(quotedTarget, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0)
                return rawQuery[..pos] + replacement + rawQuery[(pos + quotedTarget.Length)..];

            var bareTarget = $"{alias}:{termValue}";
            pos = rawQuery.IndexOf(bareTarget, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0)
                return rawQuery[..pos] + replacement + rawQuery[(pos + bareTarget.Length)..];
        }
        return rawQuery;
    }

    private static string ReplaceFreeTextTerm(string rawQuery, string termValue, string replacement)
    {
        var quotedTarget = $"\"{termValue}\"";
        int pos = rawQuery.IndexOf(quotedTarget, StringComparison.OrdinalIgnoreCase);
        if (pos >= 0)
            return rawQuery[..pos] + replacement + rawQuery[(pos + quotedTarget.Length)..];

        pos = rawQuery.IndexOf(termValue, StringComparison.OrdinalIgnoreCase);
        if (pos >= 0)
            return rawQuery[..pos] + replacement + rawQuery[(pos + termValue.Length)..];

        return rawQuery;
    }
}

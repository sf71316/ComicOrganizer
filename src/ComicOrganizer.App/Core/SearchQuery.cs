namespace ComicOrganizer.App.Core;

/// <summary>
/// 搜尋 DSL（#48 Phase C）—— 結構化查詢語法 parser + 結果型別。
///
/// Grammar：
///   query     := term ( WS+ term )*
///   term      := [-]? ( field_term | bare_term )
///   field_term := field ":" value
///   field     := "artist"|"author"|"tag"|"language"|"lang"|"read"|"status"
///   value     := bare_word | quoted_string
///   bare_term := bare_word | quoted_string                        // 純文字
///   quoted_string := '"' ... '"'
///
/// 範例：
///   artist:flugel
///   tag:"big breasts"
///   language:zh -tag:DL版
///   "くぎづけ" read:reading
///   foo bar                              // 兩個純文字（AND）
///
/// 多 term 邏輯：positive AND，negative 從結果集 except。
/// 無法識別的 field（"unknown:foo"）退化成純文字保留原 token。
/// </summary>
public static class SearchQueryParser
{
    private static readonly Dictionary<string, SearchField> FieldAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "artist", SearchField.Artist }, { "author", SearchField.Artist },
            { "tag", SearchField.Tag },
            { "language", SearchField.Language }, { "lang", SearchField.Language },
            { "read", SearchField.ReadStatus }, { "status", SearchField.ReadStatus },
        };

    public static SearchQuery Parse(string? input)
    {
        var query = new SearchQuery();
        if (string.IsNullOrWhiteSpace(input)) return query;

        int i = 0;
        while (i < input.Length)
        {
            // skip WS
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i >= input.Length) break;

            bool negated = false;
            if (input[i] == '-')
            {
                // 確認下一字不是 WS / EOF（避免單獨 '-' 被吃成 negated empty）
                if (i + 1 < input.Length && !char.IsWhiteSpace(input[i + 1]))
                {
                    negated = true;
                    i++;
                }
            }

            // 純文字 quoted token：直接整段取出
            if (input[i] == '"')
            {
                i++;
                int qs = i;
                while (i < input.Length && input[i] != '"') i++;
                var qval = input[qs..i];
                if (i < input.Length) i++;   // skip closing quote
                if (!string.IsNullOrEmpty(qval))
                    query.Terms.Add(new SearchTerm(SearchField.FreeText, qval, negated));
                continue;
            }

            // 讀 head（直到 ':' 或 WS）
            int hs = i;
            while (i < input.Length && input[i] != ':' && !char.IsWhiteSpace(input[i])) i++;
            var head = input[hs..i];

            if (i < input.Length && input[i] == ':')
            {
                i++;   // skip ':'
                string value;
                if (i < input.Length && input[i] == '"')
                {
                    i++;
                    int vs = i;
                    while (i < input.Length && input[i] != '"') i++;
                    value = input[vs..i];
                    if (i < input.Length) i++;
                }
                else
                {
                    int vs = i;
                    while (i < input.Length && !char.IsWhiteSpace(input[i])) i++;
                    value = input[vs..i];
                }

                if (FieldAliases.TryGetValue(head, out var field))
                {
                    if (!string.IsNullOrEmpty(value))
                        query.Terms.Add(new SearchTerm(field, value, negated));
                }
                else
                {
                    // 不認識的 field → 整段（含 head:value）退化成純文字
                    var combined = head + ":" + value;
                    if (!string.IsNullOrEmpty(combined))
                        query.Terms.Add(new SearchTerm(SearchField.FreeText, combined, negated));
                }
            }
            else
            {
                // bare word — 純文字 term
                if (!string.IsNullOrEmpty(head))
                    query.Terms.Add(new SearchTerm(SearchField.FreeText, head, negated));
            }
        }
        return query;
    }
}

public enum SearchField
{
    /// <summary>純文字 — 對 path / title 子字串模糊比對。</summary>
    FreeText,
    /// <summary>artist: / author: — 比對 author.main_name + author_alias.name。</summary>
    Artist,
    /// <summary>tag: — 比對 tag.name 跟 tag_translation.name（都包進 union）。</summary>
    Tag,
    /// <summary>language: / lang: — 比對 asset_meta.language（精準 code）。</summary>
    Language,
    /// <summary>read: / status: — unread/reading/completed/dropped。</summary>
    ReadStatus,
}

/// <summary>單一搜尋條件 — 一個 query 由多個 term 組成（positive AND，negative except）。</summary>
public record SearchTerm(SearchField Field, string Value, bool Negated);

/// <summary>parse 後的查詢結構。</summary>
public class SearchQuery
{
    public List<SearchTerm> Terms { get; } = new();
    public bool IsEmpty => Terms.Count == 0;
    public bool HasOnlyNegativeTerms => Terms.Count > 0 && Terms.All(t => t.Negated);
}

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

    /// <summary>
    /// 解析使用者當前正在輸入的欄位 token（例如 "artist:fl" 或 "-tag:"）。
    /// 若末端不是 field: 格式（如純文字、未輸入冒號或不認識的欄位）回傳 null。
    /// </summary>
    public static ActiveFieldToken? ExtractActiveFieldToken(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // 找最後一個不在引號內的 whitespace
        int lastTokenStart = 0;
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (text[i] == ' ' || text[i] == '\t'))
            {
                lastTokenStart = i + 1;
            }
        }

        if (lastTokenStart >= text.Length) return null;
        var token = text[lastTokenStart..];

        int offset = 0;
        if (token.StartsWith('-'))
        {
            offset = 1;
        }

        var colonIdx = token.IndexOf(':', offset);
        if (colonIdx < 0) return null;

        var head = token[offset..colonIdx];
        if (!FieldAliases.TryGetValue(head, out var field))
            return null;

        var prefix = token[..(colonIdx + 1)];
        var rawValue = token[(colonIdx + 1)..];

        // 若引號已經閉合（例如 tag:"big breasts"），視為該欄位 token 已完成輸入，不再觸發建議
        if (rawValue.Length >= 2 && rawValue.StartsWith('"') && rawValue.EndsWith('"'))
            return null;

        var value = rawValue.Trim('"');

        return new ActiveFieldToken(prefix, field, value, lastTokenStart);
    }

    /// <summary>
    /// 將選取的建議值帶入搜尋字串，若值含空白自動加上雙引號，並在尾部補一個空白方便繼續輸入。
    /// </summary>
    public static string ApplySuggestion(string originalText, ActiveFieldToken token, string insertValue)
    {
        var formatted = insertValue.Contains(' ') || insertValue.Contains('\t')
            ? $"\"{insertValue}\""
            : insertValue;

        var before = originalText[..token.StartIndex];
        return before + token.Prefix + formatted + " ";
    }

    /// <summary>
    /// 當使用者點擊欄位提示 Chip（例如 "artist:"、"tag:"）時，將欄位名稱帶入搜尋框。
    /// 支援空字串補完、結尾空白追加、輸入前綴補完（如 "art" -> "artist:"、"-art" -> "-artist:"）以及最後 token 為其他字詞時補空白追加。
    /// </summary>
    public static string ApplyFieldChip(string? currentText, string fieldPrefix)
    {
        if (string.IsNullOrEmpty(currentText))
        {
            return fieldPrefix;
        }

        if (char.IsWhiteSpace(currentText[^1]))
        {
            return currentText + fieldPrefix;
        }

        // 找最後一個不在引號內的 whitespace
        int lastTokenStart = 0;
        bool inQuotes = false;
        for (int i = 0; i < currentText.Length; i++)
        {
            if (currentText[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (currentText[i] == ' ' || currentText[i] == '\t'))
            {
                lastTokenStart = i + 1;
            }
        }

        if (lastTokenStart >= currentText.Length)
        {
            return currentText + fieldPrefix;
        }

        var before = currentText[..lastTokenStart];
        var lastToken = currentText[lastTokenStart..];

        var fieldName = fieldPrefix.TrimEnd(':');

        if (lastToken.StartsWith('-'))
        {
            var prefix = lastToken[1..];
            if (!prefix.Contains(':') && (prefix.Length == 0 || fieldName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return before + "-" + fieldPrefix;
            }
        }
        else
        {
            if (!lastToken.Contains(':') && fieldName.StartsWith(lastToken, StringComparison.OrdinalIgnoreCase))
            {
                return before + fieldPrefix;
            }
        }

        return currentText + " " + fieldPrefix;
    }
}

/// <summary>代表當前正在輸入且可觸發 autocomplete 的欄位 token。</summary>
public record ActiveFieldToken(string Prefix, SearchField Field, string Value, int StartIndex);

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

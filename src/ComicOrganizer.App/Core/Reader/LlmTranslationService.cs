using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ComicOrganizer.App.Core;

public enum LlmApiType
{
    OpenAiCompatible, // 適用於 LM-Studio, vLLM, OpenAI, Ollama /v1
    OllamaNative      // 適用於 Ollama 原生 /api/chat
}

public class LlmTranslationService : ITranslationService
{
    private readonly HttpClient _httpClient;
    private string _endpoint = "http://192.168.0.74:1234";
    private string _modelName = "qwen3.6-35b-a3b-mtp";
    private LlmApiType _apiType = LlmApiType.OpenAiCompatible;
    private string? _apiKey;   // 雲端 OpenAI 相容端點的 Bearer 金鑰；null=免認證(本地端)

    public string? LastError { get; private set; }

    public LlmTranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// 組 OpenAI 相容 chat/completions URL，相容各 provider 的 base 慣例：
    /// 已是完整 URL → 原樣；已含版本段(/v1、/v1beta/openai 等，如 Gemini) → 補 /chat/completions；
    /// 否則(純 host，如 LM Studio/OpenAI) → 補 /v1/chat/completions。
    /// </summary>
    public static string BuildOpenAiChatUrl(string endpoint)
    {
        var e = endpoint.TrimEnd('/');
        if (e.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return e;
        if (Regex.IsMatch(e, @"/v\d+\w*(/openai)?$", RegexOptions.IgnoreCase)) return e + "/chat/completions";
        return e + "/v1/chat/completions";
    }

    public void Configure(string endpoint, string modelName, LlmApiType apiType, string? apiKey = null)
    {
        if (!string.IsNullOrEmpty(endpoint))
        {
            _endpoint = endpoint.TrimEnd('/');
        }
        if (!string.IsNullOrEmpty(modelName))
        {
            _modelName = modelName;
        }
        _apiType = apiType;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public string Endpoint => _endpoint;
    public string ModelName => _modelName;
    public LlmApiType ApiType => _apiType;

    public async Task<List<string>> TranslateBatchAsync(List<string> texts, string fromLanguage = "ja", string toLanguage = "zh-Hant")
    {
        LastError = null;
        if (texts == null || texts.Count == 0)
            return new List<string>();

        var systemPrompt = "You are a Japanese-to-Traditional-Chinese translator for manga speech bubbles. Translate each element in the input JSON array to Traditional Chinese. " +
            "IMPORTANT: Return an empty string \"\" for ANY of the following: " +
            "(1) onomatopoeia or sound effects (e.g. ゴゴゴ, ドキドキ, ばさっ, キャー, ガタッ), " +
            "(2) single kana or single kanji without clear dialog meaning (e.g. な, あ, 伊, 十, ん), " +
            "(3) partial/fragmented text that is not a complete phrase, " +
            "(4) OCR noise or gibberish (e.g. vhah, nー, random symbols). " +
            "Only translate text that is clearly part of character dialog or narration. " +
            "Output ONLY a JSON array with the same number of elements. No explanation, no markdown. " +
            "/no_think"; // Qwen3 等推理模型的軟開關：關閉思考，直接輸出答案（翻譯任務不需長推理，避免 token 全燒在思考上）。

        // 使用 UnsafeRelaxedJsonEscaping 保留原始 UTF-8 字元（日文、中文），而非轉成 \uXXXX
        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 預先濾除噪框（純拉丁/數字、單字，如 "Jup"/"A"/"8"/"vhah"），不送進 LLM。
        // 否則模型常把「該回空字串的噪框」整格刪掉 → 回傳元素變少 → 後續譯文整批往前位移 → 對錯氣泡。
        var realIdx = new List<int>();
        var realTexts = new List<string>();
        for (int i = 0; i < texts.Count; i++)
        {
            if (!IsLikelyNoise(texts[i]))
            {
                realIdx.Add(i);
                realTexts.Add(texts[i]);
            }
        }
        // 全部都是噪框：直接回傳等長空字串
        if (realTexts.Count == 0)
        {
            var allEmpty = new List<string>();
            for (int i = 0; i < texts.Count; i++) allEmpty.Add("");
            return allEmpty;
        }
        var userContent = JsonSerializer.Serialize(realTexts, jsonOptions);

        // Few-shot 範例，包含狀聲詞/擬聲詞與雜訊過濾範例，幫助模型理解輸入/輸出格式
        var exampleUserContent = "[\"こんにちは\",\"ゴゴゴ\",\"ありがとう\",\"ドキドキ\",\"お兄さま\",\"あ、あれは…\",\"キャー\",\"vhah\",\"伊\",\"な\",\"ばさっ\",\"十\",\"もうしばらくご辛抱を…\"]";
        var exampleAssistantContent = "[\"你好\",\"\",\"謝謝\",\"\",\"哥哥大人\",\"啊、那是…\",\"\",\"\",\"\",\"\",\"\",\"\",\"請再忍耐一下…\"]";

        string requestUrl;
        object requestBody;

        if (_apiType == LlmApiType.OpenAiCompatible)
        {
            requestUrl = BuildOpenAiChatUrl(_endpoint);
            requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = exampleUserContent },
                    new { role = "assistant", content = exampleAssistantContent },
                    new { role = "user", content = userContent }
                },
                temperature = 0.3,
                max_tokens = 4096, // 上限：關閉思考後輸出僅 JSON 陣列很短，給足餘裕避免被 server 預設值截斷
                stream = false
            };
        }
        else
        {
            requestUrl = $"{_endpoint}/api/chat";
            requestBody = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = exampleUserContent },
                    new { role = "assistant", content = exampleAssistantContent },
                    new { role = "user", content = userContent }
                },
                options = new
                {
                    temperature = 0.3,
                    num_predict = 4096 // Ollama 的輸出 token 上限（對應 OpenAI max_tokens）
                },
                stream = false,
                format = "json"
            };
        }

        try
        {
            // 重試條件：解析失敗，或回傳數量與送出的（已濾噪）數量不符（位移風險）。
            // aligned = 數量正確的結果；bestEffort = 至少可解析（保底，勝過整批 fallback 回原文）。
            List<string>? aligned = null;
            List<string>? bestEffort = null;
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts && aligned == null; attempt++)
            {
                if (attempt > 1)
                {
                    System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] 結果不可用（解析失敗或數量不符 {bestEffort?.Count}/{realTexts.Count}），重試第 {attempt}/{maxAttempts} 次。");
                }
                var parsed = await RequestAndParseAsync(requestUrl, requestBody, jsonOptions);
                if (parsed != null)
                {
                    bestEffort = parsed;
                    if (parsed.Count == realTexts.Count) aligned = parsed;
                }
            }

            var chosen = aligned ?? bestEffort;
            if (chosen != null)
            {
                // 對齊到已濾噪的 realTexts 長度（多截少補空）
                var realResult = new List<string>(chosen);
                while (realResult.Count < realTexts.Count) realResult.Add("");
                if (realResult.Count > realTexts.Count) realResult = realResult.GetRange(0, realTexts.Count);

                // 還原回完整長度：噪框位置補空字串，其餘填回對應譯文
                var full = new List<string>();
                for (int i = 0; i < texts.Count; i++) full.Add("");
                for (int k = 0; k < realIdx.Count; k++)
                {
                    full[realIdx[k]] = realResult[k]?.Trim() ?? "";
                }
                return full;
            }

            throw new Exception("Failed to deserialize translation JSON after all attempts (strict + lenient salvage + retry).");
        }
        catch (Exception ex)
        {
            // 降級處理：發生任何錯誤皆回傳原始未翻譯的文字，防止程式卡死
            LastError = ex.ToString();
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService ERROR] {ex.Message}");
            return new List<string>(texts);
        }
    }

    /// <summary>
    /// 送出一次 LLM 請求並嘗試解析譯文陣列。
    /// HTTP 失敗丟例外（交外層降級）；解析失敗（含寬鬆容錯）回 null（交外層重試）。
    /// 解析順序：嚴格 List&lt;string&gt; → wrapper{"results"} → 混合內容萃取合法 JSON → 寬鬆容錯救援。
    /// </summary>
    private async Task<List<string>?> RequestAndParseAsync(string requestUrl, object requestBody, JsonSerializerOptions jsonOptions)
    {
        var jsonPayload = JsonSerializer.Serialize(requestBody, jsonOptions);
        System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Request payload (first 300 chars): {jsonPayload[..Math.Min(jsonPayload.Length, 300)]}");
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // 雲端 OpenAI 相容端點需 Authorization: Bearer；以 HttpRequestMessage 帶每請求標頭(不污染共用 HttpClient)
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
        if (!string.IsNullOrEmpty(_apiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) - Response: {errorContent}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Raw API response (first 500 chars): {responseJson[..Math.Min(responseJson.Length, 500)]}");
        string rawContent = "";

        using (var doc = JsonDocument.Parse(responseJson))
        {
            var root = doc.RootElement;
            if (_apiType == LlmApiType.OpenAiCompatible)
            {
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message))
                {
                    // 優先取 content
                    if (message.TryGetProperty("content", out var contentProp))
                    {
                        rawContent = contentProp.GetString() ?? "";
                    }
                    // 如果 content 為空，嘗試 reasoning_content (部分 thinking 模型)
                    if (string.IsNullOrEmpty(rawContent) && message.TryGetProperty("reasoning_content", out var reasoningProp))
                    {
                        System.Diagnostics.Debug.WriteLine("[LlmTranslationService] content was empty, found reasoning_content field.");
                        rawContent = reasoningProp.GetString() ?? "";
                    }
                }
            }
            else
            {
                if (root.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    rawContent = contentProp.GetString() ?? "";
                }
            }
        }

        // 處理 thinking 模型的 <think>...</think> 標籤
        rawContent = StripThinkingTags(rawContent);

        if (string.IsNullOrEmpty(rawContent))
        {
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] 空回應，觸發重試。Raw JSON (first 300): {responseJson[..Math.Min(responseJson.Length, 300)]}");
            return null;
        }

        rawContent = StripMarkdownJson(rawContent);

        // 1. 嚴格解析 List<string>
        try
        {
            var direct = JsonSerializer.Deserialize<List<string>>(rawContent);
            if (direct != null) return direct;
        }
        catch { /* 換下一個解析策略 */ }

        // 2. wrapper 物件 {"results": [...]}
        try
        {
            var wrapper = JsonSerializer.Deserialize<LlmResponseWrapper>(rawContent);
            if (wrapper?.Results != null) return wrapper.Results;
        }
        catch { /* 換下一個解析策略 */ }

        // 3. 從混合內容萃取「合法」JSON 陣列（thinking 文字夾雜 JSON）
        var extracted = ExtractJsonArrayFromMixedContent(rawContent);
        if (extracted != null) return extracted;

        // 4. 寬鬆容錯解析：救援小模型常見壞 JSON（最後一格漏結尾引號、以全形 」 當引號、元素未加引號等）
        var lenient = ParseLenientArray(rawContent);
        if (lenient != null)
        {
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] 寬鬆容錯解析救回 {lenient.Count} 個元素。");
            return lenient;
        }

        System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] 全部解析失敗 (len={rawContent.Length}): {rawContent[..Math.Min(rawContent.Length, 200)]}");
        return null;
    }

    /// <summary>
    /// 寬鬆容錯解析字串陣列，專治小模型輸出的「幾乎合法」JSON。
    /// 逐元素掃描，能處理：最後一格未閉合引號、以全形 」/』 當作結尾引號、個別元素漏引號。
    /// CJK 對白幾乎不含 ASCII 雙引號，故以 ", / "] / [" 作為元素邊界判斷相當穩。
    /// 救不回（找不到 '['、無任何元素）時回 null。
    /// </summary>
    public static List<string>? ParseLenientArray(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int start = text.IndexOf('[');
        if (start < 0) return null;
        int lastBracket = text.LastIndexOf(']');
        int bodyEnd = lastBracket > start ? lastBracket : text.Length; // 無收尾 ] 就掃到結尾

        var result = new List<string>();
        int i = start + 1;
        while (i < bodyEnd)
        {
            // 跳過元素間的空白與逗號
            while (i < bodyEnd && (char.IsWhiteSpace(text[i]) || text[i] == ',')) i++;
            if (i >= bodyEnd) break;

            if (text[i] == '"')
            {
                i++; // 跳過開頭引號
                var sb = new StringBuilder();
                bool closed = false;
                while (i < bodyEnd)
                {
                    char c = text[i];
                    if (c == '\\' && i + 1 < bodyEnd)
                    {
                        char n = text[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', '/' => '/', _ => n });
                        i += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        // 視後一個非空白字元是否為 , 或 ] 判斷是否真的是結尾引號
                        int j = i + 1;
                        while (j < bodyEnd && char.IsWhiteSpace(text[j])) j++;
                        if (j >= bodyEnd || text[j] == ',' || text[j] == ']')
                        {
                            i++;
                            closed = true;
                            break;
                        }
                        sb.Append('"'); i++; continue; // 文字內的引號（極少見）
                    }
                    sb.Append(c); i++;
                }
                string val = sb.ToString();
                if (!closed)
                {
                    // 未正常閉合：模型常把結尾的 ASCII 引號寫成全形 」/』，去掉一個結尾全形/彎引號還原原意
                    val = val.TrimEnd();
                    if (val.Length > 0 && (val[^1] == '」' || val[^1] == '』' || val[^1] == '”' || val[^1] == '"'))
                        val = val.Substring(0, val.Length - 1);
                }
                result.Add(val);
            }
            else
            {
                // 元素未加引號：讀到下一個頂層逗號（或結尾），去除包圍的引號/全形括號
                int seg = i;
                while (i < bodyEnd && text[i] != ',') i++;
                string val = text.Substring(seg, i - seg).Trim();
                val = val.Trim('「', '」', '『', '』', '"', '”', '“');
                result.Add(val);
            }
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 判斷一段 OCR 文字是否為「噪框」（不該送進 LLM 翻譯）：
    /// 空白、單一字元，或完全不含 CJK/假名（純拉丁字母/數字/符號，如 "Jup"、"A"、"8"、"vhah"）。
    /// 濾掉這些可避免模型把該回空字串的格子整格刪掉，造成回傳數量變少、譯文整批位移。
    /// 注意：含假名的擬聲詞（ゴゴゴ、ばさっ）仍會送進 LLM，由 LLM 依規則翻成空字串。
    /// </summary>
    public static bool IsLikelyNoise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        string s = text.Trim();
        if (s.Length <= 1) return true;
        return !HasCjk(s);
    }

    private static bool HasCjk(string s)
    {
        foreach (char c in s)
        {
            // 平假名 U+3040–309F、片假名 U+30A0–30FF、CJK 統一表意 U+4E00–9FFF、半形片假名 U+FF66–FF9D
            if ((c >= '぀' && c <= 'ヿ') ||
                (c >= '一' && c <= '鿿') ||
                (c >= 'ｦ' && c <= 'ﾝ'))
                return true;
        }
        return false;
    }

    private class LlmResponseWrapper
    {
        [JsonPropertyName("results")]
        public List<string>? Results { get; set; }
    }

    /// <summary>
    /// 移除 thinking 模型回傳的 &lt;think&gt;...&lt;/think&gt; 標籤，只保留標籤之後的實際內容。
    /// 如果沒有 think 標籤則原樣回傳。
    /// </summary>
    public static string StripThinkingTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        
        // 1. 處理 <think>...</think> 標籤包裹
        const string closeTag = "</think>";
        int closeIndex = text.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
        if (closeIndex >= 0)
        {
            text = text.Substring(closeIndex + closeTag.Length);
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Stripped <think> tags, remaining content length: {text.Trim().Length}");
            return text.Trim();
        }
        
        // 2. 處理只有 <think> 開頭但沒有閉合的情況（模型被截斷）
        const string openTag = "<think>";
        if (text.TrimStart().StartsWith(openTag, StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine("[LlmTranslationService] Content starts with <think> but no closing tag found - model response may have been truncated.");
            return "";
        }
        
        // 3. 處理無標籤的 thinking 輸出（例如 "Thinking Process:" 或 "**Thinking**" 開頭）
        //    嘗試找到第一個 JSON 陣列的起始位置
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("["))
        {
            // 內容不是以 JSON 陣列開頭，可能含有 thinking 文字
            // 嘗試從內容中找到第一個 [" 作為 JSON 陣列開始
            int jsonStart = text.IndexOf("[\"");
            if (jsonStart > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Found potential JSON array at position {jsonStart}, stripping {jsonStart} chars of preceding text.");
                text = text.Substring(jsonStart);
            }
        }
        
        return text.Trim();
    }

    public static string StripMarkdownJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Trim();

        // 移除常見的 ```json ... ``` 包裝
        if (text.StartsWith("```"))
        {
            int firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd != -1)
            {
                text = text.Substring(firstLineEnd + 1);
            }
            else
            {
                text = text.Substring(3);
            }

            if (text.EndsWith("```"))
            {
                text = text.Substring(0, text.Length - 3);
            }
        }
        return text.Trim();
    }

    /// <summary>
    /// 從混合文字內容中萃取 JSON 陣列。
    /// 使用正規表達式定位 JSON 字串陣列，避免被 thinking 文字中的括號混淆。
    /// </summary>
    public static List<string>? ExtractJsonArrayFromMixedContent(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // 策略：從文字中找出所有以 [" 開頭的位置，嘗試從每個位置解析 JSON 陣列
        // 從後往前搜尋，因為實際翻譯通常在 thinking 文字之後
        var candidates = new List<int>();
        int searchFrom = text.Length - 1;
        while (searchFrom >= 0)
        {
            int pos = text.LastIndexOf("[\"", searchFrom, StringComparison.Ordinal);
            if (pos < 0) break;
            candidates.Add(pos);
            searchFrom = pos - 1;
        }

        foreach (int startPos in candidates)
        {
            // 從 startPos 開始找對應的 ]
            int depth = 0;
            int endPos = -1;
            bool inString = false;
            bool escaped = false;
            
            for (int i = startPos; i < text.Length; i++)
            {
                char c = text[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\' && inString) { escaped = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) { endPos = i; break; }
                }
            }

            if (endPos < 0) continue;

            string jsonCandidate = text.Substring(startPos, endPos - startPos + 1);
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Trying JSON candidate at pos {startPos} (length={jsonCandidate.Length}): {jsonCandidate[..Math.Min(jsonCandidate.Length, 150)]}");

            try
            {
                var result = JsonSerializer.Deserialize<List<string>>(jsonCandidate);
                if (result != null && result.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Successfully extracted JSON array with {result.Count} elements.");
                    return result;
                }
            }
            catch
            {
                // 這個候選不是合法 JSON，繼續嘗試下一個
            }
        }

        System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] No valid JSON array found in mixed content (length={text.Length}).");
        return null;
    }
}

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

    public string? LastError { get; private set; }

    public LlmTranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public void Configure(string endpoint, string modelName, LlmApiType apiType)
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
            "Output ONLY a JSON array with the same number of elements. No explanation, no markdown.";

        // 使用 UnsafeRelaxedJsonEscaping 保留原始 UTF-8 字元（日文、中文），而非轉成 \uXXXX
        var jsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var userContent = JsonSerializer.Serialize(texts, jsonOptions);

        // Few-shot 範例，包含狀聲詞/擬聲詞與雜訊過濾範例，幫助模型理解輸入/輸出格式
        var exampleUserContent = "[\"こんにちは\",\"ゴゴゴ\",\"ありがとう\",\"ドキドキ\",\"お兄さま\",\"あ、あれは…\",\"キャー\",\"vhah\",\"伊\",\"な\",\"ばさっ\",\"十\",\"もうしばらくご辛抱を…\"]";
        var exampleAssistantContent = "[\"你好\",\"\",\"謝謝\",\"\",\"哥哥大人\",\"啊、那是…\",\"\",\"\",\"\",\"\",\"\",\"\",\"請再忍耐一下…\"]";

        string requestUrl;
        object requestBody;

        if (_apiType == LlmApiType.OpenAiCompatible)
        {
            requestUrl = $"{_endpoint}/v1/chat/completions";
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
                    temperature = 0.3
                },
                stream = false,
                format = "json"
            };
        }

        try
        {
            var jsonPayload = JsonSerializer.Serialize(requestBody, jsonOptions);
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService] Request payload (first 300 chars): {jsonPayload[..Math.Min(jsonPayload.Length, 300)]}");
            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            // 設定適當的 timeout，避免本機推論過久導致系統崩潰，可交由 HttpClient 配置，此處設為 60 秒
            var response = await _httpClient.PostAsync(requestUrl, content);
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
                throw new Exception($"LLM returned an empty response content. Raw JSON (first 300 chars): {responseJson[..Math.Min(responseJson.Length, 300)]}");
            }

            rawContent = StripMarkdownJson(rawContent);

            // 雙重安全解析 JSON 
            List<string>? translations = null;
            try
            {
                // 1. 嘗試直接解析為 List<string> (防呆)
                translations = JsonSerializer.Deserialize<List<string>>(rawContent);
            }
            catch
            {
                // 忽略錯誤，繼續下一個解析嘗試
            }

            if (translations == null)
            {
                try
                {
                    // 2. 嘗試解析為 LlmResponseWrapper 物件 {"results": [...]}
                    var wrapper = JsonSerializer.Deserialize<LlmResponseWrapper>(rawContent);
                    translations = wrapper?.Results;
                }
                catch
                {
                    // 忽略錯誤
                }
            }

            if (translations == null)
            {
                // 3. 最後嘗試：從混合文字中萃取 JSON 陣列（模型可能回傳了推理文字夾雜 JSON）
                translations = ExtractJsonArrayFromMixedContent(rawContent);
            }

            if (translations != null)
            {
                var resultList = new List<string>(translations);
                
                // 確保回傳的陣列長度與輸入一致
                while (resultList.Count < texts.Count)
                {
                    resultList.Add("");
                }
                if (resultList.Count > texts.Count)
                {
                    resultList = resultList.GetRange(0, texts.Count);
                }
                
                for (int i = 0; i < resultList.Count; i++)
                {
                    resultList[i] = resultList[i]?.Trim() ?? "";
                }
                return resultList;
            }

            throw new Exception($"Failed to deserialize translation JSON (content length={rawContent.Length}): {rawContent[..Math.Min(rawContent.Length, 200)]}");
        }
        catch (Exception ex)
        {
            // 降級處理：發生任何錯誤皆回傳原始未翻譯的文字，防止程式卡死
            LastError = ex.ToString();
            System.Diagnostics.Debug.WriteLine($"[LlmTranslationService ERROR] {ex.Message}");
            return new List<string>(texts);
        }
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

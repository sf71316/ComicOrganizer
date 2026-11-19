using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComicOrganizer.App.Core;

/// <summary>
/// nhentai API client 實作（IMetadataAPIClient）。
///
/// 認證：Authorization: Key {apiKey} 標頭（API Key 模式，nhk_... 開頭）。
/// 限速：1.5 秒/請求 throttle，避免 45/分鐘上限。
/// 端點：
///   GET /api/v2/galleries/{id}        — 單本完整 metadata
///   GET /api/v2/search?query=...&page=N — 搜尋
///   GET /api/v2/user                  — 驗證 token
///
/// raw_json 寫進 asset_external_source 前剝掉 pages / related / comments
/// 三個大欄位（避免 35KB → 1.5KB），metadata 部分原樣保留。
/// </summary>
public class NhentaiApiClient : IMetadataAPIClient, IDisposable
{
    public const string DefaultBaseUrl = "https://nhentai.net";
    public const string SourceNameValue = "nhentai";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private readonly TimeSpan _minInterval;

    public string SourceName => SourceNameValue;

    /// <summary>
    /// 建構子。
    /// </summary>
    /// <param name="apiKey">nhentai API key（可空 — 公開端點仍能用，但限額較低）</param>
    /// <param name="http">注入 HttpClient（測試用 mock）。null = 自建 + 自管 dispose</param>
    /// <param name="baseUrl">預設 https://nhentai.net；測試可換</param>
    /// <param name="minIntervalMs">throttle 最小間隔（毫秒）。預設 1500。</param>
    public NhentaiApiClient(string? apiKey = null, HttpClient? http = null,
                            string baseUrl = DefaultBaseUrl, int minIntervalMs = 1500)
    {
        if (http == null)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _http.BaseAddress ??= new Uri(baseUrl);
            _ownsHttp = false;
        }
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ComicOrganize/1.0");
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Key", apiKey.Trim());

        _minInterval = TimeSpan.FromMilliseconds(minIntervalMs);
    }

    public async Task<MetadataLookupResult?> GetByExternalIdAsync(string externalId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalId) || !long.TryParse(externalId, out _))
            return null;

        using var resp = await SendThrottledAsync($"/api/v2/galleries/{externalId}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseGalleryResponse(json);
    }

    public async Task<IReadOnlyList<MetadataSearchHit>> SearchAsync(string query, int page = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<MetadataSearchHit>();
        var url = $"/api/v2/search?query={Uri.EscapeDataString(query)}&page={page}";
        using var resp = await SendThrottledAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseSearchResponse(json);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await SendThrottledAsync("/api/v2/user", ct);
            if (!resp.IsSuccessStatusCode)
                return new ConnectionTestResult(false, null,
                    $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var username = doc.RootElement.TryGetProperty("username", out var u) ? u.GetString() : null;
            return new ConnectionTestResult(true, username, null);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, null, ex.Message);
        }
    }

    // ── 內部 ───────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendThrottledAsync(string urlPath, CancellationToken ct)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestUtc;
            if (elapsed < _minInterval)
                await Task.Delay(_minInterval - elapsed, ct);
            _lastRequestUtc = DateTime.UtcNow;
            return await _http.GetAsync(urlPath, ct);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>解析 GET /galleries/{id} 回應。raw_json 剝掉 pages/related/comments 後存。</summary>
    public static MetadataLookupResult? ParseGalleryResponse(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        if (node == null || node["id"] == null) return null;

        // 剝大欄位 → 給 raw_json archive
        node.Remove("pages");
        node.Remove("related");
        node.Remove("comments");
        var slimJson = node.ToJsonString();

        using var doc = JsonDocument.Parse(slimJson);
        var root = doc.RootElement;

        var result = new MetadataLookupResult
        {
            Source = SourceNameValue,
            ExternalId = root.GetProperty("id").GetInt64().ToString(),
            MediaId = root.TryGetProperty("media_id", out var mid) ? mid.GetString() : null,
            UploadAt = root.TryGetProperty("upload_date", out var ud) && ud.ValueKind == JsonValueKind.Number
                ? ud.GetInt64() : null,
            NumPages = root.TryGetProperty("num_pages", out var np) && np.ValueKind == JsonValueKind.Number
                ? np.GetInt32() : null,
            NumFavorites = root.TryGetProperty("num_favorites", out var nf) && nf.ValueKind == JsonValueKind.Number
                ? nf.GetInt32() : null,
            Scanlator = root.TryGetProperty("scanlator", out var sc) ? sc.GetString() : null,
            RawJson = slimJson,
        };

        if (root.TryGetProperty("title", out var titleElem) && titleElem.ValueKind == JsonValueKind.Object)
        {
            result.TitleEnglish = StringOrNull(titleElem, "english");
            result.TitleJapanese = StringOrNull(titleElem, "japanese");
            result.TitlePretty = StringOrNull(titleElem, "pretty");
        }

        if (root.TryGetProperty("tags", out var tagsElem) && tagsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tagsElem.EnumerateArray())
            {
                int? count = t.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number
                    ? c.GetInt32() : null;
                result.Tags.Add(new MetadataTag(
                    StringOrNull(t, "type") ?? "",
                    StringOrNull(t, "name") ?? "",
                    StringOrNull(t, "slug") ?? "",
                    count
                ));
            }
        }

        return result;
    }

    /// <summary>解析 GET /search 回應。</summary>
    public static IReadOnlyList<MetadataSearchHit> ParseSearchResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var resultArr) ||
            resultArr.ValueKind != JsonValueKind.Array)
            return Array.Empty<MetadataSearchHit>();

        var hits = new List<MetadataSearchHit>();
        foreach (var item in resultArr.EnumerateArray())
        {
            var hit = new MetadataSearchHit
            {
                ExternalId = item.GetProperty("id").GetInt64().ToString(),
                TitleEnglish = StringOrNull(item, "english_title"),
                TitleJapanese = StringOrNull(item, "japanese_title"),
                NumPages = item.TryGetProperty("num_pages", out var np) && np.ValueKind == JsonValueKind.Number
                    ? np.GetInt32() : null,
            };
            if (item.TryGetProperty("tag_ids", out var tagIds) && tagIds.ValueKind == JsonValueKind.Array)
            {
                foreach (var tid in tagIds.EnumerateArray())
                    if (tid.ValueKind == JsonValueKind.Number)
                        hit.TagIds.Add(tid.GetInt32());
            }
            hits.Add(hit);
        }
        return hits;
    }

    private static string? StringOrNull(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) return null;
        return v.GetString();
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _throttle.Dispose();
    }
}

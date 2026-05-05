using System.Net;
using System.Text;
using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// NhentaiApiClient 測試（#52 Phase B）。
/// 用 mock HttpMessageHandler 餵罐頭回應。
/// </summary>
public class NhentaiApiClientTests
{
    // ── Mock infrastructure ─────────────────────────────────────────────

    private class MockHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public List<HttpRequestMessage> Captured { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Captured.Add(request);
            return Task.FromResult(Handler(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (NhentaiApiClient client, MockHandler handler) MakeClient(string apiKey = "nhk_test")
    {
        var handler = new MockHandler();
        var http = new HttpClient(handler);
        return (new NhentaiApiClient(apiKey, http, minIntervalMs: 0), handler);
    }

    // ── NhentaiIdExtractor ──────────────────────────────────────────────

    [Theory]
    [InlineData("[Author] work (123456).zip", "123456")]
    [InlineData("title 567890.zip", "567890")]
    [InlineData("[Author] work [567890] [chinese].zip", "567890")]
    [InlineData("12345 short.zip", "12345")]
    [InlineData("1234567 long.zip", "1234567")]
    [InlineData("Vol.3 第5話.zip", null)]   // 太短
    [InlineData("12345678 too long.zip", null)] // 8 位太長
    [InlineData("[Cuvie] work no id.zip", null)]
    [InlineData("", null)]
    public void IdExtractor_FindsValidIds(string filename, string? expected)
    {
        Assert.Equal(expected, NhentaiIdExtractor.Extract(filename));
    }

    // ── ParseGalleryResponse — schema 解析 ──────────────────────────────

    [Fact]
    public void ParseGalleryResponse_FullFields()
    {
        // 用 568742 spike fixture 縮減版 — 含所有重要欄位
        var json = """
        {
          "id": 568742,
          "media_id": "3319522",
          "title": {
            "english": "[Flugel] Kugizuke Heart [Digital]",
            "japanese": "[Flugel] くぎづけはーと [DL版]",
            "pretty": "Kugizuke Heart"
          },
          "scanlator": "",
          "upload_date": 1744974299,
          "num_pages": 208,
          "num_favorites": 2797,
          "tags": [
            { "id": 108417, "type": "artist", "name": "flugel", "slug": "flugel", "count": 92, "url": "/artist/flugel/" },
            { "id": 33173, "type": "category", "name": "manga", "slug": "manga", "count": 132213, "url": "/category/manga/" },
            { "id": 6346, "type": "language", "name": "japanese", "slug": "japanese", "count": 326791, "url": "/language/japanese/" },
            { "id": 2937, "type": "tag", "name": "big breasts", "slug": "big-breasts", "count": 219192, "url": "/tag/big-breasts/" }
          ],
          "pages": [{"number":1,"path":"galleries/3319522/1.webp","width":1280,"height":1810}],
          "related": [{"id":111,"english_title":"x"}],
          "comments": []
        }
        """;
        var r = NhentaiApiClient.ParseGalleryResponse(json);

        Assert.NotNull(r);
        Assert.Equal("nhentai", r!.Source);
        Assert.Equal("568742", r.ExternalId);
        Assert.Equal("3319522", r.MediaId);
        Assert.Equal("Kugizuke Heart", r.TitlePretty);
        Assert.Equal("[Flugel] Kugizuke Heart [Digital]", r.TitleEnglish);
        Assert.Equal("[Flugel] くぎづけはーと [DL版]", r.TitleJapanese);
        Assert.Equal(1744974299, r.UploadAt);
        Assert.Equal(208, r.NumPages);
        Assert.Equal(2797, r.NumFavorites);
        Assert.Equal("", r.Scanlator);
        Assert.Equal(4, r.Tags.Count);
        Assert.Contains(r.Tags, t => t.Type == "artist" && t.Slug == "flugel");
        Assert.Contains(r.Tags, t => t.Type == "language" && t.Slug == "japanese");
    }

    [Fact]
    public void ParseGalleryResponse_StripsLargeFieldsFromRawJson()
    {
        var json = """
        {
          "id": 1, "media_id": "m", "title": {"english":"e","pretty":"p","japanese":null},
          "tags": [],
          "pages": [{"number":1,"path":"foo"}],
          "related": [{"id":99}],
          "comments": [{"text":"hi"}]
        }
        """;
        var r = NhentaiApiClient.ParseGalleryResponse(json);
        Assert.NotNull(r);
        Assert.DoesNotContain("\"pages\"", r!.RawJson);
        Assert.DoesNotContain("\"related\"", r.RawJson);
        Assert.DoesNotContain("\"comments\"", r.RawJson);
        Assert.Contains("\"id\":1", r.RawJson);
        Assert.Contains("\"title\"", r.RawJson);
    }

    [Fact]
    public void ParseGalleryResponse_NullJapaneseTitle()
    {
        var json = """{"id":1,"media_id":"m","title":{"english":"e","pretty":"p","japanese":null},"tags":[]}""";
        var r = NhentaiApiClient.ParseGalleryResponse(json);
        Assert.NotNull(r);
        Assert.Null(r!.TitleJapanese);
        Assert.Equal("e", r.TitleEnglish);
    }

    // ── ParseSearchResponse ────────────────────────────────────────────

    [Fact]
    public void ParseSearchResponse_MultipleHits()
    {
        var json = """
        {
          "result": [
            { "id": 568742, "media_id":"x", "english_title":"Kugizuke Heart [Digital]",
              "japanese_title":"くぎづけはーと [DL版]", "thumbnail":"t1", "thumbnail_width":250,
              "thumbnail_height":353, "num_pages":208, "tag_ids":[108417,33173,6346,2937],
              "blacklisted":false },
            { "id": 999, "media_id":"y", "english_title":"Other", "japanese_title":null,
              "thumbnail":"t2", "thumbnail_width":250, "thumbnail_height":350, "num_pages":30,
              "tag_ids":[100], "blacklisted":false }
          ],
          "num_pages": 1, "per_page": 25
        }
        """;
        var hits = NhentaiApiClient.ParseSearchResponse(json);
        Assert.Equal(2, hits.Count);
        Assert.Equal("568742", hits[0].ExternalId);
        Assert.Contains("Kugizuke", hits[0].TitleEnglish);
        Assert.Equal(208, hits[0].NumPages);
        Assert.Equal(4, hits[0].TagIds.Count);
        Assert.Null(hits[1].TitleJapanese);
    }

    [Fact]
    public void ParseSearchResponse_EmptyResult()
    {
        Assert.Empty(NhentaiApiClient.ParseSearchResponse("""{"result":[],"num_pages":0}"""));
    }

    // ── HTTP integration ───────────────────────────────────────────────

    [Fact]
    public async Task GetByExternalId_AddsAuthHeader()
    {
        var (client, handler) = MakeClient("nhk_secret");
        handler.Handler = req => Json("""{"id":1,"media_id":"m","title":{"english":"e","japanese":null,"pretty":"p"},"tags":[]}""");

        await client.GetByExternalIdAsync("1");

        Assert.Single(handler.Captured);
        var auth = handler.Captured[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Key", auth!.Scheme);
        Assert.Equal("nhk_secret", auth.Parameter);
    }

    [Fact]
    public async Task GetByExternalId_HitsCorrectPath()
    {
        var (client, handler) = MakeClient();
        handler.Handler = req => Json("""{"id":568742,"media_id":"m","title":{"english":"e","japanese":null,"pretty":"p"},"tags":[]}""");

        await client.GetByExternalIdAsync("568742");

        Assert.Equal("/api/v2/galleries/568742", handler.Captured[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetByExternalId_Returns_Null_On_404()
    {
        var (client, handler) = MakeClient();
        handler.Handler = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await client.GetByExternalIdAsync("999999");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByExternalId_Invalid_Id_Returns_Null_Without_Calling_Api()
    {
        var (client, handler) = MakeClient();
        handler.Handler = _ => throw new InvalidOperationException("should not be called");

        var result = await client.GetByExternalIdAsync("not-a-number");

        Assert.Null(result);
        Assert.Empty(handler.Captured);
    }

    [Fact]
    public async Task SearchAsync_EncodesQuery()
    {
        var (client, handler) = MakeClient();
        handler.Handler = _ => Json("""{"result":[],"num_pages":0}""");

        await client.SearchAsync("artist:flugel くぎづけはーと");

        var url = handler.Captured[0].RequestUri!.ToString();
        Assert.Contains("/api/v2/search", url);
        Assert.Contains("page=1", url);
        // url-encoded japanese chars
        Assert.Contains("artist%3Aflugel", url);
    }

    [Fact]
    public async Task TestConnection_Success_Parses_Username()
    {
        var (client, handler) = MakeClient();
        handler.Handler = _ => Json("""{"id":5404111,"username":"sf71316","slug":"sf71316"}""");

        var result = await client.TestConnectionAsync();

        Assert.True(result.Success);
        Assert.Equal("sf71316", result.UserName);
    }

    [Fact]
    public async Task TestConnection_HttpError_Returns_Failure()
    {
        var (client, handler) = MakeClient();
        handler.Handler = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { ReasonPhrase = "Unauthorized" };

        var result = await client.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Contains("401", result.ErrorMessage!);
    }

    [Fact]
    public async Task SourceName_Is_nhentai()
    {
        var (client, _) = MakeClient();
        Assert.Equal("nhentai", client.SourceName);
    }
}

using System.Net;
using System.Text;
using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// TagTranslationUpdater 測試（#53 Phase 5-B）。
/// 用 MockHandler 餵 GitHub releases JSON / sha 罐頭回應，驗證 parse + atomic download。
/// </summary>
public class TagTranslationUpdaterTests
{
    // ── Mock infrastructure ─────────────────────────────────────────────

    private class MockHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public List<HttpRequestMessage> Captured { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Captured.Add(req);
            return Task.FromResult(Handler(req));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage Bytes(byte[] body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new ByteArrayContent(body) };

    /// <summary>典型的 GitHub releases/latest 回應（取自 v7.24139.1 實際 release，剪掉不必要 asset）。</summary>
    private const string SampleReleaseJson = """
    {
      "tag_name": "v7.24139.1",
      "name": "EhTagTranslation Database v7.24139.1",
      "published_at": "2026-05-07T08:38:51Z",
      "assets": [
        {
          "name": "db.text.json.gz",
          "browser_download_url": "https://github.com/EhTagTranslation/Database/releases/download/v7.24139.1/db.text.json.gz",
          "size": 1234567
        },
        {
          "name": "sha",
          "browser_download_url": "https://github.com/EhTagTranslation/Database/releases/download/v7.24139.1/sha",
          "size": 40
        },
        {
          "name": "db.full.json.gz",
          "browser_download_url": "https://github.com/EhTagTranslation/Database/releases/download/v7.24139.1/db.full.json.gz",
          "size": 3300000
        }
      ]
    }
    """;

    private const string SampleSha = "abc1234567890def1234567890abcdef12345678";

    // ── CheckLatestAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CheckLatest_ParsesValidRelease_ReturnsInfo()
    {
        var handler = new MockHandler
        {
            Handler = req =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/releases/latest"))
                    return Json(SampleReleaseJson);
                if (req.RequestUri.AbsolutePath.EndsWith("/sha"))
                    return Text(SampleSha + "\n");
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        var info = await updater.CheckLatestAsync();

        Assert.NotNull(info);
        Assert.Equal("v7.24139.1", info!.Tag);
        Assert.Equal(SampleSha, info.Sha);   // 驗 trim 掉換行
        Assert.Equal(1234567, info.DbSizeBytes);
        Assert.Contains("db.text.json.gz", info.DbAssetUrl);
        Assert.Equal("2026-05-07T08:38:51Z", info.PublishedAt);
    }

    [Fact]
    public async Task CheckLatest_NetworkError_ReturnsNull()
    {
        var handler = new MockHandler
        {
            Handler = _ => throw new HttpRequestException("simulated network error")
        };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        Assert.Null(await updater.CheckLatestAsync());
    }

    [Fact]
    public async Task CheckLatest_404_ReturnsNull()
    {
        var handler = new MockHandler
        {
            Handler = _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        Assert.Null(await updater.CheckLatestAsync());
    }

    [Fact]
    public async Task CheckLatest_MalformedJson_ReturnsNull()
    {
        var handler = new MockHandler { Handler = _ => Json("{ broken: json") };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        Assert.Null(await updater.CheckLatestAsync());
    }

    [Fact]
    public async Task CheckLatest_MissingDbAsset_ReturnsNull()
    {
        // 只有 sha 沒 db.text.json.gz → 回 null
        var json = """{"tag_name":"v1","assets":[{"name":"sha","browser_download_url":"http://x/sha","size":40}]}""";
        var handler = new MockHandler
        {
            Handler = req => req.RequestUri!.AbsolutePath.EndsWith("/releases/latest")
                ? Json(json) : Text(SampleSha)
        };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        Assert.Null(await updater.CheckLatestAsync());
    }

    [Fact]
    public async Task CheckLatest_ShaFetchFails_ReturnsNull()
    {
        var handler = new MockHandler
        {
            Handler = req => req.RequestUri!.AbsolutePath.EndsWith("/releases/latest")
                ? Json(SampleReleaseJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound)
        };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        Assert.Null(await updater.CheckLatestAsync());
    }

    // ── DownloadAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task Download_Success_WritesAtomically()
    {
        var payload = Encoding.UTF8.GetBytes(string.Join("", Enumerable.Repeat("hello-world ", 100)));
        var handler = new MockHandler { Handler = _ => Bytes(payload) };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        var dest = Path.Combine(Path.GetTempPath(), $"tu_dl_{Guid.NewGuid():N}.gz");
        try
        {
            var ok = await updater.DownloadAsync("http://x/db.text.json.gz", dest);
            Assert.True(ok);
            Assert.True(File.Exists(dest));
            Assert.False(File.Exists(dest + ".tmp"));   // tmp 應已 rename 掉
            Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task Download_HttpError_ReturnsFalse_NoDestFile()
    {
        var handler = new MockHandler { Handler = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        var dest = Path.Combine(Path.GetTempPath(), $"tu_404_{Guid.NewGuid():N}.gz");
        var ok = await updater.DownloadAsync("http://x/404", dest);

        Assert.False(ok);
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".tmp"));
    }

    [Fact]
    public async Task Download_OverwritesExisting()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"tu_ow_{Guid.NewGuid():N}.gz");
        try
        {
            // 先寫舊檔
            await File.WriteAllBytesAsync(dest, new byte[] { 0xDE, 0xAD });

            var newPayload = new byte[] { 0xBE, 0xEF, 0xCA, 0xFE };
            var handler = new MockHandler { Handler = _ => Bytes(newPayload) };
            var updater = new TagTranslationUpdater(new HttpClient(handler));

            var ok = await updater.DownloadAsync("http://x/db.gz", dest);

            Assert.True(ok);
            Assert.Equal(newPayload, await File.ReadAllBytesAsync(dest));
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    [Fact]
    public async Task Download_ReportsProgress()
    {
        // 1024 bytes payload；Content-Length 由 ByteArrayContent 自動設
        var payload = new byte[1024];
        new Random(42).NextBytes(payload);

        var handler = new MockHandler { Handler = _ => Bytes(payload) };
        var updater = new TagTranslationUpdater(new HttpClient(handler));

        var samples = new List<double>();
        var progress = new Progress<double>(samples.Add);

        var dest = Path.Combine(Path.GetTempPath(), $"tu_pr_{Guid.NewGuid():N}.gz");
        try
        {
            var ok = await updater.DownloadAsync("http://x/db.gz", dest, progress);
            // 等 SynchronizationContext 把 progress callback 排完
            await Task.Delay(50);

            Assert.True(ok);
            Assert.NotEmpty(samples);
            // 最後一筆應接近 1.0
            Assert.True(samples[^1] > 0.99 && samples[^1] <= 1.0,
                $"last progress should be ~1.0 but was {samples[^1]}");
        }
        finally { if (File.Exists(dest)) File.Delete(dest); }
    }

    // ── IsUpdateAvailable ───────────────────────────────────────────────

    [Theory]
    [InlineData("abc123", "abc123", false)]                   // 相同
    [InlineData("ABC123", "abc123", false)]                   // case-insensitive
    [InlineData("  abc123  ", "abc123", false)]                // 兩端空白 trim
    [InlineData("abc123", "def456", true)]                    // 不同 → 有更新
    [InlineData("", "abc123", true)]                          // local 空 → 有更新（從未 sync）
    [InlineData("abc123", "", false)]                         // remote 空 → 沒拿到資訊，不報更新
    [InlineData("", "", false)]                               // 都空 → false
    public void IsUpdateAvailable_Matrix(string local, string remote, bool expected)
    {
        Assert.Equal(expected, TagTranslationUpdater.IsUpdateAvailable(local, remote));
    }
}

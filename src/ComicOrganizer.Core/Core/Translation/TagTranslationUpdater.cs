using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ComicOrganizer.App.Core;

/// <summary>
/// EhTag 翻譯庫線上更新服務（#53 Phase 5-B）。
///
/// 上游：GitHub repo EhTagTranslation/Database
///   - releases/latest API 回 tag_name + assets 列表
///   - assets 內含 db.text.json.gz（1.2 MB 壓縮）跟 sha（40 byte 純文字 commit sha）
///   - 比對策略：拉 sha asset 跟 local TagTranslationDb.HeadSha 比對，不同就 prompt 下載
///
/// 失敗模式（網路錯 / API 不通 / 解析錯）一律靜默回 null / false，
/// 由 caller 決定是否提示使用者（背景檢查吞、主動點擊顯示）。
/// </summary>
public class TagTranslationUpdater
{
    private static readonly Uri ReleasesApiUrl =
        new("https://api.github.com/repos/EhTagTranslation/Database/releases/latest");

    private readonly HttpClient _http;

    public TagTranslationUpdater(HttpClient? httpClient = null)
    {
        _http = httpClient ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub API 強制要 User-Agent，沒帶會 403
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ComicOrganizer", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        return client;
    }

    /// <summary>查 GitHub release 的最新版本資訊（含 sha）。
    /// 網路 / 解析 / 缺少 asset 一律回 null（caller fallback 提示「無法檢查更新」）。</summary>
    public async Task<LatestReleaseInfo?> CheckLatestAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(ReleasesApiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? tagName = root.TryGetProperty("tag_name", out var tn) ? tn.GetString() : null;
            string? publishedAt = root.TryGetProperty("published_at", out var pa) ? pa.GetString() : null;

            if (string.IsNullOrEmpty(tagName)) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            string? dbUrl = null;
            long dbSize = 0;
            string? shaUrl = null;

            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                var size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetInt64() : 0;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (name == "db.text.json.gz") { dbUrl = url; dbSize = size; }
                else if (name == "sha") { shaUrl = url; }
            }

            if (string.IsNullOrEmpty(dbUrl) || string.IsNullOrEmpty(shaUrl)) return null;

            // 拉 sha asset（40 byte 純文字 commit sha）
            using var shaResp = await _http.GetAsync(shaUrl, ct).ConfigureAwait(false);
            if (!shaResp.IsSuccessStatusCode) return null;
            var sha = (await shaResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
            if (string.IsNullOrEmpty(sha)) return null;

            return new LatestReleaseInfo(tagName, sha, dbUrl, dbSize, publishedAt);
        }
        catch
        {
            // 網路 / DNS / 解析 — 一律靜默
            return null;
        }
    }

    /// <summary>下載 db asset 到 destPath。失敗回 false（caller 自行重試或提示）。
    /// 寫入策略 atomic：先寫 destPath + ".tmp"，完成後 rename，避免下載中斷產生半檔污染 cache。</summary>
    public async Task<bool> DownloadAsync(string url, string destPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tmpPath = destPath + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;

            var total = resp.Content.Headers.ContentLength ?? 0;
            using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using (var dst = File.Create(tmpPath))
            {
                var buf = new byte[8192];
                long copied = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    copied += read;
                    if (total > 0) progress?.Report((double)copied / total);
                }
            }

            // atomic rename — 先刪舊（File.Move 不支援 overwrite 在某些 Win API path）
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmpPath, destPath);
            return true;
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            return false;
        }
    }

    /// <summary>local sha 跟 remote sha 不同 → 有更新。
    /// remote 空字串視為「沒拿到資訊」回 false（避免 false positive）；local 空 + remote 有值 → 有更新（從未 sync 過）。</summary>
    public static bool IsUpdateAvailable(string localSha, string remoteSha)
    {
        if (string.IsNullOrEmpty(remoteSha)) return false;
        return !string.Equals(localSha?.Trim(), remoteSha.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>GitHub release 解析後的最新版資訊。</summary>
/// <param name="Tag">release tag name（例如 v7.24139.1）。</param>
/// <param name="Sha">commit sha（40 字元）— 跟 local TagTranslationDb.HeadSha 比對。</param>
/// <param name="DbAssetUrl">db.text.json.gz 的 browser_download_url。</param>
/// <param name="DbSizeBytes">壓縮檔大小（給 UI 顯示「下載 1.2 MB」）。</param>
/// <param name="PublishedAt">ISO 8601 時間字串（給 UI 顯示「發布於 2026-05-07」）。</param>
public record LatestReleaseInfo(string Tag, string Sha, string DbAssetUrl, long DbSizeBytes, string? PublishedAt);

using System.Net.Http;

namespace ComicOrganizer.App.Core;

/// <summary>下載結果分類（供 UI 決定提示 / 重試）。</summary>
public enum DownloadOutcome
{
    Success,
    Cancelled,
    NetworkError,
    SizeMismatch,
}

/// <summary>下載結果：分類 + 給 UI 顯示的訊息。</summary>
public sealed record DownloadResult(DownloadOutcome Outcome, string? Message)
{
    public bool Ok => Outcome == DownloadOutcome.Success;
}

/// <summary>
/// 按需下載大型離線模型（目前為 comic-text-detector.onnx，約 95 MB）。
/// 模型訓練資料僅限個人研究／使用，故不隨程式發行，改由使用者於設定頁或啟用功能時自 HuggingFace 下載。
/// 下載策略：ResponseHeadersRead 串流 → 寫 .part 暫存 → 大小驗證 → 原子取代目標檔。
/// </summary>
public static class ModelDownloader
{
    /// <summary>comic-text-detector ONNX 模型檔案名稱。</summary>
    public const string ComicTextDetectorFileName = "comic-text-detector.onnx";

    /// <summary>comic-text-detector ONNX（mayocream 重打包版本）。</summary>
    public const string ComicTextDetectorUrl =
        "https://huggingface.co/mayocream/comic-text-detector-onnx/resolve/main/comic-text-detector.onnx";

    /// <summary>已知檔案大小（bytes），用於下載後驗證及缺少 Content-Length 時的進度分母。</summary>
    public const long ComicTextDetectorSizeBytes = 94669756L;

    /// <summary>低於此門檻視為 HTML 錯誤頁或下載截斷。</summary>
    private const long MinPlausibleSizeBytes = 90_000_000L;

    /// <summary>主程式目錄下 inference/comic-text-detector.onnx 預設路徑。</summary>
    public static string ComicTextDetectorPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inference", ComicTextDetectorFileName);

    /// <summary>
    /// 依序檢查主程式 BaseDirectory 與選擇性的 DataDirectory 下的 inference/，回傳已存在的模型路徑；
    /// 若皆不存在則回傳建議寫入之偏好路徑。
    /// </summary>
    public static string ResolveComicTextDetectorPath(string? dataDirectory = null)
    {
        string basePath = ComicTextDetectorPath;
        if (File.Exists(basePath))
        {
            return basePath;
        }

        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            string dataPath = Path.Combine(dataDirectory, "inference", ComicTextDetectorFileName);
            if (File.Exists(dataPath))
            {
                return dataPath;
            }
        }

        return basePath;
    }

    /// <summary>取得下載儲存目標路徑（若傳入有效 DataDirectory 且主目錄不可寫或偏好奇目錄統一存放時可指定）。</summary>
    public static string GetPreferredComicTextDetectorTargetPath(string? dataDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            return Path.Combine(dataDirectory, "inference", ComicTextDetectorFileName);
        }

        return ComicTextDetectorPath;
    }

    /// <summary>檢查 comic-text-detector.onnx 是否已就緒且達到合理容量。</summary>
    public static bool IsComicTextDetectorAvailable(string? dataDirectory = null)
    {
        string path = ResolveComicTextDetectorPath(dataDirectory);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return new FileInfo(path).Length >= MinPlausibleSizeBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 下載模型至 <paramref name="destPath"/>。採用 .part 暫存串流寫入、大小驗證與原子搬移。
    /// </summary>
    public static async Task<DownloadResult> DownloadAsync(
        string url,
        string destPath,
        long expectedSize,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
    {
        var tmpPath = destPath + ".part";
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return new DownloadResult(DownloadOutcome.NetworkError, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            long headerTotal = resp.Content.Headers.ContentLength ?? 0;
            long total = headerTotal > 0 ? headerTotal : expectedSize;

            using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var dst = File.Create(tmpPath))
            {
                var buf = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    copied += read;
                    progress?.Report((copied, total));
                }

                await dst.FlushAsync(ct).ConfigureAwait(false);

                if (headerTotal > 0 && copied != headerTotal)
                {
                    SafeDelete(tmpPath);
                    return new DownloadResult(
                        DownloadOutcome.SizeMismatch,
                        $"下載大小不符（{copied:N0} / {headerTotal:N0} bytes），可能中斷，請重試。");
                }

                if (copied < MinPlausibleSizeBytes)
                {
                    SafeDelete(tmpPath);
                    return new DownloadResult(
                        DownloadOutcome.SizeMismatch,
                        $"下載內容過小（{copied:N0} bytes），可能是錯誤頁，請重試。");
                }
            }

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(tmpPath, destPath);
            return new DownloadResult(DownloadOutcome.Success, null);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(tmpPath);
            return new DownloadResult(DownloadOutcome.Cancelled, "已取消下載。");
        }
        catch (Exception ex)
        {
            SafeDelete(tmpPath);
            return new DownloadResult(DownloadOutcome.NetworkError, ex.Message);
        }
    }

    /// <summary>下載 comic-text-detector.onnx 至預設 BaseDirectory\inference 路徑。</summary>
    public static Task<DownloadResult> DownloadComicTextDetectorAsync(
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
        => DownloadAsync(ComicTextDetectorUrl, ComicTextDetectorPath, ComicTextDetectorSizeBytes, progress, ct);

    /// <summary>下載 comic-text-detector.onnx 至指定路徑。</summary>
    public static Task<DownloadResult> DownloadComicTextDetectorToAsync(
        string destPath,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
        => DownloadAsync(ComicTextDetectorUrl, destPath, ComicTextDetectorSizeBytes, progress, ct);

    private static void SafeDelete(string p)
    {
        try
        {
            if (File.Exists(p))
            {
                File.Delete(p);
            }
        }
        catch
        {
        }
    }
}

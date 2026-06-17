using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ComicOrganizer.App.Core;

/// <summary>下載結果分類（供 UI 決定提示/重試）。</summary>
public enum DownloadOutcome
{
    Success,
    Cancelled,
    NetworkError,
    SizeMismatch,   // Content-Length 不符或檔案過小（HTML 錯誤頁/截斷）
}

/// <summary>下載結果：分類 + 給 UI 顯示的訊息。</summary>
public sealed record DownloadResult(DownloadOutcome Outcome, string? Message)
{
    public bool Ok => Outcome == DownloadOutcome.Success;
}

/// <summary>
/// 按需下載大型離線模型（目前只有 comic-text-detector.onnx，~95MB）。
/// 模型訓練資料 Manga109-s 僅限個人用、不可散布，故不打包進發行檔，改由使用者啟用時自 HuggingFace 下載。
/// 下載策略：ResponseHeadersRead 串流 → 寫 .part 暫存 → 大小驗證 → 原子 rename，避免半殘檔污染 inference/。
/// </summary>
public static class ModelDownloader
{
    /// <summary>comic-text-detector ONNX（mayocream 重打包，見 reference）。</summary>
    public const string ComicTextDetectorUrl =
        "https://huggingface.co/mayocream/comic-text-detector-onnx/resolve/main/comic-text-detector.onnx";

    /// <summary>已知檔案大小（bytes），下載完做驗證；亦當 Content-Length 缺失時的進度分母。</summary>
    public const long ComicTextDetectorSizeBytes = 94669756L;

    /// <summary>低於此視為 HTML 錯誤頁/截斷（HF resolve 正常會回完整 ~95MB）。</summary>
    private const long MinPlausibleSizeBytes = 90_000_000L;

    /// <summary>exe 同層 inference/comic-text-detector.onnx（與 <see cref="ComicTextDetector"/> 載入路徑一致）。</summary>
    public static string ComicTextDetectorPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inference", "comic-text-detector.onnx");

    /// <summary>
    /// 下載模型到 <paramref name="destPath"/>。串流寫 .part → 驗證大小 → 原子 rename。
    /// 失敗不留半檔；任何結果都不丟例外，由 <see cref="DownloadResult"/> 表達，UI 自行重試。
    /// </summary>
    /// <param name="progress">回報 (已下載 bytes, 總 bytes)；總數未知時 total=expectedSize。</param>
    public static async Task<DownloadResult> DownloadAsync(
        string url,
        string destPath,
        long expectedSize,
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken ct = default)
    {
        var tmpPath = destPath + ".part";
        // 大檔下載需長時間 → 不靠 HttpClient.Timeout（會中斷串流），逾時/取消一律走 CancellationToken
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new DownloadResult(DownloadOutcome.NetworkError, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

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

                // 大小驗證：① Content-Length 已知就必須完全相符（擋截斷）② 至少要 ~95MB（擋 HTML 錯誤頁）
                if (headerTotal > 0 && copied != headerTotal)
                {
                    SafeDelete(tmpPath);
                    return new DownloadResult(DownloadOutcome.SizeMismatch,
                        $"下載大小不符（{copied:N0} / {headerTotal:N0} bytes），可能中斷，請重試。");
                }
                if (copied < MinPlausibleSizeBytes)
                {
                    SafeDelete(tmpPath);
                    return new DownloadResult(DownloadOutcome.SizeMismatch,
                        $"下載內容過小（{copied:N0} bytes），可能是錯誤頁，請重試。");
                }
            }

            // 原子 rename（File.Move 部分 Win API path 不支援 overwrite → 先刪舊）
            if (File.Exists(destPath)) File.Delete(destPath);
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

    /// <summary>便捷：下載 comic-text-detector.onnx 到預設 inference/ 路徑。</summary>
    public static Task<DownloadResult> DownloadComicTextDetectorAsync(
        IProgress<(long downloaded, long total)>? progress = null, CancellationToken ct = default)
        => DownloadAsync(ComicTextDetectorUrl, ComicTextDetectorPath, ComicTextDetectorSizeBytes, progress, ct);

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略清理失敗 */ }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PaddleOCRSharp;
using SharpCompress.Archives;

namespace ComicOrganizer.App.Core;

public class PaddleOcrService : IOcrService
{
    private PaddleOCREngine? _engine;
    private readonly object _lock = new object();
    private readonly HttpClient _httpClient = new HttpClient();
    private readonly string _inferenceDir;

    /// <summary>inferenceDir 預設為 exe 同層的 inference/；測試可指向 App bin 的模型目錄。</summary>
    public PaddleOcrService(string? inferenceDir = null)
    {
        _inferenceDir = inferenceDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inference");
    }

    private async Task EnsureEngineAsync()
    {
        if (_engine != null) return;

        string inferenceDir = _inferenceDir;

        string detPath = Path.Combine(inferenceDir, "PP-OCRv6_small_det_infer");
        string recPath = Path.Combine(inferenceDir, "PP-OCRv6_small_rec_infer");
        string clsPath = Path.Combine(inferenceDir, "PP-OCRv5_mobile_cls_infer");
        string dictPath = Path.Combine(inferenceDir, "ppocr_keys.txt");

        // 初始化 PaddleOCR 引擎
        var modelConfig = new OCRModelConfig
        {
            det_infer = detPath,
            rec_infer = recPath,
            cls_infer = clsPath,
            keys = dictPath
        };

        var param = new OCRParameter
        {
            cpu_math_library_num_threads = 4,
            enable_mkldnn = true,
            use_angle_cls = false, // 關閉方向分類器
            cls = false
        };

        _engine = new PaddleOCREngine(modelConfig, param);
    }

    private async Task DownloadFileAsync(string url, string destPath)
    {
        var bytes = await _httpClient.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(destPath, bytes);
    }

    private async Task DownloadAndExtractTarAsync(string url, string outputDir)
    {
        string tempFile = Path.Combine(outputDir, Path.GetFileName(url));
        try
        {
            await DownloadFileAsync(url, tempFile);
            
            // 使用 .NET 8 原生 TarFile 解壓
            System.Formats.Tar.TarFile.ExtractToDirectory(tempFile, outputDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public bool IsLanguageSupported(string langCode)
    {
        // PaddleOCR 多語言模型支援日文辨識
        return langCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    }

    public List<string> GetSupportedLanguages()
    {
        return new List<string> { "ja-JP", "ja" };
    }

    public async Task<List<OcrBlock>> RecognizeAsync(Image image, string langCode = "ja-JP")
    {
        await EnsureEngineAsync();

        if (_engine == null)
            throw new InvalidOperationException("Failed to initialize PaddleOCR Engine.");

        byte[] imgBytes;
        using (var ms = new MemoryStream())
        {
            image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            imgBytes = ms.ToArray();
        }

        OCRResult result;
        lock (_lock)
        {
            result = _engine.DetectText(imgBytes);
        }

        var blocks = new List<OcrBlock>();
        if (result?.TextBlocks != null)
        {
            foreach (var cell in result.TextBlocks)
            {
                if (cell.BoxPoints == null || cell.BoxPoints.Count < 4) continue;

                float minX = cell.BoxPoints.Min(p => p.X);
                float minY = cell.BoxPoints.Min(p => p.Y);
                float maxX = cell.BoxPoints.Max(p => p.X);
                float maxY = cell.BoxPoints.Max(p => p.Y);

                blocks.Add(new OcrBlock
                {
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    Text = cell.Text?.Trim() ?? "",
                    OriginalText = cell.Text?.Trim() ?? ""
                });
            }
        }

        return blocks;
    }

    // thresholdMultiplier 預設 1.0：只併「間隔在一個欄寬以內」的相鄰文字欄。
    // 同一氣泡內的鄰欄間隔通常 < 0.5 欄寬仍會合併；相鄰氣泡（間隔 > 1 欄寬）則不再被誤併成過大的合併框，避免譯文溢出。
    public List<OcrBlock> MergeBlocks(List<OcrBlock> blocks, bool isVerticalFlow = true, float thresholdMultiplier = 1.0f)
    {
        if (blocks == null || blocks.Count <= 1)
            return blocks ?? new List<OcrBlock>();

        int n = blocks.Count;
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (ShouldMerge(blocks[i], blocks[j], isVerticalFlow, thresholdMultiplier))
                {
                    adj[i].Add(j);
                    adj[j].Add(i);
                }
            }
        }

        var visited = new bool[n];
        var mergedBlocks = new List<OcrBlock>();

        for (int i = 0; i < n; i++)
        {
            if (visited[i]) continue;

            var component = new List<int>();
            DFS(i, visited, adj, component);

            mergedBlocks.Add(MergeComponent(component, blocks, isVerticalFlow));
        }

        return mergedBlocks;
    }

    private void DFS(int u, bool[] visited, List<int>[] adj, List<int> component)
    {
        visited[u] = true;
        component.Add(u);
        foreach (int v in adj[u])
        {
            if (!visited[v])
            {
                DFS(v, visited, adj, component);
            }
        }
    }

    private bool ShouldMerge(OcrBlock a, OcrBlock b, bool isVerticalFlow, float thresholdMultiplier)
    {
        if (isVerticalFlow)
        {
            float avgW = (a.Width + b.Width) / 2f;
            float maxLeft = Math.Max(a.X, b.X);
            float minRight = Math.Min(a.X + a.Width, b.X + b.Width);
            float distX = Math.Max(0, maxLeft - minRight);

            if (distX > avgW * thresholdMultiplier)
                return false;

            float maxTop = Math.Max(a.Y, b.Y);
            float minBottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
            float overlapY = Math.Max(0, minBottom - maxTop);
            float minHeight = Math.Min(a.Height, b.Height);

            return overlapY > minHeight * 0.3f;
        }
        else
        {
            float avgH = (a.Height + b.Height) / 2f;
            float maxTop = Math.Max(a.Y, b.Y);
            float minBottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
            float distY = Math.Max(0, maxTop - minBottom);

            if (distY > avgH * thresholdMultiplier)
                return false;

            float maxLeft = Math.Max(a.X, b.X);
            float minRight = Math.Min(a.X + a.Width, b.X + b.Width);
            float overlapX = Math.Max(0, minRight - maxLeft);
            float minWidth = Math.Min(a.Width, b.Width);

            return overlapX > minWidth * 0.3f;
        }
    }

    private OcrBlock MergeComponent(List<int> component, List<OcrBlock> originalBlocks, bool isVerticalFlow)
    {
        var blocks = component.Select(idx => originalBlocks[idx]).ToList();

        float minX = blocks.Min(b => b.X);
        float minY = blocks.Min(b => b.Y);
        float maxX = blocks.Max(b => b.X + b.Width);
        float maxY = blocks.Max(b => b.Y + b.Height);

        if (isVerticalFlow)
        {
            blocks = blocks.OrderByDescending(b => b.X).ToList();
        }
        else
        {
            blocks = blocks.OrderBy(b => b.Y).ToList();
        }

        var texts = blocks.Select(b => b.Text.Trim()).ToList();
        string mergedText = string.Join(" ", texts);

        mergedText = CleanCjkSpaces(mergedText);

        return new OcrBlock
        {
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Text = mergedText,
            OriginalText = mergedText,
            Children = blocks
        };
    }

    private string CleanCjkSpaces(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        string cjkPattern = @"[\u4e00-\u9fa5\u3040-\u309f\u30a0-\u30ff\u3000-\u303f\uff00-\uffef\u30fb\u2026]";
        string pattern = $"({cjkPattern})\\s+({cjkPattern})";

        string prev;
        do
        {
            prev = text;
            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, "$1$2");
        } while (text != prev);

        return text.Trim();
    }
}

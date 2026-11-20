using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ComicOrganizer.App.Core;
using PaddleOCRSharp;

namespace ComicOrganizer.Plugin.PaddleOcr;

/// <summary>
/// 跨平台（無 System.Drawing 依賴）之 PaddleOCR 核心實作。
/// 供 Avalonia 端直接使用，並由 WinForms 端的 <see cref="PaddleOcrService"/> 委派共用，避免重複實作。
/// </summary>
public sealed class PaddleSkiaOcrService : ISkiaOcrService, IDisposable
{
    private const float SplitGapRatio = 0.3f;

    private PaddleOCREngine? _engine;
    private readonly object _lock = new();
    private readonly string _inferenceDir;

    public PaddleSkiaOcrService(string? inferenceDir = null)
    {
        _inferenceDir = inferenceDir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "inference");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }

    private void EnsureEngine()
    {
        if (_engine != null) return;
        lock (_lock)
        {
            if (_engine != null) return;

            var modelConfig = new OCRModelConfig
            {
                det_infer = Path.Combine(_inferenceDir, "PP-OCRv6_small_det_infer"),
                rec_infer = Path.Combine(_inferenceDir, "PP-OCRv6_small_rec_infer"),
                cls_infer = Path.Combine(_inferenceDir, "PP-OCRv5_mobile_cls_infer"),
                keys = Path.Combine(_inferenceDir, "ppocr_keys.txt")
            };

            var param = new OCRParameter
            {
                cpu_math_library_num_threads = 4,
                enable_mkldnn = true,
                use_angle_cls = false,
                cls = false
            };

            _engine = new PaddleOCREngine(modelConfig, param);
        }
    }

    public bool IsLanguageSupported(string langCode)
        => langCode.StartsWith("ja", StringComparison.OrdinalIgnoreCase);

    public List<string> GetSupportedLanguages() => new() { "ja-JP", "ja" };

    public Task<List<OcrBlock>> RecognizeEncodedAsync(byte[] encodedImageBytes, string langCode = "ja-JP")
    {
        if (encodedImageBytes == null || encodedImageBytes.Length == 0)
            return Task.FromResult(new List<OcrBlock>());

        return Task.Run(() =>
        {
            EnsureEngine();
            if (_engine == null)
                throw new InvalidOperationException("Failed to initialize PaddleOCR Engine.");

            OCRResult result;
            lock (_lock)
            {
                result = _engine.DetectText(encodedImageBytes);
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

                    string text = cell.Text?.Trim() ?? "";
                    blocks.Add(new OcrBlock
                    {
                        X = minX,
                        Y = minY,
                        Width = maxX - minX,
                        Height = maxY - minY,
                        Text = text,
                        OriginalText = text
                    });
                }
            }

            return blocks;
        });
    }

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
            Dfs(i, visited, adj, component);
            mergedBlocks.Add(MergeComponent(component, blocks, isVerticalFlow));
        }

        return SplitMergedBlocksByGap(mergedBlocks, isVerticalFlow);
    }

    public static List<List<OcrBlock>> ClusterChildrenByGap(List<OcrBlock>? children, bool isVerticalFlow, float gapRatio)
    {
        var clusters = new List<List<OcrBlock>>();
        if (children == null || children.Count == 0) return clusters;
        if (children.Count < 2) { clusters.Add(new List<OcrBlock>(children)); return clusters; }

        var ordered = isVerticalFlow
            ? children.OrderBy(c => c.X).ToList()
            : children.OrderBy(c => c.Y).ToList();
        float avgSize = isVerticalFlow
            ? (float)ordered.Average(c => c.Width)
            : (float)ordered.Average(c => c.Height);
        float thr = avgSize * gapRatio;

        var cur = new List<OcrBlock> { ordered[0] };
        for (int i = 1; i < ordered.Count; i++)
        {
            float gap = isVerticalFlow
                ? ordered[i].X - (ordered[i - 1].X + ordered[i - 1].Width)
                : ordered[i].Y - (ordered[i - 1].Y + ordered[i - 1].Height);
            if (gap > thr) { clusters.Add(cur); cur = new List<OcrBlock>(); }
            cur.Add(ordered[i]);
        }
        clusters.Add(cur);
        return clusters;
    }

    private List<OcrBlock> SplitMergedBlocksByGap(List<OcrBlock> merged, bool isVerticalFlow)
    {
        var result = new List<OcrBlock>();
        foreach (var b in merged)
        {
            var clusters = ClusterChildrenByGap(b.Children, isVerticalFlow, SplitGapRatio);
            if (clusters.Count <= 1) { result.Add(b); continue; }
            foreach (var cl in clusters) result.Add(BuildBlockFromChildren(cl, isVerticalFlow));
        }
        return result;
    }

    private OcrBlock BuildBlockFromChildren(List<OcrBlock> children, bool isVerticalFlow)
    {
        float minX = children.Min(c => c.X);
        float minY = children.Min(c => c.Y);
        float maxX = children.Max(c => c.X + c.Width);
        float maxY = children.Max(c => c.Y + c.Height);

        var ordered = isVerticalFlow
            ? children.OrderByDescending(c => c.X).ToList()
            : children.OrderBy(c => c.Y).ToList();
        string text = CleanCjkSpaces(string.Join(" ", ordered.Select(c => c.Text.Trim())));

        return new OcrBlock
        {
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Text = text,
            OriginalText = text,
            Children = ordered
        };
    }

    private static void Dfs(int u, bool[] visited, List<int>[] adj, List<int> component)
    {
        visited[u] = true;
        component.Add(u);
        foreach (int v in adj[u])
        {
            if (!visited[v]) Dfs(v, visited, adj, component);
        }
    }

    private static bool ShouldMerge(OcrBlock a, OcrBlock b, bool isVerticalFlow, float thresholdMultiplier)
    {
        if (isVerticalFlow)
        {
            float avgW = (a.Width + b.Width) / 2f;
            float maxLeft = Math.Max(a.X, b.X);
            float minRight = Math.Min(a.X + a.Width, b.X + b.Width);
            float distX = Math.Max(0, maxLeft - minRight);
            if (distX > avgW * thresholdMultiplier) return false;

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
            if (distY > avgH * thresholdMultiplier) return false;

            float maxLeft = Math.Max(a.X, b.X);
            float minRight = Math.Min(a.X + a.Width, b.X + b.Width);
            float overlapX = Math.Max(0, minRight - maxLeft);
            float minWidth = Math.Min(a.Width, b.Width);
            return overlapX > minWidth * 0.3f;
        }
    }

    private static OcrBlock MergeComponent(List<int> component, List<OcrBlock> originalBlocks, bool isVerticalFlow)
    {
        var blocks = component.Select(idx => originalBlocks[idx]).ToList();
        float minX = blocks.Min(b => b.X);
        float minY = blocks.Min(b => b.Y);
        float maxX = blocks.Max(b => b.X + b.Width);
        float maxY = blocks.Max(b => b.Y + b.Height);

        blocks = isVerticalFlow
            ? blocks.OrderByDescending(b => b.X).ToList()
            : blocks.OrderBy(b => b.Y).ToList();

        string mergedText = CleanCjkSpaces(string.Join(" ", blocks.Select(b => b.Text.Trim())));
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

    private static string CleanCjkSpaces(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = Regex.Replace(text, @"\s+", " ");
        const string cjkPattern = @"[\u4e00-\u9fa5\u3040-\u309f\u30a0-\u30ff\u3000-\u303f\uff00-\uffef\u30fb\u2026]";
        const string pattern = $"({cjkPattern})\\s+({cjkPattern})";
        string prev;
        do
        {
            prev = text;
            text = Regex.Replace(text, pattern, "$1$2");
        } while (text != prev);
        return text.Trim();
    }
}

using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using ComicOrganizer.App.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 無 System.Drawing 依賴的 Windows 10/11 原生 OCR 實作（直接使用 WinRT OcrEngine 與 IBuffer），
/// 實作 <see cref="ISkiaOcrService"/> 供 Avalonia 閱讀器與 <see cref="SkiaOcrPluginManager"/> 使用。
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsSkiaOcrService : ISkiaOcrService
{
    private OcrEngine? _engine;
    private string _currentLang = "";

    public bool IsLanguageSupported(string langCode)
    {
        try
        {
            var lang = new Language(langCode);
            return OcrEngine.IsLanguageSupported(lang);
        }
        catch
        {
            return false;
        }
    }

    public List<string> GetSupportedLanguages()
    {
        return OcrEngine.AvailableRecognizerLanguages
            .Select(l => l.LanguageTag)
            .ToList();
    }

    private void EnsureEngine(string langCode)
    {
        if (_engine != null && string.Equals(_currentLang, langCode, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var lang = new Language(langCode);
            _engine = OcrEngine.TryCreateFromLanguage(lang);
        }
        catch
        {
            _engine = null;
        }

        _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        _currentLang = langCode;
    }

    public async Task<List<OcrBlock>> RecognizeEncodedAsync(byte[] encodedImageBytes, string langCode = "ja-JP")
    {
        if (encodedImageBytes == null || encodedImageBytes.Length == 0)
            return new List<OcrBlock>();

        EnsureEngine(langCode);
        if (_engine == null)
            throw new InvalidOperationException("無法初始化 Windows 原生 OCR 引擎（請確認已安裝對應語言套件或改用 PaddleOCR）。");

        using var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras))
        {
            writer.WriteBytes(encodedImageBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        ras.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
        var ocrResult = await _engine.RecognizeAsync(softwareBitmap);

        var blocks = new List<OcrBlock>();
        if (ocrResult?.Lines == null) return blocks;

        foreach (var line in ocrResult.Lines)
        {
            if (line.Words == null || line.Words.Count == 0) continue;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;
                if (rect.X < minX) minX = (float)rect.X;
                if (rect.Y < minY) minY = (float)rect.Y;
                if (rect.X + rect.Width > maxX) maxX = (float)(rect.X + rect.Width);
                if (rect.Y + rect.Height > maxY) maxY = (float)(rect.Y + rect.Height);
            }

            string text = line.Text?.Trim() ?? "";
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

        return blocks;
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
        return mergedBlocks;
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

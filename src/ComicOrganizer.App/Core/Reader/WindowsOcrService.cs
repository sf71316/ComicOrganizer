using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ComicOrganizer.App.Core;

public class OcrBlock
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string Text { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public List<OcrBlock> Children { get; set; } = new();
}

public class WindowsOcrService : IOcrService
{
    private OcrEngine? _engine;
    private string _currentLang = "";

    public bool IsLanguageSupported(string langCode)
    {
        try
        {
            var lang = new Windows.Globalization.Language(langCode);
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
        if (_engine != null && _currentLang == langCode)
            return;

        var lang = new Windows.Globalization.Language(langCode);
        _engine = OcrEngine.TryCreateFromLanguage(lang);
        if (_engine == null)
        {
            // Fallback to user profile default
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }

        _currentLang = langCode;
    }

    public async Task<List<OcrBlock>> RecognizeAsync(Image image, string langCode = "ja-JP")
    {
        Bitmap bmp;
        bool createdNew = false;
        if (image is Bitmap b)
        {
            bmp = b;
        }
        else
        {
            bmp = new Bitmap(image);
            createdNew = true;
        }

        try
        {
            EnsureEngine(langCode);
            if (_engine == null)
                throw new InvalidOperationException("Failed to initialize Windows OCR Engine.");

            using var stream = new MemoryStream();
            bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            
            var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream))
            {
                writer.WriteBytes(stream.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            randomAccessStream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
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

                blocks.Add(new OcrBlock
                {
                    X = minX,
                    Y = minY,
                    Width = maxX - minX,
                    Height = maxY - minY,
                    Text = line.Text,
                    OriginalText = line.Text
                });
            }

            return blocks;
        }
        finally
        {
            if (createdNew)
            {
                bmp.Dispose();
            }
        }
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
            // 直書 (垂直流)：行是直立的，行與行水平相鄰
            float avgW = (a.Width + b.Width) / 2f;
            float maxLeft = Math.Max(a.X, b.X);
            float minRight = Math.Min(a.X + a.Width, b.X + b.Width);
            float distX = Math.Max(0, maxLeft - minRight);

            // 水平距離小於行寬的 thresholdMultiplier 倍
            if (distX > avgW * thresholdMultiplier)
                return false;

            // 垂直重疊：交集長度大於較矮者高度的 30%
            float maxTop = Math.Max(a.Y, b.Y);
            float minBottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
            float overlapY = Math.Max(0, minBottom - maxTop);
            float minHeight = Math.Min(a.Height, b.Height);

            return overlapY > minHeight * 0.3f;
        }
        else
        {
            // 橫書 (水平流)：行是水平的，行與行垂直相鄰
            float avgH = (a.Height + b.Height) / 2f;
            float maxTop = Math.Max(a.Y, b.Y);
            float minBottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
            float distY = Math.Max(0, maxTop - minBottom);

            // 垂直距離小於行高的 thresholdMultiplier 倍
            if (distY > avgH * thresholdMultiplier)
                return false;

            // 水平重疊：交集長度大於較窄者寬度的 30%
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

        // 排序文字
        // 直書：從右至左閱讀，所以按 X 座標降序排列
        // 橫書：從上至下閱讀，所以按 Y 座標升序排列
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

        // 進行 CJK 空白清理
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

    public static string CleanCjkSpaces(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // 先縮減連續空白
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        // CJK 字元範圍 (包括漢字、平假名、片假名、全形標點與全形英數)
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

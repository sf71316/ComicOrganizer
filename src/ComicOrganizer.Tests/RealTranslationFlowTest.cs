using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Plugin.PaddleOcr;

namespace ComicOrganizer.Tests;

public class RealTranslationFlowTest
{
    private readonly ITestOutputHelper _output;

    public RealTranslationFlowTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private AppConfig LoadRealAppConfig()
    {
        // 尋找真實 appsettings.json
        string[] potentialPaths = new[]
        {
            @"E:\Project\ComicOrganizer\src\ComicOrganizer.App\bin\Debug\net8.0-windows10.0.19041.0\appsettings.json",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ComicOrganizer.App", "bin", "Debug", "net8.0-windows10.0.19041.0", "appsettings.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json")
        };

        foreach (var path in potentialPaths)
        {
            if (File.Exists(path))
            {
                _output.WriteLine($"[Config] Loaded config from: {path}");
                var manager = new ConfigManager(path);
                return manager.Load();
            }
        }
        _output.WriteLine("[Config] Real appsettings.json not found, using default fallback.");
        return new ConfigManager().Load();
    }

    [Fact]
    public async Task TestFullTranslationFlow()
    {
        string testFile = @"E:\Project\ComicOrganizer\test\005.jpg";
        if (!File.Exists(testFile))
        {
            _output.WriteLine($"[ERROR] Test file 005.jpg does not exist at: {testFile}");
            Assert.Fail($"Test file 005.jpg not found at: {testFile}");
            return;
        }

        // 1. 載入組態並覆寫模型名稱
        var config = LoadRealAppConfig();
        config.TranslationModelName = "gemma-4-e4b-it"; // 強制在測試中覆寫使用 gemma-4-e4b-it

        _output.WriteLine($"[Config] Endpoint: {config.TranslationEndpoint}");
        _output.WriteLine($"[Config] Model Name (Overridden): {config.TranslationModelName}");
        _output.WriteLine($"[Config] ApiType: {config.TranslationApiType}");

        // 2. 執行 OCR 與段落合併
        IOcrService ocrService = config.OcrEngineType == 1 ? (IOcrService)new PaddleOcrService() : new WindowsOcrService();
        var langCode = "ja-JP";
        if (!ocrService.IsLanguageSupported(langCode))
        {
            _output.WriteLine($"[WARNING] ja-JP is not supported. Using fallback.");
            langCode = ocrService.GetSupportedLanguages().FirstOrDefault() ?? "zh-Hant-TW";
        }

        _output.WriteLine($"[OCR] Loading image: {testFile}");
        using var stream = File.OpenRead(testFile);
        using var image = ReaderImageLoader.Decode(stream, ".jpg");
        _output.WriteLine($"[OCR] Image Size: {image.Width}x{image.Height}");

        var rawBlocks = await ocrService.RecognizeAsync(image, langCode);
        _output.WriteLine($"[OCR] Raw blocks count: {rawBlocks.Count}");

        var mergedBlocks = ocrService.MergeBlocks(rawBlocks, isVerticalFlow: true);
        _output.WriteLine($"[OCR] Merged blocks count: {mergedBlocks.Count}");

        var textsToTranslate = mergedBlocks.Select(b => b.Text).ToList();
        _output.WriteLine("\n--- [Texts to Translate] ---");
        for (int i = 0; i < textsToTranslate.Count; i++)
        {
            _output.WriteLine($"  [{i}] {textsToTranslate[i]}");
        }

        if (textsToTranslate.Count == 0)
        {
            _output.WriteLine("[INFO] No texts found to translate.");
            return;
        }

        // 3. 呼叫翻譯服務並計算時間
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        var translator = new LlmTranslationService(httpClient);
        translator.Configure(config.TranslationEndpoint, config.TranslationModelName, (LlmApiType)config.TranslationApiType);

        _output.WriteLine($"\n[LLM] Translating batch with model {config.TranslationModelName}...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = await translator.TranslateBatchAsync(textsToTranslate);
        stopwatch.Stop();

        _output.WriteLine($"\n[LLM Performance]");
        _output.WriteLine($"  Total Response Time: {stopwatch.ElapsedMilliseconds} ms");
        _output.WriteLine($"  Average Time Per Sentence: {stopwatch.ElapsedMilliseconds / (float)Math.Max(1, textsToTranslate.Count):F2} ms");

        _output.WriteLine("\n--- [Translation Results] ---");
        bool anyTranslated = false;
        for (int i = 0; i < textsToTranslate.Count; i++)
        {
            string translation = i < results.Count ? results[i] : "(No Result)";
            bool changed = translation != textsToTranslate[i];
            if (changed) anyTranslated = true;
            _output.WriteLine($"  [{i}] ORIGINAL:   {textsToTranslate[i]}");
            _output.WriteLine($"       TRANSLATED: {translation} {(changed ? "✓" : "⚠ UNCHANGED")}");
        }

        _output.WriteLine($"\n[Summary] Translation success: {anyTranslated}");

        if (!string.IsNullOrEmpty(translator.LastError))
        {
            _output.WriteLine($"\n[LLM ERROR] {translator.LastError}");
            _output.WriteLine("[NOTE] Results above are FALLBACK (original text returned due to error).");
        }

        Assert.NotNull(results);
        Assert.Equal(textsToTranslate.Count, results.Count);
        Assert.True(anyTranslated, "Translation failed - all results are identical to input (likely LLM error). Check LastError for details.");
    }

    [Fact]
    public async Task TestBatchFullTranslationFlow()
    {
        string testDir = @"E:\Project\ComicOrganizer\test";
        if (!Directory.Exists(testDir))
        {
            _output.WriteLine($"[ERROR] Test directory does not exist at: {testDir}");
            Assert.Fail($"Test directory not found at: {testDir}");
            return;
        }

        var config = LoadRealAppConfig();
        // 強制在測試中覆寫使用 gemma-4-e4b-it，以確保在沒有配置真實 API key 時仍能正常測試
        config.TranslationModelName = "gemma-4-e4b-it";

        _output.WriteLine($"[Config] Endpoint: {config.TranslationEndpoint}");
        _output.WriteLine($"[Config] Model Name (Overridden): {config.TranslationModelName}");
        _output.WriteLine($"[Config] ApiType: {config.TranslationApiType}");

        // 強制使用 PaddleOcrService 進行多圖測試
        IOcrService ocrService = new PaddleOcrService();
        var langCode = "ja-JP";

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        var translator = new LlmTranslationService(httpClient);
        translator.Configure(config.TranslationEndpoint, config.TranslationModelName, (LlmApiType)config.TranslationApiType);

        var imageFiles = Directory.GetFiles(testDir, "*.jpg")
            .Where(f => !f.EndsWith("_draw.jpg") && !f.EndsWith("_improved_draw.jpg") && !f.EndsWith("_original_draw.jpg"))
            .OrderBy(f => f)
            .ToList();

        _output.WriteLine($"[Batch] Found {imageFiles.Count} test images.");

        foreach (var imgFile in imageFiles)
        {
            _output.WriteLine($"\n==========================================");
            _output.WriteLine($"[Image] Processing file: {Path.GetFileName(imgFile)}");
            _output.WriteLine($"==========================================");

            using var stream = File.OpenRead(imgFile);
            using var image = ReaderImageLoader.Decode(stream, Path.GetExtension(imgFile));
            _output.WriteLine($"[OCR] Image Size: {image.Width}x{image.Height}");

            var rawBlocks = await ocrService.RecognizeAsync(image, langCode);
            _output.WriteLine($"[OCR] Raw blocks count: {rawBlocks.Count}");

            var mergedBlocks = ocrService.MergeBlocks(rawBlocks, isVerticalFlow: true);
            _output.WriteLine($"[OCR] Merged blocks count: {mergedBlocks.Count}");

            var textsToTranslate = mergedBlocks.Select(b => b.Text).ToList();
            if (textsToTranslate.Count == 0)
            {
                _output.WriteLine("[INFO] No texts found to translate in this image.");
                continue;
            }

            _output.WriteLine("\n--- [Texts to Translate] ---");
            for (int i = 0; i < textsToTranslate.Count; i++)
            {
                _output.WriteLine($"  [{i}] {textsToTranslate[i]}");
            }

            _output.WriteLine($"\n[LLM] Translating...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var results = await translator.TranslateBatchAsync(textsToTranslate);
            stopwatch.Stop();

            _output.WriteLine($"\n[LLM Performance] Response Time: {stopwatch.ElapsedMilliseconds} ms");

            _output.WriteLine("\n--- [Translation Results] ---");
            for (int i = 0; i < textsToTranslate.Count; i++)
            {
                string translation = i < results.Count ? results[i] : "(No Result)";
                bool changed = translation != textsToTranslate[i];
                string status = string.IsNullOrEmpty(translation) ? "✓ (FILTERED/EMPTY)" : (changed ? "✓ TRANSLATED" : "⚠ UNCHANGED");
                _output.WriteLine($"  [{i}] ORIGINAL:   {textsToTranslate[i]}");
                _output.WriteLine($"       TRANSLATED: \"{translation}\" ({status})");
            }

            if (!string.IsNullOrEmpty(translator.LastError))
            {
                _output.WriteLine($"\n[LLM ERROR] {translator.LastError}");
            }
        }
    }

    private const float VerticalCharSpacingRatio = 0.40f;
    private const float LineSpacingRatio = 0.45f;

    [Fact]
    public async Task TestTranslationDrawingFlow()
    {
        string testFile = @"E:\Project\ComicOrganizer\test\005.jpg";
        if (!File.Exists(testFile))
        {
            _output.WriteLine($"[ERROR] Test file 005.jpg not found at: {testFile}");
            Assert.Fail($"Test file 005.jpg not found.");
            return;
        }

        // 1. 執行 PaddleOCR
        IOcrService ocrService = new PaddleOcrService();
        using var stream = File.OpenRead(testFile);
        using var originalImage = ReaderImageLoader.Decode(stream, ".jpg");

        var rawBlocks = await ocrService.RecognizeAsync(originalImage, "ja-JP");
        var mergedBlocks = ocrService.MergeBlocks(rawBlocks, isVerticalFlow: true);

        // 2. 模擬翻譯映射
        var translations = new Dictionary<string, string>();
        foreach (var block in mergedBlocks)
        {
            var txt = block.Text;
            if (txt.Contains("春燕")) translations[txt] = "春燕大人";
            else if (txt.Contains("恵香") || txt.Contains("参ります")) translations[txt] = "惠香大人馬上就來";
            else if (txt.Contains("ご辛抱") || txt.Contains("しばらく")) translations[txt] = "請再稍微忍耐一下…";
            else if (txt.Contains("何をなさって") || txt.Contains("おいでですか")) translations[txt] = "您、您在做什麼呢！？";
            else if (txt.Contains("離宮")) translations[txt] = "現在我要去離宮了";
            else if (txt.Contains("ちる")) translations[txt] = "散落了…";
            else translations[txt] = "（中文譯文）";
        }

        // 3. 繪製並儲存 Original (舊版塗白整個大框)
        string originalDrawFile = @"E:\Project\ComicOrganizer\test\005_original_draw.jpg";
        RenderAndSave(originalImage, mergedBlocks, translations, originalDrawFile, useImprovedMask: false);
        _output.WriteLine($"[Render] Original drawing saved to: {originalDrawFile}");

        // 4. 繪製並儲存 Improved (新版子區塊分別塗白)
        string improvedDrawFile = @"E:\Project\ComicOrganizer\test\005_improved_draw.jpg";
        RenderAndSave(originalImage, mergedBlocks, translations, improvedDrawFile, useImprovedMask: true);
        _output.WriteLine($"[Render] Improved drawing saved to: {improvedDrawFile}");

        Assert.True(File.Exists(originalDrawFile));
        Assert.True(File.Exists(improvedDrawFile));
    }

    private void RenderAndSave(Image originalImage, List<OcrBlock> blocks, Dictionary<string, string> translations, string destPath, bool useImprovedMask)
    {
        // 複製一份以進行繪製
        using var bmp = new Bitmap(originalImage.Width, originalImage.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(originalImage, 0, 0, originalImage.Width, originalImage.Height);

            using SolidBrush whiteBrush = new SolidBrush(Color.White);
            using SolidBrush blackBrush = new SolidBrush(Color.Black);

            foreach (var item in blocks)
            {
                if (translations.TryGetValue(item.Text, out string chineseText))
                {
                    float x = item.X;
                    float y = item.Y;
                    float w = item.Width;
                    float h = item.Height;
                    RectangleF rect = new RectangleF(x, y, w, h);

                    // 遮罩塗白
                    if (useImprovedMask && item.Children != null && item.Children.Count > 0)
                    {
                        foreach (var child in item.Children)
                        {
                            RectangleF cRect = new RectangleF(child.X, child.Y, child.Width, child.Height);
                            RectangleF cRectInflated = RectangleF.Inflate(cRect, 2f, 2f);
                            g.FillRectangle(whiteBrush, cRectInflated);
                        }
                    }
                    else
                    {
                        RectangleF rectInflated = RectangleF.Inflate(rect, 2f, 2f);
                        g.FillRectangle(whiteBrush, rectInflated);
                    }

                    // 繪製文字
                    float emSize = EstimateOptimalFontSize(g, chineseText, w, h);
                    using Font font = new Font("Microsoft JhengHei UI", emSize, FontStyle.Bold);
                    DrawVerticalString(g, chineseText, font, blackBrush, rect);
                }
            }
        }
        bmp.Save(destPath, System.Drawing.Imaging.ImageFormat.Jpeg);
    }

    private float EstimateOptimalFontSize(Graphics g, string text, float w, float h)
    {
        if (string.IsNullOrEmpty(text)) return 9f;
        float num = w * 0.95f;
        float num2 = h * 0.95f;
        float result = 6f;
        for (float num3 = 40f; num3 >= 6f; num3 -= 0.5f)
        {
            float charSize = num3 * 1.333f;
            float charSpacing = charSize * VerticalCharSpacingRatio;
            float cellH = charSize + charSpacing;
            int num6 = (int)(num2 / cellH);
            if (num6 > 0)
            {
                int num7 = (int)Math.Ceiling((double)text.Length / (double)num6);
                float num8 = (float)num7 * charSize + (float)(num7 - 1) * (charSize * LineSpacingRatio);
                if (num8 <= num)
                {
                    result = num3;
                    break;
                }
            }
        }
        return result;
    }

    private void DrawVerticalString(Graphics g, string text, Font font, Brush brush, RectangleF rect)
    {
        float charSize = font.SizeInPoints * 1.333f;
        float charSpacing = charSize * VerticalCharSpacingRatio;
        float lineSpacing = charSize * LineSpacingRatio;

        float cellW = charSize;
        float cellH = charSize + charSpacing;

        int maxCharsPerLine = (int)Math.Max(1f, rect.Height / cellH);
        List<string> lines = new List<string>();
        for (int i = 0; i < text.Length; i += maxCharsPerLine)
        {
            int length = Math.Min(maxCharsPerLine, text.Length - i);
            lines.Add(text.Substring(i, length));
        }

        int count = lines.Count;
        float totalWidth = (float)count * cellW + (float)(count - 1) * lineSpacing;
        float startX = rect.Right - cellW;
        if (totalWidth < rect.Width)
        {
            startX = rect.Right - (rect.Width - totalWidth) / 2f - cellW;
        }
        startX = Math.Max(rect.Left, startX);
        using StringFormat format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = (StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
        };

        for (int j = 0; j < count; j++)
        {
            string lineText = lines[j];
            float currentX = startX - (float)j * (cellW + lineSpacing);
            if (currentX < rect.Left - cellW * 0.5f) break;

            float lineH = (float)lineText.Length * cellH;
            float startY = rect.Top;
            if (lineH < rect.Height)
            {
                startY = rect.Top + (rect.Height - lineH) / 2f;
            }

            for (int k = 0; k < lineText.Length; k++)
            {
                char c = lineText[k];
                float y = startY + (float)k * cellH;
                g.DrawString(layoutRectangle: new RectangleF(currentX, y, cellW, charSize), s: c.ToString(), font: font, brush: brush, format: format);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Tests;

public class OcrTest
{
    private readonly ITestOutputHelper _output;

    public OcrTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestJapaneseOcrOnWebp()
    {
        string testFile = @"E:\Project\ComicOrganizer\test\10.webp";
        if (!File.Exists(testFile))
        {
            _output.WriteLine($"[WARNING] Test file 10.webp does not exist at: {testFile}. Skipping test.");
            return;
        }

        IOcrService ocrService = new WindowsOcrService();
        var langCode = "ja-JP";
        if (!ocrService.IsLanguageSupported(langCode))
        {
            _output.WriteLine($"[WARNING] ja-JP is not supported. Using fallback.");
            langCode = ocrService.GetSupportedLanguages().FirstOrDefault() ?? "zh-Hant-TW";
        }

        _output.WriteLine($"Target File: {Path.GetFileName(testFile)}");
        using var stream = File.OpenRead(testFile);
        using var image = ReaderImageLoader.Decode(stream, ".webp");
        _output.WriteLine($"Image Size: {image.Width}x{image.Height}");

        var rawBlocks = await ocrService.RecognizeAsync(image, langCode);
        _output.WriteLine($"\n--- [1. 原始 OCR 偵測到的行 ({rawBlocks.Count} 行)] ---");
        for (int i = 0; i < rawBlocks.Count; i++)
        {
            var b = rawBlocks[i];
            _output.WriteLine($"  [{i + 1}] Text: \"{b.Text}\" [Box: X={b.X}, Y={b.Y}, W={b.Width}, H={b.Height}]");
        }

        var mergedBlocks = ocrService.MergeBlocks(rawBlocks, isVerticalFlow: true);
        _output.WriteLine($"\n--- [2. 經相鄰演算法合併後的句子 (共 {mergedBlocks.Count} 句)] ---");
        for (int i = 0; i < mergedBlocks.Count; i++)
        {
            var b = mergedBlocks[i];
            _output.WriteLine($"  句子 {i + 1}: \"{b.Text}\"");
            _output.WriteLine($"        [合併邊界: X={b.X:F0}, Y={b.Y:F0}, W={b.Width:F0}, H={b.Height:F0}]");
        }

        // 驗證關鍵合併是否成功
        bool hasTarget = mergedBlocks.Any(b => b.Text.Contains("パッと見そんなに変わってないけど"));
        Assert.True(hasTarget, "應成功合併「パッと見そんなに」與「変わってないけど」");
    }

    [Fact]
    public async Task TestPaddleOcrJapanese()
    {
        string testFile = @"E:\Project\ComicOrganizer\test\005.jpg";
        if (!File.Exists(testFile))
        {
            _output.WriteLine($"[ERROR] Test file 005.jpg does not exist at: {testFile}");
            Assert.Fail($"Test file 005.jpg not found at: {testFile}");
            return;
        }

        _output.WriteLine("Initializing PaddleOcrService (may download model if first run)...");
        IOcrService ocrService = new PaddleOcrService();
        
        _output.WriteLine($"Target File: {Path.GetFileName(testFile)}");
        using var stream = File.OpenRead(testFile);
        using var image = ReaderImageLoader.Decode(stream, ".jpg");
        _output.WriteLine($"Image Size: {image.Width}x{image.Height}");

        var rawBlocks = await ocrService.RecognizeAsync(image, "ja-JP");
        _output.WriteLine($"\n--- [PaddleOCR 原始偵測結果 ({rawBlocks.Count} 行)] ---");
        for (int i = 0; i < rawBlocks.Count; i++)
        {
            var b = rawBlocks[i];
            _output.WriteLine($"  [{i + 1}] Text: \"{b.Text}\" [Box: X={b.X:F0}, Y={b.Y:F0}, W={b.Width:F0}, H={b.Height:F0}]");
        }

        var mergedBlocks = ocrService.MergeBlocks(rawBlocks, isVerticalFlow: true);
        _output.WriteLine($"\n--- [PaddleOCR 合併後句子 (共 {mergedBlocks.Count} 句)] ---");
        for (int i = 0; i < mergedBlocks.Count; i++)
        {
            var b = mergedBlocks[i];
            _output.WriteLine($"  句子 {i + 1}: \"{b.Text}\"");
        }

        Assert.NotEmpty(rawBlocks);
    }

    [Fact]
    public void DumpPaddleOcrTypes()
    {
        var type = typeof(PaddleOCRSharp.OCRResult);
        _output.WriteLine($"=== OCRResult Properties ({type.FullName}) ===");
        foreach (var prop in type.GetProperties())
        {
            _output.WriteLine($"Prop: {prop.Name} ({prop.PropertyType.FullName})");
        }
        
        var blockType = typeof(PaddleOCRSharp.TextBlock);
        _output.WriteLine($"\n=== TextBlock Properties ({blockType.FullName}) ===");
        foreach (var prop in blockType.GetProperties())
        {
            _output.WriteLine($"Prop: {prop.Name} ({prop.PropertyType.FullName})");
        }

        var pointType = typeof(PaddleOCRSharp.OCRPoint);
        _output.WriteLine($"\n=== OCRPoint Properties ({pointType.FullName}) ===");
        foreach (var prop in pointType.GetProperties())
        {
            _output.WriteLine($"Prop: {prop.Name} ({prop.PropertyType.FullName})");
        }
        foreach (var field in pointType.GetFields())
        {
            _output.WriteLine($"Field: {field.Name} ({field.FieldType.FullName})");
        }

        var paramType = typeof(PaddleOCRSharp.OCRParameter);
        _output.WriteLine($"\n=== OCRParameter Properties ({paramType.FullName}) ===");
        foreach (var prop in paramType.GetProperties())
        {
            _output.WriteLine($"Prop: {prop.Name} ({prop.PropertyType.FullName})");
        }
    }

    [Fact]
    public async Task TestUrls()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        var urls = new[]
        {
            "https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_det_infer.tar",
            "https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/japan_PP-OCRv3_rec_infer.tar",
            "https://paddleocr.bj.bcebos.com/PP-OCRv3/multilingual/japan_PP-OCRv3_mobile_rec_infer.tar",
            "https://paddleocr.bj.bcebos.com/DYF/Cls/ch_ppocr_mobile_v2.0_cls_infer.tar",
            "https://fastly.jsdelivr.net/gh/PaddlePaddle/PaddleOCR@main/ppocr/utils/dict/japan_dict.txt"
        };
        foreach (var url in urls)
        {
            try
            {
                var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                _output.WriteLine($"{url} -> {(int)resp.StatusCode} ({resp.ReasonPhrase})");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{url} -> Error: {ex.Message}");
            }
        }
    }
}

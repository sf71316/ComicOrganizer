using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.Tests;

public class FontSizeConsistencyTests
{
    private readonly ITestOutputHelper _output;

    public FontSizeConsistencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestEstimateOriginalFontSize_VerticalLayout()
    {
        float dpiRatio = 96f / 72f; 
        var normalBlock = new OcrBlock
        {
            Width = 30,
            Height = 150,
            OriginalText = "これはテストです"
        };

        float fontSize = FontSizeOptimizer.EstimateOriginalFontSizeInPoints(
            !string.IsNullOrEmpty(normalBlock.OriginalText) ? normalBlock.OriginalText : normalBlock.Text, 
            normalBlock.Width, 
            normalBlock.Height, 
            dpiRatio
        );
        _output.WriteLine($"Normal Block Estimated Font Size: {fontSize:F2}pt");
        Assert.True(fontSize >= 8f && fontSize <= 15f);
    }

    [Fact]
    public void TestCalculateConsistentFontSizes_Scenarios()
    {
        float dpiRatio = 96f / 72f;
        var blocks = new List<OcrBlock>();

        var blockA = new OcrBlock
        {
            Text = "譯文A",
            OriginalText = "こんにちは",
            Width = 35,
            Height = 100
        };
        blocks.Add(blockA);

        var blockB = new OcrBlock
        {
            Text = "做什麼呢！？",
            OriginalText = "何ですか！？",
            Width = 60,
            Height = 180
        };
        blocks.Add(blockB);

        var blockC = new OcrBlock
        {
            Text = "這是一段非常長的翻譯文字，基準字體一定塞不下",
            OriginalText = "短い原文", 
            Width = 30,
            Height = 80
        };
        blocks.Add(blockC);

        var blockD = new OcrBlock
        {
            Text = "大喊！",
            OriginalText = "何だ！",
            Width = 80,
            Height = 120
        };
        blocks.Add(blockD);

        Func<OcrBlock, float, float> mockEstimateOptimalFontSize = (b, dpi) =>
        {
            if (b.Text == blockC.Text) return 10f;
            if (b.Text == blockB.Text) return 24f;
            if (b.Text == blockA.Text) return 18f;
            if (b.Text == blockD.Text) return 24f;
            return 14f;
        };

        var result = FontSizeOptimizer.CalculateConsistentFontSizes(blocks, dpiRatio, mockEstimateOptimalFontSize);

        foreach (var block in blocks)
        {
            float origSize = FontSizeOptimizer.EstimateOriginalFontSizeInPoints(
                !string.IsNullOrEmpty(block.OriginalText) ? block.OriginalText : block.Text,
                block.Width,
                block.Height,
                dpiRatio
            );
            float finalSize = result[block];
            _output.WriteLine($"Block: [{block.OriginalText}] -> [{block.Text}] | Orig Est: {origSize:F2}pt | Final Size: {finalSize:F2}pt");
        }

        float sizeA = result[blockA];
        Assert.True(sizeA >= 12f && sizeA <= 16f);
        float sizeB = result[blockB];
        Assert.True(sizeB > sizeA);
        float sizeC = result[blockC];
        Assert.Equal(10f, sizeC);
        float sizeD = result[blockD];
        Assert.True(sizeD > sizeA);
    }

    [Fact]
    public async Task TestOnRealImage_005()
    {
        string testFile = @"E:\Project\ComicOrganizer\test\005.jpg";
        Assert.True(File.Exists(testFile), $"Test file 005.jpg not found at: {testFile}");

        IOcrService ocrService = new WindowsOcrService();
        using var originalBmp = new Bitmap(testFile);
        
        var rawBlocks = await ocrService.RecognizeAsync(originalBmp, "ja-JP");
        var mergedBlocks = ocrService.MergeBlocks(rawBlocks, isVerticalFlow: true);

        _output.WriteLine($"OCR Merged Blocks count: {mergedBlocks.Count}");

        foreach (var b in mergedBlocks)
        {
            b.OriginalText = b.Text; 
            b.Text = TranslateBlock(b.OriginalText);
            _output.WriteLine($"Block OCR: [{b.OriginalText}] -> Trans: [{b.Text}] at X={b.X:F0}, Y={b.Y:F0}, W={b.Width:F0}, H={b.Height:F0}");
        }

        string artifactDir = @"C:\Users\admin\.gemini\antigravity-cli\brain\08d7f3e3-ad5a-4807-9a84-73520c27975b";
        Directory.CreateDirectory(artifactDir);

        float pixelsPerPoint = 96f / 72f; 

        // 3. 繪製並儲存【調整前】
        using (var originalDrawBmp = new Bitmap(originalBmp))
        {
            using (var g = Graphics.FromImage(originalDrawBmp))
            {
                using var whiteBrush = new SolidBrush(Color.White);
                using var blackBrush = new SolidBrush(Color.Black);

                foreach (var b in mergedBlocks)
                {
                    if (string.IsNullOrWhiteSpace(b.Text)) continue;

                    RectangleF rect = new RectangleF(b.X, b.Y, b.Width, b.Height);
                    
                    if (b.Children != null && b.Children.Count > 0)
                    {
                        foreach (var c in b.Children)
                        {
                            g.FillRectangle(whiteBrush, RectangleF.Inflate(new RectangleF(c.X, c.Y, c.Width, c.Height), 2f, 2f));
                        }
                    }
                    else
                    {
                        g.FillRectangle(whiteBrush, RectangleF.Inflate(rect, 2f, 2f));
                    }

                    float emSize = FontSizeOptimizer.EstimateOptimalFontSize(b.Text, b.Width, b.Height, pixelsPerPoint, 24f);
                    using Font font = new Font("Microsoft JhengHei UI", emSize, FontStyle.Bold);
                    
                    var state = g.Save();
                    g.SetClip(rect);
                    FontSizeOptimizer.DrawVerticalString(g, b.Text, font, blackBrush, rect, pixelsPerPoint);
                    g.Restore(state);
                }
            }
            originalDrawBmp.Save(Path.Combine(artifactDir, "005_original_draw.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        // 4. 繪製並儲存【調整後】
        using (var improvedDrawBmp = new Bitmap(originalBmp))
        {
            using (var g = Graphics.FromImage(improvedDrawBmp))
            {
                using var whiteBrush = new SolidBrush(Color.White);
                using var blackBrush = new SolidBrush(Color.Black);

                var fontSizes = FontSizeOptimizer.CalculateConsistentFontSizes(
                    mergedBlocks, 
                    pixelsPerPoint, 
                    (b, ppp) => FontSizeOptimizer.EstimateOptimalFontSize(b.Text, b.Width, b.Height, ppp, 24f)
                );

                foreach (var b in mergedBlocks)
                {
                    if (string.IsNullOrWhiteSpace(b.Text)) continue;

                    RectangleF rect = new RectangleF(b.X, b.Y, b.Width, b.Height);
                    
                    if (b.Children != null && b.Children.Count > 0)
                    {
                        foreach (var c in b.Children)
                        {
                            g.FillRectangle(whiteBrush, RectangleF.Inflate(new RectangleF(c.X, c.Y, c.Width, c.Height), 2f, 2f));
                        }
                    }
                    else
                    {
                        g.FillRectangle(whiteBrush, RectangleF.Inflate(rect, 2f, 2f));
                    }

                    float emSize = fontSizes.ContainsKey(b) ? fontSizes[b] : 14f;
                    using Font font = new Font("Microsoft JhengHei UI", emSize, FontStyle.Bold);
                    
                    var state = g.Save();
                    g.SetClip(rect);
                    FontSizeOptimizer.DrawVerticalString(g, b.Text, font, blackBrush, rect, pixelsPerPoint);
                    g.Restore(state);
                }
            }
            improvedDrawBmp.Save(Path.Combine(artifactDir, "005_improved_draw.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
        }

        _output.WriteLine($"Saved images to: {artifactDir}");
    }

    private string TranslateBlock(string originalText)
    {
        if (string.IsNullOrEmpty(originalText)) return "";

        string clean = originalText.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("　", "");
        
        if (clean.Contains("しゅる") || clean.Contains("しる") || clean.Contains("ばさ") || clean.Contains("タッ") || clean.Contains("とさ") || clean.Length <= 1)
        {
            return ""; 
        }

        if (clean.Contains("春燕さま"))
        {
            if (clean.Contains("っ")) return "春燕大人！";
            return "春燕大人";
        }
        if (clean.Contains("惠香さま")) 
        {
            return "惠香大人馬上就來";
        }
        if (clean.Contains("もうしばらく")) 
        {
            return "請再稍微忍耐一下…";
        }
        if (clean.Contains("何をなさって")) 
        {
            return "您在做什麼呢！？";
        }
        if (clean.Contains("離宮")) 
        {
            return "──現在要去離宮";
        }

        return originalText; 
    }
}

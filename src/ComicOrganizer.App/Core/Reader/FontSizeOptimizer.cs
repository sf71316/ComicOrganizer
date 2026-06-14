using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ComicOrganizer.App.Core;

public static class FontSizeOptimizer
{
    public const float VerticalCharSpacingRatio = 0.01f;
    public const float LineSpacingRatio = 0.45f;

    /// <summary>
    /// 根據原文文字的區塊大小與字數，估算其原始字體大小（單位為 Points）。
    /// </summary>
    public static float EstimateOriginalFontSizeInPoints(string rawText, float w, float h, float pixelsPerPoint)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return 12f;
        }

        var lines = rawText.Split(new[] { "\r\n", "\r", "\n", " " }, StringSplitOptions.RemoveEmptyEntries);
        int lineCount = Math.Max(1, lines.Length);
        int maxLineChars = lines.Select(l => l.Length).DefaultIfEmpty(0).Max();
        if (maxLineChars == 0) maxLineChars = rawText.Length;

        if (w <= 0 || h <= 0) return 12f;

        bool isVertical = w < h;
        float estimatedPixelSize;

        if (isVertical)
        {
            float sizeByWidth = w / lineCount;
            float sizeByHeight = h / (maxLineChars + VerticalCharSpacingRatio * (maxLineChars - 1));
            estimatedPixelSize = Math.Min(sizeByWidth, sizeByHeight);
        }
        else
        {
            float sizeByHeight = h / lineCount;
            float sizeByWidth = w / (maxLineChars + LineSpacingRatio * (maxLineChars - 1));
            estimatedPixelSize = Math.Min(sizeByHeight, sizeByWidth);
        }

        float sizeInPoints = estimatedPixelSize / pixelsPerPoint;
        return Math.Clamp(sizeInPoints, 6f, 40f);
    }

    /// <summary>
    /// 計算整頁一致性的譯文字體大小。
    /// </summary>
    public static Dictionary<OcrBlock, float> CalculateConsistentFontSizes(
        List<OcrBlock> blocks, 
        float pixelsPerPoint,
        Func<OcrBlock, float, float> estimateOptimalFontSizeForBlock)
    {
        var result = new Dictionary<OcrBlock, float>();
        if (blocks == null || blocks.Count == 0) return result;

        var origSizes = new List<float>();
        var blockOrigSizes = new Dictionary<OcrBlock, float>();

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            float origSize = EstimateOriginalFontSize(block, pixelsPerPoint);
            blockOrigSizes[block] = origSize;

            // 一般合理的字體大小在 8pt ~ 25pt 之間，作為中位數基準的採樣來源
            if (origSize >= 8f && origSize <= 25f)
            {
                origSizes.Add(origSize);
            }
        }

        float baseFontSize = 14f; 
        if (origSizes.Count > 0)
        {
            origSizes.Sort();
            int mid = origSizes.Count / 2;
            baseFontSize = (origSizes.Count % 2 != 0) 
                ? origSizes[mid] 
                : (origSizes[mid - 1] + origSizes[mid]) / 2f;
        }

        baseFontSize = Math.Clamp(baseFontSize, 14f, 30f);

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            float maxFitSize = estimateOptimalFontSizeForBlock(block, pixelsPerPoint);
            blockOrigSizes.TryGetValue(block, out float origSize);

            float targetSize;

            // 大喊字體：原文大小大於基準的 1.4 倍且大於 18pt
            if (origSize > baseFontSize * 1.4f && origSize > 18f)
            {
                targetSize = Math.Min(origSize, maxFitSize);
                targetSize = Math.Min(targetSize, 24f);
            }
            // 悄悄話字體：原文大小小於基準的 0.8 倍且小於 11pt
            else if (origSize < baseFontSize * 0.8f && origSize < 11f && origSize >= 6f)
            {
                targetSize = Math.Min(origSize, maxFitSize);
            }
            // 一般對話：使用基準字體
            else
            {
                targetSize = baseFontSize;
                if (targetSize > maxFitSize)
                {
                    targetSize = maxFitSize;
                }
            }

            result[block] = Math.Max(6f, targetSize);
        }

        return result;
    }

    /// <summary>
    /// 估算 block 原始字體大小（pt）。多欄（Children &gt; 1）時改用各子框（原始單欄）分別估再取中位數——
    /// 否則合併後的多欄文字被 CleanCjkSpaces 去空白後會被當成「單一長欄」，
    /// 用 高度/總字數 算出每字過小（多欄越多低估越嚴重），導致譯文字級被鎖在偏小值。
    /// 無 children 或單欄時退回原本的整框估算。
    /// </summary>
    private static float EstimateOriginalFontSize(OcrBlock block, float pixelsPerPoint)
    {
        if (block.Children != null && block.Children.Count > 1)
        {
            var perColumn = new List<float>();
            foreach (var ch in block.Children)
            {
                if (ch.Width <= 0 || ch.Height <= 0) continue;
                string ct = !string.IsNullOrEmpty(ch.OriginalText) ? ch.OriginalText : ch.Text;
                if (string.IsNullOrWhiteSpace(ct)) continue;
                perColumn.Add(EstimateOriginalFontSizeInPoints(ct, ch.Width, ch.Height, pixelsPerPoint));
            }
            if (perColumn.Count > 0)
            {
                perColumn.Sort();
                return perColumn[perColumn.Count / 2];
            }
        }

        string raw = !string.IsNullOrEmpty(block.OriginalText) ? block.OriginalText : block.Text;
        return EstimateOriginalFontSizeInPoints(raw, block.Width, block.Height, pixelsPerPoint);
    }

    /// <summary>
    /// 在不超出邊框的前提下，估算單個文字區塊的最佳字體大小。
    /// </summary>
    public static float EstimateOptimalFontSize(string text, float w, float h, float pixelsPerPoint, float maxFontSize = 24f)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 9f;
        }
        float num = w * 0.95f;
        float num2 = h * 0.95f;

        float area = w * h;
        if (text.Length <= 2 && area > 10000f)
        {
            maxFontSize = Math.Min(maxFontSize, 16f);
        }
        else if (text.Length <= 3 && area > 20000f)
        {
            maxFontSize = Math.Min(maxFontSize, 18f);
        }

        float result = 6f;
        for (float num3 = maxFontSize; num3 >= 6f; num3 -= 0.5f)
        {
            float charSize = num3 * pixelsPerPoint;
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

    /// <summary>
    /// 垂直繪製中文字串。
    /// </summary>
    public static void DrawVerticalString(Graphics g, string text, Font font, Brush brush, RectangleF rect, float pixelsPerPoint)
    {
        float charSize = font.SizeInPoints * pixelsPerPoint;
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
            FormatFlags = StringFormatFlags.NoWrap
        };
        for (int j = 0; j < count; j++)
        {
            string lineText = lines[j];
            float currentX = startX - (float)j * (cellW + lineSpacing);
            if (currentX < rect.Left - cellW * 0.5f)
            {
                break;
            }
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

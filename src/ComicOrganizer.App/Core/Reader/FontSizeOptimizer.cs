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

    /// <summary>
    /// 依「實際偵測到的文字欄（Children）」位置垂直繪製譯文：欄位水平中心取自各子欄、
    /// 每欄字數由合併框高度決定並在合併框內垂直置中。與 DrawVerticalString 的「等距欄位網格」不同——
    /// 這裡讓字嚴格落在已塗白的子欄上，避免不規則/多欄氣泡時譯文畫到欄間插圖或被 clip 切斷。
    /// columns 需依閱讀順序（直書由右至左）排列；空清單時退回整個 merged 當單欄。
    /// </summary>
    public static void DrawVerticalStringInColumns(Graphics g, string text, Font font, Brush brush, RectangleF merged, List<RectangleF> columns, float pixelsPerPoint)
    {
        if (string.IsNullOrEmpty(text)) return;

        var cols = (columns != null && columns.Count > 0) ? columns : new List<RectangleF> { merged };

        float charSize = font.SizeInPoints * pixelsPerPoint;
        float cellH = charSize * (1f + VerticalCharSpacingRatio);

        using StringFormat format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        // 各欄容量：軟容量（0.97×高度，用於均分留邊距）與硬容量（完整高度，懸掛標點的上限）
        int n = cols.Count;
        int[] caps = new int[n];
        int[] hardCaps = new int[n];
        for (int i = 0; i < n; i++)
        {
            caps[i] = Math.Max(1, (int)(cols[i].Height * 0.97f / cellH));
            hardCaps[i] = Math.Max(caps[i], (int)(cols[i].Height / cellH));
        }

        // 分欄規劃：欄位均分 + 基本禁則微調（含懸掛標點）
        int[] counts = PlanColumnCounts(text, caps, hardCaps);
        int useCols = counts.Length;

        // 共同頂端：所有欄「頂端對齊」，整塊文字在 merged 區內垂直置中。
        // 不再各欄各自於子欄內置中——否則字數少的欄（如只分到 1 字的欄）會浮在跟其他欄不同的高度（跑位）。
        int maxCount = 1;
        for (int ci = 0; ci < useCols; ci++) maxCount = Math.Max(maxCount, counts[ci]);
        float blockH = Math.Min(maxCount * cellH, merged.Height);
        float commonTop = merged.Top + Math.Max(0f, (merged.Height - blockH) / 2f);

        // 逐欄繪製（X 取自各子欄中心；Y 一律從 commonTop 由上往下排）
        int idx = 0;
        for (int ci = 0; ci < useCols && idx < text.Length; ci++)
        {
            int take = Math.Min(counts[ci], text.Length - idx);
            string colText = text.Substring(idx, take);
            idx += take;

            RectangleF col = cols[ci];
            float cx = col.Left + col.Width / 2f - charSize / 2f;
            for (int k = 0; k < colText.Length; k++)
            {
                float y = commonTop + k * cellH;
                DrawVerticalGlyph(g, colText[k], font, brush, cx, y, charSize, format);
            }
        }
    }

    /// <summary>
    /// 追隨原文各欄（Children）形狀垂直繪製譯文——比均勻網格更適合圓/不規則/斜基線氣泡：
    /// 每欄可容字數依該子欄高度（容量），循序由右至左填字，各欄在「自身子欄的 Y/高度」內垂直置中。
    /// 因此邊欄短、中欄長、左右基線可不同高，整體貼合原文（=氣泡）形狀，不會在圓/斜氣泡邊角戳出。
    /// 並自動縮字確保：字寬 ≤ 相鄰欄間距(不重疊) 且 各欄總容量 ≥ 字數(不溢出)。
    /// columns 為各子欄矩形；空清單時退回 merged 走自算均勻網格 <see cref="DrawVerticalStringAuto"/>。
    /// </summary>
    public static void DrawVerticalStringShaped(Graphics g, string text, Font font, Brush brush, RectangleF merged, List<RectangleF>? columns, float pixelsPerPoint)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (columns == null || columns.Count == 0)
        {
            DrawVerticalStringAuto(g, text, font, brush, merged, pixelsPerPoint);
            return;
        }

        var cols = columns.OrderByDescending(c => c.Left + c.Width / 2f).ToList(); // 由右至左
        int n = cols.Count;

        // 相鄰欄中心最小間距（避免水平重疊的字寬上限）
        float minPitch = float.MaxValue;
        for (int i = 1; i < n; i++)
        {
            float d = Math.Abs((cols[i - 1].Left + cols[i - 1].Width / 2f) - (cols[i].Left + cols[i].Width / 2f));
            if (d > 1f) minPitch = Math.Min(minPitch, d);
        }
        if (minPitch == float.MaxValue) minPitch = cols[0].Width;

        float maxCharSize = font.SizeInPoints * pixelsPerPoint;
        float minCharSize = 6f * pixelsPerPoint;

        // 縮字：字寬 ≤ 欄間距(不重疊) 且 各欄總容量 ≥ 字數(不溢出)。多數情況第一輪即成立(不縮)。
        float charSize = maxCharSize;
        for (float cs = maxCharSize; cs >= minCharSize; cs -= 0.5f * pixelsPerPoint)
        {
            charSize = cs;
            float cH = cs * (1f + VerticalCharSpacingRatio);
            int totalCap = 0;
            for (int i = 0; i < n; i++) totalCap += Math.Max(1, (int)(cols[i].Height * 0.97f / cH));
            if (cs <= minPitch && totalCap >= text.Length) break;
        }

        float cellH = charSize * (1f + VerticalCharSpacingRatio);
        using Font drawFont = new Font(font.FontFamily, charSize / pixelsPerPoint, font.Style);
        using StringFormat format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        // 各欄容量（依子欄高度）；循序由右至左填字（追隨原文形狀），再做基本禁則微調
        int[] caps = new int[n];
        for (int i = 0; i < n; i++) caps[i] = Math.Max(1, (int)(cols[i].Height * 0.97f / cellH));
        int[] counts = new int[n];
        int rem = text.Length;
        for (int i = 0; i < n && rem > 0; i++)
        {
            counts[i] = Math.Min(caps[i], rem);
            rem -= counts[i];
        }
        if (rem > 0) counts[n - 1] += rem; // 容量不足（縮字後理論上不會）→ 末欄收尾，交 clip
        AdjustKinsoku(text, counts, caps);

        int idx = 0;
        for (int ci = 0; ci < n && idx < text.Length; ci++)
        {
            int take = Math.Min(counts[ci], text.Length - idx);
            if (take <= 0) continue;
            string colText = text.Substring(idx, take);
            idx += take;

            RectangleF col = cols[ci];
            float cx = col.Left + col.Width / 2f - charSize / 2f;
            float lineH = take * cellH;
            float startY = col.Top + Math.Max(0f, (col.Height - lineH) / 2f); // 在「自身子欄」內垂直置中 → 追隨形狀/斜基線
            for (int k = 0; k < take; k++)
                DrawVerticalGlyph(g, colText[k], drawFont, brush, cx, startY + k * cellH, charSize, format);
        }
    }

    // 欄距比例：相鄰欄中心距 = charSize × (1 + ColumnGapRatio)。
    // 固定欄距 ≥ 字寬 → 結構上不重疊（manga-image-translator 直排預設 0.2，取同量級）。
    public const float ColumnGapRatio = 0.18f;

    /// <summary>
    /// 在氣泡區域內「自算均勻直排網格」繪製譯文（路線2：原文已精準抹除、整塊乾淨時用）。
    /// 與 <see cref="DrawVerticalStringInColumns"/> 不同——不綁原文 OCR 子欄位置，
    /// 而是用我們控制的欄距（charSize×(1+ColumnGapRatio)）自行排版，欄距恆 ≥ 字寬故不重疊；
    /// 字數由區域高度決定每欄容量，欄數由字數推得，整塊在區域內水平/垂直置中、各欄頂端對齊。
    /// 保留 kinsoku 斷行（PlanColumnCounts）與全形標點修正（DrawVerticalGlyph）。直書由右至左。
    /// </summary>
    public static void DrawVerticalStringAuto(Graphics g, string text, Font font, Brush brush, RectangleF region, float pixelsPerPoint)
    {
        if (string.IsNullOrEmpty(text) || region.Width <= 0 || region.Height <= 0) return;
        int len = text.Length;

        // 內縮留邊：合併框是緊貼原文的 bbox，留些邊距讓譯文不貼著(圓/不規則)氣泡邊緣、邊角不擦出。
        float insetX = region.Width * 0.05f, insetY = region.Height * 0.04f;
        var inset = RectangleF.Inflate(region, -insetX, -insetY);
        if (inset.Width > 4f && inset.Height > 4f) region = inset;

        using StringFormat format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        // 自動縮字保證整段塞進 region（不溢出氣泡）：從外部估算字級往下找，
        // 直到「區域寬可容欄數 × 每欄容量 ≥ 字數」。多數氣泡第一輪就成立（不縮）；長譯文/窄框才縮。下限 6pt。
        float maxCharSize = font.SizeInPoints * pixelsPerPoint;
        float minCharSize = 6f * pixelsPerPoint;
        float charSize = maxCharSize;
        for (float cs = maxCharSize; cs >= minCharSize; cs -= 0.5f * pixelsPerPoint)
        {
            charSize = cs;
            float cH = cs * (1f + VerticalCharSpacingRatio);
            float pitch = cs * (1f + ColumnGapRatio);
            int mCols = Math.Max(1, (int)(region.Width / pitch));
            int cap = Math.Max(1, (int)(region.Height * 0.97f / cH));
            if ((long)mCols * cap >= len) break;
        }

        float cellH = charSize * (1f + VerticalCharSpacingRatio);
        float colPitch = charSize * (1f + ColumnGapRatio);
        using Font drawFont = new Font(font.FontFamily, charSize / pixelsPerPoint, font.Style);

        // 每欄容量（區域高度，留 3% 邊距）；需要欄數 = ceil(字數 / 容量)；受區域寬度可容納欄數上限
        int capPerCol = Math.Max(1, (int)(region.Height * 0.97f / cellH));
        int needCols = Math.Max(1, (int)Math.Ceiling((double)len / capPerCol));
        int maxColsByWidth = Math.Max(1, (int)(region.Width / colPitch));
        int nCols = Math.Min(needCols, maxColsByWidth);

        // 均分各欄字數 + 基本禁則（容量一致）
        int[] caps = new int[nCols];
        for (int i = 0; i < nCols; i++) caps[i] = capPerCol;
        int[] counts = PlanColumnCounts(text, caps, caps);
        nCols = counts.Length;

        // 整塊置中：水平依實際欄數、垂直依最長欄；各欄頂端對齊
        float blockW = nCols * colPitch;
        float rightEdge = region.Right - Math.Max(0f, (region.Width - blockW) / 2f); // 第一欄（最右）右緣
        int maxCount = 1;
        for (int i = 0; i < counts.Length; i++) maxCount = Math.Max(maxCount, counts[i]);
        float blockH = Math.Min(maxCount * cellH, region.Height);
        float commonTop = region.Top + Math.Max(0f, (region.Height - blockH) / 2f);

        int idx = 0;
        for (int ci = 0; ci < nCols && idx < text.Length; ci++)
        {
            int take = Math.Min(counts[ci], text.Length - idx);
            string colText = text.Substring(idx, take);
            idx += take;

            float colCenterX = rightEdge - colPitch * (ci + 0.5f); // 由右至左
            float cx = colCenterX - charSize / 2f;
            for (int k = 0; k < colText.Length; k++)
            {
                float y = commonTop + k * cellH;
                DrawVerticalGlyph(g, colText[k], drawFont, brush, cx, y, charSize, format);
            }
        }
    }

    // 第 2 類：破折號/省略號/長音/波浪——直排需旋轉 90°（橫向 → 縱向）。
    private const string VertRotate90 = "…‥⋯—―─━ー～〜";
    // 第 1 類：句読点——橫排字身在左下，直排需移到右上。
    private const string VertShiftTopRight = "。、，．｡､";

    /// <summary>
    /// 在 (cx, y) 的 charSize 方格內繪製單一直排字元，對特定全形標點做修正：
    /// 破折號/省略號/長音旋轉 90°；句読点往右上位移；其餘照常置中繪製。
    /// 純座標/旋轉處理，不依賴字型的直排字形（避免缺字 tofu）。
    /// </summary>
    private static void DrawVerticalGlyph(Graphics g, char c, Font font, Brush brush, float cx, float y, float charSize, StringFormat format)
    {
        if (VertRotate90.IndexOf(c) >= 0)
        {
            // 繞方格中心旋轉 90°：水平的「…」「—」變成縱向
            float ccx = cx + charSize / 2f;
            float ccy = y + charSize / 2f;
            var st = g.Save();
            g.TranslateTransform(ccx, ccy);
            g.RotateTransform(90f);
            g.TranslateTransform(-ccx, -ccy);
            g.DrawString(c.ToString(), font, brush, new RectangleF(cx, y, charSize, charSize), format);
            g.Restore(st);
        }
        else if (VertShiftTopRight.IndexOf(c) >= 0)
        {
            // 往右上偏移（比例可調）：右 +0.18em、上 −0.34em
            var rect = new RectangleF(cx + charSize * 0.18f, y - charSize * 0.34f, charSize, charSize);
            g.DrawString(c.ToString(), font, brush, rect, format);
        }
        else
        {
            g.DrawString(c.ToString(), font, brush, new RectangleF(cx, y, charSize, charSize), format);
        }
    }

    /// <summary>
    /// 把長度為 text.Length 的字串規劃成各欄字數：(1) 用「能容納全文的最少欄數」做欄位均分（避免孤兒欄），
    /// (2) 對欄界做基本禁則微調。回傳的 counts 總和 == text.Length（容量極不足時最後一欄會超出，交由 clip 收尾）。
    /// caps 為各欄容量（依閱讀順序，直書由右至左）。純函式，便於單元測試。
    /// </summary>
    public static int[] PlanColumnCounts(string text, int[] caps, int[]? hardCaps = null)
    {
        int len = text?.Length ?? 0;
        if (len == 0 || caps == null || caps.Length == 0) return Array.Empty<int>();

        // 使用欄數：能容納全文的最少欄數（由右至左累加）
        int useCols = caps.Length;
        int cum = 0;
        for (int i = 0; i < caps.Length; i++) { cum += caps[i]; if (cum >= len) { useCols = i + 1; break; } }

        // (1) 欄位均分：盡量平均分配到 useCols 欄，受各欄容量限制，避免「孤兒欄」
        int[] counts = new int[useCols];
        int remaining = len;
        for (int i = 0; i < useCols; i++)
        {
            int colsLeft = useCols - i;
            int want = (remaining + colsLeft - 1) / colsLeft;   // ceil(remaining / colsLeft)
            counts[i] = Math.Min(want, caps[i]);
            remaining -= counts[i];
        }
        if (remaining > 0) counts[useCols - 1] += remaining;     // 容量不足時不漏字（極端情況，交由 clip 收尾）

        // (2) 基本禁則微調（追い込み 允許掛到硬容量＝完整欄高，讓標點懸掛在欄底邊距內仍可見）
        AdjustKinsoku(text!, counts, hardCaps ?? caps);
        return counts;
    }

    // 禁則字集（繁中為主，含常見日文）：行頭禁則＝收尾標點/括號/連續記號/小書假名；行尾禁則＝起始括號。
    private const string KinsokuLineStart = "。．，、；：！？”’｝〕）］》〉」』】…‧—─～ー々ぁぃぅぇぉっゃゅょゎ・,.!?;:)]}";
    private const string KinsokuLineEnd = "（［｛〔《〈「『【“‘([{";

    private static bool IsLineStartForbidden(char c) => KinsokuLineStart.IndexOf(c) >= 0;
    private static bool IsLineEndForbidden(char c) => KinsokuLineEnd.IndexOf(c) >= 0;

    /// <summary>
    /// 對已均分的每欄字數做基本禁則微調（受 limit 容量限制，調不動就放棄該次以保不溢出）：
    /// - 追い込み：下一欄欄首是行頭禁則字（。、！？」…）→ 拉回本欄欄尾（可連拉，處理「！？」「…。」）。
    /// - 追い出し：本欄欄尾是行尾禁則字（起始括號 「（）→ 推到下一欄欄首。
    /// limit 為各欄可容字數上限（懸掛標點時傳硬容量＝完整欄高，標點掛在欄底邊距內仍在塗白/clip 範圍內）。
    /// </summary>
    private static void AdjustKinsoku(string text, int[] counts, int[] limit)
    {
        int start = 0;
        for (int i = 0; i < counts.Length - 1; i++)
        {
            // 追い込み：拉回行頭禁則字。允許超出硬容量 1 字（懸掛標點 ぶら下げ）——
            // 繪製端在 lineH>欄高 時自動靠頂，超出的標點落在欄底；標點經右上位移後 ink 仍在塗白/clip 範圍內。
            while (start + counts[i] < text.Length
                   && IsLineStartForbidden(text[start + counts[i]])
                   && counts[i] + 1 <= limit[i] + 1
                   && counts[i + 1] - 1 >= 1)
            {
                counts[i]++; counts[i + 1]--;
            }

            if (start + counts[i] - 1 >= 0 && start + counts[i] - 1 < text.Length
                && IsLineEndForbidden(text[start + counts[i] - 1])
                && counts[i] - 1 >= 1
                && counts[i + 1] + 1 <= limit[i + 1])
            {
                counts[i]--; counts[i + 1]++;
            }

            start += counts[i];
        }
    }
}

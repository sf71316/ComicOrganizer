using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 跨平台 Skia 漫畫譯文氣泡遮罩與直排排版渲染器。
/// 採用與 <see cref="BubbleEnvelope"/> / <see cref="WordBreakAdvisor"/> / <see cref="ChineseWordDict"/> 相同的排版邏輯：
/// 1. 優先對原本文字塊 / 子字塊區域進行局部白底(或暗底)塗白遮罩，保留氣泡外背景與手繪效果。
/// 2. 於偵測到的 <see cref="BubbleEnvelope"/> 真實輪廓內進行面積法字級、詞邊界吸附與標點旋轉排版。
/// 3. 支援輸出為合成後的 <see cref="SKImage"/>，可接續交由 <see cref="ReaderImageEnhancer"/> 進行夜間護眼等全局濾鏡，確保氣泡與紙面零色差。
/// </summary>
public static class SkiaTranslationRenderer
{
    private const string FontResourceName = "ComicOrganizer.App.Resources.fonts.SweiSansCJKtc-Medium.ttf";
    private const float VerticalCharSpacingRatio = 0.01f;
    private const float ColumnGapRatio = 0.18f;
    private const float BubbleVertFillRatio = 0.85f;
    private const float BubbleFillRatio = 0.50f;
    private const float GlobalMaxFontSizePx = 48f;
    private const float MinCharSizePx = 10f;

    private const string VertRotate90 = "…‥⋯—―─━ー～〜";
    private const string VertShiftTopRight = "。、，．｡､";
    private const string KinsokuLineStart = "。．，、；：！？”’｝〕）］》〉」』】…‧—─～ー々ぁぃぅぇぉっゃゅょゎ・,.!?;:)]}";
    private const string KinsokuLineEnd = "（［｛〔《〈「『【“‘([{";

    private static readonly Lazy<SKTypeface> CachedTypeface = new(() =>
    {
        try
        {
            var asm = typeof(SkiaTranslationRenderer).Assembly;
            using var s = asm.GetManifestResourceStream(FontResourceName);
            if (s != null)
            {
                var typeface = SKTypeface.FromStream(s);
                if (typeface != null) return typeface;
            }
        }
        catch
        {
            // Fallback below
        }

        return SKTypeface.FromFamilyName("Microsoft JhengHei UI")
               ?? SKTypeface.FromFamilyName("Noto Sans CJK TC")
               ?? SKTypeface.Default;
    });

    public static SKTypeface TranslationTypeface => CachedTypeface.Value;

    /// <summary>
    /// 在目標頁面的副本上繪製所有已翻譯氣泡，回傳全新的合成 <see cref="SKImage"/>（呼叫端負責 Dispose）。
    /// </summary>
    public static SKImage RenderTranslatedPage(SKImage sourceImage, IReadOnlyList<OcrBlock> blocks)
    {
        if (sourceImage == null) throw new ArgumentNullException(nameof(sourceImage));
        if (blocks == null || blocks.Count == 0)
            return sourceImage;

        var info = new SKImageInfo(sourceImage.Width, sourceImage.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null) return sourceImage;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.DrawImage(sourceImage, 0, 0);

        DrawTranslatedBlocks(canvas, sourceImage, blocks);
        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// 在現有 <paramref name="canvas"/>（來源影像像素座標空間）繪製所有氣泡遮罩與直排繁中譯文。
    /// </summary>
    public static void DrawTranslatedBlocks(SKCanvas canvas, SKImage sourceImage, IReadOnlyList<OcrBlock> blocks)
    {
        if (blocks == null || blocks.Count == 0) return;

        using var bmp = SKBitmap.FromImage(sourceImage);
        if (bmp == null) return;

        using var lightMaskPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var darkMaskPaint = new SKPaint { Color = new SKColor(18, 18, 22), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var lightTextPaint = new SKPaint { Color = new SKColor(246, 246, 246), IsAntialias = true };
        using var darkTextPaint = new SKPaint { Color = new SKColor(20, 20, 24), IsAntialias = true };

        var allRegions = blocks
            .Select(b => new SKRectI((int)b.X, (int)b.Y, (int)(b.X + b.Width), (int)(b.Y + b.Height)))
            .ToList();

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (string.IsNullOrWhiteSpace(block.Text)) continue;

            var blockers = new List<SKRectI>(Math.Max(0, allRegions.Count - 1));
            for (int j = 0; j < allRegions.Count; j++)
            {
                if (j != i) blockers.Add(allRegions[j]);
            }

            var textRegion = allRegions[i];
            var env = BubbleEnvelope.Detect(bmp, textRegion, blockers: blockers);
            var maskPaint = env.DarkBubble ? darkMaskPaint : lightMaskPaint;
            var textPaint = env.DarkBubble ? lightTextPaint : darkTextPaint;

            // 1. 局部塗白（優先遮罩原始子文字行框 + 微量安全 padding，最大化保留插圖背景）
            if (block.Children != null && block.Children.Count > 0)
            {
                foreach (var child in block.Children)
                {
                    var padX = Math.Max(2f, child.Width * 0.08f);
                    var padY = Math.Max(2f, child.Height * 0.05f);
                    var r = new SKRect(
                        child.X - padX,
                        child.Y - padY,
                        child.X + child.Width + padX,
                        child.Y + child.Height + padY);
                    canvas.DrawRoundRect(r, 4f, 4f, maskPaint);
                }
            }
            else
            {
                var padX = Math.Max(3f, block.Width * 0.08f);
                var padY = Math.Max(3f, block.Height * 0.06f);
                var r = new SKRect(
                    block.X - padX,
                    block.Y - padY,
                    block.X + block.Width + padX,
                    block.Y + block.Height + padY);
                canvas.DrawRoundRect(r, 6f, 6f, maskPaint);
            }

            // 2. 在氣泡實際輪廓（若有效）或 fallback bounding box 內排直書譯文
            if (env.Valid)
            {
                DrawVerticalTextInEnvelope(canvas, block.Text, env, textPaint);
            }
            else
            {
                DrawVerticalTextInBox(
                    canvas,
                    block.Text,
                    new SKRect(block.X, block.Y, block.X + block.Width, block.Y + block.Height),
                    textPaint);
            }
        }
    }

    private static void DrawVerticalTextInEnvelope(SKCanvas canvas, string text, BubbleEnvelope env, SKPaint textPaint)
    {
        int len = text.Length;
        if (len == 0 || env.Right - env.Left < 4) return;

        const float InsetRatio = 0.09f;
        int iLeft = env.Left + (int)((env.Right - env.Left) * InsetRatio);
        int iRight = env.Right - (int)((env.Right - env.Left) * InsetRatio);
        if (iRight - iLeft < 4) { iLeft = env.Left; iRight = env.Right; }

        (int t, int b) Inset(int x)
        {
            int xi = Math.Clamp(x, env.Left, env.Right);
            int tt = env.Top[xi], bb = env.Bottom[xi];
            if (tt < 0 || bb < tt) return (tt, bb);
            int pad = (int)((bb - tt) * InsetRatio);
            return (tt + pad, bb - pad);
        }

        float bubbleW = iRight - iLeft;
        float centerX = (iLeft + iRight) / 2f;

        long area = 0;
        int hcnt = 0;
        for (int x = iLeft; x <= iRight; x++)
        {
            var (t, b) = Inset(x);
            if (t >= 0 && b >= t) { area += (b - t + 1); hcnt++; }
        }
        if (area <= 0 || hcnt == 0) return;
        float avgH = (float)area / hcnt;

        float k = (1f + VerticalCharSpacingRatio) * (1f + ColumnGapRatio);
        float charSize = Math.Clamp((float)Math.Sqrt(BubbleFillRatio * area / (len * k)), MinCharSizePx, GlobalMaxFontSizePx);

        float cellH = 0f, pitch = 0f;
        int nCols = 1;
        float[] colCx = Array.Empty<float>();
        int[] colTop = Array.Empty<int>(), colBot = Array.Empty<int>(), caps = Array.Empty<int>();

        for (int iter = 0; iter < 16; iter++)
        {
            cellH = charSize * (1f + VerticalCharSpacingRatio);
            pitch = charSize * (1f + ColumnGapRatio);
            int maxCols = Math.Max(1, (int)(bubbleW / pitch));
            int avgCap = Math.Max(1, (int)(avgH * BubbleVertFillRatio / cellH));
            nCols = Math.Clamp((int)Math.Ceiling((double)len / avgCap), 1, maxCols);

            float blockRight = centerX + nCols * pitch / 2f;
            colCx = new float[nCols];
            colTop = new int[nCols];
            colBot = new int[nCols];
            caps = new int[nCols];
            long totalCap = 0;

            for (int i = 0; i < nCols; i++)
            {
                float cx = blockRight - pitch * (i + 0.5f);
                colCx[i] = cx;
                int xi = Math.Clamp((int)cx, iLeft, iRight);
                var (t, b) = Inset(xi);
                colTop[i] = t;
                colBot[i] = b;
                caps[i] = (t < 0 || b < t) ? 1 : Math.Max(1, (int)((b - t) * BubbleVertFillRatio / cellH));
                totalCap += caps[i];
            }

            if (totalCap >= len || charSize <= MinCharSizePx) break;
            charSize = Math.Max(MinCharSizePx, charSize * 0.9f);
        }

        var counts = BuildBalancedCounts(text, len, nCols, caps);
        using var font = new SKFont(TranslationTypeface, charSize);

        int idx = 0;
        for (int ci = 0; ci < nCols && idx < len; ci++)
        {
            int take = Math.Min(counts[ci], len - idx);
            if (take <= 0) continue;
            string colText = text.Substring(idx, take);
            idx += take;

            int t = colTop[ci], b = colBot[ci];
            if (t < 0 || b < t)
            {
                var (ft, fb) = Inset((int)colCx[ci]);
                t = ft;
                b = fb;
            }
            float colH = take * cellH;
            float startY = t + Math.Max(0f, ((b - t) - colH) / 2f);
            float colCenterX = colCx[ci];

            canvas.Save();
            canvas.ClipRect(new SKRect(colCenterX - charSize * 0.6f, t - 2f, colCenterX + charSize * 0.6f, b + 2f));
            for (int kk = 0; kk < take; kk++)
            {
                DrawVerticalGlyph(canvas, colText[kk], font, textPaint, colCenterX, startY + kk * cellH, charSize);
            }
            canvas.Restore();
        }
    }

    private static void DrawVerticalTextInBox(SKCanvas canvas, string text, SKRect box, SKPaint textPaint)
    {
        int len = text.Length;
        if (len == 0 || box.Width <= 4 || box.Height <= 4) return;

        float area = box.Width * box.Height;
        float k = (1f + VerticalCharSpacingRatio) * (1f + ColumnGapRatio);
        float charSize = Math.Clamp((float)Math.Sqrt(BubbleFillRatio * area / (len * k)), MinCharSizePx, GlobalMaxFontSizePx);

        float cellH = 0f, pitch = 0f;
        int nCols = 1;
        int capPerCol = 1;
        for (int iter = 0; iter < 16; iter++)
        {
            cellH = charSize * (1f + VerticalCharSpacingRatio);
            pitch = charSize * (1f + ColumnGapRatio);
            int maxCols = Math.Max(1, (int)(box.Width / pitch));
            capPerCol = Math.Max(1, (int)(box.Height * 0.90f / cellH));
            nCols = Math.Clamp((int)Math.Ceiling((double)len / capPerCol), 1, maxCols);
            if (nCols * capPerCol >= len || charSize <= MinCharSizePx) break;
            charSize = Math.Max(MinCharSizePx, charSize * 0.9f);
        }

        var caps = new int[nCols];
        Array.Fill(caps, capPerCol);
        var counts = BuildBalancedCounts(text, len, nCols, caps);
        using var font = new SKFont(TranslationTypeface, charSize);

        float blockRight = box.MidX + nCols * pitch / 2f;
        int idx = 0;
        for (int ci = 0; ci < nCols && idx < len; ci++)
        {
            int take = Math.Min(counts[ci], len - idx);
            if (take <= 0) continue;
            string colText = text.Substring(idx, take);
            idx += take;

            float colCenterX = blockRight - pitch * (ci + 0.5f);
            float colH = take * cellH;
            float startY = box.Top + Math.Max(0f, (box.Height - colH) / 2f);

            canvas.Save();
            canvas.ClipRect(new SKRect(box.Left - 2f, box.Top - 2f, box.Right + 2f, box.Bottom + 2f));
            for (int kk = 0; kk < take; kk++)
            {
                DrawVerticalGlyph(canvas, colText[kk], font, textPaint, colCenterX, startY + kk * cellH, charSize);
            }
            canvas.Restore();
        }
    }

    private static int[] BuildBalancedCounts(string text, int len, int nCols, int[] caps)
    {
        var counts = new int[nCols];
        int rem = len;
        for (int i = 0; i < nCols; i++)
        {
            int colsLeft = nCols - i;
            int want = (rem + colsLeft - 1) / colsLeft;
            counts[i] = Math.Min(want, caps[i]);
            rem -= counts[i];
        }

        int guard = 0;
        while (rem > 0 && guard++ < 10000)
        {
            int best = -1, bestHead = 0;
            for (int i = 0; i < nCols; i++)
            {
                int h = caps[i] - counts[i];
                if (h > bestHead) { bestHead = h; best = i; }
            }
            if (best < 0) { counts[nCols - 1] += rem; break; }
            counts[best]++;
            rem--;
        }

        SnapColumnBreaksToWords(text, counts, caps);
        AdjustKinsoku(text, counts, caps);
        return counts;
    }

    private static void SnapColumnBreaksToWords(string text, int[] counts, int[] caps)
    {
        int nCols = counts.Length;
        if (nCols < 2 || ChineseWordDict.Shared.Count == 0) return;

        const int Radius = 2;
        var forbidden = WordBreakAdvisor.BuildForbiddenBreaks(text);
        int prefix = 0;
        for (int i = 0; i < nCols - 1; i++)
        {
            prefix += counts[i];
            int b = prefix;
            int leftRoom = Math.Min(counts[i] - 1, caps[i + 1] - counts[i + 1]);
            int rightRoom = Math.Min(counts[i + 1] - 1, caps[i] - counts[i]);
            int minB = b - Math.Max(0, leftRoom);
            int maxB = b + Math.Max(0, rightRoom);

            int nb = WordBreakAdvisor.NearestAllowedBreak(forbidden, b, Radius, minB, maxB);
            if (nb >= 0 && nb != b)
            {
                int delta = nb - b;
                counts[i] += delta;
                counts[i + 1] -= delta;
                prefix = nb;
            }
        }
    }

    private static void AdjustKinsoku(string text, int[] counts, int[] limit)
    {
        int start = 0;
        for (int i = 0; i < counts.Length - 1; i++)
        {
            while (start + counts[i] < text.Length &&
                   KinsokuLineStart.IndexOf(text[start + counts[i]]) >= 0 &&
                   counts[i] + 1 <= limit[i] + 1 &&
                   counts[i + 1] - 1 >= 1)
            {
                counts[i]++;
                counts[i + 1]--;
            }

            if (start + counts[i] - 1 >= 0 && start + counts[i] - 1 < text.Length &&
                KinsokuLineEnd.IndexOf(text[start + counts[i] - 1]) >= 0 &&
                counts[i] - 1 >= 1 &&
                counts[i + 1] + 1 <= limit[i + 1])
            {
                counts[i]--;
                counts[i + 1]++;
            }

            start += counts[i];
        }
    }

    private static void DrawVerticalGlyph(SKCanvas canvas, char c, SKFont font, SKPaint paint, float centerX, float topY, float charSize)
    {
        string s = c.ToString();
        var metrics = font.Metrics;
        float baselineY = topY + (charSize / 2f) - (metrics.Ascent + metrics.Descent) / 2f;

        if (VertRotate90.IndexOf(c) >= 0)
        {
            float ccy = topY + charSize / 2f;
            canvas.Save();
            canvas.RotateDegrees(90f, centerX, ccy);
            canvas.DrawText(s, centerX, baselineY, SKTextAlign.Center, font, paint);
            canvas.Restore();
        }
        else if (VertShiftTopRight.IndexOf(c) >= 0)
        {
            canvas.DrawText(
                s,
                centerX + charSize * 0.22f,
                baselineY - charSize * 0.34f,
                SKTextAlign.Center,
                font,
                paint);
        }
        else
        {
            canvas.DrawText(s, centerX, baselineY, SKTextAlign.Center, font, paint);
        }
    }
}

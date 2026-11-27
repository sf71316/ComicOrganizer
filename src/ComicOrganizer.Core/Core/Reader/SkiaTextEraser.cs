using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 跨平台 Skia 原文文字遮罩抹除器：依文字遮罩由邊界往內以鄰近背景色做 BFS 擴散填補，
/// 讓平底、漸層或帶有網點的對話氣泡皆能自然補底，不殘留硬邊白矩形。
/// </summary>
public static class SkiaTextEraser
{
    /// <summary>
    /// 將全頁偵測遮罩限縮於「具有非空譯文的對話區塊」內，
    /// 以保留氣泡外或刻意略過翻譯的手繪日文狀聲詞與特效字體。
    /// </summary>
    public static byte[] RestrictMaskToTranslatedBlocks(
        byte[] rawMask,
        int width,
        int height,
        IReadOnlyList<OcrBlock> blocks,
        int paddingPx = 8)
    {
        if (rawMask == null) throw new ArgumentNullException(nameof(rawMask));
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));

        int expectedLen = checked(width * height);
        if (rawMask.Length != expectedLen)
        {
            throw new ArgumentException($"Mask length {rawMask.Length} does not match {width}x{height}.", nameof(rawMask));
        }

        var scoped = new byte[expectedLen];
        if (blocks == null || blocks.Count == 0)
        {
            return scoped;
        }

        int pad = Math.Max(0, paddingPx);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text))
            {
                continue;
            }

            if (block.Children != null && block.Children.Count > 0)
            {
                foreach (var child in block.Children)
                {
                    CopyRegionMask(rawMask, scoped, width, height, child.X, child.Y, child.Width, child.Height, pad);
                }
            }
            else
            {
                CopyRegionMask(rawMask, scoped, width, height, block.X, block.Y, block.Width, block.Height, pad);
            }
        }

        return scoped;
    }

    /// <summary>檢查指定區塊內是否至少包含一個待抹除的遮罩像素。</summary>
    public static bool HasMaskPixelsInBlock(byte[] mask, int width, int height, OcrBlock block, int paddingPx = 8)
    {
        if (mask == null || mask.Length != width * height)
        {
            return false;
        }

        if (block.Children != null && block.Children.Count > 0)
        {
            foreach (var child in block.Children)
            {
                if (HasMaskPixelsInRect(mask, width, height, child.X, child.Y, child.Width, child.Height, paddingPx))
                {
                    return true;
                }
            }

            return false;
        }

        return HasMaskPixelsInRect(mask, width, height, block.X, block.Y, block.Width, block.Height, paddingPx);
    }

    /// <summary>依文字遮罩抹除 <paramref name="source"/> 中的文字像素，回傳新的 <see cref="SKBitmap"/>（Bgra8888）。</summary>
    public static SKBitmap? Erase(SKImage source, byte[] mask)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        using var srcBmp = SKBitmap.FromImage(source);
        if (srcBmp == null)
        {
            return null;
        }

        return Erase(srcBmp, mask);
    }

    /// <summary>依文字遮罩抹除 <paramref name="source"/> 中的文字像素，回傳新的 <see cref="SKBitmap"/>（不改動原圖）。</summary>
    public static SKBitmap Erase(SKBitmap source, byte[] mask)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (mask == null) throw new ArgumentNullException(nameof(mask));

        int w = source.Width;
        int h = source.Height;
        if (w <= 0 || h <= 0) throw new ArgumentOutOfRangeException(nameof(source));
        if (mask.Length != w * h)
        {
            throw new ArgumentException($"Mask length {mask.Length} does not match bitmap {w}x{h}.", nameof(mask));
        }

        var result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(result))
        {
            canvas.DrawBitmap(source, 0, 0);
        }

        bool anyMasked = false;
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] != 0)
            {
                anyMasked = true;
                break;
            }
        }

        if (!anyMasked)
        {
            return result;
        }

        var px = result.GetPixelSpan();
        int stride = result.RowBytes;
        DiffusionFillBgra8888(px, stride, w, h, mask);
        result.NotifyPixelsChanged();
        return result;
    }

    private static void CopyRegionMask(
        byte[] srcMask,
        byte[] dstMask,
        int width,
        int height,
        float fx,
        float fy,
        float fw,
        float fh,
        int pad)
    {
        if (!TryClampRect(fx, fy, fw, fh, pad, width, height, out int x0, out int y0, out int x1, out int y1))
        {
            return;
        }

        for (int y = y0; y < y1; y++)
        {
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                if (srcMask[row + x] != 0)
                {
                    dstMask[row + x] = 1;
                }
            }
        }
    }

    private static bool HasMaskPixelsInRect(
        byte[] mask,
        int width,
        int height,
        float fx,
        float fy,
        float fw,
        float fh,
        int pad)
    {
        if (!TryClampRect(fx, fy, fw, fh, pad, width, height, out int x0, out int y0, out int x1, out int y1))
        {
            return false;
        }

        for (int y = y0; y < y1; y++)
        {
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                if (mask[row + x] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryClampRect(
        float fx,
        float fy,
        float fw,
        float fh,
        int pad,
        int width,
        int height,
        out int x0,
        out int y0,
        out int x1,
        out int y1)
    {
        x0 = Math.Clamp((int)Math.Floor(fx) - pad, 0, width);
        y0 = Math.Clamp((int)Math.Floor(fy) - pad, 0, height);
        x1 = Math.Clamp((int)Math.Ceiling(fx + fw) + pad, 0, width);
        y1 = Math.Clamp((int)Math.Ceiling(fy + fh) + pad, 0, height);
        return x1 > x0 && y1 > y0;
    }

    private static void DiffusionFillBgra8888(Span<byte> px, int stride, int width, int height, byte[] mask)
    {
        int total = width * height;
        var known = new bool[total];
        var queued = new bool[total];
        for (int i = 0; i < total; i++)
        {
            known[i] = mask[i] == 0;
        }

        var queue = new Queue<int>();
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                if (known[i])
                {
                    continue;
                }

                if ((x > 0 && known[i - 1]) ||
                    (x < width - 1 && known[i + 1]) ||
                    (y > 0 && known[i - width]) ||
                    (y < height - 1 && known[i + width]))
                {
                    queue.Enqueue(i);
                    queued[i] = true;
                }
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % width;
            int y = i / width;

            int bs = 0, gs = 0, rs = 0, c = 0;
            if (x > 0 && known[i - 1])
            {
                int o = y * stride + (x - 1) * 4;
                bs += px[o];
                gs += px[o + 1];
                rs += px[o + 2];
                c++;
            }

            if (x < width - 1 && known[i + 1])
            {
                int o = y * stride + (x + 1) * 4;
                bs += px[o];
                gs += px[o + 1];
                rs += px[o + 2];
                c++;
            }

            if (y > 0 && known[i - width])
            {
                int o = (y - 1) * stride + x * 4;
                bs += px[o];
                gs += px[o + 1];
                rs += px[o + 2];
                c++;
            }

            if (y < height - 1 && known[i + width])
            {
                int o = (y + 1) * stride + x * 4;
                bs += px[o];
                gs += px[o + 1];
                rs += px[o + 2];
                c++;
            }

            if (c > 0)
            {
                int o = y * stride + x * 4;
                px[o] = (byte)(bs / c);
                px[o + 1] = (byte)(gs / c);
                px[o + 2] = (byte)(rs / c);
                px[o + 3] = 255;
            }

            known[i] = true;

            if (x > 0 && !known[i - 1] && !queued[i - 1])
            {
                queue.Enqueue(i - 1);
                queued[i - 1] = true;
            }

            if (x < width - 1 && !known[i + 1] && !queued[i + 1])
            {
                queue.Enqueue(i + 1);
                queued[i + 1] = true;
            }

            if (y > 0 && !known[i - width] && !queued[i - width])
            {
                queue.Enqueue(i - width);
                queued[i - width] = true;
            }

            if (y < height - 1 && !known[i + width] && !queued[i + width])
            {
                queue.Enqueue(i + width);
                queued[i + width] = true;
            }
        }
    }
}

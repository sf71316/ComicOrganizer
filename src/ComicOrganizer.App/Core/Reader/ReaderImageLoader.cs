using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Reader 與封面共用的圖片解碼 helper：
/// WebP 由 SkiaSharp（SKCodec → BGRA8888 Unpremul → GDI+ Format32bppArgb）解碼，
/// 其他格式維持走 GDI+ Image.FromStream。
/// </summary>
public static class ReaderImageLoader
{
    public static Image Decode(Stream stream, string extension)
    {
        if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeWebpToBitmap(stream);
        }
        return Image.FromStream(stream);
    }

    /// <summary>從實體 WebP 檔案解碼成 GDI+ Bitmap (Format32bppArgb)。</summary>
    public static Bitmap DecodeWebpToBitmap(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        return DecodeWebpToBitmap(fs);
    }

    /// <summary>
    /// 從串流以 SkiaSharp SKCodec 解碼 WebP 成 GDI+ Bitmap (Format32bppArgb)。
    /// 採用 BGRA8888 + Unpremul（與 GDI+ Format32bppArgb 記憶體配置一致）；
    /// 若 Bitmap.Stride 與 TargetInfo.RowBytes 相符則直接解碼進 Scan0，否則透過 SKBitmap 逐列複製。
    /// </summary>
    public static Bitmap DecodeWebpToBitmap(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stream sourceStream = stream;
        MemoryStream? seekableCopy = null;
        if (!stream.CanSeek)
        {
            seekableCopy = new MemoryStream();
            stream.CopyTo(seekableCopy);
            seekableCopy.Position = 0;
            sourceStream = seekableCopy;
        }

        try
        {
            using var skStream = new SKManagedStream(sourceStream, false);
            using var codec = SKCodec.Create(skStream)
                ?? throw new InvalidDataException("無法透過 SkiaSharp SKCodec 解碼圖片串流。");

            var srcInfo = codec.Info;
            if (srcInfo.Width <= 0 || srcInfo.Height <= 0)
                throw new InvalidDataException($"圖片尺寸無效：{srcInfo.Width}x{srcInfo.Height}。");

            var targetInfo = new SKImageInfo(
                srcInfo.Width,
                srcInfo.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Unpremul);

            var bmp = new Bitmap(targetInfo.Width, targetInfo.Height, PixelFormat.Format32bppArgb);
            try
            {
                var rect = new Rectangle(0, 0, targetInfo.Width, targetInfo.Height);
                var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    if (bmpData.Stride == targetInfo.RowBytes)
                    {
                        var result = codec.GetPixels(targetInfo, bmpData.Scan0);
                        if (result != SKCodecResult.Success)
                            throw new InvalidDataException($"SkiaSharp WebP 解碼失敗：{result}。");
                    }
                    else
                    {
                        using var skBitmap = new SKBitmap(targetInfo);
                        var result = codec.GetPixels(targetInfo, skBitmap.GetPixels());
                        if (result != SKCodecResult.Success)
                            throw new InvalidDataException($"SkiaSharp WebP 解碼失敗：{result}。");

                        int copyBytes = Math.Min(skBitmap.RowBytes, Math.Abs(bmpData.Stride));
                        unsafe
                        {
                            byte* srcBase = (byte*)skBitmap.GetPixels().ToPointer();
                            byte* dstBase = (byte*)bmpData.Scan0.ToPointer();
                            for (int y = 0; y < targetInfo.Height; y++)
                            {
                                Buffer.MemoryCopy(
                                    srcBase + (long)y * skBitmap.RowBytes,
                                    dstBase + (long)y * bmpData.Stride,
                                    Math.Abs(bmpData.Stride),
                                    copyBytes);
                            }
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }
        finally
        {
            seekableCopy?.Dispose();
        }
    }

    /// <summary>
    /// 解碼並等比例縮放成輕量縮圖 <see cref="Bitmap"/>（<see cref="PixelFormat.Format32bppArgb"/>）。
    /// 優先透過 <see cref="SKCodec"/> 搭配 <c>GetScaledDimensions</c> 降低大圖解碼記憶體占用；
    /// 若 SKCodec 無法處理非 WebP 格式則退回 GDI+ 縮放。
    /// </summary>
    public static Bitmap DecodeThumbnail(Stream stream, string extension, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int targetMaxW = Math.Max(1, maxWidth);
        int targetMaxH = Math.Max(1, maxHeight);

        Stream sourceStream = stream;
        MemoryStream? seekableCopy = null;
        if (!stream.CanSeek)
        {
            seekableCopy = new MemoryStream();
            stream.CopyTo(seekableCopy);
            seekableCopy.Position = 0;
            sourceStream = seekableCopy;
        }

        try
        {
            long startPos = sourceStream.Position;
            using (var skStream = new SKManagedStream(sourceStream, false))
            using (var codec = SKCodec.Create(skStream))
            {
                if (codec != null && codec.Info.Width > 0 && codec.Info.Height > 0)
                {
                    int srcW = codec.Info.Width;
                    int srcH = codec.Info.Height;
                    float scale = Math.Min(1f, Math.Min((float)targetMaxW / srcW, (float)targetMaxH / srcH));
                    int outW = Math.Clamp((int)Math.Round(srcW * scale), 1, targetMaxW);
                    int outH = Math.Clamp((int)Math.Round(srcH * scale), 1, targetMaxH);

                    var scaledSize = codec.GetScaledDimensions(scale);
                    int decodeW = Math.Max(1, scaledSize.Width);
                    int decodeH = Math.Max(1, scaledSize.Height);
                    var decodeInfo = new SKImageInfo(decodeW, decodeH, SKColorType.Bgra8888, SKAlphaType.Unpremul);

                    using var decoded = SKBitmap.Decode(codec, decodeInfo);
                    if (decoded != null)
                    {
                        if (decoded.Width == outW && decoded.Height == outH)
                            return CopySkBitmapToGdiBitmap(decoded);

                        var resizeInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                        using var resized = decoded.Resize(resizeInfo, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                        if (resized != null)
                            return CopySkBitmapToGdiBitmap(resized);
                    }
                }
            }

            if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SkiaSharp 無法解碼 WebP 縮圖。");

            sourceStream.Position = startPos;
            using var srcImg = Image.FromStream(sourceStream);
            float gdiScale = Math.Min(1f, Math.Min((float)targetMaxW / srcImg.Width, (float)targetMaxH / srcImg.Height));
            int gdiW = Math.Clamp((int)Math.Round(srcImg.Width * gdiScale), 1, targetMaxW);
            int gdiH = Math.Clamp((int)Math.Round(srcImg.Height * gdiScale), 1, targetMaxH);

            var thumb = new Bitmap(gdiW, gdiH, PixelFormat.Format32bppArgb);
            try
            {
                using var g = Graphics.FromImage(thumb);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(srcImg, 0, 0, gdiW, gdiH);
                return thumb;
            }
            catch
            {
                thumb.Dispose();
                throw;
            }
        }
        finally
        {
            seekableCopy?.Dispose();
        }
    }

    private static Bitmap CopySkBitmapToGdiBitmap(SKBitmap skBitmap)
    {
        int w = skBitmap.Width;
        int h = skBitmap.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        try
        {
            var rect = new Rectangle(0, 0, w, h);
            var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int copyBytes = Math.Min(skBitmap.RowBytes, Math.Abs(bmpData.Stride));
                unsafe
                {
                    byte* srcBase = (byte*)skBitmap.GetPixels().ToPointer();
                    byte* dstBase = (byte*)bmpData.Scan0.ToPointer();
                    for (int y = 0; y < h; y++)
                    {
                        Buffer.MemoryCopy(
                            srcBase + (long)y * skBitmap.RowBytes,
                            dstBase + (long)y * bmpData.Stride,
                            Math.Abs(bmpData.Stride),
                            copyBytes);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
            return bmp;
        }
        catch
        {
            bmp.Dispose();
            throw;
        }
    }
}

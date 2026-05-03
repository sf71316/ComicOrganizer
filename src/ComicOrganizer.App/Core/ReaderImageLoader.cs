using System.Drawing.Imaging;
using SixLabors.ImageSharp.PixelFormats;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Reader 內 page decode helper：webp 走 ImageSharp（GDI+ 不支援），其他走 GDI+ Image.FromStream。
/// webp → Bitmap 走 unsafe direct pixel copy（zero-allocation），同 CoverManager。
/// 詳見 docs/webp-optimization-report.md。
/// </summary>
public static class ReaderImageLoader
{
    public static Image Decode(Stream stream, string extension)
    {
        var ext = (extension ?? "").ToLowerInvariant();
        if (ext == ".webp")
        {
            using var imgSharp = SixLabors.ImageSharp.Image.Load<Bgra32>(stream);
            return ConvertToBitmap(imgSharp);
        }
        return Image.FromStream(stream);
    }

    /// <summary>
    /// ImageSharp Bgra32 → GDI+ Bitmap (Format32bppArgb)。
    /// 走 LockBits + unsafe Span<Bgra32> 直接逐列 CopyTo，跳過 byte[] 中轉。
    /// 4K 圖每張省 ~30MB heap alloc，reader 翻 200 頁 webp 漫畫 = 省 6GB GC 壓力。
    /// </summary>
    private static Bitmap ConvertToBitmap(SixLabors.ImageSharp.Image<Bgra32> imgSharp)
    {
        int w = imgSharp.Width, h = imgSharp.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                imgSharp.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < h; y++)
                    {
                        var sourceRow = accessor.GetRowSpan(y);
                        var targetRowPtr = (Bgra32*)(data.Scan0 + y * data.Stride);
                        var targetRowSpan = new Span<Bgra32>(targetRowPtr, w);
                        sourceRow.CopyTo(targetRowSpan);
                    }
                });
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}

using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.PixelFormats;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Reader 內 page decode helper：webp 走 ImageSharp（GDI+ 不支援），其他走 GDI+ Image.FromStream。
/// 與 CoverManager 內邏輯相同，獨立出來避免循環相依。
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

    private static Bitmap ConvertToBitmap(SixLabors.ImageSharp.Image<Bgra32> imgSharp)
    {
        int w = imgSharp.Width, h = imgSharp.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = w * 4;
            byte[] pixels = new byte[rowBytes * h];
            imgSharp.CopyPixelDataTo(pixels);
            if (data.Stride == rowBytes)
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }
}

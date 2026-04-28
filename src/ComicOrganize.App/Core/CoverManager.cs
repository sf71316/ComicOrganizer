using ComicOrganize.App.Models;
using System.Drawing;
using System.IO;
using System.IO.Compression;

namespace ComicOrganize.App.Core;

public class CoverManager
{
    private readonly AppConfig _config;
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };

    public CoverManager(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 非同步取得指定路徑的封面圖片
    /// </summary>
    public async Task<Image?> GetCoverAsync(string path, int maxWidth = 200)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(path))
                {
                    return GetFolderCover(path, maxWidth);
                }
                else if (File.Exists(path))
                {
                    string ext = Path.GetExtension(path).ToLower();
                    if (ext == ".zip" || ext == ".cbz")
                    {
                        return GetZipCover(path, maxWidth);
                    }
                    else if (ImageExtensions.Contains(ext))
                    {
                        return LoadAndResizeImage(path, maxWidth);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取封面失敗 ({path}): {ex.Message}");
            }
            return null;
        });
    }

    private Image? GetFolderCover(string folderPath, int maxWidth)
    {
        var files = Directory.GetFiles(folderPath);
        var coverFile = files.FirstOrDefault(f => 
            Path.GetFileNameWithoutExtension(f).Equals("cover", StringComparison.OrdinalIgnoreCase) &&
            ImageExtensions.Contains(Path.GetExtension(f).ToLower()));

        if (coverFile != null) return LoadAndResizeImage(coverFile, maxWidth);

        var firstImage = files.OrderBy(f => f).FirstOrDefault(f => 
            ImageExtensions.Contains(Path.GetExtension(f).ToLower()));

        if (firstImage != null) return LoadAndResizeImage(firstImage, maxWidth);
        return null;
    }

    private Image? GetZipCover(string zipPath, int maxWidth)
    {
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries
                    .Where(e => ImageExtensions.Contains(Path.GetExtension(e.FullName).ToLower()))
                    .OrderBy(e => e.FullName)
                    .FirstOrDefault();

                if (entry != null)
                {
                    using (var stream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        using (var img = Image.FromStream(ms))
                        {
                            return ResizeImage(img, maxWidth);
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private Image? LoadAndResizeImage(string filePath, int maxWidth)
    {
        using (var img = Image.FromFile(filePath))
        {
            return ResizeImage(img, maxWidth);
        }
    }

    private Image ResizeImage(Image img, int maxWidth)
    {
        if (img.Width <= maxWidth) return new Bitmap(img);
        double ratio = (double)maxWidth / img.Width;
        int newHeight = (int)(img.Height * ratio);
        var res = new Bitmap(maxWidth, newHeight);
        using (var g = Graphics.FromImage(res))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, 0, 0, maxWidth, newHeight);
        }
        return res;
    }
}

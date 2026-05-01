using ComicOrganizer.App.Models;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp.PixelFormats;

namespace ComicOrganizer.App.Core;

public class CoverManager
{
    private readonly AppConfig _config;
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };

    // Cache 服務（optional — 沒接 cache 時 GetCoverAsync 走純 decode 路徑，與舊版相容）
    private IAssetMetaStore? _metaStore;
    private IThumbStore? _thumbStore;
    private CacheWriter? _cacheWriter;

    /// <summary>thumb 縮圖固定寬度，跟 GalleryControl Large card 寬一致。</summary>
    public const int ThumbWidth = 330;
    private const long JpegQuality = 80L;

    // 開啟後 GetCoverAsync 會在 Debug Output 印分階段時序，方便在不同儲存介質
    // (HDD vs SSD) 上觀察 zip vs subdir 解析路徑的成本。
    // 訊息以 "[Cover]" 開頭，VS Output 或 DebugView 可篩選。
    private const bool DebugTiming = true;

    private static void Log(string msg)
    {
        if (DebugTiming) Debug.WriteLine("[Cover] " + msg);
    }

    private static string Short(string path) => Path.GetFileName(path);

    public CoverManager(AppConfig config)
    {
        _config = config;
    }

    /// <summary>注入 cache 服務（MainForm 啟動時呼叫）。沒注入時 GetCoverAsync 走純 decode 路徑。</summary>
    public void AttachCache(IAssetMetaStore metaStore, IThumbStore thumbStore, CacheWriter cacheWriter)
    {
        _metaStore = metaStore;
        _thumbStore = thumbStore;
        _cacheWriter = cacheWriter;
    }

    /// <summary>SHA1(path)[0:40] 作為 thumb 檔名 hash。</summary>
    private static string ComputeThumbKey(string path)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(path));
        var sb = new StringBuilder(40);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();   // 40 字元 hex
    }

    private static long GetMtimeUnix(string path)
    {
        try
        {
            DateTime t = Directory.Exists(path)
                ? Directory.GetLastWriteTimeUtc(path)
                : File.GetLastWriteTimeUtc(path);
            return new DateTimeOffset(t, TimeSpan.Zero).ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static byte[] EncodeJpeg(Image img, long quality)
    {
        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var ms = new MemoryStream();
        var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        img.Save(ms, encoder, p);
        return ms.ToArray();
    }

    /// <summary>
    /// 非同步取得指定路徑的封面圖片。
    /// 有接 cache (AttachCache 呼叫過) 時優先讀 cache hit；miss 才走 decode 並背景寫 cache。
    /// </summary>
    public async Task<Image?> GetCoverAsync(string path, int maxWidth = 200)
    {
        // ===== Cache hit fast path =====
        if (_metaStore != null && _thumbStore != null && _cacheWriter != null)
        {
            var swCache = Stopwatch.StartNew();
            var meta = _metaStore.Get(path);
            if (meta?.CoverThumbKey != null && meta.Mtime == GetMtimeUnix(path))
            {
                using var stream = _thumbStore.OpenRead(meta.CoverThumbKey);
                if (stream != null)
                {
                    try
                    {
                        var img = Image.FromStream(stream);
                        _cacheWriter.EnqueueTouch(path);   // 非同步更新 last_accessed
                        swCache.Stop();
                        Log($"CACHE-HIT    '{Short(path)}' → {img.Width}x{img.Height,-12} total={swCache.ElapsedMilliseconds}ms");
                        return img;
                    }
                    catch
                    {
                        // thumb 檔損壞 → fall through 走 decode + 重寫
                    }
                }
            }
        }

        // ===== Cache miss → 既有 decode 路徑 =====
        return await Task.Run(() =>
        {
            var swTotal = Stopwatch.StartNew();
            string kind = "?";
            Image? result = null;
            try
            {
                if (Directory.Exists(path))
                {
                    kind = "FOLDER";
                    result = GetFolderCover(path, maxWidth);
                }
                else if (File.Exists(path))
                {
                    string ext = Path.GetExtension(path).ToLower();
                    if (ext == ".zip" || ext == ".cbz")
                    {
                        kind = "ZIP-direct";
                        result = GetZipCover(path, maxWidth);
                    }
                    else if (ImageExtensions.Contains(ext))
                    {
                        kind = "IMG-direct";
                        result = LoadAndResizeImage(path, maxWidth);
                    }
                    else
                    {
                        kind = "UNSUPPORTED(" + ext + ")";
                    }
                }
                else
                {
                    kind = "MISSING";
                }
            }
            catch (Exception ex)
            {
                kind += "-EX";
                Log($"FAIL {kind} '{Short(path)}': {ex.Message}");
                return null;
            }
            swTotal.Stop();
            Log($"{kind,-12} '{Short(path)}' → {(result != null ? $"{result.Width}x{result.Height}" : "null"),-12} total={swTotal.ElapsedMilliseconds}ms");

            // 寫 cache (背景 queue) — 只在有接 cache 且 decode 成功時才寫
            if (result != null && _cacheWriter != null)
            {
                EnqueueCacheWrite(path, result);
            }

            return result;
        });
    }

    /// <summary>把已 decode 的 image 縮成 thumb 尺寸 → JPEG → 入 CacheWriter queue。</summary>
    private void EnqueueCacheWrite(string path, Image decoded)
    {
        try
        {
            // 縮成 ThumbWidth (330) — 已小於就不縮
            Image thumb = decoded.Width > ThumbWidth
                ? ResizeImage(decoded, ThumbWidth)
                : new Bitmap(decoded);
            try
            {
                var jpeg = EncodeJpeg(thumb, JpegQuality);
                var thumbKey = ComputeThumbKey(path);
                bool isFolder = Directory.Exists(path);
                long? fileSize = null;
                try { if (!isFolder) fileSize = new FileInfo(path).Length; } catch { }

                var meta = new AssetMeta
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    IsFolder = isFolder,
                    FileSize = fileSize,
                    Mtime = GetMtimeUnix(path),
                    CoverThumbKey = thumbKey,
                    CoverWidth = thumb.Width,
                    CoverHeight = thumb.Height,
                    LastScanned = NowUnix(),
                    LastAccessed = NowUnix(),
                };
                _cacheWriter!.Enqueue(new CacheWriteOp
                {
                    Meta = meta,
                    ThumbKey = thumbKey,
                    JpegBytes = jpeg,
                });
            }
            finally
            {
                if (!ReferenceEquals(thumb, decoded)) thumb.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log($"  cache write enqueue failed: {ex.Message}");
        }
    }

    private Image? GetFolderCover(string folderPath, int maxWidth)
    {
        return GetFolderCoverRecursive(folderPath, maxWidth, _config.MaxScanDepth);
    }

    /// <summary>
    /// 遞迴搜尋封面：當前目錄 cover.* → 第一張圖檔 → 第一個 zip/cbz → 字典序首個子目錄遞迴。
    /// remainingDepth 控制最大遞迴層數，避免無限/過深掃描。
    /// </summary>
    private Image? GetFolderCoverRecursive(string folderPath, int maxWidth, int remainingDepth)
    {
        if (remainingDepth <= 0)
        {
            Log($"  depth-exhausted at '{Short(folderPath)}'");
            return null;
        }

        try
        {
            var swDir = Stopwatch.StartNew();
            var files = Directory.GetFiles(folderPath);
            swDir.Stop();
            Log($"  scan '{Short(folderPath)}' ({files.Length} files) dir-list={swDir.ElapsedMilliseconds}ms");

            // 1. cover.* 命名圖檔
            var coverFile = files.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals("cover", StringComparison.OrdinalIgnoreCase) &&
                ImageExtensions.Contains(Path.GetExtension(f).ToLower()));
            if (coverFile != null)
            {
                Log($"  rule=COVER_FILE picked '{Short(coverFile)}'");
                return LoadAndResizeImage(coverFile, maxWidth);
            }

            // 2. 字典序第一張圖檔（先過濾極端大小：< Min 視為縮圖/banner，> Max 視為拚版/插畫）
            //    若全部都被過濾掉，fallback 到「不過濾」的字典序第一張，避免使用者看到完全空白的卡片
            var sortedImages = files
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var firstImage = sortedImages.FirstOrDefault(f =>
            {
                long size = new FileInfo(f).Length;
                return size >= _config.MinCoverFileSizeBytes && size <= _config.MaxCoverFileSizeBytes;
            });
            if (firstImage != null)
            {
                Log($"  rule=FIRST_IMAGE picked '{Short(firstImage)}' ({new FileInfo(firstImage).Length / 1024}KB)");
                return LoadAndResizeImage(firstImage, maxWidth);
            }
            // fallback：候選都被過濾掉，退而求其次取字典序第一張（無視大小）
            if (sortedImages.Count > 0)
            {
                var fb = sortedImages[0];
                Log($"  rule=FIRST_IMAGE_FALLBACK picked '{Short(fb)}' ({new FileInfo(fb).Length / 1024}KB) — 候選都被大小過濾掉");
                return LoadAndResizeImage(fb, maxWidth);
            }

            // 3. 字典序第一個 zip/cbz (zip 不過濾大小 — entry 內部過濾)
            var firstArchive = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(f =>
                {
                    string ext = Path.GetExtension(f).ToLower();
                    return ext == ".zip" || ext == ".cbz";
                });
            if (firstArchive != null)
            {
                Log($"  rule=FIRST_ZIP picked '{Short(firstArchive)}'");
                var img = GetZipCover(firstArchive, maxWidth);
                if (img != null) return img;
            }

            // 4. 沒任何可用素材 → 遞迴字典序第一個有東西的子目錄
            var subDirs = Directory.GetDirectories(folderPath)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            foreach (var subDir in subDirs)
            {
                Log($"  rule=RECURSE_SUBDIR '{Short(subDir)}' (depth left={remainingDepth - 1})");
                var img = GetFolderCoverRecursive(subDir, maxWidth, remainingDepth - 1);
                if (img != null) return img;
            }
        }
        catch (Exception ex)
        {
            Log($"  EX in folder '{Short(folderPath)}': {ex.Message}");
        }

        return null;
    }

    private Image? GetZipCover(string zipPath, int maxWidth)
    {
        try
        {
            var swOpen = Stopwatch.StartNew();
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                swOpen.Stop();
                // entry 套大小過濾，但若候選全被過濾則 fallback 不過濾
                var sortedImages = archive.Entries
                    .Where(e => ImageExtensions.Contains(Path.GetExtension(e.FullName).ToLower()))
                    .OrderBy(e => e.FullName)
                    .ToList();
                var entry = sortedImages
                    .FirstOrDefault(e => e.Length >= _config.MinCoverFileSizeBytes && e.Length <= _config.MaxCoverFileSizeBytes);
                if (entry == null && sortedImages.Count > 0)
                {
                    entry = sortedImages[0];
                    Log($"  zip='{Short(zipPath)}' fallback (候選都被大小過濾掉) → '{entry.FullName}'");
                }

                if (entry != null)
                {
                    string ext = Path.GetExtension(entry.FullName).ToLower();
                    string method = entry.CompressedLength == entry.Length ? "stored" : "deflate";

                    var swExtract = Stopwatch.StartNew();
                    using (var stream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        swExtract.Stop();
                        ms.Position = 0;

                        var swDecode = Stopwatch.StartNew();
                        using (var img = LoadImageFromStream(ms, ext))
                        {
                            swDecode.Stop();
                            var swResize = Stopwatch.StartNew();
                            var resized = ResizeImage(img, maxWidth);
                            swResize.Stop();

                            Log($"  zip='{Short(zipPath)}' entry='{entry.FullName}' size={entry.Length / 1024}KB method={method}" +
                                $" open={swOpen.ElapsedMilliseconds}ms extract={swExtract.ElapsedMilliseconds}ms" +
                                $" decode={swDecode.ElapsedMilliseconds}ms resize={swResize.ElapsedMilliseconds}ms");
                            return resized;
                        }
                    }
                }
                Log($"  zip='{Short(zipPath)}' has no image entries");
            }
        }
        catch (Exception ex)
        {
            Log($"  zip EX '{Short(zipPath)}': {ex.Message}");
        }
        return null;
    }

    private Image? LoadAndResizeImage(string filePath, int maxWidth)
    {
        string ext = Path.GetExtension(filePath).ToLower();
        var swDecode = Stopwatch.StartNew();
        if (ext == ".webp")
        {
            // GDI+ 不支援 WebP decoder，走 ImageSharp 載入後轉 GDI+ Bitmap
            using (var img = LoadWebpAsBitmap(filePath))
            {
                swDecode.Stop();
                var swResize = Stopwatch.StartNew();
                var res = ResizeImage(img, maxWidth);
                swResize.Stop();
                Log($"  load ext={ext} src={img.Width}x{img.Height} decode={swDecode.ElapsedMilliseconds}ms resize={swResize.ElapsedMilliseconds}ms");
                return res;
            }
        }

        using (var img = Image.FromFile(filePath))
        {
            swDecode.Stop();
            var swResize = Stopwatch.StartNew();
            var res = ResizeImage(img, maxWidth);
            swResize.Stop();
            Log($"  load ext={ext} src={img.Width}x{img.Height} decode={swDecode.ElapsedMilliseconds}ms resize={swResize.ElapsedMilliseconds}ms");
            return res;
        }
    }

    /// <summary>
    /// 從 stream 載入圖片：webp 走 ImageSharp，其他走 GDI+ Image.FromStream。
    /// 用在 zip/cbz 內 entry 解壓後 stream → Image。
    /// </summary>
    private static Image LoadImageFromStream(Stream stream, string extension)
    {
        if (extension == ".webp")
        {
            using var imgSharp = SixLabors.ImageSharp.Image.Load<Bgra32>(stream);
            return ConvertImageSharpToBitmap(imgSharp);
        }
        return Image.FromStream(stream);
    }

    /// <summary>
    /// 用 ImageSharp 載 webp 檔轉成 GDI+ Bitmap。
    /// </summary>
    private static Bitmap LoadWebpAsBitmap(string path)
    {
        using var imgSharp = SixLabors.ImageSharp.Image.Load<Bgra32>(path);
        return ConvertImageSharpToBitmap(imgSharp);
    }

    /// <summary>
    /// ImageSharp Bgra32 直接 pixel-copy 到 GDI+ Bitmap (Format32bppArgb)。
    /// Bgra32 與 Format32bppArgb 記憶體 layout 一致，不需 R/B swap。
    /// 比 PNG encode/decode round-trip 省 ~50~100ms (大圖)。
    /// </summary>
    private static Bitmap ConvertImageSharpToBitmap(SixLabors.ImageSharp.Image<Bgra32> imgSharp)
    {
        int width = imgSharp.Width;
        int height = imgSharp.Height;
        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = width * 4;
            byte[] pixels = new byte[rowBytes * height];
            imgSharp.CopyPixelDataTo(pixels);

            if (bmpData.Stride == rowBytes)
            {
                Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            }
            else
            {
                // stride padding 對齊：逐 row 拷貝
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * rowBytes,
                        IntPtr.Add(bmpData.Scan0, y * bmpData.Stride), rowBytes);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bmpData);
        }
        return bmp;
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

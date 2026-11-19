using Avalonia.Media.Imaging;
using ComicOrganizer.App.Core;
using PDFtoImage;
using SharpCompress.Archives;
using SkiaSharp;

namespace ComicOrganizer.Avalonia.Services;

/// <summary>
/// 純 SkiaSharp 封面縮圖載入與提取服務（跨平台純 Skia 實作，無 GDI+ 依賴）。
/// 負責：
/// 1. 從 <see cref="IThumbStore"/> 讀取既有縮圖快取，搭配執行緒安全 LRU 記憶體快取解碼為 Avalonia <see cref="Bitmap"/>。
/// 2. 針對單一圖片、資料夾（cover.* 優先 → 自然排序 → 子壓縮檔/PDF → 最多遞迴 2 層）、壓縮檔（SharpCompress）與 PDF（PDFtoImage）提取並縮放為 JPEG 位元組。
/// 3. <see cref="LoadOrExtractCoverAsync"/> 預設唯讀，不主動寫回 <see cref="IThumbStore"/>。
/// </summary>
public sealed class SkiaCoverService : IDisposable
{
    public const int DefaultThumbWidth = 330;
    public const int DefaultJpegQuality = 85;

    private const int DefaultMaxFolderDepth = 2;
    private const int MaxCandidateAttemptsPerLevel = 5;
    private const int PdfCoverRenderDpi = 150;

    // PDFium 原生函式庫非執行緒安全，序列化所有 PDF 頁面渲染呼叫
    private static readonly object PdfLock = new();

    private readonly IThumbStore _thumbStore;
    private readonly int _maxMemoryCacheItems;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheMap = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lruList = new();
    private volatile bool _disposed;

    public SkiaCoverService(IThumbStore thumbStore, int maxMemoryCacheItems = 512)
    {
        ArgumentNullException.ThrowIfNull(thumbStore);
        if (maxMemoryCacheItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemoryCacheItems), "快取上限不可為負數。");
        }

        _thumbStore = thumbStore;
        _maxMemoryCacheItems = maxMemoryCacheItems;
    }

    /// <summary>目前記憶體 LRU 快取中的項目數量。</summary>
    public int MemoryCacheCount
    {
        get
        {
            lock (_cacheLock)
            {
                return _cacheMap.Count;
            }
        }
    }

    /// <summary>
    /// 從 <see cref="IThumbStore"/> 讀取指定 thumbKey 的原始 JPEG 位元組。
    /// 若 key 無效、檔案不存在、為空或發生 IO 錯誤，安全回傳 <c>null</c>。
    /// </summary>
    public Task<byte[]?> ReadCachedThumbBytesAsync(string? thumbKey, CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested || !IsValidThumbKey(thumbKey))
        {
            return Task.FromResult<byte[]?>(null);
        }

        string key = thumbKey!;
        return Task.Run(() => ReadCachedThumbBytesCore(key, ct));
    }

    /// <summary>
    /// 從 <see cref="IThumbStore"/> 讀取快取縮圖並解碼為 Avalonia <see cref="Bitmap"/>，
    /// 命中執行緒安全 LRU 記憶體快取時直接回傳既有實例避免重複 IO 與解碼。
    /// 若檔案不存在或損壞，安全回傳 <c>null</c>。
    /// </summary>
    public Task<Bitmap?> GetCachedThumbAsync(string? thumbKey, CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested || !IsValidThumbKey(thumbKey))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        string key = thumbKey!;
        string cacheKey = BuildThumbCacheKey(key);

        if (TryGetFromMemoryCache(cacheKey, out var cached))
        {
            return Task.FromResult<Bitmap?>(cached);
        }

        return Task.Run(() =>
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            if (TryGetFromMemoryCache(cacheKey, out var cachedInWorker))
            {
                return cachedInWorker;
            }

            var bytes = ReadCachedThumbBytesCore(key, ct);
            if (bytes == null || bytes.Length == 0 || _disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            var decoded = DecodeAvaloniaBitmap(bytes);
            if (decoded == null)
            {
                return null;
            }

            return AddOrGetFromMemoryCache(cacheKey, decoded);
        });
    }

    /// <summary>
    /// 從來源路徑（單圖、資料夾、壓縮檔或 PDF）以純 SkiaSharp 提取封面並等比縮放編碼為 JPEG 位元組。
    /// 不放大寬度小於 <paramref name="targetWidth"/> 的圖片；任何損壞、無圖或 IO 錯誤安全回傳 <c>null</c>。
    /// </summary>
    public Task<byte[]?> ExtractCoverJpegBytesAsync(
        string sourcePath,
        int targetWidth = DefaultThumbWidth,
        int jpegQuality = DefaultJpegQuality,
        CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested || string.IsNullOrWhiteSpace(sourcePath))
        {
            return Task.FromResult<byte[]?>(null);
        }

        int normalizedWidth = Math.Max(1, targetWidth);
        int normalizedQuality = Math.Clamp(jpegQuality, 1, 100);

        return Task.Run(() => ExtractCoverJpegBytesCore(sourcePath, normalizedWidth, normalizedQuality, ct));
    }

    /// <summary>
    /// 優先從 <see cref="IThumbStore"/> 與記憶體快取載入封面；若未命中且來源存在，
    /// 則現場提取封面並解碼為 <see cref="Bitmap"/>（預設不主動寫回 <see cref="IThumbStore"/>）。
    /// </summary>
    public async Task<Bitmap?> LoadOrExtractCoverAsync(
        string sourcePath,
        string? thumbKey,
        CancellationToken ct = default)
    {
        if (_disposed || ct.IsCancellationRequested)
        {
            return null;
        }

        if (IsValidThumbKey(thumbKey))
        {
            var fromThumb = await GetCachedThumbAsync(thumbKey, ct).ConfigureAwait(false);
            if (fromThumb != null)
            {
                return fromThumb;
            }
        }

        if (_disposed || ct.IsCancellationRequested || string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        string fallbackCacheKey = IsValidThumbKey(thumbKey)
            ? BuildThumbCacheKey(thumbKey!)
            : BuildSourceCacheKey(sourcePath);

        if (TryGetFromMemoryCache(fallbackCacheKey, out var cachedSource))
        {
            return cachedSource;
        }

        var jpegBytes = await ExtractCoverJpegBytesAsync(
            sourcePath,
            DefaultThumbWidth,
            DefaultJpegQuality,
            ct).ConfigureAwait(false);

        if (jpegBytes == null || jpegBytes.Length == 0 || _disposed || ct.IsCancellationRequested)
        {
            return null;
        }

        return await Task.Run(() =>
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            var decoded = DecodeAvaloniaBitmap(jpegBytes);
            if (decoded == null)
            {
                return null;
            }

            return AddOrGetFromMemoryCache(fallbackCacheKey, decoded);
        }).ConfigureAwait(false);
    }

    /// <summary>清空記憶體 LRU 快取強參考（不強制 Dispose，避免畫面上仍在顯示中的 Bitmap 失效拋例外）。</summary>
    public void ClearMemoryCache()
    {
        lock (_cacheLock)
        {
            _cacheMap.Clear();
            _lruList.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        List<Bitmap> toDispose;
        lock (_cacheLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            toDispose = new List<Bitmap>(_lruList.Count);
            for (var node = _lruList.First; node != null; node = node.Next)
            {
                toDispose.Add(node.Value.Bitmap);
            }

            _cacheMap.Clear();
            _lruList.Clear();
        }

        DisposeBitmapsSafely(toDispose);
    }

    private byte[]? ReadCachedThumbBytesCore(string thumbKey, CancellationToken ct)
    {
        try
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            using var stream = _thumbStore.OpenRead(thumbKey);
            if (stream == null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            if (ct.IsCancellationRequested || ms.Length == 0)
            {
                return null;
            }

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ExtractCoverJpegBytesCore(string sourcePath, int targetWidth, int jpegQuality, CancellationToken ct)
    {
        try
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            if (Directory.Exists(sourcePath))
            {
                return ExtractFolderCover(sourcePath, targetWidth, jpegQuality, DefaultMaxFolderDepth, ct);
            }

            if (!File.Exists(sourcePath))
            {
                return null;
            }

            if (SupportedExtensions.IsImage(sourcePath))
            {
                return ExtractImageFileCover(sourcePath, targetWidth, jpegQuality);
            }

            if (SupportedExtensions.IsArchive(sourcePath))
            {
                return ExtractArchiveCover(sourcePath, targetWidth, jpegQuality, ct);
            }

            if (SupportedExtensions.IsPdf(sourcePath))
            {
                return ExtractPdfCover(sourcePath, targetWidth, jpegQuality, ct);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ExtractFolderCover(
        string folderPath,
        int targetWidth,
        int jpegQuality,
        int remainingDepth,
        CancellationToken ct)
    {
        if (_disposed || ct.IsCancellationRequested)
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folderPath);
        }
        catch
        {
            return null;
        }

        // 1. 優先選擇 cover.*（支援圖檔副檔名）
        var coverCandidates = files
            .Where(f =>
                SupportedExtensions.IsImage(f)
                && string.Equals(Path.GetFileNameWithoutExtension(f), "cover", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f) ?? string.Empty, NaturalStringComparer.Instance)
            .ToList();

        foreach (var coverFile in coverCandidates)
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            var bytes = ExtractImageFileCover(coverFile, targetWidth, jpegQuality);
            if (bytes != null)
            {
                return bytes;
            }
        }

        // 2. 依自然排序挑第一張支援圖檔
        var imageCandidates = files
            .Where(f =>
                SupportedExtensions.IsImage(f)
                && !string.Equals(Path.GetFileNameWithoutExtension(f), "cover", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f) ?? string.Empty, NaturalStringComparer.Instance)
            .Take(MaxCandidateAttemptsPerLevel);

        foreach (var imageFile in imageCandidates)
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            var bytes = ExtractImageFileCover(imageFile, targetWidth, jpegQuality);
            if (bytes != null)
            {
                return bytes;
            }
        }

        // 3. 子壓縮檔 / PDF fallback
        var containerCandidates = files
            .Where(f => SupportedExtensions.IsArchive(f) || SupportedExtensions.IsPdf(f))
            .OrderBy(f => Path.GetFileName(f) ?? string.Empty, NaturalStringComparer.Instance)
            .Take(MaxCandidateAttemptsPerLevel);

        foreach (var containerFile in containerCandidates)
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            byte[]? bytes = SupportedExtensions.IsPdf(containerFile)
                ? ExtractPdfCover(containerFile, targetWidth, jpegQuality, ct)
                : ExtractArchiveCover(containerFile, targetWidth, jpegQuality, ct);

            if (bytes != null)
            {
                return bytes;
            }
        }

        // 4. 遞迴子目錄（最多 DefaultMaxFolderDepth 層）
        if (remainingDepth <= 0)
        {
            return null;
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(folderPath);
        }
        catch
        {
            return null;
        }

        var orderedSubDirs = subDirs
            .Where(d => !IsIgnoredDirectoryName(Path.GetFileName(d)))
            .OrderBy(d => Path.GetFileName(d) ?? string.Empty, NaturalStringComparer.Instance);

        foreach (var subDir in orderedSubDirs)
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            var bytes = ExtractFolderCover(subDir, targetWidth, jpegQuality, remainingDepth - 1, ct);
            if (bytes != null)
            {
                return bytes;
            }
        }

        return null;
    }

    private byte[]? ExtractImageFileCover(string filePath, int targetWidth, int jpegQuality)
    {
        try
        {
            // 使用 FileStream 讀取進記憶體，避免 Windows 下 mmap 鎖住來源檔案
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return DecodeAndEncodeImageStream(fs, targetWidth, jpegQuality);
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ExtractArchiveCover(string archivePath, int targetWidth, int jpegQuality, CancellationToken ct)
    {
        try
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var imageEntries = archive.Entries
                .Where(e =>
                    !e.IsDirectory
                    && !string.IsNullOrEmpty(e.Key)
                    && !IsIgnoredArchiveEntry(e.Key)
                    && SupportedExtensions.IsImage(e.Key))
                .OrderBy(e => e.Key!, NaturalStringComparer.Instance)
                .Take(MaxCandidateAttemptsPerLevel)
                .ToList();

            foreach (var entry in imageEntries)
            {
                if (_disposed || ct.IsCancellationRequested)
                {
                    return null;
                }

                try
                {
                    using var entryStream = entry.OpenEntryStream();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    ms.Position = 0;

                    var jpeg = DecodeAndEncodeImageStream(ms, targetWidth, jpegQuality);
                    if (jpeg != null)
                    {
                        return jpeg;
                    }
                }
                catch
                {
                    // 單一損壞條目忽略，嘗試下一張候選圖
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ExtractPdfCover(string pdfPath, int targetWidth, int jpegQuality, CancellationToken ct)
    {
        try
        {
            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            byte[] pdfBytes = File.ReadAllBytes(pdfPath);
            if (pdfBytes.Length == 0 || _disposed || ct.IsCancellationRequested)
            {
                return null;
            }

            SKBitmap? rendered = null;
            lock (PdfLock)
            {
                if (_disposed || ct.IsCancellationRequested)
                {
                    return null;
                }

                int pageCount = Conversion.GetPageCount(pdfBytes);
                if (pageCount <= 0)
                {
                    return null;
                }

                rendered = Conversion.ToImage(pdfBytes, page: 0, options: new RenderOptions(Dpi: PdfCoverRenderDpi));
            }

            if (rendered == null)
            {
                return null;
            }

            using (rendered)
            {
                return EncodeBitmapToJpegBytes(rendered, targetWidth, jpegQuality);
            }
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? DecodeAndEncodeImageStream(Stream stream, int targetWidth, int jpegQuality)
    {
        using var encodedData = SKData.Create(stream);
        if (encodedData == null || encodedData.Size <= 0)
        {
            return null;
        }

        using var codec = SKCodec.Create(encodedData);
        if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        var origin = codec.EncodedOrigin;
        bool swapDims = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        int orientedWidth = swapDims ? codec.Info.Height : codec.Info.Width;
        int orientedHeight = swapDims ? codec.Info.Width : codec.Info.Height;
        if (orientedWidth <= 0 || orientedHeight <= 0)
        {
            return null;
        }

        int outW;
        int outH;
        if (orientedWidth > targetWidth)
        {
            outW = targetWidth;
            outH = Math.Max(1, (int)Math.Round(orientedHeight * (double)targetWidth / orientedWidth));
        }
        else
        {
            outW = orientedWidth;
            outH = orientedHeight;
        }

        var alphaType = codec.Info.AlphaType == SKAlphaType.Opaque
            ? SKAlphaType.Opaque
            : SKAlphaType.Premul;

        float scale = orientedWidth > targetWidth ? (float)targetWidth / orientedWidth : 1f;
        SKSizeI decodeSize = scale < 1f
            ? codec.GetScaledDimensions(scale)
            : new SKSizeI(codec.Info.Width, codec.Info.Height);

        int decodeW = Math.Max(1, decodeSize.Width);
        int decodeH = Math.Max(1, decodeSize.Height);

        var decodeInfo = new SKImageInfo(decodeW, decodeH, SKColorType.Bgra8888, alphaType);
        SKBitmap? decoded = new SKBitmap(decodeInfo);
        try
        {
            var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
            if (result != SKCodecResult.Success
                && (decodeW != codec.Info.Width || decodeH != codec.Info.Height))
            {
                // 部分格式不支援縮放解碼時，退回原始解析度解碼再交由 Skia 縮放
                decoded.Dispose();
                var fullInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, alphaType);
                decoded = new SKBitmap(fullInfo);
                result = codec.GetPixels(fullInfo, decoded.GetPixels());
            }

            if (result != SKCodecResult.Success)
            {
                return null;
            }

            return RenderOrientedAndEncodeJpeg(decoded, origin, outW, outH, jpegQuality);
        }
        finally
        {
            decoded.Dispose();
        }
    }

    private static byte[]? EncodeBitmapToJpegBytes(SKBitmap source, int targetWidth, int jpegQuality)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        int outW;
        int outH;
        if (source.Width > targetWidth)
        {
            outW = targetWidth;
            outH = Math.Max(1, (int)Math.Round(source.Height * (double)targetWidth / source.Width));
        }
        else
        {
            outW = source.Width;
            outH = source.Height;
        }

        return RenderOrientedAndEncodeJpeg(source, SKEncodedOrigin.TopLeft, outW, outH, jpegQuality);
    }

    private static byte[]? RenderOrientedAndEncodeJpeg(
        SKBitmap decoded,
        SKEncodedOrigin origin,
        int outW,
        int outH,
        int jpegQuality)
    {
        var dstInfo = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(dstInfo);
        if (surface == null)
        {
            return null;
        }

        using var srcImage = SKImage.FromBitmap(decoded);
        if (srcImage == null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        bool swapDims = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        float drawW = swapDims ? outH : outW;
        float drawH = swapDims ? outW : outH;

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(outW, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(outW, outH);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, outH);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(outW, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(outW, outH);
                canvas.RotateDegrees(270);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, outH);
                canvas.RotateDegrees(270);
                break;
        }

        var sampling = (drawW < decoded.Width || drawH < decoded.Height)
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        canvas.DrawImage(srcImage, new SKRect(0, 0, drawW, drawH), sampling);
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, jpegQuality);
        return encoded?.ToArray();
    }

    private static Bitmap? DecodeAvaloniaBitmap(byte[] imageBytes)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes, writable: false);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private bool TryGetFromMemoryCache(string cacheKey, out Bitmap? bitmap)
    {
        lock (_cacheLock)
        {
            if (!_disposed && _cacheMap.TryGetValue(cacheKey, out var node))
            {
                if (!ReferenceEquals(_lruList.First, node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                }

                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    private Bitmap? AddOrGetFromMemoryCache(string cacheKey, Bitmap newBitmap)
    {
        Bitmap? duplicateToDispose = null;
        Bitmap? result;

        lock (_cacheLock)
        {
            if (_disposed)
            {
                duplicateToDispose = newBitmap;
                result = null;
            }
            else if (_maxMemoryCacheItems == 0)
            {
                result = newBitmap;
            }
            else if (_cacheMap.TryGetValue(cacheKey, out var existingNode))
            {
                if (!ReferenceEquals(_lruList.First, existingNode))
                {
                    _lruList.Remove(existingNode);
                    _lruList.AddFirst(existingNode);
                }

                duplicateToDispose = newBitmap;
                result = existingNode.Value.Bitmap;
            }
            else
            {
                var node = new LinkedListNode<CacheEntry>(new CacheEntry(cacheKey, newBitmap));
                _lruList.AddFirst(node);
                _cacheMap[cacheKey] = node;
                result = newBitmap;

                // 只從強參考 LRU 索引移除，絕不在此 Dispose 可能仍掛在 UI Image.Source 上的 Bitmap
                while (_cacheMap.Count > _maxMemoryCacheItems && _lruList.Last != null)
                {
                    var tail = _lruList.Last;
                    _lruList.RemoveLast();
                    _cacheMap.Remove(tail.Value.Key);
                }
            }
        }

        if (duplicateToDispose != null)
        {
            DisposeBitmapSafely(duplicateToDispose);
        }

        return result;
    }

    private static void DisposeBitmapsSafely(List<Bitmap> bitmaps)
    {
        foreach (var bmp in bitmaps)
        {
            DisposeBitmapSafely(bmp);
        }
    }

    private static void DisposeBitmapSafely(Bitmap bitmap)
    {
        try
        {
            bitmap.Dispose();
        }
        catch
        {
            // 忽略已釋放或原生資源清理例外
        }
    }

    private static bool IsValidThumbKey(string? thumbKey)
        => !string.IsNullOrWhiteSpace(thumbKey) && thumbKey.Length >= 2;

    private static string BuildThumbCacheKey(string thumbKey) => "thumb:" + thumbKey;

    private static string BuildSourceCacheKey(string sourcePath) => "src:" + sourcePath;

    private static bool IsIgnoredDirectoryName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        return name.StartsWith('.')
            || string.Equals(name, "__MACOSX", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredArchiveEntry(string key)
    {
        var segments = key.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (string.Equals(seg, "__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var fileName = Path.GetFileName(key);
        return string.IsNullOrEmpty(fileName) || fileName.StartsWith("._", StringComparison.Ordinal);
    }

    private readonly struct CacheEntry
    {
        public CacheEntry(string key, Bitmap bitmap)
        {
            Key = key;
            Bitmap = bitmap;
        }

        public string Key { get; }
        public Bitmap Bitmap { get; }
    }
}

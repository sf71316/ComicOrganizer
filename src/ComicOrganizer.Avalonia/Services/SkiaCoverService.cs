using System.Buffers;
using System.IO.Compression;
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
/// 3. <see cref="LoadOrExtractCoverAsync"/> 現場解出封面時，若有提供 <see cref="ThumbWriteBackQueue"/>（一般模式）就交給它在背景寫回縮圖；
///    唯讀模式不提供，完全不寫入。
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
    private readonly ThumbWriteBackQueue? _writeBack;
    private readonly BitmapReleaseGcTrigger _releaseGcTrigger;
    private readonly int _maxMemoryCacheItems;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _cacheMap = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lruList = new();
    private volatile bool _disposed;

    /// <param name="writeBack">現場解出封面後寫回縮圖用；<c>null</c>（唯讀模式）表示不寫回。</param>
    /// <param name="releaseGcTrigger">點陣圖被擠出快取時回報像素量，累積到門檻觸發背景 GC；<c>null</c> 用預設門檻。</param>
    public SkiaCoverService(
        IThumbStore thumbStore,
        int maxMemoryCacheItems = 512,
        ThumbWriteBackQueue? writeBack = null,
        BitmapReleaseGcTrigger? releaseGcTrigger = null)
    {
        ArgumentNullException.ThrowIfNull(thumbStore);
        if (maxMemoryCacheItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemoryCacheItems), "快取上限不可為負數。");
        }

        _thumbStore = thumbStore;
        _writeBack = writeBack;
        _releaseGcTrigger = releaseGcTrigger ?? new BitmapReleaseGcTrigger();
        _maxMemoryCacheItems = maxMemoryCacheItems;
    }

    private double _displayScale = 1.0;

    /// <summary>
    /// 封面解碼用的螢幕縮放比例（DIP → 實際像素），由主視窗經 <see cref="ReportDisplayScale"/> 回報。
    /// </summary>
    public double DisplayScale => Volatile.Read(ref _displayScale);

    /// <summary>
    /// 回報視窗目前的螢幕縮放比例。只升不降：視窗移到較低縮放的螢幕時維持較高值，
    /// 避免移回高縮放螢幕時已載入的封面變模糊。非正數 / NaN / 無限大忽略。
    /// </summary>
    public void ReportDisplayScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
        {
            return;
        }

        double current = Volatile.Read(ref _displayScale);
        while (scale > current)
        {
            double observed = Interlocked.CompareExchange(ref _displayScale, scale, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    /// <summary>
    /// 將顯示框大小（DIP）換算為解碼尺寸（實際像素，無條件進位），供 <see cref="GetCachedThumbAsync"/> 等的 decodeSize 使用。
    /// 寬或高 &lt;= 0 視為不限制該方向；兩者皆無效時回傳 <see cref="CoverDecodeSize.Original"/>。
    /// </summary>
    public CoverDecodeSize ToDecodeSize(double dipWidth, double dipHeight)
    {
        double scale = DisplayScale;
        return new CoverDecodeSize(ToDevicePixels(dipWidth, scale), ToDevicePixels(dipHeight, scale));
    }

    private static int ToDevicePixels(double dip, double scale)
    {
        if (!double.IsFinite(dip) || dip <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(dip * scale);
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
    /// <param name="decodeSize">
    /// 顯示框大小（實際像素）；預設為原尺寸。依 UniformToFill 計算「填滿框所需的最小尺寸」縮小解碼，只縮小不放大；
    /// 不同框大小在記憶體快取中各自獨立。
    /// </param>
    public Task<Bitmap?> GetCachedThumbAsync(string? thumbKey, CancellationToken ct = default, CoverDecodeSize decodeSize = default)
    {
        if (_disposed || ct.IsCancellationRequested || !IsValidThumbKey(thumbKey))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        string key = thumbKey!;
        string cacheKey = BuildThumbCacheKey(key, decodeSize);

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

            // 原圖不比目標寬時不縮小，改用原尺寸 key，避免同一張圖在快取中存兩份相同像素
            int effectiveWidth = GetEffectiveDecodeWidth(bytes, decodeSize);
            string effectiveCacheKey = effectiveWidth > 0 || !decodeSize.IsSpecified
                ? cacheKey
                : BuildThumbCacheKey(key, CoverDecodeSize.Original);
            if (!ReferenceEquals(effectiveCacheKey, cacheKey) && TryGetFromMemoryCache(effectiveCacheKey, out var cachedOriginal))
            {
                return cachedOriginal;
            }

            var decoded = DecodeAvaloniaBitmap(bytes, effectiveWidth);
            if (decoded == null)
            {
                return null;
            }

            return AddOrGetFromMemoryCache(effectiveCacheKey, decoded);
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
    /// 則現場提取封面並解碼為 <see cref="Bitmap"/>；有提供寫回佇列時，解出的 JPEG 會在背景寫回縮圖。
    /// </summary>
    /// <param name="decodeSize">顯示框大小（實際像素），見 <see cref="GetCachedThumbAsync"/>；預設為原尺寸。</param>
    public async Task<Bitmap?> LoadOrExtractCoverAsync(
        string sourcePath,
        string? thumbKey,
        CancellationToken ct = default,
        CoverDecodeSize decodeSize = default)
    {
        if (_disposed || ct.IsCancellationRequested)
        {
            return null;
        }

        if (IsValidThumbKey(thumbKey))
        {
            var fromThumb = await GetCachedThumbAsync(thumbKey, ct, decodeSize).ConfigureAwait(false);
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
            ? BuildThumbCacheKey(thumbKey!, decodeSize)
            : BuildSourceCacheKey(sourcePath, decodeSize);

        if (TryGetFromMemoryCache(fallbackCacheKey, out var cachedSource))
        {
            return cachedSource;
        }

        var jpegBytes = await ExtractCoverJpegBytesAsync(
            sourcePath,
            DefaultThumbWidth,
            DefaultJpegQuality,
            ct).ConfigureAwait(false);

        if (jpegBytes != null && jpegBytes.Length > 0)
        {
            // 解出來就寫回縮圖（背景執行、不等待）；卡片已捲走也照寫，下次顯示就不必再解壓縮
            _writeBack?.TrySubmit(sourcePath, jpegBytes);
        }

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

            int effectiveWidth = GetEffectiveDecodeWidth(jpegBytes, decodeSize);
            var effectiveSize = effectiveWidth > 0 ? decodeSize : CoverDecodeSize.Original;
            string effectiveCacheKey = IsValidThumbKey(thumbKey)
                ? BuildThumbCacheKey(thumbKey!, effectiveSize)
                : BuildSourceCacheKey(sourcePath, effectiveSize);

            var decoded = DecodeAvaloniaBitmap(jpegBytes, effectiveWidth);
            if (decoded == null)
            {
                return null;
            }

            return AddOrGetFromMemoryCache(effectiveCacheKey, decoded);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 只查記憶體 LRU 快取（不讀磁碟、不解碼、不排隊），可在 UI 執行緒同步呼叫。
    /// 查詢的 key 與 <see cref="LoadOrExtractCoverAsync"/> 存放的規則一致：有效縮圖 key 用 <c>thumb:</c>，否則用 <c>src:</c>；
    /// 指定框大小時先查該大小，再查原尺寸（原圖比目標窄時會以原尺寸 key 存放）。
    /// </summary>
    public bool TryGetMemoryCachedCover(
        string? sourcePath,
        string? thumbKey,
        CoverDecodeSize decodeSize,
        out Bitmap? bitmap)
    {
        bitmap = null;
        if (_disposed)
        {
            return false;
        }

        bool useThumbKey = IsValidThumbKey(thumbKey);
        if (!useThumbKey && string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        string sizedKey = useThumbKey
            ? BuildThumbCacheKey(thumbKey!, decodeSize)
            : BuildSourceCacheKey(sourcePath!, decodeSize);
        if (TryGetFromMemoryCache(sizedKey, out bitmap))
        {
            return bitmap != null;
        }

        if (!decodeSize.IsSpecified)
        {
            return false;
        }

        string originalKey = useThumbKey
            ? BuildThumbCacheKey(thumbKey!, CoverDecodeSize.Original)
            : BuildSourceCacheKey(sourcePath!, CoverDecodeSize.Original);
        if (!TryGetFromMemoryCache(originalKey, out var original) || original == null)
        {
            bitmap = null;
            return false;
        }

        // 只在「原圖不比所需寬」時沿用原尺寸那張（與非同步載入的存放規則一致）；
        // 原尺寸那張比所需寬時，非同步載入會另外縮小解碼一張，這裡不拿大張的代替
        var pixelSize = original.PixelSize;
        if (ComputeEffectiveDecodeWidth(pixelSize.Width, pixelSize.Height, decodeSize) != 0)
        {
            bitmap = null;
            return false;
        }

        bitmap = original;
        return true;
    }

    /// <summary>清空記憶體 LRU 快取強參考（不強制 Dispose，避免畫面上仍在顯示中的 Bitmap 失效拋例外）。</summary>
    public void ClearMemoryCache()
    {
        long releasedBytes = 0;
        lock (_cacheLock)
        {
            for (var node = _lruList.First; node != null; node = node.Next)
            {
                releasedBytes += EstimatePixelBytes(node.Value.Bitmap);
            }

            _cacheMap.Clear();
            _lruList.Clear();
        }

        _releaseGcTrigger.ReportReleased(releasedBytes);
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

            if (stream.CanSeek)
            {
                long len = stream.Length;
                if (len <= 0 || len > int.MaxValue || ct.IsCancellationRequested)
                {
                    return null;
                }

                // 直接依檔案精確長度配置單一陣列，省去 MemoryStream 中繼緩衝與 ToArray() 二次複製
                var buffer = new byte[(int)len];
                int offset = 0;
                while (offset < buffer.Length)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return null;
                    }

                    int read = stream.Read(buffer, offset, buffer.Length - offset);
                    if (read == 0)
                    {
                        break;
                    }

                    offset += read;
                }

                if (offset == 0 || ct.IsCancellationRequested)
                {
                    return null;
                }

                if (offset == buffer.Length)
                {
                    return buffer;
                }

                var trimmed = new byte[offset];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, offset);
                return trimmed;
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
            // 使用 FileStream 搭配 ArrayPool 讀取進原生 SKData，避免 mmap 鎖檔與 LOH byte[] 配置
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return DecodeAndEncodeImageStream(fs, targetWidth, jpegQuality, fs.CanSeek ? fs.Length : -1);
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ExtractArchiveCover(string archivePath, int targetWidth, int jpegQuality, CancellationToken ct)
    {
        if (_disposed || ct.IsCancellationRequested)
        {
            return null;
        }

        // .zip / .cbz 優先走 .NET 內建 ZipFile（直接讀尾端中央目錄，比 SharpCompress 快且大幅減少 .NET 垃圾）
        var ext = Path.GetExtension(archivePath);
        if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".cbz", StringComparison.OrdinalIgnoreCase))
        {
            var zipResult = TryExtractZipCover(archivePath, targetWidth, jpegQuality, ct);
            if (zipResult != null)
            {
                return zipResult;
            }

            if (_disposed || ct.IsCancellationRequested)
            {
                return null;
            }
        }

        try
        {
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
                    var jpeg = DecodeAndEncodeImageStream(entryStream, targetWidth, jpegQuality, entry.Size > 0 ? entry.Size : -1);
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

    private byte[]? TryExtractZipCover(string archivePath, int targetWidth, int jpegQuality, CancellationToken ct)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archivePath);

            // .NET 的 ZipFile 一律以 UTF-8 解碼條目名稱，且不讀「Unicode 路徑附加欄位」（0x7075）；
            // SharpCompress 會優先採用該欄位的正確名稱。非 UTF-8 檔名（例如舊版 WinRAR / 日文 Windows 壓的 Shift-JIS、Big5）
            // 在這裡會變成含 U+FFFD 的亂碼，自然排序結果可能與改版前不同而選錯封面。
            // 遇到這類壓縮檔就整個交給下方 SharpCompress 處理，確保選到的封面與改版前一致。
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.Contains('\uFFFD'))
                {
                    return null;
                }
            }

            var imageEntries = zip.Entries
                .Where(e =>
                    e.Length > 0
                    && !string.IsNullOrEmpty(e.FullName)
                    && !e.FullName.EndsWith('/')
                    && !e.FullName.EndsWith('\\')
                    && !IsIgnoredArchiveEntry(e.FullName)
                    && SupportedExtensions.IsImage(e.FullName))
                .OrderBy(e => e.FullName, NaturalStringComparer.Instance)
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
                    using var entryStream = entry.Open();
                    var jpeg = DecodeAndEncodeImageStream(entryStream, targetWidth, jpegQuality, entry.Length);
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
            // 非標準 ZIP（例如副檔名誤標為 .zip 的 RAR/7z）交給 SharpCompress fallback
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

    private static byte[]? DecodeAndEncodeImageStream(Stream stream, int targetWidth, int jpegQuality, long expectedLength = -1)
    {
        using var encodedData = ReadStreamToSkData(stream, expectedLength);
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

    /// <summary>
    /// 透過 <see cref="ArrayPool{T}"/> 將串流讀入共用緩衝區再一次複製為原生 <see cref="SKData"/>，
    /// 避免 <see cref="MemoryStream"/> 倍增擴容與 <see cref="SKData.Create(Stream)"/> 在 LOH 留下數 MB 暫時 byte[] 垃圾。
    /// <para>
    /// 不設人為大小上限（改版前的 <see cref="SKData.Create(Stream)"/> 也沒有）：只受 .NET 單一陣列長度上限
    /// （<see cref="Array.MaxLength"/>，約 2 GB）限制，超過才放棄。
    /// </para>
    /// </summary>
    private static SKData? ReadStreamToSkData(Stream stream, long expectedLength)
    {
        const int DefaultInitialCapacity = 64 * 1024;
        int maxAllowedBytes = Array.MaxLength;

        if (expectedLength > maxAllowedBytes)
        {
            return null;
        }

        int initialCapacity = expectedLength > 0
            ? (int)expectedLength
            : DefaultInitialCapacity;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        try
        {
            int totalRead = 0;
            while (true)
            {
                if (totalRead == buffer.Length)
                {
                    // 緩衝區剛好讀滿：先試讀 1 個位元組確認是否已到結尾，避免大小剛好時白白配置兩倍緩衝區
                    int probe = stream.ReadByte();
                    if (probe < 0)
                    {
                        break;
                    }

                    if (buffer.Length >= maxAllowedBytes)
                    {
                        return null;
                    }

                    int nextSize = (int)Math.Min(maxAllowedBytes, (long)buffer.Length * 2);
                    byte[] grown = ArrayPool<byte>.Shared.Rent(nextSize);
                    Buffer.BlockCopy(buffer, 0, grown, 0, totalRead);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = grown;
                    buffer[totalRead++] = (byte)probe;
                    continue;
                }

                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead <= 0)
            {
                return null;
            }

            return SKData.CreateCopy(buffer.AsSpan(0, totalRead));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

    /// <summary>
    /// 實際要縮小解碼的寬度（0 = 原尺寸）。封面以 UniformToFill 顯示，需同時填滿框寬與框高：
    /// 所需寬度 = max(框寬, 框高 × 原圖寬高比)；寬扁封面（如跨頁）因此會保留較大寬度，避免被放大而模糊。
    /// 所需寬度不小於原圖寬度時回傳 0（不放大）。
    /// </summary>
    internal static int GetEffectiveDecodeWidth(byte[] imageBytes, CoverDecodeSize decodeSize)
    {
        if (!decodeSize.IsSpecified)
        {
            return 0;
        }

        var (originalWidth, originalHeight) = ReadImageSize(imageBytes);
        return ComputeEffectiveDecodeWidth(originalWidth, originalHeight, decodeSize);
    }

    /// <summary>依原圖尺寸計算縮小解碼寬度；不需縮小（原圖不比所需寬）時回傳 0。</summary>
    private static int ComputeEffectiveDecodeWidth(int originalWidth, int originalHeight, CoverDecodeSize decodeSize)
    {
        if (!decodeSize.IsSpecified || originalWidth <= 0 || originalHeight <= 0)
        {
            return 0;
        }

        int required = Math.Max(decodeSize.Width, 0);
        if (decodeSize.Height > 0)
        {
            int forHeight = (int)Math.Ceiling(decodeSize.Height * (double)originalWidth / originalHeight);
            required = Math.Max(required, forHeight);
        }

        return required > 0 && required < originalWidth ? required : 0;
    }

    /// <summary>
    /// 解碼為 Avalonia <see cref="Bitmap"/>；<paramref name="decodePixelWidth"/> &gt; 0 時以高品質縮小解碼到該寬度
    /// （例：探索牆疊牌 330 → 約 188 px，記憶體約省 68%）。呼叫端需先經 <see cref="GetEffectiveDecodeWidth"/> 計算並確保不放大。
    /// </summary>
    private static Bitmap? DecodeAvaloniaBitmap(byte[] imageBytes, int decodePixelWidth = 0)
    {
        try
        {
            using var ms = new MemoryStream(imageBytes, writable: false);
            if (decodePixelWidth > 0)
            {
                return Bitmap.DecodeToWidth(ms, decodePixelWidth, BitmapInterpolationMode.HighQuality);
            }

            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>只讀圖檔標頭取得尺寸（不解碼像素）；無法辨識時回傳 (0, 0)。</summary>
    private static (int Width, int Height) ReadImageSize(byte[] imageBytes)
    {
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data);
        return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
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
        long evictedBytes = 0;

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

                // 只從強參考 LRU 索引移除，絕不在此 Dispose 可能仍掛在 UI Image.Source 上的 Bitmap；
                // 改為回報放掉的像素量，累積夠多時觸發背景 GC 讓原生記憶體早點回收
                while (_cacheMap.Count > _maxMemoryCacheItems && _lruList.Last != null)
                {
                    var tail = _lruList.Last;
                    _lruList.RemoveLast();
                    _cacheMap.Remove(tail.Value.Key);
                    evictedBytes += EstimatePixelBytes(tail.Value.Bitmap);
                }
            }
        }

        if (duplicateToDispose != null)
        {
            DisposeBitmapSafely(duplicateToDispose);
        }

        if (evictedBytes > 0)
        {
            _releaseGcTrigger.ReportReleased(evictedBytes);
        }

        return result;
    }

    /// <summary>點陣圖像素佔用的原生記憶體估計（BGRA 每像素 4 bytes）。</summary>
    private static long EstimatePixelBytes(Bitmap bitmap)
    {
        try
        {
            var size = bitmap.PixelSize;
            return (long)size.Width * size.Height * 4;
        }
        catch
        {
            return 0;
        }
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

    private static string BuildThumbCacheKey(string thumbKey, CoverDecodeSize decodeSize = default)
        => "thumb:" + thumbKey + BuildSizeSuffix(decodeSize);

    private static string BuildSourceCacheKey(string sourcePath, CoverDecodeSize decodeSize = default)
        => "src:" + sourcePath + BuildSizeSuffix(decodeSize);

    /// <summary>原尺寸不加後綴，維持與既有快取 key 相同；指定框大小時以 <c>@寬x高</c> 區分。</summary>
    private static string BuildSizeSuffix(CoverDecodeSize decodeSize)
        => decodeSize.IsSpecified ? "@" + decodeSize.Width + "x" + decodeSize.Height : string.Empty;

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

/// <summary>
/// 封面顯示框大小（實際像素），用於依顯示大小縮小解碼；<see cref="Original"/>（預設值）表示原尺寸解碼。
/// 寬或高 &lt;= 0 表示不限制該方向。
/// </summary>
public readonly record struct CoverDecodeSize(int Width, int Height)
{
    public static CoverDecodeSize Original => default;

    public bool IsSpecified => Width > 0 || Height > 0;
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using SkiaSharp;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 畫面上「現場從原始檔解出」的封面，交給背景執行緒寫回 <c>thumbs\</c> 與 cache.db 的縮圖索引，
/// 下次顯示同一本作品時直接讀縮圖檔（約 1–2 ms），不必再解壓縮原始檔（約 10–25 ms）。
/// <para>
/// 規則（與「資料管理 → 增量更新縮圖」一致）：
/// <list type="bullet">
/// <item>只處理已在 catalog 的作品；不在 catalog 的路徑略過（不新增作品列）。</item>
/// <item>單張圖片檔略過（與增量同步相同，避免瀏覽圖片資料夾時大量寫入）。</item>
/// <item>已有可讀縮圖檔的作品略過，不覆寫。</item>
/// <item>縮圖目錄總大小達 <c>ThumbCacheMaxBytes</c> 後停止寫入（不主動淘汰既有縮圖）；上限 0 表示不限制。</item>
/// <item>同一個路徑在本次執行只送一次。</item>
/// </list>
/// 唯讀模式不得建立此物件（由呼叫端保證）。Dispose 必須在 meta store Dispose 之前。
/// </para>
/// </summary>
public sealed class ThumbWriteBackQueue : IDisposable
{
    /// <summary>寫回的縮圖在 asset_thumb.cover_origin 的標記。</summary>
    public const string CoverOriginTag = "skia-lazy";

    private const int QueueCapacity = 512;
    private const int MaxBatchSize = 32;

    private readonly IAssetMetaStore _store;
    private readonly IThumbStore _thumbs;
    private readonly Func<long> _limitBytes;
    private readonly Channel<WriteBackItem> _channel;
    private readonly ConcurrentDictionary<string, byte> _submitted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task _worker;

    // 縮圖目錄目前總大小：第一次需要時才在背景執行緒計算（列舉整個 thumbs\），之後每寫一張累加
    private long _totalBytes = -1;
    private volatile bool _full;
    private int _disposed;
    private int _writtenCount;

    public ThumbWriteBackQueue(IAssetMetaStore store, IThumbStore thumbs, Func<long> limitBytes)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(thumbs);
        ArgumentNullException.ThrowIfNull(limitBytes);
        _store = store;
        _thumbs = thumbs;
        _limitBytes = limitBytes;
        _channel = Channel.CreateBounded<WriteBackItem>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _worker = Task.Run(WorkerLoopAsync);
    }

    /// <summary>本次執行已寫回的縮圖數。</summary>
    public int WrittenCount => Volatile.Read(ref _writtenCount);

    /// <summary>縮圖目錄已達上限，之後不再寫回。</summary>
    public bool IsFull => _full;

    /// <summary>
    /// 送出一張現場解出的封面（JPEG）。立即返回，不阻塞呼叫端；佇列滿、已達上限、已送過或已 Dispose 時回傳 false 並略過。
    /// </summary>
    public bool TrySubmit(string sourcePath, byte[] jpegBytes)
    {
        if (Volatile.Read(ref _disposed) != 0 || _full)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || jpegBytes == null || jpegBytes.Length == 0)
        {
            return false;
        }

        if (!_submitted.TryAdd(sourcePath, 0))
        {
            return false;
        }

        if (!_channel.Writer.TryWrite(new WriteBackItem(sourcePath, jpegBytes)))
        {
            // 佇列滿或已關閉：放掉這次，之後再顯示時可重送
            _submitted.TryRemove(sourcePath, out _);
            return false;
        }

        return true;
    }

    /// <summary>與「資料管理 → 增量更新縮圖」相同的縮圖 key：路徑 UTF-8 的 SHA1 小寫十六進位。</summary>
    public static string BuildThumbKey(string path)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();

    private async Task WorkerLoopAsync()
    {
        var reader = _channel.Reader;
        var batch = new List<AssetMeta>(MaxBatchSize);
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < MaxBatchSize && reader.TryRead(out var item))
                {
                    var meta = WriteThumbFile(item);
                    if (meta != null)
                    {
                        batch.Add(meta);
                    }
                }

                if (batch.Count > 0)
                {
                    CommitIndex(batch);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThumbWriteBack] worker crashed: {ex}");
        }
    }

    /// <summary>寫縮圖檔（不持有 DB 鎖），回傳要更新索引的 meta；略過時回傳 null。</summary>
    private AssetMeta? WriteThumbFile(WriteBackItem item)
    {
        try
        {
            if (_full || SupportedExtensions.IsImage(item.Path))
            {
                return null;
            }

            var meta = _store.Get(item.Path);
            if (meta == null)
            {
                return null;
            }

            string key;
            if (!string.IsNullOrEmpty(meta.CoverThumbKey))
            {
                using (var existing = _thumbs.OpenRead(meta.CoverThumbKey))
                {
                    if (existing != null)
                    {
                        return null;
                    }
                }

                // 索引有 key 但檔案不見了：沿用原本的 key 補回檔案
                key = meta.CoverThumbKey;
            }
            else
            {
                key = BuildThumbKey(item.Path);
            }

            if (!HasRoomFor(item.Jpeg.LongLength))
            {
                return null;
            }

            _thumbs.Write(key, item.Jpeg);
            _totalBytes += item.Jpeg.LongLength;

            var (width, height) = ReadJpegSize(item.Jpeg);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            meta.CoverOrigin = CoverOriginTag;
            meta.CoverThumbKey = key;
            meta.CoverWidth = width;
            meta.CoverHeight = height;
            meta.LastScanned = now;
            meta.LastAccessed = now;
            return meta;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThumbWriteBack] write failed: {ex.Message}");
            return null;
        }
    }

    private bool HasRoomFor(long bytes)
    {
        long limit = _limitBytes();
        if (limit <= 0)
        {
            return true;
        }

        if (_totalBytes < 0)
        {
            _totalBytes = _thumbs.TotalSizeBytes();
        }

        if (_totalBytes + bytes > limit)
        {
            _full = true;
            return false;
        }

        return true;
    }

    private void CommitIndex(List<AssetMeta> batch)
    {
        try
        {
            // 一批一個交易：少做 WAL flush，持有 DB 鎖的時間也很短（檔案已在交易外寫好）
            _store.RunInTransaction(() =>
            {
                foreach (var meta in batch)
                {
                    _store.Upsert(meta);
                }
            });
            Interlocked.Add(ref _writtenCount, batch.Count);
        }
        catch (Exception ex)
        {
            // 索引沒寫進去時縮圖檔會變成孤兒檔，由「資料管理 → 完整性檢查」清理
            Debug.WriteLine($"[ThumbWriteBack] index commit failed: {ex.Message}");
        }
    }

    private static (int? Width, int? Height) ReadJpegSize(byte[] jpeg)
    {
        try
        {
            using (var ms = new MemoryStream(jpeg, writable: false))
            {
                using (var codec = SKCodec.Create(ms))
                {
                    if (codec != null)
                    {
                        return (codec.Info.Width, codec.Info.Height);
                    }
                }
            }
        }
        catch
        {
            // 取不到尺寸不影響寫回
        }

        return (null, null);
    }

    /// <summary>停止收件並等背景執行緒寫完佇列中剩下的縮圖（最多 5 秒）。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // 背景執行緒例外已在內部記錄
        }
    }

    private readonly record struct WriteBackItem(string Path, byte[] Jpeg);
}

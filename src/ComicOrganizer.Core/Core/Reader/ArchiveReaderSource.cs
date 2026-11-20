using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 從壓縮檔（zip/cbz/rar/cbr/7z）讀圖。
/// 隨機存取對 zip 便宜、對 solid 7z/rar 昂貴(每次從頭重解) → 快速來回撥頁會頓。
/// 開檔時起背景工作，用「第二個 archive 實例」以 forward-only <see cref="IReader"/> 循序預解到有界 LRU 快取
/// (solid 的高效循序路徑、且不阻塞主實例的隨機 fallback)；OpenPage 命中快取即更新 MRU 並瞬間回傳，
/// 未命中走主實例隨機解壓並寫入 LRU 快取（超過預算時淘汰最久未用頁面，永遠保留剛存取的該頁）。
/// </summary>
public class ArchiveReaderSource : IReaderSource
{
    private static readonly HashSet<string> ImageExts = SupportedExtensions.Image;

    /// <summary>預設快取記憶體上限（256 MB）。</summary>
    public const long DefaultMaxCacheBytes = 256L * 1024 * 1024;

    private readonly IArchive _archive;
    private readonly IArchiveEntry[] _entries;             // 已過濾 + 自然排序的圖片 entries
    private readonly Dictionary<string, int> _indexByKey;  // entry.Key → 排序後索引
    private readonly byte[]?[] _pageBytes;                 // 快取每頁 bytes（null=未快取或已淘汰）
    private readonly long _maxCacheBytes;
    private readonly LinkedList<int> _lruList = new();
    private readonly Dictionary<int, LinkedListNode<int>> _lruNodes = new();
    private readonly object _cacheLock = new();
    private readonly object _archiveLock = new();
    private long _cachedBytes;
    private volatile bool _disposed;

    public string DisplayName { get; }
    public string RootPath { get; }
    public int PageCount => _entries.Length;
    public long MaxCacheBytes => _maxCacheBytes;
    public long CachedBytes { get { lock (_cacheLock) return _cachedBytes; } }
    public int CachedPageCount { get { lock (_cacheLock) return _lruNodes.Count; } }

    public ArchiveReaderSource(string archivePath, long maxCacheBytes = DefaultMaxCacheBytes)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        if (maxCacheBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCacheBytes), "maxCacheBytes must be positive.");

        RootPath = archivePath;
        DisplayName = Path.GetFileName(archivePath);
        _maxCacheBytes = maxCacheBytes;

        _archive = ArchiveFactory.OpenArchive(archivePath);
        bool isSolid = _archive.IsSolid;

        _entries = _archive.Entries
            .Where(e => !e.IsDirectory && e.Key != null && ImageExts.Contains(Path.GetExtension(e.Key)))
            .OrderBy(e => e.Key ?? "", NaturalStringComparer.Instance)
            .ToArray();

        _indexByKey = new Dictionary<string, int>(_entries.Length);
        for (int i = 0; i < _entries.Length; i++)
        {
            var k = _entries[i].Key;
            if (k != null) _indexByKey[k] = i;
        }
        _pageBytes = new byte[_entries.Length][];

        if (_entries.Length > 0)
            Task.Run(() => BackgroundExtractAll(isSolid));
    }

    /// <summary>背景：第二實例 forward-only 循序解壓至 LRU 快取（達背景預算上限即停止，避免擠掉使用者正在看的頁）。</summary>
    private void BackgroundExtractAll(bool isSolid)
    {
        long backgroundBudget = isSolid
            ? _maxCacheBytes * 3 / 4
            : Math.Min(_maxCacheBytes / 4, 64L * 1024 * 1024);

        try
        {
            using var archive2 = ArchiveFactory.OpenArchive(RootPath);
            using var reader = archive2.ExtractAllEntries();
            while (!_disposed && reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory || entry.Key == null) continue;
                if (!_indexByKey.TryGetValue(entry.Key, out int idx)) continue;

                lock (_cacheLock)
                {
                    if (_disposed) break;
                    if (_pageBytes[idx] != null) continue;   // 已被 OpenPage 解過
                    if (_cachedBytes >= backgroundBudget) break;
                }

                using var ms = new MemoryStream();
                using (var es = reader.OpenEntryStream()) es.CopyTo(ms);
                var bytes = ms.ToArray();

                lock (_cacheLock)
                {
                    if (_disposed) break;
                    if (_pageBytes[idx] != null) continue;
                    if (_cachedBytes + bytes.Length > backgroundBudget) break;

                    _pageBytes[idx] = bytes;
                    _cachedBytes += bytes.Length;
                    // 背景預解掛到 LRU 尾端，優先保留使用者實際瀏覽過的頁面
                    var node = _lruList.AddLast(idx);
                    _lruNodes[idx] = node;
                }
            }
        }
        catch { /* 背景失敗 → OpenPage 全走隨機 fallback */ }
    }

    public Stream OpenPage(int index)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ArchiveReaderSource));
        if (index < 0 || index >= _entries.Length) throw new ArgumentOutOfRangeException(nameof(index));

        lock (_cacheLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ArchiveReaderSource));
            if (_pageBytes[index] is byte[] cached)
            {
                TouchMruLocked(index);
                return new MemoryStream(cached, writable: false);
            }
        }

        lock (_archiveLock)
        {
            lock (_cacheLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ArchiveReaderSource));
                if (_pageBytes[index] is byte[] cached)
                {
                    TouchMruLocked(index);
                    return new MemoryStream(cached, writable: false);
                }
            }

            using var ms = new MemoryStream();
            using (var src = _entries[index].OpenEntryStream()) src.CopyTo(ms);
            var bytes = ms.ToArray();

            lock (_cacheLock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ArchiveReaderSource));

                if (_pageBytes[index] is byte[] existing)
                {
                    TouchMruLocked(index);
                    return new MemoryStream(existing, writable: false);
                }

                _pageBytes[index] = bytes;
                _cachedBytes += bytes.Length;
                var node = _lruList.AddFirst(index);
                _lruNodes[index] = node;

                while (_cachedBytes > _maxCacheBytes && _lruList.Count > 1)
                {
                    var victimNode = _lruList.Last!;
                    int victimIdx = victimNode.Value;
                    if (victimIdx == index) break;

                    _lruList.RemoveLast();
                    _lruNodes.Remove(victimIdx);
                    var victimBytes = _pageBytes[victimIdx];
                    _pageBytes[victimIdx] = null;
                    if (victimBytes != null)
                    {
                        _cachedBytes -= victimBytes.Length;
                    }
                }
            }

            return new MemoryStream(bytes, writable: false);
        }
    }

    private void TouchMruLocked(int index)
    {
        if (_lruNodes.TryGetValue(index, out var node))
        {
            _lruList.Remove(node);
            _lruList.AddFirst(node);
        }
    }

    public string GetPageExtension(int index) =>
        Path.GetExtension(_entries[index].Key ?? "") ?? "";

    public string GetPageName(int index) =>
        Path.GetFileName(_entries[index].Key ?? "") ?? $"page-{index}";

    public ReaderPageMetadata GetPageMetadata(int index)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ArchiveReaderSource));
        if (index < 0 || index >= _entries.Length) throw new ArgumentOutOfRangeException(nameof(index));

        var entry = _entries[index];
        string name = Path.GetFileName(entry.Key ?? "");
        if (string.IsNullOrEmpty(name)) name = $"page-{index}";
        long? size = entry.Size >= 0 ? entry.Size : null;
        return new ReaderPageMetadata(index, name, size, entry.LastModifiedTime);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_archiveLock)
        {
            _archive.Dispose();
        }
        lock (_cacheLock)
        {
            Array.Clear(_pageBytes);
            _lruList.Clear();
            _lruNodes.Clear();
            _cachedBytes = 0;
        }
    }
}

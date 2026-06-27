using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 從壓縮檔（zip/cbz/rar/cbr/7z）讀圖。
/// 隨機存取對 zip 便宜、對 solid 7z/rar 昂貴(每次從頭重解) → 快速來回撥頁會頓。
/// 開檔時起背景工作，用「第二個 archive 實例」以 forward-only <see cref="IReader"/> 循序解壓整包到記憶體
/// (solid 的高效循序路徑、且不阻塞主實例的隨機 fallback)；OpenPage 命中 _pageBytes 即免鎖瞬間回傳，
/// 未命中走主實例隨機解壓並順手快取。超過記憶體上限則停止預解，餘頁走 fallback。
/// </summary>
public class ArchiveReaderSource : IReaderSource
{
    private static readonly HashSet<string> ImageExts = SupportedExtensions.Image;
    private const long MaxPreloadBytes = 1_200_000_000L;   // 背景預解記憶體上限

    private readonly IArchive _archive;
    private readonly IArchiveEntry[] _entries;             // 已過濾 + 自然排序的圖片 entries
    private readonly Dictionary<string, int> _indexByKey;  // entry.Key → 排序後索引
    private readonly byte[]?[] _pageBytes;                 // 背景循序解壓的每頁 bytes(null=尚未解到)
    private readonly object _archiveLock = new();
    private volatile bool _disposed;

    public string DisplayName { get; }
    public string RootPath { get; }
    public int PageCount => _entries.Length;

    public ArchiveReaderSource(string archivePath)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"Archive not found: {archivePath}");

        RootPath = archivePath;
        DisplayName = Path.GetFileName(archivePath);

        _archive = ArchiveFactory.OpenArchive(archivePath);
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
            Task.Run(BackgroundExtractAll);
    }

    /// <summary>背景：第二實例 forward-only 循序解壓整包到 _pageBytes(不阻塞主實例隨機 fallback)。</summary>
    private void BackgroundExtractAll()
    {
        try
        {
            using var archive2 = ArchiveFactory.OpenArchive(RootPath);
            using var reader = archive2.ExtractAllEntries();
            long total = 0;
            while (!_disposed && reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory || entry.Key == null) continue;
                if (!_indexByKey.TryGetValue(entry.Key, out int idx)) continue;
                if (Volatile.Read(ref _pageBytes[idx]) != null) continue;   // 已被 fallback 解過

                using var ms = new MemoryStream();
                using (var es = reader.OpenEntryStream()) es.CopyTo(ms);
                var bytes = ms.ToArray();
                Volatile.Write(ref _pageBytes[idx], bytes);
                total += bytes.Length;
                if (total > MaxPreloadBytes) break;     // 記憶體上限 → 停止預解
            }
        }
        catch { /* 背景失敗 → OpenPage 全走隨機 fallback */ }
    }

    public Stream OpenPage(int index)
    {
        if (index < 0 || index >= _entries.Length) throw new ArgumentOutOfRangeException(nameof(index));

        var cached = Volatile.Read(ref _pageBytes[index]);
        if (cached != null) return new MemoryStream(cached, writable: false);   // 命中：免鎖、免重解

        lock (_archiveLock)
        {
            cached = Volatile.Read(ref _pageBytes[index]);
            if (cached != null) return new MemoryStream(cached, writable: false);

            using var ms = new MemoryStream();
            using (var src = _entries[index].OpenEntryStream()) src.CopyTo(ms);
            var bytes = ms.ToArray();
            Volatile.Write(ref _pageBytes[index], bytes);   // 順手快取，下次免重解
            return new MemoryStream(bytes, writable: false);
        }
    }

    public string GetPageExtension(int index) =>
        Path.GetExtension(_entries[index].Key ?? "") ?? "";

    public string GetPageName(int index) =>
        Path.GetFileName(_entries[index].Key ?? "") ?? $"page-{index}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_archiveLock) { _archive.Dispose(); }
    }
}

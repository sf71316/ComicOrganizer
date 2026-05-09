using SharpCompress.Archives;
using SharpCompress.Common;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 從壓縮檔（zip/cbz/rar/cbr/7z）讀圖。
/// SharpCompress.IArchive 支援隨機存取 entries，但 archive instance 非 thread-safe，
/// 內部 lock 序列化所有 stream 讀取。OpenPage 會把 entry 完整讀進 MemoryStream 回傳，
/// 避免 caller 持 stream 期間 archive lock 被卡住影響其他頁載入。
/// </summary>
public class ArchiveReaderSource : IReaderSource
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
    };

    private readonly IArchive _archive;
    private readonly IArchiveEntry[] _entries;   // 已過濾 + 自然排序的圖片 entries
    private readonly object _archiveLock = new();
    private bool _disposed;

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
    }

    public Stream OpenPage(int index)
    {
        if (index < 0 || index >= _entries.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var ms = new MemoryStream();
        lock (_archiveLock)
        {
            using var src = _entries[index].OpenEntryStream();
            src.CopyTo(ms);
        }
        ms.Position = 0;
        return ms;
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

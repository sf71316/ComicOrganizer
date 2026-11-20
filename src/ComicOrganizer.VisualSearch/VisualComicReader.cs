using System.IO.Hashing;
using System.Text;
using SharpCompress.Archives;
using SkiaSharp;

namespace ComicOrganizer.VisualSearch;

public sealed record VisualComicItem(string Path, string Title, bool IsDirectory);

public sealed record RawComicPage(long MangaId, int PageNo, string EntryName, byte[] Bytes);

public static class VisualComicReader
{
    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".cbz", ".rar", ".cbr", ".7z"
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp"
    };

    public static bool IsSupportedArchive(string path)
        => ArchiveExts.Contains(Path.GetExtension(path));

    public static bool IsSupportedImage(string path)
        => ImageExts.Contains(Path.GetExtension(path));

    /// <summary>
    /// 遞迴探索指定根目錄下所有漫畫（含任意新增作者資料夾與壓縮檔），支援選用上限。
    /// </summary>
    public static List<VisualComicItem> DiscoverComics(string rootDir, int? maxCount = null, int minImagesPerFolder = 3)
    {
        var results = new List<VisualComicItem>();
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
            return results;

        var queue = new Queue<string>();
        queue.Enqueue(rootDir);

        while (queue.Count > 0 && (!maxCount.HasValue || results.Count < maxCount.Value))
        {
            var dir = queue.Dequeue();
            string[] files;
            string[] subDirs;
            try
            {
                files = Directory.GetFiles(dir);
                subDirs = Directory.GetDirectories(dir);
            }
            catch
            {
                continue;
            }

            int imgCount = files.Count(f => ImageExts.Contains(Path.GetExtension(f)));
            if (imgCount >= minImagesPerFolder)
            {
                results.Add(new VisualComicItem(dir, Path.GetFileName(dir), IsDirectory: true));
                if (maxCount.HasValue && results.Count >= maxCount.Value) break;
            }

            foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (ArchiveExts.Contains(Path.GetExtension(f)))
                {
                    results.Add(new VisualComicItem(f, Path.GetFileNameWithoutExtension(f), IsDirectory: false));
                    if (maxCount.HasValue && results.Count >= maxCount.Value) break;
                }
            }

            foreach (var sub in subDirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.') || name.StartsWith("_Legacy", StringComparison.OrdinalIgnoreCase))
                    continue;
                queue.Enqueue(sub);
            }
        }

        return results;
    }

    public static VisualComicItem? TryCreateItem(string path)
    {
        if (Directory.Exists(path))
            return new VisualComicItem(path, Path.GetFileName(path), IsDirectory: true);
        if (File.Exists(path) && IsSupportedArchive(path))
            return new VisualComicItem(path, Path.GetFileNameWithoutExtension(path), IsDirectory: false);
        return null;
    }

    /// <summary>
    /// 計算內容指紋（XxHash3）：搬移或改名不會改變內容指紋，讓增量更新可零重算更新路徑。
    /// </summary>
    public static string ComputeContentFingerprint(VisualComicItem item)
    {
        var hasher = new XxHash3();
        if (item.IsDirectory)
        {
            var imgs = Directory.GetFiles(item.Path)
                .Where(f => ImageExts.Contains(Path.GetExtension(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var f in imgs)
            {
                var fi = new FileInfo(f);
                hasher.Append(Encoding.UTF8.GetBytes($"{fi.Name}|{fi.Length};"));
            }
        }
        else
        {
            var fi = new FileInfo(item.Path);
            hasher.Append(BitConverter.GetBytes(fi.Length));
            using var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[Math.Min(65536, fi.Length)];
            int read = fs.Read(buf, 0, buf.Length);
            hasher.Append(buf.AsSpan(0, read));
            if (fi.Length > 65536)
            {
                fs.Seek(-Math.Min(65536, fi.Length), SeekOrigin.End);
                read = fs.Read(buf, 0, buf.Length);
                hasher.Append(buf.AsSpan(0, read));
            }
        }
        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    public static IEnumerable<RawComicPage> StreamRawPages(long mangaId, VisualComicItem item, CancellationToken ct = default)
    {
        if (item.IsDirectory)
        {
            var files = Directory.GetFiles(item.Path)
                .Where(f => ImageExts.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, VisualNaturalStringComparer.Instance)
                .ToList();

            int pageNo = 0;
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                pageNo++;
                byte[]? bytes = null;
                try { bytes = File.ReadAllBytes(f); } catch { }
                if (bytes is { Length: > 0 })
                    yield return new RawComicPage(mangaId, pageNo, Path.GetFileName(f), bytes);
            }
            yield break;
        }

        IArchive? archive = null;
        try { archive = ArchiveFactory.OpenArchive(item.Path); } catch { yield break; }

        using (archive)
        {
            var entries = archive.Entries
                .Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) && ImageExts.Contains(Path.GetExtension(e.Key)))
                .OrderBy(e => e.Key, VisualNaturalStringComparer.Instance)
                .ToList();

            int pageNo = 0;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                pageNo++;
                byte[]? bytes = null;
                try
                {
                    using var es = entry.OpenEntryStream();
                    using var ms = new MemoryStream(entry.Size > 0 && entry.Size < int.MaxValue ? (int)entry.Size : 65536);
                    es.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                catch { }
                if (bytes is { Length: > 0 })
                    yield return new RawComicPage(mangaId, pageNo, entry.Key!, bytes);
            }
        }
    }

    public static SKImage? LoadSinglePageImage(string mangaPath, string entryName)
    {
        try
        {
            if (Directory.Exists(mangaPath))
            {
                var full = Path.Combine(mangaPath, entryName);
                if (!File.Exists(full)) return null;
                using var bmp = SKBitmap.Decode(full);
                return bmp != null ? SKImage.FromBitmap(bmp) : null;
            }
            if (File.Exists(mangaPath))
            {
                using var archive = ArchiveFactory.OpenArchive(mangaPath);
                var entry = archive.Entries.FirstOrDefault(e =>
                    !e.IsDirectory && string.Equals(e.Key, entryName, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;
                using var es = entry.OpenEntryStream();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                ms.Position = 0;
                using var bmp = SKBitmap.Decode(ms);
                return bmp != null ? SKImage.FromBitmap(bmp) : null;
            }
        }
        catch { }
        return null;
    }
}

internal sealed class VisualNaturalStringComparer : IComparer<string?>
{
    public static readonly VisualNaturalStringComparer Instance = new();

    public int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        int ia = 0, ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                long na = 0, nb = 0;
                while (ia < a.Length && char.IsDigit(a[ia])) na = na * 10 + (a[ia++] - '0');
                while (ib < b.Length && char.IsDigit(b[ib])) nb = nb * 10 + (b[ib++] - '0');
                if (na != nb) return na.CompareTo(nb);
            }
            else
            {
                int cmp = char.ToUpperInvariant(a[ia]).CompareTo(char.ToUpperInvariant(b[ib]));
                if (cmp != 0) return cmp;
                ia++;
                ib++;
            }
        }
        return a.Length.CompareTo(b.Length);
    }
}

namespace ComicOrganizer.App.Core;

public enum FolderRole
{
    Unknown,
    Library,    // 庫的 root (= AppConfig.RootDirectory)
    Category,   // 分類目錄 (例: 不常看 / 推薦 / 語言)
    Author,     // 作者目錄
    Work,       // 作品 (含 image 的 leaf folder 或 .zip / .cbz 檔)
}

/// <summary>
/// Bottom-up 偵測資料夾在漫畫庫中的角色。
///
/// 規則：
///   leaf = 含 image 的資料夾 / .zip / .cbz → Work
///   父節點全是 Work / Archive → Author
///   父節點全是 Author → Category（除非自身是 RootDirectory，那是 Library）
///   父節點含 Category → Category（再上一層）
///
/// 取樣：當資料夾 children 很多時只取前 N 個判定，避免大目錄全掃。
/// </summary>
public class FolderRoleDetector
{
    private readonly string _libraryRoot;
    private readonly Dictionary<string, FolderRole> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };
    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".cbz" };

    /// <summary>取樣子目錄上限 — 取前 N 個 detect 角色推父節點，避免大目錄全掃。</summary>
    private const int SampleChildCount = 5;

    /// <summary>含 N 張以上 image 才視為 leaf（chapter / 純圖作品）；少於此數可能是 cover.jpg 不算 leaf。</summary>
    private const int LeafImageThreshold = 5;

    public FolderRoleDetector(string libraryRoot)
    {
        _libraryRoot = Normalize(libraryRoot);
    }

    public FolderRole Detect(string path)
    {
        path = Normalize(path);
        if (_cache.TryGetValue(path, out var cached)) return cached;

        var role = DetectInternal(path);
        _cache[path] = role;
        return role;
    }

    private FolderRole DetectInternal(string path)
    {
        // Library root: 路徑相等直接判定
        if (string.Equals(path, _libraryRoot, StringComparison.OrdinalIgnoreCase))
            return FolderRole.Library;

        // 檔案：image 不會被當作 detect 對象，archive 視為 Work
        if (File.Exists(path))
        {
            string ext = Path.GetExtension(path);
            if (ArchiveExts.Contains(ext)) return FolderRole.Work;
            if (ImageExts.Contains(ext)) return FolderRole.Work;   // 單張圖也算 work
            return FolderRole.Unknown;
        }

        if (!Directory.Exists(path)) return FolderRole.Unknown;

        // 列 children
        string[] files;
        string[] subdirs;
        try
        {
            files = Directory.GetFiles(path);
            subdirs = Directory.GetDirectories(path);
        }
        catch
        {
            return FolderRole.Unknown;
        }

        var imageFiles = files.Where(f => ImageExts.Contains(Path.GetExtension(f))).ToList();
        var archiveFiles = files.Where(f => ArchiveExts.Contains(Path.GetExtension(f))).ToList();
        int imageCount = imageFiles.Count;
        int archiveCount = archiveFiles.Count;
        int subdirCount = subdirs.Length;

        // === Leaf 判定（嚴格）===
        // (a) 大量 image (≥5) 且看起來像章節頁 → Work
        // (b) 少量 image 但沒其他子目錄/壓縮檔 → Work (single short)
        // 否則 → 不是 leaf，看 children 推
        bool looksLikeChapter = imageCount >= LeafImageThreshold && LooksLikeNumberedPages(imageFiles);
        bool isPureImageLeaf = imageCount > 0 && subdirCount == 0 && archiveCount == 0;

        if (looksLikeChapter || isPureImageLeaf) return FolderRole.Work;

        // === 含 archive 的特殊處理 ===
        // archive 不論有沒有 image (cover.jpg) 也算 Author 的 work 容器
        if (archiveCount > 0 && subdirCount == 0) return FolderRole.Author;

        // === 沒 images 沒 archives 沒 subdirs → 空目錄 ===
        if (subdirCount == 0 && archiveCount == 0 && imageCount == 0) return FolderRole.Unknown;

        // === 取樣子目錄角色 ===
        var childRoles = subdirs.Take(SampleChildCount).Select(Detect).ToList();
        if (archiveCount > 0) childRoles.Add(FolderRole.Work);  // 含 archive 也是 Work child

        if (childRoles.Count == 0) return FolderRole.Unknown;

        // 子全是 Work → 自己是 Author
        if (childRoles.All(r => r == FolderRole.Work)) return FolderRole.Author;
        // 子全是 Author → 自己是 Category
        if (childRoles.All(r => r == FolderRole.Author)) return FolderRole.Category;
        // 子含 Category → 自己也是 Category（多層分類）
        if (childRoles.Any(r => r == FolderRole.Category)) return FolderRole.Category;
        // 混合 Work/Author → 偏 Author
        if (childRoles.All(r => r == FolderRole.Work || r == FolderRole.Author)) return FolderRole.Author;

        return FolderRole.Unknown;
    }

    /// <summary>
    /// 判斷一組 image 檔名是否看起來像「章節頁面」(01.jpg, 02.jpg, page_001.jpg)。
    /// 多數命名是純數字 / 含數字後綴 → 章節頁；多數含奇怪字母 (cover, banner) → 不是。
    /// </summary>
    private static bool LooksLikeNumberedPages(List<string> imageFiles)
    {
        if (imageFiles.Count == 0) return false;
        int numericLike = imageFiles.Count(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            // 純數字 / 含主要為數字 (e.g. p001, 01_a)
            return name.Length > 0 && name.Any(char.IsDigit) &&
                   !name.Equals("cover", StringComparison.OrdinalIgnoreCase) &&
                   !name.StartsWith("banner", StringComparison.OrdinalIgnoreCase);
        });
        return numericLike >= imageFiles.Count / 2;   // 過半像數字 → 是 chapter
    }

    private static string Normalize(string p) =>
        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

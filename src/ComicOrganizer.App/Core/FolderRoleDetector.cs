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

        bool hasImages = files.Any(f => ImageExts.Contains(Path.GetExtension(f)));
        bool hasArchives = files.Any(f => ArchiveExts.Contains(Path.GetExtension(f)));

        // === Leaf 判定 ===
        // 含直接 images → 自己是 Work (chapter folder 或 single work folder)
        if (hasImages) return FolderRole.Work;

        // === 含 archive 的特殊處理 ===
        // 全是壓縮檔（每個 zip = 一個 Work）→ 自己是 Author
        if (hasArchives && subdirs.Length == 0) return FolderRole.Author;

        // === 純子目錄（含 mixed archive + subdir）→ 看子節點推 ===
        if (subdirs.Length == 0)
        {
            // 沒 images / 沒 archives / 沒子目錄 → 空目錄
            return FolderRole.Unknown;
        }

        // 取樣子目錄角色
        var childRoles = subdirs.Take(SampleChildCount).Select(Detect).ToList();
        // 加 archive 子節點也是 Work，計入 sample
        if (hasArchives) childRoles.Add(FolderRole.Work);

        // 多數決：
        // 子全是 Work → 自己是 Author
        if (childRoles.All(r => r == FolderRole.Work)) return FolderRole.Author;
        // 子全是 Author → 自己是 Category
        if (childRoles.All(r => r == FolderRole.Author)) return FolderRole.Category;
        // 子含 Category → 自己也是 Category（多層分類）
        if (childRoles.Any(r => r == FolderRole.Category)) return FolderRole.Category;
        // 混合 Work/Author → 偏 Author（單一作者的 page 與 章節 zip 並存）
        if (childRoles.All(r => r == FolderRole.Work || r == FolderRole.Author)) return FolderRole.Author;

        return FolderRole.Unknown;
    }

    private static string Normalize(string p) =>
        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

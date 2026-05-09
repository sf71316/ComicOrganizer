using System.Text.RegularExpressions;

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
/// Bottom-up 偵測資料夾在漫畫庫中的角色。從 leaf 往上推，不預設深度，
/// 能容忍各種庫結構（不常看/作者/作品、語言/作者/作品、直接 作者/作品 …）。
///
/// === 判斷策略（四層）===
///
/// 1. Fast path（不看子內容直接定）
///    path == _libraryRoot                              → Library
///    File: .zip/.cbz/.rar/.cbr/.7z                     → Work
///    File: .jpg/.png/.webp/...                         → Work (單張圖)
///
/// 2. Leaf 判定（看自己的檔案）
///    imageCount >= 5 且過半符合 PageNameRules          → Work (章節資料夾)
///    imageCount > 0 且無子目錄、無 archive             → Work (single short)
///    archive > 0 且無子目錄                            → Author (一堆 zip 容器)
///    全空                                              → Unknown
///
/// 3. 子目錄全是同類（取樣前 SampleChildCount 個遞迴 Detect）
///    全 Work (單一 child) :  名字像 Work title → Work；否則 Author
///    全 Work (多個 child) :  名字像 Series    → Category
///                            名字像 Work title → Category (Series 沒抓到的容錯)
///                            否則              → Author
///    全 Author             :  Category
///    全 Category           :  用 grandchild 探測 — 含 Author → Category；否則 → Author
///    含 Author             :  Category (多作者容器)
///    Work + Category 混合  :  Author (作者 + 一些 Series 合集)
///
/// 4. Tie-breaker（宣告式 regex 陣列，不早 return — 必須等子分析完才用名字）
///    SeriesNameRules : \d+集全 / 全\d+[巻卷] / 完結篇 / Vol.X-Vol.Y / 合集|総集編|総集
///    WorkTitleRules  : (C\d{2,3}) / 同人誌/成年コミック / 漢化|中国翻訳|DL版 /
///                      第N話|Vol.N|Ch.N / 6-8 位日期數字 / 多個 []
///
/// === 關鍵設計 ===
/// - Cache：_cache dict 防止重複 Detect 同路徑
/// - 取樣：SampleChildCount=5，避免大目錄全掃
/// - Leaf 門檻：LeafImageThreshold=5（少於此數可能是 cover.jpg 不算 leaf）
/// - 不早 return：例如「総集編」名字含 Series 信號，但若底下只是頁圖仍是 Work
/// - Grandchild 探測：區分「Author 底下的 Series 合集」vs「Category 底下的多作者」
/// </summary>
public class FolderRoleDetector
{
    private readonly string _libraryRoot;
    private readonly Dictionary<string, FolderRole> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ImageExts = SupportedExtensions.Image;
    private static readonly HashSet<string> ArchiveExts = SupportedExtensions.Archive;

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

        var folderName = Path.GetFileName(path);

        // 注意：LooksLikeSeries / LooksLikeWorkTitle 都是 tie-breaker，
        // 不在此處直接 return — 必須等子目錄分析完才能判斷。
        // (e.g. "[作者] 総集編" 名字含 Series 信號但底下只有頁圖 → 仍是 Work)

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

        // 子全是 Work → 看資料夾名 + 子數量決定是 Work / Author / Category
        if (childRoles.All(r => r == FolderRole.Work))
        {
            int childWorkCount = subdirCount + (archiveCount > 0 ? 1 : 0);

            // 名字明確是 Series 容器（X集全 / 全X巻 / 完結 / 合集 / Vol.X-Y）→ Category，children 是獨立卷
            // 此判定優先於後續 Work checks，因為 children 即使叫 Vol1/Vol2 也是獨立 work
            if (childWorkCount > 1 && LooksLikeSeries(folderName)) return FolderRole.Category;

            // 名字像 Work title (含 Comic Market 編號 / 翻譯標記 / 章節標記 / "[Author] Title"
            // / 多重方括號) → 自己是 Work-with-chapters。children 不論名字怎麼變（Spring/Summer/
            // Ch1/序章/上篇）都視為章節
            if (LooksLikeWorkTitle(folderName)) return FolderRole.Work;

            // child folders 名字像章節 (Ch1/Ch2/第N話/Vol.N/純數字) → 即使父名沒線索也判 Work
            var childNames = subdirs.Take(SampleChildCount).Select(Path.GetFileName).Where(n => n != null).Cast<string>().ToList();
            if (subdirCount > 0 && LooksLikeChapterFolders(childNames)) return FolderRole.Work;

            // 單一 Work child + 父名無 Work 線索 → Author with single work
            if (childWorkCount <= 1) return FolderRole.Author;

            // 多個 Work children + 父名無 Work 線索 → Author with multiple works
            return FolderRole.Author;
        }
        // 子全是 Author → 自己是 Category (e.g., "不常看")
        if (childRoles.All(r => r == FolderRole.Author)) return FolderRole.Category;
        // 子全是 Category → 看 Category 的內涵
        // 包 Authors 的 Category (語言/中文/作者) → 自己是上層 Category
        // 包 Works 的 Category (Cuvie/NIGHTMARE 6集全) → 自己是 Author
        if (childRoles.All(r => r == FolderRole.Category))
        {
            bool containsAuthors = subdirs.Take(SampleChildCount).Any(CategoryContainsAuthors);
            return containsAuthors ? FolderRole.Category : FolderRole.Author;
        }
        // 子含 Author → 多作者容器 → Category (Library / 真 Category)
        if (childRoles.Any(r => r == FolderRole.Author)) return FolderRole.Category;
        // 剩下：Work + Category 混合 (即 Author 含一些單作品 + 一些 Series 合集)
        // 例：Cuvie/{ NIGHTMARE 6集全 (Category), 其他作品.zip (Work) }
        return FolderRole.Author;
    }

    /// <summary>遞迴判斷 Category 是否包含 Author（區分「真 Category」vs「Series under Author」）。</summary>
    private bool CategoryContainsAuthors(string categoryPath)
    {
        try
        {
            var grandSubs = Directory.GetDirectories(categoryPath).Take(SampleChildCount);
            foreach (var gs in grandSubs)
            {
                var role = Detect(gs);
                if (role == FolderRole.Author) return true;
                if (role == FolderRole.Category && CategoryContainsAuthors(gs)) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Series（合集 / 系列）判定規則 — 命中任一 → folder 是「多 Work 集合」應為 Category。
    /// 即使 folder 名有 [作者]/Comic Market 編號等 Work 信號，含 Series 標記也優先判 Category。
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] SeriesNameRules =
    {
        ("VolumesComplete", new Regex(@"\d+集全", RegexOptions.Compiled)),                       // 6集全
        ("AllVolumes",      new Regex(@"全\d+[巻卷]", RegexOptions.Compiled)),                    // 全6巻
        ("Completed",       new Regex(@"完結篇?", RegexOptions.Compiled)),
        ("VolumeRange",     new Regex(@"Vol\.\s*\d+\s*[-－~～]\s*Vol\.\s*\d+",
                                      RegexOptions.Compiled | RegexOptions.IgnoreCase)),           // Vol.1-Vol.5
        ("Anthology",       new Regex(@"合集|総集編|総集", RegexOptions.Compiled)),
    };

    /// <summary>
    /// Work title 判定規則 — folder name 命中任一條 → 視為 Work。
    /// 規則 (Name, Regex)：未來可拉到 AppConfig 讓使用者自訂。
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] WorkTitleRules =
    {
        // Comic Market 編號 (C99 / C103)
        ("ComicMarket",      new Regex(@"\(C\d{2,3}\)", RegexOptions.Compiled)),
        // 商品類型標記
        ("DoujinshiTag",     new Regex(@"\((?:同人誌|成年コミック|商業誌)\)", RegexOptions.Compiled)),
        // 翻譯 / 漢化 / 版本標記
        ("TranslationTag",   new Regex(@"漢化|中国翻訳|中譯|DL版|無修正|個人漢化", RegexOptions.Compiled)),
        // 章節編號標記
        ("ChapterMarker",    new Regex(@"第\d+話|Vol\.\s*\d+|Ch\.\s*\d+",
                                      RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        // 6-8 位數字 (日期 yyyymmdd 之類)
        ("DateLikeNumber",   new Regex(@"\d{6,8}", RegexOptions.Compiled)),
        // 多個 [] (≥2)：Author 通常只 1 個 [作者]；2+ 通常是 Work
        ("MultipleBrackets", new Regex(@"\[[^\]]*\][^[]*\[[^\]]*\]", RegexOptions.Compiled)),
        // "[Author] Title" 樣式 — 單一方括號開頭 + 之後有文字
        // Author folder 通常是純 [Name]；有後綴內容多半是 Work title
        ("AuthorPrefixedTitle", new Regex(@"^\[[^\]]+\][\s\(\-_].*\S", RegexOptions.Compiled)),
    };

    /// <summary>
    /// 章節「資料夾」名判定規則 — child folder name 命中任一條 → 父層是 Work-with-chapters 而非 Author。
    /// 區別 Page (image 檔名) 與 Chapter (子目錄名)：兩者語意不同，分開規則。
    /// </summary>
    private static readonly Regex[] ChapterFolderRules =
    {
        new(@"^Ch(?:apter)?[\s._\-]*\d+",   RegexOptions.Compiled | RegexOptions.IgnoreCase),     // Ch1, Ch.2, Chapter 3, Ch_4
        new(@"^第\d+(?:話|章|回)",            RegexOptions.Compiled),                                // 第1話 / 第2章
        new(@"^Vol(?:ume)?[\s._\-]*\d+",     RegexOptions.Compiled | RegexOptions.IgnoreCase),     // Vol.1, Volume 2
        new(@"^Part[\s._\-]*\d+",            RegexOptions.Compiled | RegexOptions.IgnoreCase),     // Part 1
        new(@"^\d+$",                        RegexOptions.Compiled),                                // 純數字資料夾（01 / 002）
        new(@"^\d+[\s._\-]",                 RegexOptions.Compiled),                                // "01 - 序章" / "02_本篇"
    };

    /// <summary>章節頁面檔名判定規則 — image 檔名命中任一條 → 視為章節頁。</summary>
    private static readonly Regex[] PageNameRules =
    {
        new(@"^\d+$",         RegexOptions.Compiled),                                        // 純數字: 01, 002
        new(@"^p_?\d+",       RegexOptions.Compiled | RegexOptions.IgnoreCase),              // p01, p_001
        new(@"^page_?\d+",    RegexOptions.Compiled | RegexOptions.IgnoreCase),              // page1, page_001
        new(@"^\d+[_\-]",     RegexOptions.Compiled),                                        // 01_a, 02-b
        new(@"^第\d+頁",       RegexOptions.Compiled),
    };

    /// <summary>排除清單：明顯不是頁碼的檔名 (不算章節 page 數)。</summary>
    private static readonly Regex[] NotPageRules =
    {
        new(@"^cover\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^banner\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^thumb\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^logo\b",   RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    /// <summary>命中 WorkTitleRules 任一條 → 是 Work title。</summary>
    private static bool LooksLikeWorkTitle(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return false;
        return WorkTitleRules.Any(r => r.Pattern.IsMatch(folderName));
    }

    /// <summary>命中 SeriesNameRules 任一條 → folder 是合集 / 系列 (Category)。</summary>
    private static bool LooksLikeSeries(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return false;
        return SeriesNameRules.Any(r => r.Pattern.IsMatch(folderName));
    }

    /// <summary>過半 child folder 名字像章節 → 自己是 Work-with-chapters。</summary>
    private static bool LooksLikeChapterFolders(IReadOnlyList<string> childFolderNames)
    {
        if (childFolderNames.Count == 0) return false;
        int matched = childFolderNames.Count(name =>
            !string.IsNullOrEmpty(name) && ChapterFolderRules.Any(r => r.IsMatch(name)));
        // 嚴格半數以上（避免 1/2 個誤判）— 特例：只有 1 個 child 時要求命中
        if (childFolderNames.Count == 1) return matched == 1;
        return matched * 2 > childFolderNames.Count;
    }

    /// <summary>過半 image 命中 PageNameRules 且不被 NotPageRules 排除 → 是 chapter folder。</summary>
    private static bool LooksLikeNumberedPages(List<string> imageFiles)
    {
        if (imageFiles.Count == 0) return false;
        int matched = imageFiles.Count(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (string.IsNullOrEmpty(name)) return false;
            if (NotPageRules.Any(r => r.IsMatch(name))) return false;
            return PageNameRules.Any(r => r.IsMatch(name));
        });
        return matched >= imageFiles.Count / 2;
    }

    private static string Normalize(string p) =>
        p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

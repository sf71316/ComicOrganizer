using ComicOrganizer.App.Core;
using Xunit;
using Xunit.Abstractions;

namespace ComicOrganizer.Tests;

/// <summary>
/// 對真實漫畫庫跑 FolderRoleDetector，列印 tree 結構 + 抽出作者/作品。
/// 修改 LibraryRoot 為自己的庫路徑。路徑不存在時 test 跳過、不會 fail。
/// </summary>
public class FolderRoleAnalysisTest
{
    // ★ 改成你的庫路徑
    private const string LibraryRoot = @"E:\Project\ComicOrganizer\TestData\Library";

    /// <summary>tree 最大深度，避免過深掃描。</summary>
    private const int MaxDepth = 5;

    /// <summary>每層子目錄最多列幾個（避免一個 Category 1000 個 author 全印）。</summary>
    private const int MaxEntriesPerLevel = 100;

    private readonly ITestOutputHelper _output;

    public FolderRoleAnalysisTest(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AnalyzeRealLibrary()
    {
        if (!Directory.Exists(LibraryRoot))
        {
            _output.WriteLine($"[skip] LibraryRoot not found: {LibraryRoot}");
            _output.WriteLine($"  改 FolderRoleAnalysisTest.LibraryRoot 為你的庫路徑後重跑");
            return;
        }

        var detector = new FolderRoleDetector(LibraryRoot);
        var stats = new RoleStats();

        _output.WriteLine($"=== 漫畫庫角色分析: {LibraryRoot} ===");
        _output.WriteLine("");
        Walk(LibraryRoot, detector, depth: 0, stats);
        _output.WriteLine("");
        _output.WriteLine($"=== 統計 ===");
        _output.WriteLine($"Library : {stats.Library}");
        _output.WriteLine($"Category: {stats.Category}");
        _output.WriteLine($"Author  : {stats.Author}");
        _output.WriteLine($"Work    : {stats.Work}");
        _output.WriteLine($"Unknown : {stats.Unknown}");
        _output.WriteLine("");
        _output.WriteLine($"=== Top 30 偵測出的作者 ===");
        foreach (var name in stats.AuthorNames.Take(30))
        {
            _output.WriteLine($"  {name}");
        }
    }

    private void Walk(string path, FolderRoleDetector detector, int depth, RoleStats stats)
    {
        if (depth > MaxDepth) return;

        var role = detector.Detect(path);
        var indent = new string(' ', depth * 2);
        var displayName = depth == 0 ? path : Path.GetFileName(path);

        // 抽 metadata
        string? extracted = role switch
        {
            FolderRole.Author => ExtractAuthorName(displayName),
            FolderRole.Work => ExtractWorkTitle(displayName),
            _ => null,
        };
        var extra = extracted != null && extracted != displayName ? $"  → {extracted}" : "";

        _output.WriteLine($"{indent}[{role,-8}] {displayName}{extra}");

        // 統計
        switch (role)
        {
            case FolderRole.Library: stats.Library++; break;
            case FolderRole.Category: stats.Category++; break;
            case FolderRole.Author:
                stats.Author++;
                if (extracted != null) stats.AuthorNames.Add(extracted);
                break;
            case FolderRole.Work: stats.Work++; break;
            case FolderRole.Unknown: stats.Unknown++; break;
        }

        // 只在 Library/Category/Author 繼續往下走（Work 通常是 leaf 不展開）
        if (role != FolderRole.Library && role != FolderRole.Category && role != FolderRole.Author) return;

        if (!Directory.Exists(path)) return;

        // 列子目錄
        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.GetDirectories(path)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntriesPerLevel);
        }
        catch { return; }

        foreach (var sub in subdirs) Walk(sub, detector, depth + 1, stats);

        // Author 目錄要額外列直接放的檔案（zip/cbz/pdf/長檔名被截斷的檔等）
        // 放寬條件：不靠副檔名 (有些檔名末段被截斷沒 `.`，Path.GetExtension 抓不到)
        // 過濾 cover.* / banner.* / 系統檔 / 隱藏檔
        if (role == FolderRole.Author)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(path)
                    .Where(f =>
                    {
                        var fn = Path.GetFileName(f).ToLowerInvariant();
                        if (string.IsNullOrEmpty(fn)) return false;
                        if (fn.StartsWith(".")) return false;                  // 系統檔 .DS_Store
                        if (fn == "thumbs.db" || fn == "desktop.ini") return false;
                        // 跳 cover.* / banner.*
                        var stem = Path.GetFileNameWithoutExtension(fn);
                        if (stem == "cover" || stem.StartsWith("banner") || stem.StartsWith("thumb")) return false;
                        // 過濾單純圖檔 (Author dir 直接放的圖通常是 cover/banner)
                        var ext = Path.GetExtension(fn);
                        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".webp" || ext == ".bmp" || ext == ".gif")
                            return false;
                        return true;
                    })
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxEntriesPerLevel);
            }
            catch { return; }

            foreach (var f in files) Walk(f, detector, depth + 1, stats);
        }
    }

    /// <summary>
    /// 從 author folder name 抽出主名 + 別名（雙語名常見：日文+羅馬拼音）：
    ///   "タイガー[Tiger Roman]"   → main="タイガー",        alias="Tiger Roman"
    ///   "Digital Lover (なかじまゆか)" → main="Digital Lover", alias="なかじまゆか"
    ///   "[Group (Author)]"        → main="Group",          alias="Author"
    ///   "[Flugel]"                → main="Flugel",         alias=null
    ///   "[小林優子]"               → main="小林優子",        alias=null
    ///   "Flugel"                  → main="Flugel",         alias=null
    /// 顯示用 "main / alias" 字串：tracker / parser 後續可拆兩欄存 DB
    /// </summary>
    private static string ExtractAuthorName(string folderName)
    {
        var (main, alias) = ExtractAuthorMainAndAlias(folderName);
        return alias != null ? $"{main} / {alias}" : main;
    }

    private static (string Main, string? Alias) ExtractAuthorMainAndAlias(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return (folderName, null);
        var s = folderName.Trim();

        // 1. "前綴 [後綴]" — 雙語名典型
        var m1 = System.Text.RegularExpressions.Regex.Match(s, @"^(.+?)\s*\[([^\]]+)\]\s*$");
        if (m1.Success)
        {
            var prefix = m1.Groups[1].Value.Trim();
            var bracket = m1.Groups[2].Value.Trim();
            // bracket 內含 (xxx) 視為 group 內 author
            var p1 = bracket.IndexOf('(');
            var p2 = bracket.LastIndexOf(')');
            if (p1 >= 0 && p2 > p1)
            {
                bracket = bracket.Substring(p1 + 1, p2 - p1 - 1).Trim();
            }
            // prefix 是空 → 純 [xxx] 格式 (沒前綴)
            if (string.IsNullOrEmpty(prefix)) return (bracket, null);
            return (prefix, bracket);
        }

        // 2. "Group (Author)" 純小括號格式
        var m2 = System.Text.RegularExpressions.Regex.Match(s, @"^(.+?)\s*\(([^()]+)\)\s*$");
        if (m2.Success)
        {
            return (m2.Groups[1].Value.Trim(), m2.Groups[2].Value.Trim());
        }

        // 3. 純 [Single]
        var m3 = System.Text.RegularExpressions.Regex.Match(s, @"^\[([^\]]+)\]$");
        if (m3.Success) return (m3.Groups[1].Value.Trim(), null);

        // 4. 純名稱
        return (s, null);
    }

    /// <summary>
    /// 從 work folder name 抽出作品名（簡單規則 — 移除前置 [作者] 與後綴 tags）：
    ///   "[Flugel] くぎづけはーと [中文] [DL版]" → "くぎづけはーと"
    ///   "(C75) [Digital Lover] D.L.action 45 (xxx)" → "D.L.action 45"
    ///   "作品1" → "作品1"
    /// </summary>
    private static string ExtractWorkTitle(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return folderName;

        // 砍 .zip / .cbz / .pdf 副檔名 (不能用 Path.GetFileNameWithoutExtension —
        // 它會把 "D.L.action" 的最後一個 . 當副檔名分隔，砍掉 .action)
        var name = folderName;
        foreach (var ext in new[] { ".zip", ".cbz", ".pdf" })
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^ext.Length];
                break;
            }
        }

        // 去 (Comic Market 編號) 之類
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\(C\d{2,3}\)", "").Trim();

        // 去前置 [Author]、[Group (Author)]
        name = System.Text.RegularExpressions.Regex.Replace(name, @"^\s*\[[^\]]*\]\s*", "");

        // 去後續 [tag] [tag] ...
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\[[^\]]*\]\s*", " ").Trim();

        // 去後續 (xxx) tags
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^\)]*\)\s*", " ").Trim();

        return string.IsNullOrWhiteSpace(name) ? folderName : name;
    }

    private class RoleStats
    {
        public int Library, Category, Author, Work, Unknown;
        public HashSet<string> AuthorNames { get; } = new();
    }
}

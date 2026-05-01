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

        IEnumerable<string> children;
        try
        {
            children = Directory.GetDirectories(path)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .Take(MaxEntriesPerLevel);
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            Walk(child, detector, depth + 1, stats);
        }
    }

    /// <summary>
    /// 從 author folder name 抽出作者名（簡單規則）：
    ///   "[Flugel]" → "Flugel"
    ///   "[小林優子]" → "小林優子"
    ///   "Flugel" → "Flugel" (沒括號就原樣)
    ///   "[Group (Author)]" → "Author" (社團/作者格式)
    /// </summary>
    private static string ExtractAuthorName(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return folderName;

        // 抓最外層 [...]
        int lb = folderName.IndexOf('[');
        int rb = folderName.IndexOf(']');
        if (lb >= 0 && rb > lb)
        {
            var inside = folderName.Substring(lb + 1, rb - lb - 1).Trim();
            // 含小括號 → 取括號內 (Author)
            int p1 = inside.IndexOf('(');
            int p2 = inside.IndexOf(')');
            if (p1 >= 0 && p2 > p1)
            {
                return inside.Substring(p1 + 1, p2 - p1 - 1).Trim();
            }
            return inside;
        }
        return folderName;
    }

    /// <summary>
    /// 從 work folder name 抽出作品名（簡單規則 — 移除前置 [作者] 與後綴 tags）：
    ///   "[Flugel] くぎづけはーと [中文] [DL版]" → "くぎづけはーと"
    ///   "作品1" → "作品1"
    /// </summary>
    private static string ExtractWorkTitle(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return folderName;

        // 砍 .zip / .cbz 副檔名
        var name = Path.GetFileNameWithoutExtension(folderName);

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

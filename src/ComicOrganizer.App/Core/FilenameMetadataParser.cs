using System.Text.RegularExpressions;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 從漫畫資料夾名 / 檔名抽 metadata 的純函式 parser。無 IO、可單元測試。
///
/// === 處理對象（一般同人誌命名慣例）===
/// 作者目錄名：
///   "Flugel"                          → main=Flugel
///   "[小林優子]"                       → main=小林優子
///   "タイガー[Tiger Roman]"             → main=タイガー, alias=Tiger Roman (日文+羅馬拼音)
///   "Digital Lover (なかじまゆか)"      → main=Digital Lover, alias=なかじまゆか
///   "[Group (Author)]"                → main=Group, alias=Author
///
/// 作品檔名/資料夾名：
///   "(C99) [Flugel] くぎづけはーと [中国翻訳][DL版].zip"
///     → event=(C99), author=Flugel, title=くぎづけはーと, lang=zh-CN, tags=[DL版]
///
///   "[Digital Lover (なかじまゆか)] D.L.action 45 (Various) [中国翻訳].zip"
///     → author=Digital Lover (alias なかじまゆか), title=D.L.action 45,
///       parody=Various, lang=zh-CN
///
/// 解析失敗時保留原檔名為 Title，其他欄位 null。
/// </summary>
public static class FilenameMetadataParser
{
    // 副檔名 — 跟 FolderRoleDetector.ArchiveExts 對齊
    private static readonly Regex ExtensionRegex = new(
        @"\.(zip|cbz|cbr|rar|7z|pdf|jpg|jpeg|png|webp|bmp|gif)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 開頭的 (CXX) Comic Market 編號與常見同人展會
    private static readonly Regex EventRegex = new(
        @"^\s*\((C\d{2,3}|COMIC[^)]*|COMITIA[^)]*|例大祭[^)]*|東方[^)]*|紅楼夢[^)]*|紅樓夢[^)]*|サンクリ[^)]*|SC\d{2,4}|FF\d{2,3}|PF\d{2,3}|こみトレ[^)]*)\)\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 第一個 [...] 視為 author block
    private static readonly Regex FirstBracketRegex = new(
        @"^\s*\[([^\]]+)\]\s*",
        RegexOptions.Compiled);

    // "Group (Author)" 拆分；要求小括號收尾
    private static readonly Regex GroupArtistRegex = new(
        @"^(.+?)\s*\(([^()]+)\)\s*$",
        RegexOptions.Compiled);

    // 章節編號
    private static readonly Regex ChapterRegex = new(
        @"(第\d+話|Vol\.\s*\d+|Ch\.\s*\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 通用 [...] 與 (...) 抓取
    private static readonly Regex AnyBracketRegex = new(@"\[([^\]]+)\]", RegexOptions.Compiled);
    private static readonly Regex AnyParenRegex = new(@"\(([^()]+)\)", RegexOptions.Compiled);

    // 語言對應表（命中即設 Language，不再進 Tags）
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "中国翻訳", "zh-CN" },
        { "簡体中国語訳", "zh-CN" },
        { "簡体", "zh-CN" },
        { "简体", "zh-CN" },
        { "簡中", "zh-CN" },
        { "簡體中文", "zh-CN" },
        { "简体中文", "zh-CN" },
        { "繁体中国語訳", "zh-TW" },
        { "繁體", "zh-TW" },
        { "繁体", "zh-TW" },
        { "中譯", "zh-TW" },
        { "中國翻譯", "zh-TW" },
        { "繁中", "zh-TW" },
        { "繁體中文", "zh-TW" },
        { "中文", "zh" },
        { "中国語", "zh" },
        { "中國語", "zh" },
        { "日本語", "ja" },
        { "English", "en" },
        { "英訳", "en" },
        { "Korean", "ko" },
        { "韓国語訳", "ko" },
        { "ロシア語訳", "ru" },
    };

    // 翻譯組辨識：含這些關鍵字 → 視為 TranslationGroup
    private static readonly string[] TranslationGroupKeywords = { "漢化", "翻訳", "Translation", "汉化" };

    /// <summary>
    /// 解析「作者目錄名」抽出 main + alias。同 FolderRoleAnalysisTest.ExtractAuthorMainAndAlias 邏輯。
    /// </summary>
    public static (string Main, string? Alias) ParseAuthorFolderName(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return (folderName, null);
        var s = folderName.Trim();

        // 1. "前綴 [後綴]" — 雙語 / 帶括號內藝術家
        var m1 = Regex.Match(s, @"^(.+?)\s*\[([^\]]+)\]\s*$");
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
            if (string.IsNullOrEmpty(prefix)) return (bracket, null);
            return (prefix, bracket);
        }

        // 2. "Group (Author)" 純括號格式
        var m2 = GroupArtistRegex.Match(s);
        if (m2.Success)
        {
            return (m2.Groups[1].Value.Trim(), m2.Groups[2].Value.Trim());
        }

        // 3. 純 [...]：可能是 [Single] 或 [Group (Author)]
        var m3 = Regex.Match(s, @"^\[([^\]]+)\]$");
        if (m3.Success)
        {
            var inside = m3.Groups[1].Value.Trim();
            // 內部含 "Group (Author)" → 拆 main/alias
            var ga = GroupArtistRegex.Match(inside);
            if (ga.Success)
            {
                return (ga.Groups[1].Value.Trim(), ga.Groups[2].Value.Trim());
            }
            return (inside, null);
        }

        // 4. 純名稱
        return (s, null);
    }

    /// <summary>
    /// 判斷一個 bracket 內容是否就是 author 名（用來跳過重複 / dedupe）。
    /// 命中條件：完全相等 OR 以 author 名開頭後跟空格/小括號（例：「Cuvie (Cuvie)」）。
    /// </summary>
    private static bool IsAuthorBracket(string content, string author)
    {
        if (string.IsNullOrEmpty(author)) return false;
        if (content.Equals(author, StringComparison.OrdinalIgnoreCase)) return true;
        if (content.Length > author.Length &&
            content.StartsWith(author, StringComparison.OrdinalIgnoreCase) &&
            (content[author.Length] == ' ' || content[author.Length] == '('))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 解析「作品檔名 / 資料夾名」抽 metadata。
    /// </summary>
    /// <param name="name">原始檔名或目錄名（可含副檔名）</param>
    /// <param name="authorFromParent">由父目錄帶下來的 author 主名（可空）。有值就跳過 filename author 解析。</param>
    public static ParsedMetadata ParseWorkName(string name, string? authorFromParent = null)
    {
        var result = new ParsedMetadata();
        if (string.IsNullOrEmpty(name)) return result;

        var s = name.Trim();

        // 1. 砍副檔名
        s = ExtensionRegex.Replace(s, "");

        // 2. 開頭 (CXX) → event tag
        var ev = EventRegex.Match(s);
        if (ev.Success)
        {
            result.Tags.Add(new TagEntry(ev.Groups[1].Value.Trim(), "event"));
            s = s.Substring(ev.Length);
        }
        s = s.Trim();

        // 3. 第一個 [...] 視為 author block（除非父目錄已給 author）
        if (authorFromParent == null)
        {
            var ab = FirstBracketRegex.Match(s);
            if (ab.Success)
            {
                var inside = ab.Groups[1].Value.Trim();
                var ga = GroupArtistRegex.Match(inside);
                if (ga.Success)
                {
                    // "Group (Author)" — Group 為主、Author 為 alias（同人誌慣例）
                    result.AuthorMainName = ga.Groups[1].Value.Trim();
                    result.AuthorAlias = ga.Groups[2].Value.Trim();
                }
                else
                {
                    result.AuthorMainName = inside;
                }
                s = s.Substring(ab.Length).Trim();
            }
        }
        else
        {
            // 父目錄已給 author。filename 開頭的 [...] 僅當「跟 authorFromParent 同名」才砍
            // 不能無腦砍：開頭可能是 [中文]、[DL版] 之類非 author bracket
            result.AuthorMainName = authorFromParent;
            var ab = FirstBracketRegex.Match(s);
            if (ab.Success && IsAuthorBracket(ab.Groups[1].Value.Trim(), authorFromParent))
            {
                s = s.Substring(ab.Length).Trim();
            }
        }

        // 4. 抽章節編號（Vol.3 / 第5話 / Ch.10）— 不從 s 移除，留在 title
        var ch = ChapterRegex.Match(s);
        if (ch.Success) result.ChapterNo = ch.Groups[1].Value.Trim();

        // 5. 抽所有 [...] tags — 分流到 Language / TranslationGroup / Tags
        foreach (Match m in AnyBracketRegex.Matches(s))
        {
            var content = m.Groups[1].Value.Trim();
            // 跟 author 同名的 bracket 跳過 — 避免 author 又被當 generic tag
            if (!string.IsNullOrEmpty(result.AuthorMainName) && IsAuthorBracket(content, result.AuthorMainName))
            {
                continue;
            }
            if (LanguageMap.TryGetValue(content, out var lang))
            {
                result.Language = lang;
                continue;
            }
            if (TranslationGroupKeywords.Any(kw => content.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                result.TranslationGroup = content;
                continue;
            }
            result.Tags.Add(new TagEntry(content, null));
        }
        s = AnyBracketRegex.Replace(s, " ").Trim();

        // 6. 抽 (...) — 通常是 parody 或原作標題
        foreach (Match m in AnyParenRegex.Matches(s))
        {
            var content = m.Groups[1].Value.Trim();
            result.Tags.Add(new TagEntry(content, "parody"));
        }
        s = AnyParenRegex.Replace(s, " ").Trim();

        // 7. 剩下的多餘空白壓縮 → title
        s = Regex.Replace(s, @"\s+", " ").Trim();
        result.Title = string.IsNullOrEmpty(s) ? null : s;

        return result;
    }
}

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
        @"^(C\d{2,3}|COMIC[^)]*|COMITIA[^)]*|例大祭[^)]*|東方[^)]*|紅楼夢[^)]*|紅樓夢[^)]*|サンクリ[^)]*|SC\d{2,4}|FF\d{2,3}|PF\d{2,3}|こみトレ[^)]*|砲雷撃戦[^)]*|歌姫庭園[^)]*|ぷにケット[^)]*|ふたけっと[^)]*|エアコミケ[^)]*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 前導單一 (...) 或 [...] 比對
    private static readonly Regex LeadingParenRegex = new(@"^\s*\(([^()]+)\)\s*", RegexOptions.Compiled);

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

    // 日文/CJK 原作副標（如 `艦隊これくしょん-艦これ-` → `艦隊これくしょん -艦これ-`）
    private static readonly Regex CjkTrailingSubtitleDashRegex = new(
        @"(?<=[^\x00-\x7F\s])(-[^-\s]+-)\s*$",
        RegexOptions.Compiled);

    // 出版形式詞（出現在 (...) 時屬於分類標記而非原作 parody）
    private static readonly HashSet<string> FormatTypeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "同人誌", "同人志", "成年コミック", "一般コミック", "商業誌", "商業志", "成年漫画",
        "同人CG集", "18禁ゲームCG", "成年コミック・雑誌", "成年コミック．雑誌", "成年コミック☆雑誌",
        "雑誌寄せ集め", "汉化同人誌", "成年____",
    };

    // 非作者身份的常見版本/規格方括號詞（避免 authorFromParent 時被誤當作社團名剝除）
    private static readonly HashSet<string> NonAuthorSquareTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "DL版", "Digital", "無修正", "无修正", "修正", "Uncensored", "Decensored",
        "AI Generated", "RAW", "Full Color", "全彩", "カラー", "Sample",
        "無碼", "无码", "薄碼", "薄码", "疏碼", "疏码", "有修正", "無修", "无修",
        "騎兵", "步兵", "カラー化", "単行本", "单行本", "單行本", "見本", "有水印",
        "去碼", "去码", "機翻", "机翻", "AI翻譯", "AI翻译", "進行中", "进行中",
        "完結", "完结", "連載中", "连载中", "整合", "合集", "Hcomic",
    };

    // 語言對應表（命中即設 Language，不再進 Tags）
    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "中国翻訳", "zh-CN" },
        { "中国翻译", "zh-CN" },
        { "簡体中国語訳", "zh-CN" },
        { "簡体", "zh-CN" },
        { "简体", "zh-CN" },
        { "簡中", "zh-CN" },
        { "简中", "zh-CN" },
        { "簡體中文", "zh-CN" },
        { "简体中文", "zh-CN" },
        { "繁体中国語訳", "zh-TW" },
        { "繁體", "zh-TW" },
        { "繁体", "zh-TW" },
        { "中譯", "zh-TW" },
        { "中國翻譯", "zh-TW" },
        { "中國翻訳", "zh-TW" },
        { "繁中", "zh-TW" },
        { "繁體中文", "zh-TW" },
        { "中文", "zh" },
        { "中文翻譯", "zh" },
        { "日翻中", "zh" },
        { "中文化", "zh" },
        { "中漫", "zh" },
        { "中文版", "zh" },
        { "中国語", "zh" },
        { "中國語", "zh" },
        { "Chinese", "zh" },
        { "CH", "zh" },
        { "日本語", "ja" },
        { "JP", "ja" },
        { "English", "en" },
        { "ENG", "en" },
        { "英訳", "en" },
        { "Korean", "ko" },
        { "韓国語訳", "ko" },
        { "ロシア語訳", "ru" },
    };

    // 翻譯組辨識：含這些關鍵字 → 視為 TranslationGroup
    private static readonly string[] TranslationGroupKeywords =
    {
        "漢化", "翻訳", "Translation", "汉化",
        "家族社", "宇宙分組", "宇宙分组", "漢化社", "汉化社",
        "翻譯組", "翻译组", "翻譯社", "翻译社", "漢化部", "汉化部",
        "掃圖", "扫图", "Scan by", "Scanned by",
        "重嵌", "嵌字", "修圖", "修图", "校對", "校对",
        "工坊", "工房", "個人翻譯", "个人翻译", "禁漫天堂",
        "機翻", "机翻",
    };

    // 雜訊方括號規則：純頁碼/編號、單字母、版本號、日期戳與漢化編號（不寫入 Tags）
    private static readonly Regex NoiseDigitsRegex = new(@"^\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex NoiseSingleLetterRegex = new(@"^[A-Za-z]$", RegexOptions.Compiled);
    private static readonly Regex NoiseVersionRegex = new(@"^v\d+(?:\.\d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NoiseDateRegex = new(@"^\d{4}[-/.年]\d{1,2}(?:[-/.月]\d{1,2}日?)?$", RegexOptions.Compiled);
    private static readonly Regex NoiseReleaseCodeRegex = new(@"^[A-Za-z]{2,5}-\d{2,4}-[A-Za-z0-9]+$", RegexOptions.Compiled);

    // 雜誌刊號與圓括號雜訊（不寫入 parody）
    private static readonly Regex MagazineYearMonthIssueRegex = new(@"\d{4}\s*年\s*\d{1,2}\s*月[号號]", RegexOptions.Compiled);
    private static readonly Regex MagazineComicPrefixRegex = new(@"^COM[Il1]C\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MagazineKnownNamesRegex = new(
        @"^(?:コミック|WEEKLY\s*快楽天|WEEKLY\s*Kairakuten|快楽天|異世快楽天|漫画ばんがいち|アナンガ・ランガ)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ParenUpdateDateRegex = new(@"^\d{4}年\d{1,2}月\d{1,2}日更新$", RegexOptions.Compiled);
    private static readonly Regex ParenCensorChapterRegex = new(@"^\d+話有修正$", RegexOptions.Compiled);

    /// <summary>
    /// 解析「作者目錄名」抽出 main + alias。同 FolderRoleAnalysisTest.ExtractAuthorMainAndAlias 邏輯。
    /// </summary>
    public static (string Main, string? Alias) ParseAuthorFolderName(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return (folderName, null);
        var s = folderName.Trim().Normalize(System.Text.NormalizationForm.FormC);

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

    private static bool IsEventToken(string content)
        => !string.IsNullOrWhiteSpace(content) && EventRegex.IsMatch(content.Trim());

    private static bool IsTranslationGroup(string content)
        => !string.IsNullOrWhiteSpace(content)
        && TranslationGroupKeywords.Any(kw => content.Contains(kw, StringComparison.OrdinalIgnoreCase));

    private static bool IsNoiseSquareTag(string content)
    {
        var c = content.Trim();
        if (string.IsNullOrEmpty(c)) return true;
        return NoiseDigitsRegex.IsMatch(c)
            || NoiseSingleLetterRegex.IsMatch(c)
            || NoiseVersionRegex.IsMatch(c)
            || NoiseDateRegex.IsMatch(c)
            || NoiseReleaseCodeRegex.IsMatch(c);
    }

    private static bool IsMagazineOrNoiseParen(string content)
    {
        var c = content.Trim();
        if (string.IsNullOrEmpty(c)) return true;
        return MagazineYearMonthIssueRegex.IsMatch(c)
            || MagazineComicPrefixRegex.IsMatch(c)
            || MagazineKnownNamesRegex.IsMatch(c)
            || ParenUpdateDateRegex.IsMatch(c)
            || ParenCensorChapterRegex.IsMatch(c)
            || NoiseDigitsRegex.IsMatch(c);
    }

    private static bool HasRemainingTitleText(string rest)
    {
        if (string.IsNullOrWhiteSpace(rest)) return false;
        var stripped = AnyBracketRegex.Replace(rest, " ");
        stripped = AnyParenRegex.Replace(stripped, " ");
        return !string.IsNullOrWhiteSpace(stripped);
    }

    private static string NormalizeParodyName(string content)
    {
        var s = Regex.Replace(content.Trim(), @"\s+", " ");
        s = CjkTrailingSubtitleDashRegex.Replace(s, " $1");
        if (Regex.IsMatch(s, @"^Fate\s*Grand\s*Order$", RegexOptions.IgnoreCase))
            return "Fate Grand Order";
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>
    /// 判斷一個 bracket 內容是否就是 author / circle 名（用來跳過重複 / dedupe）。
    /// 命中條件：完全相等、以 author 開頭後跟空格/小括號、或拆出 Group (Artist) 後任一相符。
    /// </summary>
    private static bool IsAuthorBracket(string content, string author, string? alias = null)
    {
        if (string.IsNullOrEmpty(author)) return false;
        if (MatchesAuthorToken(content, author)) return true;
        if (!string.IsNullOrEmpty(alias) && MatchesAuthorToken(content, alias)) return true;

        var ga = GroupArtistRegex.Match(content.Trim());
        if (ga.Success)
        {
            var g = ga.Groups[1].Value.Trim();
            var a = ga.Groups[2].Value.Trim();
            if (MatchesAuthorToken(g, author) || MatchesAuthorToken(a, author)) return true;
            if (!string.IsNullOrEmpty(alias) && (MatchesAuthorToken(g, alias) || MatchesAuthorToken(a, alias))) return true;
        }
        return false;
    }

    private static bool MatchesAuthorToken(string candidate, string target)
    {
        if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase)) return true;
        if (candidate.Length > target.Length &&
            candidate.StartsWith(target, StringComparison.OrdinalIgnoreCase) &&
            (candidate[target.Length] == ' ' || candidate[target.Length] == '('))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 解析「作品檔名 / 資料夾名」抽 metadata。
    /// </summary>
    /// <param name="name">原始檔名或目錄名（可含副檔名）</param>
    /// <param name="authorFromParent">由父目錄帶下來的 author 主名（可空）。有值時作為主作者並仍正確識別前導社團/作者方括號。</param>
    public static ParsedMetadata ParseWorkName(string name, string? authorFromParent = null)
    {
        var result = new ParsedMetadata();
        if (string.IsNullOrEmpty(name)) return result;

        var s = name.Trim().Normalize(System.Text.NormalizationForm.FormC);
        if (authorFromParent != null)
            authorFromParent = authorFromParent.Trim().Normalize(System.Text.NormalizationForm.FormC);

        // 1. 砍副檔名
        s = ExtensionRegex.Replace(s, "");

        // 2. 剝除開頭的出版形式詞 / 展會 (CXX) / 雜誌刊號 / 前置 [規格/語言/漢化組]
        // （依使用者設定：出版形式詞、展會場次與規格詞僅剝除以還原純標題，不寫入 Tags）
        while (!string.IsNullOrWhiteSpace(s))
        {
            var pm = LeadingParenRegex.Match(s);
            if (pm.Success)
            {
                var inner = pm.Groups[1].Value.Trim();
                if (IsEventToken(inner) || FormatTypeWords.Contains(inner) || IsMagazineOrNoiseParen(inner))
                {
                    s = s.Substring(pm.Length).Trim();
                    continue;
                }
                if (LanguageMap.TryGetValue(inner, out var pLang))
                {
                    result.Language ??= pLang;
                    s = s.Substring(pm.Length).Trim();
                    continue;
                }
                break;
            }

            var bm = FirstBracketRegex.Match(s);
            if (bm.Success)
            {
                var inner = bm.Groups[1].Value.Trim();
                if (LanguageMap.TryGetValue(inner, out var bLang))
                {
                    result.Language ??= bLang;
                    s = s.Substring(bm.Length).Trim();
                    continue;
                }
                if (NonAuthorSquareTags.Contains(inner)
                    || FormatTypeWords.Contains(inner)
                    || IsEventToken(inner)
                    || IsNoiseSquareTag(inner))
                {
                    s = s.Substring(bm.Length).Trim();
                    continue;
                }
                if (IsTranslationGroup(inner))
                {
                    result.TranslationGroup ??= inner;
                    s = s.Substring(bm.Length).Trim();
                    continue;
                }
            }
            break;
        }
        s = s.Trim();

        // 3. 第一個 [...] 視為 author / circle block（除非是版本規格詞如 [DL版]）
        if (authorFromParent == null)
        {
            var ab = FirstBracketRegex.Match(s);
            if (ab.Success)
            {
                var inside = ab.Groups[1].Value.Trim();
                if (!NonAuthorSquareTags.Contains(inside))
                {
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
        }
        else
        {
            // 父目錄已給 author。前導 [...] 若是同名/別名，或屬於「非語言/非漢化/非規格詞」且後面仍有標題文字，
            // 視為作品檔名自帶的 [社團 (作者)] / [筆名] 標記，取出別名並移除，避免被誤當成一般 Tag。
            result.AuthorMainName = authorFromParent;
            var ab = FirstBracketRegex.Match(s);
            if (ab.Success)
            {
                var inside = ab.Groups[1].Value.Trim();
                bool isDupAuthor = IsAuthorBracket(inside, authorFromParent);
                bool isCircleAuthorBlock = !isDupAuthor
                    && !LanguageMap.ContainsKey(inside)
                    && !NonAuthorSquareTags.Contains(inside)
                    && !FormatTypeWords.Contains(inside)
                    && !IsEventToken(inside)
                    && !IsNoiseSquareTag(inside)
                    && !IsTranslationGroup(inside)
                    && HasRemainingTitleText(s.Substring(ab.Length));

                if (isDupAuthor || isCircleAuthorBlock)
                {
                    var ga = GroupArtistRegex.Match(inside);
                    if (ga.Success)
                    {
                        var grp = ga.Groups[1].Value.Trim();
                        var art = ga.Groups[2].Value.Trim();
                        if (!art.Equals(authorFromParent, StringComparison.OrdinalIgnoreCase))
                            result.AuthorAlias ??= art;
                        if (!grp.Equals(authorFromParent, StringComparison.OrdinalIgnoreCase))
                            result.AuthorAlias ??= grp;
                    }
                    else if (!inside.Equals(authorFromParent, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AuthorAlias ??= inside;
                    }
                    s = s.Substring(ab.Length).Trim();
                }
            }
        }

        // 4. 抽章節編號（Vol.3 / 第5話 / Ch.10）— 不從 s 移除，留在 title
        var ch = ChapterRegex.Match(s);
        if (ch.Success) result.ChapterNo = ch.Groups[1].Value.Trim();

        // 5. 抽所有 [...] tags — 分流到 Language / Noise / TranslationGroup / Tags
        foreach (Match m in AnyBracketRegex.Matches(s))
        {
            var content = m.Groups[1].Value.Trim();
            // 跟 author / alias 同名的 bracket 跳過 — 避免 author 又被當 generic tag
            if (!string.IsNullOrEmpty(result.AuthorMainName)
                && IsAuthorBracket(content, result.AuthorMainName, result.AuthorAlias))
            {
                continue;
            }
            if (LanguageMap.TryGetValue(content, out var lang))
            {
                result.Language ??= lang;
                continue;
            }
            if (NonAuthorSquareTags.Contains(content)
                || FormatTypeWords.Contains(content)
                || IsEventToken(content)
                || IsNoiseSquareTag(content))
            {
                continue;
            }
            if (IsTranslationGroup(content))
            {
                result.TranslationGroup ??= content;
                continue;
            }
            result.Tags.Add(new TagEntry(content, null));
        }
        s = AnyBracketRegex.Replace(s, " ").Trim();

        // 6. 抽 (...) — 排除語言、出版形式詞、展會場次、雜誌刊號與規格詞，其餘正規化後歸為 parody
        foreach (Match m in AnyParenRegex.Matches(s))
        {
            var content = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(content)) continue;

            if (LanguageMap.TryGetValue(content, out var pLang))
            {
                result.Language ??= pLang;
                continue;
            }
            if (FormatTypeWords.Contains(content)
                || IsEventToken(content)
                || NonAuthorSquareTags.Contains(content)
                || IsMagazineOrNoiseParen(content))
            {
                continue;
            }

            var normalizedParody = NormalizeParodyName(content);
            if (!string.IsNullOrEmpty(normalizedParody))
            {
                result.Tags.Add(new TagEntry(normalizedParody, "parody"));
            }
        }
        s = AnyParenRegex.Replace(s, " ").Trim();

        // 7. 剩下的多餘空白壓縮 → title
        s = Regex.Replace(s, @"\s+", " ").Trim();
        result.Title = string.IsNullOrEmpty(s) ? null : s;

        return result;
    }
}

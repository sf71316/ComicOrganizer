using FuzzySharp;
using ComicOrganizer.App.Models;
using System.Text.RegularExpressions;

namespace ComicOrganizer.App.Core;

public class ArchiveOrganizer
{
    private static readonly Regex BalancedBracketRegex = new(
        @"\[([^\]]+)\]|\(([^()]+)\)|\{([^\}]+)\}",
        RegexOptions.Compiled);

    private static readonly Regex CompactStripRegex = new(
        @"[\s\.\-・_･'""`~!@#$%^&*+=,、。]+",
        RegexOptions.Compiled);

    // 切分多作者合作分隔符（故意不將英文字母 'x' 與單獨空白當作無條件切詞符）
    private static readonly Regex AuthorMultiSplitRegex = new(
        @"\s*(?:[×・、/|／\\]|(?<=\s)[-xX](?=\s))\s*",
        RegexOptions.Compiled);

    private static readonly string[] BuiltInNoiseTokens =
    {
        "ai", "dl", "ch", "en", "jp", "ko", "zh", "raw", "v2", "v3",
        "no", "in", "on", "of", "the", "and", "to", "ru",
        "comiket", "sample", "digital", "patreon", "skeb", "pixiv", "fanbox", "fantia"
    };

    private readonly MappingIntelligence _intelligence;
    private readonly AppConfig _config;

    // 目標目錄清單相同時重用預解析別名與碰撞表，避免每次掃描重算
    private IReadOnlyList<string>? _cachedTargetInput;
    private List<TargetEntity>? _cachedTargets;
    private Dictionary<string, int>? _cachedWordCollisions;

    public ArchiveOrganizer(MappingIntelligence intelligence, AppConfig config)
    {
        _intelligence = intelligence;
        _config = config;
    }

    public MatchResult? FindBestMatch(string sourceName, List<string> targetFolderNames)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || targetFolderNames.Count == 0)
        {
            return null;
        }

        // 1. 知識庫預判（MappingIntelligence 已具備詞界與短雜訊防呆，支援相對子分類路徑與簡繁／日漢字對等）
        var learnedFolder = _intelligence.TryGetMappedFolder(sourceName);
        if (learnedFolder != null)
        {
            var matchedTarget = targetFolderNames.FirstOrDefault(
                t => string.Equals(t, learnedFolder, StringComparison.OrdinalIgnoreCase));

            if (matchedTarget == null)
            {
                string learnedLeaf = FilenameMetadataParser.ExtractLeafFolderName(learnedFolder);
                string learnedLeafCanon = CanonicalLower(learnedLeaf);
                var leafMatches = targetFolderNames.Where(t =>
                {
                    string tLeaf = FilenameMetadataParser.ExtractLeafFolderName(t);
                    return string.Equals(tLeaf, learnedLeaf, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrEmpty(learnedLeafCanon) &&
                            string.Equals(CanonicalLower(tLeaf), learnedLeafCanon, StringComparison.Ordinal));
                }).ToList();

                if (leafMatches.Count == 1)
                {
                    matchedTarget = leafMatches[0];
                }
            }

            if (matchedTarget != null)
            {
                return new MatchResult
                {
                    FolderName = matchedTarget,
                    Confidence = 100.0,
                    IsLearned = true,
                    MatchSourceSummary = "知識庫"
                };
            }
        }

        // 2. 結構化解析來源作者／標題 ＋ 目標庫別名展開與碰撞表
        var (targets, wordCollisions) = GetOrBuildTargetEntities(targetFolderNames);
        var sourceInfo = ParseSourceInfo(sourceName);

        TargetEntity? bestTarget = null;
        double bestScore = -1.0;
        string bestReason = string.Empty;
        double secondBestScore = -1.0;

        foreach (var target in targets)
        {
            var (score, reason) = ScoreTarget(sourceInfo, target, wordCollisions);
            if (score > bestScore)
            {
                secondBestScore = bestScore;
                bestScore = score;
                bestTarget = target;
                bestReason = reason;
            }
            else if (score > secondBestScore)
            {
                secondBestScore = score;
            }
        }

        // 若出現非精確匹配的模糊平手／極接近歧義，避免任意被字典序第一個目標吸走
        if (bestTarget != null && bestScore < 90.0 && secondBestScore >= bestScore - 1.5 && secondBestScore > 0)
        {
            bestScore = Math.Min(bestScore, 70.0);
        }

        double minThreshold = Math.Clamp(_config.MinMatchConfidence, 0.0, 100.0);
        if (bestTarget == null || bestScore < minThreshold)
        {
            return null;
        }

        return new MatchResult
        {
            FolderName = bestTarget.FolderName,
            Confidence = Math.Round(bestScore, 1),
            IsLearned = false,
            MatchSourceSummary = bestReason
        };
    }

    /// <summary>
    /// 從來源名稱擷取可用於學習或比對的安全關鍵字：
    /// 先檢查完整括號是否屬於黑名單／雜訊再視需要拆多作者，絕不再先用空白或 'x' 切碎片語。
    /// </summary>
    public List<string> ExtractAllKeywords(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new List<string>();
        }

        var normalized = FilenameMetadataParser.NormalizeMatchText(input);
        var keywords = new List<string>();

        foreach (Match m in BalancedBracketRegex.Matches(normalized))
        {
            string rawBlock = m.Groups[1].Success ? m.Groups[1].Value
                : (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            string block = rawBlock.Trim();
            if (string.IsNullOrEmpty(block) || IsNoise(block))
            {
                continue;
            }

            // 支援 "[Group (Author)]" 拆解後保留群組名與作者名
            var ga = Regex.Match(block, @"^(.+?)\s*\(([^()]+)\)\s*$");
            if (ga.Success)
            {
                AddCleanAuthorTokens(ga.Groups[1].Value, keywords);
                AddCleanAuthorTokens(ga.Groups[2].Value, keywords);
            }
            else
            {
                AddCleanAuthorTokens(block, keywords);
            }
        }

        return keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 抽取「僅來自作者／社團欄」的關鍵字，供歸檔成功後安全寫回 mappings.json。
    /// </summary>
    public List<string> ExtractAuthorKeywordsForLearning(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return new List<string>();
        }

        var info = ParseSourceInfo(sourceName);
        var result = new List<string>();
        foreach (var cand in info.AuthorCandidates)
        {
            if (cand.Value.Length >= 2 && !IsNoise(cand.Value))
            {
                result.Add(cand.Value);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool IsKeywordValidForTarget(string kw, string targetFolderName)
    {
        if (string.IsNullOrWhiteSpace(kw) || IsNoise(kw))
        {
            return false;
        }

        string cleanKw = FilenameMetadataParser.NormalizeMatchText(kw);
        if (cleanKw.Length < 2)
        {
            return false;
        }

        bool kwIsAscii = IsAsciiOnly(cleanKw);
        string kwCanonical = CanonicalLower(cleanKw);
        string compactKw = Compact(cleanKw);
        string compactKwCanon = Compact(kwCanonical);
        var aliases = FilenameMetadataParser.ExpandAuthorFolderAliases(targetFolderName);
        foreach (var alias in aliases)
        {
            if (string.Equals(cleanKw, alias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string aliasCanonical = CanonicalLower(alias);
            if (!string.IsNullOrEmpty(kwCanonical) && string.Equals(kwCanonical, aliasCanonical, StringComparison.Ordinal))
            {
                return true;
            }

            string compactAlias = Compact(alias);
            if (compactKw.Length >= 3 && string.Equals(compactKw, compactAlias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string compactAliasCanon = Compact(aliasCanonical);
            if (compactKwCanon.Length >= 3 && string.Equals(compactKwCanon, compactAliasCanon, StringComparison.Ordinal))
            {
                return true;
            }

            // 純 ASCII 短詞（< 4 字元）不開放部分包含，避免如 "ai" 驗證過關
            if (kwIsAscii && cleanKw.Length < 4)
            {
                continue;
            }

            if (cleanKw.Length >= 4 && alias.Length >= 4)
            {
                if (IsWordBoundaryContained(alias, cleanKw) ||
                    IsWordBoundaryContained(cleanKw, alias) ||
                    IsWordBoundaryContained(aliasCanonical, kwCanonical) ||
                    IsWordBoundaryContained(kwCanonical, aliasCanonical))
                {
                    return true;
                }

                if (ComputeAuthorFuzzyScore(cleanKw, alias) >= 85.0 ||
                    ComputeAuthorFuzzyScore(kwCanonical, aliasCanonical) >= 85.0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void AddCleanAuthorTokens(string segment, List<string> targetList)
    {
        var trimmed = segment.Trim();
        if (string.IsNullOrEmpty(trimmed) || IsNoise(trimmed))
        {
            return;
        }

        foreach (var part in AuthorMultiSplitRegex.Split(trimmed))
        {
            var p = part.Trim();
            if (p.Length < 2 || IsNoise(p))
            {
                continue;
            }

            targetList.Add(p);
        }
    }

    private (List<TargetEntity> Targets, Dictionary<string, int> WordCollisions) GetOrBuildTargetEntities(List<string> targetFolderNames)
    {
        if (_cachedTargetInput != null
            && _cachedTargets != null
            && _cachedWordCollisions != null
            && ReferenceEquals(_cachedTargetInput, targetFolderNames))
        {
            return (_cachedTargets, _cachedWordCollisions);
        }

        var targets = new List<TargetEntity>(targetFolderNames.Count);
        var collisions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in targetFolderNames)
        {
            var rawAliases = FilenameMetadataParser.ExpandAuthorFolderAliases(folder);
            var aliasEntries = new List<AliasEntry>(rawAliases.Count);
            var seenTokensForThisTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in rawAliases)
            {
                var lower = raw.ToLowerInvariant();
                var compact = Compact(lower);
                var canonicalLower = CanonicalLower(raw);
                var canonicalCompact = Compact(canonicalLower);
                var words = SplitSpaceTokens(lower);
                aliasEntries.Add(new AliasEntry(raw, lower, compact, canonicalLower, canonicalCompact, words));

                foreach (var w in words)
                {
                    if (w.Length >= 2)
                    {
                        seenTokensForThisTarget.Add(w);
                    }
                }
            }

            foreach (var tok in seenTokensForThisTarget)
            {
                collisions[tok] = collisions.TryGetValue(tok, out int count) ? count + 1 : 1;
            }

            targets.Add(new TargetEntity(folder, aliasEntries));
        }

        _cachedTargetInput = targetFolderNames;
        _cachedTargets = targets;
        _cachedWordCollisions = collisions;
        return (targets, collisions);
    }

    private SourceMatchInfo ParseSourceInfo(string sourceName)
    {
        var normalized = FilenameMetadataParser.NormalizeMatchText(sourceName);
        var parsed = FilenameMetadataParser.ParseWorkName(normalized);

        var candidates = new List<SourceAuthorCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddAuthor(string? raw, bool isSplitSub)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var clean = raw.Trim();
            if (clean.Length < 2 || IsNoise(clean))
            {
                return;
            }

            if (seen.Add(clean))
            {
                string lower = clean.ToLowerInvariant();
                string canonicalLower = CanonicalLower(clean);
                candidates.Add(new SourceAuthorCandidate(
                    clean,
                    lower,
                    Compact(lower),
                    canonicalLower,
                    Compact(canonicalLower),
                    isSplitSub));
            }
        }

        void ExpandAuthorField(string? field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return;
            }

            var f = field.Trim();
            TryAddAuthor(f, isSplitSub: false);

            foreach (var sub in AuthorMultiSplitRegex.Split(f))
            {
                var s = sub.Trim();
                if (!string.Equals(s, f, StringComparison.OrdinalIgnoreCase))
                {
                    TryAddAuthor(s, isSplitSub: true);
                }
            }
        }

        ExpandAuthorField(parsed.AuthorMainName);
        ExpandAuthorField(parsed.AuthorAlias);

        // 若檔名沒有領頭 [...] 作者欄但含有 "(Author)" 等少見非黑名單括號，不把最後的純文字標題直接冒充作者
        string titleLower = string.IsNullOrWhiteSpace(parsed.Title) ? string.Empty : parsed.Title.ToLowerInvariant();
        string titleCompact = string.IsNullOrEmpty(titleLower) ? string.Empty : Compact(titleLower);
        string titleCanonicalLower = string.IsNullOrWhiteSpace(parsed.Title) ? string.Empty : CanonicalLower(parsed.Title);
        string titleCanonicalCompact = string.IsNullOrEmpty(titleCanonicalLower) ? string.Empty : Compact(titleCanonicalLower);

        return new SourceMatchInfo(
            candidates,
            parsed.Title ?? string.Empty,
            titleLower,
            titleCompact,
            titleCanonicalLower,
            titleCanonicalCompact);
    }

    private (double Score, string Reason) ScoreTarget(
        SourceMatchInfo source,
        TargetEntity target,
        Dictionary<string, int> wordCollisions)
    {
        double bestScore = 0.0;
        string bestReason = string.Empty;

        // Track A：作者／社團結構化欄位比對（最高精準度）
        foreach (var cand in source.AuthorCandidates)
        {
            foreach (var alias in target.Aliases)
            {
                // A1. 完全一致、簡繁／日漢字對等或去標點空白一致（支援 [ae]、[MM]、Pure Tone vs PureTone、尾崎未来 vs 尾崎未來）
                if (string.Equals(cand.Lower, alias.Lower, StringComparison.Ordinal))
                {
                    double score = cand.IsSplitSub ? 95.0 : 98.0;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestReason = cand.IsSplitSub ? "多作者吻合" : "作者精確吻合";
                    }
                    continue;
                }

                if (string.Equals(cand.CanonicalLower, alias.CanonicalLower, StringComparison.Ordinal))
                {
                    double score = cand.IsSplitSub ? 94.5 : 97.5;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestReason = cand.IsSplitSub ? "多作者字形對等吻合" : "作者字形對等吻合";
                    }
                    continue;
                }

                if (cand.Compact.Length >= 2 && alias.Compact.Length >= 2 &&
                    string.Equals(cand.Compact, alias.Compact, StringComparison.Ordinal))
                {
                    double score = cand.IsSplitSub ? 93.0 : 96.0;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestReason = "作者正規化吻合";
                    }
                    continue;
                }

                if (cand.CanonicalCompact.Length >= 2 && alias.CanonicalCompact.Length >= 2 &&
                    string.Equals(cand.CanonicalCompact, alias.CanonicalCompact, StringComparison.Ordinal))
                {
                    double score = cand.IsSplitSub ? 92.5 : 95.5;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestReason = "作者字形正規化吻合";
                    }
                    continue;
                }

                // 短名稱（<= 3 字元）僅允許精確吻合，硬性否決部分包含與模糊猜測
                int minLen = Math.Min(cand.Lower.Length, alias.Lower.Length);
                if (minLen < 4)
                {
                    continue;
                }

                // A2. 詞界完整詞命中（例如 [Marvel] 命中 "Marvel Comics"）
                if (IsWordBoundaryTokenMatch(cand.Lower, alias.Lower) ||
                    IsWordBoundaryTokenMatch(cand.CanonicalLower, alias.CanonicalLower))
                {
                    int colCount = wordCollisions.TryGetValue(cand.Lower, out int c1) ? c1 : 1;
                    int colAlias = wordCollisions.TryGetValue(alias.Lower, out int c2) ? c2 : 1;
                    int maxCol = Math.Max(colCount, colAlias);
                    double score = maxCol > 1 ? 82.0 : 90.0;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestReason = maxCol > 1 ? "作者部分詞(多目標碰撞)" : "作者詞界吻合";
                    }
                    continue;
                }

                // A3. 非空白分隔長 CJK 子字串包含：僅允許長度比例 >= 0.65（防止 2~3 字短詞吞掉長名）
                if (cand.Lower.Length >= 4 && alias.Lower.Length >= 4)
                {
                    int maxLen = Math.Max(cand.Lower.Length, alias.Lower.Length);
                    double lenRatio = (double)minLen / maxLen;
                    if (lenRatio >= 0.65 &&
                        (cand.Lower.Contains(alias.Lower, StringComparison.Ordinal) ||
                         alias.Lower.Contains(cand.Lower, StringComparison.Ordinal) ||
                         cand.CanonicalLower.Contains(alias.CanonicalLower, StringComparison.Ordinal) ||
                         alias.CanonicalLower.Contains(cand.CanonicalLower, StringComparison.Ordinal)))
                    {
                        double score = 88.0;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestReason = "作者高比例吻合";
                        }
                        continue;
                    }
                }

                // A4. 欄位級混合模糊比對（Jaro-Winkler + Bigram Dice + 長度比懲罰）
                double fuzzy = Math.Max(
                    ComputeAuthorFuzzyScore(cand.Lower, alias.Lower),
                    ComputeAuthorFuzzyScore(cand.CanonicalLower, alias.CanonicalLower));
                if (fuzzy > bestScore)
                {
                    bestScore = fuzzy;
                    bestReason = "作者近似比對";
                }
            }
        }

        // Track B：標題／系列名目錄比對（相容以「鬼滅の刃」、「One Punch Man (ワンパンマン)」分類的作品集）
        if (!string.IsNullOrEmpty(source.TitleLower))
        {
            foreach (var alias in target.Aliases)
            {
                bool aliasIsAscii = IsAsciiOnly(alias.Lower);
                int minAllowedTitleAlias = aliasIsAscii ? 4 : 3;
                if (alias.Lower.Length < minAllowedTitleAlias)
                {
                    continue;
                }

                if (string.Equals(source.TitleLower, alias.Lower, StringComparison.Ordinal) ||
                    string.Equals(source.TitleCanonicalLower, alias.CanonicalLower, StringComparison.Ordinal) ||
                    (source.TitleCompact.Length >= 4 &&
                     (string.Equals(source.TitleCompact, alias.Compact, StringComparison.Ordinal) ||
                      string.Equals(source.TitleCanonicalCompact, alias.CanonicalCompact, StringComparison.Ordinal))))
                {
                    if (95.0 > bestScore)
                    {
                        bestScore = 95.0;
                        bestReason = "作品標題吻合";
                    }
                    continue;
                }

                if (IsTitleBoundaryMatch(source.TitleLower, alias.Lower) ||
                    IsTitleBoundaryMatch(source.TitleCanonicalLower, alias.CanonicalLower))
                {
                    int colCount = wordCollisions.TryGetValue(alias.Lower, out int c) ? c : 1;
                    double score = colCount > 1 ? 78.0 : 90.0;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestReason = "作品系列詞界吻合";
                    }
                }
            }
        }

        return (bestScore, bestReason);
    }

    private static double ComputeAuthorFuzzyScore(string a, string b)
    {
        if (a.Length < 4 || b.Length < 4)
        {
            return 0.0;
        }

        int minLen = Math.Min(a.Length, b.Length);
        int maxLen = Math.Max(a.Length, b.Length);
        double lenRatio = (double)minLen / maxLen;
        if (lenRatio < 0.5)
        {
            return 0.0;
        }

        double jw = JaroWinkler(a, b);
        double dice = BigramDice(a, b);
        double combined = 0.55 * jw + 0.45 * dice;

        // 僅在字元重疊度也足夠時才轉換為分數，最高封頂 88（低於精確匹配的 95-98）
        double score = combined * (0.65 + 0.35 * lenRatio) * 89.0;
        return Math.Min(88.0, score);
    }

    private static bool IsWordBoundaryTokenMatch(string aLower, string bLower)
    {
        var shorter = aLower.Length <= bLower.Length ? aLower : bLower;
        var longer = aLower.Length <= bLower.Length ? bLower : aLower;

        if (shorter.Length == 0 || string.Equals(shorter, longer, StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = SplitSpaceTokens(longer);
        if (tokens.Count > 1 && tokens.Any(t => string.Equals(t, shorter, StringComparison.Ordinal)))
        {
            return true;
        }

        return IsWordBoundaryContained(longer, shorter);
    }

    private static bool IsTitleBoundaryMatch(string titleLower, string aliasLower)
    {
        int idx = 0;
        while (idx <= titleLower.Length - aliasLower.Length)
        {
            int pos = titleLower.IndexOf(aliasLower, idx, StringComparison.Ordinal);
            if (pos < 0)
            {
                return false;
            }

            bool leftOk = pos == 0 || !IsWordCharForBoundary(titleLower[pos - 1], aliasLower[0]);
            int end = pos + aliasLower.Length;
            bool rightOk = end >= titleLower.Length || !IsWordCharForBoundary(titleLower[end], aliasLower[^1]);

            if (leftOk && rightOk)
            {
                return true;
            }

            idx = pos + 1;
        }

        return false;
    }

    private static bool IsWordCharForBoundary(char adjacent, char edgeChar)
    {
        // ASCII 英數邊界不得相鄰字母／數字；日文／中文標題開頭或空白/標點/數字隔離時視為詞界（如 "鬼滅の刃 第01巻"）
        if (edgeChar <= 0x7F)
        {
            return char.IsLetterOrDigit(adjacent);
        }

        return char.IsLetter(adjacent);
    }

    private static bool IsWordBoundaryContained(string haystack, string needle)
    {
        int idx = 0;
        while (idx <= haystack.Length - needle.Length)
        {
            int pos = haystack.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                return false;
            }

            bool leftOk = pos == 0 || !char.IsLetterOrDigit(haystack[pos - 1]);
            int end = pos + needle.Length;
            bool rightOk = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);

            if (leftOk && rightOk)
            {
                return true;
            }

            idx = pos + 1;
        }

        return false;
    }

    private static double JaroWinkler(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        int len1 = s1.Length;
        int len2 = s2.Length;
        if (len1 == 0 || len2 == 0) return 0.0;

        int matchDistance = Math.Max(0, Math.Max(len1, len2) / 2 - 1);
        var s1Matches = new bool[len1];
        var s2Matches = new bool[len2];

        int matches = 0;
        for (int i = 0; i < len1; i++)
        {
            int start = Math.Max(0, i - matchDistance);
            int end = Math.Min(i + matchDistance + 1, len2);
            for (int j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j]) continue;
                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        double t = 0;
        int k = 0;
        for (int i = 0; i < len1; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (s1[i] != s2[k]) t++;
            k++;
        }

        double m = matches;
        double jaro = ((m / len1) + (m / len2) + ((m - t / 2.0) / m)) / 3.0;

        int prefix = 0;
        int maxPrefix = Math.Min(4, Math.Min(len1, len2));
        for (int i = 0; i < maxPrefix; i++)
        {
            if (s1[i] == s2[i]) prefix++;
            else break;
        }

        return jaro + prefix * 0.1 * (1.0 - jaro);
    }

    private static double BigramDice(string s1, string s2)
    {
        if (s1.Length < 2 || s2.Length < 2)
        {
            return s1 == s2 ? 1.0 : 0.0;
        }

        var bigrams1 = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < s1.Length - 1; i++)
        {
            var bg = s1.Substring(i, 2);
            bigrams1[bg] = bigrams1.TryGetValue(bg, out int c) ? c + 1 : 1;
        }

        int intersection = 0;
        for (int i = 0; i < s2.Length - 1; i++)
        {
            var bg = s2.Substring(i, 2);
            if (bigrams1.TryGetValue(bg, out int count) && count > 0)
            {
                intersection++;
                bigrams1[bg] = count - 1;
            }
        }

        int totalBigrams = (s1.Length - 1) + (s2.Length - 1);
        return totalBigrams == 0 ? 0.0 : (2.0 * intersection) / totalBigrams;
    }

    private static string Compact(string s)
        => CompactStripRegex.Replace(s.ToLowerInvariant(), string.Empty);

    private static List<string> SplitSpaceTokens(string s)
        => s.Split([' ', '-', '_', '.', '・', '(', ')', '[', ']', '、', '/', '／'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static bool IsAsciiOnly(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7F) return false;
        }
        return true;
    }

    private bool IsNoise(string token)
    {
        string cleanToken = FilenameMetadataParser.NormalizeMatchText(token)
            .Trim('[', ']', '(', ')', '{', '}', ' ');
        if (string.IsNullOrEmpty(cleanToken))
        {
            return true;
        }

        if (FilenameMetadataParser.IsKnownNoiseTag(cleanToken))
        {
            return true;
        }

        if (BuiltInNoiseTokens.Any(b => string.Equals(cleanToken, b, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (_config.Blacklist.Any(nw =>
            !string.IsNullOrWhiteSpace(nw)
            && (string.Equals(cleanToken, nw.Trim(), StringComparison.OrdinalIgnoreCase)
                || (nw.Trim().Length >= 4 && cleanToken.Contains(nw.Trim(), StringComparison.OrdinalIgnoreCase)))))
        {
            return true;
        }

        foreach (var pattern in _config.BlacklistPatterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern) && Regex.IsMatch(cleanToken, pattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// 依資料夾角色探勘目標庫中所有可歸檔的「作者目標資料夾」相對路徑（如 "Cuvie"、"_Legacy\尾崎未來"）。
    /// 會自動穿透 <see cref="FolderRole.Category"/> 分類容器至 <paramref name="maxDepth"/>，
    /// 停在 <see cref="FolderRole.Author"/> 並略過純作品資料夾 <see cref="FolderRole.Work"/> 與排除目錄。
    /// </summary>
    public static List<string> DiscoverTargetAuthorFolders(
        string rootDirectory,
        IReadOnlySet<string>? excludedDirectories = null,
        int maxDepth = 3)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return results;
        }

        int clampedDepth = Math.Clamp(maxDepth, 1, 10);
        var detector = new FolderRoleDetector(rootDirectory);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedExcluded = BuildNormalizedExclusionSet(excludedDirectories);

        void ScanLevel(string currentDir, int depth)
        {
            string canonicalCurrent;
            try
            {
                canonicalCurrent = Path.GetFullPath(currentDir);
            }
            catch
            {
                return;
            }

            if (!visited.Add(canonicalCurrent))
            {
                return;
            }

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch
            {
                return;
            }

            foreach (var subDir in subDirs)
            {
                string dirName = Path.GetFileName(subDir);
                if (string.IsNullOrWhiteSpace(dirName) || dirName.StartsWith('.'))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                string relPath = Path.GetRelativePath(rootDirectory, subDir);
                if (IsExcludedTargetPath(normalizedExcluded, dirName, relPath, subDir))
                {
                    continue;
                }

                var role = detector.Detect(subDir);
                switch (role)
                {
                    case FolderRole.Author:
                        results.Add(relPath);
                        break;
                    case FolderRole.Category:
                        if (depth < clampedDepth)
                        {
                            ScanLevel(subDir, depth + 1);
                        }
                        break;
                    case FolderRole.Unknown:
                        if (IsEmptyLeafDirectory(subDir))
                        {
                            results.Add(relPath);
                        }
                        break;
                    case FolderRole.Work:
                    case FolderRole.Library:
                    default:
                        break;
                }
            }
        }

        ScanLevel(rootDirectory, 1);

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, NaturalStringComparer.Instance)
            .ToList();
    }

    private static HashSet<string>? BuildNormalizedExclusionSet(IReadOnlySet<string>? excludedDirectories)
    {
        if (excludedDirectories == null || excludedDirectories.Count == 0)
        {
            return null;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in excludedDirectories)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string trimmed = raw.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            set.Add(trimmed);
            set.Add(trimmed.Replace('/', '\\'));
        }

        return set.Count > 0 ? set : null;
    }

    private static bool IsExcludedTargetPath(
        HashSet<string>? excluded,
        string dirName,
        string relativePath,
        string fullPath)
    {
        if (excluded == null)
        {
            return false;
        }

        if (excluded.Contains(dirName) ||
            excluded.Contains(relativePath) ||
            excluded.Contains(relativePath.Replace('/', '\\')))
        {
            return true;
        }

        try
        {
            string normalizedFull = Path.GetFullPath(fullPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return excluded.Contains(normalizedFull);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEmptyLeafDirectory(string dirPath)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(dirPath).Any();
        }
        catch
        {
            return false;
        }
    }

    private static string CanonicalLower(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : FilenameMetadataParser.CanonicalizeMatchText(text).ToLowerInvariant();

    /// <summary>
    /// 正規化並驗證欲建立的新作者／目標資料夾名稱；阻擋非法路徑字元、保留名與排除清單。
    /// </summary>
    public static bool TryNormalizeTargetFolderName(
        string? rawName,
        out string normalized,
        out string? errorMessage,
        IReadOnlySet<string>? excludedFolders = null)
    {
        normalized = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawName))
        {
            errorMessage = "資料夾名稱不可為空。";
            return false;
        }

        string trimmed = rawName.Trim().TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed == "." ||
            trimmed == ".." ||
            string.Equals(trimmed, "未匹配", StringComparison.Ordinal))
        {
            errorMessage = "資料夾名稱無效或為系統保留字。";
            return false;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (trimmed.IndexOfAny(invalidChars) >= 0 || trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            errorMessage = "資料夾名稱不可包含路徑分隔或系統非法字元。";
            return false;
        }

        if (WindowsReservedNames.Contains(trimmed))
        {
            errorMessage = $"「{trimmed}」為系統保留裝置名稱。";
            return false;
        }

        if (excludedFolders != null && excludedFolders.Contains(trimmed))
        {
            errorMessage = $"「{trimmed}」屬於已設定排除之目錄。";
            return false;
        }

        normalized = trimmed;
        return true;
    }

    /// <summary>
    /// 從未匹配來源名稱抽出建議的新作者資料夾名稱（保留「社團 (作者)」慣例，並排除雜訊／黑名單／既有資料夾）。
    /// </summary>
    public string? TrySuggestNewAuthorFolder(
        string sourceName,
        IReadOnlyCollection<string> existingFolders,
        IReadOnlySet<string>? excludedFolders = null)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        var parsed = FilenameMetadataParser.ParseWorkName(sourceName);
        if (string.IsNullOrWhiteSpace(parsed.AuthorMainName) || IsNoise(parsed.AuthorMainName))
        {
            return null;
        }

        bool hasValidAlias = !string.IsNullOrWhiteSpace(parsed.AuthorAlias) && !IsNoise(parsed.AuthorAlias);
        string rawSuggestion = hasValidAlias
            ? $"{parsed.AuthorMainName.Trim()} ({parsed.AuthorAlias!.Trim()})"
            : parsed.AuthorMainName.Trim();

        if (!TryNormalizeTargetFolderName(rawSuggestion, out var normalized, out _, excludedFolders))
        {
            return null;
        }

        if (existingFolders.Any(f =>
                IsSameTargetFolder(f, normalized) ||
                IsSameTargetFolder(f, parsed.AuthorMainName.Trim()) ||
                (hasValidAlias && IsSameTargetFolder(f, parsed.AuthorAlias!.Trim()))))
        {
            return null;
        }

        return normalized;
    }

    private static bool IsSameTargetFolder(string existingFolder, string candidateName)
    {
        if (string.Equals(existingFolder, candidateName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string leaf = FilenameMetadataParser.ExtractLeafFolderName(existingFolder);
        if (string.Equals(leaf, candidateName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string candidateCanon = CanonicalLower(candidateName);
        if (!string.IsNullOrEmpty(candidateCanon) &&
            string.Equals(CanonicalLower(leaf), candidateCanon, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 依查詢關鍵字對目標資料夾清單進行精確→葉目錄→簡繁日字形對等→字首→包含→FuzzySharp 別名模糊排序，供可搜尋下拉選單即時呈現。
    /// </summary>
    public static List<(string FolderName, int Score)> RankTargetFolders(
        string? query,
        IReadOnlyList<string> candidates,
        int maxResults = 30)
    {
        var result = new List<(string FolderName, int Score)>();
        if (candidates == null || candidates.Count == 0 || maxResults <= 0)
        {
            return result;
        }

        var trimmedQuery = query?.Trim();
        if (string.IsNullOrEmpty(trimmedQuery))
        {
            foreach (var folder in candidates)
            {
                if (string.IsNullOrWhiteSpace(folder) || string.Equals(folder, "未匹配", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add((folder, 0));
                if (result.Count >= maxResults)
                {
                    break;
                }
            }

            return result;
        }

        string qLower = trimmedQuery.ToLowerInvariant();
        string qCanon = CanonicalLower(trimmedQuery);
        foreach (var folder in candidates)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.Equals(folder, "未匹配", StringComparison.Ordinal))
            {
                continue;
            }

            int score = ScoreFolderCandidate(trimmedQuery, qLower, qCanon, folder);
            if (score > 0)
            {
                result.Add((folder, score));
            }
        }

        return result
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.FolderName, NaturalStringComparer.Instance)
            .Take(maxResults)
            .ToList();
    }

    private static int ScoreFolderCandidate(string query, string qLower, string qCanon, string folder)
    {
        if (string.Equals(folder, query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        string leaf = FilenameMetadataParser.ExtractLeafFolderName(folder);
        if (string.Equals(leaf, query, StringComparison.OrdinalIgnoreCase))
        {
            return 990;
        }

        string leafCanon = CanonicalLower(leaf);
        if (!string.IsNullOrEmpty(qCanon) && string.Equals(leafCanon, qCanon, StringComparison.Ordinal))
        {
            return 970;
        }

        if (leaf.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            folder.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 850;
        }

        if (!string.IsNullOrEmpty(qCanon) && leafCanon.StartsWith(qCanon, StringComparison.Ordinal))
        {
            return 830;
        }

        int leafIdx = leaf.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (leafIdx >= 0)
        {
            return Math.Max(620, 770 - Math.Min(leafIdx * 4 + leaf.Length, 140));
        }

        int leafCanonIdx = !string.IsNullOrEmpty(qCanon) ? leafCanon.IndexOf(qCanon, StringComparison.Ordinal) : -1;
        if (leafCanonIdx >= 0)
        {
            return Math.Max(610, 750 - Math.Min(leafCanonIdx * 4 + leafCanon.Length, 140));
        }

        int idx = folder.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return Math.Max(580, 720 - Math.Min(idx * 4 + folder.Length, 140));
        }

        int bestFuzzy = Math.Max(
            Fuzz.WeightedRatio(qLower, leaf.ToLowerInvariant()),
            string.IsNullOrEmpty(qCanon) ? 0 : Fuzz.WeightedRatio(qCanon, leafCanon));

        foreach (var alias in FilenameMetadataParser.ExpandAuthorFolderAliases(folder))
        {
            string aliasCanon = CanonicalLower(alias);
            if (alias.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(qCanon) && aliasCanon.StartsWith(qCanon, StringComparison.Ordinal)))
            {
                return 800;
            }

            if (alias.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(qCanon) && aliasCanon.Contains(qCanon, StringComparison.Ordinal)))
            {
                return 680;
            }

            int aliasFuzzy = Math.Max(
                Fuzz.WeightedRatio(qLower, alias.ToLowerInvariant()),
                string.IsNullOrEmpty(qCanon) ? 0 : Fuzz.WeightedRatio(qCanon, aliasCanon));
            if (aliasFuzzy > bestFuzzy)
            {
                bestFuzzy = aliasFuzzy;
            }
        }

        return bestFuzzy >= 48 ? bestFuzzy : 0;
    }

    private sealed record AliasEntry(
        string Raw,
        string Lower,
        string Compact,
        string CanonicalLower,
        string CanonicalCompact,
        List<string> SpaceTokens);
    private sealed record TargetEntity(string FolderName, List<AliasEntry> Aliases);
    private sealed record SourceAuthorCandidate(
        string Value,
        string Lower,
        string Compact,
        string CanonicalLower,
        string CanonicalCompact,
        bool IsSplitSub);
    private sealed record SourceMatchInfo(
        List<SourceAuthorCandidate> AuthorCandidates,
        string Title,
        string TitleLower,
        string TitleCompact,
        string TitleCanonicalLower,
        string TitleCanonicalCompact);
}

public class MatchResult
{
    public string FolderName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool IsLearned { get; set; }
    public string MatchSourceSummary { get; set; } = string.Empty;
}

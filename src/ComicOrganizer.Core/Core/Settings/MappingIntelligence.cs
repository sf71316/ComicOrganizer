using System.Text.Json;
using System.Text.RegularExpressions;

namespace ComicOrganizer.App.Core;

public class MappingIntelligence
{
    private static readonly Regex BracketBlockRegex = new(@"\[([^\]]+)\]|\(([^()]+)\)|\{([^\}]+)\}", RegexOptions.Compiled);

    private Dictionary<string, string> _learnedMappings = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;

    public MappingIntelligence(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings.json");
        Load();
    }

    public void Learn(string keyword, string targetFolderName)
    {
        var normalizedKey = NormalizeKey(keyword);
        if (!IsValidLearningKey(normalizedKey) || string.IsNullOrWhiteSpace(targetFolderName))
        {
            return;
        }

        if (!_learnedMappings.ContainsKey(normalizedKey) || _learnedMappings[normalizedKey] != targetFolderName)
        {
            _learnedMappings[normalizedKey] = targetFolderName;
            Save();
        }
    }

    public string? TryGetMappedFolder(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || _learnedMappings.Count == 0)
        {
            return null;
        }

        var normalizedFileName = FilenameMetadataParser.NormalizeMatchText(fileName);

        // 1. 優先比對結構化解析出的作者／別名與完整括號標記（如 [ae] 可精準命中已學習 key，避免雜訊 substring 干擾）
        var parsed = FilenameMetadataParser.ParseWorkName(normalizedFileName);
        if (!string.IsNullOrWhiteSpace(parsed.AuthorMainName))
        {
            var mainKey = NormalizeKey(parsed.AuthorMainName);
            if (!FilenameMetadataParser.IsKnownNoiseTag(mainKey) && _learnedMappings.TryGetValue(mainKey, out var mappedMain))
            {
                return mappedMain;
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.AuthorAlias))
        {
            var aliasKey = NormalizeKey(parsed.AuthorAlias);
            if (!FilenameMetadataParser.IsKnownNoiseTag(aliasKey) && _learnedMappings.TryGetValue(aliasKey, out var mappedAlias))
            {
                return mappedAlias;
            }
        }

        foreach (Match m in BracketBlockRegex.Matches(normalizedFileName))
        {
            string rawBlock = m.Groups[1].Success ? m.Groups[1].Value
                : (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            var blockKey = NormalizeKey(rawBlock);
            if (string.IsNullOrEmpty(blockKey) || FilenameMetadataParser.IsKnownNoiseTag(blockKey))
            {
                continue;
            }

            if (_learnedMappings.TryGetValue(blockKey, out var mappedBlock))
            {
                return mappedBlock;
            }
        }

        // 2. Fallback：依關鍵字長度由長到短檢查具詞界保護的子字串命中，防止 "ai" / "no" 等極短 key 誤中整個檔名
        var lowerFileName = normalizedFileName.ToLowerInvariant();
        var sortedMappings = _learnedMappings.OrderByDescending(x => x.Key.Length);

        foreach (var entry in sortedMappings)
        {
            var key = NormalizeKey(entry.Key);
            if (!CanUseSubstringFallback(key))
            {
                continue;
            }

            if (ContainsWithBoundary(lowerFileName, key))
            {
                return entry.Value;
            }
        }

        return null;
    }

    internal static string NormalizeKey(string keyword)
        => FilenameMetadataParser.NormalizeMatchText(keyword).ToLowerInvariant();

    internal static bool IsValidLearningKey(string normalizedKey)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey) || normalizedKey.Length < 2)
        {
            return false;
        }

        return !FilenameMetadataParser.IsKnownNoiseTag(normalizedKey);
    }

    private static bool CanUseSubstringFallback(string normalizedKey)
    {
        if (!IsValidLearningKey(normalizedKey))
        {
            return false;
        }

        bool asciiOnly = IsAsciiOnly(normalizedKey);
        if (asciiOnly && normalizedKey.Length < 4)
        {
            return false;
        }

        return true;
    }

    private static bool ContainsWithBoundary(string haystackLower, string needleLower)
    {
        bool requireWordBoundary = IsAsciiLetterDigitOnly(needleLower);
        int startIndex = 0;

        while (startIndex <= haystackLower.Length - needleLower.Length)
        {
            int idx = haystackLower.IndexOf(needleLower, startIndex, StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            if (!requireWordBoundary)
            {
                return true;
            }

            bool leftOk = idx == 0 || !char.IsLetterOrDigit(haystackLower[idx - 1]);
            int afterIdx = idx + needleLower.Length;
            bool rightOk = afterIdx >= haystackLower.Length || !char.IsLetterOrDigit(haystackLower[afterIdx]);

            if (leftOk && rightOk)
            {
                return true;
            }

            startIndex = idx + 1;
        }

        return false;
    }

    private static bool IsAsciiOnly(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterDigitOnly(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7F || (!char.IsLetterOrDigit(c) && c != '_'))
            {
                return false;
            }
        }

        return true;
    }

    public void Load()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var json = File.ReadAllText(_storagePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            _learnedMappings = loaded == null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch { _learnedMappings = new(StringComparer.OrdinalIgnoreCase); }
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_learnedMappings, options);
            File.WriteAllText(_storagePath, json);
        }
        catch { }
    }

    public int KnowledgeCount => _learnedMappings.Count;

    public int MergeMappings(IReadOnlyDictionary<string, string> incoming, bool overwriteExisting = false)
    {
        if (incoming == null || incoming.Count == 0)
        {
            return 0;
        }

        int changed = 0;
        foreach (var kvp in incoming)
        {
            var normalizedKey = NormalizeKey(kvp.Key);
            if (!IsValidLearningKey(normalizedKey) || string.IsNullOrWhiteSpace(kvp.Value))
            {
                continue;
            }

            if (!_learnedMappings.TryGetValue(normalizedKey, out var existing) ||
                (overwriteExisting && !string.Equals(existing, kvp.Value, StringComparison.Ordinal)))
            {
                _learnedMappings[normalizedKey] = kvp.Value;
                changed++;
            }
        }

        if (changed > 0)
        {
            Save();
        }

        return changed;
    }

    // 用於 Debug 的方法
    public Dictionary<string, string> GetAllMappings() => new Dictionary<string, string>(_learnedMappings);
}

using FuzzySharp;
using ComicOrganizer.App.Models;
using System.Text.RegularExpressions;

namespace ComicOrganizer.App.Core;

public class ArchiveOrganizer
{
    private readonly MappingIntelligence _intelligence;
    private readonly AppConfig _config;

    public ArchiveOrganizer(MappingIntelligence intelligence, AppConfig config)
    {
        _intelligence = intelligence;
        _config = config;
    }

    public MatchResult? FindBestMatch(string sourceName, List<string> targetFolderNames)
    {
        if (targetFolderNames.Count == 0) return null;

        // 1. 知識庫預判
        var learnedFolder = _intelligence.TryGetMappedFolder(sourceName);
        if (learnedFolder != null && targetFolderNames.Contains(learnedFolder))
        {
            return new MatchResult { FolderName = learnedFolder, Confidence = 100.0, IsLearned = true };
        }

        // 2. 混合式比對
        string? bestFolder = null;
        double bestScore = -1;

        foreach (var target in targetFolderNames)
        {
            double score = CalculateSmartScore(sourceName, target);
            if (score > bestScore)
            {
                bestScore = score;
                bestFolder = target;
            }
        }
        
        if (bestFolder == null) return null;

        return new MatchResult
        {
            FolderName = bestFolder,
            Confidence = bestScore,
            IsLearned = false
        };
    }

    private double CalculateSmartScore(string source, string target)
    {
        if (source.Contains(target, StringComparison.OrdinalIgnoreCase)) return 95.0;
        
        var tags = ExtractAllKeywords(source);
        if (tags.Any(t => target.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(target, StringComparison.OrdinalIgnoreCase)))
        {
            return 90.0; 
        }

        return Fuzz.WeightedRatio(source, target);
    }

    public List<string> ExtractAllKeywords(string input)
    {
        var keywords = new List<string>();
        var matches = Regex.Matches(input, @"[\[\(\{](.*?)[\]\)\}]");
        foreach (Match m in matches)
        {
            string content = m.Groups[1].Value;
            var parts = content.Split(new[] { '-', '×', 'x', ',', ' ', ':', '|', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                string trimmed = p.Trim();
                if (trimmed.Length > 1 && !IsNoise(trimmed))
                {
                    keywords.Add(trimmed);
                }
            }
        }
        return keywords.Distinct().ToList();
    }

    public bool IsKeywordValidForTarget(string kw, string targetFolderName)
    {
        if (IsNoise(kw)) return false;

        if (targetFolderName.Contains(kw, StringComparison.OrdinalIgnoreCase) || 
            kw.Contains(targetFolderName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (Fuzz.WeightedRatio(kw, targetFolderName) > 80)
            return true;

        return false;
    }

    private bool IsNoise(string token)
    {
        string cleanToken = token.Trim('[', ']', '(', ')', '{', '}', ' ');
        
        if (_config.Blacklist.Any(nw => cleanToken.Contains(nw, StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (var pattern in _config.BlacklistPatterns)
        {
            if (Regex.IsMatch(cleanToken, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}

public class MatchResult
{
    public string FolderName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public bool IsLearned { get; set; }
}

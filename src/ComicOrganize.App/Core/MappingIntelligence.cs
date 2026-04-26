using System.Text.Json;

namespace ComicOrganize.App.Core;

public class MappingIntelligence
{
    private Dictionary<string, string> _learnedMappings = new();
    private readonly string _storagePath;

    public MappingIntelligence(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings.json");
        Load();
    }

    public void Learn(string keyword, string targetFolderName)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        
        // 正規化關鍵字：轉小寫並修剪
        string normalizedKey = keyword.ToLower().Trim();

        if (!_learnedMappings.ContainsKey(normalizedKey) || _learnedMappings[normalizedKey] != targetFolderName)
        {
            _learnedMappings[normalizedKey] = targetFolderName;
            Save();
        }
    }

    public string? TryGetMappedFolder(string fileName)
    {
        string normalizedFileName = fileName.ToLower();
        
        // 優先匹配較長的關鍵字，以提高準確度
        var sortedMappings = _learnedMappings.OrderByDescending(x => x.Key.Length);

        foreach (var entry in sortedMappings)
        {
            if (normalizedFileName.Contains(entry.Key))
            {
                return entry.Value;
            }
        }
        return null;
    }

    public void Load()
    {
        if (!File.Exists(_storagePath)) return;
        try
        {
            var json = File.ReadAllText(_storagePath);
            _learnedMappings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch { _learnedMappings = new(); }
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

    // 用於 Debug 的方法
    public Dictionary<string, string> GetAllMappings() => new Dictionary<string, string>(_learnedMappings);
}

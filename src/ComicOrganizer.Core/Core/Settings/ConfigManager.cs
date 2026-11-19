using System.Text.Json;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

public class ConfigManager
{
    private readonly string _configPath;
    
    public ConfigManager(string? path = null)
    {
        _configPath = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(_configPath, json);
    }
}

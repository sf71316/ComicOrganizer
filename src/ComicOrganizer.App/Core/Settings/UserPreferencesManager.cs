using System.Text.Json;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// UserPreferences 的 JSON 持久化（preferences.json，與 appsettings.json 分檔）。
/// 結構與 ConfigManager 一致：Load 失敗回 default、Save 直接覆寫。
/// </summary>
public class UserPreferencesManager
{
    private readonly string _path;

    public UserPreferencesManager(string? path = null)
    {
        _path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.json");
    }

    public UserPreferences Load()
    {
        if (!File.Exists(_path))
            return new UserPreferences();

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public void Save(UserPreferences prefs)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(prefs, options);
        File.WriteAllText(_path, json);
    }
}

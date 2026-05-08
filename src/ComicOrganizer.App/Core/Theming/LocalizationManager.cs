using System;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace ComicOrganizer.App.Core;

public static class LocalizationManager
{
    private static ResourceManager? _resourceManager;
    public static event EventHandler? LanguageChanged;

    public static void Initialize(string languageCode)
    {
        _resourceManager = new ResourceManager("ComicOrganizer.App.Properties.Resources", typeof(LocalizationManager).Assembly);
        SetLanguage(languageCode);
    }

    public static void SetLanguage(string languageCode)
    {
        try
        {
            var culture = new CultureInfo(languageCode);
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;

            OnLanguageChanged();
        }
        catch (CultureNotFoundException)
        {
            // Fallback to default
        }
    }

    public static string GetString(string key)
    {
        return _resourceManager?.GetString(key) ?? key;
    }

    private static void OnLanguageChanged()
    {
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}

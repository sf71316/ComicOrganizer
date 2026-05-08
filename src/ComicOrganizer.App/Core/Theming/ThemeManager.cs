using System;
using System.Drawing;
using System.Windows.Forms;
using ComicOrganizer.App.Models;
using Microsoft.Win32;

namespace ComicOrganizer.App.Core;

public static class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.System;
    public static event EventHandler? ThemeChanged;

    public static AppTheme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                UpdateAntdUITheme();
                OnThemeChanged();
            }
        }
    }

    public static bool IsDark
    {
        get
        {
            if (_currentTheme == AppTheme.Dark) return true;
            if (_currentTheme == AppTheme.Light) return false;
            return GetIsSystemDark();
        }
    }

    private static void UpdateAntdUITheme()
    {
        AntdUI.Config.IsDark = IsDark;
    }

    private static bool GetIsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void OnThemeChanged()
    {
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Initialize(AppTheme initialTheme)
    {
        _currentTheme = initialTheme;
        UpdateAntdUITheme();
    }

    /// <summary>
    /// 套主題到 control hierarchy。
    /// 設計：各 Form/UserControl 自己訂閱 ThemeChanged 並提供 ApplyThemeColors() 重設 token color，
    /// 因為 Designer 內 BackColor=ThemeColors.X 是「值複製」一次性，IsDark 切後不會自動更新。
    /// 此方法只負責同步 AntdUI.Config + 觸發 ThemeChanged 廣播給所有訂閱者。
    /// </summary>
    public static void ApplyTheme(Control control)
    {
        AntdUI.Config.IsDark = IsDark;
        OnThemeChanged();
    }
}

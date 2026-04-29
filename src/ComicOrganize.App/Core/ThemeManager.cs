using System;
using System.Drawing;
using System.Windows.Forms;
using ComicOrganize.App.Models;
using Microsoft.Win32;

namespace ComicOrganize.App.Core;

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

    public static void ApplyTheme(Control control)
    {
        if (control is Form form)
        {
            form.BackColor = ThemeColors.Background;
            form.ForeColor = ThemeColors.Text;
        }

        ApplyToControl(control);
    }

    private static void ApplyToControl(Control control)
    {
        // AntdUI 控制項通常會自動跟隨 AntdUI.Config.Theme
        // 但我們自定義的控制項或原生 Forms 控制項需要手動更新
        
        if (control is AntdUI.Panel panel)
        {
            // 如果 Panel 用作卡片背景，可能需要特別處理
            if (panel.Name == "rootPanel")
            {
                panel.Back = ThemeColors.Surface;
            }
        }
        else if (!(control is AntdUI.IControl)) // 非 AntdUI 控制項
        {
            control.BackColor = GetDefaultBackColor(control);
            control.ForeColor = GetDefaultForeColor(control);
        }

        foreach (Control child in control.Controls)
        {
            ApplyToControl(child);
        }
    }

    private static Color GetDefaultBackColor(Control control)
    {
        return control switch
        {
            Button => ThemeColors.Secondary,
            TextBox => ThemeColors.Surface,
            ListBox => ThemeColors.Surface,
            TreeView => ThemeColors.TreeViewSurface,
            ListView => ThemeColors.Surface,
            _ => ThemeColors.Background
        };
    }

    private static Color GetDefaultForeColor(Control control)
    {
        return ThemeColors.Text;
    }
}

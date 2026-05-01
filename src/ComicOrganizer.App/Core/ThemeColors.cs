using System.Drawing;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 全域主題色。支援 Dark/Light 模式切換。
/// </summary>
public static class ThemeColors
{
    // Light Mode Colors (原始主題)
    private static readonly Color LightText = Color.FromArgb(102, 69, 146);
    private static readonly Color LightBackground = Color.FromArgb(212, 211, 211);
    private static readonly Color LightPrimary = Color.FromArgb(131, 117, 159);
    private static readonly Color LightSecondary = Color.FromArgb(194, 177, 201);
    private static readonly Color LightAccent = Color.FromArgb(176, 147, 180);
    private static readonly Color LightSurface = Color.FromArgb(241, 233, 249);
    private static readonly Color LightBorder = Color.FromArgb(154, 124, 194);
    private static readonly Color LightTabHeader = Color.FromArgb(0xE8, 0xD7, 0xFA);  // #e8d7fa 比 Surface 沉一點
    private static readonly Color LightStatusBar = Color.FromArgb(0x84, 0x77, 0x9D);  // #84779d 紫灰，深於 Primary
    private static readonly Color LightRowAlternateBg = Color.FromArgb(219, 217, 255);
    private static readonly Color LightTreeViewSurface = Color.FromArgb(241, 233, 249);

    // Dark Mode Colors
    private static readonly Color DarkText = Color.FromArgb(220, 220, 220);
    private static readonly Color DarkBackground = Color.FromArgb(30, 30, 30);
    private static readonly Color DarkPrimary = Color.FromArgb(60, 60, 60);
    private static readonly Color DarkSecondary = Color.FromArgb(45, 45, 48);
    private static readonly Color DarkAccent = Color.FromArgb(0, 122, 204);
    private static readonly Color DarkSurface = Color.FromArgb(189, 189, 189);
    private static readonly Color DarkBorder = Color.FromArgb(63, 63, 70);
    private static readonly Color DarkTabHeader = Color.FromArgb(63, 63, 70);   // 暗模式 header 用 Border 同色（比 Surface 中灰更沉）
    private static readonly Color DarkStatusBar = Color.FromArgb(45, 45, 48);   // 跟 RowAlternate 同色，沉穩
    private static readonly Color DarkRowAlternateBg = Color.FromArgb(45, 45, 48);
    private static readonly Color DarkTreeViewSurface = Color.FromArgb(189, 189, 189);

    public static Color Text => ThemeManager.IsDark ? DarkText : LightText;
    public static Color Background => ThemeManager.IsDark ? DarkBackground : LightBackground;
    public static Color Primary => ThemeManager.IsDark ? DarkPrimary : LightPrimary;
    public static Color Secondary => ThemeManager.IsDark ? DarkSecondary : LightSecondary;
    public static Color Accent => ThemeManager.IsDark ? DarkAccent : LightAccent;
    public static Color Surface => ThemeManager.IsDark ? DarkSurface : LightSurface;
    public static Color TreeViewSurface => ThemeManager.IsDark ? DarkTreeViewSurface : LightTreeViewSurface;
    public static Color Border => ThemeManager.IsDark ? DarkBorder : LightBorder;
    public static Color TabHeader => ThemeManager.IsDark ? DarkTabHeader : LightTabHeader;
    public static Color StatusBar => ThemeManager.IsDark ? DarkStatusBar : LightStatusBar;
    public static Color HoverToolBarBackground => Color.FromArgb(140, 127, 124, 128);
    public static Color RowAlternateBg => ThemeManager.IsDark ? DarkRowAlternateBg : LightRowAlternateBg;
}

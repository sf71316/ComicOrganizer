namespace ComicOrganize.App.Core;

/// <summary>
/// 全域主題色（漫畫庫淺紫主題）。對應 CSS 變數：
/// --text #664592 / --background #d4d3d3 / --primary #83759f / --secondary #c2b1c9 / --accent #b093b4
/// 修改主題時調這裡即可，所有用 ThemeColors.X 引用的位置都會同步生效。
/// </summary>
public static class ThemeColors
{
    public static readonly Color Text       = Color.FromArgb(102, 69, 146);   // #664592
    public static readonly Color Background = Color.FromArgb(212, 211, 211);  // #d4d3d3
    public static readonly Color Primary    = Color.FromArgb(131, 117, 159);  // #83759f
    public static readonly Color Secondary  = Color.FromArgb(194, 177, 201);  // #c2b1c9
    public static readonly Color Accent     = Color.FromArgb(176, 147, 180);  // #b093b4

    // 衍生色（不在原始 5-token 內，但本主題實用）
    public static readonly Color Surface    = Color.FromArgb(241, 233, 249);  // #eddffb 內容區淺紫底
    public static readonly Color Border     = Color.FromArgb(192, 181, 207);  // #c0b5cf 邊框/分隔線淺紫灰
}

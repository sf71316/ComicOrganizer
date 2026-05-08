namespace ComicOrganizer.App.Core;

/// <summary>
/// Form/UserControl 實作此介面表示「會跟著主題/語系切換的控件」。
/// 配合 <see cref="ThemedControlExtensions.WireThemeAndLocalization{T}"/> 自動訂閱事件 + Disposed 解除。
/// </summary>
public interface IThemedControl
{
    /// <summary>套用主題色到自己的子控件（IsDark 切換時觸發 + 啟動時觸發一次）。</summary>
    void ApplyThemeColors();

    /// <summary>套用 i18n 文字到自己的子控件（語系切換時觸發 + 啟動時觸發一次）。</summary>
    void ApplyLocalization();
}

public static class ThemedControlExtensions
{
    /// <summary>
    /// 訂閱 ThemeChanged / LanguageChanged 事件並立即套用一次。Disposed 時自動解除（防 leak）。
    /// 用法：建構子內 InitializeComponent() 之後呼叫一次。
    /// </summary>
    public static void WireThemeAndLocalization<T>(this T self) where T : System.Windows.Forms.Control, IThemedControl
    {
        EventHandler themeHandler = (s, e) => self.ApplyThemeColors();
        EventHandler langHandler = (s, e) => self.ApplyLocalization();
        ThemeManager.ThemeChanged += themeHandler;
        LocalizationManager.LanguageChanged += langHandler;
        self.Disposed += (s, e) =>
        {
            ThemeManager.ThemeChanged -= themeHandler;
            LocalizationManager.LanguageChanged -= langHandler;
        };
        self.ApplyThemeColors();
        self.ApplyLocalization();
    }
}

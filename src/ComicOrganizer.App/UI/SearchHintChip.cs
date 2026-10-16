using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 搜尋輔助 chip — 顯示一個 DSL field 名（如 "artist:"）。
/// 依使用者輸入的 prefix 動態切換樣式：
///   matched prefix → bold + Primary 色，剩餘部分 normal Text 色
///   不匹配 → 整體 dimmed gray
/// 沒外部相依，純 OnPaint 渲染。
/// </summary>
public class SearchHintChip : Label
{
    private string _full = "";
    private int _matchedPrefixLen;
    private bool _matched;

    public SearchHintChip()
    {
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
        AutoSize = true;
        Margin = new Padding(2, 2, 6, 2);
        Padding = new Padding(2, 1, 2, 1);
        BackColor = Color.Transparent;
        Font = new Font("Microsoft JhengHei UI", 8.5F);
    }

    /// <summary>設定 chip 文字 + 匹配狀態。</summary>
    /// <param name="fullText">完整顯示文字（例如 "artist:"）。</param>
    /// <param name="matchedPrefixLen">前 N 個字算匹配（用粗體 + 主題色）。0 = 不匹配。</param>
    public void SetMatch(string fullText, int matchedPrefixLen)
    {
        _full = fullText ?? "";
        _matchedPrefixLen = Math.Clamp(matchedPrefixLen, 0, _full.Length);
        _matched = _matchedPrefixLen > 0;
        Text = _full;   // 給 AutoSize 用
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (string.IsNullOrEmpty(_full)) return;

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var prefix = _full[.._matchedPrefixLen];
        var suffix = _full[_matchedPrefixLen..];

        var prefixColor = ThemeColors.Primary;
        var suffixColor = _matched ? ThemeColors.Text : Color.FromArgb(140, 140, 140);

        using var boldFont = new Font(Font, FontStyle.Bold);

        var origin = new Point(Padding.Left, Padding.Top);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        if (!string.IsNullOrEmpty(prefix))
        {
            TextRenderer.DrawText(g, prefix, boldFont, origin, prefixColor, flags);
            var pw = TextRenderer.MeasureText(g, prefix, boldFont, Size.Empty, flags).Width;
            origin.Offset(pw, 0);
        }
        if (!string.IsNullOrEmpty(suffix))
        {
            TextRenderer.DrawText(g, suffix, Font, origin, suffixColor, flags);
        }
    }
}

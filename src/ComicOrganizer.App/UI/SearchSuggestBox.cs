using System.Drawing.Drawing2D;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 單一搜尋自動完成候選項目。
/// </summary>
public class SearchSuggestionItem
{
    public string DisplayText { get; set; } = "";
    public string SubText { get; set; } = "";
    public string InsertValue { get; set; } = "";
    public string Category { get; set; } = "";
}

/// <summary>
/// 搜尋欄 DSL 自動完成下拉候選框。
/// 支援滑鼠點選與鍵盤 ↑↓ 導航，不搶走搜尋輸入框之焦點；點擊外部區域時自動收起。
/// </summary>
public class SearchSuggestBox : Control, IMessageFilter
{
    private readonly List<SearchSuggestionItem> _items = new();
    private int _selectedIndex = 0;
    private bool _filterRegistered;
    private const int RowHeight = 28;

    /// <summary>關聯的搜尋輸入框（點擊輸入框本身時不關閉選單）。</summary>
    public Control? OwnerInput { get; set; }

    public event Action<SearchSuggestionItem>? ItemSelected;

    public bool HasItems => _items.Count > 0;
    public int SelectedIndex => _selectedIndex;
    public SearchSuggestionItem? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    public SearchSuggestBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);

        Visible = false;
        Font = new Font("Microsoft JhengHei UI", 9F);
        Cursor = Cursors.Hand;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && !_filterRegistered)
        {
            Application.AddMessageFilter(this);
            _filterRegistered = true;
        }
        else if (!Visible && _filterRegistered)
        {
            Application.RemoveMessageFilter(this);
            _filterRegistered = false;
        }
    }

    public bool PreFilterMessage(ref Message m)
    {
        // WM_LBUTTONDOWN (0x0201), WM_RBUTTONDOWN (0x0204), WM_MBUTTONDOWN (0x0207), WM_NCLBUTTONDOWN (0x00A1)
        if (Visible && (m.Msg == 0x0201 || m.Msg == 0x0204 || m.Msg == 0x0207 || m.Msg == 0x00A1))
        {
            var clicked = FromChildHandle(m.HWnd);
            if (clicked != this &&
                (OwnerInput == null || (clicked != OwnerInput && !OwnerInput.Contains(clicked))))
            {
                HideSuggestions();
            }
        }
        return false;
    }

    public void SetItems(IReadOnlyList<SearchSuggestionItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count > 0 ? 0 : -1;
        AdjustHeight();
        Invalidate();
    }

    public void ShowSuggestions()
    {
        if (_items.Count > 0)
        {
            AdjustHeight();
            Visible = true;
            BringToFront();
            Invalidate();
        }
    }

    public void HideSuggestions()
    {
        Visible = false;
        _items.Clear();
        _selectedIndex = -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _filterRegistered)
        {
            Application.RemoveMessageFilter(this);
            _filterRegistered = false;
        }
        base.Dispose(disposing);
    }

    public void SelectNext()
    {
        if (_items.Count == 0) return;
        _selectedIndex = (_selectedIndex + 1) % _items.Count;
        Invalidate();
    }

    public void SelectPrev()
    {
        if (_items.Count == 0) return;
        _selectedIndex = (_selectedIndex - 1 + _items.Count) % _items.Count;
        Invalidate();
    }

    private void AdjustHeight()
    {
        int targetH = Math.Clamp(_items.Count * RowHeight + 4, 32, 284);
        if (Height != targetH)
        {
            Height = targetH;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = (e.Y - 2) / RowHeight;
        if (idx >= 0 && idx < _items.Count && idx != _selectedIndex)
        {
            _selectedIndex = idx;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            ItemSelected?.Invoke(_items[_selectedIndex]);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var clientBounds = ClientRectangle;
        using var bgBrush = new SolidBrush(ThemeColors.Surface);
        g.FillRectangle(bgBrush, clientBounds);

        using var borderPen = new Pen(ThemeColors.Border, 1);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        if (_items.Count == 0) return;

        using var smallFont = new Font(Font.FontFamily, 8F);
        using var selBgBrush = new SolidBrush(ThemeColors.Primary);
        var textFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var rightFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        for (int i = 0; i < _items.Count; i++)
        {
            int top = 2 + i * RowHeight;
            var rowRect = new Rectangle(2, top, Width - 4, RowHeight);
            if (rowRect.Bottom > Height) break;

            bool isSelected = (i == _selectedIndex);
            if (isSelected)
            {
                g.FillRectangle(selBgBrush, rowRect);
            }

            var item = _items[i];
            var textColor = isSelected ? ThemeColors.HeaderText : ThemeColors.Text;
            var badgeColor = isSelected ? ThemeColors.HeaderText : ThemeColors.Primary;
            var subColor = isSelected ? Color.FromArgb(220, ThemeColors.HeaderText) : Color.FromArgb(140, 140, 140);

            // 1. 類別標記 (如 [作者] / [標籤])
            int x = rowRect.Left + 6;
            if (!string.IsNullOrEmpty(item.Category))
            {
                var catText = $"[{item.Category}]";
                var catSize = TextRenderer.MeasureText(g, catText, smallFont);
                var catRect = new Rectangle(x, top, catSize.Width, RowHeight);
                TextRenderer.DrawText(g, catText, smallFont, catRect, badgeColor, textFlags);
                x += catSize.Width + 4;
            }

            // 2. 右側副資訊 (如 "12 部作品")
            int rightWidth = 0;
            if (!string.IsNullOrEmpty(item.SubText))
            {
                var subSize = TextRenderer.MeasureText(g, item.SubText, smallFont);
                rightWidth = subSize.Width + 8;
                var subRect = new Rectangle(rowRect.Right - rightWidth, top, rightWidth, RowHeight);
                TextRenderer.DrawText(g, item.SubText, smallFont, subRect, subColor, rightFlags);
            }

            // 3. 主要顯示文字 (如 "巨乳 (big-breasts)" 或 "Flugel")
            int mainWidth = Math.Max(10, rowRect.Right - rightWidth - x - 4);
            var mainRect = new Rectangle(x, top, mainWidth, RowHeight);
            TextRenderer.DrawText(g, item.DisplayText, Font, mainRect, textColor, textFlags);
        }
    }
}

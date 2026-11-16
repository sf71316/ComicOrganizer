using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// AntdUI.Drawer 內裝載的 host — 管 AuthorInfoPanel 與 WorkDetailPanel 兩個顯示模式，
/// 同一時刻只有一個 Visible。提供統一的事件介面給 GalleryControl 接。
/// </summary>
public partial class DetailDrawerHost : UserControl
{
    public AuthorInfoPanel AuthorPanel => authorPanel;
    public WorkDetailPanel WorkPanel => workPanel;

    /// <summary>左邊界拖曳調寬過程中即時觸發 — 請求寬度（px）。</summary>
    public event Action<int>? RequestResizeWidth;

    /// <summary>左邊界拖曳調寬放開滑鼠後觸發 — 最終寬度（px）供持久化。</summary>
    public event Action<int>? ResizeCompleted;

    private bool _isDraggingResize;
    private bool _gripHover;
    private int _dragStartScreenX;
    private int _dragStartHostWidth;
    private int _lastRequestedWidth;

    /// <summary>任一面板目前是否處於編輯模式。</summary>
    public bool IsEditing => authorPanel.IsEditing || workPanel.IsEditing;

    /// <summary>目前編輯中的面板是否有尚未儲存的欄位變更。</summary>
    public bool HasUnsavedChanges => authorPanel.HasUnsavedChanges || workPanel.HasUnsavedChanges;

    public DetailDrawerHost()
    {
        InitializeComponent();
        BackColor = ThemeColors.Surface;
        leftResizeGrip.Width = LogicalToDeviceUnits(5);
        leftResizeGrip.BackColor = ThemeColors.Surface;
        WireResizeGrip();
    }

    private void WireResizeGrip()
    {
        leftResizeGrip.Paint += (s, e) =>
        {
            var lineColor = (_isDraggingResize || _gripHover) ? ThemeColors.Primary : ThemeColors.Border;
            int lineWidth = (_isDraggingResize || _gripHover) ? 2 : 1;
            using var pen = new Pen(lineColor, lineWidth);
            e.Graphics.DrawLine(pen, 0, 0, 0, leftResizeGrip.Height);
        };
        leftResizeGrip.MouseEnter += (s, e) => { _gripHover = true; leftResizeGrip.Invalidate(); };
        leftResizeGrip.MouseLeave += (s, e) => { _gripHover = false; leftResizeGrip.Invalidate(); };
        leftResizeGrip.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _isDraggingResize = true;
            _dragStartScreenX = Control.MousePosition.X;
            _dragStartHostWidth = FindForm()?.Width ?? Width;
            _lastRequestedWidth = _dragStartHostWidth;
            leftResizeGrip.Capture = true;
            leftResizeGrip.Invalidate();
        };
        leftResizeGrip.MouseMove += (s, e) =>
        {
            if (!_isDraggingResize) return;
            int deltaX = _dragStartScreenX - Control.MousePosition.X;
            int targetWidth = _dragStartHostWidth + deltaX;
            _lastRequestedWidth = targetWidth;
            RequestResizeWidth?.Invoke(targetWidth);
        };
        leftResizeGrip.MouseUp += (s, e) =>
        {
            if (!_isDraggingResize || e.Button != MouseButtons.Left) return;
            FinishDragResize();
        };
        leftResizeGrip.MouseCaptureChanged += (s, e) =>
        {
            if (_isDraggingResize && !leftResizeGrip.Capture)
            {
                FinishDragResize();
            }
        };
    }

    private void FinishDragResize()
    {
        if (!_isDraggingResize) return;
        _isDraggingResize = false;
        if (leftResizeGrip.Capture) leftResizeGrip.Capture = false;
        leftResizeGrip.Invalidate();
        int finalWidth = FindForm()?.Width ?? _lastRequestedWidth;
        ResizeCompleted?.Invoke(finalWidth);
    }

    /// <summary>放棄兩個面板的未存檔編輯並退出編輯模式。</summary>
    public void DiscardEditingState()
    {
        authorPanel.DiscardEditing();
        workPanel.DiscardEditing();
    }

    /// <summary>切到作者模式並套用資料。</summary>
    public void ShowAuthor(AuthorInfo info)
    {
        workPanel.DiscardEditing();
        workPanel.Visible = false;
        authorPanel.Visible = true;
        authorPanel.ShowInfo(info);
    }

    public void ShowAuthorEmpty(string message)
    {
        workPanel.DiscardEditing();
        workPanel.Visible = false;
        authorPanel.Visible = true;
        authorPanel.ShowEmpty(message);
    }

    /// <summary>切到作品模式並套用資料。</summary>
    public void ShowWork(WorkDetail detail, Image? cover)
    {
        authorPanel.DiscardEditing();
        authorPanel.Visible = false;
        workPanel.Visible = true;
        workPanel.Show(detail, cover);
    }

    public void ShowWorkEmpty(string message)
    {
        authorPanel.DiscardEditing();
        authorPanel.Visible = false;
        workPanel.Visible = true;
        workPanel.ShowEmpty(message);
    }
}

/// <summary>作品編輯狀態快照（供未存檔比對用）。</summary>
public sealed record WorkEditSnapshot(
    string? Title,
    string? AuthorName,
    string? Series,
    string? ChapterNo,
    string? Language,
    string? TranslationGroup,
    int? Rating,
    IReadOnlyList<string> Tags,
    string? PendingTagInput = null);

/// <summary>作者別名快照（供未存檔比對用）。</summary>
public readonly record struct AliasSnapshot(string Name, string? Lang);

/// <summary>作者編輯狀態快照（供未存檔比對用）。</summary>
public sealed record AuthorEditSnapshot(
    string? MainName,
    IReadOnlyList<AliasSnapshot> Aliases,
    IReadOnlyList<string> Tags,
    string? PendingAliasName = null,
    string? PendingAliasLang = null,
    string? PendingTagInput = null);

/// <summary>純狀態比對 helper：判斷編輯中表單是否相較原始資料有實際異動。</summary>
public static class DrawerEditStateComparer
{
    public static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static int? NormalizeRating(int? rating)
        => rating is > 0 ? rating : null;

    public static bool HasWorkEditChanges(WorkDetail? original, WorkEditSnapshot current)
    {
        if (original == null) return false;

        if (!string.Equals(NormalizeText(original.Title), NormalizeText(current.Title), StringComparison.Ordinal))
            return true;
        if (!string.Equals(NormalizeText(original.AuthorMainName), NormalizeText(current.AuthorName), StringComparison.Ordinal))
            return true;
        if (!string.Equals(NormalizeText(original.Series), NormalizeText(current.Series), StringComparison.Ordinal))
            return true;
        if (!string.Equals(NormalizeText(original.ChapterNo), NormalizeText(current.ChapterNo), StringComparison.Ordinal))
            return true;
        if (!string.Equals(NormalizeText(original.Language), NormalizeText(current.Language), StringComparison.Ordinal))
            return true;
        if (!string.Equals(NormalizeText(original.TranslationGroup), NormalizeText(current.TranslationGroup), StringComparison.Ordinal))
            return true;
        if (NormalizeRating(original.Rating) != NormalizeRating(current.Rating))
            return true;
        if (!string.IsNullOrWhiteSpace(current.PendingTagInput))
            return true;

        var originalTags = original.Tags.Select(t => t.Name);
        return !AreTagSetsEqual(originalTags, current.Tags);
    }

    public static bool HasAuthorEditChanges(AuthorInfo? original, AuthorEditSnapshot current)
    {
        if (original == null) return false;

        if (!string.Equals(NormalizeText(original.Author.MainName), NormalizeText(current.MainName), StringComparison.Ordinal))
            return true;
        if (!string.IsNullOrWhiteSpace(current.PendingAliasName)
            || !string.IsNullOrWhiteSpace(current.PendingAliasLang)
            || !string.IsNullOrWhiteSpace(current.PendingTagInput))
            return true;

        var originalAliases = original.Aliases.Select(a => new AliasSnapshot(a.Name, a.Lang));
        if (!AreAliasSetsEqual(originalAliases, current.Aliases))
            return true;

        var originalTags = original.AuthorTags.Select(t => t.Name);
        return !AreTagSetsEqual(originalTags, current.Tags);
    }

    private static bool AreTagSetsEqual(IEnumerable<string> left, IEnumerable<string> right)
    {
        var leftSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in left)
        {
            var normalized = NormalizeText(item);
            if (normalized != null) leftSet.Add(normalized);
        }

        var rightSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in right)
        {
            var normalized = NormalizeText(item);
            if (normalized != null) rightSet.Add(normalized);
        }

        return leftSet.SetEquals(rightSet);
    }

    private static bool AreAliasSetsEqual(IEnumerable<AliasSnapshot> left, IEnumerable<AliasSnapshot> right)
    {
        var leftSet = NormalizeAliases(left);
        var rightSet = NormalizeAliases(right);
        return leftSet.SetEquals(rightSet);
    }

    private static HashSet<(string Name, string Lang)> NormalizeAliases(IEnumerable<AliasSnapshot> aliases)
    {
        var set = new HashSet<(string Name, string Lang)>();
        foreach (var a in aliases)
        {
            var name = NormalizeText(a.Name);
            if (name == null) continue;
            var lang = NormalizeText(a.Lang) ?? "";
            set.Add((name, lang));
        }
        return set;
    }
}

using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 畫廊虛擬化清單項目（卡片格網的 <see cref="GalleryCardControl"/>、詳細列表的 <see cref="GalleryListRowControl"/>）共用基底：
/// 以零 XAML 繫結＋單一 <see cref="INotifyPropertyChanged"/> 訂閱取代每個欄位一個繫結。
/// <para>
/// - <see cref="ItemsRepeater"/> 回收設成 <c>null</c> 時忽略清空，等待下次重用直接以新項目覆蓋。
/// - 靜態欄位由子類別在 <see cref="ApplyStaticContent"/> 直接設值（只在值不同時寫入）。
/// - 動態欄位（<see cref="GalleryItemVm.Cover1"/>、<see cref="GalleryItemVm.HasCover1"/>、<see cref="GalleryItemVm.IsSelected"/>）透過單一事件處理更新。
/// - 內部視覺樹固定使用本地 DataContext（<see cref="IsolateInnerDataContext"/>），外層 DataContext 的變更不會傳遍子元件。
/// - 按鈕與右鍵選單由子類別的 Click 事件委派給 <see cref="BoundItem"/>，省去 ICommand 繫結與 CanExecuteChanged 訂閱。
/// </para>
/// </summary>
public abstract class GalleryItemControlBase : UserControl
{
    private static readonly ConcurrentDictionary<string, IBrush> AccentBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IBrush FallbackAccentBrush = new SolidColorBrush(Color.Parse("#3B82F6"));

    // 內部視覺樹固定使用的 DataContext（本地值）。項目控制項完全不使用 XAML 繫結，內部元件不需要知道目前項目；
    // 固定成本地值後，外層 DataContext 的變更（ItemsRepeater 回收時設 null、重用時設新項目）只會通知控制項本身，
    // 不會再沿著所有子元件逐一傳遞（卡片約 60 個子元件，本機取樣回收時約 141 ms / 8 秒拖動）。
    private static readonly object IsolatedDataContext = new();

    private GalleryItemVm? _boundItem;
    private bool _subscribed;

    /// <summary>目前顯示的項目；供點擊處理、測試與診斷使用。</summary>
    internal GalleryItemVm? BoundItem => _boundItem;

    /// <summary>子類別在 <c>InitializeComponent()</c> 之後呼叫，把內部根元素的 DataContext 固定成本地值。</summary>
    protected static void IsolateInnerDataContext(Control innerRoot)
    {
        ArgumentNullException.ThrowIfNull(innerRoot);
        innerRoot.DataContext = IsolatedDataContext;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // ItemsRepeater 回收時會將 DataContext 設成 null：略過清空，避免無謂重排與取消狀態
        if (DataContext is GalleryItemVm item)
        {
            BindItem(item);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnAttachedToVisualTreeCore();
        HandleAttached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HandleDetached();
    }

    /// <summary>子類別掛上視覺樹時的額外處理（例如設定游標）。</summary>
    protected virtual void OnAttachedToVisualTreeCore()
    {
    }

    /// <summary>
    /// 掛回視覺樹：重新訂閱目前項目，並重新套用封面與選取狀態。
    /// 卸下期間已退訂，這段時間內項目的封面載入完成 / 被放掉、選取變更都不會通知控制項，掛回時必須補上。
    /// （套用方法都只在值不同時才寫入，狀態沒變時幾乎沒有成本。）
    /// </summary>
    internal void HandleAttached()
    {
        EnsureSubscribed();
        if (_boundItem is { } item)
        {
            ApplySelectionState(item);
            ApplyCoverState(item);
        }
    }

    /// <summary>卸下視覺樹：退訂目前項目，避免項目持有已不在畫面上的控制項。</summary>
    internal void HandleDetached()
    {
        Unsubscribe();
    }

    /// <summary>套用靜態欄位（標題、副標、日期、徽章、ToolTip 等），只在值不同時寫入。</summary>
    protected abstract void ApplyStaticContent(GalleryItemVm item);

    /// <summary>套用選取外框。</summary>
    protected abstract void ApplySelectionState(GalleryItemVm item);

    /// <summary>套用封面與佔位層。</summary>
    protected abstract void ApplyCoverState(GalleryItemVm item);

    private void BindItem(GalleryItemVm item)
    {
        if (ReferenceEquals(_boundItem, item))
        {
            EnsureSubscribed();
            return;
        }

        Unsubscribe();
        _boundItem = item;
        EnsureSubscribed();

        ApplySelectionState(item);
        ApplyCoverState(item);
        ApplyStaticContent(item);
    }

    private void EnsureSubscribed()
    {
        if (_boundItem is not null && !_subscribed)
        {
            _boundItem.PropertyChanged += OnItemPropertyChanged;
            _subscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (_boundItem is not null && _subscribed)
        {
            _boundItem.PropertyChanged -= OnItemPropertyChanged;
            _subscribed = false;
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var item = _boundItem;
        if (item is null || !ReferenceEquals(sender, item))
        {
            return;
        }

        if (e.PropertyName is nameof(GalleryItemVm.Cover1) or nameof(GalleryItemVm.HasCover1))
        {
            ApplyCoverState(item);
        }
        else if (e.PropertyName == nameof(GalleryItemVm.IsSelected))
        {
            ApplySelectionState(item);
        }
    }

    /// <summary>依色碼取得（並快取）強調色筆刷；色碼無效時回傳預設藍色。</summary>
    protected static IBrush ResolveAccentBrush(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            return AccentBrushCache.GetOrAdd(hex.Trim(), static key =>
                Color.TryParse(key, out var color)
                    ? new SolidColorBrush(color)
                    : FallbackAccentBrush);
        }

        return FallbackAccentBrush;
    }

    /// <summary>只在值不同時設定文字，減少屬性寫入與文字排版失效。</summary>
    protected static void SetTextIfChanged(TextBlock target, string? value)
    {
        if (!string.Equals(target.Text, value, StringComparison.Ordinal))
        {
            target.Text = value;
        }
    }

    /// <summary>只在值不同時設定可見性。</summary>
    protected static void SetVisibleIfChanged(Control target, bool visible)
    {
        if (target.IsVisible != visible)
        {
            target.IsVisible = visible;
        }
    }
}

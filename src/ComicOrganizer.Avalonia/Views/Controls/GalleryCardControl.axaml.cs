using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 畫廊卡片虛擬化專用控制項：以零 XAML 繫結＋單一 <see cref="INotifyPropertyChanged"/> 訂閱取代 24 個獨立繫結。
/// <para>
/// - <see cref="ItemsRepeater"/> 回收設成 <c>null</c> 時忽略清空，等待下次重用直接以新項目覆蓋（承襲 <see cref="StableDataContextHost"/> 語意）。
/// - 靜態欄位（標題、副標題、日期、角標、頁數、佔位字元、ToolTip）僅於值不同時直接設值，完全省去 21 個繫結的退訂／訂閱／取值成本。
/// - 動態欄位（<see cref="GalleryItemVm.Cover1"/>、<see cref="GalleryItemVm.HasCover1"/>、<see cref="GalleryItemVm.IsSelected"/>）透過單一事件處理更新。
/// - 按鈕與右鍵選單直接透過 Click 事件委派給當前 <see cref="_boundItem"/>，省去 7 個 ICommand 繫結與 CanExecuteChanged 訂閱。
/// </para>
/// </summary>
public partial class GalleryCardControl : UserControl
{
    private static readonly ConcurrentDictionary<string, IBrush> AccentBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IBrush FallbackAccentBrush = new SolidColorBrush(Color.Parse("#3B82F6"));

    private GalleryItemVm? _boundItem;
    private bool _subscribed;
    private string? _appliedAccentHex;

    // 卡片內部視覺樹固定使用的 DataContext（本地值）。卡片完全不使用 XAML 繫結，
    // 內部元件不需要知道目前項目；固定成本地值後，外層 DataContext 的變更（ItemsRepeater 回收時設 null、
    // 重用時設新項目）只會通知卡片本身，不會再沿著約 60 個子元件逐一傳遞（本機取樣回收時約 141 ms / 8 秒拖動）。
    private static readonly object IsolatedDataContext = new();

    public GalleryCardControl()
    {
        InitializeComponent();
        RootCard.DataContext = IsolatedDataContext;
    }

    /// <summary>供測試與診斷查詢目前卡片繫結的項目。</summary>
    internal GalleryItemVm? BoundItem => _boundItem;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // ItemsRepeater 回收時會將 DataContext 設成 null：略過清空，避免無謂重排與取消狀態
        if (DataContext is GalleryItemVm item)
        {
            BindItem(item);
        }
    }

    private static Cursor? s_handCursor;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ActivateButton.Cursor is null && TopLevel.GetTopLevel(this) is not null)
        {
            s_handCursor ??= new Cursor(StandardCursorType.Hand);
            ActivateButton.Cursor = s_handCursor;
        }
        HandleAttached();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HandleDetached();
    }

    /// <summary>
    /// 掛回視覺樹：重新訂閱目前項目，並重新套用封面與選取狀態。
    /// 卸下期間已退訂，這段時間內項目的封面載入完成 / 被放掉、選取變更都不會通知卡片，掛回時必須補上。
    /// （兩個套用方法都只在值不同時才寫入，狀態沒變時幾乎沒有成本。）
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

    /// <summary>卸下視覺樹：退訂目前項目，避免項目持有已不在畫面上的卡片。</summary>
    internal void HandleDetached()
    {
        Unsubscribe();
    }

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

        // 1. 動態狀態（選取外框、封面與佔位層）
        ApplySelectionState(item);
        ApplyCoverState(item);

        // 2. 靜態欄位（僅在值改變時才設值，減少 Avalonia 屬性寫入與 TextLayout 失效）
        if (!string.Equals(_appliedAccentHex, item.AccentColorHex, StringComparison.OrdinalIgnoreCase))
        {
            _appliedAccentHex = item.AccentColorHex;
            AccentBorder.Background = ResolveAccentBrush(item.AccentColorHex);
        }

        if (!string.Equals(FallbackGlyphText.Text, item.FallbackGlyph, StringComparison.Ordinal))
        {
            FallbackGlyphText.Text = item.FallbackGlyph;
        }

        if (!string.Equals(TypeBadgeText.Text, item.TypeBadgeText, StringComparison.Ordinal))
        {
            TypeBadgeText.Text = item.TypeBadgeText;
        }

        if (PageCountBorder.IsVisible != item.HasPageCount)
        {
            PageCountBorder.IsVisible = item.HasPageCount;
        }

        if (item.HasPageCount && !string.Equals(PageCountText.Text, item.PageCountText, StringComparison.Ordinal))
        {
            PageCountText.Text = item.PageCountText;
        }

        if (!string.Equals(TitleText.Text, item.DisplayTitle, StringComparison.Ordinal))
        {
            TitleText.Text = item.DisplayTitle;
            ToolTip.SetTip(TitleText, item.DisplayTitle);
        }

        if (!string.Equals(SubtitleText.Text, item.SubtitleText, StringComparison.Ordinal))
        {
            SubtitleText.Text = item.SubtitleText;
            ToolTip.SetTip(SubtitleText, item.SubtitleText);
        }

        if (!string.Equals(ModifiedText.Text, item.ModifiedDisplay, StringComparison.Ordinal))
        {
            ModifiedText.Text = item.ModifiedDisplay;
        }

        ToolTip.SetTip(ActivateButton, item.PrimaryActionTooltip);
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

    private void ApplySelectionState(GalleryItemVm item)
    {
        RootCard.Classes.Set("selected", item.IsSelected);
    }

    private void ApplyCoverState(GalleryItemVm item)
    {
        bool hasCover = item.HasCover1;
        if (!ReferenceEquals(CoverImage.Source, item.Cover1))
        {
            CoverImage.Source = item.Cover1;
        }

        if (CoverImage.IsVisible != hasCover)
        {
            CoverImage.IsVisible = hasCover;
        }

        bool showPlaceholder = !hasCover;
        if (AccentBorder.IsVisible != showPlaceholder)
        {
            AccentBorder.IsVisible = showPlaceholder;
        }

        if (FallbackGlyphText.IsVisible != showPlaceholder)
        {
            FallbackGlyphText.IsVisible = showPlaceholder;
        }
    }

    private static IBrush ResolveAccentBrush(string? hex)
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

    private void OnActivateClick(object? sender, RoutedEventArgs e)
        => _boundItem?.ActivateCommand.Execute(null);

    private void OnOpenInReaderClick(object? sender, RoutedEventArgs e)
        => _boundItem?.OpenInReaderCommand.Execute(null);

    private void OnOpenExternalClick(object? sender, RoutedEventArgs e)
        => _boundItem?.OpenExternalCommand.Execute(null);

    private void OnInspectDetailClick(object? sender, RoutedEventArgs e)
        => _boundItem?.InspectDetailCommand.Execute(null);

    private void OnRevealInExplorerClick(object? sender, RoutedEventArgs e)
        => _boundItem?.RevealInExplorerCommand.Execute(null);
}

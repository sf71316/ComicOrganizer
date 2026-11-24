using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicOrganizer.Avalonia.ViewModels;

namespace ComicOrganizer.Avalonia.Views.Controls;

/// <summary>
/// 畫廊詳細列表模式的一列：零 XAML 繫結（原列表範本約 21 個繫結＋6 個 ICommand），
/// 繫結與訂閱邏輯見 <see cref="GalleryItemControlBase"/>。
/// </summary>
public partial class GalleryListRowControl : GalleryItemControlBase
{
    private static Cursor? s_handCursor;

    private string? _appliedAccentHex;

    public GalleryListRowControl()
    {
        InitializeComponent();
        IsolateInnerDataContext(RootCard);
    }

    protected override void OnAttachedToVisualTreeCore()
    {
        if (ActivateButton.Cursor is null && TopLevel.GetTopLevel(this) is not null)
        {
            s_handCursor ??= new Cursor(StandardCursorType.Hand);
            ActivateButton.Cursor = s_handCursor;
        }
    }

    protected override void ApplyStaticContent(GalleryItemVm item)
    {
        if (!string.Equals(_appliedAccentHex, item.AccentColorHex, StringComparison.OrdinalIgnoreCase))
        {
            _appliedAccentHex = item.AccentColorHex;
            AccentBorder.Background = ResolveAccentBrush(item.AccentColorHex);
        }

        SetTextIfChanged(FallbackGlyphText, item.FallbackGlyph);
        SetTextIfChanged(TitleText, item.DisplayTitle);
        SetTextIfChanged(SubtitleText, item.SubtitleText);
        SetTextIfChanged(TypeBadgeText, item.TypeBadgeText);
        SetTextIfChanged(SizeText, item.SizeDisplay);
        SetTextIfChanged(ModifiedText, item.ModifiedDisplay);
        ToolTip.SetTip(ActivateButton, item.PrimaryActionTooltip);

        bool canBasket = item.CanPreviewAndBasket;
        SetVisibleIfChanged(PreviewButton, canBasket);
        SetVisibleIfChanged(BasketButton, canBasket);
        SetVisibleIfChanged(PreviewMenuItem, canBasket);
        SetVisibleIfChanged(BasketMenuItem, canBasket);
        SetVisibleIfChanged(BasketSeparator, canBasket);
    }

    protected override void ApplyBasketState(GalleryItemVm item)
    {
        bool inBasket = item.CanPreviewAndBasket && item.IsInBasket;
        SetTextIfChanged(BasketButtonText, inBasket ? "✓ 已入籃" : "📌 挑書");
        var header = inBasket ? "✓ 移出待挑書籃" : "📌 加入待挑書籃";
        if (!string.Equals(BasketMenuItem.Header as string, header, StringComparison.Ordinal))
        {
            BasketMenuItem.Header = header;
        }
    }

    protected override void ApplySelectionState(GalleryItemVm item)
    {
        RootCard.Classes.Set("selected", item.IsSelected);
    }

    protected override void ApplyCoverState(GalleryItemVm item)
    {
        bool hasCover = item.HasCover1;
        if (!ReferenceEquals(CoverImage.Source, item.Cover1))
        {
            CoverImage.Source = item.Cover1;
        }

        SetVisibleIfChanged(CoverImage, hasCover);
        SetVisibleIfChanged(AccentBorder, !hasCover);
        SetVisibleIfChanged(FallbackGlyphText, !hasCover);
    }

    private void OnActivateClick(object? sender, RoutedEventArgs e)
        => BoundItem?.ActivateCommand.Execute(null);

    private void OnPreviewClick(object? sender, RoutedEventArgs e)
        => BoundItem?.PreviewCommand.Execute(null);

    private void OnToggleBasketClick(object? sender, RoutedEventArgs e)
        => BoundItem?.ToggleBasketCommand.Execute(null);

    private void OnOpenInReaderClick(object? sender, RoutedEventArgs e)
        => BoundItem?.OpenInReaderCommand.Execute(null);

    private void OnOpenExternalClick(object? sender, RoutedEventArgs e)
        => BoundItem?.OpenExternalCommand.Execute(null);

    private void OnInspectDetailClick(object? sender, RoutedEventArgs e)
        => BoundItem?.InspectDetailCommand.Execute(null);

    private void OnRevealInExplorerClick(object? sender, RoutedEventArgs e)
        => BoundItem?.RevealInExplorerCommand.Execute(null);
}

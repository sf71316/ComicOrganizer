using Avalonia.Controls;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.ViewModels;
using ComicOrganizer.Avalonia.Views;
using SukiUI.Controls;
using Xunit;

namespace ComicOrganizer.Tests;

public class GalleryCardTransitionTests
{
    [Fact]
    public void GalleryItemCard_DisablesTransitions_OnCardButtonsAndCoverImage()
    {
        AvaloniaTestThread.Run(() =>
        {
            var view = new GalleryPageView();

            var coverImage = new Image();
            coverImage.Classes.Add("gallery-cover-fade");

            var basicButton = new Button { Content = coverImage };
            basicButton.Classes.Add("Basic");

            var outlinedButton = new Button { Content = new TextBlock { Text = "ℹ" } };
            outlinedButton.Classes.Add("Outlined");

            var card = new GlassCard
            {
                Content = new StackPanel
                {
                    Children = { basicButton, outlinedButton }
                }
            };
            card.Classes.Add("gallery-item-card");

            // 將卡片掛到 GalleryPageView 的樣式作用域下套用樣式
            view.Content = card;
            view.ApplyStyling();
            card.ApplyStyling();
            basicButton.ApplyStyling();
            outlinedButton.ApplyStyling();
            coverImage.ApplyStyling();

            Assert.True(card.Transitions is null || card.Transitions.Count == 0);
            Assert.True(basicButton.Transitions is null || basicButton.Transitions.Count == 0);
            Assert.True(outlinedButton.Transitions is null || outlinedButton.Transitions.Count == 0);
            Assert.True(coverImage.Transitions is null || coverImage.Transitions.Count == 0);
            Assert.False(coverImage.IsHitTestVisible);
        });
    }

    [Fact]
    public void GalleryItemVm_CachesFormattedDisplayStrings_WithoutReallocation()
    {
        var meta = new GalleryItemSortMetadata(
            Path: @"E:\Comics\test.zip",
            Name: "test.zip",
            DisplayTitle: "測試作品",
            IsFolder: false,
            SizeBytes: 5 * 1024 * 1024,
            ModifiedTicks: new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            ReadStatus: ReadStatus.Reading,
            ReadCount: 2,
            LastRead: null);

        var vm = new GalleryItemVm(
            meta,
            authorName: "作者A",
            coverThumbKey: null,
            pageCount: 120,
            language: "繁體中文",
            isAuthorFolder: false,
            onActivate: _ => { },
            onRevealInExplorer: _ => { });

        Assert.Equal("📖 閱讀中", vm.TypeBadgeText);
        Assert.Equal("120P", vm.PageCountText);
        Assert.Equal("5.0 MB", vm.SizeDisplay);
        Assert.Equal("作者A · 繁體中文 · 5.0 MB", vm.SubtitleText);
        Assert.Equal("以內建閱讀器開啟：測試作品", vm.PrimaryActionTooltip);

        // 多次讀取必須回傳同一個字串參考（零重複配置）
        Assert.Same(vm.PageCountText, vm.PageCountText);
        Assert.Same(vm.SizeDisplay, vm.SizeDisplay);
        Assert.Same(vm.ModifiedDisplay, vm.ModifiedDisplay);
        Assert.Same(vm.SubtitleText, vm.SubtitleText);
        Assert.Same(vm.PrimaryActionTooltip, vm.PrimaryActionTooltip);
    }

    [Fact]
    public void GalleryCardControl_RecyclesWithoutNullChurn_AndUpdatesDynamicProperties()
    {
        AvaloniaTestThread.Run(() =>
        {
            var card = new ComicOrganizer.Avalonia.Views.Controls.GalleryCardControl();
            var item1 = new GalleryItemVm(
                new GalleryItemSortMetadata(@"E:\Comics\a.zip", "a.zip", "作品 A", false, 2 * 1024 * 1024, 1700000000000, ReadStatus.Unread, 0, null),
                authorName: "作者甲",
                coverThumbKey: null,
                pageCount: 40,
                language: "日文",
                isAuthorFolder: false,
                onActivate: _ => { },
                onRevealInExplorer: _ => { });
            var item2 = new GalleryItemVm(
                new GalleryItemSortMetadata(@"E:\Comics\b.zip", "b.zip", "作品 B", false, 8 * 1024 * 1024, 1710000000000, ReadStatus.Completed, 1, null),
                authorName: "作者乙",
                coverThumbKey: null,
                pageCount: null,
                language: "繁體中文",
                isAuthorFolder: false,
                onActivate: _ => { },
                onRevealInExplorer: _ => { });

            card.DataContext = item1;
            var titleTb = card.FindControl<TextBlock>("TitleText")!;
            var badgeTb = card.FindControl<TextBlock>("TypeBadgeText")!;
            var pageBorder = card.FindControl<Border>("PageCountBorder")!;
            var rootCard = card.FindControl<GlassCard>("RootCard")!;

            Assert.Equal("作品 A", titleTb.Text);
            Assert.Equal("● 未讀", badgeTb.Text);
            Assert.True(pageBorder.IsVisible);
            Assert.DoesNotContain("selected", rootCard.Classes);

            // 動態屬性 IsSelected 變更應即時反映到選取外框
            item1.IsSelected = true;
            Assert.Contains("selected", rootCard.Classes);

            // ItemsRepeater 回收設為 null 時不清空 UI（避免無謂重排）
            card.DataContext = null;
            Assert.Equal("作品 A", titleTb.Text);

            // 回收重用換成 item2：所有靜態與動態欄位直接覆蓋，且舊 item1 不再影響卡片
            card.DataContext = item2;
            Assert.Equal("作品 B", titleTb.Text);
            Assert.Equal("✓ 已完成", badgeTb.Text);
            Assert.False(pageBorder.IsVisible);
            Assert.DoesNotContain("selected", rootCard.Classes);

            // 舊 item1 的屬性變更不可再影響已換成 item2 的卡片
            item1.IsSelected = false;
            item2.IsSelected = true;
            Assert.Contains("selected", rootCard.Classes);
        });
    }

    [Fact]
    public void GalleryCardControl_Reattach_AppliesChangesMadeWhileDetached()
    {
        AvaloniaTestThread.Run(() =>
        {
            var card = new ComicOrganizer.Avalonia.Views.Controls.GalleryCardControl();
            var item = new GalleryItemVm(
                new GalleryItemSortMetadata(@"E:\Comics\c.zip", "c.zip", "作品 C", false, 1024, 1700000000000, ReadStatus.Unread, 0, null),
                authorName: "作者丙",
                coverThumbKey: null,
                pageCount: 10,
                language: null,
                isAuthorFolder: false,
                onActivate: _ => { },
                onRevealInExplorer: _ => { });
            var rootCard = card.FindControl<GlassCard>("RootCard")!;

            card.DataContext = item;
            card.HandleAttached();
            Assert.DoesNotContain("selected", rootCard.Classes);

            // 卸下期間已退訂：選取變更不會即時反映到卡片
            card.HandleDetached();
            item.IsSelected = true;
            Assert.DoesNotContain("selected", rootCard.Classes);

            // 掛回時要補上卸下期間漏掉的變更，並恢復即時更新
            card.HandleAttached();
            Assert.Contains("selected", rootCard.Classes);
            item.IsSelected = false;
            Assert.DoesNotContain("selected", rootCard.Classes);
        });
    }

    [Fact]
    public void GalleryCardControl_OuterDataContextChanges_DoNotPropagateIntoCardTree()
    {
        AvaloniaTestThread.Run(() =>
        {
            var card = new ComicOrganizer.Avalonia.Views.Controls.GalleryCardControl();
            var item1 = new GalleryItemVm(
                new GalleryItemSortMetadata(@"E:\Comics\a.zip", "a.zip", "作品 A", false, 1024, 1700000000000, ReadStatus.Unread, 0, null),
                authorName: "作者甲",
                coverThumbKey: null,
                pageCount: 10,
                language: null,
                isAuthorFolder: false,
                onActivate: _ => { },
                onRevealInExplorer: _ => { });
            var item2 = new GalleryItemVm(
                new GalleryItemSortMetadata(@"E:\Comics\b.zip", "b.zip", "作品 B", false, 2048, 1710000000000, ReadStatus.Unread, 0, null),
                authorName: "作者乙",
                coverThumbKey: null,
                pageCount: 20,
                language: null,
                isAuthorFolder: false,
                onActivate: _ => { },
                onRevealInExplorer: _ => { });

            var titleTb = card.FindControl<TextBlock>("TitleText")!;
            var rootCard = card.FindControl<GlassCard>("RootCard")!;
            object? innerContext = rootCard.DataContext;
            int innerChanges = 0;
            titleTb.DataContextChanged += (_, _) => innerChanges++;

            // 模擬 ItemsRepeater：設項目 → 回收設 null → 重用設新項目
            card.DataContext = item1;
            card.DataContext = null;
            card.DataContext = item2;

            // 卡片內容照常更新，但內部元件的 DataContext 從未改變（變更沒有傳進卡片內部）
            Assert.Equal("作品 B", titleTb.Text);
            Assert.NotNull(innerContext);
            Assert.Same(innerContext, rootCard.DataContext);
            Assert.Same(innerContext, titleTb.DataContext);
            Assert.Equal(0, innerChanges);
        });
    }
}

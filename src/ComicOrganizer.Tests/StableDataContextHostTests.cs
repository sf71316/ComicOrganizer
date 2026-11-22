using Avalonia.Controls;
using Avalonia.Data;
using ComicOrganizer.Avalonia.Views.Controls;
using Xunit;

namespace ComicOrganizer.Tests;

public class StableDataContextHostTests
{
    private sealed record DummyItem(string Title);

    [Fact]
    public void Host_IgnoresNullDataContext_AndSwapsDirectlyToNewItem()
    {
        AvaloniaTestThread.Run(() =>
        {
            var child = new Border();
            var host = new StableDataContextHost { Child = child };
            var first = new object();
            var second = new object();
            var changes = new List<object?>();
            child.DataContextChanged += (_, _) => changes.Add(child.DataContext);

            host.DataContext = first;
            Assert.Same(first, child.DataContext);

            // ItemsRepeater 回收時把外層設成 null：內層卡片維持原項目，不經過 null
            host.DataContext = null;
            Assert.Same(first, child.DataContext);

            // 重用時直接從舊項目換成新項目
            host.DataContext = second;
            Assert.Same(second, child.DataContext);

            Assert.DoesNotContain(null, changes);
        });
    }

    [Fact]
    public void Host_ForwardsToReplacedChild()
    {
        AvaloniaTestThread.Run(() =>
        {
            var item = new object();
            var host = new StableDataContextHost { DataContext = item };
            var child = new Border();

            host.Child = child;
            Assert.Same(item, child.DataContext);

            host.DataContext = null;
            Assert.Same(item, child.DataContext);
        });
    }

    [Fact]
    public void Host_EvaluatesChildBindingsOnInitialNonNullDataContext_AndPreservesAcrossNullRecycle()
    {
        AvaloniaTestThread.Run(() =>
        {
            var tb = new TextBlock
            {
                [!TextBlock.TextProperty] = new Binding(nameof(DummyItem.Title)),
            };
            var child = new Border { Child = tb };
            var host = new StableDataContextHost { Child = child };

            // 首次賦予非 null DataContext：子樹繫結必須立刻取值（不可卡在 BeginUpdate 未結束）
            host.DataContext = new DummyItem("首頁作者");
            Assert.Equal("首頁作者", tb.Text);

            // 回收時設為 null：維持原顯示值不清空
            host.DataContext = null;
            Assert.Equal("首頁作者", tb.Text);

            // 重用時換成新項目：直接更新為新標題
            host.DataContext = new DummyItem("第二位作者");
            Assert.Equal("第二位作者", tb.Text);
        });
    }
}

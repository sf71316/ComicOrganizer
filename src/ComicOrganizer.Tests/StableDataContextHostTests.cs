using Avalonia.Controls;
using ComicOrganizer.Avalonia.Views.Controls;
using Xunit;

namespace ComicOrganizer.Tests;

public class StableDataContextHostTests
{
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
}

using ComicOrganizer.App.Models;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 工作項目 3（D1 分頁導航歷史）純邏輯單元測試：
/// - 初始狀態、Push、CanGoBack / CanGoForward
/// - TryGoBack / TryGoForward 往返與邊界
/// - Back 後 Push 截斷舊 Forward 歷史
/// - 連續重複目標去重（資料夾不分大小寫、同虛擬清單）與原地更新 RefreshResolver / Title
/// - 相異虛擬清單區分
/// - 容量上限淘汰最舊項目
/// - ParseXButtonAction 解析 WM_XBUTTONUP wParam
/// </summary>
public class TabNavigationHistoryTests
{
    [Fact]
    public void InitialState_EmptyAndCannotNavigate()
    {
        var history = new TabNavigationHistory();

        Assert.Equal(0, history.Count);
        Assert.Equal(-1, history.CurrentIndex);
        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.False(history.TryGoBack(out var backEntry));
        Assert.Null(backEntry);
        Assert.False(history.TryGoForward(out var fwdEntry));
        Assert.Null(fwdEntry);
    }

    [Fact]
    public void Push_And_BackForwardRoundTrip_TracksIndexAndBoundaries()
    {
        var history = new TabNavigationHistory();
        var f1 = TabNavigationEntry.ForFolder(@"C:\Comics\A");
        var f2 = TabNavigationEntry.ForFolder(@"C:\Comics\B");
        var f3 = TabNavigationEntry.ForFolder(@"C:\Comics\C");

        Assert.True(history.Push(f1));
        Assert.Equal(1, history.Count);
        Assert.Equal(0, history.CurrentIndex);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);

        Assert.True(history.Push(f2));
        Assert.True(history.Push(f3));
        Assert.Equal(3, history.Count);
        Assert.Equal(2, history.CurrentIndex);
        Assert.Same(f3, history.Current);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);

        // 退兩步到 f1
        Assert.True(history.TryGoBack(out var back1));
        Assert.Same(f2, back1);
        Assert.Equal(1, history.CurrentIndex);
        Assert.True(history.CanGoBack);
        Assert.True(history.CanGoForward);

        Assert.True(history.TryGoBack(out var back2));
        Assert.Same(f1, back2);
        Assert.Equal(0, history.CurrentIndex);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);

        // 到達起點後再退回傳 false
        Assert.False(history.TryGoBack(out var beyondStart));
        Assert.Null(beyondStart);
        Assert.Equal(0, history.CurrentIndex);

        // 前進兩步回到 f3
        Assert.True(history.TryGoForward(out var fwd1));
        Assert.Same(f2, fwd1);
        Assert.True(history.TryGoForward(out var fwd2));
        Assert.Same(f3, fwd2);
        Assert.Equal(2, history.CurrentIndex);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);

        // 到達終點後再前進回傳 false
        Assert.False(history.TryGoForward(out var beyondEnd));
        Assert.Null(beyondEnd);
        Assert.Equal(2, history.CurrentIndex);
    }

    [Fact]
    public void PushAfterBack_TruncatesForwardHistory()
    {
        var history = new TabNavigationHistory();
        var f1 = TabNavigationEntry.ForFolder(@"C:\Comics\A");
        var f2 = TabNavigationEntry.ForFolder(@"C:\Comics\B");
        var f3 = TabNavigationEntry.ForFolder(@"C:\Comics\C");
        var f4 = TabNavigationEntry.ForFolder(@"C:\Comics\D");

        history.Push(f1);
        history.Push(f2);
        history.Push(f3);

        Assert.True(history.TryGoBack(out _)); // 回到 f2
        Assert.True(history.CanGoForward);

        // 推入 f4 應截斷 f3
        Assert.True(history.Push(f4));
        Assert.Equal(3, history.Count);
        Assert.Equal(2, history.CurrentIndex);
        Assert.Same(f4, history.Current);
        Assert.False(history.CanGoForward);

        Assert.True(history.TryGoBack(out var prev));
        Assert.Same(f2, prev);
    }

    [Fact]
    public void Push_DuplicateDestination_DeduplicatesAndUpdatesMetadataInPlace()
    {
        var history = new TabNavigationHistory();

        // 資料夾不分大小寫與結尾斜線視為同一目的地
        var firstFolder = TabNavigationEntry.ForFolder(@"C:\Comics\AuthorA", "AuthorA");
        var sameFolderDiffCase = TabNavigationEntry.ForFolder(@"c:\comics\authora\", "AuthorA (Updated)");

        Assert.True(history.Push(firstFolder));
        Assert.False(history.Push(sameFolderDiffCase));
        Assert.Equal(1, history.Count);
        Assert.Equal("AuthorA (Updated)", history.Current!.Title);

        // 同虛擬清單：VirtualLibraryId、Title、IconSvg、VirtualPaths 序列一致 → 去重並更新 RefreshResolver
        Func<IReadOnlyList<string>> resolver1 = () => new[] { @"C:\1.zip" };
        Func<IReadOnlyList<string>> resolver2 = () => new[] { @"C:\1.zip", @"C:\2.zip" };

        var v1 = TabNavigationEntry.ForVirtualWorks(
            "我的清單",
            new[] { @"C:\Comics\Book1.zip", @"C:\Comics\Book2.zip" },
            "BookOutlined",
            resolver1,
            virtualLibraryId: 42);
        var v1Duplicate = TabNavigationEntry.ForVirtualWorks(
            "我的清單",
            new[] { @"c:\comics\book1.zip", @"c:\comics\book2.zip" },
            "BookOutlined",
            resolver2,
            virtualLibraryId: 42);

        Assert.True(history.Push(v1));
        Assert.Equal(2, history.Count);
        Assert.False(history.Push(v1Duplicate));
        Assert.Equal(2, history.Count);
        Assert.Same(resolver2, history.Current!.RefreshResolver);
    }

    [Fact]
    public void Push_DistinctVirtualLists_AreNotDeduplicated()
    {
        var history = new TabNavigationHistory();
        var pathsA = new[] { @"C:\Comics\Book1.zip" };
        var pathsB = new[] { @"C:\Comics\Book1.zip", @"C:\Comics\Book2.zip" };

        var baseEntry = TabNavigationEntry.ForVirtualWorks("清單A", pathsA, "TagOutlined", virtualLibraryId: 1);
        var diffLibId = TabNavigationEntry.ForVirtualWorks("清單A", pathsA, "TagOutlined", virtualLibraryId: 2);
        var diffTitle = TabNavigationEntry.ForVirtualWorks("清單B", pathsA, "TagOutlined", virtualLibraryId: 2);
        var diffIcon = TabNavigationEntry.ForVirtualWorks("清單B", pathsA, "StarOutlined", virtualLibraryId: 2);
        var diffPaths = TabNavigationEntry.ForVirtualWorks("清單B", pathsB, "StarOutlined", virtualLibraryId: 2);

        Assert.True(history.Push(baseEntry));
        Assert.True(history.Push(diffLibId));
        Assert.True(history.Push(diffTitle));
        Assert.True(history.Push(diffIcon));
        Assert.True(history.Push(diffPaths));
        Assert.Equal(5, history.Count);
        Assert.Equal(4, history.CurrentIndex);
    }

    [Fact]
    public void Push_ExceedingCapacity_EvictsOldestEntriesAndKeepsCorrectIndex()
    {
        var history = new TabNavigationHistory(maxCapacity: 3);

        for (int i = 1; i <= 5; i++)
        {
            Assert.True(history.Push(TabNavigationEntry.ForFolder($@"C:\Comics\Dir{i}")));
        }

        Assert.Equal(3, history.Count);
        Assert.Equal(2, history.CurrentIndex);
        Assert.Equal(@"C:\Comics\Dir5", history.Current!.FolderPath);

        Assert.True(history.TryGoBack(out var prev1));
        Assert.Equal(@"C:\Comics\Dir4", prev1!.FolderPath);

        Assert.True(history.TryGoBack(out var prev2));
        Assert.Equal(@"C:\Comics\Dir3", prev2!.FolderPath);

        // Dir1 與 Dir2 已被淘汰，不可再退
        Assert.False(history.CanGoBack);
        Assert.False(history.TryGoBack(out _));
    }

    [Theory]
    [InlineData(0x0001_0000L, -1)] // XBUTTON1
    [InlineData(0x0001_0020L, -1)] // XBUTTON1 + 低位旗標
    [InlineData(0x0002_0000L, 1)]  // XBUTTON2
    [InlineData(0x0002_0040L, 1)]  // XBUTTON2 + 低位旗標
    [InlineData(unchecked((long)0xFFFF_FFFF_0002_0000L), 1)] // 高 32 位符號擴展不影響 HIWORD
    [InlineData(0x0000_0000L, 0)]
    [InlineData(0x0000_0001L, 0)]  // 僅低位為 1，HIWORD 為 0
    [InlineData(0x0003_0000L, 0)]  // 未定義側鍵編號
    public void ParseXButtonAction_ParsesWin32WParamHiWord(long wParam, int expectedAction)
    {
        Assert.Equal(expectedAction, TabNavigationHistory.ParseXButtonAction(wParam));
    }
}

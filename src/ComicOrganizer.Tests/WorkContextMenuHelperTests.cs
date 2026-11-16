using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.UI;
using Xunit;

namespace ComicOrganizer.Tests;

public class WorkContextMenuHelperTests : IDisposable
{
    private readonly string _testRoot;

    public WorkContextMenuHelperTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "WorkContextMenuHelperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, true);
        }
        catch
        {
            // Ignore temp cleanup failure
        }
    }

    private string CreateFile(string relativePath)
    {
        var full = Path.Combine(_testRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "dummy");
        return full;
    }

    [Fact]
    public void IsWorkAsset_ArchiveAndPdf_ReturnsTrue_SingleImageAndTxt_ReturnsFalse()
    {
        var cbz = CreateFile("book.cbz");
        var pdf = CreateFile("book.pdf");
        var jpg = CreateFile("page.jpg");
        var txt = CreateFile("readme.txt");

        Assert.True(WorkContextMenuHelper.IsWorkAsset(cbz));
        Assert.True(WorkContextMenuHelper.IsWorkAsset(pdf));
        Assert.False(WorkContextMenuHelper.IsWorkAsset(jpg));
        Assert.False(WorkContextMenuHelper.IsWorkAsset(txt));
        Assert.False(WorkContextMenuHelper.IsWorkAsset(""));
        Assert.False(WorkContextMenuHelper.IsWorkAsset(Path.Combine(_testRoot, "missing.zip")));
    }

    [Fact]
    public void IsWorkAsset_Folder_RequiresAtLeastTwoImagesAndNoSubdirectories()
    {
        var workDir = Path.Combine(_testRoot, "WorkDir");
        Directory.CreateDirectory(workDir);
        CreateFile(Path.Combine("WorkDir", "001.jpg"));
        CreateFile(Path.Combine("WorkDir", "002.png"));

        var singleImgDir = Path.Combine(_testRoot, "SingleImgDir");
        Directory.CreateDirectory(singleImgDir);
        CreateFile(Path.Combine("SingleImgDir", "001.jpg"));

        var parentWithSubDir = Path.Combine(_testRoot, "ParentWithSub");
        Directory.CreateDirectory(Path.Combine(parentWithSubDir, "SubChapter"));
        CreateFile(Path.Combine("ParentWithSub", "001.jpg"));
        CreateFile(Path.Combine("ParentWithSub", "002.jpg"));

        Assert.True(WorkContextMenuHelper.IsWorkAsset(workDir));
        Assert.False(WorkContextMenuHelper.IsWorkAsset(singleImgDir));
        Assert.False(WorkContextMenuHelper.IsWorkAsset(parentWithSubDir));
    }

    [Fact]
    public void CanUseBuiltInReader_ChecksDirectImagesArchivesAndPdf()
    {
        var imgDir = Path.Combine(_testRoot, "ImgDir");
        Directory.CreateDirectory(imgDir);
        CreateFile(Path.Combine("ImgDir", "001.webp"));

        var authorDir = Path.Combine(_testRoot, "AuthorDir");
        Directory.CreateDirectory(Path.Combine(authorDir, "Work1"));
        CreateFile(Path.Combine("AuthorDir", "Work1", "001.jpg"));

        var cbz = CreateFile("comic.cbz");
        var pdf = CreateFile("doc.pdf");
        var singleImg = CreateFile("cover.jpg");
        var txt = CreateFile("notes.txt");

        Assert.True(WorkContextMenuHelper.CanUseBuiltInReader(imgDir));
        Assert.False(WorkContextMenuHelper.CanUseBuiltInReader(authorDir));
        Assert.True(WorkContextMenuHelper.CanUseBuiltInReader(cbz));
        Assert.True(WorkContextMenuHelper.CanUseBuiltInReader(pdf));
        Assert.True(WorkContextMenuHelper.CanUseBuiltInReader(singleImg));
        Assert.False(WorkContextMenuHelper.CanUseBuiltInReader(txt));
    }

    private static List<ContextMenuStripItem> FlattenItems(IContextMenuStripItem[] items)
        => items.OfType<ContextMenuStripItem>().ToList();

    [Fact]
    public void BuildWorkMenuItems_WorkWithReadingStatusAndLibraries_SetsEnableCheckedAndSubmenu()
    {
        var libs = new[]
        {
            new VirtualLibrary(10L, "最愛清單", "", null, 0, true, 1, 1),
            new VirtualLibrary(20L, "待看清單", "tag:foo", null, 1, false, 1, 1),
        };
        var ctx = new WorkMenuContext
        {
            Path = @"C:\Comics\Author\Work.cbz",
            IsFolderNode = false,
            IsWorkOverride = true,
            CanUseBuiltInReaderOverride = true,
            CurrentStatus = ReadStatus.Reading,
            VirtualLibraries = libs,
            WorkLibraryIds = new HashSet<long> { 10L },
        };

        var rawItems = WorkContextMenuHelper.BuildWorkMenuItems(ctx);
        var flat = FlattenItems(rawItems);

        Assert.Equal("👁 閱讀", flat.First(x => x.ID == WorkContextMenuHelper.IdOpenPrimary).Text);
        Assert.Equal("🔗 使用外部閱讀器開啟", flat.First(x => x.ID == WorkContextMenuHelper.IdOpenExternal).Text);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdOpenNewTab);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdOpenExplorer);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdCopyPath);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdRefreshCover);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdAuthorInfo);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdWorkDetail);

        var addToList = flat.First(x => x.ID == WorkContextMenuHelper.IdAddToList);
        Assert.NotNull(addToList.Sub);
        var subItems = FlattenItems(addToList.Sub!);
        var lib10 = subItems.First(x => Equals(x.Tag, 10L));
        var lib20 = subItems.First(x => Equals(x.Tag, 20L));
        Assert.True(lib10.Checked);
        Assert.False(lib20.Checked);
        Assert.Contains(subItems, x => x.ID == WorkContextMenuHelper.IdAddToListCreate);

        Assert.True(flat.First(x => x.ID == WorkContextMenuHelper.IdStatusCompleted).Enabled);
        Assert.False(flat.First(x => x.ID == WorkContextMenuHelper.IdStatusReading).Enabled);
        Assert.True(flat.First(x => x.ID == WorkContextMenuHelper.IdStatusDropped).Enabled);
        Assert.True(flat.First(x => x.ID == WorkContextMenuHelper.IdStatusUnread).Enabled);
    }

    [Theory]
    [InlineData(null, true, true, true, false)]
    [InlineData(ReadStatus.Unread, true, true, true, false)]
    [InlineData(ReadStatus.Reading, true, false, true, true)]
    [InlineData(ReadStatus.Completed, false, true, true, true)]
    [InlineData(ReadStatus.Dropped, true, true, false, true)]
    public void BuildWorkMenuItems_StatusEnableStates_MatchCurrentReadStatus(
        ReadStatus? currentStatus,
        bool expectCompletedEnabled,
        bool expectReadingEnabled,
        bool expectDroppedEnabled,
        bool expectUnreadEnabled)
    {
        var ctx = new WorkMenuContext
        {
            Path = @"C:\Comics\Work.zip",
            IsFolderNode = false,
            IsWorkOverride = true,
            CanUseBuiltInReaderOverride = true,
            CurrentStatus = currentStatus,
        };

        var flat = FlattenItems(WorkContextMenuHelper.BuildWorkMenuItems(ctx));

        Assert.Equal(expectCompletedEnabled, flat.First(x => x.ID == WorkContextMenuHelper.IdStatusCompleted).Enabled);
        Assert.Equal(expectReadingEnabled, flat.First(x => x.ID == WorkContextMenuHelper.IdStatusReading).Enabled);
        Assert.Equal(expectDroppedEnabled, flat.First(x => x.ID == WorkContextMenuHelper.IdStatusDropped).Enabled);
        Assert.Equal(expectUnreadEnabled, flat.First(x => x.ID == WorkContextMenuHelper.IdStatusUnread).Enabled);
    }

    [Fact]
    public void BuildWorkMenuItems_NonWorkDirectory_OmitsWorkOnlyItems()
    {
        var ctx = new WorkMenuContext
        {
            Path = @"C:\Comics\Author",
            IsFolderNode = true,
            IsWorkOverride = false,
            CanUseBuiltInReaderOverride = false,
        };

        var flat = FlattenItems(WorkContextMenuHelper.BuildWorkMenuItems(ctx));

        Assert.Equal("📂 進入目錄", flat.First(x => x.ID == WorkContextMenuHelper.IdOpenPrimary).Text);
        Assert.Equal("🔗 使用外部程式開啟", flat.First(x => x.ID == WorkContextMenuHelper.IdOpenExternal).Text);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdOpenNewTab);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdOpenExplorer);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdCopyPath);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdRefreshCover);
        Assert.Contains(flat, x => x.ID == WorkContextMenuHelper.IdAuthorInfo);

        Assert.DoesNotContain(flat, x => x.ID == WorkContextMenuHelper.IdWorkDetail);
        Assert.DoesNotContain(flat, x => x.ID == WorkContextMenuHelper.IdAddToList);
        Assert.DoesNotContain(flat, x => x.ID != null && x.ID.StartsWith("status_", StringComparison.Ordinal));
    }

    [Fact]
    public void DispatchWorkMenuItem_RoutesByIdAndTag()
    {
        string? refreshed = null;
        string? copied = null;
        (string path, long id)? toggled = null;
        string? createdFor = null;
        (string path, ReadStatus status)? setStatus = null;

        var actions = new WorkMenuActions
        {
            RefreshCover = p => refreshed = p,
            CopyPath = p => copied = p,
            ToggleVirtualLibrary = (p, id) => toggled = (p, id),
            CreateVirtualLibraryAndAdd = p => createdFor = p,
            SetReadStatus = (p, s) => setStatus = (p, s),
        };

        const string targetPath = @"C:\Comics\Work.cbz";
        WorkContextMenuHelper.DispatchWorkMenuItem(
            new ContextMenuStripItem("refresh") { ID = WorkContextMenuHelper.IdRefreshCover }, targetPath, actions);
        WorkContextMenuHelper.DispatchWorkMenuItem(
            new ContextMenuStripItem("copy") { ID = WorkContextMenuHelper.IdCopyPath }, targetPath, actions);
        WorkContextMenuHelper.DispatchWorkMenuItem(
            new ContextMenuStripItem("lib") { ID = WorkContextMenuHelper.IdAddToListToggle, Tag = 42L }, targetPath, actions);
        WorkContextMenuHelper.DispatchWorkMenuItem(
            new ContextMenuStripItem("new") { ID = WorkContextMenuHelper.IdAddToListCreate }, targetPath, actions);
        WorkContextMenuHelper.DispatchWorkMenuItem(
            new ContextMenuStripItem("reading") { ID = WorkContextMenuHelper.IdStatusReading, Tag = ReadStatus.Reading }, targetPath, actions);

        Assert.Equal(targetPath, refreshed);
        Assert.Equal(targetPath, copied);
        Assert.Equal((targetPath, 42L), toggled);
        Assert.Equal(targetPath, createdFor);
        Assert.Equal((targetPath, ReadStatus.Reading), setStatus);
    }

    [Fact]
    public void GetTabCloseIndices_NeverIncludesDashboard_AndComputesSelfOthersRight()
    {
        var tags = new string?[] { "DASHBOARD", @"C:\A", null, @"C:\B" };

        Assert.Empty(WorkContextMenuHelper.GetTabCloseIndices(tags, 0, TabCloseScope.Self));
        Assert.Equal(new[] { 1 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 1, TabCloseScope.Self));

        Assert.Equal(new[] { 1, 2, 3 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 0, TabCloseScope.Others));
        Assert.Equal(new[] { 2, 3 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 1, TabCloseScope.Others));
        Assert.Equal(new[] { 1, 3 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 2, TabCloseScope.Others));

        Assert.Equal(new[] { 1, 2, 3 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 0, TabCloseScope.Right));
        Assert.Equal(new[] { 2, 3 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 1, TabCloseScope.Right));
        Assert.Equal(new[] { 3 }, WorkContextMenuHelper.GetTabCloseIndices(tags, 2, TabCloseScope.Right));
        Assert.Empty(WorkContextMenuHelper.GetTabCloseIndices(tags, 3, TabCloseScope.Right));

        // Only Dashboard + 1 normal tab: closing Others from normal tab must yield empty (protect Dashboard)
        var twoTabs = new string?[] { "DASHBOARD", @"C:\Only" };
        Assert.Empty(WorkContextMenuHelper.GetTabCloseIndices(twoTabs, 1, TabCloseScope.Others));
    }

    [Fact]
    public void BuildTabHeaderMenuItems_And_GetTabCloseTargets_ReflectFolderVirtualAndDashboardStates()
    {
        using var dash = new AntdUI.TabPage { Tag = WorkContextMenuHelper.DashboardTabTag };
        using var folderTab = new AntdUI.TabPage { Tag = @"C:\Comics\Author" };
        using var virtualTab = new AntdUI.TabPage { Tag = null };
        var pages = new[] { dash, folderTab, virtualTab };

        var dashMenu = FlattenItems(WorkContextMenuHelper.BuildTabHeaderMenuItems(pages, dash));
        Assert.True(dashMenu.First(x => x.ID == WorkContextMenuHelper.IdTabRefresh).Enabled);
        Assert.False(dashMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCopyPath).Enabled);
        Assert.False(dashMenu.First(x => x.ID == WorkContextMenuHelper.IdTabOpenExplorer).Enabled);
        Assert.False(dashMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseSelf).Enabled);
        Assert.True(dashMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseOthers).Enabled);
        Assert.True(dashMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseRight).Enabled);

        var virtualMenu = FlattenItems(WorkContextMenuHelper.BuildTabHeaderMenuItems(pages, virtualTab));
        Assert.False(virtualMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCopyPath).Enabled);
        Assert.False(virtualMenu.First(x => x.ID == WorkContextMenuHelper.IdTabOpenExplorer).Enabled);
        Assert.True(virtualMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseSelf).Enabled);
        Assert.True(virtualMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseOthers).Enabled);
        Assert.False(virtualMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseRight).Enabled);

        var folderMenu = FlattenItems(WorkContextMenuHelper.BuildTabHeaderMenuItems(pages, folderTab));
        Assert.True(folderMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCopyPath).Enabled);
        Assert.True(folderMenu.First(x => x.ID == WorkContextMenuHelper.IdTabOpenExplorer).Enabled);
        Assert.True(folderMenu.First(x => x.ID == WorkContextMenuHelper.IdTabCloseSelf).Enabled);

        var closeOthers = WorkContextMenuHelper.GetTabCloseTargets(pages, folderTab, TabCloseScope.Others);
        Assert.Single(closeOthers);
        Assert.Same(virtualTab, closeOthers[0]);
    }
}

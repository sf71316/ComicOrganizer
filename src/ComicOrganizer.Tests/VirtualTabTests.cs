using ComicOrganizer.App.UI;
using Xunit;

namespace ComicOrganizer.Tests;

public class VirtualTabTests : IDisposable
{
    private readonly string _testRoot;

    public VirtualTabTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "VirtualTabTests_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void FilterVirtualPaths_ExcludesFilesInsideIncludedFolder_AndKeepsStandaloneFiles()
    {
        var workFolder = Path.Combine(_testRoot, "WorkA");
        var page1 = Path.Combine(workFolder, "001.jpg");
        var page2 = Path.Combine(workFolder, "002.png");
        var standaloneArchive = Path.Combine(_testRoot, "WorkB", "comic.cbz");

        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { workFolder };
        var result = VirtualTabPathHelper.FilterVirtualPaths(
            new[] { workFolder, page1, page2, standaloneArchive },
            dirs.Contains);

        Assert.Equal(2, result.Count);
        Assert.Equal(workFolder, result[0]);
        Assert.Equal(standaloneArchive, result[1]);
    }

    [Fact]
    public void FilterVirtualPaths_TrailingSeparatorAndCaseDuplicates_AreNormalizedAndDeduplicated()
    {
        var workFolder = Path.Combine(_testRoot, "WorkA");
        var workFolderTrailing = workFolder + Path.DirectorySeparatorChar;
        var workFolderLower = workFolder.ToLowerInvariant();
        var innerFile = Path.Combine(workFolder, "001.jpg");

        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { workFolder };
        var result = VirtualTabPathHelper.FilterVirtualPaths(
            new[] { workFolderTrailing, workFolderLower, innerFile, "", "   " },
            dirs.Contains);

        Assert.Single(result);
        Assert.Equal(workFolderTrailing, result[0]);
    }

    [Fact]
    public void FilterVirtualPaths_KeepsSubdirectoryEvenWhenParentFolderIsIncluded()
    {
        var parentFolder = Path.Combine(_testRoot, "Author");
        var childFolder = Path.Combine(parentFolder, "Work1");

        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { parentFolder, childFolder };
        var result = VirtualTabPathHelper.FilterVirtualPaths(
            new[] { parentFolder, childFolder },
            dirs.Contains);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ResolveExistingItems_SkipsMissingPaths_AndFiltersPagesInsideFolder()
    {
        var workDir = Path.Combine(_testRoot, "WorkFolder");
        Directory.CreateDirectory(workDir);
        var innerImg = Path.Combine(workDir, "01.jpg");
        File.WriteAllText(innerImg, "img");

        var otherDir = Path.Combine(_testRoot, "Other");
        Directory.CreateDirectory(otherDir);
        var archiveFile = Path.Combine(otherDir, "book.zip");
        File.WriteAllText(archiveFile, "zip");

        var missingPath = Path.Combine(_testRoot, "Deleted", "ghost.cbz");

        var items = VirtualTabPathHelper.ResolveExistingItems(
            new[] { workDir, innerImg, archiveFile, missingPath });

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i is DirectoryInfo && string.Equals(i.FullName, workDir, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(items, i => i is FileInfo && string.Equals(i.FullName, archiveFile, StringComparison.OrdinalIgnoreCase));
    }
}

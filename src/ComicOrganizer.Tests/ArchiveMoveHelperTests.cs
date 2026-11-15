using ComicOrganizer.App.Core;
using Xunit;

namespace ComicOrganizer.Tests;

public class ArchiveMoveHelperTests : IDisposable
{
    private readonly string _testRoot;

    public ArchiveMoveHelperTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "ArchiveMoveHelperTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch
        {
            // Ignore temp folder cleanup failure
        }
    }

    [Fact]
    public void MoveItemSafely_File_ShouldSucceed()
    {
        var sourceDir = Path.Combine(_testRoot, "Source");
        var destDir = Path.Combine(_testRoot, "Dest");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, "comic.cbz");
        File.WriteAllText(sourceFile, "dummy content");

        string targetPath = ArchiveMoveHelper.MoveItemSafely(sourceFile, destDir);

        Assert.Equal(Path.Combine(destDir, "comic.cbz"), targetPath);
        Assert.True(File.Exists(targetPath));
        Assert.Equal("dummy content", File.ReadAllText(targetPath));
        Assert.False(File.Exists(sourceFile));
    }

    [Fact]
    public void MoveItemSafely_Directory_ShouldSucceed()
    {
        var sourceDir = Path.Combine(_testRoot, "Source");
        var destDir = Path.Combine(_testRoot, "Dest");
        Directory.CreateDirectory(sourceDir);

        var comicDir = Path.Combine(sourceDir, "[Author] My Manga Vol.01");
        Directory.CreateDirectory(comicDir);
        File.WriteAllText(Path.Combine(comicDir, "001.jpg"), "page 1");

        var subDir = Path.Combine(comicDir, "extras");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "info.txt"), "extra info");

        string targetPath = ArchiveMoveHelper.MoveItemSafely(comicDir, destDir);

        Assert.Equal(Path.Combine(destDir, "[Author] My Manga Vol.01"), targetPath);
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(Path.Combine(targetPath, "001.jpg")));
        Assert.True(File.Exists(Path.Combine(targetPath, "extras", "info.txt")));
        Assert.False(Directory.Exists(comicDir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveItemSafely_EmptyOrWhitespaceDestDir_ShouldThrowAndKeepSource(string? invalidDestDir)
    {
        var sourceDir = Path.Combine(_testRoot, "Source");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, "test.zip");
        File.WriteAllText(sourceFile, "keep me");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ArchiveMoveHelper.MoveItemSafely(sourceFile, invalidDestDir!));

        Assert.Contains("目標路徑不可為空", ex.Message);
        Assert.True(File.Exists(sourceFile));
        Assert.Equal("keep me", File.ReadAllText(sourceFile));
    }

    [Fact]
    public void MoveItemSafely_NonExistentSource_ShouldThrowFileNotFoundException()
    {
        var destDir = Path.Combine(_testRoot, "Dest");
        var nonExistentPath = Path.Combine(_testRoot, "does_not_exist.zip");

        Assert.Throws<FileNotFoundException>(() =>
            ArchiveMoveHelper.MoveItemSafely(nonExistentPath, destDir));
    }

    [Fact]
    public void MoveItemSafely_SameSourceAndTarget_ShouldThrowInvalidOperationException()
    {
        var sourceDir = Path.Combine(_testRoot, "Folder");
        Directory.CreateDirectory(sourceDir);

        var sourceFile = Path.Combine(sourceDir, "test.zip");
        File.WriteAllText(sourceFile, "same");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ArchiveMoveHelper.MoveItemSafely(sourceFile, sourceDir));

        Assert.Contains("相同", ex.Message);
        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public void MoveItemSafely_TargetFileAlreadyExists_ShouldThrowIOExceptionAndKeepBoth()
    {
        var sourceDir = Path.Combine(_testRoot, "Source");
        var destDir = Path.Combine(_testRoot, "Dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        var sourceFile = Path.Combine(sourceDir, "comic.zip");
        var destFile = Path.Combine(destDir, "comic.zip");

        File.WriteAllText(sourceFile, "source content");
        File.WriteAllText(destFile, "dest content");

        var ex = Assert.Throws<IOException>(() =>
            ArchiveMoveHelper.MoveItemSafely(sourceFile, destDir));

        Assert.Contains("目標已存在同名項目", ex.Message);
        Assert.True(File.Exists(sourceFile));
        Assert.Equal("source content", File.ReadAllText(sourceFile));
        Assert.True(File.Exists(destFile));
        Assert.Equal("dest content", File.ReadAllText(destFile));
    }

    [Fact]
    public void MoveItemSafely_TargetDirectoryAlreadyExists_ShouldThrowIOExceptionAndKeepBoth()
    {
        var sourceDir = Path.Combine(_testRoot, "Source");
        var destDir = Path.Combine(_testRoot, "Dest");

        var sourceComic = Path.Combine(sourceDir, "ComicSeries");
        var destComic = Path.Combine(destDir, "ComicSeries");

        Directory.CreateDirectory(sourceComic);
        Directory.CreateDirectory(destComic);

        File.WriteAllText(Path.Combine(sourceComic, "source.txt"), "source data");
        File.WriteAllText(Path.Combine(destComic, "dest.txt"), "dest data");

        var ex = Assert.Throws<IOException>(() =>
            ArchiveMoveHelper.MoveItemSafely(sourceComic, destDir));

        Assert.Contains("目標已存在同名項目", ex.Message);
        Assert.True(Directory.Exists(sourceComic));
        Assert.True(File.Exists(Path.Combine(sourceComic, "source.txt")));
        Assert.True(Directory.Exists(destComic));
        Assert.True(File.Exists(Path.Combine(destComic, "dest.txt")));
    }

    [Fact]
    public void CopyDirectory_ShouldRecursivelyCopyAllFilesAndSubdirectories()
    {
        var sourceDir = Path.Combine(_testRoot, "CopySource");
        var destDir = Path.Combine(_testRoot, "CopyDest");

        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "root");

        var subDir = Path.Combine(sourceDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "sub.txt"), "sub");

        ArchiveMoveHelper.CopyDirectory(sourceDir, destDir);

        Assert.True(Directory.Exists(destDir));
        Assert.True(File.Exists(Path.Combine(destDir, "root.txt")));
        Assert.Equal("root", File.ReadAllText(Path.Combine(destDir, "root.txt")));
        Assert.True(File.Exists(Path.Combine(destDir, "sub", "sub.txt")));
        Assert.Equal("sub", File.ReadAllText(Path.Combine(destDir, "sub", "sub.txt")));
    }

    [Fact]
    public void CopyDirectory_WhenFileAlreadyExists_ShouldThrowIOExceptionWithoutOverwriting()
    {
        var sourceDir = Path.Combine(_testRoot, "CopySourceConflict");
        var destDir = Path.Combine(_testRoot, "CopyDestConflict");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        File.WriteAllText(Path.Combine(sourceDir, "same.txt"), "new content");
        File.WriteAllText(Path.Combine(destDir, "same.txt"), "original content");

        Assert.Throws<IOException>(() =>
            ArchiveMoveHelper.CopyDirectory(sourceDir, destDir));

        Assert.Equal("original content", File.ReadAllText(Path.Combine(destDir, "same.txt")));
    }
}

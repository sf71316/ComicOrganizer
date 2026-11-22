using ComicOrganizer.App.Core;
using ComicOrganizer.Avalonia.Services;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// 「現場解出封面後寫回縮圖」背景佇列（<see cref="ThumbWriteBackQueue"/>）。
/// 全部在暫存目錄進行，不碰真實資料目錄。
/// </summary>
public class ThumbWriteBackQueueTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly SqliteAssetMetaStore _store;
    private readonly FileSystemThumbStore _thumbs;

    public ThumbWriteBackQueueTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comic_thumb_writeback_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "cache.db");
        _store = new SqliteAssetMetaStore(_dbPath);
        _thumbs = new FileSystemThumbStore(Path.Combine(_tempDir, "thumbs"));
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略暫存目錄清理失敗
        }
    }

    [Fact]
    public void Submit_CatalogAssetWithoutThumb_WritesThumbFileAndIndex()
    {
        string path = AddCatalogAsset("work1.zip");
        byte[] jpeg = CreateJpeg(330, 470);

        using (var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0))
        {
            Assert.True(queue.TrySubmit(path, jpeg));
        }

        var meta = _store.Get(path);
        Assert.NotNull(meta);
        string expectedKey = ThumbWriteBackQueue.BuildThumbKey(path);
        Assert.Equal(expectedKey, meta!.CoverThumbKey);
        Assert.Equal(ThumbWriteBackQueue.CoverOriginTag, meta.CoverOrigin);
        Assert.Equal(330, meta.CoverWidth);
        Assert.Equal(470, meta.CoverHeight);

        // 作品列其他欄位不被改動
        Assert.Equal("work1.zip", meta.Name);
        Assert.Equal(12345, meta.FileSize);
        Assert.Equal(1_700_000_000, meta.Mtime);

        using (var stream = _thumbs.OpenRead(expectedKey))
        {
            Assert.NotNull(stream);
            Assert.Equal(jpeg.Length, stream!.Length);
        }
    }

    [Fact]
    public void Submit_PathNotInCatalog_WritesNothing()
    {
        string path = Path.Combine(_tempDir, "not_in_catalog.zip");

        using (var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0))
        {
            Assert.True(queue.TrySubmit(path, CreateJpeg(100, 140)));
        }

        Assert.Null(_store.Get(path));
        Assert.Empty(_thumbs.EnumerateAllKeys());
    }

    [Fact]
    public void Submit_SingleImageFile_IsSkipped()
    {
        string path = AddCatalogAsset("page01.jpg");

        using (var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0))
        {
            queue.TrySubmit(path, CreateJpeg(100, 140));
        }

        Assert.Null(_store.Get(path)!.CoverThumbKey);
        Assert.Empty(_thumbs.EnumerateAllKeys());
    }

    [Fact]
    public void Submit_OverLimit_StopsWritingAndMarksFull()
    {
        // 既有縮圖已佔一些空間，上限設得比「既有 + 新的一張」小
        _thumbs.Write("aa_existing", new byte[2000]);
        string path = AddCatalogAsset("work2.zip");
        byte[] jpeg = CreateJpeg(330, 470);

        ThumbWriteBackQueue queue = new(_store, _thumbs, () => 2000 + jpeg.Length - 1);
        try
        {
            queue.TrySubmit(path, jpeg);
        }
        finally
        {
            queue.Dispose();
        }

        Assert.True(queue.IsFull);
        Assert.Equal(0, queue.WrittenCount);
        Assert.Null(_store.Get(path)!.CoverThumbKey);
        Assert.Equal(new[] { "aa_existing" }, _thumbs.EnumerateAllKeys().ToArray());

        // 已達上限後不再收件
        Assert.False(queue.TrySubmit(AddCatalogAsset("work3.zip"), jpeg));
    }

    [Fact]
    public void Submit_ExistingReadableThumb_IsNotOverwritten()
    {
        string path = AddCatalogAsset("work4.zip");
        byte[] original = CreateJpeg(200, 280);
        _thumbs.Write("bb_original", original);
        var meta = _store.Get(path)!;
        meta.CoverThumbKey = "bb_original";
        meta.CoverOrigin = "skia-sync";
        _store.Upsert(meta);

        using (var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0))
        {
            queue.TrySubmit(path, CreateJpeg(330, 470));
        }

        var after = _store.Get(path)!;
        Assert.Equal("bb_original", after.CoverThumbKey);
        Assert.Equal("skia-sync", after.CoverOrigin);
        using (var stream = _thumbs.OpenRead("bb_original"))
        {
            Assert.Equal(original.Length, stream!.Length);
        }

        Assert.Single(_thumbs.EnumerateAllKeys());
    }

    [Fact]
    public void Submit_IndexedKeyButFileMissing_RewritesFileUnderSameKey()
    {
        string path = AddCatalogAsset("work5.zip");
        var meta = _store.Get(path)!;
        meta.CoverThumbKey = "cc_missing";
        _store.Upsert(meta);

        using (var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0))
        {
            queue.TrySubmit(path, CreateJpeg(330, 470));
        }

        Assert.Equal("cc_missing", _store.Get(path)!.CoverThumbKey);
        using (var stream = _thumbs.OpenRead("cc_missing"))
        {
            Assert.NotNull(stream);
        }
    }

    [Fact]
    public void Submit_SamePathTwice_OnlyFirstAccepted()
    {
        string path = AddCatalogAsset("work6.zip");
        using (var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0))
        {
            Assert.True(queue.TrySubmit(path, CreateJpeg(100, 140)));
            Assert.False(queue.TrySubmit(path, CreateJpeg(100, 140)));
        }
    }

    [Fact]
    public void Submit_AfterDispose_IsRejected()
    {
        string path = AddCatalogAsset("work7.zip");
        var queue = new ThumbWriteBackQueue(_store, _thumbs, () => 0);
        queue.Dispose();

        Assert.False(queue.TrySubmit(path, CreateJpeg(100, 140)));
        Assert.Null(_store.Get(path)!.CoverThumbKey);
    }

    [Fact]
    public void AppDataContext_ReadOnly_DoesNotCreateWriteBack()
    {
        string dataDir = Path.Combine(_tempDir, "ctx");
        Directory.CreateDirectory(dataDir);

        using (var roCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: true))
        {
            Assert.Null(roCtx.CreateThumbWriteBack());
        }

        using (var rwCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false))
        {
            using (var writeBack = rwCtx.CreateThumbWriteBack())
            {
                Assert.NotNull(writeBack);
            }
        }
    }

    private string AddCatalogAsset(string name)
    {
        string path = Path.Combine(_tempDir, "lib", name);
        _store.UpsertCatalogOnly(new AssetMeta
        {
            Path = path,
            Name = name,
            IsFolder = false,
            FileSize = 12345,
            Mtime = 1_700_000_000,
        });
        return path;
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using (var bitmap = new SKBitmap(width, height))
        {
            bitmap.Erase(new SKColor(90, 150, 210));
            using (var image = SKImage.FromBitmap(bitmap))
            {
                using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 85))
                {
                    return data.ToArray();
                }
            }
        }
    }
}

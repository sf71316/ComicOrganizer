using ComicOrganizer.App.Core;
using ComicOrganizer.App.UI;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using SkiaSharp;
using Xunit;

namespace ComicOrganizer.Tests;

public class ReaderViewModelTests
{
    private static string CreateTempComicFolder(out string[] pageFiles)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ComicOrg_ReaderVmTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        pageFiles = new string[3];
        for (int i = 0; i < 3; i++)
        {
            using var bmp = new SKBitmap(64, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(new SKColor((byte)(220 + i * 10), 230, 240));
            using var img = SKImage.FromBitmap(bmp);
            using var encoded = img.Encode(SKEncodedImageFormat.Png, 90);
            var filePath = Path.Combine(dir, $"00{i + 1}.png");
            File.WriteAllBytes(filePath, encoded.ToArray());
            pageFiles[i] = filePath;
        }
        return dir;
    }

    private static string CreateTempDataDir(out SqliteAssetMetaStore writableStore, string comicDir)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "ComicOrg_DataDirTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "cache.db");
        writableStore = new SqliteAssetMetaStore(dbPath);
        writableStore.UpsertCatalogOnly(new AssetMeta
        {
            Path = comicDir,
            Name = Path.GetFileName(comicDir),
            IsFolder = true,
            Mtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        long assetId = writableStore.GetAssetId(comicDir)!.Value;
        writableStore.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            Title = "Test Comic Title",
            LastPage = 1,
            PageCount = 3
        });
        return dataDir;
    }

    [Fact]
    public void ReaderViewModel_ResumesLastPage_AndTogglesModesAndEnhancementPresets()
    {
        var comicDir = CreateTempComicFolder(out _);
        var dataDir = CreateTempDataDir(out var setupStore, comicDir);
        setupStore.Dispose();

        try
        {
            using var ctx = new AppDataContext(explicitDataDirectory: dataDir);
            using var vm = new ReaderViewModel(
                ctx,
                comicDir,
                runOnUiAsync: action => { action(); return Task.CompletedTask; });

            Assert.Equal(3, vm.PageCount);
            // 應恢復到 db 中的 LastPage = 1（即 2 / 3）
            Assert.Equal(1, vm.CurrentPageIndex);
            Assert.Equal("2 / 3", vm.PageCounterText);
            Assert.Equal(3, vm.Thumbnails.Count);

            // 切換連續長條與韓漫無縫頁距
            vm.SetViewModeCommand.Execute("ContinuousScroll");
            Assert.True(vm.IsContinuousMode);
            Assert.False(vm.ShowDirectionToggle);
            Assert.True(vm.ShowContinuousGapToggle);
            Assert.Equal(8, vm.ContinuousGapPx);

            vm.ToggleContinuousGapCommand.Execute(null);
            Assert.True(vm.IsGaplessWebtoon);
            Assert.Equal(0, vm.ContinuousGapPx);
            Assert.Equal("韓漫無縫 (0px)", vm.ContinuousGapLabel);

            // 切換畫質預設 & 自訂滑桿參數
            vm.ApplyEnhancementPresetCommand.Execute("VintageBleach");
            Assert.Equal(ReaderImageEnhancementPreset.VintageBleach, vm.ActiveEnhancementPreset);
            Assert.True(vm.IsPresetVintageBleach);
            Assert.True(vm.EnhancementOptions.WhitePoint < 0.90f);

            vm.SharpenAmount = 1.15;
            Assert.Equal(ReaderImageEnhancementPreset.Custom, vm.ActiveEnhancementPreset);
            Assert.InRange(vm.EnhancementOptions.SharpenAmount, 1.14f, 1.16f);
        }
        finally
        {
            try { Directory.Delete(comicDir, true); } catch { }
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }

    [Fact]
    public void ReaderViewModel_WritableVsReadOnlyBookmarksAndProgressRespectDatabaseProtection()
    {
        var comicDir = CreateTempComicFolder(out _);
        var dataDir = CreateTempDataDir(out var setupStore, comicDir);
        var assetId = setupStore.GetAssetId(comicDir)!.Value;
        setupStore.AddBookmark(assetId, 0, "預設書籤");
        setupStore.Dispose();

        try
        {
            // 非唯讀模式：書籤新增與頁碼儲存應寫入資料庫
            using (var writableCtx = new AppDataContext(explicitDataDirectory: dataDir))
            {
                Assert.False(writableCtx.IsReadOnlyDatabase);
                using var vm = new ReaderViewModel(
                    writableCtx,
                    comicDir,
                    runOnUiAsync: action => { action(); return Task.CompletedTask; });

                Assert.Single(vm.Bookmarks);
                Assert.Equal(1, vm.CurrentPageIndex);

                vm.NotifyViewportPageChanged(2);
                vm.ToggleCurrentBookmarkCommand.Execute(null);
                Assert.Equal(2, vm.Bookmarks.Count);
                Assert.True(vm.IsCurrentPageBookmarked);
            }

            // 重新開啟資料庫驗證已被寫入
            using (var checkStore = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db")))
            {
                Assert.Equal(2, checkStore.GetLastPage(comicDir));
                Assert.Equal(2, checkStore.ListBookmarks(assetId).Count);
            }
        }
        finally
        {
            try { Directory.Delete(comicDir, true); } catch { }
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }

    [Fact]
    public async Task AllListViewModels_RouteDefaultWorkClickToBuiltInReader_AndKeepExternalLaunchOption()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ComicOrg_LauncherWireTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var thumbDir = Path.Combine(tempRoot, "thumbs");
        Directory.CreateDirectory(thumbDir);
        var archiveFile = Path.Combine(tempRoot, "sample_work.cbz");
        File.WriteAllBytes(archiveFile, new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        using var store = new SqliteAssetMetaStore(Path.Combine(tempRoot, "cache.db"));
        store.UpsertCatalogOnly(new AssetMeta
        {
            Path = archiveFile,
            Name = "sample_work.cbz",
            IsFolder = false,
            Mtime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
        long assetId = store.GetAssetId(archiveFile)!.Value;
        store.UpsertMetadata(new AssetMetadata
        {
            AssetId = assetId,
            Title = "Sample Work",
            ReadStatus = ReadStatus.Reading,
            LastPage = 0,
            PageCount = 12
        });

        using var coverService = new SkiaCoverService(new FileSystemThumbStore(thumbDir));
        var extSpy = new RecordingExternalLauncher();
        var readerSpy = new RecordingReaderLauncher();
        Func<Action, Task> syncUi = action => { action(); return Task.CompletedTask; };

        try
        {
            // 1. ReadingTrackPageViewModel
            using (var trackVm = new ReadingTrackPageViewModel(
                store,
                coverService,
                externalLauncher: extSpy,
                toastManager: null,
                runOnUiAsync: syncUi,
                autoLoad: false,
                isReadOnlyDatabase: true,
                readerLauncher: readerSpy))
            {
                await trackVm.RefreshAsync();
                var item = Assert.Single(trackVm.InProgressWorks);
                item.OpenCommand.Execute(null);
                item.OpenExternalCommand.Execute(null);
                Assert.Contains(archiveFile, readerSpy.OpenedPaths);
                Assert.Contains(archiveFile, extSpy.OpenedPaths);
            }

            readerSpy.OpenedPaths.Clear();
            extSpy.OpenedPaths.Clear();

            // 2. GalleryPageViewModel + DetailDrawer
            using (var galleryVm = new GalleryPageViewModel(
                store,
                coverService,
                rootDirectory: tempRoot,
                externalLauncher: extSpy,
                toastManager: null,
                runOnUiAsync: syncUi,
                autoLoad: false,
                isReadOnlyDatabase: true,
                readerLauncher: readerSpy))
            {
                await galleryVm.RefreshAsync();
                var galleryItem = galleryVm.Items.First(i => string.Equals(i.Path, archiveFile, StringComparison.OrdinalIgnoreCase));
                galleryItem.ActivateCommand.Execute(null);
                galleryItem.InspectDetailCommand.Execute(null);
                galleryVm.DetailDrawer.OpenTargetCommand.Execute(null);
                galleryVm.DetailDrawer.OpenExternalCommand.Execute(null);
                Assert.Equal(2, readerSpy.OpenedPaths.Count);
                Assert.Single(extSpy.OpenedPaths);
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void ReaderViewModel_SyncsSettingsOcrAndTooltipsLiveWhenConfigSaved()
    {
        var comicDir = CreateTempComicFolder(out _);
        var dataDir = CreateTempDataDir(out var setupStore, comicDir);
        setupStore.Dispose();

        try
        {
            using var ctx = new AppDataContext(explicitDataDirectory: dataDir);
            var settingsVm = new SettingsPageViewModel(ctx);
            Assert.NotEmpty(settingsVm.OcrEngineOptions);
            Assert.Contains(settingsVm.OcrEngineOptions, o => o.Value == 0);
            Assert.Contains(settingsVm.OcrEngineOptions, o => o.Value == 1);

            using var vm = new ReaderViewModel(
                ctx,
                comicDir,
                runOnUiAsync: action => { action(); return Task.CompletedTask; });

            Assert.Contains("系統設定", vm.OcrEngineSummaryText);
            Assert.Contains("RTL", vm.DirectionToggleTooltip);
            vm.ToggleDirectionCommand.Execute(null);
            Assert.Contains("LTR", vm.DirectionToggleTooltip);

            vm.SetViewModeCommand.Execute("ContinuousScroll");
            Assert.Contains("標準頁距", vm.ContinuousGapTooltip);
            vm.ToggleContinuousGapCommand.Execute(null);
            Assert.Contains("韓漫無縫", vm.ContinuousGapTooltip);

            int initialEngine = vm.Pipeline.OcrEngineType;
            int targetEngine = vm.OcrEngineOptions.FirstOrDefault(o => o.IsAvailable && o.EngineType != initialEngine)?.EngineType
                               ?? initialEngine;

            var updated = new ComicOrganizer.App.Models.AppConfig
            {
                UnorganizedDirectory = ctx.Config.UnorganizedDirectory,
                RootDirectory = ctx.Config.RootDirectory,
                OcrEngineType = targetEngine,
                MaskEngineType = 2,
                TranslationModelName = "test-model-updated"
            };
            ctx.SaveConfig(updated);

            Assert.Equal(targetEngine, vm.Pipeline.OcrEngineType);
            Assert.Equal(2, vm.Pipeline.MaskEngineType);
            Assert.Contains("test-model-updated", vm.LlmSummaryText);

            updated.MaskEngineType = 0;
            ctx.SaveConfig(updated);
            Assert.Equal(0, vm.Pipeline.MaskEngineType);
        }
        finally
        {
            try { Directory.Delete(comicDir, true); } catch { }
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }

    [Fact]
    public void SettingsPageViewModel_RefreshReaderModelStatus_ReflectsOnnxPresenceAndMaskOption()
    {
        var comicDir = CreateTempComicFolder(out _);
        var dataDir = CreateTempDataDir(out var setupStore, comicDir);
        setupStore.Dispose();

        try
        {
            using var ctx = new AppDataContext(explicitDataDirectory: dataDir);
            var settingsVm = new SettingsPageViewModel(ctx, runOnUiAsync: static a => { a(); return Task.CompletedTask; });

            Assert.Equal(0, ctx.Config.MaskEngineType);
            Assert.Equal(0, settingsVm.SelectedMaskEngine.Value);
            Assert.Contains("尚未安裝", settingsVm.TextMaskModelStatusText);
            Assert.False(settingsVm.IsTextMaskModelReady);
            Assert.Contains(settingsVm.MaskEngineOptions, o => o.Value == 2);
            Assert.Contains(settingsVm.MaskEngineOptions, o => o.Value == 0);

            // 模擬設定頁下載後模型檔就位（達合理容量門檻 >= 90MB）
            var inferDir = Path.Combine(dataDir, "inference");
            Directory.CreateDirectory(inferDir);
            var modelPath = Path.Combine(inferDir, ModelDownloader.ComicTextDetectorFileName);
            using (var fs = File.Create(modelPath))
            {
                fs.SetLength(92_000_000L);
            }

            settingsVm.RefreshReaderModelStatusCommand.Execute(null);
            Assert.True(settingsVm.IsTextMaskModelReady);
            Assert.Contains("已就緒", settingsVm.TextMaskModelStatusText);

            // 確認可切換並儲存選配的 ONNX 精準遮罩 (2)，也可切回預設矩形遮罩 (0)
            settingsVm.SelectedMaskEngine = settingsVm.MaskEngineOptions.First(o => o.Value == 2);
            settingsVm.SaveCommand.Execute(null);
            Assert.Equal(2, ctx.Config.MaskEngineType);

            settingsVm.SelectedMaskEngine = settingsVm.MaskEngineOptions.First(o => o.Value == 0);
            settingsVm.SaveCommand.Execute(null);
            Assert.Equal(0, ctx.Config.MaskEngineType);
        }
        finally
        {
            try { Directory.Delete(comicDir, true); } catch { }
            try { Directory.Delete(dataDir, true); } catch { }
        }
    }

    private sealed class RecordingReaderLauncher : IReaderWindowLauncher
    {
        public List<string> OpenedPaths { get; } = new();

        public bool OpenInReader(string path, string? displayTitle, string? authorName, Action? onReaderClosed, out string? errorMessage)
        {
            OpenedPaths.Add(path);
            errorMessage = null;
            return true;
        }
    }

    private sealed class RecordingExternalLauncher : IExternalLauncher
    {
        public List<string> OpenedPaths { get; } = new();

        public bool OpenWithDefaultApp(string path, out string? errorMessage)
        {
            OpenedPaths.Add(path);
            errorMessage = null;
            return true;
        }

        public bool RevealInFileExplorer(string path, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }
}

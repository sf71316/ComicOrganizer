using System.Text;
using System.Text.Json;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.Avalonia.Services;
using ComicOrganizer.Avalonia.ViewModels;
using Xunit;

namespace ComicOrganizer.Tests;

/// <summary>
/// Phase 5（W5.1 ～ W5.4）Metadata 保險箱完整單元與整合測試：
/// 匯出 JSON / CSV、四層比對與只補新增匯入、未對應清單持久化與手動對應、SQLite 熱備份（保留 10 份）與安全還原。
/// </summary>
public sealed class MetadataVaultTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ComicOrg_Vault_{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var d in _tempDirs)
        {
            try
            {
                if (Directory.Exists(d))
                {
                    Directory.Delete(d, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task W5_1_ExportSnapshotJsonAndCsv_IncludesDictionariesWorksBookmarksVlibsAndMappings()
    {
        var dataDir = CreateTempDir("W51Data");
        var rootDir = CreateTempDir("W51Root");
        var outDir = CreateTempDir("W51Out");

        using var store = new SqliteAssetMetaStore(Path.Combine(dataDir, "cache.db"));
        var intel = new MappingIntelligence(Path.Combine(dataDir, "mappings.json"));
        intel.Learn("SampleArtist", "SampleArtistFolder");

        long aid = store.UpsertAuthor("SampleArtist", Path.Combine(rootDir, "SampleArtist"));
        store.AddAuthorAlias(aid, "サンプル作者", "ja");
        long tGroup = store.UpsertTag("full color", "content");
        long tVirt = store.UpsertTag("anthology", "virtual");
        store.UpsertTagTranslation(tGroup, "zh-TW", "全彩", "manual");
        store.AddTagAlias(tVirt, "collection");
        store.AssociateAuthorTag(aid, tGroup);

        long vlId = store.CreateVirtualLibrary("精選全彩", "tag:\"full color\"", null);
        store.SetVirtualLibraryPinned(vlId, true);

        string workPath = Path.Combine(rootDir, "SampleArtist", "[SampleArtist] Demo Comic.zip");
        store.Upsert(new AssetMeta
        {
            Path = workPath,
            Name = Path.GetFileName(workPath),
            IsFolder = false,
            FileSize = 4096,
            Mtime = 1000,
            LastScanned = 1000,
        });
        long workId = store.GetAssetId(workPath)!.Value;
        store.UpsertMetadata(new AssetMetadata
        {
            AssetId = workId,
            AuthorId = aid,
            Title = "Demo, \"Special\" Title",
            Series = "Demo Series",
            ChapterNo = "Ch.1",
            Language = "zh-TW",
            TranslationGroup = "DemoGroup",
            PageCount = 32,
            Rating = 5,
            ReadStatus = ReadStatus.Reading,
            ReadCount = 3,
            LastRead = 1700000000,
            LastPage = 12,
            ExternalId = "nh:123456",
            ManuallyEdited = true,
            ParsedAt = 1000
        });
        store.SetAssetTagEntries(workId, new[] { new TagEntry("full color", "content") });
        store.AddBookmark(workId, 5, "精彩頁");
        store.AddWorkToVirtualLibrary(vlId, workPath);
        store.UpsertExternalSource(new AssetExternalSource
        {
            AssetId = workId,
            Source = "nhentai",
            SourceId = "123456",
            RawJson = "{\"id\":123456}",
            FetchedAt = 1000
        });

        var svc = new MetadataSnapshotService(store, dataDir, rootDir);
        string compactJsonPath = Path.Combine(outDir, "compact.json");
        var summary = await svc.ExportToJsonAsync(
            compactJsonPath,
            new MetadataSnapshotExportOptions(IncludeExternalSources: false, IncludeLearnedMappings: true));

        Assert.Equal(1, summary.WorkCount);
        Assert.Equal(1, summary.AuthorCount);
        Assert.Equal(2, summary.TagCount);
        Assert.Equal(1, summary.TagTranslationCount);
        Assert.Equal(1, summary.BookmarkCount);
        Assert.Equal(1, summary.VirtualLibraryCount);
        Assert.Equal(1, summary.LearnedMappingCount);

        var loadedDoc = JsonSerializer.Deserialize<MetadataSnapshotDocument>(
            await File.ReadAllTextAsync(compactJsonPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.NotNull(loadedDoc);
        Assert.False(loadedDoc!.IncludesExternalSources);
        Assert.Empty(loadedDoc.Works[0].ExternalSources);
        Assert.Equal("SampleArtist\\[SampleArtist] Demo Comic.zip".Replace('\\', Path.DirectorySeparatorChar), loadedDoc.Works[0].RelativePath);

        string fullJsonPath = Path.Combine(outDir, "full.json");
        await svc.ExportToJsonAsync(
            fullJsonPath,
            new MetadataSnapshotExportOptions(IncludeExternalSources: true, IncludeLearnedMappings: true));
        var fullDoc = JsonSerializer.Deserialize<MetadataSnapshotDocument>(
            await File.ReadAllTextAsync(fullJsonPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Single(fullDoc!.Works[0].ExternalSources);

        string csvPath = Path.Combine(outDir, "catalog.csv");
        int csvCount = await svc.ExportWorksToCsvAsync(csvPath);
        Assert.Equal(1, csvCount);
        byte[] csvBytes = await File.ReadAllBytesAsync(csvPath);
        Assert.True(csvBytes.Length > 3 && csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF);
        string csvText = Encoding.UTF8.GetString(csvBytes);
        Assert.Contains("\"Demo, \"\"Special\"\" Title\"", csvText);
        Assert.Contains("SampleArtist", csvText);
    }

    [Fact]
    public async Task W5_2_ImportAnalyzeAndApply_FourTierMatching_AndDefaultFillPreservesManuallyEdited()
    {
        var oldData = CreateTempDir("OldData");
        var oldRoot = CreateTempDir("OldRoot");
        var newData = CreateTempDir("NewData");
        var newRoot = CreateTempDir("NewRoot");
        var snapshotPath = Path.Combine(CreateTempDir("SnapOut"), "snapshot.json");

        string pathTier1 = Path.Combine(oldRoot, "AuthorA", "Tier1.zip");
        string oldTier2 = Path.Combine(oldRoot, "AuthorB", "Tier2.zip");
        string oldTier3 = Path.Combine(oldRoot, "OldBucket", "UniqueNameSize.zip");
        string oldUnmatched = Path.Combine(oldRoot, "GhostFolder", "MissingBook.zip");
        string oldAmbiguous = Path.Combine(oldRoot, "FolderOne", "SameDup.zip");

        // 建立舊庫並匯出快照
        using (var srcStore = new SqliteAssetMetaStore(Path.Combine(oldData, "cache.db")))
        {
            var srcIntel = new MappingIntelligence(Path.Combine(oldData, "mappings.json"));
            srcIntel.Learn("ImportedCircle", "ImportedArtist");

            long aid = srcStore.UpsertAuthor("ArtistImported");
            srcStore.AddAuthorAlias(aid, "インポート作者", "ja");
            long tNew = srcStore.UpsertTag("mosaic censorship", "content");
            srcStore.UpsertTagTranslation(tNew, "zh-TW", "馬賽克", "manual");

            void SeedOld(string p, string title, long size, int rating, ReadStatus st, int lastPg, string bmLabel)
            {
                srcStore.Upsert(new AssetMeta { Path = p, Name = Path.GetFileName(p), IsFolder = false, FileSize = size, Mtime = 100, LastScanned = 100 });
                long id = srcStore.GetAssetId(p)!.Value;
                srcStore.UpsertMetadata(new AssetMetadata
                {
                    AssetId = id,
                    AuthorId = aid,
                    Title = title,
                    Language = "ja",
                    Rating = rating,
                    ReadStatus = st,
                    ReadCount = st == ReadStatus.Unread ? 0 : 2,
                    LastPage = lastPg,
                    ParsedAt = 100
                });
                srcStore.SetAssetTagEntries(id, new[] { new TagEntry("mosaic censorship", "content") });
                srcStore.AddBookmark(id, 3, bmLabel);
            }

            SeedOld(pathTier1, "Snap Title 1", 1111, 5, ReadStatus.Completed, 20, "Snap BM1");
            SeedOld(oldTier2, "Snap Title 2", 2222, 4, ReadStatus.Reading, 7, "Snap BM2");
            SeedOld(oldTier3, "Snap Title 3", 3333, 5, ReadStatus.Completed, 15, "Snap BM3");
            SeedOld(oldUnmatched, "Missing Book", 4444, 3, ReadStatus.Unread, 0, "Snap BM4");
            SeedOld(oldAmbiguous, "Ambiguous Book", 5555, 5, ReadStatus.Unread, 0, "Snap BM5");

            var srcSvc = new MetadataSnapshotService(srcStore, oldData, oldRoot);
            await srcSvc.ExportToJsonAsync(snapshotPath);
        }

        // 建立新庫：
        // - pathTier1 保持相同完整路徑，但本地已 ManuallyEdited 且 Rating=2，應保留本地標題與評分，僅補缺失書籤/標籤
        // - newTier2 位於新根目錄相同相對路徑
        // - newTier3 移至新資料夾名但同檔名+大小且唯一
        // - Duplicate x2 不應錯誤自動匹配（歸入未對應）
        using var dstStore = new SqliteAssetMetaStore(Path.Combine(newData, "cache.db"));
        string newTier2 = Path.Combine(newRoot, "AuthorB", "Tier2.zip");
        string newTier3 = Path.Combine(newRoot, "CompletelyRenamedFolder", "UniqueNameSize.zip");
        string newDupA = Path.Combine(newRoot, "Dup1", "SameDup.zip");
        string newDupB = Path.Combine(newRoot, "Dup2", "SameDup.zip");

        foreach (var (p, sz) in new[] { (pathTier1, 1111L), (newTier2, 2222L), (newTier3, 3333L), (newDupA, 5555L), (newDupB, 5555L) })
        {
            dstStore.Upsert(new AssetMeta { Path = p, Name = Path.GetFileName(p), IsFolder = false, FileSize = sz, Mtime = 200, LastScanned = 200 });
        }

        long dstTier1Id = dstStore.GetAssetId(pathTier1)!.Value;
        dstStore.UpsertMetadata(new AssetMetadata
        {
            AssetId = dstTier1Id,
            Title = "My Custom Edited Title",
            Rating = 2,
            ReadStatus = ReadStatus.Reading,
            ReadCount = 1,
            LastPage = 4,
            ManuallyEdited = true,
            ParsedAt = 200
        });
        dstStore.AddBookmark(dstTier1Id, 3, "Local BM Keep");

        var dstSvc = new MetadataSnapshotService(dstStore, newData, newRoot);
        var report = await dstSvc.AnalyzeImportAsync(snapshotPath);

        Assert.Equal(1, report.MatchedTier1FullPath);
        Assert.Equal(1, report.MatchedTier2RelativePath);
        Assert.Equal(1, report.MatchedTier3FileNameSize);
        Assert.Equal(2, report.UnmatchedCount); // MissingBook + SameDup (ambiguous)

        var result = await dstSvc.ApplyImportAsync(report);
        Assert.Equal(2, result.UnmatchedSavedCount);

        // Tier 1 保留既有手動編輯標題、評分、閱讀進度與原有第 3 頁書籤，並補上標籤
        var d1 = dstStore.GetWorkDetail(pathTier1)!;
        Assert.Equal("My Custom Edited Title", d1.Title);
        Assert.Equal(2, d1.Rating);
        Assert.Equal(4, d1.LastPage);
        Assert.Equal("ja", d1.Language); // 原先空白 → 自動補齊
        Assert.Contains(d1.Tags, t => t.Name == "mosaic censorship");
        var bms1 = dstStore.ListBookmarks(dstTier1Id);
        Assert.Single(bms1);
        Assert.Equal("Local BM Keep", bms1[0].Label);

        // Tier 2 與 Tier 3 補齊所有 metadata 與書籤
        var d2 = dstStore.GetWorkDetail(newTier2)!;
        Assert.Equal("Snap Title 2", d2.Title);
        Assert.Equal(7, d2.LastPage);
        Assert.Single(dstStore.ListBookmarks(d2.AssetId));

        var d3 = dstStore.GetWorkDetail(newTier3)!;
        Assert.Equal("Snap Title 3", d3.Title);

        // Mappings 已合併
        var dstIntel = new MappingIntelligence(Path.Combine(newData, "mappings.json"));
        Assert.Equal("ImportedArtist", dstIntel.TryGetMappedFolder("[ImportedCircle] Something.zip"));
    }

    [Fact]
    public async Task W5_3_And_W5_4_SettingsVault_UnmappedResolution_BackupPrune10_RestoreAndReadOnlyGuard()
    {
        var srcData = CreateTempDir("VaultSrcData");
        var dataDir = CreateTempDir("VaultAppData");
        var rootDir = CreateTempDir("VaultRoot");
        var snapFile = Path.Combine(CreateTempDir("VaultSnap"), "vault_snap.json");

        // 產生僅有 1 筆缺檔未對應作品的快照
        using (var srcStore = new SqliteAssetMetaStore(Path.Combine(srcData, "cache.db")))
        {
            long aid = srcStore.UpsertAuthor("MissingWorkAuthor");
            string ghostPath = Path.Combine(srcData, "DoesNotExist", "LostTreasure.zip");
            srcStore.Upsert(new AssetMeta { Path = ghostPath, Name = "LostTreasure.zip", IsFolder = false, FileSize = 9999, Mtime = 100, LastScanned = 100 });
            long gid = srcStore.GetAssetId(ghostPath)!.Value;
            srcStore.UpsertMetadata(new AssetMetadata
            {
                AssetId = gid,
                AuthorId = aid,
                Title = "Recovered Treasure Title",
                Rating = 5,
                LastPage = 9,
                ReadStatus = ReadStatus.Reading,
                ParsedAt = 100
            });
            srcStore.AddBookmark(gid, 2, "寶藏書籤");

            await new MetadataSnapshotService(srcStore, srcData, srcData).ExportToJsonAsync(snapFile);
        }

        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "appsettings.json"),
            JsonSerializer.Serialize(new AppConfig { RootDirectory = rootDir }));

        string targetWorkPath = Path.Combine(rootDir, "TargetArtist", "RenamedWorkOnDisk.zip");
        using (var initCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false))
        {
            initCtx.MetaStore.Upsert(new AssetMeta
            {
                Path = targetWorkPath,
                Name = "RenamedWorkOnDisk.zip",
                IsFolder = false,
                FileSize = 7777,
                Mtime = 500,
                LastScanned = 500
            });
            initCtx.MetaStore.UpsertAuthor("BaselineAuthorBeforeRestore");
        }

        // 1. 透過 SettingsPageViewModel 匯入分析 → 套用 → 手動對應未對應清單 → 備份保留 10 份與還原
        using (var ctx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: false))
        {
            var settingsVm = new SettingsPageViewModel(ctx, runOnUiAsync: static a => { a(); return Task.CompletedTask; });
            await settingsVm.AnalyzeImportSnapshotPathAsync(snapFile);
            Assert.True(settingsVm.HasPendingImportAnalysis);

            await settingsVm.ApplyImportSnapshotCommand.ExecuteAsync(null);
            Assert.False(settingsVm.HasPendingImportAnalysis);
            var unmapped = Assert.Single(settingsVm.UnmappedImportItems);

            // 切換略過並驗證持久化
            unmapped.ToggleSkipCommand.Execute(null);
            Assert.Equal(UnmappedWorkStatus.Skipped, settingsVm.UnmappedImportItems[0].Status);

            var reloadedSettingsVm = new SettingsPageViewModel(ctx, runOnUiAsync: static a => { a(); return Task.CompletedTask; });
            var persistedItem = Assert.Single(reloadedSettingsVm.UnmappedImportItems);
            Assert.Equal(UnmappedWorkStatus.Skipped, persistedItem.Status);

            // 手動指定對應至 RenamedWorkOnDisk.zip
            reloadedSettingsVm.VaultUnmappedSearchText = "RenamedWorkOnDisk";
            Assert.NotNull(reloadedSettingsVm.SelectedCatalogMatchTarget);
            persistedItem.AssignToSelectedTargetCommand.Execute(null);
            Assert.Empty(reloadedSettingsVm.UnmappedImportItems);

            var resolvedDetail = ctx.MetaStore.GetWorkDetail(targetWorkPath)!;
            Assert.Equal("Recovered Treasure Title", resolvedDetail.Title);
            Assert.Equal(5, resolvedDetail.Rating);

            // 連續建立 12 份備份，應自動清理保留最新 10 份
            var backupSvc = new DatabaseBackupService(dataDir);
            for (int i = 0; i < 12; i++)
            {
                backupSvc.CreateSafetyBackup(ctx.MetaStore, $"test_{i}");
                await Task.Delay(12);
            }
            var backups = backupSvc.ListBackups();
            Assert.Equal(DatabaseBackupService.DefaultRetainCount, backups.Count);

            // 找到 ApplyImport 自動建立前的 before_import 備份或手動備份來驗證還原（首先破壞目前狀態）
            var chosenBackup = backups.Last(); // 保留清單中較早的一份（contains Recovered Treasure or before）
            ctx.MetaStore.UpsertAuthor("TemporaryAuthorShouldVanishAfterRestore");
            Assert.NotNull(ctx.MetaStore.FindAuthorByMainName("TemporaryAuthorShouldVanishAfterRestore"));

            // 找一份還未包含 TemporaryAuthorShouldVanishAfterRestore 的最新備份還原
            ctx.RestoreDatabaseFromBackup(chosenBackup.FullPath, chosenBackup.MappingsBackupPath);
            Assert.Null(ctx.MetaStore.FindAuthorByMainName("TemporaryAuthorShouldVanishAfterRestore"));
            Assert.NotNull(ctx.MetaStore.FindAuthorByMainName("BaselineAuthorBeforeRestore"));
        }

        // 2. 唯讀模式下應攔截匯入、手動備份與還原
        using (var roCtx = new AppDataContext(explicitDataDirectory: dataDir, forceReadOnlyDatabase: true))
        {
            var roSettings = new SettingsPageViewModel(roCtx, runOnUiAsync: static a => { a(); return Task.CompletedTask; });
            Assert.False(roSettings.IsDatabaseWritable);
            Assert.False(roSettings.CanOperateVaultWriteActions);
            Assert.Throws<InvalidOperationException>(() => roCtx.CreateSafetyBackup("ro_blocked"));
            Assert.Throws<InvalidOperationException>(() => roCtx.RestoreDatabaseFromBackup(Path.Combine(dataDir, "nonexistent.db")));
        }
    }

    [Fact]
    public async Task CaseInsensitiveDuplicateTags_EhTagSyncAndSnapshotAnalyzeApply_DoNotCrash()
    {
        var srcData = CreateTempDir("DupSrc");
        var dstData = CreateTempDir("DupDst");
        var snapFile = Path.Combine(CreateTempDir("DupSnap"), "dup_snap.json");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              "data": [
                { "namespace": "parody", "data": { "amagami": { "name": "圣诞之吻" } } },
                { "namespace": "female", "data": { "sole female": { "name": "单女主" } } }
              ]
            }
            """));
        var translator = new TagTranslator(() => TagTranslationDb.LoadFromJsonStream(ms));
        Assert.Equal("單女主", translator.TranslateAny("sole female", "zh-TW"));
        Assert.Equal("聖誕之吻", translator.TranslateAny("Amagami", "zh-TW"));

        // 來源庫故意塞入大小寫不同但字母相同的標籤與待手動對應作品
        using (var srcStore = new SqliteAssetMetaStore(Path.Combine(srcData, "cache.db")))
        {
            long tLower = srcStore.UpsertTag("amagami", "parody");
            long tUpper = srcStore.UpsertTag("Amagami", "parody");
            long tFemale = srcStore.UpsertTag("sole female", "female");
            long tFemaleUpper = srcStore.UpsertTag("Sole Female", "female");
            Assert.NotEqual(tLower, tUpper);
            Assert.NotEqual(tFemale, tFemaleUpper);

            // 以 Ordinal（區分大小寫）建立 nameToId，確保大小寫變體不會觸發 DuplicateKey 也不會互相覆蓋遺漏
            var tags = srcStore.ListTagsByUsage();
            var nameToId = new Dictionary<string, long>(tags.Count, StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                nameToId[tag.Name] = tag.Id;
            }

            int synced = translator.SyncTranslationsToDb(srcStore, "zh-TW", nameToId);
            Assert.Equal(4, synced);

            var transMap = srcStore.GetTagTranslations(new[] { tLower, tUpper, tFemale, tFemaleUpper }, "zh-TW");
            Assert.Equal("聖誕之吻", transMap[tLower]);
            Assert.Equal("聖誕之吻", transMap[tUpper]);
            Assert.Equal("單女主", transMap[tFemale]);
            Assert.Equal("單女主", transMap[tFemaleUpper]);

            string ghostPath = Path.Combine(srcData, "Missing", "DupTagWork.zip");
            srcStore.Upsert(new AssetMeta { Path = ghostPath, Name = "DupTagWork.zip", IsFolder = false, FileSize = 7777, Mtime = 100, LastScanned = 100 });
            long gid = srcStore.GetAssetId(ghostPath)!.Value;
            srcStore.UpsertMetadata(new AssetMetadata { AssetId = gid, Title = "Dup Work", ParsedAt = 100 });
            srcStore.SetAssetTagEntries(gid, new[] { new TagEntry("amagami", "parody"), new TagEntry("Sole Female", "female") });

            await new MetadataSnapshotService(srcStore, srcData, srcData).ExportToJsonAsync(snapFile);
        }

        // 目標庫亦預先存在大小寫不同標籤、譯文與作品，驗證 Analyze / Apply / ManualResolution 全流程皆不崩潰
        using var dstStore = new SqliteAssetMetaStore(Path.Combine(dstData, "cache.db"));
        long dt1 = dstStore.UpsertTag("amagami", "parody");
        long dt2 = dstStore.UpsertTag("Amagami", "parody");
        dstStore.UpsertTagTranslation(dt1, "zh-TW", "聖誕之吻", "ehtag");
        dstStore.UpsertTagTranslation(dt2, "zh-TW", "聖誕之吻", "ehtag");

        string localTargetPath = Path.Combine(dstData, "Library", "LocalRenamed.zip");
        dstStore.Upsert(new AssetMeta { Path = localTargetPath, Name = "LocalRenamed.zip", IsFolder = false, FileSize = 8888, Mtime = 200, LastScanned = 200 });
        long localAssetId = dstStore.GetAssetId(localTargetPath)!.Value;

        var svc = new MetadataSnapshotService(dstStore, dstData, dstData);
        var report = await svc.AnalyzeImportAsync(snapFile);
        Assert.Equal(1, report.UnmatchedCount);

        var applyResult = await svc.ApplyImportAsync(report);
        Assert.Equal(1, applyResult.UnmatchedSavedCount);

        var unmapped = Assert.Single(svc.LoadUnmappedItems());
        Assert.True(svc.ApplyManualWorkResolution(unmapped.ItemId, localAssetId));
        Assert.Empty(svc.LoadUnmappedItems());

        var detail = dstStore.GetWorkDetail(localTargetPath)!;
        Assert.Equal("Dup Work", detail.Title);
    }
}

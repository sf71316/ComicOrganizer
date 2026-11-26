using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ComicOrganizer.App.Core;

/// <summary>
/// Phase 5 Metadata 保險箱核心服務：
/// - 匯出／匯入版本化 JSON 快照（標籤、譯文、別名、作者、作品 metadata、書籤、虛擬庫、mappings.json）
/// - 匯出作品清冊 CSV（UTF-8 BOM，Excel 直接可讀）
/// - 唯讀 Diff 預覽分析 ＋ 四層作品匹配（完整路徑 → 相對庫根 → 檔名+大小 → 未對應清單）
/// - 未對應清單持久化保存（Pending / Skipped），支援手動指定作品套用或捨棄
/// </summary>
public sealed class MetadataSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAssetMetaStore _store;
    private readonly string? _rootDirectory;
    private readonly string? _mappingsPath;
    private readonly string _unmappedStatePath;

    public MetadataSnapshotService(
        IAssetMetaStore store,
        string dataDirectory,
        string? rootDirectory = null,
        string? mappingsPath = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _rootDirectory = NormalizeDirOrNull(rootDirectory);
        _mappingsPath = mappingsPath ?? Path.Combine(dataDirectory, "mappings.json");
        _unmappedStatePath = Path.Combine(dataDirectory, "unmapped_import_items.json");
    }

    // ===== W5.1: Export =====

    public Task<MetadataSnapshotExportSummary> ExportToJsonAsync(
        string outputPath,
        MetadataSnapshotExportOptions? options = null,
        CancellationToken ct = default)
        => Task.Run(() => ExportToJson(outputPath, options ?? new MetadataSnapshotExportOptions(), ct), ct);

    public MetadataSnapshotExportSummary ExportToJson(
        string outputPath,
        MetadataSnapshotExportOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        options ??= new MetadataSnapshotExportOptions();

        var corpus = _store.ExportSnapshotCorpus(options.IncludeExternalSources);
        ct.ThrowIfCancellationRequested();

        var doc = new MetadataSnapshotDocument
        {
            SchemaVersion = MetadataSnapshotDocument.CurrentSchemaVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            SourceRootDirectory = _rootDirectory ?? string.Empty,
            IncludesExternalSources = options.IncludeExternalSources,
            Tags = corpus.Tags.ToList(),
            TagTranslations = corpus.TagTranslations.ToList(),
            TagAliases = corpus.TagAliases.ToList(),
            Authors = corpus.Authors.ToList(),
            AuthorAliases = corpus.AuthorAliases.ToList(),
            AuthorTags = corpus.AuthorTags.ToList(),
            VirtualLibraries = corpus.VirtualLibraries
                .Select(v => new SnapshotVirtualLibraryDto
                {
                    Name = v.Name,
                    Dsl = v.Dsl,
                    IconSvg = v.IconSvg,
                    SortOrder = v.SortOrder,
                    Pinned = v.Pinned,
                    CreatedAt = v.CreatedAt,
                    UpdatedAt = v.UpdatedAt
                })
                .ToList()
        };

        if (options.IncludeLearnedMappings && !string.IsNullOrWhiteSpace(_mappingsPath) && File.Exists(_mappingsPath))
        {
            var intel = new MappingIntelligence(_mappingsPath);
            doc.LearnedMappings = new Dictionary<string, string>(intel.GetAllMappings(), StringComparer.OrdinalIgnoreCase);
        }

        int bookmarkCount = 0;
        foreach (var w in corpus.Works)
        {
            ct.ThrowIfCancellationRequested();
            bookmarkCount += w.Bookmarks.Count;

            doc.Works.Add(new SnapshotWorkDto
            {
                FullPath = w.FullPath,
                RelativePath = ComputeRelativePath(_rootDirectory, w.FullPath),
                FileName = w.FileName,
                IsFolder = w.IsFolder,
                FileSize = w.FileSize,
                AuthorMainName = w.AuthorMainName,
                Title = w.Title,
                Series = w.Series,
                ChapterNo = w.ChapterNo,
                Language = w.Language,
                TranslationGroup = w.TranslationGroup,
                PageCount = w.PageCount,
                Rating = w.Rating,
                ReadStatus = w.ReadStatus,
                ReadCount = w.ReadCount,
                LastRead = w.LastRead,
                LastPage = w.LastPage,
                ExternalId = w.ExternalId,
                ManuallyEdited = w.ManuallyEdited,
                ParsedAt = w.ParsedAt,
                Tags = w.Tags.ToList(),
                Bookmarks = w.Bookmarks.ToList(),
                ManualVirtualLibraries = w.ManualVirtualLibraries.ToList(),
                ExternalSources = options.IncludeExternalSources ? w.ExternalSources.ToList() : new()
            });
        }

        WriteJsonAtomically(outputPath, doc);

        return new MetadataSnapshotExportSummary(
            OutputPath: outputPath,
            WorkCount: doc.Works.Count,
            AuthorCount: doc.Authors.Count,
            TagCount: doc.Tags.Count,
            TagTranslationCount: doc.TagTranslations.Count,
            BookmarkCount: bookmarkCount,
            VirtualLibraryCount: doc.VirtualLibraries.Count,
            LearnedMappingCount: doc.LearnedMappings.Count);
    }

    public Task<int> ExportWorksToCsvAsync(string outputPath, CancellationToken ct = default)
        => Task.Run(() => ExportWorksToCsv(outputPath, ct), ct);

    public int ExportWorksToCsv(string outputPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var corpus = _store.ExportSnapshotCorpus(includeExternalSources: false);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = outputPath + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var writer = new StreamWriter(tmpPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.WriteLine("Title,Author,Series,ChapterNo,Language,TranslationGroup,PageCount,Rating,ReadStatus,ReadCount,LastPage,ManuallyEdited,Tags,BookmarksCount,FullPath");
                foreach (var w in corpus.Works)
                {
                    ct.ThrowIfCancellationRequested();
                    string tagsStr = string.Join("; ", w.Tags.Select(t => t.Name));
                    var fields = new[]
                    {
                        w.Title ?? string.Empty,
                        w.AuthorMainName ?? string.Empty,
                        w.Series ?? string.Empty,
                        w.ChapterNo ?? string.Empty,
                        w.Language ?? string.Empty,
                        w.TranslationGroup ?? string.Empty,
                        w.PageCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        w.Rating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        w.ReadStatus.ToString(),
                        w.ReadCount.ToString(CultureInfo.InvariantCulture),
                        w.LastPage?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        w.ManuallyEdited ? "true" : "false",
                        tagsStr,
                        w.Bookmarks.Count.ToString(CultureInfo.InvariantCulture),
                        w.FullPath
                    };

                    writer.WriteLine(string.Join(",", fields.Select(EscapeCsvField)));
                }
            }

            File.Move(tmpPath, outputPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }

        return corpus.Works.Count;
    }

    // ===== W5.2: Read-Only Import Analysis & Transactional Apply =====

    public Task<MetadataImportAnalysisReport> AnalyzeImportAsync(
        string snapshotFilePath,
        MetadataImportOptions? options = null,
        CancellationToken ct = default)
        => Task.Run(() => AnalyzeImport(snapshotFilePath, options ?? new MetadataImportOptions(), ct), ct);

    public MetadataImportAnalysisReport AnalyzeImport(
        string snapshotFilePath,
        MetadataImportOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotFilePath);
        options ??= new MetadataImportOptions();

        var doc = ReadSnapshotDocument(snapshotFilePath);
        var currentCorpus = _store.ExportSnapshotCorpus(includeExternalSources: false);
        var catalogEntries = _store.ListAssetEntries();

        // Build dictionary indices from live DB
        var existingTags = new HashSet<string>(currentCorpus.Tags.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
        var existingTagTrans = BuildTagTranslationMap(currentCorpus.TagTranslations);
        var existingTagAliases = new HashSet<string>(currentCorpus.TagAliases.Select(a => a.AliasName), StringComparer.OrdinalIgnoreCase);
        var existingAuthors = new HashSet<string>(currentCorpus.Authors.Select(a => a.MainName), StringComparer.OrdinalIgnoreCase);
        var existingAuthorAliases = new HashSet<string>(
            currentCorpus.AuthorAliases.Select(a => $"{a.AuthorMainName}\0{a.AliasName}"),
            StringComparer.OrdinalIgnoreCase);
        var existingVirtualLibs = new HashSet<string>(currentCorpus.VirtualLibraries.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
        var existingWorkByAssetId = currentCorpus.Works.ToDictionary(w => w.AssetId);

        int tagsToAdd = doc.Tags.Count(t => !string.IsNullOrWhiteSpace(t.Name) && !existingTags.Contains(t.Name));
        int transToAdd = 0;
        foreach (var tr in doc.TagTranslations)
        {
            if (string.IsNullOrWhiteSpace(tr.TagName) || string.IsNullOrWhiteSpace(tr.Lang) || string.IsNullOrWhiteSpace(tr.TranslatedName))
            {
                continue;
            }

            string key = $"{tr.TagName}\0{tr.Lang}".ToLowerInvariant();
            if (!existingTagTrans.ContainsKey(key) || options.OverwriteTagTranslations)
            {
                transToAdd++;
            }
        }

        int tagAliasesToAdd = doc.TagAliases.Count(a => !string.IsNullOrWhiteSpace(a.AliasName) && !existingTagAliases.Contains(a.AliasName));
        int authorsToAdd = doc.Authors.Count(a => !string.IsNullOrWhiteSpace(a.MainName) && !existingAuthors.Contains(a.MainName));
        int authorAliasesToAdd = doc.AuthorAliases.Count(a =>
            !string.IsNullOrWhiteSpace(a.AuthorMainName)
            && !string.IsNullOrWhiteSpace(a.AliasName)
            && !existingAuthorAliases.Contains($"{a.AuthorMainName}\0{a.AliasName}"));
        int vlibsToAdd = doc.VirtualLibraries.Count(v => !string.IsNullOrWhiteSpace(v.Name) && !existingVirtualLibs.Contains(v.Name));

        int mappingsToAdd = 0;
        if (options.ImportLearnedMappings && doc.LearnedMappings.Count > 0)
        {
            var currentMappings = !string.IsNullOrWhiteSpace(_mappingsPath) && File.Exists(_mappingsPath)
                ? new MappingIntelligence(_mappingsPath).GetAllMappings()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in doc.LearnedMappings)
            {
                if (!currentMappings.TryGetValue(kvp.Key, out var curVal) ||
                    (options.OverwriteExistingMetadata && !string.Equals(curVal, kvp.Value, StringComparison.Ordinal)))
                {
                    mappingsToAdd++;
                }
            }
        }

        // Build 4-tier matching indices on current catalog
        var matchIndex = CatalogMatchIndex.Build(catalogEntries, _rootDirectory);
        int tier1 = 0, tier2 = 0, tier3 = 0, worksWithUpdates = 0, bookmarksToAdd = 0;
        var matchedPlans = new List<MatchedWorkImportPlan>();
        var unmatchedWorks = new List<UnmappedSnapshotWork>();

        foreach (var sw in doc.Works)
        {
            ct.ThrowIfCancellationRequested();
            var (matchedEntry, tier) = matchIndex.Resolve(sw);
            if (matchedEntry == null)
            {
                unmatchedWorks.Add(new UnmappedSnapshotWork
                {
                    ItemId = Guid.NewGuid().ToString("N"),
                    Status = UnmappedWorkStatus.Pending,
                    RecordedAtUtc = DateTimeOffset.UtcNow,
                    Work = sw
                });
                continue;
            }

            switch (tier)
            {
                case WorkMatchTier.FullPath: tier1++; break;
                case WorkMatchTier.RelativePath: tier2++; break;
                case WorkMatchTier.FileNameAndSize: tier3++; break;
            }

            existingWorkByAssetId.TryGetValue(matchedEntry.Id, out var localWork);
            bool willModify = DoesWorkHaveApplicableChanges(sw, localWork, options, out int newBookmarks);
            if (willModify)
            {
                worksWithUpdates++;
            }

            bookmarksToAdd += newBookmarks;
            matchedPlans.Add(new MatchedWorkImportPlan(sw, matchedEntry.Id, matchedEntry.Path, tier, willModify));
        }

        return new MetadataImportAnalysisReport
        {
            Document = doc,
            SourceFilePath = snapshotFilePath,
            TagsTotal = doc.Tags.Count,
            TagsToAdd = tagsToAdd,
            TagTranslationsToAdd = transToAdd,
            TagAliasesToAdd = tagAliasesToAdd,
            AuthorsToAdd = authorsToAdd,
            AuthorAliasesToAdd = authorAliasesToAdd,
            VirtualLibrariesToAdd = vlibsToAdd,
            LearnedMappingsToAdd = mappingsToAdd,
            WorksTotal = doc.Works.Count,
            MatchedTier1FullPath = tier1,
            MatchedTier2RelativePath = tier2,
            MatchedTier3FileNameSize = tier3,
            WorksWithUpdates = worksWithUpdates,
            BookmarksToAdd = bookmarksToAdd,
            MatchedPlans = matchedPlans,
            UnmatchedWorks = unmatchedWorks
        };
    }

    public Task<MetadataImportApplyResult> ApplyImportAsync(
        MetadataImportAnalysisReport report,
        MetadataImportOptions? options = null,
        CancellationToken ct = default)
        => Task.Run(() => ApplyImport(report, options ?? new MetadataImportOptions(), ct), ct);

    public MetadataImportApplyResult ApplyImport(
        MetadataImportAnalysisReport report,
        MetadataImportOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        options ??= new MetadataImportOptions();

        var doc = report.Document;
        var currentCorpus = _store.ExportSnapshotCorpus(includeExternalSources: false);
        var existingWorkByAssetId = currentCorpus.Works.ToDictionary(w => w.AssetId);

        int tagsCount = 0, transCount = 0, tagAliasCount = 0;
        int authorsCount = 0, authorAliasCount = 0, vlibsCount = 0;
        int worksUpdated = 0, bookmarksAdded = 0;

        _store.RunInTransaction(() =>
        {
            var tagIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in currentCorpus.Tags)
            {
                tagIdMap[t.Name] = _store.UpsertTag(t.Name, t.Category);
            }

            foreach (var t in doc.Tags)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(t.Name)) continue;

                bool isNew = !tagIdMap.ContainsKey(t.Name);
                long tagId = _store.UpsertTag(t.Name, t.Category);
                tagIdMap[t.Name] = tagId;
                if (isNew)
                {
                    tagsCount++;
                }
                else if (options.OverwriteExistingMetadata && !string.IsNullOrWhiteSpace(t.Category))
                {
                    _store.SetTagCategory(tagId, t.Category);
                }
            }

            var existingTrans = BuildTagTranslationMap(currentCorpus.TagTranslations);
            foreach (var tr in doc.TagTranslations)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(tr.TagName) || string.IsNullOrWhiteSpace(tr.Lang) || string.IsNullOrWhiteSpace(tr.TranslatedName))
                {
                    continue;
                }

                if (!tagIdMap.TryGetValue(tr.TagName, out long tagId))
                {
                    tagId = _store.UpsertTag(tr.TagName);
                    tagIdMap[tr.TagName] = tagId;
                    tagsCount++;
                }

                string key = $"{tr.TagName}\0{tr.Lang}".ToLowerInvariant();
                if (!existingTrans.ContainsKey(key) || options.OverwriteTagTranslations)
                {
                    _store.UpsertTagTranslation(tagId, tr.Lang, tr.TranslatedName, tr.Source ?? "snapshot");
                    transCount++;
                }
            }

            var existingTagAliases = new HashSet<string>(currentCorpus.TagAliases.Select(a => a.AliasName), StringComparer.OrdinalIgnoreCase);
            foreach (var ta in doc.TagAliases)
            {
                if (string.IsNullOrWhiteSpace(ta.AliasName) || string.IsNullOrWhiteSpace(ta.TargetTagName)) continue;
                if (existingTagAliases.Contains(ta.AliasName)) continue;

                if (!tagIdMap.TryGetValue(ta.TargetTagName, out long targetTagId))
                {
                    targetTagId = _store.UpsertTag(ta.TargetTagName);
                    tagIdMap[ta.TargetTagName] = targetTagId;
                }

                if (!tagIdMap.ContainsKey(ta.AliasName))
                {
                    _store.AddTagAlias(targetTagId, ta.AliasName);
                    existingTagAliases.Add(ta.AliasName);
                    tagAliasCount++;
                }
            }

            var authorIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in currentCorpus.Authors)
            {
                var found = _store.FindAuthorByMainName(a.MainName);
                if (found != null)
                {
                    authorIdMap[a.MainName] = found.Id;
                }
            }

            foreach (var a in doc.Authors)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(a.MainName)) continue;
                bool isNew = !authorIdMap.ContainsKey(a.MainName);
                long aid = _store.UpsertAuthor(a.MainName, a.FolderPath);
                authorIdMap[a.MainName] = aid;
                if (isNew)
                {
                    authorsCount++;
                }
            }

            var existingAuthorAliases = new HashSet<string>(
                currentCorpus.AuthorAliases.Select(a => $"{a.AuthorMainName}\0{a.AliasName}"),
                StringComparer.OrdinalIgnoreCase);
            foreach (var aa in doc.AuthorAliases)
            {
                if (string.IsNullOrWhiteSpace(aa.AuthorMainName) || string.IsNullOrWhiteSpace(aa.AliasName)) continue;
                string key = $"{aa.AuthorMainName}\0{aa.AliasName}";
                if (existingAuthorAliases.Contains(key)) continue;

                if (!authorIdMap.TryGetValue(aa.AuthorMainName, out long aid))
                {
                    aid = _store.UpsertAuthor(aa.AuthorMainName);
                    authorIdMap[aa.AuthorMainName] = aid;
                    authorsCount++;
                }

                _store.AddAuthorAlias(aid, aa.AliasName, aa.Lang);
                existingAuthorAliases.Add(key);
                authorAliasCount++;
            }

            foreach (var at in doc.AuthorTags)
            {
                if (string.IsNullOrWhiteSpace(at.AuthorMainName) || string.IsNullOrWhiteSpace(at.TagName)) continue;
                if (!authorIdMap.TryGetValue(at.AuthorMainName, out long aid)) continue;
                if (!tagIdMap.TryGetValue(at.TagName, out long tid))
                {
                    tid = _store.UpsertTag(at.TagName, at.Category);
                    tagIdMap[at.TagName] = tid;
                }

                _store.AssociateAuthorTag(aid, tid);
            }

            var vlibByName = BuildVirtualLibraryMap(currentCorpus.VirtualLibraries);
            foreach (var vl in doc.VirtualLibraries)
            {
                if (string.IsNullOrWhiteSpace(vl.Name) || string.IsNullOrWhiteSpace(vl.Dsl)) continue;
                if (!vlibByName.TryGetValue(vl.Name, out var existingVl))
                {
                    long id = _store.CreateVirtualLibrary(vl.Name, vl.Dsl, vl.IconSvg);
                    if (vl.Pinned)
                    {
                        _store.SetVirtualLibraryPinned(id, true);
                    }
                    vlibByName[vl.Name] = new VirtualLibrary(id, vl.Name, vl.Dsl, vl.IconSvg, vl.SortOrder, vl.Pinned, vl.CreatedAt, vl.UpdatedAt);
                    vlibsCount++;
                }
                else if (options.OverwriteExistingMetadata)
                {
                    _store.UpdateVirtualLibrary(existingVl.Id, vl.Name, vl.Dsl, vl.IconSvg);
                    _store.SetVirtualLibraryPinned(existingVl.Id, vl.Pinned);
                }
            }

            foreach (var plan in report.MatchedPlans)
            {
                ct.ThrowIfCancellationRequested();
                existingWorkByAssetId.TryGetValue(plan.TargetAssetId, out var localWork);
                var (updated, bmAdded) = ApplyWorkDtoToAsset(
                    plan.SourceWork,
                    plan.TargetAssetId,
                    plan.TargetPath,
                    localWork,
                    authorIdMap,
                    tagIdMap,
                    vlibByName,
                    options);
                if (updated) worksUpdated++;
                bookmarksAdded += bmAdded;
            }
        });

        int mappingsMerged = 0;
        if (options.ImportLearnedMappings && doc.LearnedMappings.Count > 0 && !string.IsNullOrWhiteSpace(_mappingsPath))
        {
            var intel = new MappingIntelligence(_mappingsPath);
            mappingsMerged = intel.MergeMappings(doc.LearnedMappings, options.OverwriteExistingMetadata);
        }

        // Persist unmatched items for follow-up maintenance UI
        int unmappedSaved = MergeAndSaveUnmappedItems(report.UnmatchedWorks);

        return new MetadataImportApplyResult(
            TagsAddedOrUpdated: tagsCount,
            TagTranslationsAddedOrUpdated: transCount,
            TagAliasesAdded: tagAliasCount,
            AuthorsAdded: authorsCount,
            AuthorAliasesAdded: authorAliasCount,
            VirtualLibrariesAdded: vlibsCount,
            LearnedMappingsAddedOrUpdated: mappingsMerged,
            WorksUpdated: worksUpdated,
            BookmarksAdded: bookmarksAdded,
            UnmatchedSavedCount: unmappedSaved);
    }

    // ===== Unmapped Maintenance APIs (W5.3) =====

    public IReadOnlyList<UnmappedSnapshotWork> LoadUnmappedItems()
    {
        if (!File.Exists(_unmappedStatePath))
        {
            return Array.Empty<UnmappedSnapshotWork>();
        }

        try
        {
            var json = File.ReadAllText(_unmappedStatePath);
            return JsonSerializer.Deserialize<List<UnmappedSnapshotWork>>(json, JsonOptions)
                   ?? new List<UnmappedSnapshotWork>();
        }
        catch
        {
            return Array.Empty<UnmappedSnapshotWork>();
        }
    }

    public bool ApplyManualWorkResolution(
        string unmappedItemId,
        long targetAssetId,
        MetadataImportOptions? options = null)
    {
        options ??= new MetadataImportOptions();
        var items = LoadUnmappedItems().ToList();
        var targetItem = items.FirstOrDefault(i => string.Equals(i.ItemId, unmappedItemId, StringComparison.Ordinal));
        if (targetItem == null)
        {
            return false;
        }

        var entry = _store.ListAssetEntries().FirstOrDefault(e => e.Id == targetAssetId);
        if (entry == null)
        {
            return false;
        }

        var corpus = _store.ExportSnapshotCorpus(includeExternalSources: false);
        var localWork = corpus.Works.FirstOrDefault(w => w.AssetId == targetAssetId);

        _store.RunInTransaction(() =>
        {
            var tagIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in corpus.Tags)
            {
                tagIdMap[t.Name] = _store.UpsertTag(t.Name, t.Category);
            }
            var authorIdMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in corpus.Authors)
            {
                var found = _store.FindAuthorByMainName(a.MainName);
                if (found != null) authorIdMap[a.MainName] = found.Id;
            }
            var vlibByName = BuildVirtualLibraryMap(corpus.VirtualLibraries);

            ApplyWorkDtoToAsset(
                targetItem.Work,
                targetAssetId,
                entry.Path,
                localWork,
                authorIdMap,
                tagIdMap,
                vlibByName,
                options);
        });

        items.RemoveAll(i => string.Equals(i.ItemId, unmappedItemId, StringComparison.Ordinal));
        SaveUnmappedItems(items);
        return true;
    }

    public bool SetUnmappedItemSkipped(string unmappedItemId, bool skipped = true)
    {
        var items = LoadUnmappedItems().ToList();
        var found = items.FirstOrDefault(i => string.Equals(i.ItemId, unmappedItemId, StringComparison.Ordinal));
        if (found == null)
        {
            return false;
        }

        found.Status = skipped ? UnmappedWorkStatus.Skipped : UnmappedWorkStatus.Pending;
        SaveUnmappedItems(items);
        return true;
    }

    public bool DiscardUnmappedItem(string unmappedItemId)
    {
        var items = LoadUnmappedItems().ToList();
        int removed = items.RemoveAll(i => string.Equals(i.ItemId, unmappedItemId, StringComparison.Ordinal));
        if (removed == 0)
        {
            return false;
        }

        SaveUnmappedItems(items);
        return true;
    }

    public void ClearAllUnmappedItems()
    {
        SaveUnmappedItems(Array.Empty<UnmappedSnapshotWork>());
    }

    // ===== Helpers =====

    private (bool Updated, int BookmarksAdded) ApplyWorkDtoToAsset(
        SnapshotWorkDto sw,
        long assetId,
        string targetPath,
        SnapshotWorkRecord? local,
        Dictionary<string, long> authorIdMap,
        Dictionary<string, long> tagIdMap,
        Dictionary<string, VirtualLibrary> vlibByName,
        MetadataImportOptions options)
    {
        bool overwriteMeta = options.OverwriteExistingMetadata;
        bool overwriteRead = options.OverwriteReadingProgress;

        long? incomingAuthorId = null;
        if (!string.IsNullOrWhiteSpace(sw.AuthorMainName))
        {
            if (!authorIdMap.TryGetValue(sw.AuthorMainName, out long aid))
            {
                aid = _store.UpsertAuthor(sw.AuthorMainName);
                authorIdMap[sw.AuthorMainName] = aid;
            }
            incomingAuthorId = aid;
        }

        long? resolvedAuthorId = null;
        if (local != null)
        {
            var foundLocalAuthor = local.AuthorMainName != null ? _store.FindAuthorByMainName(local.AuthorMainName) : null;
            resolvedAuthorId = foundLocalAuthor?.Id;
        }

        bool canOverwriteLocalMeta = overwriteMeta || !(local?.ManuallyEdited ?? false);

        string? nextTitle = MergeText(local?.Title, sw.Title, overwriteMeta && canOverwriteLocalMeta);
        string? nextSeries = MergeText(local?.Series, sw.Series, overwriteMeta && canOverwriteLocalMeta);
        string? nextChapter = MergeText(local?.ChapterNo, sw.ChapterNo, overwriteMeta && canOverwriteLocalMeta);
        string? nextLang = MergeText(local?.Language, sw.Language, overwriteMeta && canOverwriteLocalMeta);
        string? nextGroup = MergeText(local?.TranslationGroup, sw.TranslationGroup, overwriteMeta && canOverwriteLocalMeta);
        string? nextExtId = MergeText(local?.ExternalId, sw.ExternalId, overwriteMeta && canOverwriteLocalMeta);
        long? nextAuthorId = (overwriteMeta && canOverwriteLocalMeta && incomingAuthorId.HasValue)
            ? incomingAuthorId
            : (resolvedAuthorId ?? incomingAuthorId);
        int? nextPageCount = (overwriteMeta && sw.PageCount.HasValue) ? sw.PageCount : (local?.PageCount ?? sw.PageCount);
        int? nextRating = (overwriteMeta && sw.Rating.HasValue) ? sw.Rating : (local?.Rating ?? sw.Rating);

        ReadStatus nextStatus = overwriteRead
            ? sw.ReadStatus
            : (local == null || local.ReadStatus == ReadStatus.Unread ? sw.ReadStatus : local.ReadStatus);
        int nextReadCount = overwriteRead
            ? sw.ReadCount
            : Math.Max(local?.ReadCount ?? 0, sw.ReadCount);
        long? nextLastRead = overwriteRead
            ? sw.LastRead
            : MaxNullable(local?.LastRead, sw.LastRead);
        int? nextLastPage = overwriteRead
            ? sw.LastPage
            : (local?.LastPage ?? sw.LastPage);
        bool nextManual = (local?.ManuallyEdited ?? false) || sw.ManuallyEdited;

        bool metaChanged = local == null
            || !string.Equals(local.Title, nextTitle, StringComparison.Ordinal)
            || !string.Equals(local.Series, nextSeries, StringComparison.Ordinal)
            || !string.Equals(local.ChapterNo, nextChapter, StringComparison.Ordinal)
            || !string.Equals(local.Language, nextLang, StringComparison.Ordinal)
            || !string.Equals(local.TranslationGroup, nextGroup, StringComparison.Ordinal)
            || !string.Equals(local.ExternalId, nextExtId, StringComparison.Ordinal)
            || !string.Equals(local.AuthorMainName, incomingAuthorId.HasValue && nextAuthorId == incomingAuthorId ? sw.AuthorMainName : local.AuthorMainName, StringComparison.Ordinal)
            || local.PageCount != nextPageCount
            || local.Rating != nextRating
            || local.ReadStatus != nextStatus
            || local.ReadCount != nextReadCount
            || local.LastRead != nextLastRead
            || local.LastPage != nextLastPage
            || local.ManuallyEdited != nextManual;

        if (metaChanged)
        {
            _store.UpsertMetadata(new AssetMetadata
            {
                AssetId = assetId,
                AuthorId = nextAuthorId,
                Title = nextTitle,
                Series = nextSeries,
                ChapterNo = nextChapter,
                Language = nextLang,
                TranslationGroup = nextGroup,
                PageCount = nextPageCount,
                Rating = nextRating,
                ReadStatus = nextStatus,
                ReadCount = nextReadCount,
                LastRead = nextLastRead,
                LastPage = nextLastPage,
                ExternalId = nextExtId,
                ManuallyEdited = nextManual,
                ParsedAt = Math.Max(local?.ParsedAt ?? 0, sw.ParsedAt)
            });
        }

        // Tags
        var localTags = local?.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool tagsChanged = false;
        if (overwriteMeta && sw.Tags.Count > 0)
        {
            _store.SetAssetTagEntries(
                assetId,
                sw.Tags.Select(t => new TagEntry(t.Name, t.Category ?? string.Empty)).ToList());
            tagsChanged = true;
        }
        else
        {
            foreach (var st in sw.Tags)
            {
                if (string.IsNullOrWhiteSpace(st.Name) || localTags.Contains(st.Name))
                {
                    continue;
                }

                if (!tagIdMap.TryGetValue(st.Name, out long tid))
                {
                    tid = _store.UpsertTag(st.Name, st.Category);
                    tagIdMap[st.Name] = tid;
                }

                _store.AssociateTag(assetId, tid);
                localTags.Add(st.Name);
                tagsChanged = true;
            }
        }

        // Bookmarks (add non-conflicting page_index)
        var existingPages = local?.Bookmarks.Select(b => b.PageIndex).ToHashSet()
            ?? _store.ListBookmarks(assetId).Select(b => b.PageIndex).ToHashSet();
        int bookmarksAdded = 0;
        foreach (var bm in sw.Bookmarks)
        {
            if (existingPages.Contains(bm.PageIndex))
            {
                continue;
            }

            _store.AddBookmark(assetId, bm.PageIndex, bm.Label);
            existingPages.Add(bm.PageIndex);
            bookmarksAdded++;
        }

        // Manual virtual libraries
        foreach (var vlName in sw.ManualVirtualLibraries)
        {
            if (vlibByName.TryGetValue(vlName, out var vl))
            {
                _store.AddWorkToVirtualLibrary(vl.Id, targetPath);
            }
        }

        // External sources
        if (sw.ExternalSources.Count > 0)
        {
            foreach (var ext in sw.ExternalSources)
            {
                var existingExt = _store.GetExternalSource(assetId, ext.Source);
                if (existingExt == null || overwriteMeta)
                {
                    _store.UpsertExternalSource(new AssetExternalSource
                    {
                        AssetId = assetId,
                        Source = ext.Source,
                        SourceId = ext.SourceId,
                        MediaId = ext.MediaId,
                        UploadAt = ext.UploadAt,
                        RawJson = ext.RawJson,
                        FetchedAt = ext.FetchedAt
                    });
                }
            }
        }

        return (metaChanged || tagsChanged || bookmarksAdded > 0, bookmarksAdded);
    }

    private static bool DoesWorkHaveApplicableChanges(
        SnapshotWorkDto sw,
        SnapshotWorkRecord? local,
        MetadataImportOptions options,
        out int bookmarksToAdd)
    {
        var existingPages = local?.Bookmarks.Select(b => b.PageIndex).ToHashSet() ?? new HashSet<int>();
        bookmarksToAdd = sw.Bookmarks.Count(b => !existingPages.Contains(b.PageIndex));

        if (local == null)
        {
            return true;
        }

        if (options.OverwriteExistingMetadata || options.OverwriteReadingProgress)
        {
            return true;
        }

        if (bookmarksToAdd > 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(local.Title) && !string.IsNullOrWhiteSpace(sw.Title)) return true;
        if (string.IsNullOrWhiteSpace(local.AuthorMainName) && !string.IsNullOrWhiteSpace(sw.AuthorMainName)) return true;
        if (string.IsNullOrWhiteSpace(local.Series) && !string.IsNullOrWhiteSpace(sw.Series)) return true;
        if (string.IsNullOrWhiteSpace(local.Language) && !string.IsNullOrWhiteSpace(sw.Language)) return true;
        if (!local.Rating.HasValue && sw.Rating.HasValue) return true;
        if (!local.LastPage.HasValue && sw.LastPage.HasValue) return true;
        if (local.ReadStatus == ReadStatus.Unread && sw.ReadStatus != ReadStatus.Unread) return true;
        if (sw.ReadCount > local.ReadCount) return true;

        var localTagNames = local.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sw.Tags.Any(t => !string.IsNullOrWhiteSpace(t.Name) && !localTagNames.Contains(t.Name))) return true;

        return false;
    }

    private int MergeAndSaveUnmappedItems(IReadOnlyList<UnmappedSnapshotWork> incoming)
    {
        if (incoming.Count == 0)
        {
            return LoadUnmappedItems().Count;
        }

        var existing = LoadUnmappedItems().ToList();
        var byKey = new Dictionary<string, UnmappedSnapshotWork>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in existing)
        {
            string key = BuildUnmappedDedupeKey(item.Work);
            byKey.TryAdd(key, item);
        }

        foreach (var item in incoming)
        {
            string key = BuildUnmappedDedupeKey(item.Work);
            if (!byKey.ContainsKey(key))
            {
                existing.Add(item);
                byKey[key] = item;
            }
        }

        SaveUnmappedItems(existing);
        return existing.Count;
    }

    private static string BuildUnmappedDedupeKey(SnapshotWorkDto w)
        => $"{w.RelativePath ?? w.FullPath}\0{w.FileName}\0{w.FileSize}";

    private void SaveUnmappedItems(IReadOnlyList<UnmappedSnapshotWork> items)
    {
        WriteJsonAtomically(_unmappedStatePath, items);
    }

    private static MetadataSnapshotDocument ReadSnapshotDocument(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var doc = JsonSerializer.Deserialize<MetadataSnapshotDocument>(json, JsonOptions);
        if (doc == null)
        {
            throw new InvalidDataException("無法解析 JSON 快照格式。");
        }

        if (doc.SchemaVersion > MetadataSnapshotDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"快照版本 v{doc.SchemaVersion} 高於目前程式支援版本 v{MetadataSnapshotDocument.CurrentSchemaVersion}。");
        }

        return doc;
    }

    private static void WriteJsonAtomically<T>(string path, T payload)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            File.WriteAllText(tmpPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmpPath, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private static string? MergeText(string? local, string? incoming, bool overwrite)
    {
        if (overwrite && !string.IsNullOrWhiteSpace(incoming))
        {
            return incoming;
        }

        return string.IsNullOrWhiteSpace(local) ? (string.IsNullOrWhiteSpace(incoming) ? local : incoming) : local;
    }

    private static long? MaxNullable(long? a, long? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        return Math.Max(a.Value, b.Value);
    }

    private static string? ComputeRelativePath(string? rootDir, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(rootDir) || string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        try
        {
            string normRoot = NormalizeDirOrNull(rootDir)!;
            string normFull = Path.GetFullPath(fullPath);
            if (!normFull.StartsWith(normRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normFull[(normRoot.Length + 1)..].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, SnapshotTagTranslationRecord> BuildTagTranslationMap(
        IReadOnlyList<SnapshotTagTranslationRecord> records)
    {
        var map = new Dictionary<string, SnapshotTagTranslationRecord>(records.Count, StringComparer.Ordinal);
        foreach (var t in records)
        {
            map[$"{t.TagName}\0{t.Lang}".ToLowerInvariant()] = t;
        }
        return map;
    }

    private static Dictionary<string, VirtualLibrary> BuildVirtualLibraryMap(
        IReadOnlyList<VirtualLibrary> virtualLibraries)
    {
        var map = new Dictionary<string, VirtualLibrary>(virtualLibraries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var v in virtualLibraries)
        {
            map[v.Name] = v;
        }
        return map;
    }

    private static string? NormalizeDirOrNull(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            return Path.GetFullPath(dir.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return dir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private sealed class CatalogMatchIndex
    {
        private readonly Dictionary<string, AssetCatalogEntry> _byFullPath;
        private readonly Dictionary<string, AssetCatalogEntry> _byRelativePath;
        private readonly Dictionary<string, List<AssetCatalogEntry>> _byFileKey;
        private readonly Dictionary<string, List<AssetCatalogEntry>> _byFolderName;
        private readonly string? _rootDirectory;

        private CatalogMatchIndex(
            Dictionary<string, AssetCatalogEntry> byFullPath,
            Dictionary<string, AssetCatalogEntry> byRelativePath,
            Dictionary<string, List<AssetCatalogEntry>> byFileKey,
            Dictionary<string, List<AssetCatalogEntry>> byFolderName,
            string? rootDirectory)
        {
            _byFullPath = byFullPath;
            _byRelativePath = byRelativePath;
            _byFileKey = byFileKey;
            _byFolderName = byFolderName;
            _rootDirectory = rootDirectory;
        }

        public static CatalogMatchIndex Build(IReadOnlyList<AssetCatalogEntry> entries, string? rootDirectory)
        {
            var byFull = new Dictionary<string, AssetCatalogEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
            var byRel = new Dictionary<string, AssetCatalogEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
            var byFile = new Dictionary<string, List<AssetCatalogEntry>>(entries.Count, StringComparer.OrdinalIgnoreCase);
            var byFolder = new Dictionary<string, List<AssetCatalogEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                string normPath = NormalizeFull(entry.Path);
                byFull.TryAdd(normPath, entry);

                string? rel = ComputeRelativePath(rootDirectory, normPath);
                if (!string.IsNullOrWhiteSpace(rel))
                {
                    byRel.TryAdd(rel, entry);
                }

                if (!entry.IsFolder && entry.FileSize.HasValue)
                {
                    string fileKey = $"{entry.Name}\0{entry.FileSize.Value}";
                    if (!byFile.TryGetValue(fileKey, out var list))
                    {
                        list = new List<AssetCatalogEntry>(1);
                        byFile[fileKey] = list;
                    }
                    list.Add(entry);
                }
                else if (entry.IsFolder)
                {
                    if (!byFolder.TryGetValue(entry.Name, out var list))
                    {
                        list = new List<AssetCatalogEntry>(1);
                        byFolder[entry.Name] = list;
                    }
                    list.Add(entry);
                }
            }

            return new CatalogMatchIndex(byFull, byRel, byFile, byFolder, rootDirectory);
        }

        public (AssetCatalogEntry? Entry, WorkMatchTier Tier) Resolve(SnapshotWorkDto sw)
        {
            // Tier 1: Full Path
            if (!string.IsNullOrWhiteSpace(sw.FullPath) && _byFullPath.TryGetValue(NormalizeFull(sw.FullPath), out var fullMatch))
            {
                return (fullMatch, WorkMatchTier.FullPath);
            }

            // Tier 2: Relative Path under current RootDirectory
            if (!string.IsNullOrWhiteSpace(sw.RelativePath) && !string.IsNullOrWhiteSpace(_rootDirectory))
            {
                string normRel = sw.RelativePath
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                    .TrimStart(Path.DirectorySeparatorChar);
                if (_byRelativePath.TryGetValue(normRel, out var relMatch))
                {
                    return (relMatch, WorkMatchTier.RelativePath);
                }
            }

            // Tier 3: FileName + FileSize (for files, unique match only) or unique FolderName
            if (!sw.IsFolder && !string.IsNullOrWhiteSpace(sw.FileName) && sw.FileSize.HasValue)
            {
                string key = $"{sw.FileName}\0{sw.FileSize.Value}";
                if (_byFileKey.TryGetValue(key, out var matches) && matches.Count == 1)
                {
                    return (matches[0], WorkMatchTier.FileNameAndSize);
                }
            }
            else if (sw.IsFolder && !string.IsNullOrWhiteSpace(sw.FileName))
            {
                if (_byFolderName.TryGetValue(sw.FileName, out var matches) && matches.Count == 1)
                {
                    return (matches[0], WorkMatchTier.FileNameAndSize);
                }
            }

            return (null, WorkMatchTier.Unmatched);
        }

        private static string NormalizeFull(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}

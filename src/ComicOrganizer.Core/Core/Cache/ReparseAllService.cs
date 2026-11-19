using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 整庫重 parse metadata。對 asset 表逐筆跑 FilenameMetadataParser，
/// 寫入 asset_meta + 補 author / tag 關聯。
///
/// === 規則 ===
/// - asset_meta.manually_edited = 1 的整筆跳過（保留使用者編輯）
/// - 保留 read_status / read_count / last_read / last_page / last_page
/// - parsed_at 更新為 now
///
/// === Author 解析優先順序 ===
/// 1. parser 純檔名 (asset.name) — filename 自帶 [Author] 多半穩定
/// 2. fallback：parent dir name → 先查 author.folder_path 反查現有作者
///                              → 否則 ParseAuthorFolderName
/// 3. 都無 → asset_meta.author_id = NULL
/// </summary>
public class ReparseAllService
{
    private readonly IAssetMetaStore _metaStore;
    private readonly RebuildProgressTracker _tracker;
    private readonly Func<string?>? _libraryRootProvider;

    public ReparseAllService(IAssetMetaStore metaStore, RebuildProgressTracker tracker,
                             Func<string?>? libraryRootProvider = null)
    {
        _metaStore = metaStore;
        _tracker = tracker;
        _libraryRootProvider = libraryRootProvider;
    }

    public class Summary
    {
        public int Total { get; set; }
        public int Reparsed { get; set; }
        public int Skipped { get; set; }       // manually_edited = 1
        public int Cataloged { get; set; }     // 掃描磁碟補登的遺漏 catalog 筆數
        public int Failed { get; set; }
        public bool Cancelled { get; set; }
    }

    /// <summary>
    /// 掃描 libraryRoot 目錄樹，找出磁碟存在但尚未編目於 asset 表的 Author/Work 目錄與 archive/PDF 檔案（略過獨立圖片與 Unknown/Category 容器）。
    /// 只做快速 fs metadata 收集並透過 UpsertCatalogOnly 補登，不預先解碼縮圖。
    /// </summary>
    private List<AssetMeta> CollectMissingCatalogItems(
        string libraryRoot, FolderRoleDetector detector, CancellationToken ct)
    {
        var knownPaths = new HashSet<string>(
            _metaStore.ListAssetEntries().Select(e => e.Path),
            StringComparer.OrdinalIgnoreCase);

        var missing = new List<AssetMeta>();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var stack = new Stack<string>();
        stack.Push(libraryRoot);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                ct.ThrowIfCancellationRequested();
                var subName = Path.GetFileName(sub);
                if (string.IsNullOrEmpty(subName) || subName.StartsWith('.'))
                    continue;

                stack.Push(sub);

                if (knownPaths.Contains(sub))
                    continue;

                var role = detector.Detect(sub);
                if (role is FolderRole.Author or FolderRole.Work)
                {
                    try
                    {
                        var di = new DirectoryInfo(sub);
                        missing.Add(new AssetMeta
                        {
                            Path = sub,
                            Name = di.Name,
                            IsFolder = true,
                            FileSize = null,
                            Mtime = new DateTimeOffset(di.LastWriteTimeUtc).ToUnixTimeSeconds(),
                            LastScanned = nowUnix,
                        });
                        knownPaths.Add(sub);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException) { }
                }
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!SupportedExtensions.BuiltInReader.Contains(Path.GetExtension(file)))
                    continue;

                if (knownPaths.Contains(file))
                    continue;

                try
                {
                    var fi = new FileInfo(file);
                    missing.Add(new AssetMeta
                    {
                        Path = file,
                        Name = fi.Name,
                        IsFolder = false,
                        FileSize = fi.Length,
                        Mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds(),
                        LastScanned = nowUnix,
                    });
                    knownPaths.Add(file);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or FileNotFoundException or IOException) { }
            }
        }

        return missing;
    }

    /// <summary>整庫重 parse；走 _tracker 顯示進度，支援取消。</summary>
    public Task<Summary> RunAsync()
    {
        var ct = _tracker.Start("整庫重新解析中");
        return Task.Run(() =>
        {
            var summary = new Summary();
            try
            {
                var libraryRoot = _libraryRootProvider?.Invoke();
                FolderRoleDetector? detector = (!string.IsNullOrWhiteSpace(libraryRoot) && Directory.Exists(libraryRoot))
                    ? new FolderRoleDetector(libraryRoot)
                    : null;

                // Pass -1：補登磁碟上存在但尚未寫入 asset 表的作品檔案（archive / pdf）與作者/作品資料夾
                if (detector != null && !string.IsNullOrWhiteSpace(libraryRoot))
                {
                    var missing = CollectMissingCatalogItems(libraryRoot, detector, ct);
                    if (missing.Count > 0)
                    {
                        _metaStore.RunInTransaction(() =>
                        {
                            foreach (var item in missing)
                                _metaStore.UpsertCatalogOnly(item);
                        });
                        summary.Cataloged = missing.Count;
                    }
                }

                var entries = _metaStore.ListAssetEntries();
                summary.Total = entries.Count;

                // Pass 0：先補登所有在目錄樹中尚未入 author 表的作者資料夾（涵蓋根目錄直屬資料夾與已編目資料夾）
                if (detector != null && !string.IsNullOrWhiteSpace(libraryRoot))
                {
                    var candidateDirs = new HashSet<string>(
                        entries.Where(e => e.IsFolder).Select(e => e.Path),
                        StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (var topDir in Directory.EnumerateDirectories(libraryRoot))
                            candidateDirs.Add(topDir);
                    }
                    catch { /* 忽略磁碟列舉權限錯誤 */ }

                    _metaStore.RunInTransaction(() =>
                    {
                        foreach (var dirPath in candidateDirs)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (Directory.Exists(dirPath) && detector.Detect(dirPath) == FolderRole.Author)
                            {
                                var (main, alias) = FilenameMetadataParser.ParseAuthorFolderName(Path.GetFileName(dirPath));
                                if (!string.IsNullOrWhiteSpace(main))
                                {
                                    long aid = _metaStore.UpsertAuthor(main.Trim(), dirPath);
                                    if (!string.IsNullOrWhiteSpace(alias))
                                        _metaStore.AddAuthorAlias(aid, alias.Trim());
                                }
                            }
                        }
                    });
                }

                int done = 0;
                const int batchSize = 50;
                for (int i = 0; i < entries.Count; i += batchSize)
                {
                    ct.ThrowIfCancellationRequested();
                    var batch = entries.Skip(i).Take(batchSize).ToList();

                    _metaStore.RunInTransaction(() =>
                    {
                        foreach (var entry in batch)
                        {
                            _tracker.Update(new RebuildProgress
                            {
                                Done = done,
                                Total = entries.Count,
                                CurrentPath = entry.Path,
                            });

                            try
                            {
                                FolderRole? detectedRole = (detector != null && entry.IsFolder && Directory.Exists(entry.Path))
                                    ? detector.Detect(entry.Path)
                                    : null;
                                bool isNonWorkContainer = detectedRole is FolderRole.Author or FolderRole.Category or FolderRole.Library
                                    || (entry.IsFolder && _metaStore.IsAuthorFolderPath(entry.Path));
                                bool isStandaloneImage = !entry.IsFolder && SupportedExtensions.IsImage(entry.Path);

                                if (isNonWorkContainer || isStandaloneImage)
                                {
                                    _metaStore.DeleteMetadata(entry.Id);
                                    _metaStore.SetAssetTagEntries(entry.Id, Array.Empty<TagEntry>());
                                    summary.Skipped++;
                                }
                                else
                                {
                                    var existing = _metaStore.GetMetadata(entry.Id);
                                    if (existing != null && existing.ManuallyEdited)
                                    {
                                        summary.Skipped++;
                                    }
                                    else
                                    {
                                        ReparseSingle(entry, existing, detector, libraryRoot);
                                        summary.Reparsed++;
                                    }
                                }
                            }
                            catch
                            {
                                summary.Failed++;
                            }

                            done++;
                        }
                    });
                }

                _metaStore.DeleteOrphanTags();
                _tracker.NotifyEnd(cancelled: false);
            }
            catch (OperationCanceledException)
            {
                summary.Cancelled = true;
                _tracker.NotifyEnd(cancelled: true);
            }
            return summary;
        }, ct);
    }

    private void ReparseSingle(AssetCatalogEntry entry, AssetMetadata? existing,
                               FolderRoleDetector? detector, string? libraryRoot)
    {
        var parentDir = Path.GetDirectoryName(entry.Path);
        var (authorFromFolder, authorFolderPath) = FindAncestorAuthorInfo(entry.Path, parentDir, detector, libraryRoot);

        // Pass 1: 若父／祖先目錄為已登錄作者資料夾，帶入主作者名並同步剝除檔名自帶社團/筆名括號
        var parsed = FilenameMetadataParser.ParseWorkName(entry.Name, authorFromParent: authorFromFolder);

        // Pass 2: filename 沒抓到 author → 用父目錄反查 / 解析
        if (string.IsNullOrEmpty(parsed.AuthorMainName) && !string.IsNullOrEmpty(parentDir))
        {
            var existingAuthorId = _metaStore.ResolveAuthorIdByPath(parentDir);
            if (existingAuthorId.HasValue)
            {
                var existingAuthor = _metaStore.GetAuthorInfo(existingAuthorId.Value)?.Author;
                if (existingAuthor != null)
                {
                    parsed.AuthorMainName = existingAuthor.MainName;
                    authorFolderPath ??= existingAuthor.FolderPath;
                }
            }
            else
            {
                var (mainName, alias) = FilenameMetadataParser.ParseAuthorFolderName(
                    Path.GetFileName(parentDir));
                if (!string.IsNullOrEmpty(mainName))
                {
                    parsed.AuthorMainName = mainName;
                    if (!string.IsNullOrEmpty(alias) && string.IsNullOrEmpty(parsed.AuthorAlias))
                        parsed.AuthorAlias = alias;
                }
            }
        }

        // 解析作者名 → author_id（找不到就 upsert；若已知作者資料夾路徑一併補齊）
        long? authorId = null;
        if (!string.IsNullOrWhiteSpace(parsed.AuthorMainName))
        {
            var upsertFolder = string.Equals(parsed.AuthorMainName.Trim(), authorFromFolder, StringComparison.OrdinalIgnoreCase)
                ? authorFolderPath
                : null;
            authorId = _metaStore.UpsertAuthor(parsed.AuthorMainName.Trim(), upsertFolder);
            if (!string.IsNullOrWhiteSpace(parsed.AuthorAlias))
                _metaStore.AddAuthorAlias(authorId.Value, parsed.AuthorAlias.Trim());
        }

        // 寫 asset_meta — 保留閱讀記錄
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var meta = new AssetMetadata
        {
            AssetId = entry.Id,
            AuthorId = authorId,
            Title = parsed.Title,
            ChapterNo = parsed.ChapterNo,
            Language = parsed.Language,
            TranslationGroup = parsed.TranslationGroup,
            Series = existing?.Series,                 // parser 不抽 series，保留
            PageCount = existing?.PageCount,
            Rating = existing?.Rating,
            ReadStatus = existing?.ReadStatus ?? ReadStatus.Unread,
            ReadCount = existing?.ReadCount ?? 0,
            LastRead = existing?.LastRead,
            LastPage = existing?.LastPage,
            ExternalId = existing?.ExternalId,
            ParsedAt = now,
            ManuallyEdited = false,                    // 自動 parse 出來不可能 manually_edited
        };
        _metaStore.UpsertMetadata(meta);

        // 寫 tags — 用 SetAssetTagEntries 保留 parody/event 分類，只排除 language / translation_group
        var filteredTags = parsed.Tags
            .Where(t => !string.Equals(t.Category, "language", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(t.Category, "translation_group", StringComparison.OrdinalIgnoreCase))
            .ToList();
        _metaStore.SetAssetTagEntries(entry.Id, filteredTags);
    }

    private (string? MainName, string? FolderPath) FindAncestorAuthorInfo(
        string entryPath, string? parentDir, FolderRoleDetector? detector, string? libraryRoot)
    {
        if (string.IsNullOrEmpty(parentDir)) return (null, null);

        // 1. 若無 detector（單元測試等離線 DB 情境），僅檢查直接父目錄是否已在 author.folder_path 登錄
        if (detector == null || string.IsNullOrWhiteSpace(libraryRoot))
        {
            if (!_metaStore.IsAuthorFolderPath(parentDir)) return (null, null);
            var fid = _metaStore.ResolveAuthorIdByPath(parentDir);
            var author = fid.HasValue ? _metaStore.GetAuthorInfo(fid.Value)?.Author : null;
            return (author?.MainName, author?.FolderPath);
        }

        // 2. 有磁碟根目錄時往上找最近 Author 層（跨過中間 Series / 合集層）
        var libNorm = libraryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = entryPath;
        for (int depth = 0; depth < 10; depth++)
        {
            var dir = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(dir)) return (null, null);
            var dirNorm = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (dirNorm.Equals(libNorm, StringComparison.OrdinalIgnoreCase)) return (null, null);

            if (_metaStore.IsAuthorFolderPath(dir))
            {
                var fid = _metaStore.ResolveAuthorIdByPath(dir);
                var name = fid.HasValue ? _metaStore.GetAuthorInfo(fid.Value)?.Author.MainName : null;
                if (!string.IsNullOrEmpty(name)) return (name, dir);
            }
            if (Directory.Exists(dir))
            {
                var role = detector.Detect(dir);
                if (role == FolderRole.Author)
                {
                    var (mainP, _) = FilenameMetadataParser.ParseAuthorFolderName(Path.GetFileName(dir));
                    return (string.IsNullOrEmpty(mainP) ? null : mainP, dir);
                }
                if (role == FolderRole.Library) return (null, null);
            }
            current = dir;
        }
        return (null, null);
    }
}

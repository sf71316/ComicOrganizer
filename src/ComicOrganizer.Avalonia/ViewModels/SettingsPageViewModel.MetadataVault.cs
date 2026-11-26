using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Notifications;
using ComicOrganizer.App.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotificationType = Avalonia.Controls.Notifications.NotificationType;

namespace ComicOrganizer.Avalonia.ViewModels;

public sealed record VaultCatalogPickOption(long AssetId, string DisplayTitle, string FullPath)
{
    public override string ToString() => DisplayTitle;
}

public sealed partial class VaultUnmappedWorkItemVm : ObservableObject
{
    private readonly Action<VaultUnmappedWorkItemVm> _onAssignSelected;
    private readonly Action<VaultUnmappedWorkItemVm> _onToggleSkip;
    private readonly Action<VaultUnmappedWorkItemVm> _onDiscard;

    public VaultUnmappedWorkItemVm(
        UnmappedSnapshotWork model,
        Action<VaultUnmappedWorkItemVm> onAssignSelected,
        Action<VaultUnmappedWorkItemVm> onToggleSkip,
        Action<VaultUnmappedWorkItemVm> onDiscard)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _onAssignSelected = onAssignSelected;
        _onToggleSkip = onToggleSkip;
        _onDiscard = onDiscard;
        _status = model.Status;
    }

    public UnmappedSnapshotWork Model { get; }
    public string ItemId => Model.ItemId;
    public string Title => string.IsNullOrWhiteSpace(Model.Work.Title) ? Model.Work.FileName : Model.Work.Title!;
    public string AuthorText => string.IsNullOrWhiteSpace(Model.Work.AuthorMainName) ? "未知作者" : Model.Work.AuthorMainName!;
    public string PathHintText => string.IsNullOrWhiteSpace(Model.Work.RelativePath)
        ? Model.Work.FullPath
        : $"{Model.Work.RelativePath}（原檔：{Model.Work.FileName}）";
    public string MetaSummaryText => $"標籤 {Model.Work.Tags.Count} · 書籤 {Model.Work.Bookmarks.Count} · 評分 {(Model.Work.Rating?.ToString() ?? "-")} · 頁數 {(Model.Work.PageCount?.ToString() ?? "-")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBadgeText))]
    [NotifyPropertyChangedFor(nameof(SkipToggleButtonText))]
    private UnmappedWorkStatus _status;

    public string StatusBadgeText => Status == UnmappedWorkStatus.Skipped ? "已略過（保留待查）" : "待指定對應";
    public string SkipToggleButtonText => Status == UnmappedWorkStatus.Skipped ? "↩ 取消略過" : "⏭ 略過";

    [RelayCommand]
    private void AssignToSelectedTarget() => _onAssignSelected(this);

    [RelayCommand]
    private void ToggleSkip() => _onToggleSkip(this);

    [RelayCommand]
    private void Discard() => _onDiscard(this);
}

public sealed partial class DatabaseBackupItemVm : ObservableObject
{
    private readonly Action<DatabaseBackupItemVm> _onRestore;

    public DatabaseBackupItemVm(DatabaseBackupEntry entry, Action<DatabaseBackupItemVm> onRestore)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _onRestore = onRestore;
    }

    public DatabaseBackupEntry Entry { get; }
    public string FileName => Entry.FileName;
    public string CreatedAtText => Entry.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string ReasonText => Entry.Reason;
    public string SizeText => FormatBytes(Entry.FileSizeBytes);
    public string CompanionText => Entry.HasMappingsBackup ? "含 mappings.json" : "僅 DB";

    [RelayCommand]
    private void Restore() => _onRestore(this);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        return $"{kb / 1024.0:F2} MB";
    }
}

public sealed partial class SettingsPageViewModel
{
    private MetadataImportAnalysisReport? _pendingImportReport;
    private IReadOnlyList<AssetCatalogEntry>? _cachedCatalogEntriesForUnmapped;

    /// <summary>
    /// 由 View 注入之儲存檔案對話框：(標題, 預設檔名, 預設副檔名, 篩選說明, 副檔名清單) → 路徑。
    /// </summary>
    public Func<string, string, string, string, IReadOnlyList<string>, Task<string?>>? SaveFileAsync { get; set; }

    /// <summary>
    /// 由 View 注入之危險操作確認對話框：(標題, 說明) → true 表示確認執行。
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmActionAsync { get; set; }

    public ObservableCollection<VaultUnmappedWorkItemVm> UnmappedImportItems { get; } = new();
    public ObservableCollection<VaultCatalogPickOption> CatalogCandidateMatches { get; } = new();
    public ObservableCollection<DatabaseBackupItemVm> DatabaseBackups { get; } = new();

    public bool IsDatabaseWritable => !_dataContext.IsReadOnlyDatabase;

    public string BackupsDirectoryPath => new DatabaseBackupService(_dataContext.DataDirectory).BackupsDirectory;

    [ObservableProperty]
    private bool _vaultIncludeExternalSources;

    [ObservableProperty]
    private bool _vaultIncludeLearnedMappings = true;

    [ObservableProperty]
    private bool _vaultOverwriteMetadata;

    [ObservableProperty]
    private bool _vaultOverwriteReadingProgress;

    [ObservableProperty]
    private bool _vaultOverwriteTagTranslations;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyVaultImport))]
    [NotifyPropertyChangedFor(nameof(CanOperateVaultWriteActions))]
    private bool _isVaultBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingImportAnalysis))]
    [NotifyPropertyChangedFor(nameof(CanApplyVaultImport))]
    private string _vaultImportFilePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingImportAnalysis))]
    [NotifyPropertyChangedFor(nameof(CanApplyVaultImport))]
    private string _vaultImportPreviewText = "尚未載入匯入快照。請先點擊「📂 選擇快照檔進行分析」，檢視差異預覽後再決定是否套用。";

    [ObservableProperty]
    private string _vaultUnmappedSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignSelectedCatalogTarget))]
    private VaultCatalogPickOption? _selectedCatalogMatchTarget;

    [ObservableProperty]
    private string _vaultStatusMessage = string.Empty;

    public bool HasPendingImportAnalysis => _pendingImportReport != null;
    public bool CanOperateVaultWriteActions => IsDatabaseWritable && !IsVaultBusy;
    public bool CanApplyVaultImport => IsDatabaseWritable && !IsVaultBusy && _pendingImportReport != null;
    public bool HasUnmappedImportItems => UnmappedImportItems.Count > 0;
    public bool HasDatabaseBackups => DatabaseBackups.Count > 0;
    public bool CanAssignSelectedCatalogTarget => IsDatabaseWritable && !IsVaultBusy && SelectedCatalogMatchTarget != null;

    public string UnmappedSummaryText => UnmappedImportItems.Count == 0
        ? "目前沒有待處理的未對應作品。"
        : $"尚有 {UnmappedImportItems.Count} 筆未對應作品（{UnmappedImportItems.Count(i => i.Status == UnmappedWorkStatus.Pending)} 筆待指定 · {UnmappedImportItems.Count(i => i.Status == UnmappedWorkStatus.Skipped)} 筆已略過）";

    private void InitializeMetadataVaultSection()
    {
        RefreshUnmappedImportItems();
        RefreshDatabaseBackups();
    }

    partial void OnVaultUnmappedSearchTextChanged(string value)
    {
        RefreshUnmappedCatalogCandidates(value);
    }

    private MetadataSnapshotService CreateSnapshotService()
        => new(_dataContext.MetaStore, _dataContext.DataDirectory, _dataContext.Config.RootDirectory);

    [RelayCommand]
    private async Task ExportSnapshotJsonAsync()
    {
        if (IsVaultBusy) return;
        string? outputPath = SaveFileAsync != null
            ? await SaveFileAsync(
                "匯出 Metadata 保險箱 JSON 快照",
                $"comic_metadata_snapshot_{DateTime.Now:yyyyMMdd_HHmm}.json",
                "json",
                "Metadata 快照檔 (*.json)",
                ["*.json"])
            : null;

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        IsVaultBusy = true;
        try
        {
            var opts = new MetadataSnapshotExportOptions(
                IncludeExternalSources: VaultIncludeExternalSources,
                IncludeLearnedMappings: VaultIncludeLearnedMappings);
            var summary = await CreateSnapshotService().ExportToJsonAsync(outputPath, opts).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                VaultStatusMessage = $"已匯出快照（{summary.WorkCount:N0} 作品 / {summary.TagCount:N0} 標籤 / {summary.TagTranslationCount:N0} 譯文 / {summary.BookmarkCount:N0} 書籤 / {summary.LearnedMappingCount:N0} 規則）至 {summary.OutputPath}";
                ShowToast("快照匯出完成", VaultStatusMessage, NotificationType.Success, 5);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() => ShowToast("快照匯出失敗", ex.Message, NotificationType.Error, 5)).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsVaultBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task ExportWorksCsvAsync()
    {
        if (IsVaultBusy) return;
        string? outputPath = SaveFileAsync != null
            ? await SaveFileAsync(
                "匯出作品 Metadata 清冊 CSV",
                $"comic_catalog_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                "csv",
                "CSV 清冊 (*.csv)",
                ["*.csv"])
            : null;

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        IsVaultBusy = true;
        try
        {
            int count = await CreateSnapshotService().ExportWorksToCsvAsync(outputPath).ConfigureAwait(false);
            await _runOnUiAsync(() =>
            {
                VaultStatusMessage = $"已匯出 {count:N0} 筆作品清冊至 {outputPath}";
                ShowToast("CSV 匯出完成", VaultStatusMessage, NotificationType.Success, 4);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() => ShowToast("CSV 匯出失敗", ex.Message, NotificationType.Error, 5)).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsVaultBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task AnalyzeImportSnapshotAsync()
    {
        if (IsVaultBusy) return;

        string? filePath = PickFileAsync != null
            ? await PickFileAsync(
                "選擇要匯入分析的 Metadata JSON 快照",
                _dataContext.DataDirectory,
                "Metadata 快照檔 (*.json)",
                ["*.json"])
            : null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await AnalyzeImportSnapshotPathAsync(filePath).ConfigureAwait(false);
    }

    public async Task AnalyzeImportSnapshotPathAsync(string filePath)
    {
        if (IsVaultBusy || string.IsNullOrWhiteSpace(filePath)) return;

        IsVaultBusy = true;
        try
        {
            var opts = new MetadataImportOptions(
                OverwriteExistingMetadata: VaultOverwriteMetadata,
                OverwriteReadingProgress: VaultOverwriteReadingProgress,
                OverwriteTagTranslations: VaultOverwriteTagTranslations,
                ImportLearnedMappings: VaultIncludeLearnedMappings);

            var report = await CreateSnapshotService().AnalyzeImportAsync(filePath, opts).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _pendingImportReport = report;
                VaultImportFilePath = filePath;
                VaultImportPreviewText = report.BuildSummaryText();
                OnPropertyChanged(nameof(HasPendingImportAnalysis));
                OnPropertyChanged(nameof(CanApplyVaultImport));
                VaultStatusMessage = "已完成匯入差異分析（尚未寫入資料庫）。";
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() =>
            {
                _pendingImportReport = null;
                VaultImportPreviewText = $"解析快照失敗：{ex.Message}";
                OnPropertyChanged(nameof(HasPendingImportAnalysis));
                OnPropertyChanged(nameof(CanApplyVaultImport));
                ShowToast("快照分析失敗", ex.Message, NotificationType.Error, 5);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsVaultBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public async Task ApplyImportSnapshotAsync()
    {
        if (IsVaultBusy || _pendingImportReport == null) return;
        if (_dataContext.IsReadOnlyDatabase)
        {
            ShowToast("唯讀保護", "共用開發庫為唯讀模式，已攔截匯入寫入。", NotificationType.Warning, 4);
            return;
        }

        var reportToApply = _pendingImportReport;
        IsVaultBusy = true;
        try
        {
            var opts = new MetadataImportOptions(
                OverwriteExistingMetadata: VaultOverwriteMetadata,
                OverwriteReadingProgress: VaultOverwriteReadingProgress,
                OverwriteTagTranslations: VaultOverwriteTagTranslations,
                ImportLearnedMappings: VaultIncludeLearnedMappings);

            var result = await Task.Run(() =>
            {
                _dataContext.TryCreateSafetyBackup("before_import");
                return CreateSnapshotService().ApplyImport(reportToApply, opts);
            }).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _pendingImportReport = null;
                _cachedCatalogEntriesForUnmapped = null;
                OnPropertyChanged(nameof(HasPendingImportAnalysis));
                OnPropertyChanged(nameof(CanApplyVaultImport));
                RefreshUnmappedImportItems();
                RefreshDatabaseBackups();

                VaultStatusMessage =
                    $"匯入完成（已自動建立安全備份）：更新作品 {result.WorksUpdated:N0} 筆 · 新增標籤 {result.TagsAddedOrUpdated:N0} · 譯文 {result.TagTranslationsAddedOrUpdated:N0} · 書籤 {result.BookmarksAdded:N0} · 歸檔規則 {result.LearnedMappingsAddedOrUpdated:N0} · 未對應保存 {result.UnmatchedSavedCount:N0} 筆";
                VaultImportPreviewText = VaultStatusMessage;
                _dataContext.NotifySettingsChanged();
                ShowToast("Metadata 匯入成功", VaultStatusMessage, NotificationType.Success, 5);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() => ShowToast("套用匯入失敗", ex.Message, NotificationType.Error, 5)).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsVaultBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void RefreshUnmappedImportItems()
    {
        var items = CreateSnapshotService().LoadUnmappedItems();
        UnmappedImportItems.Clear();
        foreach (var item in items)
        {
            UnmappedImportItems.Add(new VaultUnmappedWorkItemVm(
                item,
                OnAssignUnmappedWork,
                OnToggleSkipUnmappedWork,
                OnDiscardUnmappedWork));
        }

        OnPropertyChanged(nameof(HasUnmappedImportItems));
        OnPropertyChanged(nameof(UnmappedSummaryText));

        if (UnmappedImportItems.Count > 0 && CatalogCandidateMatches.Count == 0)
        {
            RefreshUnmappedCatalogCandidates(VaultUnmappedSearchText);
        }
    }

    [RelayCommand]
    private void RefreshUnmappedCatalogCandidates(string? keyword = null)
    {
        try
        {
            _cachedCatalogEntriesForUnmapped ??= _dataContext.MetaStore.ListAssetEntries();
            var kw = string.IsNullOrWhiteSpace(keyword) ? VaultUnmappedSearchText.Trim() : keyword.Trim();

            var filtered = _cachedCatalogEntriesForUnmapped
                .Where(e => string.IsNullOrEmpty(kw)
                    || e.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || e.Path.Contains(kw, StringComparison.OrdinalIgnoreCase))
                .Take(40)
                .Select(e => new VaultCatalogPickOption(
                    e.Id,
                    $"{(e.IsFolder ? "📁" : "📦")} {e.Name}（{Path.GetFileName(Path.GetDirectoryName(e.Path) ?? string.Empty)}）",
                    e.Path))
                .ToList();

            CatalogCandidateMatches.Clear();
            foreach (var opt in filtered)
            {
                CatalogCandidateMatches.Add(opt);
            }

            SelectedCatalogMatchTarget = CatalogCandidateMatches.FirstOrDefault();
            OnPropertyChanged(nameof(CanAssignSelectedCatalogTarget));
        }
        catch
        {
        }
    }

    private void OnAssignUnmappedWork(VaultUnmappedWorkItemVm vm)
    {
        if (_dataContext.IsReadOnlyDatabase)
        {
            ShowToast("唯讀保護", "共用開發庫為唯讀模式，不可寫入對應關係。", NotificationType.Warning, 4);
            return;
        }

        if (SelectedCatalogMatchTarget == null)
        {
            ShowToast("請先選擇庫內作品", "請於上方搜尋並挑選要對應的本機作品後再套用。", NotificationType.Warning, 4);
            return;
        }

        var opts = new MetadataImportOptions(
            OverwriteExistingMetadata: VaultOverwriteMetadata,
            OverwriteReadingProgress: VaultOverwriteReadingProgress,
            OverwriteTagTranslations: VaultOverwriteTagTranslations,
            ImportLearnedMappings: false);

        if (CreateSnapshotService().ApplyManualWorkResolution(vm.ItemId, SelectedCatalogMatchTarget.AssetId, opts))
        {
            RefreshUnmappedImportItems();
            _dataContext.NotifySettingsChanged();
            VaultStatusMessage = $"已將未對應項目「{vm.Title}」手動套用至 {SelectedCatalogMatchTarget.DisplayTitle}";
            ShowToast("手動對應成功", VaultStatusMessage, NotificationType.Success, 4);
        }
    }

    private void OnToggleSkipUnmappedWork(VaultUnmappedWorkItemVm vm)
    {
        bool nextSkipped = vm.Status != UnmappedWorkStatus.Skipped;
        if (CreateSnapshotService().SetUnmappedItemSkipped(vm.ItemId, nextSkipped))
        {
            vm.Status = nextSkipped ? UnmappedWorkStatus.Skipped : UnmappedWorkStatus.Pending;
            OnPropertyChanged(nameof(UnmappedSummaryText));
        }
    }

    private void OnDiscardUnmappedWork(VaultUnmappedWorkItemVm vm)
    {
        if (CreateSnapshotService().DiscardUnmappedItem(vm.ItemId))
        {
            RefreshUnmappedImportItems();
        }
    }

    [RelayCommand]
    private void RefreshDatabaseBackups()
    {
        var list = new DatabaseBackupService(_dataContext.DataDirectory).ListBackups();
        DatabaseBackups.Clear();
        foreach (var entry in list)
        {
            DatabaseBackups.Add(new DatabaseBackupItemVm(entry, b => _ = RestoreFromBackupItemAsync(b)));
        }

        OnPropertyChanged(nameof(HasDatabaseBackups));
        OnPropertyChanged(nameof(BackupsDirectoryPath));
    }

    [RelayCommand]
    public async Task CreateManualBackupAsync()
    {
        if (IsVaultBusy) return;
        if (_dataContext.IsReadOnlyDatabase)
        {
            ShowToast("唯讀保護", "共用開發庫為唯讀模式，不可建立備份寫入。", NotificationType.Warning, 4);
            return;
        }

        IsVaultBusy = true;
        try
        {
            var entry = await Task.Run(() => _dataContext.CreateSafetyBackup("manual")).ConfigureAwait(false);
            await _runOnUiAsync(() =>
            {
                RefreshDatabaseBackups();
                VaultStatusMessage = $"已建立資料庫熱備份：{entry.FileName}（自動保留最近 {DatabaseBackupService.DefaultRetainCount} 份）";
                ShowToast("備份建立完成", VaultStatusMessage, NotificationType.Success, 4);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() => ShowToast("建立備份失敗", ex.Message, NotificationType.Error, 5)).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsVaultBusy = false).ConfigureAwait(false);
        }
    }

    private async Task RestoreFromBackupItemAsync(DatabaseBackupItemVm item)
    {
        if (IsVaultBusy) return;
        if (_dataContext.IsReadOnlyDatabase)
        {
            ShowToast("唯讀保護", "共用開發庫為唯讀模式，已攔截還原操作。", NotificationType.Warning, 4);
            return;
        }

        if (ConfirmActionAsync != null)
        {
            bool confirmed = await ConfirmActionAsync(
                "確認還原資料庫備份",
                $"確定要將 cache.db 還原至備份「{item.FileName}」（{item.CreatedAtText}）嗎？\n系統會在覆蓋前先自動備份目前資料庫（before_restore），以防誤操作。");
            if (!confirmed)
            {
                return;
            }
        }

        IsVaultBusy = true;
        try
        {
            await Task.Run(() =>
                _dataContext.RestoreDatabaseFromBackup(item.Entry.FullPath, item.Entry.MappingsBackupPath)).ConfigureAwait(false);

            await _runOnUiAsync(() =>
            {
                _cachedCatalogEntriesForUnmapped = null;
                RefreshDatabaseBackups();
                VaultStatusMessage = $"已成功從備份 {item.FileName} 還原資料庫與規則，畫面已同步更新。";
                ShowToast("資料庫還原完成", VaultStatusMessage, NotificationType.Success, 5);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _runOnUiAsync(() => ShowToast("還原備份失敗", ex.Message, NotificationType.Error, 5)).ConfigureAwait(false);
        }
        finally
        {
            await _runOnUiAsync(() => IsVaultBusy = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void OpenBackupsFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupsDirectoryPath);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupsDirectoryPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast("無法開啟備份資料夾", ex.Message, NotificationType.Warning, 4);
        }
    }
}

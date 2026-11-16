using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

/// <summary>
/// GalleryControl 內容載入 — folder 模式 + virtual 模式 + List view + cover preview popup
/// + 卡片點擊路由（reader / explorer / external viewer）。
/// 從 GalleryControl.cs 抽出。
/// </summary>
public partial class GalleryControl
{
    /// <summary>建一張完整 wired 的 ComicCardControl — folder mode + virtual mode 共用。
    /// 把 ~22 行 event handler 連線從 LoadGalleryAsync / LoadGalleryFromPathsAsync 抽出，
    /// 順帶 fire-and-forget 載入封面。
    /// </summary>
    /// <param name="fsi">file/dir info（卡片資料來源）。</param>
    /// <param name="mode">S/M/L/List view mode（影響卡片 size + 內部 layout）。</param>
    /// <param name="showMetadata">虛擬庫 mode 套作品 metadata view 在卡片上（folder mode 不套，
    /// 因為 folder tree 已暗示作者，重複顯示冗餘）。</param>
    private ComicCardControl BuildCardForGallery(
        FileSystemInfo fsi,
        GalleryViewMode mode,
        bool showMetadata,
        GalleryItemSortMetadata? sortMeta = null)
    {
        bool isFolderNode = fsi is DirectoryInfo;
        var card = new ComicCardControl();
        card.SetData(fsi, isFolderNode);
        card.Tag = sortMeta ?? BuildSortMetadata(fsi, statsMap: null);

        if (showMetadata)
        {
            var view = _metaStore?.GetMetadataView(fsi.FullName);
            if (view != null) card.SetMetadata(view);
        }
        card.ApplyViewMode(mode);
        card.ReadStatusProvider = p => _metaStore?.GetReadStatus(p);
        card.ProgressProvider = p =>
        {
            if (_metaStore == null) return null;
            var lp = _metaStore.GetLastPage(p);
            var view = _metaStore.GetMetadataView(p);
            return (lp, view?.PageCount);
        };
        card.VirtualLibrariesProvider = () => _metaStore?.ListVirtualLibraries() ?? Array.Empty<VirtualLibrary>();
        card.WorkLibraryIdsProvider = p => _metaStore?.ListVirtualLibraryIdsForWork(p) ?? Array.Empty<long>();
        card.RefreshProgress();

        // 透過 queue 延遲 RecordRead；queue.Recorded event 會 RefreshCardReadStatusByPath
        card.RequestOpenFolder += p => { EnqueueReadIfEnabled(p); OpenFolderTab(p, false); };
        card.RequestOpenViewer += p => { EnqueueReadIfEnabled(p); OpenForReading(p); };
        card.RequestNewTab += (p, forceNew) =>
        {
            EnqueueReadIfEnabled(p);
            if (Directory.Exists(p)) OpenFolderTab(p, forceNew);
            else OpenForReading(p);
        };
        card.RequestExplorer += p => OpenInExplorer(p);
        card.RequestRefreshCover += p => _ = RefreshWorkCoverAsync(p);
        card.RequestAuthorInfo += p => ShowAuthorInfo(p);
        card.RequestWorkDetail += p => ShowWorkDetail(p);
        card.RequestToggleVirtualLibrary += (p, libId) => ToggleWorkInVirtualLibrary(libId, p);
        card.RequestCreateVirtualLibraryAndAdd += p => CreateVirtualLibraryAndAddWork(p);
        // 強制走外部開啟（不看 UseBuiltInReader 偏好）：漫畫格式 → 外部 viewer；其他 → 系統預設
        card.RequestOpenWithExternal += p =>
        {
            EnqueueReadIfEnabled(p);
            if (WorkContextMenuHelper.CanUseBuiltInReader(p)) OpenWithExternalViewer(p);
            else OpenWithSystemDefault(p);
        };
        card.RequestSetReadStatus += (p, status) => SetWorkReadStatus(p, status);

        // 目錄與檔案都試著載封面：CoverManager 對目錄會遞迴往下找首張圖檔（受 MaxScanDepth 限制）
        // 統一以 Large 寬度載入，切換 view mode 時 pic.ImageFit=Contain 縮放顯示，不需重新載
        _ = LoadCoverInBackgroundAsync(fsi.FullName, MaxCardCoverWidth, img => card.SetCover(img));
        return card;
    }

    /// <summary>建立列表模式與共用右鍵選單使用的動作集合，確保與卡片模式行為 100% 一致。</summary>
    private WorkMenuActions CreateWorkMenuActions() => new()
    {
        OpenPrimary = p =>
        {
            EnqueueReadIfEnabled(p);
            if (Directory.Exists(p)) OpenFolderTab(p, false);
            else OpenForReading(p);
        },
        OpenExternal = p =>
        {
            EnqueueReadIfEnabled(p);
            if (WorkContextMenuHelper.CanUseBuiltInReader(p)) OpenWithExternalViewer(p);
            else OpenWithSystemDefault(p);
        },
        OpenNewTab = p =>
        {
            EnqueueReadIfEnabled(p);
            if (Directory.Exists(p)) OpenFolderTab(p, true);
            else OpenForReading(p);
        },
        OpenInExplorer = p => OpenInExplorer(p),
        RefreshCover = p => _ = RefreshWorkCoverAsync(p),
        ShowAuthorInfo = p => ShowAuthorInfo(p),
        ShowWorkDetail = p => ShowWorkDetail(p),
        ToggleVirtualLibrary = (p, libId) => ToggleWorkInVirtualLibrary(libId, p),
        CreateVirtualLibraryAndAdd = p => CreateVirtualLibraryAndAddWork(p),
        SetReadStatus = (p, status) => SetWorkReadStatus(p, status),
    };

    private void SetWorkReadStatus(string path, ReadStatus status)
    {
        _metaStore?.SetReadStatus(path, status);
        RefreshCardReadStatusByPath(path);
    }

    private IEnumerable<ComicCardControl> EnumerateAllCards()
    {
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            if (page.IsDisposed) continue;
            if (WorkContextMenuHelper.IsDashboardTag(page.Tag as string))
            {
                foreach (var flowName in new[] { "dbFlowRecentlyRead", "dbFlowRecentlyAdded" })
                {
                    var dbFlow = FindInTab<FlowLayoutPanel>(page, flowName);
                    if (dbFlow == null) continue;
                    foreach (var c in dbFlow.Controls.OfType<ComicCardControl>())
                        yield return c;
                }
                continue;
            }
            var flow = FindTabFlowLayout(page);
            if (flow == null) continue;
            foreach (var c in flow.Controls.OfType<ComicCardControl>())
                yield return c;
        }
    }

    /// <summary>掃所有 tab 的卡片（含 Dashboard）與列表緩存，刷 read status icon、進度條與排序過濾中繼資料。</summary>
    private void RefreshCardReadStatusByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        AssetGalleryStats? latestStats = null;
        if (_metaStore != null)
        {
            var statsMap = _metaStore.GetGalleryStatsByPaths(new[] { path });
            statsMap.TryGetValue(path, out latestStats);
        }

        foreach (var c in EnumerateAllCards())
        {
            if (!c.IsDisposed && string.Equals(c.ItemPath, path, StringComparison.OrdinalIgnoreCase))
            {
                c.RefreshReadStatusIcon();
                c.RefreshProgress();
                if (latestStats != null && c.Tag is GalleryItemSortMetadata meta)
                {
                    c.Tag = meta with
                    {
                        DisplayTitle = latestStats.Title ?? meta.DisplayTitle,
                        ReadStatus = latestStats.ReadStatus,
                        ReadCount = latestStats.ReadCount,
                        LastRead = latestStats.LastRead,
                    };
                }
            }
        }

        foreach (var kv in _tabFullListItems)
        {
            foreach (var row in kv.Value)
            {
                if (string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase) && latestStats != null && row.SortMeta != null)
                {
                    row.SortMeta = row.SortMeta with
                    {
                        DisplayTitle = latestStats.Title ?? row.SortMeta.DisplayTitle,
                        ReadStatus = latestStats.ReadStatus,
                        ReadCount = latestStats.ReadCount,
                        LastRead = latestStats.LastRead,
                    };
                }
            }
        }

        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            if (page.IsDisposed || WorkContextMenuHelper.IsDashboardTag(page.Tag as string)) continue;
            var state = GetOrCreateTabSortFilterState(page);
            bool needsReapply = state.ReadFilter != GalleryReadFilter.All
                || state.SortField is GallerySortField.LastRead or GallerySortField.ReadCount;
            if (!needsReapply) continue;

            bool containsPath = (_tabFullListItems.TryGetValue(page, out var list) && list.Any(r => string.Equals(r.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                || (FindTabFlowLayout(page)?.Controls.OfType<ComicCardControl>().Any(c => string.Equals(c.ItemPath, path, StringComparison.OrdinalIgnoreCase)) ?? false);
            if (containsPath)
            {
                ApplySortAndFilterToTab(page);
            }
        }
    }

    /// <summary>強制重新解碼指定路徑的封面並更新所有開啟分頁中的卡片與列表縮圖。</summary>
    private async Task RefreshWorkCoverAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsDisposed) return;
        ReportJobStarted("正在重新載入封面...");
        Image? freshImg = null;
        try
        {
            freshImg = await _coverManager.RefreshCoverAsync(path, MaxCardCoverWidth);
        }
        finally
        {
            ReportJobFinished();
        }

        if (IsDisposed)
        {
            freshImg?.Dispose();
            return;
        }

        if (freshImg == null)
        {
            var form = this.FindForm();
            if (form != null && !form.IsDisposed)
                AntdUI.Message.warn(form, "無法重新載入封面");
            return;
        }

        void ApplyUpdatedCover()
        {
            using (freshImg)
            {
                if (IsDisposed) return;
                foreach (var card in EnumerateAllCards())
                {
                    if (!card.IsDisposed && string.Equals(card.ItemPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        card.SetCover(new Bitmap(freshImg));
                    }
                }

                foreach (AntdUI.TabPage page in tabsContent.Pages)
                {
                    if (page.IsDisposed) continue;
                    var table = FindTabTable(page);
                    if (table == null || table.IsDisposed) continue;
                    if (table.DataSource is not System.ComponentModel.BindingList<GalleryListItem> list) continue;

                    bool updated = false;
                    foreach (var row in list)
                    {
                        if (!string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase)) continue;
                        var oldImg = row.Thumbnail?.Image;
                        var newCellImg = new AntdUI.CellImage(new Bitmap(freshImg))
                        {
                            Size = new Size(60, 80),
                            Radius = 4,
                            ImageFit = AntdUI.TFit.Cover,
                        };
                        row.Thumbnail = newCellImg;
                        if (oldImg != null && !ReferenceEquals(oldImg, newCellImg.Image))
                        {
                            oldImg.Dispose();
                        }
                        updated = true;
                    }
                    if (updated) table.Invalidate();
                }
            }
        }

        if (InvokeRequired)
        {
            try { BeginInvoke((Action)ApplyUpdatedCover); }
            catch { freshImg.Dispose(); }
        }
        else
        {
            ApplyUpdatedCover();
        }
    }

    /// <summary>切換作品在指定自訂虛擬閱讀清單的手動收錄狀態，並同步刷新對應分頁（若已開啟）。</summary>
    private void ToggleWorkInVirtualLibrary(long libraryId, string path)
    {
        if (_metaStore == null || string.IsNullOrWhiteSpace(path)) return;
        var existingIds = _metaStore.ListVirtualLibraryIdsForWork(path);
        bool currentlyInList = existingIds.Contains(libraryId);
        bool changed = currentlyInList
            ? _metaStore.RemoveWorkFromVirtualLibrary(libraryId, path)
            : _metaStore.AddWorkToVirtualLibrary(libraryId, path);

        var libName = _metaStore.GetVirtualLibrary(libraryId)?.Name ?? "閱讀清單";
        var form = this.FindForm();
        if (form != null && !form.IsDisposed && changed)
        {
            AntdUI.Message.success(form, currentlyInList
                ? $"已從「{libName}」移出"
                : $"已加入「{libName}」");
        }

        RefreshOpenTabsForVirtualLibrary(libraryId);
    }

    /// <summary>開啟建立自訂虛擬庫對話框（允許空白 DSL 建立手動清單），建立後立即加入此作品。</summary>
    private void CreateVirtualLibraryAndAddWork(string path)
    {
        if (_metaStore == null || string.IsNullOrWhiteSpace(path)) return;
        var form = this.FindForm();
        if (form == null || form.IsDisposed) return;

        using var dlg = new SaveVirtualLibraryDialog(_metaStore, initialDsl: null, allowEmptyDsl: true);
        if (dlg.ShowDialog(form) != DialogResult.OK) return;

        long newId = _metaStore.CreateVirtualLibrary(dlg.ResultName, dlg.ResultDsl, dlg.ResultIconSvg);
        bool added = _metaStore.AddWorkToVirtualLibrary(newId, path);

        if (_currentViewMode == TreeViewMode.MyLib)
        {
            _ = LoadTreeAsync();
        }

        AntdUI.Message.success(form, added
            ? $"已建立「{dlg.ResultName}」並加入作品"
            : $"已建立「{dlg.ResultName}」");
    }

    private void RefreshOpenTabsForVirtualLibrary(long libraryId)
    {
        foreach (AntdUI.TabPage page in tabsContent.Pages.Cast<AntdUI.TabPage>().ToList())
        {
            if (page.IsDisposed) continue;
            if (_tabVirtualLibraryIds.TryGetValue(page, out var tabLibId) && tabLibId == libraryId)
            {
                RefreshTab(page);
            }
        }
    }

    private int NextLoadGeneration(Control target)
        => _loadGenerations.AddOrUpdate(target, 1, (_, prev) => prev + 1);

    private void InvalidateLoadGeneration(Control? target)
    {
        if (target != null) _loadGenerations.AddOrUpdate(target, 1, (_, prev) => prev + 1);
    }

    private bool IsCurrentLoadGeneration(Control target, int generation)
        => !this.IsDisposed && !target.IsDisposed
           && _loadGenerations.TryGetValue(target, out var cur) && cur == generation;

    /// <summary>非 folder 模式的 gallery 載入 — 直接吃 path list（asset.path），不走 DirectoryInfo 列舉。</summary>
    private Task LoadGalleryFromPathsAsync(FlowLayoutPanel container, IReadOnlyList<string> paths, GalleryViewMode mode)
        => LoadGalleryFromPathsAsync(container, paths, () => mode, ownerPage: null);

    private Task LoadGalleryFromPathsAsync(
        FlowLayoutPanel container,
        IReadOnlyList<string> paths,
        Func<GalleryViewMode> getMode,
        AntdUI.TabPage? ownerPage = null)
        => LoadGalleryCardsCoreAsync(
            container,
            () => VirtualTabPathHelper.ResolveExistingItems(paths),
            getMode,
            showMetadata: true,
            ownerPage: ownerPage);

    private Task LoadGalleryCardsCoreAsync(
        FlowLayoutPanel container,
        Func<List<FileSystemInfo>> fetchItems,
        Func<GalleryViewMode> getMode,
        bool showMetadata,
        AntdUI.TabPage? ownerPage = null)
    {
        int generation = NextLoadGeneration(container);
        ReportJobStarted("正在載入項目與封面...");
        container.Controls.Clear();
        UpdateTabFilterCountLabel(ownerPage, 0, 0);

        AntdUI.Spin.open(container, async config =>
        {
            try
            {
                if (!IsCurrentLoadGeneration(container, generation)) return;
                var initialState = ownerPage != null ? GetOrCreateTabSortFilterState(ownerPage) : new TabSortFilterState();
                var prepared = await Task.Run(() =>
                {
                    var rawItems = fetchItems();
                    var statsMap = _metaStore?.GetGalleryStatsByPaths(rawItems.Select(i => i.FullName));
                    var list = new List<(FileSystemInfo Fs, GalleryItemSortMetadata Meta)>(rawItems.Count);
                    foreach (var fs in rawItems)
                    {
                        list.Add((fs, BuildSortMetadata(fs, statsMap)));
                    }
                    return GallerySortFilterHelper.SortItems(
                        list,
                        x => x.Meta,
                        initialState.SortField,
                        initialState.SortDescending);
                });
                if (!IsCurrentLoadGeneration(container, generation)) return;

                // 把 foreach 拆 chunks，批與批之間 await Task.Delay(1) 讓出 UI thread 給 Spin 動畫 paint。
                // 整體時間幾乎相同 (Add HWND 時間不變) 但使用者看得到 Spin 在轉 + cards 分批冒出有 progress 感
                const int ChunkSize = 8;
                for (int i = 0; i < prepared.Count; i += ChunkSize)
                {
                    if (!IsCurrentLoadGeneration(container, generation)) break;
                    var chunk = prepared.Skip(i).Take(ChunkSize).ToList();
                    try
                    {
                        this.Invoke(() =>
                        {
                            if (!IsCurrentLoadGeneration(container, generation)) return;
                            var mode = getMode();
                            var liveState = ownerPage != null ? GetOrCreateTabSortFilterState(ownerPage) : null;
                            container.SuspendLayout();
                            foreach (var entry in chunk)
                            {
                                var card = BuildCardForGallery(entry.Fs, mode, showMetadata, entry.Meta);
                                if (liveState != null)
                                {
                                    card.Visible = GallerySortFilterHelper.MatchesFilter(
                                        entry.Meta,
                                        liveState.ReadFilter,
                                        liveState.FilterKeyword);
                                }
                                container.Controls.Add(card);
                            }
                            container.ResumeLayout(true);   // 該 chunk 的 cards 立刻 layout + paint
                        });
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }
                    await Task.Delay(1);   // 讓 UI thread 喘口氣 paint Spin animation frame
                }

                if (ownerPage != null && IsCurrentLoadGeneration(container, generation))
                {
                    try
                    {
                        this.Invoke(() =>
                        {
                            if (IsCurrentLoadGeneration(container, generation))
                                ApplySortAndFilterToTab(ownerPage);
                        });
                    }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
            }
            finally
            {
                ReportJobFinished();
            }
        });

        return Task.CompletedTask;
    }

    private async Task LoadPreviewAsync(System.Windows.Forms.PictureBox pic, string path)
    {
        var img = await _coverManager.GetCoverAsync(path, 300);
        if (img == null || pic.IsDisposed) return;
        try { this.BeginInvoke(new Action(() => { if (!pic.IsDisposed) pic.Image = img; })); }
        catch { }
    }

    // ===== 滑鼠附近 floating preview popup =====
    private HoverPreviewForm? _previewForm;
    private System.Windows.Forms.PictureBox? _previewPic;

    private void EnsurePreviewPopup()
    {
        if (_previewForm != null) return;
        _previewForm = new HoverPreviewForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            TopMost = true,
            Size = new Size(240, 320),
            BackColor = Color.White,
        };
        _previewPic = new System.Windows.Forms.PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _previewForm.Controls.Add(_previewPic);
    }

    private async Task LoadPreviewPopupAsync(string path)
    {
        EnsurePreviewPopup();
        var img = await _coverManager.GetCoverAsync(path, 300);
        if (img == null || _previewForm == null || _previewPic == null) return;
        try
        {
            this.BeginInvoke(new Action(() =>
            {
                if (_previewPic != null && !_previewPic.IsDisposed)
                {
                    _previewPic.Image = img;
                    UpdatePreviewPopupPosition();
                    if (_previewForm != null && !_previewForm.Visible) _previewForm.Show();
                }
            }));
        }
        catch { }
    }

    private void UpdatePreviewPopupPosition()
    {
        if (_previewForm == null) return;
        var cur = Cursor.Position;
        var screen = Screen.FromPoint(cur).WorkingArea;
        int x = cur.X + 20;
        int y = cur.Y + 20;
        if (x + _previewForm.Width > screen.Right) x = cur.X - _previewForm.Width - 20;
        if (y + _previewForm.Height > screen.Bottom) y = screen.Bottom - _previewForm.Height - 5;
        _previewForm.Location = new Point(x, y);
    }

    private void HidePreviewPopup()
    {
        if (_previewForm != null && _previewForm.Visible) _previewForm.Hide();
    }

    private class HoverPreviewForm : Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }
    }

    /// <summary>
    /// 限制同時 decode + resize 的 task 數，避免 ThreadPool 把全部核心都塞滿、
    /// 多 task 互搶 GDI+ 與 memory bandwidth 把每張 wall-clock 從 30ms 膨脹到 200ms+。
    /// 設成 ProcessorCount/2 讓 UI thread 與其他 IO 還有餘裕；最少保留 2 個。
    /// </summary>
    private static readonly SemaphoreSlim _coverSemaphore =
        new(Math.Max(2, Environment.ProcessorCount / 2));

    /// <summary>
    /// 共用：背景抓 cover、抓到後在 UI thread 呼叫 onLoaded。
    /// 對 dispose 競態做防護。回傳 Task 給呼叫端等待完成（或 fire-and-forget 不接）。
    /// 透過 _coverSemaphore 限制平行度。
    /// </summary>
    private Task LoadCoverInBackgroundAsync(string path, int width, Action<Image> onLoaded)
    {
        return Task.Run(async () =>
        {
            await _coverSemaphore.WaitAsync();
            try
            {
                var img = await _coverManager.GetCoverAsync(path, width);
                if (img == null || this.IsDisposed) return;
                try { this.BeginInvoke(new Action(() => onLoaded(img))); }
                catch { /* form 已 dispose 等競態忽略 */ }
            }
            finally
            {
                _coverSemaphore.Release();
            }
        });
    }

    private Task LoadGalleryListAsync(AntdUI.Table table, string path, AntdUI.TabPage? ownerPage = null)
        => LoadGalleryListCoreAsync(
            table,
            () => Directory.Exists(path)
                ? new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .ToList()
                : new List<FileSystemInfo>(),
            ownerPage);

    private Task LoadGalleryListFromPathsAsync(AntdUI.Table table, IReadOnlyList<string> paths, AntdUI.TabPage? ownerPage = null)
        => LoadGalleryListCoreAsync(table, () => VirtualTabPathHelper.ResolveExistingItems(paths), ownerPage);

    private Task LoadGalleryListCoreAsync(
        AntdUI.Table table,
        Func<List<FileSystemInfo>> fetchItems,
        AntdUI.TabPage? ownerPage = null)
    {
        int generation = NextLoadGeneration(table);
        if (!table.IsDisposed) table.DataSource = null;
        if (ownerPage != null)
        {
            _tabFullListItems.Remove(ownerPage);
            UpdateTabFilterCountLabel(ownerPage, 0, 0);
        }

        ReportJobStarted("正在載入清單項目與封面...");
        AntdUI.Spin.open(this, async config =>
        {
            try
            {
                if (!IsCurrentLoadGeneration(table, generation)) return;

                // ===== Phase 1: 檔案掃描 + 批次 DB stats + 列表 binding (Spin 遮蔽期間) =====
                var prepared = await Task.Run(() =>
                {
                    var fsInfos = fetchItems();
                    var statsMap = _metaStore?.GetGalleryStatsByPaths(fsInfos.Select(i => i.FullName));

                    var result = new List<(FileSystemInfo fs, bool isDir, long size, DateTime modifiedAt, GalleryItemSortMetadata meta)>(fsInfos.Count);
                    foreach (var fs in fsInfos)
                    {
                        bool isDir = fs is DirectoryInfo;
                        long size = 0;
                        DateTime modifiedAt;
                        try
                        {
                            if (!isDir) size = ((FileInfo)fs).Length;
                            modifiedAt = fs.LastWriteTime;
                        }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        var meta = new GalleryItemSortMetadata(
                            fs.FullName,
                            fs.Name,
                            statsMap != null && statsMap.TryGetValue(fs.FullName, out var st) ? st.Title : null,
                            isDir,
                            size,
                            modifiedAt.Ticks,
                            statsMap != null && statsMap.TryGetValue(fs.FullName, out st) ? st.ReadStatus : ReadStatus.Unread,
                            statsMap != null && statsMap.TryGetValue(fs.FullName, out st) ? st.ReadCount : 0,
                            statsMap != null && statsMap.TryGetValue(fs.FullName, out st) ? st.LastRead : null);
                        result.Add((fs, isDir, size, modifiedAt, meta));
                    }
                    return result;
                });

                if (!IsCurrentLoadGeneration(table, generation)) return;

                var fullItems = new List<GalleryListItem>(prepared.Count);
                var thumbTasks = new List<(GalleryListItem Row, Task<AntdUI.CellImage?> Task)>(prepared.Count);

                foreach (var (fs, isDir, size, modifiedAt, meta) in prepared)
                {
                    var item = new GalleryListItem
                    {
                        Name = fs.Name,
                        Size = isDir ? "" : FormatSize(size),
                        CreatedAt = modifiedAt.ToString("yyyy-MM-dd HH:mm"),
                        IsFolder = isDir,
                        FullPath = fs.FullName,
                        SortMeta = meta,
                        Action = isDir
                            ? new AntdUI.CellLink[]
                            {
                                new AntdUI.CellButton("OpenFolder", "", AntdUI.TTypeMini.Default)
                                {
                                    Ghost = true,
                                    IconSvg = "RightOutlined",
                                }
                            }
                            : null,
                    };
                    fullItems.Add(item);

                    // 縮圖任務在背景解碼成 CellImage，統一回 UI 執行緒指派 Row.Thumbnail，避免跨緒觸發 PropertyChanged
                    var fullPath = item.FullPath;
                    var thumbTask = Task.Run<AntdUI.CellImage?>(async () =>
                    {
                        await _coverSemaphore.WaitAsync();
                        try
                        {
                            if (!IsCurrentLoadGeneration(table, generation)) return null;
                            var img = await _coverManager.GetCoverAsync(fullPath, 120);
                            if (img == null) return null;
                            return new AntdUI.CellImage(img)
                            {
                                Size = new Size(60, 80),
                                Radius = 4,
                                ImageFit = AntdUI.TFit.Cover,
                            };
                        }
                        finally
                        {
                            _coverSemaphore.Release();
                        }
                    });
                    thumbTasks.Add((item, thumbTask));
                }

                // 第一次 set：文字列表立即可見（縮圖欄空白），套用目前排序與就地過濾
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (!IsCurrentLoadGeneration(table, generation)) return;
                        if (ownerPage != null)
                        {
                            _tabFullListItems[ownerPage] = fullItems;
                            ApplySortAndFilterToTab(ownerPage);
                        }
                        else
                        {
                            table.DataSource = new System.ComponentModel.BindingList<GalleryListItem>(fullItems);
                        }
                    }));
                }
                catch { }

                // ===== Phase 2: 縮圖背景載入（Spin 已關閉、列表可見） =====
                if (thumbTasks.Count == 0) return;
                _ = Task.Run(async () =>
                {
                    await Task.WhenAll(thumbTasks.Select(t => t.Task));
                    if (!IsCurrentLoadGeneration(table, generation)) return;
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!IsCurrentLoadGeneration(table, generation)) return;
                            foreach (var (row, task) in thumbTasks)
                            {
                                if (task.IsCompletedSuccessfully && task.Result != null)
                                    row.Thumbnail = task.Result;
                            }
                            table.DataSource = null;
                            if (ownerPage != null)
                            {
                                ApplySortAndFilterToTab(ownerPage);
                            }
                            else
                            {
                                table.DataSource = new System.ComponentModel.BindingList<GalleryListItem>(fullItems);
                            }
                        }));
                    }
                    catch { /* form/table 已 dispose 等競態忽略 */ }
                });
            }
            finally
            {
                ReportJobFinished();
            }
        });

        return Task.CompletedTask;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    private Task LoadGalleryAsync(FlowLayoutPanel container, string path, GalleryViewMode mode)
        => LoadGalleryAsync(container, path, () => mode, ownerPage: null);

    private Task LoadGalleryAsync(
        FlowLayoutPanel container,
        string path,
        Func<GalleryViewMode> getMode,
        AntdUI.TabPage? ownerPage = null)
        => LoadGalleryCardsCoreAsync(
            container,
            () => Directory.Exists(path)
                ? new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .ToList()
                : new List<FileSystemInfo>(),
            getMode,
            showMetadata: false,
            ownerPage: ownerPage);

    private void ApplySortAndFilterToTab(AntdUI.TabPage? page)
    {
        if (page == null || page.IsDisposed || WorkContextMenuHelper.IsDashboardTag(page.Tag as string)) return;
        var state = GetOrCreateTabSortFilterState(page);
        var mode = GetTabMode(page);

        if (mode == GalleryViewMode.List)
        {
            var table = FindTabTable(page);
            if (table == null || table.IsDisposed) return;
            if (!_tabFullListItems.TryGetValue(page, out var fullItems)) return;

            var filtered = fullItems
                .Where(item => GallerySortFilterHelper.MatchesFilter(
                    item.SortMeta ?? BuildListItemFallbackMeta(item),
                    state.ReadFilter,
                    state.FilterKeyword))
                .ToList();
            var sorted = GallerySortFilterHelper.SortItems(
                filtered,
                item => item.SortMeta ?? BuildListItemFallbackMeta(item),
                state.SortField,
                state.SortDescending);
            table.DataSource = new System.ComponentModel.BindingList<GalleryListItem>(sorted);
            UpdateTabFilterCountLabel(page, sorted.Count, fullItems.Count);
            return;
        }

        var flow = FindTabFlowLayout(page);
        if (flow == null || flow.IsDisposed) return;
        var cards = flow.Controls.OfType<ComicCardControl>().ToList();
        var sortedCards = GallerySortFilterHelper.SortItems(
            cards,
            GetCardSortMeta,
            state.SortField,
            state.SortDescending);

        int visibleCount = 0;
        flow.SuspendLayout();
        try
        {
            for (int i = 0; i < sortedCards.Count; i++)
            {
                var card = sortedCards[i];
                bool matches = GallerySortFilterHelper.MatchesFilter(
                    GetCardSortMeta(card),
                    state.ReadFilter,
                    state.FilterKeyword);
                if (card.Visible != matches) card.Visible = matches;
                if (matches) visibleCount++;
                if (flow.Controls.GetChildIndex(card, false) != i)
                    flow.Controls.SetChildIndex(card, i);
            }
        }
        finally
        {
            flow.ResumeLayout(true);
        }
        UpdateTabFilterCountLabel(page, visibleCount, cards.Count);
    }

    private void UpdateSortDirectionButton(AntdUI.Button btn, bool descending)
    {
        if (btn.IsDisposed) return;
        btn.IconSvg = descending ? "SortDescendingOutlined" : "SortAscendingOutlined";
        _btnTooltip.SetTip(btn, descending ? "降冪排序" : "升冪排序");
    }

    private static void SyncTableSortHeader(AntdUI.Table? table, TabSortFilterState state)
    {
        if (table == null || table.IsDisposed || table.Columns == null) return;
        string? activeKey = state.SortField switch
        {
            GallerySortField.Name => "Name",
            GallerySortField.Size => "Size",
            GallerySortField.ModifiedTime => "CreatedAt",
            _ => null
        };
        var activeMode = state.SortDescending ? AntdUI.SortMode.DESC : AntdUI.SortMode.ASC;
        foreach (var col in table.Columns)
        {
            if (!col.SortOrder) continue;
            col.SortMode = string.Equals(col.Key, activeKey, StringComparison.Ordinal)
                ? activeMode
                : AntdUI.SortMode.NONE;
        }
        table.Invalidate();
    }

    private static void UpdateTabFilterCountLabel(AntdUI.TabPage? page, int visibleCount, int totalCount)
    {
        if (page == null || page.IsDisposed) return;
        var lbl = page.Controls.Find("tabLblFilterCount", true).OfType<System.Windows.Forms.Label>().FirstOrDefault();
        if (lbl == null || lbl.IsDisposed) return;
        lbl.Text = totalCount == 0
            ? "0 項"
            : (visibleCount == totalCount ? $"共 {totalCount} 項" : $"顯示 {visibleCount} / {totalCount} 項");
    }

    private GalleryItemSortMetadata BuildSortMetadata(
        FileSystemInfo fs,
        IReadOnlyDictionary<string, AssetGalleryStats>? statsMap)
    {
        bool isDir = fs is DirectoryInfo;
        long size = 0;
        long modifiedTicks = 0;
        try
        {
            if (!isDir) size = ((FileInfo)fs).Length;
            modifiedTicks = fs.LastWriteTime.Ticks;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        AssetGalleryStats? st = null;
        statsMap?.TryGetValue(fs.FullName, out st);
        return new GalleryItemSortMetadata(
            fs.FullName,
            fs.Name,
            st?.Title,
            isDir,
            size,
            modifiedTicks,
            st?.ReadStatus ?? ReadStatus.Unread,
            st?.ReadCount ?? 0,
            st?.LastRead);
    }

    private GalleryItemSortMetadata GetCardSortMeta(ComicCardControl card)
    {
        if (card.Tag is GalleryItemSortMetadata meta) return meta;
        var name = string.IsNullOrEmpty(card.ItemPath) ? "" : Path.GetFileName(card.ItemPath);
        var fallback = new GalleryItemSortMetadata(
            card.ItemPath ?? "",
            name,
            null,
            Directory.Exists(card.ItemPath),
            0,
            0,
            ReadStatus.Unread,
            0,
            null);
        card.Tag = fallback;
        return fallback;
    }

    private static GalleryItemSortMetadata BuildListItemFallbackMeta(GalleryListItem item)
        => new(
            item.FullPath,
            item.Name,
            null,
            item.IsFolder,
            0,
            0,
            ReadStatus.Unread,
            0,
            null);

    /// <summary>
    /// 「在檔案總管中開啟」共用邏輯：
    /// 目錄 → explorer 直接開該目錄、看裡面內容
    /// 檔案 → explorer /select 開父目錄 + highlight 該檔
    /// </summary>
    private static void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
        catch { /* 忽略無法開啟的狀況 */ }
    }

    /// <summary>
    /// 統一的「開啟閱讀」入口：依 UserPreferences.UseBuiltInReader 決定走內建 reader 或外部 viewer。
    /// 內建 reader 支援 folder / archive / 單檔圖片；單檔圖片會走 FolderReaderSource 自動 enumerate 同層。
    /// </summary>
    private void OpenForReading(string path)
    {
        if (_prefs?.UseBuiltInReader == true && WorkContextMenuHelper.CanUseBuiltInReader(path))
        {
            _ = OpenInBuiltInReaderAsync(path);
            return;
        }
        OpenWithExternalViewer(path);
    }

    private async Task OpenInBuiltInReaderAsync(
        string path,
        ReaderForm? replacingForm = null,
        IReadOnlyList<string>? explicitSiblings = null)
    {
        string pathKey = DelayedLoadingTracker.NormalizePathKey(path);
        if (string.IsNullOrEmpty(pathKey))
        {
            OpenWithExternalViewer(path);
            return;
        }

        if (_openReadersByPath.TryGetValue(pathKey, out var existingForm))
        {
            if (!existingForm.IsDisposed)
            {
                if (existingForm.WindowState == FormWindowState.Minimized)
                    existingForm.WindowState = FormWindowState.Normal;
                existingForm.Activate();
                if (replacingForm != null && !replacingForm.IsDisposed && !ReferenceEquals(existingForm, replacingForm))
                {
                    replacingForm.SkipClosingPrefsSave = true;
                    replacingForm.Close();
                }
                return;
            }
            _openReadersByPath.Remove(pathKey);
        }

        if (!_openingReaderPaths.Add(pathKey)) return;

        // 僅在活躍開書數從 0 → 1 時啟動 380ms 延遲計時器，避免併發點擊干擾既有的載入指示
        if (_openingReaderPaths.Count == 1)
        {
            BeginReaderLoadingIndicator();
        }

        // 進背景執行緒前先擷取按鍵修飾符、偏好/設定與同分頁作品清單
        bool useSkiaProbe = Control.ModifierKeys.HasFlag(Keys.Shift);
        bool rtl = (_prefs?.DefaultReadingDirection ?? "rtl") == "rtl";
        bool spreadFromCover = _prefs?.DualPageSpreadFromCover ?? false;
        int thrSec = _config.ReadCountThresholdSeconds;
        int thrPages = _config.ReadCountThresholdPages;
        bool track = _prefs?.TrackReadingRecord ?? true;
        var siblingCandidates = explicitSiblings ?? GetCurrentTabSiblingWorkPaths();

        IReaderSource? source = null;
        try
        {
            source = await Task.Run(() => CreateValidatedReaderSource(path));
            if (IsDisposed)
            {
                source.Dispose();
                source = null;
                return;
            }

            FinishOpeningReaderPath(pathKey);

            // [PoC 臨時] 按住 Shift 開啟 → 用 Skia/GPU 探測視窗
            if (useSkiaProbe)
            {
                new SkiaReaderProbeForm(source).Show();
                source = null;
                return;
            }

            var form = new ReaderForm(
                source,
                rtl,
                spreadFromCover,
                _metaStore,
                _readRecordQueue,
                thrSec,
                thrPages,
                track,
                _prefs,
                onSavePrefs: () => RequestSavePreferences?.Invoke(),
                siblingPaths: siblingCandidates,
                onSwitchWorkAsync: (nextPath, oldForm) =>
                    OpenInBuiltInReaderAsync(nextPath, replacingForm: oldForm, explicitSiblings: siblingCandidates));
            source = null;
            if (replacingForm != null && !replacingForm.IsDisposed)
            {
                replacingForm.CopyWindowPlacementTo(form);
            }
            _openReadersByPath[pathKey] = form;
            form.FormClosed += (_, _) =>
            {
                if (_openReadersByPath.TryGetValue(pathKey, out var current) && ReferenceEquals(current, form))
                    _openReadersByPath.Remove(pathKey);
            };
            form.Show();   // modeless，不阻擋 main form
            if (replacingForm != null && !replacingForm.IsDisposed && !ReferenceEquals(replacingForm, form))
            {
                replacingForm.SkipClosingPrefsSave = true;
                replacingForm.Close();
            }
        }
        catch
        {
            source?.Dispose();
            FinishOpeningReaderPath(pathKey);
            if (!IsDisposed)
            {
                // 內建 reader 失敗（含空圖集）fallback 到外部 viewer，避免使用者點了沒反應
                OpenWithExternalViewer(path);
            }
        }
        finally
        {
            FinishOpeningReaderPath(pathKey);
        }
    }

    private IReadOnlyList<string>? GetCurrentTabSiblingWorkPaths()
    {
        var page = tabsContent.SelectedTab;
        if (page == null || page.IsDisposed) return null;

        if (WorkContextMenuHelper.IsDashboardTag(page.Tag as string))
        {
            var result = new List<string>();
            foreach (var flowName in new[] { "dbFlowRecentlyRead", "dbFlowRecentlyAdded" })
            {
                var dbFlow = FindInTab<FlowLayoutPanel>(page, flowName);
                if (dbFlow == null || dbFlow.IsDisposed) continue;
                foreach (var c in dbFlow.Controls.OfType<ComicCardControl>())
                {
                    if (!c.IsDisposed && c.Visible && !string.IsNullOrEmpty(c.ItemPath)
                        && WorkContextMenuHelper.CanUseBuiltInReader(c.ItemPath))
                    {
                        result.Add(c.ItemPath);
                    }
                }
            }
            return result.Count > 0 ? result : null;
        }

        if (GetTabMode(page) == GalleryViewMode.List)
        {
            var table = FindTabTable(page);
            if (table?.DataSource is IEnumerable<GalleryListItem> rows)
            {
                var list = rows
                    .Where(r => !string.IsNullOrEmpty(r.FullPath) && WorkContextMenuHelper.CanUseBuiltInReader(r.FullPath))
                    .Select(r => r.FullPath)
                    .ToList();
                return list.Count > 0 ? list : null;
            }
            return null;
        }

        var flow = FindTabFlowLayout(page);
        if (flow == null || flow.IsDisposed) return null;
        var cardPaths = flow.Controls.OfType<ComicCardControl>()
            .Where(c => !c.IsDisposed && c.Visible && !string.IsNullOrEmpty(c.ItemPath)
                        && WorkContextMenuHelper.CanUseBuiltInReader(c.ItemPath))
            .Select(c => c.ItemPath)
            .ToList();
        return cardPaths.Count > 0 ? cardPaths : null;
    }

    private void FinishOpeningReaderPath(string pathKey)
    {
        if (_openingReaderPaths.Remove(pathKey) && _openingReaderPaths.Count == 0)
        {
            CompleteReaderLoadingIndicator(_readerOpenToken);
        }
    }

    private static IReaderSource CreateValidatedReaderSource(string path)
    {
        IReaderSource source;
        var ext = Path.GetExtension(path);
        if (SupportedExtensions.Archive.Contains(ext) && File.Exists(path))
            source = new ArchiveReaderSource(path);
        else if (SupportedExtensions.Pdf.Contains(ext) && File.Exists(path))
            source = new PdfReaderSource(path);
        else
            source = new FolderReaderSource(path);

        // 沒圖直接 fallback（避免空白 reader）
        if (source.PageCount == 0)
        {
            source.Dispose();
            throw new InvalidOperationException("No images found");
        }
        return source;
    }

    /// <summary>card 點擊 → 排隊閱讀紀錄；TrackReadingRecord=false 時 no-op。</summary>
    private void EnqueueReadIfEnabled(string path)
    {
        if (_prefs?.TrackReadingRecord == false) return;
        _readRecordQueue?.EnqueueRead(path);
    }

    /// <summary>非漫畫格式（PDF/EPUB/docx 等）走系統預設程式 — 不經過 ImageViewerPath。</summary>
    private void OpenWithSystemDefault(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "開啟失敗: " + ex.Message);
        }
    }

    private void OpenWithExternalViewer(string filePath)
    {
        try
        {
            // Honeyview 對「資料夾路徑」只會把它當單一 item 開（看到第一張就停）。
            // 改傳該資料夾內第一張圖 → viewer 載入後使用者用 PgDn/滾輪切換同目錄其它圖。
            string targetPath = filePath;
            if (Directory.Exists(filePath))
            {
                var firstImage = Directory.EnumerateFiles(filePath)
                    .Where(f => SupportedExtensions.Image.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (firstImage != null) targetPath = firstImage;
            }

            if (!string.IsNullOrEmpty(_config.ImageViewerPath) && File.Exists(_config.ImageViewerPath))
            {
                string template = string.IsNullOrWhiteSpace(_config.ImageViewerArgs) ? "\"{path}\"" : _config.ImageViewerArgs;
                string args = template.Replace("{path}", targetPath);
                System.Diagnostics.Process.Start(_config.ImageViewerPath, args);
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "開啟失敗: " + ex.Message);
        }
    }
}

/// <summary>
/// 虛擬分頁路徑清單過濾與解析 helper（卡片與清單模式共用，無 UI 依賴以利單元測試）。
/// </summary>
public static class VirtualTabPathHelper
{
    /// <summary>
    /// 過濾虛擬路徑清單：
    /// 1. 略過空白路徑並依大小寫不敏感去重（含結尾分隔符號正規化）。
    /// 2. 排除「所在目錄也同時在結果集中」的單檔（避免目錄與內頁圖檔同時出現）。
    /// </summary>
    public static List<string> FilterVirtualPaths(IReadOnlyList<string> paths, Func<string, bool>? directoryExists = null)
    {
        if (paths.Count == 0) return new List<string>();
        var isDir = directoryExists ?? Directory.Exists;
        var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            pathSet.Add(Path.TrimEndingDirectorySeparator(p));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filtered = new List<string>(paths.Count);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var normalized = Path.TrimEndingDirectorySeparator(p);
            if (!seen.Add(normalized)) continue;
            if (!IsPageInsideFolderResult(normalized, pathSet, isDir))
            {
                filtered.Add(p);
            }
        }
        return filtered;
    }

    /// <summary>
    /// 檢查 path 是否為「某個也在結果集裡的目錄」之內頁（檔案）。
    /// 目錄永遠保留；檔案若父目錄在 pathSet 中則視為內頁過濾掉。
    /// </summary>
    public static bool IsPageInsideFolderResult(string path, HashSet<string> pathSet, Func<string, bool>? directoryExists = null)
    {
        var isDir = directoryExists ?? Directory.Exists;
        if (isDir(path)) return false;
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) return false;
        return pathSet.Contains(Path.TrimEndingDirectorySeparator(parent));
    }

    /// <summary>
    /// 過濾虛擬路徑並解析為實際存在的 FileSystemInfo 清單（失效／不存在路徑自動略過）。
    /// </summary>
    public static List<FileSystemInfo> ResolveExistingItems(IReadOnlyList<string> paths)
    {
        var filtered = FilterVirtualPaths(paths);
        var items = new List<FileSystemInfo>(filtered.Count);
        foreach (var p in filtered)
        {
            FileSystemInfo? fsi = Directory.Exists(p)
                ? new DirectoryInfo(p)
                : (File.Exists(p) ? new FileInfo(p) : null);
            if (fsi != null) items.Add(fsi);
        }
        return items;
    }
}

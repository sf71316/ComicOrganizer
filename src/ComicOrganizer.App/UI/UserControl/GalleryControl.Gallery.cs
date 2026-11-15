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
    private ComicCardControl BuildCardForGallery(FileSystemInfo fsi, GalleryViewMode mode, bool showMetadata)
    {
        bool isFolderNode = fsi is DirectoryInfo;
        var card = new ComicCardControl();
        card.SetData(fsi, isFolderNode);

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
        card.RefreshProgress();

        var c = card;   // 給 lambda capture 用，避免 foreach var closure 疑慮
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
        card.RequestAuthorInfo += p => ShowAuthorInfo(p);
        card.RequestWorkDetail += p => ShowWorkDetail(p);
        // 強制走外部開啟（不看 UseBuiltInReader 偏好）：漫畫格式 → 外部 viewer；其他 → 系統預設
        card.RequestOpenWithExternal += p =>
        {
            EnqueueReadIfEnabled(p);
            if (CanUseBuiltInReader(p)) OpenWithExternalViewer(p);
            else OpenWithSystemDefault(p);
        };
        card.RequestSetReadStatus += (p, status) =>
        {
            _metaStore?.SetReadStatus(p, status);
            c.RefreshReadStatusIcon();
            c.RefreshProgress();
        };

        // 目錄與檔案都試著載封面：CoverManager 對目錄會遞迴往下找首張圖檔（受 MaxScanDepth 限制）
        // 統一以 Large 寬度載入，切換 view mode 時 pic.ImageFit=Contain 縮放顯示，不需重新載
        _ = LoadCoverInBackgroundAsync(fsi.FullName, MaxCardCoverWidth, img => card.SetCover(img));
        return card;
    }

    /// <summary>掃所有 tab 的卡片，找 ItemPath 相符者刷 read status icon 與進度條。</summary>
    private void RefreshCardReadStatusByPath(string path)
    {
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            var flow = FindTabFlowLayout(page);
            if (flow == null) continue;
            foreach (var c in flow.Controls.OfType<ComicCardControl>())
            {
                if (string.Equals(c.ItemPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    c.RefreshReadStatusIcon();
                    c.RefreshProgress();
                }
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
        => LoadGalleryFromPathsAsync(container, paths, () => mode);

    private Task LoadGalleryFromPathsAsync(FlowLayoutPanel container, IReadOnlyList<string> paths, Func<GalleryViewMode> getMode)
        => LoadGalleryCardsCoreAsync(
            container,
            () => VirtualTabPathHelper.ResolveExistingItems(paths),
            getMode,
            showMetadata: true);

    private Task LoadGalleryCardsCoreAsync(
        FlowLayoutPanel container,
        Func<List<FileSystemInfo>> fetchItems,
        Func<GalleryViewMode> getMode,
        bool showMetadata)
    {
        int generation = NextLoadGeneration(container);
        ReportJobStarted("正在載入項目與封面...");
        container.Controls.Clear();

        AntdUI.Spin.open(container, async config =>
        {
            try
            {
                if (!IsCurrentLoadGeneration(container, generation)) return;
                var items = await Task.Run(fetchItems);
                if (!IsCurrentLoadGeneration(container, generation)) return;

                // 把 foreach 拆 chunks，批與批之間 await Task.Delay(1) 讓出 UI thread 給 Spin 動畫 paint。
                // 整體時間幾乎相同 (Add HWND 時間不變) 但使用者看得到 Spin 在轉 + cards 分批冒出有 progress 感
                const int ChunkSize = 8;
                for (int i = 0; i < items.Count; i += ChunkSize)
                {
                    if (!IsCurrentLoadGeneration(container, generation)) break;
                    var chunk = items.Skip(i).Take(ChunkSize).ToList();
                    try
                    {
                        this.Invoke(() =>
                        {
                            if (!IsCurrentLoadGeneration(container, generation)) return;
                            var mode = getMode();
                            container.SuspendLayout();
                            foreach (var item in chunk)
                            {
                                container.Controls.Add(BuildCardForGallery(item, mode, showMetadata));
                            }
                            container.ResumeLayout(true);   // 該 chunk 的 cards 立刻 layout + paint
                        });
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }
                    await Task.Delay(1);   // 讓 UI thread 喘口氣 paint Spin animation frame
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

    private Task LoadGalleryListAsync(AntdUI.Table table, string path)
        => LoadGalleryListCoreAsync(table, () =>
            Directory.Exists(path)
                ? new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .ToList()
                : new List<FileSystemInfo>());

    private Task LoadGalleryListFromPathsAsync(AntdUI.Table table, IReadOnlyList<string> paths)
        => LoadGalleryListCoreAsync(table, () => VirtualTabPathHelper.ResolveExistingItems(paths));

    private Task LoadGalleryListCoreAsync(AntdUI.Table table, Func<List<FileSystemInfo>> fetchItems)
    {
        int generation = NextLoadGeneration(table);
        if (!table.IsDisposed) table.DataSource = null;

        ReportJobStarted("正在載入清單項目與封面...");
        AntdUI.Spin.open(this, async config =>
        {
            try
            {
                if (!IsCurrentLoadGeneration(table, generation)) return;

                // ===== Phase 1: 檔案掃描 + 排序 + 列表 binding (Spin 遮蔽期間) =====
                var prepared = await Task.Run(() =>
                {
                    var fsInfos = fetchItems()
                        .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(i => i.FullName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var result = new List<(FileSystemInfo fs, bool isDir, long size, DateTime createdAt)>(fsInfos.Count);
                    foreach (var fs in fsInfos)
                    {
                        bool isDir = fs is DirectoryInfo;
                        long size = 0;
                        DateTime createdAt;
                        try
                        {
                            if (!isDir) size = ((FileInfo)fs).Length;
                            createdAt = fs.CreationTime;
                        }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        result.Add((fs, isDir, size, createdAt));
                    }
                    return result;
                });

                if (!IsCurrentLoadGeneration(table, generation)) return;

                var list = new System.ComponentModel.BindingList<GalleryListItem>();
                var thumbTasks = new List<Task>();

                foreach (var (fs, isDir, size, createdAt) in prepared)
                {
                    var item = new GalleryListItem
                    {
                        Name = fs.Name,
                        Size = isDir ? "" : FormatSize(size),
                        CreatedAt = createdAt.ToString("yyyy-MM-dd HH:mm"),
                        IsFolder = isDir,
                        FullPath = fs.FullName,
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
                    list.Add(item);

                    // 縮圖任務 fire-and-forget — 透過 Semaphore 控管並行數
                    var capturedItem = item;
                    thumbTasks.Add(Task.Run(async () =>
                    {
                        await _coverSemaphore.WaitAsync();
                        try
                        {
                            if (!IsCurrentLoadGeneration(table, generation)) return;
                            var img = await _coverManager.GetCoverAsync(capturedItem.FullPath, 120);
                            if (img != null) capturedItem.Thumbnail = new AntdUI.CellImage(img)
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
                    }));
                }

                // 第一次 set：文字列表立即可見（縮圖欄空白）
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (IsCurrentLoadGeneration(table, generation))
                            table.DataSource = list;
                    }));
                }
                catch { }

                // ===== Phase 2: 縮圖背景載入（Spin 已關閉、列表可見） =====
                if (thumbTasks.Count == 0) return;
                _ = Task.Run(async () =>
                {
                    await Task.WhenAll(thumbTasks);
                    if (!IsCurrentLoadGeneration(table, generation)) return;
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!IsCurrentLoadGeneration(table, generation)) return;
                            table.DataSource = null;
                            table.DataSource = list;
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
        => LoadGalleryAsync(container, path, () => mode);

    private Task LoadGalleryAsync(FlowLayoutPanel container, string path, Func<GalleryViewMode> getMode)
        => LoadGalleryCardsCoreAsync(
            container,
            () => Directory.Exists(path)
                ? new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .OrderBy(i => i is DirectoryInfo ? 0 : 1)
                    .ThenBy(i => i.Name)
                    .ToList()
                : new List<FileSystemInfo>(),
            getMode,
            showMetadata: false);

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
        if (_prefs?.UseBuiltInReader == true && CanUseBuiltInReader(path))
        {
            try { OpenInBuiltInReader(path); return; }
            catch
            {
                // 內建 reader 失敗 fallback 到外部 viewer，避免使用者點了沒反應
            }
        }
        OpenWithExternalViewer(path);
    }

    private static bool CanUseBuiltInReader(string path)
    {
        if (Directory.Exists(path)) return true;   // FolderReaderSource 會檢查內含圖
        if (!File.Exists(path)) return false;
        var ext = Path.GetExtension(path);
        if (SupportedExtensions.Archive.Contains(ext)) return true;
        if (SupportedExtensions.Pdf.Contains(ext)) return true;
        // 單檔圖檔也支援 — FolderReaderSource 會 enumerate 同層 (Honeyview 行為)
        return SupportedExtensions.Image.Contains(ext);
    }

    private void OpenInBuiltInReader(string path)
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

        // [PoC 臨時] 按住 Shift 開啟 → 用 Skia/GPU 探測視窗（項目 4 再正式做選單入口）
        if (Control.ModifierKeys.HasFlag(Keys.Shift))
        {
            new SkiaReaderProbeForm(source).Show();
            return;
        }

        bool rtl = (_prefs?.DefaultReadingDirection ?? "rtl") == "rtl";
        bool spreadFromCover = _prefs?.DualPageSpreadFromCover ?? false;
        int thrSec = _config.ReadCountThresholdSeconds;
        int thrPages = _config.ReadCountThresholdPages;
        bool track = _prefs?.TrackReadingRecord ?? true;
        var form = new ReaderForm(source, rtl, spreadFromCover, _metaStore,
                                  _readRecordQueue, thrSec, thrPages, track);
        form.Show();   // modeless，不阻擋 main form
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

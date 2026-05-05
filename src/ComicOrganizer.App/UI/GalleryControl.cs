using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;

namespace ComicOrganizer.App.UI;

public partial class GalleryControl : UserControl, IThemedControl
{
    public enum GalleryViewMode { Small, Medium, Large, List }

    private AppConfig _config = null!;
    private CoverManager _coverManager = null!;
    private IAssetMetaStore? _metaStore;   // 可選 — null 時 card 走原本 filename 顯示
    private ReadRecordQueue? _readRecordQueue;   // 延遲提交閱讀紀錄（30s 門檻），#49 Phase 1.0
    private UserPreferences? _prefs;   // reader / 閱讀方向偏好，#49

    private const string LazyPlaceholderTag = "__LAZY__";

    // 區分 tree 右鍵點在節點 vs 空白：NodeMouseClick 時設 true，MouseUp 看到 true 就跳過 blank menu
    private bool _treeRightOnNode;

    // 卡片封面統一以 Large card 寬度載入（= ComicCardControl Large mode 的 cardSize.Width）。
    // 切換 S/M/L 時 pic.ImageFit=Contain 自動等比縮小顯示，不需重新載圖。
    // 代價：每張 bitmap 多 ~250KB，100 張多 ~25MB，可接受。
    private const int MaxCardCoverWidth = 330;

    // Gallery 顯示的檔案副檔名 (目錄一律顯示)：圖片 + 壓縮檔 + pdf
    private static readonly HashSet<string> GalleryAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
        ".zip", ".cbz",
        ".pdf",
    };

    private static bool ShouldShowInGallery(FileSystemInfo fi)
    {
        if ((fi.Attributes & FileAttributes.Hidden) != 0) return false;
        if (fi is DirectoryInfo) return true;
        return GalleryAllowedExtensions.Contains(fi.Extension);
    }

    /// <summary>
    /// 每個 tab 自己的 view mode（per-tab 獨立）。
    /// 新 tab 預設 Medium；tab close 時 entry 會清掉避免 dispose 後 reference 殘留。
    /// </summary>
    private readonly Dictionary<AntdUI.TabPage, GalleryViewMode> _tabViewModes = new();

    /// <summary>
    /// 共用 TooltipComponent — attach 到動態建的 button (例：每個 tab 的 btnRefresh)，
    /// 跟主視窗生命週期一致；ArrowAlign 跟 Segmented TooltipConfig 一致 (Bottom)
    /// </summary>
    private readonly AntdUI.TooltipComponent _btnTooltip = new() { ArrowAlign = AntdUI.TAlign.Bottom };

    // 純相框 SVG（移除 <text> — AntdUI SVG renderer 不渲染 text 元素，字母改用 SegmentedItem.Text 顯示）

    private GalleryViewMode GetTabMode(AntdUI.TabPage? page)
    {
        if (page == null) return GalleryViewMode.Medium;
        return _tabViewModes.TryGetValue(page, out var mode) ? mode : GalleryViewMode.Medium;
    }

    /// <summary>當前開著的 Drawer Form ref；ShowAuthorInfo / ShowWorkDetail 時 open，CloseRequested 時 Close。</summary>
    private Form? _detailDrawerForm;

    /// <summary>當前 drawer 的點擊外部偵測 filter（drawer 開時 Add，關時 Remove）。</summary>
    private DrawerOutsideClickFilter? _drawerClickFilter;

    public GalleryControl()
    {
        InitializeComponent();

        // GalleryControl 自己開 DoubleBuffer：tabsContent 切換 / resize 時減抖
        DoubleBuffered = true;

        WireDrawerEvents();

        // 設定內容頁籤樣式
        if (tabsContent.Style is AntdUI.Tabs.StyleCard style)
        {
            style.Closable = true;
        }
        tabsContent.CloseDisposePage = true;
        tabsContent.FillActive = Core.ThemeColors.Border;

        this.WireThemeAndLocalization();

        // Tree 點擊處理：
        //  右鍵 → 開啟 context menu（另開頁籤 / 重新整理）
        //  中鍵 → 直接開新頁籤（取代原本的雙擊行為）
        //  右鍵節點 → 三項選單 (檔案總管 / 目前頁籤 / 另開頁籤)
        //  右鍵空白 → 一項選單 (重新整理)；用 _treeRightOnNode 旗標區分
        treeFolders.NodeMouseClick += (s, e) =>
        {
            if (e.Item.Tag is not string path) return;

            if (e.Button == MouseButtons.Middle)
            {
                OpenFolderTab(path, true);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                _treeRightOnNode = true;
                ShowTreeNodeContextMenu(path);
            }
        };

        // 空白處右鍵 → 重新整理 (整棵 tree 重新載入根目錄)
        treeFolders.MouseUp += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            if (_treeRightOnNode)
            {
                _treeRightOnNode = false;
                return;
            }
            ShowTreeBlankContextMenu();
        };

        // 處理頁籤關閉事件 — 清掉 _tabViewModes 內對應的 entry 避免 dispose 後 reference 殘留
        tabsContent.ClosingPage += (s, e) =>
        {
            if (e.Value != null) _tabViewModes.Remove(e.Value);
            return true;
        };

        // 每個 Tab 自帶 breadcrumb，內容在 OpenFolderTab 建立 / 更新當前 Tab 時填好，
        // 切 Tab 不需重填，因此不綁 SelectedIndexChanged。
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        // 更新 Tree 中的 "Loading..." 節點文字（如果存在）
        UpdateTreeLoadingText(treeFolders.Items);

        // 更新所有已開 Tab 的 Segmented Tooltip + btnRefresh Tooltip（語言切換時同步）
        string[] tipKeys = { "ViewSmall", "ViewMedium", "ViewLarge", "ViewList" };
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            var seg = page.Controls.Find("tabViewModeSeg", true).OfType<AntdUI.Segmented>().FirstOrDefault();
            if (seg != null)
            {
                for (int i = 0; i < seg.Items.Count && i < tipKeys.Length; i++)
                {
                    seg.Items[i].Tooltip = LocalizationManager.GetString(tipKeys[i]);
                }
            }
            foreach (var btn in page.Controls.Find("tabBtnRefresh", true).OfType<AntdUI.Button>())
            {
                _btnTooltip.SetTip(btn, LocalizationManager.GetString("MenuRefresh"));
            }
        }
    }

    public void ApplyThemeColors()
    {
        splitter2.Panel1.BackColor = Core.ThemeColors.Border;
        splitter2.Panel2.BackColor = Core.ThemeColors.Border;
        treeFolders.BackColor = Core.ThemeColors.TreeViewSurface;
        // tabsContent 自己的 BackColor 預設繼承父 (Panel2=Border)，沒開 tab 時整片露 Border 色 → 強制設 Surface
        tabsContent.BackColor = Core.ThemeColors.Surface;
        tabsContent.ScrollBack = Core.ThemeColors.Text;
        tabsContent.FillActive = Core.ThemeColors.Border;

        // 動態建立的 Tab 內 panel 也要重套
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            page.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabContentHost", true).OfType<System.Windows.Forms.Panel>())
                p.BackColor = Core.ThemeColors.Surface;
            foreach (var p in page.Controls.Find("tabHeader", true).OfType<AntdUI.Panel>())
                p.Back = Core.ThemeColors.TabHeader;
            foreach (var f in page.Controls.Find("tabFlowLayout", true).OfType<FlowLayoutPanel>())
                f.BackColor = Core.ThemeColors.Surface;
            foreach (var t in page.Controls.Find("tabTableList", true).OfType<AntdUI.Table>())
                t.BackColor = Core.ThemeColors.Surface;
        }
        this.Refresh();
    }

    private void UpdateTreeLoadingText(AntdUI.TreeItemCollection items)
    {
        foreach (var item in items)
        {
            if (item.Tag as string == LazyPlaceholderTag)
            {
                item.Text = LocalizationManager.GetString("Loading");
            }
            if (item.Sub.Count > 0)
            {
                UpdateTreeLoadingText(item.Sub);
            }
        }
    }

    private TagTranslator? _tagTranslator;

    public void Initialize(AppConfig config, CoverManager coverManager,
                           IAssetMetaStore? metaStore = null,
                           ReadRecordQueue? readRecordQueue = null,
                           UserPreferences? prefs = null,
                           TagTranslator? tagTranslator = null)
    {
        _config = config;
        _coverManager = coverManager;
        _metaStore = metaStore;
        _readRecordQueue = readRecordQueue;
        _prefs = prefs;
        _tagTranslator = tagTranslator;

        // ctor 跑 WireDrawerEvents 時 _config 還沒設 → 按鈕可見性沒生效
        // 這邊 Initialize 完成後 sync 一次
        if (detailDrawerHost?.WorkPanel != null)
        {
            detailDrawerHost.WorkPanel.ShowFetchExternalButton = config.EnableExternalMetadata;
            detailDrawerHost.WorkPanel.SetTagTranslator(tagTranslator);
            detailDrawerHost.AuthorPanel.SetTagTranslator(tagTranslator);
        }

        // Recorded fired from background thread → marshal to UI 同步卡片 icon
        if (_readRecordQueue != null)
        {
            _readRecordQueue.Recorded += path =>
            {
                if (IsDisposed) return;
                try { BeginInvoke(() => RefreshCardReadStatusByPath(path)); }
                catch { /* handle 還沒建好或已 disposed */ }
            };
        }

        _ = LoadTreeAsync();
    }

    /// <summary>
    /// 切換指定 tab 的檢視模式（per-tab 獨立）。
    /// 卡片模式之間切換只 iterate 現有卡片改尺寸；涉及 List 才需要 reload。
    /// </summary>
    public void SetTabViewMode(AntdUI.TabPage page, GalleryViewMode mode)
    {
        if (page == null) return;
        var oldMode = GetTabMode(page);
        if (oldMode == mode) return;
        _tabViewModes[page] = mode;
        SyncTabVisibility(page);

        bool oldIsCards = oldMode != GalleryViewMode.List;
        bool newIsCards = mode != GalleryViewMode.List;
        if (oldIsCards && newIsCards)
        {
            ResizeCardsOnTab(page, mode);
        }
        else
        {
            // 切到/離 List 必須重 load（FlowLayoutPanel ↔ Table 結構不同）
            if (page.Tag is string path) LoadCurrentViewMode(page, path);
        }
    }

    private void ResizeCardsOnTab(AntdUI.TabPage page, GalleryViewMode mode)
    {
        var flow = FindTabFlowLayout(page);
        if (flow == null) return;
        flow.SuspendLayout();
        foreach (var c in flow.Controls.OfType<ComicCardControl>())
        {
            c.ApplyViewMode(mode);
        }
        flow.ResumeLayout(true);
    }

    /// <summary>掃所有 tab 的卡片，找 ItemPath 相符者刷 read status icon。</summary>
    private void RefreshCardReadStatusByPath(string path)
    {
        foreach (AntdUI.TabPage page in tabsContent.Pages)
        {
            var flow = FindTabFlowLayout(page);
            if (flow == null) continue;
            foreach (var c in flow.Controls.OfType<ComicCardControl>())
            {
                if (string.Equals(c.ItemPath, path, StringComparison.OrdinalIgnoreCase))
                    c.RefreshReadStatusIcon();
            }
        }
    }

    private void SyncTabVisibility(AntdUI.TabPage? page)
    {
        if (page == null) return;
        var flow = FindTabFlowLayout(page);
        var table = FindTabTable(page);
        bool isList = GetTabMode(page) == GalleryViewMode.List;
        if (flow != null) flow.Visible = !isList;
        if (table != null) table.Visible = isList;
    }

    public void OnConfigUpdated(AppConfig config)
    {
        _config = config;
        // 外部 metadata 啟用狀態變更 → 刷新 WorkDetailPanel 按鈕可見性
        if (detailDrawerHost?.WorkPanel != null)
            detailDrawerHost.WorkPanel.ShowFetchExternalButton = _config.EnableExternalMetadata;
        _ = LoadTreeAsync();
    }

    /// <summary>SettingsForm 儲存後 MainForm 呼叫，傳新 prefs 進來讓 reader 偏好即時生效。</summary>
    public void OnPrefsUpdated(UserPreferences prefs)
    {
        _prefs = prefs;
    }

    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            RefreshCurrentTab();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// detailDrawerHost 內兩個 panel 的事件統一接：CloseRequested / 點作品 / Author 連結 / Explorer。
    /// 從 ctor 抽出避免 ctor 過長。
    /// </summary>
    private void WireDrawerEvents()
    {
        // 兩個 panel 的 X 鈕都關掉 drawer
        detailDrawerHost.AuthorPanel.CloseRequested += () => _detailDrawerForm?.Close();
        detailDrawerHost.WorkPanel.CloseRequested += () => _detailDrawerForm?.Close();

        // 點 AuthorInfoPanel 的 RecentlyRead / AllWorks 行 → 等同卡片「閱讀」
        detailDrawerHost.AuthorPanel.RequestOpenWork += path =>
            RecordReadAndOpenViewer(path, refreshAuthorPanel: true);

        // WorkDetailPanel 的 Author 連結 → 切回 AuthorInfoPanel
        detailDrawerHost.WorkPanel.RequestOpenAuthor += authorId =>
        {
            if (_metaStore == null) return;
            var info = _metaStore.GetAuthorInfo(authorId);
            if (info == null)
                detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "作者資料缺失");
            else
                detailDrawerHost.ShowAuthor(info);
        };

        // WorkDetailPanel 的「閱讀」按鈕 + cover 點擊
        detailDrawerHost.WorkPanel.RequestOpenViewer += path =>
            RecordReadAndOpenViewer(path, refreshAuthorPanel: false);

        // WorkDetailPanel 的「開啟位置」 → Explorer
        detailDrawerHost.WorkPanel.RequestExplorer += path => OpenInExplorer(path);

        // 「重置閱讀記錄」：彈確認 Modal → ResetReadingRecord → 刷面板 + card icon
        detailDrawerHost.WorkPanel.RequestResetReading += path => ConfirmAndResetWorkReading(path);

        // 編輯儲存：寫 metadata + 設 manually_edited + 重 set tags + 刷面板 / 卡片
        detailDrawerHost.WorkPanel.RequestSaveEdit += data => SaveWorkEdit(data);

        // 從 nhentai 拉取 metadata（外部）
        detailDrawerHost.WorkPanel.RequestFetchExternal += async path => await FetchExternalMetadata(path);

        // 顯示按鈕：依 config.EnableExternalMetadata
        detailDrawerHost.WorkPanel.ShowFetchExternalButton =
            _prefs != null && _config != null && _config.EnableExternalMetadata;

        // AuthorInfoPanel 加「清除此作者所有閱讀記錄」入口（透過 panel 暴露的 event）
        detailDrawerHost.AuthorPanel.RequestResetAuthorReading += authorId => ConfirmAndResetAuthorReading(authorId);

        // AuthorInfoPanel 編輯儲存
        detailDrawerHost.AuthorPanel.RequestSaveEdit += data => SaveAuthorEdit(data);
    }

    /// <summary>AuthorInfoPanel「儲存」事件 handler — RenameAuthor + SetAuthorFolderPath + 別名 diff 寫入 + SetAuthorTags。</summary>
    private void SaveAuthorEdit(AuthorEditData data)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        try
        {
            // 1. 主名 — 改了才呼叫；衝突時 Modal 詢問是否合併
            var current = _metaStore.GetAuthorInfo(data.AuthorId);
            if (current != null && !string.Equals(current.Author.MainName, data.MainName))
            {
                try { _metaStore.RenameAuthor(data.AuthorId, data.MainName); }
                catch (InvalidOperationException)
                {
                    if (form != null)
                    {
                        var conflict = _metaStore.FindAuthorByMainName(data.MainName);
                        if (conflict != null)
                        {
                            AntdUI.Modal.open(new AntdUI.Modal.Config(form, "作者名稱衝突",
                                $"已存在主名為「{data.MainName}」的作者（id={conflict.Id}）。\n要把當前作者的所有作品/別名/標籤合併進去嗎？\n\n此動作不可復原。",
                                AntdUI.TType.Warn)
                            {
                                OkText = "合併",
                                CancelText = "取消",
                                OnOk = _ =>
                                {
                                    try { _metaStore.MergeAuthor(data.AuthorId, conflict.Id); }
                                    catch { return true; }
                                    BeginInvoke(new Action(() =>
                                    {
                                        if (IsDisposed) return;
                                        _detailDrawerForm?.Close();
                                        var ignored = LoadTreeAsync();
                                    }));
                                    return true;
                                }
                            });
                            return;   // 等使用者按 OK；合併後不繼續走下面流程
                        }
                    }
                    return;
                }
            }

            // 2. 別名 diff：DB 中存在但 data 沒有 = 刪；data 中 Id=0 = 新增；其他 = 改
            var dbAliases = _metaStore.ListAuthorAliases(data.AuthorId).ToDictionary(a => a.Id);
            var keepIds = new HashSet<long>();
            foreach (var alias in data.Aliases)
            {
                if (alias.Id == 0)
                {
                    _metaStore.AddAuthorAlias(data.AuthorId, alias.Name, alias.Lang);
                }
                else
                {
                    keepIds.Add(alias.Id);
                    if (dbAliases.TryGetValue(alias.Id, out var dbA) &&
                        (dbA.Name != alias.Name || (dbA.Lang ?? "") != (alias.Lang ?? "")))
                    {
                        _metaStore.UpdateAuthorAlias(alias.Id, alias.Name, alias.Lang);
                    }
                }
            }
            foreach (var dbA in dbAliases.Values)
                if (!keepIds.Contains(dbA.Id)) _metaStore.DeleteAuthorAlias(dbA.Id);

            // 4. tag — 整批 reset
            _metaStore.SetAuthorTags(data.AuthorId, data.Tags);

            // 5. 刷面板
            var fresh = _metaStore.GetAuthorInfo(data.AuthorId);
            if (fresh != null) detailDrawerHost.ShowAuthor(fresh);
        }
        catch (Exception ex)
        {
            if (form != null) AntdUI.Message.error(form, "儲存失敗: " + ex.Message);
        }
    }

    /// <summary>WorkDetailPanel「儲存」事件 handler — 寫 metadata + author 解析 + tags + manually_edited 旗標。</summary>
    private void SaveWorkEdit(WorkEditData data)
    {
        if (_metaStore == null) return;
        var assetId = _metaStore.GetAssetId(data.Path);
        if (assetId == null) return;

        try
        {
            // 解析作者：留空 = 清空 author_id；有名 = UpsertAuthor 取或建
            long? authorId = null;
            if (!string.IsNullOrWhiteSpace(data.AuthorName))
            {
                authorId = _metaStore.UpsertAuthor(data.AuthorName.Trim());
            }

            // 取舊 metadata 保留閱讀相關欄位（read_status / read_count / last_read / last_page），
            // 編輯不應動到閱讀記錄
            var existing = _metaStore.GetMetadata(assetId.Value);
            var updated = new AssetMetadata
            {
                AssetId = assetId.Value,
                AuthorId = authorId,
                Title = data.Title,
                Series = data.Series,
                ChapterNo = data.ChapterNo,
                Language = data.Language,
                TranslationGroup = data.TranslationGroup,
                Rating = data.Rating,
                PageCount = existing?.PageCount,
                ReadStatus = existing?.ReadStatus ?? ReadStatus.Unread,
                ReadCount = existing?.ReadCount ?? 0,
                LastRead = existing?.LastRead,
                LastPage = existing?.LastPage,
                ExternalId = existing?.ExternalId,
                ParsedAt = existing?.ParsedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ManuallyEdited = true,
            };
            _metaStore.UpsertMetadata(updated);
            _metaStore.SetAssetTags(assetId.Value, data.Tags);

            // 刷面板 + 卡片 metadata view
            ShowWorkDetail(data.Path);
            RefreshCardReadStatusByPath(data.Path);
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null) AntdUI.Message.error(form, "儲存失敗: " + ex.Message);
        }
    }

    /// <summary>「從 nhentai 拉取」事件 handler — 抽 id（沒就 search 提示）→ API 抓 → merge dialog → 寫 DB。</summary>
    private async Task FetchExternalMetadata(string path)
    {
        if (_metaStore == null || _config == null) return;
        var form = this.FindForm();
        if (form == null) return;

        var apiKey = _config.GetNhentaiApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            AntdUI.Message.warn(form, LocalizationManager.GetString("FetchExtErrNoKey") ?? "請先在「設定 / 外部資料來源」設定 API key");
            return;
        }

        var assetId = _metaStore.GetAssetId(path);
        if (assetId == null) return;
        var detail = _metaStore.GetWorkDetail(path);
        if (detail == null) return;

        // 抽 id：先試從檔名（不含副檔名 + parent dir name）
        var name = Path.GetFileName(path);
        var externalId = NhentaiIdExtractor.Extract(name) ?? NhentaiIdExtractor.Extract(Path.GetFileName(Path.GetDirectoryName(path) ?? ""));

        try
        {
            using var client = new NhentaiApiClient(apiKey);

            MetadataLookupResult? result = null;
            if (!string.IsNullOrEmpty(externalId))
            {
                result = await client.GetByExternalIdAsync(externalId);
                if (result == null)
                {
                    AntdUI.Message.warn(form, string.Format(
                        LocalizationManager.GetString("FetchExtErrNotFoundFmt") ?? "id={0} 在 nhentai 上找不到（可能已下架）",
                        externalId));
                    return;
                }
            }
            else
            {
                // 沒 id → 開 search picker fallback
                var initialQuery = BuildSearchQueryFromFilename(name);
                using var picker = new NhentaiSearchPickerDialog(name, initialQuery,
                    q => client.SearchAsync(q));
                if (picker.ShowDialog(form) != DialogResult.OK || picker.SelectedHit == null) return;
                result = await client.GetByExternalIdAsync(picker.SelectedHit.ExternalId);
                if (result == null)
                {
                    AntdUI.Message.warn(form, string.Format(
                        LocalizationManager.GetString("FetchExtErrNotFoundFmt") ?? "id={0} 在 nhentai 上找不到（可能已下架）",
                        picker.SelectedHit.ExternalId));
                    return;
                }
            }

            // 開 merge dialog（傳 translator 即時顯示中文）
            using var dlg = new ExternalMetadataMergeDialog(detail, result, batchMode: false, translator: _tagTranslator);
            if (dlg.ShowDialog(form) != DialogResult.OK || dlg.Result == null) return;

            ApplyMergeResult(detail, dlg.Result);
            ShowWorkDetail(path);
            RefreshCardReadStatusByPath(path);

            AntdUI.Message.success(form,
                LocalizationManager.GetString("FetchExtDone") ?? "metadata 已套用");
        }
        catch (Exception ex)
        {
            AntdUI.Message.error(form, string.Format(
                LocalizationManager.GetString("FetchExtErrFmt") ?? "拉取失敗：{0}", ex.Message));
        }
    }

    /// <summary>從檔名建構初始搜尋字串：parser 抽出 title + 加 artist:slug 前綴。</summary>
    private static string BuildSearchQueryFromFilename(string filename)
    {
        var parsed = FilenameMetadataParser.ParseWorkName(filename);
        var parts = new List<string>();

        // artist:slug — 用 lower-case 去除空格
        if (!string.IsNullOrWhiteSpace(parsed.AuthorMainName))
        {
            var slug = parsed.AuthorMainName.Trim().ToLowerInvariant().Replace(' ', '-');
            parts.Add($"artist:{slug}");
        }
        // title 主體
        if (!string.IsNullOrWhiteSpace(parsed.Title))
            parts.Add(parsed.Title.Trim());
        // 都沒抽到 → 用整個 filename（去副檔名）當 fallback
        if (parts.Count == 0)
            parts.Add(Path.GetFileNameWithoutExtension(filename));

        return string.Join(" ", parts);
    }

    /// <summary>把 MergeDialog 的結果寫進 DB（asset_meta + asset_tag + asset_external_source）。</summary>
    private void ApplyMergeResult(WorkDetail detail, MergeResult result)
    {
        if (_metaStore == null) return;

        // 解析作者：result.AuthorMainName 有值 → upsert；null → 保留既有
        long? authorId = detail.AuthorId;
        if (!string.IsNullOrWhiteSpace(result.AuthorMainName))
            authorId = _metaStore.UpsertAuthor(result.AuthorMainName.Trim());

        var existing = _metaStore.GetMetadata(detail.AssetId);
        var updated = new AssetMetadata
        {
            AssetId = detail.AssetId,
            AuthorId = authorId,
            Title = result.Title ?? existing?.Title ?? detail.Title,
            Series = result.Series ?? existing?.Series ?? detail.Series,
            ChapterNo = existing?.ChapterNo ?? detail.ChapterNo,
            Language = result.Language ?? existing?.Language ?? detail.Language,
            TranslationGroup = result.TranslationGroup ?? existing?.TranslationGroup ?? detail.TranslationGroup,
            PageCount = result.PageCount ?? existing?.PageCount ?? detail.PageCount,
            Rating = existing?.Rating ?? detail.Rating,
            ReadStatus = existing?.ReadStatus ?? ReadStatus.Unread,
            ReadCount = existing?.ReadCount ?? 0,
            LastRead = existing?.LastRead,
            LastPage = existing?.LastPage,
            ExternalId = result.ExternalSourceRecord?.SourceId ?? existing?.ExternalId,
            ParsedAt = existing?.ParsedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ManuallyEdited = true,   // 使用者剛 review 過外部 metadata，鎖住避免 re-parse 反轉
        };
        _metaStore.UpsertMetadata(updated);

        if (result.Tags != null) _metaStore.SetAssetTags(detail.AssetId, result.Tags);
        if (result.ExternalSourceRecord != null) _metaStore.UpsertExternalSource(result.ExternalSourceRecord);
    }

    private void ConfirmAndResetWorkReading(string path)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        var name = Path.GetFileName(path);
        AntdUI.Modal.open(new AntdUI.Modal.Config(form, "重置閱讀記錄",
            $"重置「{name}」的閱讀狀態 / 次數 / 上次閱讀 / 上次頁碼？\n\n此動作不可復原；metadata（標題 / 作者 / 標籤）會保留。",
            AntdUI.TType.Warn)
        {
            OkText = "重置",
            CancelText = "取消",
            OnOk = _ =>
            {
                try { _metaStore.ResetReadingRecord(path); }
                catch { return true; }
                // Modal OnOk 由 AntdUI 在 worker thread fire，UI 更新需 marshal 回 UI thread
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    ShowWorkDetail(path);
                    RefreshCardReadStatusByPath(path);
                });
                return true;
            }
        });
    }

    private void ConfirmAndResetAuthorReading(long authorId)
    {
        if (_metaStore == null) return;
        var form = this.FindForm();
        if (form == null) return;
        var info = _metaStore.GetAuthorInfo(authorId);
        if (info == null) return;
        AntdUI.Modal.open(new AntdUI.Modal.Config(form, "清除作者閱讀記錄",
            $"清除「{info.Author.MainName}」底下所有 {info.TotalWorks} 件作品的閱讀記錄？\n\n此動作不可復原；metadata 會保留。",
            AntdUI.TType.Warn)
        {
            OkText = "清除",
            CancelText = "取消",
            OnOk = _ =>
            {
                try { _metaStore.ResetReadingRecordsForAuthor(authorId); }
                catch { return true; }
                // 重撈 + 刷面板 + 卡片 icon — marshal 回 UI thread（Modal callback 在 worker thread）
                var fresh = _metaStore.GetAuthorInfo(authorId);
                BeginInvoke(() =>
                {
                    if (IsDisposed) return;
                    if (fresh != null) detailDrawerHost.ShowAuthor(fresh);
                    foreach (var w in info.AllWorks) RefreshCardReadStatusByPath(w.Path);
                });
                return true;
            }
        });
    }

    /// <summary>
    /// 共用：紀錄一次「讀過」+ 開外部 viewer + 刷對應 panel + 同步卡片 icon。
    /// AuthorInfoPanel 與 WorkDetailPanel 點作品的行為共用此 helper。
    /// </summary>
    private void RecordReadAndOpenViewer(string path, bool refreshAuthorPanel)
    {
        // 走 queue 延遲提交：30s 後才實際 RecordRead；queue.Recorded event 觸發時自動刷卡片 icon
        EnqueueReadIfEnabled(path);
        OpenForReading(path);
        if (refreshAuthorPanel) ShowAuthorInfo(path);
        else ShowWorkDetail(path);
    }

    /// <summary>
    /// Tree 節點右鍵 → 四項選單（檔案總管 / 目前頁籤 / 另開頁籤 / 作者資訊），各帶 SVG icon。
    /// </summary>
    private void ShowTreeNodeContextMenu(string path)
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInExplorer") ?? "檔案總管開啟")
                { IconSvg = "FolderOpenOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInCurrentTab") ?? "目前頁籤開啟")
                { IconSvg = "FileOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuOpenInNewTab") ?? "另開新頁籤")
                { IconSvg = "PlusOutlined" },
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuAuthorInfo") ?? "作者資訊")
                { IconSvg = "UserOutlined" },
        };

        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            string? t = mi.Text;
            if (t == null) return;
            if (t.Contains("檔案總管") || t.Contains("Explorer")) OpenInExplorer(path);
            else if (t.Contains("目前頁籤") || t.Contains("Current")) OpenFolderTab(path, false);
            else if (t.Contains("另開") || t.Contains("New Tab")) OpenFolderTab(path, true);
            else if (t.Contains("作者") || t.Contains("Author")) ShowAuthorInfo(path);
        }, items);
    }

    /// <summary>
    /// 解析 path 對應的 author，用 AntdUI.Drawer 從右邊滑出顯示。
    /// Mask=false（不遮主畫面），點 drawer 外由 DrawerOutsideClickFilter 自動關閉。
    /// </summary>
    private void ShowAuthorInfo(string path)
    {
        if (_metaStore == null)
        {
            detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "無法解析此路徑的作者資訊");
        }
        else
        {
            var authorId = _metaStore.ResolveAuthorIdByPath(path);
            if (authorId == null)
            {
                detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "無法解析此路徑的作者資訊");
            }
            else
            {
                var info = _metaStore.GetAuthorInfo(authorId.Value);
                if (info == null)
                    detailDrawerHost.ShowAuthorEmpty(LocalizationManager.GetString("AuthorInfoNoData") ?? "作者資料缺失");
                else
                    detailDrawerHost.ShowAuthor(info);
            }
        }
        EnsureDetailDrawerOpen();
    }

    /// <summary>
    /// 解析 path 對應的作品詳細資料，用 AntdUI.Drawer 從右邊滑出顯示。
    /// 先以 null cover 開啟 drawer 避免閃爍，async 補上 cover image。
    /// </summary>
    private void ShowWorkDetail(string path)
    {
        if (_metaStore == null)
        {
            detailDrawerHost.ShowWorkEmpty(LocalizationManager.GetString("WorkDetailNoData") ?? "找不到此作品的詳細資訊");
            EnsureDetailDrawerOpen();
            return;
        }
        var detail = _metaStore.GetWorkDetail(path);
        if (detail == null)
        {
            detailDrawerHost.ShowWorkEmpty(LocalizationManager.GetString("WorkDetailNoData") ?? "找不到此作品的詳細資訊");
            EnsureDetailDrawerOpen();
            return;
        }
        detailDrawerHost.ShowWork(detail, null);
        EnsureDetailDrawerOpen();
        _ = LoadWorkCoverAsync(path);
    }

    /// <summary>非同步載 work cover 後 marshal 回 UI thread 套到 WorkDetailPanel。</summary>
    private async Task LoadWorkCoverAsync(string path)
    {
        try
        {
            var img = await _coverManager.GetCoverAsync(path, 300);
            if (img == null) return;
            if (this.IsDisposed) { img.Dispose(); return; }
            this.Invoke(() =>
            {
                if (!this.IsDisposed) detailDrawerHost.WorkPanel.SetCover(img);
                else img.Dispose();
            });
        }
        catch { /* cover 取不到不影響 panel */ }
    }

    /// <summary>共用 Drawer open/refresh 邏輯：已開→Activate；未開→新開 380 寬 Drawer。</summary>
    private void EnsureDetailDrawerOpen()
    {
        if (_detailDrawerForm != null && !_detailDrawerForm.IsDisposed)
        {
            _detailDrawerForm.Activate();
            return;
        }

        var ownerForm = this.FindForm();
        if (ownerForm == null) return;

        // Drawer 量 Content.Size 決定 drawer 大小。每次強制 reset 成 380 × formHeight
        detailDrawerHost.Dock = DockStyle.None;
        detailDrawerHost.Size = new Size(420, ownerForm.ClientSize.Height);

        var config = new AntdUI.Drawer.Config(ownerForm, detailDrawerHost)
        {
            Align = AntdUI.TAlignMini.Right,
            Mask = false,
            Padding = 0,
            Dispose = false,
            OnClose = () =>
            {
                if (_drawerClickFilter != null)
                {
                    Application.RemoveMessageFilter(_drawerClickFilter);
                    _drawerClickFilter = null;
                }
                _detailDrawerForm = null;
            },
        };

        _detailDrawerForm = AntdUI.Drawer.open(config);

        // 點 drawer 以外區域 → 自動關閉（IMessageFilter 攔截 WM_LBUTTONDOWN）
        if (_detailDrawerForm != null)
        {
            _drawerClickFilter = new DrawerOutsideClickFilter(_detailDrawerForm);
            Application.AddMessageFilter(_drawerClickFilter);
        }
    }

    /// <summary>
    /// Tree 空白處右鍵 → 一項選單（重新整理整棵 tree）。
    /// </summary>
    private void ShowTreeBlankContextMenu()
    {
        var items = new AntdUI.IContextMenuStripItem[]
        {
            new AntdUI.ContextMenuStripItem(LocalizationManager.GetString("MenuRefresh") ?? "重新整理")
                { IconSvg = "RedoOutlined" },
        };
        AntdUI.ContextMenuStrip.open(treeFolders, mi =>
        {
            _ = LoadTreeAsync();
        }, items);
    }

    private Task LoadTreeAsync()
    {
        if (string.IsNullOrEmpty(_config.RootDirectory) || !Directory.Exists(_config.RootDirectory)) return Task.CompletedTask;

        treeFolders.Items.Clear();
        AntdUI.Spin.open(splitter2.Panel1, async config =>
        {
            var rootDir = new DirectoryInfo(_config.RootDirectory);
            var rootNode = await Task.Run(() => CreateFolderNode(rootDir));
            this.Invoke(() =>
            {
                treeFolders.Items.Add(rootNode);
                rootNode.Expand = true;
                LoadSubDirectories(rootNode, rootDir.FullName);
                UpdateTreeWidth();
            });
        });
        return Task.CompletedTask;
    }

    // Tab 內子控件 lookup 共用方法。Tab 各自含 breadcrumb / flowLayout / tableList，
    // 用 Name + 型別找到對應控件。null page 視為找不到。
    private static T? FindInTab<T>(AntdUI.TabPage? page, string name) where T : Control
    {
        if (page == null) return null;
        return page.Controls.Find(name, true).OfType<T>().FirstOrDefault();
    }

    private AntdUI.Breadcrumb? FindTabBreadcrumb(AntdUI.TabPage? page) => FindInTab<AntdUI.Breadcrumb>(page, "tabBreadcrumb");
    private FlowLayoutPanel? FindTabFlowLayout(AntdUI.TabPage? page) => FindInTab<FlowLayoutPanel>(page, "tabFlowLayout");
    private AntdUI.Table? FindTabTable(AntdUI.TabPage? page) => FindInTab<AntdUI.Table>(page, "tabTableList");

    private void UpdateBreadcrumb(string path)
    {
        var bc = FindTabBreadcrumb(tabsContent.SelectedTab);
        if (bc == null) return;

        bc.Items.Clear();
        if (string.IsNullOrEmpty(_config.RootDirectory) || string.IsNullOrEmpty(path)) return;

        string root = _config.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rootName = string.IsNullOrEmpty(Path.GetFileName(root)) ? root : Path.GetFileName(root);
        // AntdUI Breadcrumb 沒有 separator/gap 屬性，文字緊貼分隔符 → 前後補空白拉開
        bc.Items.Add(new AntdUI.BreadcrumbItem
        {
            Text = " " + rootName + " ",
            Tag = root,
            IconSvg = "HomeOutlined"
        });

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;

        string relative = path.Substring(root.Length)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(relative)) return;

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = relative.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        string accum = root;
        foreach (var seg in segments)
        {
            accum = Path.Combine(accum, seg);
            bc.Items.Add(new AntdUI.BreadcrumbItem { Text = " " + seg + " ", Tag = accum });
        }
    }

    private void RefreshCurrentTab()
    {
        if (tabsContent.SelectedTab?.Tag is not string path) return;
        LoadCurrentViewMode(tabsContent.SelectedTab, path);
    }

    private void LoadSubDirectories(AntdUI.TreeItem node, string path)
    {
        // 已展開過就不重掃（外部變動靠右鍵「重新整理」手動觸發）
        bool isPlaceholder = node.Sub.Count == 1
                             && (node.Sub[0].Tag as string) == LazyPlaceholderTag;
        if (!isPlaceholder) return;

        // 改 async：UI thread 不阻塞，使用者雙擊 tree node 觸發 expand 不卡頓
        Task.Run(() =>
        {
            List<DirectoryInfo> subDirs;
            try
            {
                subDirs = new DirectoryInfo(path).GetDirectories()
                    .Where(d => (d.Attributes & FileAttributes.Hidden) == 0)
                    .OrderBy(d => d.Name)
                    .ToList();
            }
            catch { return; }

            if (this.IsDisposed) return;
            try
            {
                this.Invoke(() =>
                {
                    if (treeFolders.IsDisposed) return;
                    node.Sub.Clear();
                    foreach (var subDir in subDirs)
                    {
                        node.Sub.Add(CreateFolderNode(subDir));
                    }
                    UpdateTreeWidth();
                });
            }
            catch { /* form/tree 已 dispose 等競態忽略 */ }
        });
    }

    private void UpdateTreeWidth()
    {
        if (treeFolders.Items.Count == 0) return;

        int maxWidth = 250;
        using (var g = treeFolders.CreateGraphics())
        {
            foreach (var item in treeFolders.Items)
            {
                int itemWidth = MeasureNodeWidth(g, item, 0);
                if (itemWidth > maxWidth) maxWidth = itemWidth;
            }
        }

        treeFolders.Width = maxWidth + 40;
    }

    private int MeasureNodeWidth(Graphics g, AntdUI.TreeItem item, int level)
    {
        var size = g.MeasureString(item.Text, treeFolders.Font);
        int currentWidth = (level * 20) + 30 + (int)size.Width;

        int maxSubWidth = currentWidth;
        if (item.Expand && item.Sub.Count > 0)
        {
            foreach (var subItem in item.Sub)
            {
                int subWidth = MeasureNodeWidth(g, subItem, level + 1);
                if (subWidth > maxSubWidth) maxSubWidth = subWidth;
            }
        }
        return maxSubWidth;
    }

    private AntdUI.TreeItem CreateFolderNode(DirectoryInfo dir)
    {
        var node = new AntdUI.TreeItem(dir.Name)
        {
            Tag = dir.FullName,
            IconSvg = "FolderOpenOutlined"
        };

        try
        {
            if (dir.GetDirectories().Any(d => (d.Attributes & FileAttributes.Hidden) == 0))
            {
                node.Sub.Add(new AntdUI.TreeItem("Loading...") { Tag = LazyPlaceholderTag });
            }
        }
        catch { }

        return node;
    }

    private void OpenFolderTab(string path, bool forceNew)
    {
        string folderName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folderName)) folderName = path;

        if (!forceNew && tabsContent.SelectedTab != null)
        {
            var currentTab = tabsContent.SelectedTab;
            if ((string?)currentTab.Tag == path) return;

            currentTab.Text = folderName;
            currentTab.Tag = path;
            UpdateBreadcrumb(path);
            LoadCurrentViewMode(currentTab, path);
            return;
        }

        var existingTab = tabsContent.Pages.Cast<AntdUI.TabPage>().FirstOrDefault(p => (string?)p.Tag == path);
        if (existingTab != null)
        {
            tabsContent.SelectedTab = existingTab;
            return;
        }

        // 新 Tab 結構：headerPanel (Dock=Top, 麵包屑 + refresh + view mode) + FlowLayoutPanel (Dock=Fill, 卡片區)
        var newPage = new AntdUI.TabPage
        {
            Text = folderName,
            Tag = path,
            BackColor = Core.ThemeColors.Surface
        };

        // 新 tab 預設 Medium。先存到 dictionary，後續控件用 GetTabMode(newPage) 取
        _tabViewModes[newPage] = GalleryViewMode.Medium;

        var headerPanel = new AntdUI.Panel
        {
            Name = "tabHeader",
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(33),  // DPI 150% 等高 → 50px，避免麵包屑文字裁切
            BackColor = Color.Transparent,
            Back = Core.ThemeColors.TabHeader,  // 跟主題切換
            Radius = 0,
        };

        var bc = new AntdUI.Breadcrumb
        {
            Name = "tabBreadcrumb",
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(8, 0, 8, 0),
            Font = new Font("Microsoft JhengHei UI", 12F),
        };
        bc.ItemClick += (s, e) =>
        {
            if (e.Item.Tag is string p) OpenFolderTab(p, false);
        };

        var btnRefresh = new AntdUI.Button
        {
            Name = "tabBtnRefresh",
            Dock = DockStyle.Right,
            Ghost = true,
            IconSvg = "RedoOutlined",
            IconRatio = 1F,
            ForeColor = Core.ThemeColors.Text,
            BackHover = Core.ThemeColors.Secondary,
            Size = LogicalToDeviceUnits(new Size(40, 32)),
        };
        btnRefresh.Click += (s, e) => RefreshCurrentTab();
        _btnTooltip.SetTip(btnRefresh, LocalizationManager.GetString("MenuRefresh"));

        // 4-segment view mode 切換器（Small / Medium / Large / List）
        var segViewMode = new AntdUI.Segmented
        {
            Name = "tabViewModeSeg",
            Dock = DockStyle.Right,
            Size = LogicalToDeviceUnits(new Size(160, 28)),
            IconRatio = 1F,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),  // S/M/L 字粗體
            // ArrowAlign=Top → 箭頭在 tooltip 頂端朝上 → tooltip 顯示在 segmented 下方
            TooltipConfig = new AntdUI.TooltipConfig { ArrowAlign = AntdUI.TAlign.Bottom },
        };
        // S/M/L 共用相框 SVG，字母用 SegmentedItem.Text 屬性顯示（AntdUI 自己 layout icon + text）
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = "S", Tooltip = LocalizationManager.GetString("ViewSmall") });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = "M", Tooltip = LocalizationManager.GetString("ViewMedium") });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { Text = "L", Tooltip = LocalizationManager.GetString("ViewLarge") });
        segViewMode.Items.Add(new AntdUI.SegmentedItem { IconSvg = "UnorderedListOutlined", Tooltip = LocalizationManager.GetString("ViewList") });
        segViewMode.SelectIndex = (int)GalleryViewMode.Medium;
        segViewMode.SelectIndexChanged += (s, e) =>
        {
            SetTabViewMode(newPage, (GalleryViewMode)segViewMode.SelectIndex);
        };

        // Dock=Right 多控件時最後加的在最右邊：先加 segViewMode 再加 btnRefresh → segViewMode 在 btnRefresh 左
        headerPanel.Controls.Add(bc);
        headerPanel.Controls.Add(segViewMode);
        headerPanel.Controls.Add(btnRefresh);

        var contentHost = new System.Windows.Forms.Panel
        {
            Name = "tabContentHost",
            Dock = DockStyle.Fill,
            BackColor = Core.ThemeColors.Surface,
        };

        bool isList = false;   // 新 tab 預設 Medium 不是 List

        var flowLayout = new BufferedFlowLayoutPanel
        {
            Name = "tabFlowLayout",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Core.ThemeColors.Surface,
            Visible = !isList,
        };

        var tableList = CreateTabTable();
        tableList.Visible = isList;

        // 縮圖 hover → 滑鼠附近 floating preview popup
        // 用 AntdUI.Table.HitTest 取 cell；
        //  - 資料：cell.Row.RECORD 直接讀綁定物件，避開 i_row 索引偏移
        //  - 欄位判斷：cell.COLUMN.Key 比對欄位 key，避開 i_cel 索引偏移
        //    （HitTest 的 i_row/i_cel 對 header/scroll/hidden row 的計法跟 BindingList 索引可能不一致）
        // 只在 Thumbnail 欄觸發 hover preview，避免整列都跳大圖太擾人
        string? currentPreviewPath = null;
        tableList.MouseMove += (s, e) =>
        {
            var cell = tableList.HitTest(e.X, e.Y, out _, out _);
            var item = cell?.Row?.RECORD as GalleryListItem;
            bool onThumbnailCol = cell?.COLUMN?.Key == "Thumbnail";
            if (!onThumbnailCol || item == null || string.IsNullOrEmpty(item.FullPath))
            {
                HidePreviewPopup();
                currentPreviewPath = null;
                return;
            }
            UpdatePreviewPopupPosition();
            if (currentPreviewPath == item.FullPath) return;
            currentPreviewPath = item.FullPath;
            _ = LoadPreviewPopupAsync(item.FullPath);
        };
        tableList.MouseLeave += (s, e) =>
        {
            currentPreviewPath = null;
            HidePreviewPopup();
        };

        // 「進入下一層」icon button click → 進入該資料夾
        tableList.CellButtonClick += (object s, AntdUI.TableButtonEventArgs args) =>
        {
            if (args.Btn?.Id != "OpenFolder") return;
            GalleryListItem? item = args.Record as GalleryListItem;
            if (item == null && tableList.DataSource is System.ComponentModel.BindingList<GalleryListItem> list
                && args.RowIndex >= 0 && args.RowIndex < list.Count)
            {
                item = list[args.RowIndex];
            }
            if (item != null && Directory.Exists(item.FullPath))
            {
                OpenFolderTab(item.FullPath, false);
            }
        };

        // 右鍵選單（原 hoverToolbar 功能）
        tableList.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var cell = tableList.HitTest(e.X, e.Y, out _, out _);
            // 同 hover 邏輯：直接從 cell.Row.RECORD 取資料，避開 i_row 索引偏移
            var item = cell?.Row?.RECORD as GalleryListItem;
            if (item == null || string.IsNullOrEmpty(item.FullPath)) return;

            var items = new List<AntdUI.IContextMenuStripItem>
            {
                new AntdUI.ContextMenuStripItem("📖 閱讀"),
                new AntdUI.ContextMenuStripItem("📂 檔案總管中開啟"),
            };
            if (Directory.Exists(item.FullPath))
            {
                items.Insert(0, new AntdUI.ContextMenuStripItem("🔗 另開頁籤"));
            }
            AntdUI.ContextMenuStrip.open(tableList, mi =>
            {
                if (mi.Text == "📖 閱讀") OpenForReading(item.FullPath);
                else if (mi.Text == "📂 檔案總管中開啟")
                    OpenInExplorer(item.FullPath);
                else if (mi.Text == "🔗 另開頁籤") OpenFolderTab(item.FullPath, true);
            }, items.ToArray());
        };

        contentHost.Controls.Add(flowLayout);
        contentHost.Controls.Add(tableList);

        newPage.Controls.Add(contentHost);
        newPage.Controls.Add(headerPanel);
        tabsContent.Pages.Add(newPage);
        tabsContent.SelectedTab = newPage;

        UpdateBreadcrumb(path);
        LoadCurrentViewMode(newPage, path);
    }

    private AntdUI.Table CreateTabTable()
    {
        var table = new AntdUI.Table
        {
            Name = "tabTableList",
            Dock = DockStyle.Fill,
            Bordered = false,
            RowHeight = (int?)80,
            AutoSizeColumnsMode = AntdUI.ColumnsMode.Fill,
            Font = new Font("Microsoft JhengHei UI", 10F),
            BackColor = Core.ThemeColors.Surface,
            GapCell = 2,           // 單元格內間距：預設約 8-12 太大，縮圖欄留白多；改 2px 讓縮圖貼齊
            VirtualMode = true,    // 大量 rows 時只繪 viewport 內，捲動流暢度大幅提升
        };

        table.Columns = new AntdUI.ColumnCollection
        {
            new AntdUI.Column("Action", "", AntdUI.ColumnAlign.Center) { Width = "44" },
            new AntdUI.Column("Thumbnail", "縮圖", AntdUI.ColumnAlign.Center) { Width = "72" },
            new AntdUI.Column("Name", "名稱") { SortOrder = true },
            new AntdUI.Column("Size", "大小", AntdUI.ColumnAlign.Right) { Width = "100", SortOrder = true },
            new AntdUI.Column("CreatedAt", "建立時間", AntdUI.ColumnAlign.Center) { Width = "160", SortOrder = true },
        };
        table.EnableHeaderResizing = true;

        // 點擊 row highlight
        table.RowSelectedBg = Core.ThemeColors.Secondary;

        // zebra：奇偶行不同底色
        table.SetRowStyle += (object s, AntdUI.TableSetRowStyleEventArgs e) =>
        {
            return new AntdUI.Table.CellStyleInfo
            {
                BackColor = e.Index % 2 == 0 ? Color.White : Core.ThemeColors.RowAlternateBg,
            };
        };

        return table;
    }

    private async Task LoadPreviewAsync(System.Windows.Forms.PictureBox pic, string path)
    {
        var img = await _coverManager.GetCoverAsync(path, 300);
        if (img == null || pic.IsDisposed) return;
        this.Invoke(() => { pic.Image = img; });
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
        this.Invoke(() =>
        {
            _previewPic.Image = img;
            UpdatePreviewPopupPosition();
            if (!_previewForm.Visible) _previewForm.Show();
        });
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


    private void LoadCurrentViewMode(AntdUI.TabPage page, string path)
    {
        var mode = GetTabMode(page);
        if (mode == GalleryViewMode.List)
        {
            var table = FindTabTable(page);
            if (table != null) _ = LoadGalleryListAsync(table, path);
        }
        else
        {
            var flow = FindTabFlowLayout(page);
            if (flow != null) _ = LoadGalleryAsync(flow, path, mode);
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
                try { this.Invoke(() => onLoaded(img)); }
                catch { /* form 已 dispose 等競態忽略 */ }
            }
            finally
            {
                _coverSemaphore.Release();
            }
        });
    }

    private Task LoadGalleryListAsync(AntdUI.Table table, string path)
    {
        AntdUI.Spin.open(this, async config =>
        {
            // ===== Phase 1: 檔案掃描 + 列表 binding (Spin 遮蔽期間) =====
            var fsInfos = await Task.Run(() =>
                new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());

            var prepared = await Task.Run(() => fsInfos.Select(fs =>
            {
                bool isDir = fs is DirectoryInfo;
                long size = isDir ? 0 : ((FileInfo)fs).Length;
                return (fs, isDir, size);
            }).ToList());

            var list = new System.ComponentModel.BindingList<GalleryListItem>();
            var thumbTasks = new List<Task>();

            foreach (var (fs, isDir, size) in prepared)
            {
                var item = new GalleryListItem
                {
                    Name = fs.Name,
                    Size = isDir ? "" : FormatSize(size),
                    CreatedAt = fs.CreationTime.ToString("yyyy-MM-dd HH:mm"),
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

                // 縮圖任務 fire-and-forget — Thumbnail 是純資料賦值，不需 Invoke；
                // 全部完成後再 reset DataSource 一次觸發整表重繪
                var capturedItem = item;
                thumbTasks.Add(Task.Run(async () =>
                {
                    var img = await _coverManager.GetCoverAsync(capturedItem.FullPath, 120);
                    // CellImage(img, int) 第二個參數是 Radius 不是 Size！要靠 Size 屬性設邊長
                    // 60×80 (3:4) 貼合漫畫封面比例
                    if (img != null) capturedItem.Thumbnail = new AntdUI.CellImage(img)
                    {
                        Size = new Size(60, 80),
                        Radius = 4,
                        ImageFit = AntdUI.TFit.Cover,
                    };
                }));
            }

            // 第一次 set：文字列表立即可見（縮圖欄空白）
            this.Invoke(() => { table.DataSource = list; });

            // ===== Phase 2: 縮圖背景載入（Spin 已關閉、列表可見） =====
            if (thumbTasks.Count == 0) return;
            _ = Task.Run(async () =>
            {
                await Task.WhenAll(thumbTasks);
                if (this.IsDisposed) return;
                try
                {
                    this.Invoke(() =>
                    {
                        if (table.IsDisposed) return;
                        // AntdUI Table 對 ListChanged/PropertyChanged 不 hook，唯一可靠重繪辦法是 reset DataSource
                        table.DataSource = null;
                        table.DataSource = list;
                    });
                }
                catch { /* form/table 已 dispose 等競態忽略 */ }
            });
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
    {
        container.Controls.Clear();

        AntdUI.Spin.open(container, async config =>
        {
            var items = await Task.Run(() =>
                new DirectoryInfo(path).GetFileSystemInfos()
                    .Where(ShouldShowInGallery)
                    .OrderBy(i => i is DirectoryInfo ? 0 : 1)
                    .ThenBy(i => i.Name)
                    .ToList());

            // 把 foreach 拆 chunks，批與批之間 await Task.Delay(1) 讓出 UI thread 給 Spin 動畫 paint。
            // 整體時間幾乎相同 (Add HWND 時間不變) 但使用者看得到 Spin 在轉 + cards 分批冒出有 progress 感
            const int ChunkSize = 8;
            for (int i = 0; i < items.Count; i += ChunkSize)
            {
                var chunk = items.Skip(i).Take(ChunkSize).ToList();
                this.Invoke(() =>
                {
                    container.SuspendLayout();
                    foreach (var item in chunk)
                    {
                        bool isFolderNode = item is DirectoryInfo;
                        var card = new ComicCardControl();
                        card.SetData(item, isFolderNode);
                        // 註：Phase 3-A 嘗試在卡片下方加 "— Author" 副標但被回退 —
                        // 良好整理過的庫從左側 tree 就知道 author，副標冗餘。metadata 留給未來
                        // 「平鋪虛擬庫」/「搜尋結果」等沒明確 hierarchy 的場景再用 SetMetadata。
                        card.ApplyViewMode(mode);
                        // 卡片用此 provider 查當前 read_status（icon 顯示 + menu disable）
                        card.ReadStatusProvider = p => _metaStore?.GetReadStatus(p);
                        var c = card;   // 給 lambda capture 用，避免 foreach var closure 疑慮
                        // 透過 queue 延遲 RecordRead；queue.Recorded event 會 RefreshCardReadStatusByPath
                        card.RequestOpenFolder += p => { EnqueueReadIfEnabled(p); OpenFolderTab(p, false); };
                        card.RequestOpenViewer += p => { EnqueueReadIfEnabled(p); OpenForReading(p); };
                        card.RequestNewTab += (p, forceNew) => { EnqueueReadIfEnabled(p); OpenFolderTab(p, forceNew); };
                        card.RequestExplorer += p => OpenInExplorer(p);
                        card.RequestAuthorInfo += p => ShowAuthorInfo(p);
                        card.RequestWorkDetail += p => ShowWorkDetail(p);
                        // 強制走外部開啟（不看 UseBuiltInReader 偏好）：
                        // 漫畫格式 → 設定的外部 viewer；其他格式 → 系統預設程式
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
                        };
                        container.Controls.Add(card);

                        // 目錄與檔案都試著載封面：CoverManager 對目錄會遞迴往下找首張圖檔（受 MaxScanDepth 限制）
                        // 統一以 Large 寬度載入，切換 view mode 時 pic.ImageFit=Contain 縮放顯示，不需重新載
                        var capturedCard = card;
                        _ = LoadCoverInBackgroundAsync(item.FullName, MaxCardCoverWidth, img => capturedCard.SetCover(img));
                    }
                    container.ResumeLayout(true);   // 該 chunk 的 cards 立刻 layout + paint
                });
                await Task.Delay(1);   // 讓 UI thread 喘口氣 paint Spin animation frame
            }
        });

        return Task.CompletedTask;
    }

    private static readonly HashSet<string> _imageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".tif"
    };

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

    private static readonly HashSet<string> BuiltInReaderArchiveExts =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".cbz", ".rar", ".cbr", ".7z" };

    private static bool CanUseBuiltInReader(string path)
    {
        if (Directory.Exists(path)) return true;   // FolderReaderSource 會檢查內含圖
        if (!File.Exists(path)) return false;
        var ext = Path.GetExtension(path);
        if (BuiltInReaderArchiveExts.Contains(ext)) return true;
        // 單檔圖檔也支援 — FolderReaderSource 會 enumerate 同層 (Honeyview 行為)
        return _imageExts.Contains(ext);
    }

    private void OpenInBuiltInReader(string path)
    {
        IReaderSource source;
        var ext = Path.GetExtension(path);
        if (BuiltInReaderArchiveExts.Contains(ext) && File.Exists(path))
            source = new ArchiveReaderSource(path);
        else
            source = new FolderReaderSource(path);

        // 沒圖直接 fallback（避免空白 reader）
        if (source.PageCount == 0)
        {
            source.Dispose();
            throw new InvalidOperationException("No images found");
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
                    .Where(f => _imageExts.Contains(Path.GetExtension(f)))
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

    private void treeFolders_BeforeExpand(object sender, AntdUI.TreeExpandEventArgs e)
    {
        if (e.Item.Tag is string path)
        {
            LoadSubDirectories(e.Item, path);
        }
        e.CanExpand = true;
    }

    private void treeFolders_NodeMouseDoubleClick(object sender, AntdUI.TreeSelectEventArgs e)
    {
        // 改用滑鼠中鍵開新頁籤，這裡留空讓 AntdUI Tree 走預設行為（toggle expand）
    }
}

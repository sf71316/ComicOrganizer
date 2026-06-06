using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.App.UI;

namespace ComicOrganizer.App;

public partial class MainForm : Window, IThemedControl
{
    private readonly ConfigManager _configManager;
    private AppConfig _config;
    private readonly UserPreferencesManager _prefsManager;
    private UserPreferences _prefs;
    private readonly MappingIntelligence _intelligence;
    private readonly ArchiveOrganizer _organizer;
    private readonly CoverManager _coverManager;

    // Cache 服務 (#44)
    private readonly IAssetMetaStore _metaStore;
    private readonly IThumbStore _thumbStore;
    private readonly CacheWriter _cacheWriter;
    private readonly CacheRebuildService _rebuildService;
    private readonly CacheIntegrityChecker _integrityChecker;
    private readonly RebuildProgressTracker _rebuildTracker;
    private readonly CatalogReconcileService _reconcileService;
    private readonly ReparseAllService _reparseService;
    private readonly TagTranslator _tagTranslator;
    private readonly TagTranslationUpdater _translationUpdater;
    private readonly string _translationDbPath;

    // 閱讀記錄延遲提交 queue（#49 Phase 1.0）
    private readonly ReadRecordQueue _readRecordQueue;

    public MainForm()
    {
        // 先載 config 並初始化 ThemeManager → ThemeColors.X getter 才會回傳對的色，
        // InitializeComponent 內 Designer 設 BackColor = ThemeColors.X 取得正確色（不會被系統 DPI 鎖到 Dark）
        _configManager = new ConfigManager();
        _config = _configManager.Load();
        _prefsManager = new UserPreferencesManager();
        _prefs = _prefsManager.Load();
        MigrateLegacyPreferences();
        LocalizationManager.Initialize(_prefs.Language);
        ThemeManager.Initialize(_prefs.Theme);

        InitializeComponent();

        this.WireThemeAndLocalization();

        _intelligence = new MappingIntelligence();
        _organizer = new ArchiveOrganizer(_intelligence, _config);
        _coverManager = new CoverManager(_config);

        // Cache 服務：cache.db 與 thumbs/ 都放在 App exe 同層
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _metaStore = new SqliteAssetMetaStore(Path.Combine(baseDir, "cache.db"));
        _thumbStore = new FileSystemThumbStore(Path.Combine(baseDir, "thumbs"));
        _cacheWriter = new CacheWriter(_metaStore, _thumbStore, _config);
        _coverManager.AttachCache(_metaStore, _thumbStore, _cacheWriter);
        _rebuildTracker = new RebuildProgressTracker(_metaStore);
        _integrityChecker = new CacheIntegrityChecker(_metaStore, _thumbStore, _rebuildTracker);
        _rebuildService = new CacheRebuildService(_config, _coverManager, _metaStore, _thumbStore, _integrityChecker, _rebuildTracker, _cacheWriter);
        _reconcileService = new CatalogReconcileService(_config, _metaStore);
        _reparseService = new ReparseAllService(_metaStore, _rebuildTracker);
        // 翻譯庫：disk cache 優先（BaseDirectory/translations/db.text.json.gz）→ fallback embedded
        _translationDbPath = Path.Combine(baseDir, "translations", "db.text.json.gz");
        _tagTranslator = new TagTranslator(_translationDbPath);
        _translationUpdater = new TagTranslationUpdater();
        _readRecordQueue = new ReadRecordQueue(_metaStore, _config.ReadCountThresholdSeconds);
        // Tracker 改變時，statusBar 自動同步顯示（背景任務全域可見）
        _rebuildTracker.StateChanged += (s, e) => UpdateStatusFromTracker();

        // 等 Form Load 後 Initialize（此時 handle 已建立、Invoke 才能用）
        this.Load += (s, e) =>
        {
            galleryControl.Initialize(_config, _coverManager, _metaStore, _readRecordQueue, _prefs, _tagTranslator);
            archiveControl.Initialize(_config, _intelligence, _organizer, _metaStore, _tagTranslator);
            metadataManageControl.Initialize(_metaStore, _reparseService, _rebuildTracker, _tagTranslator);
            metadataManageControl.InitializeTranslationDb(_tagTranslator, _translationUpdater, _translationDbPath);
            metadataManageControl.ExternalMetadataEnabled = _config.EnableExternalMetadata;

            // 啟動後背景檢查翻譯庫更新（24h 防 spam GitHub）— fire-and-forget，網路錯靜默吞
            _ = CheckTranslationDbUpdateInBackground();

            // Phase I: 詮釋資料管理 → 作者 → 多選作品 → 「同步選取作品」 → 委派 GalleryControl 走 Phase F 同樣流程
            metadataManageControl.RequestBatchSync += async (authorId, paths) =>
                await galleryControl.BatchSyncForAuthor(authorId, paths);

            // Per-row sync icon → 單本拉取（Phase E 流程）
            metadataManageControl.RequestSyncOne += async path =>
                await galleryControl.SyncOneWork(path);

            ShowGallery();
        };

        // 關閉時 flush 所有 pending 閱讀紀錄，避免「快關 app」流失計數
        this.FormClosing += (s, e) => _readRecordQueue.Dispose();

        WireMenuEvents();
    }

    private void WireMenuEvents()
    {
        mnuFileGallery.Click += (s, e) => ShowGallery();
        mnuFileArchive.Click += (s, e) => ShowArchive();
        mnuFileMetadata.Click += (s, e) => ShowMetadataManage();
        mnuFileSettings.Click += (s, e) => OpenSettings();
        mnuFileRebuildCache.Click += (s, e) => OpenRebuildCacheDialog();
        mnuFileExit.Click += (s, e) => Close();

        // 檢視模式改放在每個 Tab header 內的 Segmented，per-tab 獨立。MainForm 不再 dispatch view mode
    }

    private void OpenRebuildCacheDialog()
    {
        // 用 Show（非 ShowDialog）讓使用者可關 form 繼續用 App，任務在背景跑
        var form = new UI.RebuildCacheForm(_rebuildService, _rebuildTracker, _reconcileService);
        form.Show(this);
    }

    /// <summary>tracker StateChanged → 把進度反映到底部 statusBar。</summary>
    private void UpdateStatusFromTracker()
    {
        if (statusBar.InvokeRequired)
        {
            statusBar.BeginInvoke(new Action(UpdateStatusFromTracker));
            return;
        }

        if (_rebuildTracker.IsRunning)
        {
            var p = _rebuildTracker.Current;
            if (p.Total > 0)
                statusLabel.Text = $"{_rebuildTracker.Phase} ({p.Done}/{p.Total})";
            else
                statusLabel.Text = _rebuildTracker.Phase;
        }
        else if (_rebuildTracker.WasInterrupted)
        {
            statusLabel.Text = "重建已取消";
        }
        else if (!string.IsNullOrEmpty(_rebuildTracker.Phase))
        {
            statusLabel.Text = "重建完成";
        }
    }

    /// <summary>啟動時背景檢查翻譯庫更新。
    /// AutoCheckTranslationDbUpdate=false → skip（user 在設定頁關掉了）；
    /// 距上次成功檢查 &lt; 24h → skip；網路錯靜默吞（不打擾 user）；
    /// 有更新 → 右下角 Notification.Config + SetLink → 點連結開更新 dialog。</summary>
    private async Task CheckTranslationDbUpdateInBackground()
    {
        if (!_config.AutoCheckTranslationDbUpdate) return;

        const long DayMs = 24L * 60 * 60 * 1000;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - _config.LastTranslationDbCheckTime < DayMs) return;

        try
        {
            var info = await _translationUpdater.CheckLatestAsync().ConfigureAwait(false);
            if (info == null) return;   // 網路 / 解析失敗 — 不更新時間戳，下次啟動會重試

            // 成功拿到資訊就記時間戳，避免每次啟動打 API
            _config.LastTranslationDbCheckTime = nowMs;
            try { _configManager.Save(_config); } catch { /* IO 錯不要中斷流程 */ }

            if (!TagTranslationUpdater.IsUpdateAvailable(_tagTranslator.HeadSha, info.Sha)) return;

            // 切回 UI thread 顯示 Notification
            if (IsDisposed) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                var localShort = _tagTranslator.HeadSha.Length > 7 ? _tagTranslator.HeadSha[..7] : _tagTranslator.HeadSha;
                var remoteShort = info.Sha.Length > 7 ? info.Sha[..7] : info.Sha;
                var title = LocalizationManager.GetString("TranslationDbUpdateAvailable") ?? "翻譯庫有新版可用";
                var msg = string.Format(
                    LocalizationManager.GetString("TranslationDbUpdateAvailableMsgFmt") ?? "從 {0} 更新到 {1}（tag {2}）",
                    string.IsNullOrEmpty(localShort) ? "—" : localShort, remoteShort, info.Tag);
                var linkText = LocalizationManager.GetString("TranslationDbUpdateLink") ?? "更新";

                var cfg = new AntdUI.Notification.Config(this, title, msg, AntdUI.TType.Info, AntdUI.TAlignFrom.BR)
                {
                    AutoClose = 0,   // 不自動關 — user 自己點連結或關
                };
                cfg.SetLink(linkText, () =>
                {
                    using var dlg = new UpdateTranslationDbDialog(info, _tagTranslator.HeadSha, _translationDbPath, _translationUpdater);
                    dlg.ShowDialog(this);
                    return true;   // SetLink return true → 點完關掉 notification
                });
                cfg.open();   // AntdUI extension method（小寫 open）
            }));
        }
        catch
        {
            // CheckLatestAsync 自身已 catch all，這裡是雙重保險（避免任何意外往 fire-and-forget 外漏）
        }
    }

    /// <summary>
    /// 通用狀態列訊息。重建任務的訊息由 UpdateStatusFromTracker 自動處理；
    /// 這個 API 留給其他臨時通知用。
    /// </summary>
    public void ShowStatus(string message)
    {
        if (statusBar.InvokeRequired)
        {
            statusBar.BeginInvoke(new Action(() => ShowStatus(message)));
            return;
        }
        statusLabel.Text = message;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        DraggableMouseDown();
        base.OnMouseDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 順序：先 dispose CacheWriter (drain queue + worker join)、再 dispose stores
        _cacheWriter?.Dispose();
        _metaStore?.Dispose();
        // FileSystemThumbStore 不需 dispose
        base.OnFormClosed(e);
    }

    private void ShowGallery()
    {
        archiveControl.Visible = false;
        metadataManageControl.Visible = false;
        galleryControl.Visible = true;
    }

    private void ShowArchive()
    {
        galleryControl.Visible = false;
        metadataManageControl.Visible = false;
        archiveControl.Visible = true;
    }

    private void ShowMetadataManage()
    {
        galleryControl.Visible = false;
        archiveControl.Visible = false;
        metadataManageControl.Visible = true;
        metadataManageControl.OnShown();
    }

    private void OpenSettings()
    {
        using var settings = new SettingsForm(_configManager, _config, _prefsManager, _prefs);
        if (settings.ShowDialog(this) == DialogResult.OK)
        {
            _config = settings.Config;
            _prefs = settings.Prefs;
            archiveControl.OnConfigUpdated(_config);
            galleryControl.OnConfigUpdated(_config);
            metadataManageControl.ExternalMetadataEnabled = _config.EnableExternalMetadata;
            galleryControl.OnPrefsUpdated(_prefs);
            AntdUI.Notification.info(this,
                LocalizationManager.GetString("SettingsSaved") ?? "設定已儲存",
                "",autoClose:3,align:TAlignFrom.TR);
        }
    }

    /// <summary>
    /// 一次性遷移：舊版的 Theme / Language 在 appsettings.json，搬到 preferences.json。
    /// preferences.json 若已存在表示已遷移過或新環境，跳過。
    /// </summary>
    private void MigrateLegacyPreferences()
    {
        var prefsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preferences.json");
        if (File.Exists(prefsPath)) return;
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("Theme", out var t)
                && Enum.TryParse<AppTheme>(t.ToString(), out var theme))
                _prefs.Theme = theme;
            if (doc.RootElement.TryGetProperty("Language", out var l))
                _prefs.Language = l.GetString() ?? _prefs.Language;
            _prefsManager.Save(_prefs);
        }
        catch { /* 遷移失敗就用預設值，不致命 */ }
    }

    public void ApplyThemeColors()
    {
        windowBar.BackColor = Core.ThemeColors.Primary;
        windowBar.ForeColor = Core.ThemeColors.HeaderText;
        menuStrip1.BackColor = Core.ThemeColors.Background;
        menuStrip1.ForeColor = Core.ThemeColors.Text;
        this.BackColor = Core.ThemeColors.Background;
        this.Refresh();
    }

    public void ApplyLocalization()
    {
        this.Text = LocalizationManager.GetString("AppTitle");
        mnuFile.Text = LocalizationManager.GetString("MenuFile"); // Need to ensure these keys exist or match
        mnuFileGallery.Text = LocalizationManager.GetString("MenuGallery");
        mnuFileArchive.Text = LocalizationManager.GetString("MenuArchive");
        mnuFileMetadata.Text = LocalizationManager.GetString("MenuMetadata") ?? "詮釋資料管理(&M)";
        mnuFileSettings.Text = LocalizationManager.GetString("MenuSettings");
        mnuFileRebuildCache.Text = LocalizationManager.GetString("MenuRebuildCache") ?? "重建索引資料庫(&R)...";
        mnuFileExit.Text = LocalizationManager.GetString("MenuExit");

        // 檢視模式選單已移除，改放每個 Tab header 內的 Segmented
    }

    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        const int WM_ENTERSIZEMOVE = 0x0231;
        const int WM_EXITSIZEMOVE = 0x0232;

        if (m.Msg == WM_ENTERSIZEMOVE)
        {
            galleryControl?.SuspendFlowLayout();
        }
        else if (m.Msg == WM_EXITSIZEMOVE)
        {
            galleryControl?.ResumeFlowLayout();
        }

        base.WndProc(ref m);
    }
}

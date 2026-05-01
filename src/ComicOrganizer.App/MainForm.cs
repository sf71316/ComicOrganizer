using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using ComicOrganizer.App.UI;

namespace ComicOrganizer.App;

public partial class MainForm : Window, IThemedControl
{
    private readonly ConfigManager _configManager;
    private AppConfig _config;
    private readonly MappingIntelligence _intelligence;
    private readonly ArchiveOrganizer _organizer;
    private readonly CoverManager _coverManager;

    // Cache 服務 (#44)
    private readonly IAssetMetaStore _metaStore;
    private readonly IThumbStore _thumbStore;
    private readonly CacheWriter _cacheWriter;
    private readonly CacheRebuildService _rebuildService;

    public MainForm()
    {
        // 先載 config 並初始化 ThemeManager → ThemeColors.X getter 才會回傳對的色，
        // InitializeComponent 內 Designer 設 BackColor = ThemeColors.X 取得正確色（不會被系統 DPI 鎖到 Dark）
        _configManager = new ConfigManager();
        _config = _configManager.Load();
        LocalizationManager.Initialize(_config.Language);
        ThemeManager.Initialize(_config.Theme);

        InitializeComponent();

        this.WireThemeAndLocalization();

        _intelligence = new MappingIntelligence();
        _organizer = new ArchiveOrganizer(_intelligence, _config);
        _coverManager = new CoverManager(_config);

        // Cache 服務：cache.db 與 thumbs/ 都放在 App exe 同層
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _metaStore = new SqliteAssetMetaStore(Path.Combine(baseDir, "cache.db"));
        _thumbStore = new FileSystemThumbStore(Path.Combine(baseDir, "thumbs"));
        _cacheWriter = new CacheWriter(_metaStore, _thumbStore);
        _coverManager.AttachCache(_metaStore, _thumbStore, _cacheWriter);
        _rebuildService = new CacheRebuildService(_config, _coverManager, _metaStore, _thumbStore);

        // 等 Form Load 後 Initialize（此時 handle 已建立、Invoke 才能用）
        this.Load += (s, e) =>
        {
            galleryControl.Initialize(_config, _coverManager);
            archiveControl.Initialize(_config, _intelligence, _organizer);
            ShowGallery();
        };

        WireMenuEvents();
    }

    private void WireMenuEvents()
    {
        mnuFileGallery.Click += (s, e) => ShowGallery();
        mnuFileArchive.Click += (s, e) => ShowArchive();
        mnuFileSettings.Click += (s, e) => OpenSettings();
        mnuFileRebuildCache.Click += (s, e) => OpenRebuildCacheDialog();
        mnuFileExit.Click += (s, e) => Close();

        // 檢視模式改放在每個 Tab header 內的 Segmented，per-tab 獨立。MainForm 不再 dispatch view mode
    }

    private void OpenRebuildCacheDialog()
    {
        using var form = new UI.RebuildCacheForm(_rebuildService, RunPartialRebuildBackground);
        form.ShowDialog(this);
    }

    private CancellationTokenSource? _statusCts;

    /// <summary>
    /// 部分重建：dialog 收到 user 選 subtree 後委派給此方法，跑背景非同步、進度顯示在 statusBar，
    /// User 仍可繼續操作 gallery。
    /// </summary>
    private async Task RunPartialRebuildBackground(string subtreePath)
    {
        // 取消前一次未結束的背景任務
        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        var ct = _statusCts.Token;

        statusBar.Visible = true;
        statusProgress.Value = 0f;
        statusLabel.Text = "重建中...";
        statusCancel.Click -= StatusCancelClickHandler;
        statusCancel.Click += StatusCancelClickHandler;

        var progress = new Progress<Core.CacheRebuildService.Progress>(p =>
        {
            statusProgress.Value = p.Total > 0 ? (float)p.Done / p.Total : 0f;
            statusLabel.Text = $"重建中 ({p.Done}/{p.Total})";
        });

        try
        {
            await _rebuildService.PartialRebuildAsync(subtreePath, progress, ct);
            statusLabel.Text = $"重建完成";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "已取消";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "失敗: " + ex.Message;
        }
        finally
        {
            // 5 秒後自動隱藏
            try { await Task.Delay(5000, CancellationToken.None); } catch { }
            statusBar.Visible = false;
        }
    }

    private void StatusCancelClickHandler(object? sender, EventArgs e)
    {
        _statusCts?.Cancel();
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
        galleryControl.Visible = true;
    }

    private void ShowArchive()
    {
        galleryControl.Visible = false;
        archiveControl.Visible = true;
    }

    private void OpenSettings()
    {
        using var settings = new SettingsForm(_configManager, _config);
        if (settings.ShowDialog(this) == DialogResult.OK)
        {
            _config = settings.Config;
            archiveControl.OnConfigUpdated(_config);
            galleryControl.OnConfigUpdated(_config);
            AntdUI.Notification.info(this, 
                LocalizationManager.GetString("SettingsSaved") ?? "設定已儲存",
                "",autoClose:3,align:TAlignFrom.TR);
        }
    }

    public void ApplyThemeColors()
    {
        windowBar.BackColor = Core.ThemeColors.Primary;
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
        mnuFileSettings.Text = LocalizationManager.GetString("MenuSettings");
        mnuFileRebuildCache.Text = LocalizationManager.GetString("MenuRebuildCache") ?? "重建索引資料庫(&R)...";
        mnuFileExit.Text = LocalizationManager.GetString("MenuExit");

        // 檢視模式選單已移除，改放每個 Tab header 內的 Segmented
    }
}

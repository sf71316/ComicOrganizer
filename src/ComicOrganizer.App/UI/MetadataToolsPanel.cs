using AntdUI;
using ComicOrganizer.App.Core;
using ComicOrganizer.App.Models;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

public partial class MetadataToolsPanel : UserControl, IThemedControl
{
    private IAssetMetaStore? _metaStore;
    private ReparseAllService? _reparseService;
    private RebuildProgressTracker? _tracker;
    private ConfigManager? _configManager;
    private AppConfig? _config;

    public MetadataToolsPanel()
    {
        InitializeComponent();
        WireEvents();
        this.WireThemeAndLocalization();
    }

    public void Initialize(IAssetMetaStore metaStore, ReparseAllService reparseService,
                           RebuildProgressTracker tracker,
                           ConfigManager configManager, AppConfig config)
    {
        _metaStore = metaStore;
        _reparseService = reparseService;
        _tracker = tracker;
        _configManager = configManager;
        _config = config;
        _tracker.StateChanged += OnTrackerStateChanged;
        LoadExternalSettings();
    }

    private void LoadExternalSettings()
    {
        if (_config == null) return;
        swEnableExternal.Checked = _config.EnableExternalMetadata;
        var key = _config.GetNhentaiApiKey();
        // 不顯示真實 key（password input），但顯示「已設定」提示
        txtExtKey.Text = "";
        txtExtKey.PlaceholderText = string.IsNullOrEmpty(key)
            ? (LocalizationManager.GetString("ExtKeyPlaceholderEmpty") ?? "nhk_...")
            : (LocalizationManager.GetString("ExtKeyPlaceholderSet") ?? "(已儲存 — 留空保持原值)");
    }

    public new void Refresh()
    {
        if (_metaStore == null) return;
        var stats = _metaStore.GetParseStats();
        var pctFmt = LocalizationManager.GetString("ToolsStatsParsedFmt") ?? "已 parse: {0} ({1:F1}%)";
        lblStatsTotal.Text = string.Format(LocalizationManager.GetString("ToolsStatsTotalFmt") ?? "作品總數: {0}", stats.TotalAssets);
        lblStatsParsed.Text = string.Format(pctFmt, stats.ParsedCount, stats.ParsedPercent);
        lblStatsUnparsed.Text = string.Format(LocalizationManager.GetString("ToolsStatsUnparsedFmt") ?? "未 parse: {0}", stats.UnparsedCount);
        lblStatsManual.Text = string.Format(LocalizationManager.GetString("ToolsStatsManualFmt") ?? "手動編輯保留 (重 parse 跳過): {0}", stats.ManuallyEditedCount);
        lblStatsAuto.Text = string.Format(LocalizationManager.GetString("ToolsStatsAutoFmt") ?? "可自動重 parse: {0}", stats.AutoParsedCount);
    }

    private void WireEvents()
    {
        btnRefreshStats.Click += (s, e) => Refresh();
        btnReparse.Click += async (s, e) => await ExecuteReparseAsync();
        btnCancel.Click += (s, e) => _tracker?.Cancel();

        swEnableExternal.CheckedChanged += (s, e) =>
        {
            if (_config == null || _configManager == null) return;
            _config.EnableExternalMetadata = swEnableExternal.Checked;
            _configManager.Save(_config);
        };
        btnSaveKey.Click += (s, e) => OnSaveKey();
        btnTestConnection.Click += async (s, e) => await OnTestConnection();
    }

    private void OnSaveKey()
    {
        if (_config == null || _configManager == null) return;
        var key = txtExtKey.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(key))
        {
            // 空字串 = 不變動（避免使用者載入頁面後不小心存空 key 清掉）
            return;
        }
        _config.SetNhentaiApiKey(key);
        _configManager.Save(_config);
        txtExtKey.Text = "";
        txtExtKey.PlaceholderText = LocalizationManager.GetString("ExtKeyPlaceholderSet") ?? "(已儲存 — 留空保持原值)";
        lblExtStatus.Text = LocalizationManager.GetString("ExtStatusSaved") ?? "已儲存（請按「測試連線」驗證）";
    }

    private async Task OnTestConnection()
    {
        if (_config == null) return;
        // txtExtKey 有輸入 → 用輸入的；否則用 config 已存的
        var inputKey = txtExtKey.Text?.Trim();
        var keyToUse = !string.IsNullOrEmpty(inputKey) ? inputKey : _config.GetNhentaiApiKey();
        if (string.IsNullOrEmpty(keyToUse))
        {
            lblExtStatus.Text = LocalizationManager.GetString("ExtStatusNoKey") ?? "尚未設定 API key";
            return;
        }

        btnTestConnection.Loading = true;
        lblExtStatus.Text = LocalizationManager.GetString("ExtStatusTesting") ?? "測試中…";
        try
        {
            using var client = new NhentaiApiClient(keyToUse);
            var result = await client.TestConnectionAsync();
            if (IsDisposed) return;
            if (result.Success)
            {
                lblExtStatus.Text = string.Format(
                    LocalizationManager.GetString("ExtStatusOkFmt") ?? "✓ 連線成功（user: {0}）",
                    result.UserName ?? "?");
            }
            else
            {
                lblExtStatus.Text = string.Format(
                    LocalizationManager.GetString("ExtStatusFailFmt") ?? "✗ 連線失敗：{0}",
                    result.ErrorMessage ?? "unknown");
            }
        }
        catch (Exception ex)
        {
            lblExtStatus.Text = string.Format(
                LocalizationManager.GetString("ExtStatusFailFmt") ?? "✗ 連線失敗：{0}",
                ex.Message);
        }
        finally
        {
            btnTestConnection.Loading = false;
        }
    }

    private async Task ExecuteReparseAsync()
    {
        if (_reparseService == null || _metaStore == null) return;
        if (_tracker?.IsRunning == true) return;

        var stats = _metaStore.GetParseStats();
        var form = this.FindForm();
        if (form == null) return;

        AntdUI.Modal.open(new AntdUI.Modal.Config(form,
            LocalizationManager.GetString("ToolsReparseConfirmTitle") ?? "整庫重新解析",
            string.Format(
                LocalizationManager.GetString("ToolsReparseConfirmMsgFmt") ??
                    "重新解析 {0} 件作品 metadata（{1} 件手動編輯會跳過）。閱讀記錄會保留。",
                stats.AutoParsedCount + stats.UnparsedCount, stats.ManuallyEditedCount),
            AntdUI.TType.Info)
        {
            OkText = LocalizationManager.GetString("ToolsReparseConfirmOk") ?? "開始重新解析",
            CancelText = LocalizationManager.GetString("DialogCancel") ?? "取消",
            OnOk = _ =>
            {
                var ignored = RunReparseAsync(form);
                return true;
            }
        });
    }

    private async Task RunReparseAsync(Form form)
    {
        var summary = await _reparseService!.RunAsync();
        if (IsDisposed) return;

        BeginInvoke(new Action(() =>
        {
            if (IsDisposed) return;
            Refresh();   // 跑完更新統計

            var msg = string.Format(
                LocalizationManager.GetString("ToolsReparseDoneFmt") ??
                    "重新解析完成。\n處理: {0} / 跳過: {1} / 失敗: {2}",
                summary.Reparsed, summary.Skipped, summary.Failed);
            if (summary.Cancelled)
                msg = (LocalizationManager.GetString("ToolsReparseCancelled") ?? "重新解析已取消") + "\n\n" + msg;

            AntdUI.Modal.open(new AntdUI.Modal.Config(form,
                LocalizationManager.GetString("ToolsReparseDoneTitle") ?? "整庫重新解析完成",
                msg,
                summary.Cancelled ? AntdUI.TType.Warn : AntdUI.TType.Success)
            { CancelText = null });
        }));
    }

    private void OnTrackerStateChanged(object? sender, EventArgs e)
    {
        if (_tracker == null) return;
        if (lblProgressStatus.InvokeRequired)
        {
            lblProgressStatus.BeginInvoke(new Action(() => OnTrackerStateChanged(sender, e)));
            return;
        }

        bool running = _tracker.IsRunning;
        var p = _tracker.Current;

        progressBar.Visible = running;
        progressBar.Value = (running && p.Total > 0) ? (float)p.Done / p.Total : 0f;

        if (running)
        {
            lblProgressStatus.Text = p.Total > 0
                ? $"{_tracker.Phase} ({p.Done}/{p.Total})"
                : _tracker.Phase;
            lblProgressCurrent.Text = p.CurrentPath.Length > 100 ? "..." + p.CurrentPath[^97..] : p.CurrentPath;
        }
        else
        {
            lblProgressStatus.Text = "";
            lblProgressCurrent.Text = "";
        }

        btnReparse.Enabled = !running;
        btnRefreshStats.Enabled = !running;
        btnCancel.Visible = running;
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        // AntdUI.Panel 用 Back（rounded 填色），不是 BackColor
        statsPanel.Back = ThemeColors.Background;
        reparsePanel.Back = ThemeColors.Background;
        externalSourcePanel.Back = ThemeColors.Background;
        lblExtTitle.ForeColor = ThemeColors.Text;
        lblExtDesc.ForeColor = ThemeColors.Text;
        lblExtEnable.ForeColor = ThemeColors.Text;
        lblExtKey.ForeColor = ThemeColors.Text;
        lblExtStatus.ForeColor = ThemeColors.Text;
        lblTitle.ForeColor = ThemeColors.Text;
        lblStatsTitle.ForeColor = ThemeColors.Text;
        lblReparseTitle.ForeColor = ThemeColors.Text;
        lblReparseDesc.ForeColor = ThemeColors.Text;
        lblStatsTotal.ForeColor = ThemeColors.Text;
        lblStatsParsed.ForeColor = ThemeColors.Text;
        lblStatsUnparsed.ForeColor = ThemeColors.Text;
        lblStatsManual.ForeColor = ThemeColors.Text;
        lblStatsAuto.ForeColor = ThemeColors.Primary;
        lblProgressStatus.ForeColor = ThemeColors.Text;
        lblProgressCurrent.ForeColor = ThemeColors.Text;
    }

    public void ApplyLocalization()
    {
        lblTitle.Text = LocalizationManager.GetString("ToolsTitle") ?? "詮釋資料工具";
        lblStatsTitle.Text = LocalizationManager.GetString("ToolsStatsTitle") ?? "解析統計";
        lblReparseTitle.Text = LocalizationManager.GetString("ToolsReparseTitle") ?? "整庫重新解析 metadata";
        lblReparseDesc.Text = LocalizationManager.GetString("ToolsReparseDesc") ??
            "從檔名重新解析所有未手動編輯的作品。閱讀記錄（讀取次數 / 上次閱讀 / 上次頁碼）會保留。已標記為「手動編輯」的作品會被跳過。";
        btnRefreshStats.Text = LocalizationManager.GetString("TagManageRefresh") ?? "重新整理";
        btnReparse.Text = LocalizationManager.GetString("ToolsReparseButton") ?? "整庫重新解析";
        btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";

        lblExtTitle.Text = LocalizationManager.GetString("ExtTitle") ?? "外部 metadata 來源 (nhentai)";
        lblExtDesc.Text = LocalizationManager.GetString("ExtDesc") ?? "啟用後可從 nhentai API 拉取作品 metadata。API key 用 Windows DPAPI 加密儲存（per-user，跨機器拷貝 config 解不開）。";
        lblExtEnable.Text = LocalizationManager.GetString("ExtEnableLabel") ?? "啟用：";
        lblExtKey.Text = LocalizationManager.GetString("ExtKeyLabel") ?? "API Key:";
        btnSaveKey.Text = LocalizationManager.GetString("ExtSaveKey") ?? "儲存 Key";
        btnTestConnection.Text = LocalizationManager.GetString("ExtTestConnection") ?? "測試連線";
    }
}

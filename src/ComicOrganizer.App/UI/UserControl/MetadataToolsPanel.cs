using AntdUI;
using ComicOrganizer.App.Core;
using System.Windows.Forms;

namespace ComicOrganizer.App.UI;

public partial class MetadataToolsPanel : UserControl, IThemedControl
{
    private IAssetMetaStore? _metaStore;
    private ReparseAllService? _reparseService;
    private RebuildProgressTracker? _tracker;
    private TagTranslator? _tagTranslator;
    private TagTranslationUpdater? _updater;
    private string? _translationDbPath;

    public MetadataToolsPanel()
    {
        InitializeComponent();
        WireEvents();
        this.WireThemeAndLocalization();
    }

    public void Initialize(IAssetMetaStore metaStore, ReparseAllService reparseService,
                           RebuildProgressTracker tracker)
    {
        _metaStore = metaStore;
        _reparseService = reparseService;
        _tracker = tracker;
        _tracker.StateChanged += OnTrackerStateChanged;
    }

    /// <summary>翻譯庫區塊用 — 由 host 注入 translator + 更新服務 + disk 寫入路徑。
    /// 不在 Initialize 是為了 keep 既有 caller 不需改 signature；新功能拆方法注入。</summary>
    public void InitializeTranslationDb(TagTranslator translator, TagTranslationUpdater updater, string diskPath)
    {
        _tagTranslator = translator;
        _updater = updater;
        _translationDbPath = diskPath;
        RefreshTranslationDbInfo();
    }

    private void RefreshTranslationDbInfo()
    {
        if (_tagTranslator == null) return;
        var sha = _tagTranslator.HeadSha;
        var shaShort = string.IsNullOrEmpty(sha) ? "—" : (sha.Length > 7 ? sha[..7] : sha);

        lblTranslationVersion.Text = string.Format(
            LocalizationManager.GetString("TranslationDbVersionFmt") ?? "目前版本：{0}",
            shaShort);
        lblTranslationCount.Text = string.Format(
            LocalizationManager.GetString("TranslationDbCountFmt") ?? "條目：{0}",
            _tagTranslator.Count);

        var srcKey = _tagTranslator.Source == TagTranslationDbSource.DiskCache
            ? "TranslationDbSourceDiskCache" : "TranslationDbSourceEmbedded";
        var srcDefault = _tagTranslator.Source == TagTranslationDbSource.DiskCache
            ? "已下載" : "bundled";
        lblTranslationSource.Text = string.Format(
            LocalizationManager.GetString("TranslationDbSourceFmt") ?? "來源：{0}",
            LocalizationManager.GetString(srcKey) ?? srcDefault);
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
        btnCheckUpdate.Click += async (s, e) => await CheckUpdateClicked();
    }

    /// <summary>使用者主動點「立即檢查更新」 — 繞過 24h 限制，網路錯也要顯示提示（背景檢查才靜默）。</summary>
    private async Task CheckUpdateClicked()
    {
        if (_updater == null || _tagTranslator == null || _translationDbPath == null) return;
        var form = this.FindForm();
        if (form == null) return;

        btnCheckUpdate.Loading = true;
        try
        {
            var info = await _updater.CheckLatestAsync();
            if (info == null)
            {
                AntdUI.Notification.warn(form,
                    LocalizationManager.GetString("TranslationDbCheckFailedTitle") ?? "檢查更新失敗",
                    LocalizationManager.GetString("TranslationDbCheckFailedMsg") ?? "無法連到 GitHub，請檢查網路後再試。",
                    autoClose: 4, align: AntdUI.TAlignFrom.TR);
                return;
            }

            var local = _tagTranslator.HeadSha;
            if (!TagTranslationUpdater.IsUpdateAvailable(local, info.Sha))
            {
                AntdUI.Notification.success(form,
                    LocalizationManager.GetString("TranslationDbUpToDateTitle") ?? "已是最新版本",
                    string.Format(
                        LocalizationManager.GetString("TranslationDbUpToDateMsg") ?? "目前 {0} 為最新（tag {1}）",
                        local.Length > 7 ? local[..7] : local, info.Tag),
                    autoClose: 3, align: AntdUI.TAlignFrom.TR);
                return;
            }

            using var dlg = new UpdateTranslationDbDialog(info, local, _translationDbPath, _updater);
            dlg.ShowDialog(form);
            if (dlg.IsCompletedSuccessfully)
            {
                // 下載完成 → 提示重啟（dialog 內已有訊息，這裡再 toast 一次提醒）
                AntdUI.Notification.success(form,
                    LocalizationManager.GetString("TranslationDbDownloadDoneTitle") ?? "下載完成",
                    LocalizationManager.GetString("TranslationDbRestartToApply") ?? "重啟 app 後生效",
                    autoClose: 5, align: AntdUI.TAlignFrom.TR);
            }
        }
        finally
        {
            btnCheckUpdate.Loading = false;
        }
    }

    private async Task ExecuteReparseAsync()
    {
        if (_reparseService == null || _metaStore == null) return;
        if (_tracker?.IsRunning == true) return;

        var stats = _metaStore.GetParseStats();
        var form = this.FindForm();
        if (form == null) return;

        UI.ModalEx.Open(new AntdUI.Modal.Config(form,
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

            UI.ModalEx.Open(new AntdUI.Modal.Config(form,
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
        translationPanel.Back = ThemeColors.Background;
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
        lblTranslationTitle.ForeColor = ThemeColors.Text;
        lblTranslationVersion.ForeColor = ThemeColors.Text;
        lblTranslationCount.ForeColor = ThemeColors.Text;
        lblTranslationSource.ForeColor = ThemeColors.Text;
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
        lblTranslationTitle.Text = LocalizationManager.GetString("TranslationDbTitle") ?? "翻譯庫";
        btnCheckUpdate.Text = LocalizationManager.GetString("TranslationDbCheckUpdate") ?? "立即檢查更新";
        // 版本 / 條目 / 來源 三行依當前 _tagTranslator 狀態重新格式化
        if (_tagTranslator != null) RefreshTranslationDbInfo();
    }
}

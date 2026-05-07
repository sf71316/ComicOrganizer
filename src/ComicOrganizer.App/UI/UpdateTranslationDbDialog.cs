using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 翻譯庫線上更新 dialog（#53 Phase 5-C）。
///
/// 三個 UI state：
///   Initial    — 顯示 local sha vs remote sha + 「下載」/「取消」
///   Downloading — 顯示進度條（流式 IProgress&lt;double&gt;），按鈕變「取消下載」
///   Done       — 顯示「下載完成，重啟 app 後生效」 + 「關閉」
///
/// 下載成功後 caller（MetadataToolsPanel / MainForm）可讀 IsCompletedSuccessfully 決定是否更新 LastTranslationDbCheckTime。
/// 不會試著熱換 _tagTranslator — 重啟才生效，避免 race condition。
/// </summary>
public class UpdateTranslationDbDialog : AntdUI.Window, IThemedControl
{
    private readonly AntdUI.Label lblHeader;
    private readonly AntdUI.Label lblVersionInfo;
    private readonly AntdUI.Label lblSizeInfo;
    private readonly AntdUI.Label lblStatus;
    private readonly AntdUI.Progress progress;
    private readonly AntdUI.Button btnDownload;
    private readonly AntdUI.Button btnCancel;

    private readonly LatestReleaseInfo _info;
    private readonly string _localSha;
    private readonly string _destPath;
    private readonly TagTranslationUpdater _updater;
    private readonly CancellationTokenSource _cts = new();

    private enum State { Initial, Downloading, Done, Failed }
    private State _state = State.Initial;

    /// <summary>下載成功才會 true（給 caller 決定是否更新時間戳）。</summary>
    public bool IsCompletedSuccessfully { get; private set; }

    public UpdateTranslationDbDialog(LatestReleaseInfo info, string localSha,
        string destPath, TagTranslationUpdater updater)
    {
        _info = info;
        _localSha = localSha ?? "";
        _destPath = destPath;
        _updater = updater;

        Text = LocalizationManager.GetString("TranslationDbUpdateDialogTitle") ?? "更新翻譯庫";
        Size = new Size(480, 280);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var content = new System.Windows.Forms.Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 20, 20, 16),
        };
        Controls.Add(content);

        lblHeader = new AntdUI.Label
        {
            Text = LocalizationManager.GetString("TranslationDbUpdateAvailable") ?? "翻譯庫有新版可用",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Microsoft JhengHei UI", 12F, FontStyle.Bold),
        };

        var localShort = string.IsNullOrEmpty(_localSha) ? "—" : _localSha.Length > 7 ? _localSha[..7] : _localSha;
        var remoteShort = info.Sha.Length > 7 ? info.Sha[..7] : info.Sha;
        lblVersionInfo = new AntdUI.Label
        {
            Text = string.Format(
                LocalizationManager.GetString("TranslationDbVersionDiffFmt") ?? "目前 {0} → 最新 {1}（{2}）",
                localShort, remoteShort, info.Tag),
            Dock = DockStyle.Top,
            Height = 24,
            Margin = new Padding(0, 12, 0, 0),
        };

        var sizeMb = info.DbSizeBytes / 1024.0 / 1024.0;
        lblSizeInfo = new AntdUI.Label
        {
            Text = string.Format(
                LocalizationManager.GetString("TranslationDbSizeFmt") ?? "下載大小：{0:F1} MB",
                sizeMb),
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 6, 0, 0),
        };

        progress = new AntdUI.Progress
        {
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 16, 0, 0),
            Value = 0f,
            Visible = false,
        };

        lblStatus = new AntdUI.Label
        {
            Text = "",
            Dock = DockStyle.Top,
            Height = 22,
            Margin = new Padding(0, 8, 0, 0),
        };

        var btnRow = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(0, 8, 0, 0),
        };

        btnCancel = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("DialogCancel") ?? "取消",
            Width = 88, Height = 32,
        };
        btnCancel.Click += (s, e) => OnCancelOrClose();

        btnDownload = new AntdUI.Button
        {
            Text = LocalizationManager.GetString("TranslationDbDownload") ?? "下載",
            Type = AntdUI.TTypeMini.Primary,
            IconSvg = "DownloadOutlined",
            Width = 100, Height = 32,
        };
        btnDownload.Click += async (s, e) => await OnDownloadClicked();

        btnRow.Controls.Add(btnDownload);
        btnRow.Controls.Add(btnCancel);

        content.Controls.Add(lblStatus);
        content.Controls.Add(progress);
        content.Controls.Add(lblSizeInfo);
        content.Controls.Add(lblVersionInfo);
        content.Controls.Add(lblHeader);
        content.Controls.Add(btnRow);

        this.WireThemeAndLocalization();
    }

    private async Task OnDownloadClicked()
    {
        if (_state != State.Initial) return;
        _state = State.Downloading;
        btnDownload.Visible = false;
        btnCancel.Text = LocalizationManager.GetString("TranslationDbDownloadCancel") ?? "取消下載";
        progress.Visible = true;
        lblStatus.Text = LocalizationManager.GetString("TranslationDbDownloading") ?? "下載中…";

        var prog = new Progress<double>(v =>
        {
            if (IsDisposed) return;
            progress.Value = (float)v;
        });

        bool ok;
        try
        {
            ok = await _updater.DownloadAsync(_info.DbAssetUrl, _destPath, prog, _cts.Token);
        }
        catch (OperationCanceledException) { ok = false; }
        catch { ok = false; }

        if (IsDisposed) return;

        if (ok)
        {
            IsCompletedSuccessfully = true;
            _state = State.Done;
            progress.Value = 1f;
            lblStatus.Text = LocalizationManager.GetString("TranslationDbDownloadDone") ?? "下載完成，重啟 app 後生效";
            btnCancel.Text = LocalizationManager.GetString("DialogClose") ?? "關閉";
            btnCancel.Type = AntdUI.TTypeMini.Primary;
        }
        else
        {
            _state = State.Failed;
            lblStatus.Text = _cts.IsCancellationRequested
                ? (LocalizationManager.GetString("TranslationDbDownloadCancelled") ?? "已取消")
                : (LocalizationManager.GetString("TranslationDbDownloadFailed") ?? "下載失敗（網路？）");
            btnCancel.Text = LocalizationManager.GetString("DialogClose") ?? "關閉";
            progress.Visible = false;
        }
    }

    private void OnCancelOrClose()
    {
        if (_state == State.Downloading)
        {
            _cts.Cancel();
            // 等 DownloadAsync 在 catch OperationCanceledException 後切到 Failed state，state 切完才允許 close
            return;
        }
        Close();
    }

    public void ApplyThemeColors()
    {
        BackColor = ThemeColors.Surface;
        lblHeader.ForeColor = ThemeColors.Text;
        lblVersionInfo.ForeColor = ThemeColors.Text;
        lblSizeInfo.ForeColor = ThemeColors.Text;
        lblStatus.ForeColor = ThemeColors.Text;
    }

    public void ApplyLocalization()
    {
        Text = LocalizationManager.GetString("TranslationDbUpdateDialogTitle") ?? "更新翻譯庫";
        // 內容 label 的格式字串已在 ctor 套用，不重新格式化（避免覆蓋 download progress 時改字）
        if (_state == State.Initial)
        {
            btnDownload.Text = LocalizationManager.GetString("TranslationDbDownload") ?? "下載";
            btnCancel.Text = LocalizationManager.GetString("DialogCancel") ?? "取消";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts.Dispose();
        base.Dispose(disposing);
    }
}

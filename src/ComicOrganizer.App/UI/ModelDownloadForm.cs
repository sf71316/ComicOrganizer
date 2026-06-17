using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 按需下載 comic-text-detector.onnx 的進度對話框。純程式碼建構（避開 AntdUI Designer 坑），
/// 套用 ThemeColors。成功 → <see cref="DialogResult.OK"/>；取消/未重試成功 → Cancel。
/// </summary>
public sealed class ModelDownloadForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _status;
    private readonly Label _info;
    private readonly Button _action;        // 進行中=「取消」；失敗=「重試」；取消後=「關閉」
    private CancellationTokenSource? _cts;
    private bool _downloading;

    public ModelDownloadForm()
    {
        Text = "下載精準抹除模型";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 172);
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.Text;

        _info = new Label
        {
            Text = "comic-text-detector.onnx（約 95 MB）\n來源：HuggingFace · 訓練資料 Manga109-s，僅供個人使用。",
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(408, 44),
            ForeColor = ThemeColors.Text,
        };
        _bar = new ProgressBar
        {
            Location = new Point(16, 66),
            Size = new Size(408, 22),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
        };
        _status = new Label
        {
            Text = "準備下載…",
            AutoSize = false,
            Location = new Point(16, 94),
            Size = new Size(408, 24),
            ForeColor = ThemeColors.Text,
        };
        _action = new Button
        {
            Text = "取消",
            Location = new Point(336, 128),
            Size = new Size(88, 30),
            ForeColor = ThemeColors.Text,
            FlatStyle = FlatStyle.Flat,
        };
        _action.FlatAppearance.BorderColor = ThemeColors.Border;
        _action.Click += OnActionClick;

        Controls.Add(_info);
        Controls.Add(_bar);
        Controls.Add(_status);
        Controls.Add(_action);

        Shown += async (_, _) => await StartAsync();
        FormClosing += (_, _) => _cts?.Cancel();
    }

    private async Task StartAsync()
    {
        _downloading = true;
        _action.Text = "取消";
        _status.Text = "下載中…";
        _bar.Value = 0;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        // Progress 在 UI 執行緒建立 → 回呼自動 marshal 回 UI 執行緒
        var progress = new Progress<(long downloaded, long total)>(p =>
        {
            if (IsDisposed) return;
            if (p.total > 0)
            {
                int pct = (int)Math.Clamp(p.downloaded * 100 / p.total, 0, 100);
                _bar.Value = pct;
                _status.Text = $"下載中… {p.downloaded / 1048576.0:F1} / {p.total / 1048576.0:F1} MB（{pct}%）";
            }
            else
            {
                _status.Text = $"下載中… {p.downloaded / 1048576.0:F1} MB";
            }
        });

        DownloadResult result;
        try
        {
            result = await ModelDownloader.DownloadComicTextDetectorAsync(progress, _cts.Token);
        }
        catch (Exception ex)
        {
            result = new DownloadResult(DownloadOutcome.NetworkError, ex.Message);
        }

        _downloading = false;
        if (IsDisposed) return;     // 下載中被關閉 → 不再碰已釋放控制項

        if (result.Ok)
        {
            _bar.Value = 100;
            _status.Text = "下載完成。";
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        // 失敗/取消 → 顯示訊息；取消可關閉、其餘可重試
        _status.Text = result.Message ?? "下載失敗。";
        _action.Text = result.Outcome == DownloadOutcome.Cancelled ? "關閉" : "重試";
    }

    private async void OnActionClick(object? sender, EventArgs e)
    {
        if (_downloading)
        {
            _cts?.Cancel();         // 進行中 → 取消
            return;
        }
        if (_action.Text == "重試")
        {
            await StartAsync();
            return;
        }
        DialogResult = DialogResult.Cancel;   // 「關閉」
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cts?.Dispose();
        base.Dispose(disposing);
    }
}

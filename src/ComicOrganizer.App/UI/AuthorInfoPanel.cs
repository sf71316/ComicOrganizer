using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 作者資訊側邊面板。GalleryControl 右側 docked 顯示，內容由 IAssetMetaStore.GetAuthorInfo 提供。
/// 顯示：主名 + 別名 / 閱讀統計 / top tags / recent reads（含閱讀次數）。
/// 開啟由 GalleryControl.ShowAuthorInfo 觸發；關閉透過 CloseRequested 事件。
/// </summary>
public partial class AuthorInfoPanel : UserControl
{
    public event Action? CloseRequested;

    /// <summary>使用者在 RecentlyRead / AllWorks 清單按下某個作品 — string 為 work path。</summary>
    public event Action<string>? RequestOpenWork;

    public AuthorInfoPanel()
    {
        InitializeComponent();

        BackColor = ThemeColors.Surface;
        ForeColor = ThemeColors.Text;
        headerPanel.BackColor = ThemeColors.TabHeader;
        contentPanel.BackColor = ThemeColors.Surface;
        lblAliases.ForeColor = ThemeColors.Border;     // 次要色
        lblFolderPath.ForeColor = ThemeColors.Border;  // 次要色

        btnClose.Click += (s, e) => CloseRequested?.Invoke();

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        lblTitle.Text = LocalizationManager.GetString("AuthorInfoTitle") ?? "作者資訊";
        lblTagsHeader.Text = LocalizationManager.GetString("AuthorInfoTopTags") ?? "主要標籤";
        lblRecentHeader.Text = LocalizationManager.GetString("AuthorInfoRecentRead") ?? "最近閱讀";
        lblAllWorksHeader.Text = LocalizationManager.GetString("AuthorInfoAllWorks") ?? "全部作品";
    }

    /// <summary>套用聚合資料 — 顯示完整 panel 內容。</summary>
    public void ShowInfo(AuthorInfo info)
    {
        lblEmpty.Visible = false;
        contentPanel.Visible = true;
        headerPanel.Visible = true;

        lblMainName.Text = info.Author.MainName;

        if (!string.IsNullOrWhiteSpace(info.Author.FolderPath))
        {
            lblFolderPath.Text = "📁 " + info.Author.FolderPath;
            lblFolderPath.Visible = true;
        }
        else
        {
            lblFolderPath.Text = "";
            lblFolderPath.Visible = false;
        }

        if (info.Aliases.Count > 0)
        {
            var aliasLabel = LocalizationManager.GetString("AuthorInfoAlias") ?? "別名";
            var aliasParts = info.Aliases.Select(a =>
                string.IsNullOrWhiteSpace(a.Lang) ? a.Name : $"{a.Name} ({a.Lang})");
            lblAliases.Text = $"{aliasLabel}: " + string.Join(", ", aliasParts);
            lblAliases.Visible = true;
        }
        else
        {
            lblAliases.Text = "";
            lblAliases.Visible = false;
        }

        lblStats.Text = FormatStats(info);

        // tags
        flowTags.SuspendLayout();
        flowTags.Controls.Clear();
        foreach (var tag in info.TopTags)
        {
            var chip = new AntdUI.Tag
            {
                Text = $"{tag.Name}  {tag.Count}",
                Margin = new Padding(0, 0, 4, 4),
                Font = new Font("Microsoft JhengHei UI", 8.5F),
            };
            flowTags.Controls.Add(chip);
        }
        flowTags.ResumeLayout(true);

        // recent reads
        flowRecent.SuspendLayout();
        flowRecent.Controls.Clear();
        if (info.RecentlyRead.Count == 0)
        {
            var empty = new Label
            {
                AutoSize = true,
                Text = "(尚未開始閱讀)",
                ForeColor = ThemeColors.Border,
                Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Italic),
            };
            flowRecent.Controls.Add(empty);
        }
        else
        {
            foreach (var rr in info.RecentlyRead)
            {
                flowRecent.Controls.Add(BuildRecentItem(rr));
            }
        }
        flowRecent.ResumeLayout(true);

        // all works
        flowAllWorks.SuspendLayout();
        flowAllWorks.Controls.Clear();
        if (info.AllWorks.Count == 0)
        {
            var empty = new Label
            {
                AutoSize = true,
                Text = "(無)",
                ForeColor = ThemeColors.Border,
                Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Italic),
            };
            flowAllWorks.Controls.Add(empty);
        }
        else
        {
            foreach (var w in info.AllWorks)
            {
                flowAllWorks.Controls.Add(BuildAllWorkItem(w));
            }
        }
        flowAllWorks.ResumeLayout(true);
    }

    /// <summary>顯示「無資料」訊息（path 解析不到 author / GetAuthorInfo 回 null）。</summary>
    public void ShowEmpty(string message)
    {
        contentPanel.Visible = false;
        lblEmpty.Text = message;
        lblEmpty.Visible = true;
        lblEmpty.BringToFront();
        // header 仍顯示（X 鈕能用）
        headerPanel.Visible = true;
        headerPanel.BringToFront();
    }

    private string FormatStats(AuthorInfo info)
    {
        var workCount = LocalizationManager.GetString("AuthorInfoWorkCount") ?? "作品數";
        var unread = LocalizationManager.GetString("AuthorInfoUnread") ?? "未讀";
        var reading = LocalizationManager.GetString("AuthorInfoReading") ?? "進行中";
        var completed = LocalizationManager.GetString("AuthorInfoCompleted") ?? "已完成";
        var dropped = LocalizationManager.GetString("AuthorInfoDropped") ?? "放棄";
        var totalReads = LocalizationManager.GetString("AuthorInfoTotalReads") ?? "累計閱讀";
        return
            $"{workCount}: {info.TotalWorks}    {totalReads}: {info.TotalReadCount}\n" +
            $"{unread}: {info.UnreadCount}    {reading}: {info.ReadingCount}\n" +
            $"{completed}: {info.CompletedCount}    {dropped}: {info.DroppedCount}";
    }

    private Label BuildRecentItem(RecentRead rr)
    {
        var displayTitle = !string.IsNullOrWhiteSpace(rr.Title) ? rr.Title : Path.GetFileName(rr.Path);
        var readCountFmt = LocalizationManager.GetString("AuthorInfoReadCount") ?? "讀 {0} 次";
        var readCountStr = string.Format(readCountFmt, rr.ReadCount);
        var timeStr = FormatRelativeTime(rr.LastRead);
        var lbl = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft JhengHei UI", 9F),
            Margin = new Padding(0, 0, 0, 4),
            Text = $"• {displayTitle}\n  {readCountStr}  ({timeStr})",
        };
        AttachClickToOpen(lbl, rr.Path);
        return lbl;
    }

    private Label BuildAllWorkItem(AssetMetadataView w)
    {
        var displayTitle = !string.IsNullOrWhiteSpace(w.Title) ? w.Title : Path.GetFileName(w.Path);
        // 副資訊：language / translation_group / page_count（有什麼顯什麼）
        var hints = new List<string>();
        if (!string.IsNullOrWhiteSpace(w.Language)) hints.Add(w.Language);
        if (!string.IsNullOrWhiteSpace(w.TranslationGroup)) hints.Add(w.TranslationGroup);
        if (w.PageCount.HasValue) hints.Add($"{w.PageCount}p");
        var hintStr = hints.Count > 0 ? "  " + string.Join(" · ", hints) : "";
        var lbl = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(280, 0),
            Font = new Font("Microsoft JhengHei UI", 9F),
            Margin = new Padding(0, 0, 0, 2),
            Text = $"• {displayTitle}{hintStr}",
        };
        AttachClickToOpen(lbl, w.Path);
        return lbl;
    }

    private void AttachClickToOpen(Label lbl, string path)
    {
        var defaultColor = ForeColor;
        var hoverColor = ThemeColors.Primary;
        lbl.Cursor = Cursors.Hand;
        lbl.ForeColor = defaultColor;
        lbl.MouseEnter += (s, e) => lbl.ForeColor = hoverColor;
        lbl.MouseLeave += (s, e) => lbl.ForeColor = defaultColor;
        lbl.Click += (s, e) => RequestOpenWork?.Invoke(path);
    }

    private static string FormatRelativeTime(long? unixTs)
    {
        if (unixTs == null) return "未知";
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixTs.Value);
        var diff = DateTimeOffset.UtcNow - dt;
        if (diff.TotalDays >= 30) return dt.LocalDateTime.ToString("yyyy-MM-dd");
        if (diff.TotalDays >= 1) return $"{(int)diff.TotalDays} 天前";
        if (diff.TotalHours >= 1) return $"{(int)diff.TotalHours} 小時前";
        if (diff.TotalMinutes >= 1) return $"{(int)diff.TotalMinutes} 分前";
        return "剛剛";
    }
}

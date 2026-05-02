using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 作品詳細資訊面板。Drawer 內顯示，內容由 IAssetMetaStore.GetWorkDetail 提供。
/// 顯示：cover / 標題 / author 連結 / metadata / 閱讀記錄 / tags / 檔案資訊 / 操作按鈕。
/// </summary>
public partial class WorkDetailPanel : UserControl
{
    public event Action? CloseRequested;

    /// <summary>使用者按 Author 連結 — 切到 AuthorInfoPanel（host 處理）。</summary>
    public event Action<long>? RequestOpenAuthor;

    /// <summary>使用者按「閱讀」按鈕或 cover — 開外部 viewer + RecordRead。</summary>
    public event Action<string>? RequestOpenViewer;

    /// <summary>使用者按「開啟位置」— Explorer 定位。</summary>
    public event Action<string>? RequestExplorer;

    private string _currentPath = "";

    public WorkDetailPanel()
    {
        InitializeComponent();

        BackColor = ThemeColors.Surface;
        ForeColor = ThemeColors.Text;
        headerPanel.BackColor = ThemeColors.TabHeader;
        contentPanel.BackColor = ThemeColors.Surface;
        coverPanel.BackColor = ThemeColors.Surface;
        picCover.BackColor = ThemeColors.TabHeader;
        lnkAuthor.LinkColor = ThemeColors.Primary;
        lnkAuthor.ActiveLinkColor = ThemeColors.Primary;
        lblFileInfo.ForeColor = ThemeColors.Border;

        btnClose.Click += (s, e) => CloseRequested?.Invoke();
        btnRead.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_currentPath)) RequestOpenViewer?.Invoke(_currentPath);
        };
        btnExplorer.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_currentPath)) RequestExplorer?.Invoke(_currentPath);
        };
        picCover.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_currentPath)) RequestOpenViewer?.Invoke(_currentPath);
        };

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        lblTitle.Text = LocalizationManager.GetString("WorkDetailTitle") ?? "作品詳細資訊";
        lblReadStatsHeader.Text = LocalizationManager.GetString("WorkDetailReadStats") ?? "閱讀記錄";
        lblTagsHeader.Text = LocalizationManager.GetString("WorkDetailTags") ?? "標籤";
        lblFileInfoHeader.Text = LocalizationManager.GetString("WorkDetailFileInfo") ?? "檔案資訊";
        btnRead.Text = LocalizationManager.GetString("WorkDetailRead") ?? "閱讀";
        btnExplorer.Text = LocalizationManager.GetString("WorkDetailExplorer") ?? "開啟位置";
    }

    /// <summary>套用詳細資訊。cover 由 caller 預先載入後傳入（沒就 null）。</summary>
    public void Show(WorkDetail detail, Image? cover)
    {
        _currentPath = detail.Path;
        lblEmpty.Visible = false;
        contentPanel.Visible = true;

        // cover
        var oldCover = picCover.Image;
        picCover.Image = cover;
        if (oldCover != null && !ReferenceEquals(oldCover, cover)) oldCover.Dispose();

        // title
        lblWorkTitle.Text = !string.IsNullOrWhiteSpace(detail.Title)
            ? detail.Title
            : Path.GetFileName(detail.Path);

        // author 連結
        if (!string.IsNullOrWhiteSpace(detail.AuthorMainName) && detail.AuthorId.HasValue)
        {
            var authorLabel = LocalizationManager.GetString("WorkDetailAuthor") ?? "作者";
            var linkText = $"{authorLabel}: {detail.AuthorMainName}";
            lnkAuthor.Text = linkText;
            lnkAuthor.LinkArea = new LinkArea(authorLabel.Length + 2, detail.AuthorMainName.Length);
            lnkAuthor.Tag = detail.AuthorId.Value;
            lnkAuthor.LinkClicked -= OnAuthorLinkClicked;
            lnkAuthor.LinkClicked += OnAuthorLinkClicked;
            lnkAuthor.Visible = true;
        }
        else
        {
            lnkAuthor.Text = "";
            lnkAuthor.Visible = false;
        }

        // metadata grid
        lblMetaInfo.Text = FormatMetaInfo(detail);

        // 閱讀記錄
        lblReadStats.Text = FormatReadStats(detail);

        // tags
        flowTags.SuspendLayout();
        flowTags.Controls.Clear();
        if (detail.Tags.Count == 0)
        {
            flowTags.Controls.Add(BuildEmptyHint("(無標籤)"));
        }
        else
        {
            foreach (var t in detail.Tags)
            {
                flowTags.Controls.Add(new AntdUI.Tag
                {
                    Text = t.Name,
                    Margin = new Padding(0, 0, 4, 4),
                    Font = new Font("Microsoft JhengHei UI", 8.5F),
                });
            }
        }
        flowTags.ResumeLayout(true);

        // 檔案資訊
        lblFileInfo.Text = FormatFileInfo(detail);
    }

    /// <summary>後續 async 載完 cover 後呼叫，僅替換圖片不重 build 整個 panel。</summary>
    public void SetCover(Image? cover)
    {
        var old = picCover.Image;
        picCover.Image = cover;
        if (old != null && !ReferenceEquals(old, cover)) old.Dispose();
    }

    /// <summary>顯示「無資料」訊息。</summary>
    public void ShowEmpty(string message)
    {
        contentPanel.Visible = false;
        lblEmpty.Text = message;
        lblEmpty.Visible = true;
        lblEmpty.BringToFront();
    }

    private void OnAuthorLinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        if (lnkAuthor.Tag is long authorId) RequestOpenAuthor?.Invoke(authorId);
    }

    private static Label BuildEmptyHint(string text) => new Label
    {
        AutoSize = true,
        Text = text,
        ForeColor = ThemeColors.Border,
        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Italic),
    };

    private static string FormatMetaInfo(WorkDetail d)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Series))
        {
            var v = d.Series + (string.IsNullOrWhiteSpace(d.ChapterNo) ? "" : "  " + d.ChapterNo);
            lines.Add($"系列: {v}");
        }
        if (!string.IsNullOrWhiteSpace(d.Language))
            lines.Add($"語言: {d.Language}");
        if (!string.IsNullOrWhiteSpace(d.TranslationGroup))
            lines.Add($"翻譯組: {d.TranslationGroup}");
        if (d.PageCount.HasValue)
            lines.Add($"頁數: {d.PageCount} p");
        if (d.Rating.HasValue)
            lines.Add($"評分: {new string('★', d.Rating.Value)}{new string('☆', Math.Max(0, 5 - d.Rating.Value))}");
        if (!string.IsNullOrWhiteSpace(d.ExternalId))
            lines.Add($"外部 ID: {d.ExternalId}");
        return lines.Count == 0 ? "(無詳細 metadata)" : string.Join("\n", lines);
    }

    private static string FormatReadStats(WorkDetail d)
    {
        var statusName = d.ReadStatus switch
        {
            ReadStatus.Unread => "📭 未讀",
            ReadStatus.Reading => "📖 進行中",
            ReadStatus.Completed => "✅ 已完成",
            ReadStatus.Dropped => "⏹ 放棄",
            _ => "?"
        };
        var lines = new List<string>
        {
            $"狀態: {statusName}",
            $"閱讀次數: {d.ReadCount}",
        };
        if (d.LastRead.HasValue)
            lines.Add($"最後閱讀: {FormatRelativeTime(d.LastRead.Value)}");
        else
            lines.Add("最後閱讀: -");
        return string.Join("\n", lines);
    }

    private static string FormatFileInfo(WorkDetail d)
    {
        var sizeStr = FormatFileSize(d.FileSize);
        var mtimeStr = d.Mtime > 0
            ? DateTimeOffset.FromUnixTimeSeconds(d.Mtime).LocalDateTime.ToString("yyyy-MM-dd")
            : "-";
        var parsedStr = d.ParsedAt.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(d.ParsedAt.Value).LocalDateTime.ToString("yyyy-MM-dd")
            : "(未解析)";
        return
            $"檔名: {d.Name}\n" +
            $"類型: {(d.IsFolder ? "目錄" : "檔案")}\n" +
            $"大小: {sizeStr}\n" +
            $"修改時間: {mtimeStr}\n" +
            $"解析時間: {parsedStr}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0) return "-";
        const long KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / (double)GB:F2} GB";
        if (bytes >= MB) return $"{bytes / (double)MB:F1} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:F0} KB";
        return $"{bytes} B";
    }

    private static string FormatRelativeTime(long unixTs)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixTs);
        var diff = DateTimeOffset.UtcNow - dt;
        var absStr = dt.LocalDateTime.ToString("yyyy-MM-dd");
        string rel;
        if (diff.TotalDays >= 30) rel = absStr;
        else if (diff.TotalDays >= 1) rel = $"{(int)diff.TotalDays} 天前";
        else if (diff.TotalHours >= 1) rel = $"{(int)diff.TotalHours} 小時前";
        else if (diff.TotalMinutes >= 1) rel = $"{(int)diff.TotalMinutes} 分前";
        else rel = "剛剛";
        return diff.TotalDays >= 30 ? rel : $"{rel} ({absStr})";
    }
}

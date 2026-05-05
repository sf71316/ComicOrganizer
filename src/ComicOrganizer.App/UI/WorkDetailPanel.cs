using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// 作品詳細資訊面板。Drawer 內顯示，內容由 IAssetMetaStore.GetWorkDetail 提供。
/// 顯示：cover / 標題 / author 連結 / metadata / 閱讀記錄 / tags / 檔案資訊 / 操作按鈕。
/// 編輯模式（#45 Phase 2）：title / author / series / chapter / language / translation_group / rating / tags
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

    /// <summary>使用者按「重置閱讀記錄」— 清 read_status / count / last_read / last_page。</summary>
    public event Action<string>? RequestResetReading;

    /// <summary>使用者按「儲存」— host 處理 DB 寫入後刷新此 panel。</summary>
    public event Action<WorkEditData>? RequestSaveEdit;

    /// <summary>使用者按「從 nhentai 拉取」— host 處理 API 呼叫 + merge dialog + DB 寫入。</summary>
    public event Action<string>? RequestFetchExternal;

    /// <summary>由 host 設定 — 控制「從 nhentai 拉取」按鈕是否顯示（依 config.EnableExternalMetadata）。</summary>
    public bool ShowFetchExternalButton
    {
        get => btnFetchExternal.Visible;
        set => btnFetchExternal.Visible = value;
    }

    private string _currentPath = "";
    private bool _isEditing;
    private WorkDetail? _currentDetail;
    private readonly List<string> _editTags = new();

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
        btnResetReading.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_currentPath)) RequestResetReading?.Invoke(_currentPath);
        };
        picCover.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_currentPath) && !_isEditing) RequestOpenViewer?.Invoke(_currentPath);
        };
        btnEdit.Click += (s, e) => SetEditMode(true);
        btnCancel.Click += (s, e) => SetEditMode(false);
        btnSave.Click += (s, e) => SaveEdit();
        btnFetchExternal.Click += (s, e) =>
        {
            if (!string.IsNullOrEmpty(_currentPath)) RequestFetchExternal?.Invoke(_currentPath);
        };
        btnAddTag.Click += (s, e) => AddTagFromInput();
        txtAddTag.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddTagFromInput(); e.Handled = e.SuppressKeyPress = true; }
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
        btnResetReading.Text = LocalizationManager.GetString("WorkDetailResetReading") ?? "重置閱讀記錄";
        btnFetchExternal.Text = LocalizationManager.GetString("WorkDetailFetchExternal") ?? "從 nhentai 拉取";
    }

    /// <summary>套用詳細資訊。cover 由 caller 預先載入後傳入（沒就 null）。</summary>
    public void Show(WorkDetail detail, Image? cover)
    {
        _currentPath = detail.Path;
        _currentDetail = detail;
        _editTags.Clear();
        _editTags.AddRange(detail.Tags.Select(t => t.Name));
        SetEditMode(false);   // 顯示新作品時強制離開編輯態
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

        // tags（view mode）
        RebuildTagsView();

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

    // ===== 編輯模式 =====

    private void SetEditMode(bool editing)
    {
        _isEditing = editing;
        // view 控件
        lblWorkTitle.Visible = !editing;
        lnkAuthor.Visible = !editing && _currentDetail?.AuthorId.HasValue == true;
        lblMetaInfo.Visible = !editing;
        // edit 控件
        txtTitle.Visible = editing;
        txtAuthor.Visible = editing;
        editGrid.Visible = editing;
        addTagPanel.Visible = editing;
        // header / actions
        btnEdit.Visible = !editing;
        btnRead.Visible = !editing;
        btnExplorer.Visible = !editing;
        btnResetReading.Visible = !editing;
        btnSave.Visible = editing;
        btnCancel.Visible = editing;

        if (editing)
        {
            // 編輯態 ↔ 把當前資料填入 input
            var d = _currentDetail;
            if (d != null)
            {
                txtTitle.Text = d.Title ?? "";
                txtAuthor.Text = d.AuthorMainName ?? "";
                txtSeries.Text = d.Series ?? "";
                txtChapter.Text = d.ChapterNo ?? "";
                txtLanguage.Text = d.Language ?? "";
                txtTranslationGroup.Text = d.TranslationGroup ?? "";
                numRating.Value = d.Rating ?? 0;
            }
            txtAddTag.Text = "";
        }
        else if (_currentDetail != null)
        {
            // 取消編輯 — 重置 _editTags 為原始 tag list
            _editTags.Clear();
            _editTags.AddRange(_currentDetail.Tags.Select(t => t.Name));
        }
        RebuildTagsView();
    }

    private void RebuildTagsView()
    {
        flowTags.SuspendLayout();
        flowTags.Controls.Clear();
        var tagNames = _isEditing ? _editTags : (_currentDetail?.Tags.Select(t => t.Name).ToList() ?? new());
        if (tagNames.Count == 0)
        {
            flowTags.Controls.Add(BuildEmptyHint(_isEditing ? "(無標籤，下方新增)" : "(無標籤)"));
        }
        else
        {
            foreach (var name in tagNames)
            {
                var chip = new AntdUI.Tag
                {
                    Text = name,
                    Margin = new Padding(0, 0, 4, 4),
                    Font = new Font("Microsoft JhengHei UI", 8.5F),
                    CloseIcon = _isEditing,    // edit mode 顯示 X
                };
                if (_isEditing)
                {
                    var capturedName = name;
                    chip.CloseChanged += (s, e) =>
                    {
                        _editTags.Remove(capturedName);
                        // 用 BeginInvoke 避免在 close handler 中重 build flowTags 出問題
                        BeginInvoke(new Action(RebuildTagsView));
                        return true;   // 允許 AntdUI 正常關閉動畫
                    };
                }
                flowTags.Controls.Add(chip);
            }
        }
        flowTags.ResumeLayout(true);
    }

    private void AddTagFromInput()
    {
        var name = txtAddTag.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        if (!_editTags.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _editTags.Add(name);
            RebuildTagsView();
        }
        txtAddTag.Text = "";
    }

    private void SaveEdit()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        var data = new WorkEditData
        {
            Path = _currentPath,
            Title = NullIfEmpty(txtTitle.Text),
            AuthorName = NullIfEmpty(txtAuthor.Text),
            Series = NullIfEmpty(txtSeries.Text),
            ChapterNo = NullIfEmpty(txtChapter.Text),
            Language = NullIfEmpty(txtLanguage.Text),
            TranslationGroup = NullIfEmpty(txtTranslationGroup.Text),
            Rating = numRating.Value > 0 ? (int)numRating.Value : null,
            Tags = new List<string>(_editTags),
        };
        RequestSaveEdit?.Invoke(data);
        SetEditMode(false);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

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
        var manuallyTag = d.ManuallyEdited ? "  (手動編輯)" : "";
        return
            $"檔名: {d.Name}\n" +
            $"類型: {(d.IsFolder ? "目錄" : "檔案")}\n" +
            $"大小: {sizeStr}\n" +
            $"修改時間: {mtimeStr}\n" +
            $"解析時間: {parsedStr}{manuallyTag}";
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

/// <summary>WorkDetailPanel 「儲存」事件帶的編輯資料。AuthorName 會由 host UpsertAuthor 解析成 author_id。</summary>
public class WorkEditData
{
    public string Path { get; set; } = "";
    public string? Title { get; set; }
    public string? AuthorName { get; set; }
    public string? Series { get; set; }
    public string? ChapterNo { get; set; }
    public string? Language { get; set; }
    public string? TranslationGroup { get; set; }
    public int? Rating { get; set; }
    public List<string> Tags { get; set; } = new();
}

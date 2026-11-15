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

    /// <summary>使用者按「清除此作者所有閱讀記錄」— long 為 author id。</summary>
    public event Action<long>? RequestResetAuthorReading;

    /// <summary>使用者按「儲存」— host 處理 RenameAuthor / SetAuthorFolderPath / 別名 CRUD / SetAuthorTags。</summary>
    public event Action<AuthorEditData>? RequestSaveEdit;

    /// <summary>使用者按「同步此作者所有作品」(#52 Phase F) — host 處理批次拉取流程。</summary>
    public event Action<long>? RequestBatchSyncAll;

    /// <summary>進編輯模式時 fire — host 把 drawer 寬度撐到至少此值（auto-widen）。</summary>
    public event Action<int>? RequestExpandWidth;

    /// <summary>使用者按「展開最大寬度」 — host 把 drawer 撐到接近螢幕邊（toggle）。</summary>
    public event Action? RequestToggleMaximize;

    /// <summary>edit 模式 drawer 至少需要的寬度（涵蓋 main_name / alias / tag 編輯區）。</summary>
    private const int EditModeMinWidth = 580;

    private bool _showBatchFetchButton;
    /// <summary>由 host 設定 — 控制「同步此作者所有作品」按鈕是否顯示（依 config.EnableExternalMetadata）。
    /// 編輯模式下永遠隱藏；view mode 才看此 flag。</summary>
    public bool ShowBatchFetchButton
    {
        get => _showBatchFetchButton;
        set
        {
            _showBatchFetchButton = value;
            btnBatchFetch.Visible = value && !_isEditing;
        }
    }

    /// <summary>當前顯示的 author id（給 reset button 用）；ShowInfo 時設定。</summary>
    private long _currentAuthorId;
    private AuthorInfo? _currentInfo;
    private TagTranslator? _translator;

    /// <summary>由 host 注入翻譯器 — null 時 chip 顯示原文。</summary>
    public void SetTagTranslator(TagTranslator? translator)
    {
        _translator = translator;
        if (translator != null)
        {
            AutocompleteHelpers.BindArtistAutocomplete(txtMainName, translator);
            AutocompleteHelpers.BindArtistAutocomplete(txtAddAliasName, translator);
        }
        if (_currentInfo != null) RebuildTagsView();
    }
    private bool _isEditing;
    /// <summary>編輯中的別名（snapshot 自 _currentInfo.Aliases，使用者按 X 移除/輸入新增 不馬上寫 DB，Save 才寫）。</summary>
    private readonly List<AuthorAlias> _editAliases = new();
    private readonly List<string> _editTags = new();

    /// <summary>目前是否處於編輯模式。</summary>
    public bool IsEditing => _isEditing;

    /// <summary>處於編輯模式且相較原始資料有實際欄位異動時為 true。</summary>
    public bool HasUnsavedChanges => _isEditing && HasAuthorUnsavedChanges();

    /// <summary>強制放棄編輯狀態並重置編輯中欄位。</summary>
    public void DiscardEditing()
    {
        if (_isEditing) SetEditMode(false);
        else txtAddAliasName.Text = txtAddAliasLang.Text = txtAddTag.Text = "";
    }

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
        btnExpandMax.Click += (s, e) => RequestToggleMaximize?.Invoke();
        btnResetAuthorReading.Click += (s, e) =>
        {
            if (_currentAuthorId > 0) RequestResetAuthorReading?.Invoke(_currentAuthorId);
        };
        btnBatchFetch.Click += (s, e) =>
        {
            if (_currentAuthorId > 0) RequestBatchSyncAll?.Invoke(_currentAuthorId);
        };
        btnEdit.Click += (s, e) => SetEditMode(true);
        btnCancel.Click += (s, e) => SetEditMode(false);
        btnSave.Click += (s, e) => SaveEdit();
        btnAddAlias.Click += (s, e) => AddAliasFromInput();
        txtAddAliasName.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddAliasFromInput(); e.Handled = e.SuppressKeyPress = true; }
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
        lblTitle.Text = LocalizationManager.GetString("AuthorInfoTitle") ?? "作者資訊";
        lblTagsHeader.Text = LocalizationManager.GetString("AuthorInfoTopTags") ?? "主要標籤";
        lblRecentHeader.Text = LocalizationManager.GetString("AuthorInfoRecentRead") ?? "最近閱讀";
        lblAllWorksHeader.Text = LocalizationManager.GetString("AuthorInfoAllWorks") ?? "全部作品";
        btnResetAuthorReading.Text = LocalizationManager.GetString("AuthorInfoResetReading") ?? "清除此作者閱讀記錄";
        btnBatchFetch.Text = LocalizationManager.GetString("BatchSyncBtnText") ?? "同步此作者所有作品";
    }

    /// <summary>套用聚合資料 — 顯示完整 panel 內容。</summary>
    public void ShowInfo(AuthorInfo info)
    {
        lblEmpty.Visible = false;
        contentPanel.Visible = true;
        headerPanel.Visible = true;
        _currentAuthorId = info.Author.Id;
        _currentInfo = info;
        _editAliases.Clear();
        foreach (var a in info.Aliases) _editAliases.Add(new AuthorAlias { Id = a.Id, AuthorId = a.AuthorId, Name = a.Name, Lang = a.Lang });
        _editTags.Clear();
        _editTags.AddRange(info.AuthorTags.Select(t => t.Name));
        SetEditMode(false);

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

        // tags（view 模式 = TopTags；edit 模式 = AuthorTags 由 RebuildTagsView 切）
        RebuildTagsView();

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
        _currentAuthorId = 0;
        _currentInfo = null;
        _editAliases.Clear();
        _editTags.Clear();
        SetEditMode(false);
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

    // ===== 編輯模式（#45 Phase 3）=====

    private bool HasAuthorUnsavedChanges()
    {
        if (_currentInfo == null) return false;
        var snapshot = new AuthorEditSnapshot(
            MainName: txtMainName.Text,
            Aliases: _editAliases.Select(a => new AliasSnapshot(a.Name, a.Lang)).ToList(),
            Tags: _editTags,
            PendingAliasName: txtAddAliasName.Text,
            PendingAliasLang: txtAddAliasLang.Text,
            PendingTagInput: txtAddTag.Text);
        return DrawerEditStateComparer.HasAuthorEditChanges(_currentInfo, snapshot);
    }

    private void SetEditMode(bool editing)
    {
        _isEditing = editing;
        // 進 edit mode → 通知 host 撐寬 drawer（不縮回，尊重 user 中途調整）
        if (editing) RequestExpandWidth?.Invoke(EditModeMinWidth);
        // view 控件
        lblMainName.Visible = !editing;
        // folder_path 兩種模式都用 label 顯示（只讀，使用者不能改）
        lblFolderPath.Visible = _currentInfo?.Author.FolderPath is not null and not "";
        lblAliases.Visible = !editing && _currentInfo?.Aliases.Count > 0;
        // edit 控件
        txtMainName.Visible = editing;
        flowAliasesEdit.Visible = editing;
        addAliasPanel.Visible = editing;
        addTagPanel.Visible = editing;
        // header / actions
        btnEdit.Visible = !editing;
        btnResetAuthorReading.Visible = !editing;
        // btnBatchFetch 編輯中隱藏，view mode 看 _showBatchFetchButton（host 依 config 設）
        btnBatchFetch.Visible = !editing && _showBatchFetchButton;
        btnSave.Visible = editing;
        btnCancel.Visible = editing;

        if (editing)
        {
            var info = _currentInfo;
            if (info != null) txtMainName.Text = info.Author.MainName ?? "";
            txtAddAliasName.Text = txtAddAliasLang.Text = txtAddTag.Text = "";
        }
        else
        {
            if (_currentInfo != null)
            {
                // 取消編輯 — _editAliases / _editTags 重置回原始
                _editAliases.Clear();
                foreach (var a in _currentInfo.Aliases)
                    _editAliases.Add(new AuthorAlias { Id = a.Id, AuthorId = a.AuthorId, Name = a.Name, Lang = a.Lang });
                _editTags.Clear();
                _editTags.AddRange(_currentInfo.AuthorTags.Select(t => t.Name));
            }
            txtAddAliasName.Text = txtAddAliasLang.Text = txtAddTag.Text = "";
        }
        RebuildAliasEditChips();
        RebuildTagsView();
    }

    private void RebuildAliasEditChips()
    {
        flowAliasesEdit.SuspendLayout();
        flowAliasesEdit.Controls.Clear();
        if (_isEditing)
        {
            foreach (var alias in _editAliases.ToList())
            {
                var label = string.IsNullOrWhiteSpace(alias.Lang) ? alias.Name : $"{alias.Name} ({alias.Lang})";
                var chip = new AntdUI.Tag
                {
                    Text = label,
                    Margin = new Padding(0, 0, 4, 4),
                    Font = new Font("Microsoft JhengHei UI", 8.5F),
                    CloseIcon = true,
                    AutoSize = true,
                };
                var captured = alias;
                chip.CloseChanged += (s, e) =>
                {
                    _editAliases.Remove(captured);
                    BeginInvoke(new Action(RebuildAliasEditChips));
                    return true;
                };
                flowAliasesEdit.Controls.Add(chip);
            }
        }
        flowAliasesEdit.ResumeLayout(true);
    }

    private void RebuildTagsView()
    {
        flowTags.SuspendLayout();
        flowTags.Controls.Clear();
        if (_isEditing)
        {
            // 編輯時：顯示「作者本身」的 tag（_editTags），可刪
            foreach (var name in _editTags.ToList())
            {
                flowTags.Controls.Add(BuildAuthorTagChip(name, editable: true));
            }
            if (_editTags.Count == 0)
                flowTags.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = "(無作者標籤)",
                    ForeColor = ThemeColors.Border,
                    Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Italic),
                });
        }
        else
        {
            // view mode：顯示 TopTags (work tags 聚合，含 count)
            var info = _currentInfo;
            if (info != null)
            {
                foreach (var tag in info.TopTags)
                {
                    var display = _translator?.TranslateAny(tag.Name);
                    var labelText = !string.IsNullOrEmpty(display)
                        ? $"{display}  {tag.Count}"
                        : $"{tag.Name}  {tag.Count}";
                    var chip = new AntdUI.Tag
                    {
                        Text = labelText,
                        Margin = new Padding(0, 0, 4, 4),
                        Font = new Font("Microsoft JhengHei UI", 8.5F),
                        AutoSize = true,
                    };
                    if (!string.IsNullOrEmpty(display))
                    {
                        var tt = new ToolTip();
                        tt.SetToolTip(chip, tag.Name);
                    }
                    flowTags.Controls.Add(chip);
                }
                if (info.TopTags.Count == 0)
                    flowTags.Controls.Add(new Label
                    {
                        AutoSize = true,
                        Text = "(無)",
                        ForeColor = ThemeColors.Border,
                        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Italic),
                    });
            }
        }
        flowTags.ResumeLayout(true);
    }

    private AntdUI.Tag BuildAuthorTagChip(string name, bool editable)
    {
        var display = _translator?.TranslateAny(name);
        var chip = new AntdUI.Tag
        {
            Text = !string.IsNullOrEmpty(display) ? display : name,
            Margin = new Padding(0, 0, 4, 4),
            Font = new Font("Microsoft JhengHei UI", 8.5F),
            CloseIcon = editable,
            AutoSize = true,
        };
        if (!string.IsNullOrEmpty(display) && display != name)
        {
            var tt = new ToolTip();
            tt.SetToolTip(chip, name);
        }
        if (editable)
        {
            var captured = name;
            chip.CloseChanged += (s, e) =>
            {
                _editTags.Remove(captured);
                BeginInvoke(new Action(RebuildTagsView));
                return true;
            };
        }
        return chip;
    }

    private void AddAliasFromInput()
    {
        var name = txtAddAliasName.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        var lang = string.IsNullOrWhiteSpace(txtAddAliasLang.Text) ? null : txtAddAliasLang.Text.Trim();
        // 同名同 lang 不重複
        if (_editAliases.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(a.Lang ?? "", lang ?? "", StringComparison.OrdinalIgnoreCase)))
        {
            txtAddAliasName.Text = "";
            txtAddAliasLang.Text = "";
            return;
        }
        _editAliases.Add(new AuthorAlias { Id = 0, AuthorId = _currentAuthorId, Name = name, Lang = lang });
        txtAddAliasName.Text = txtAddAliasLang.Text = "";
        RebuildAliasEditChips();
    }

    private void AddTagFromInput()
    {
        var name = txtAddTag.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        if (!_editTags.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _editTags.Add(name);
        }
        txtAddTag.Text = "";
        RebuildTagsView();
    }

    private void SaveEdit()
    {
        if (_currentAuthorId <= 0) return;
        var data = new AuthorEditData
        {
            AuthorId = _currentAuthorId,
            MainName = (txtMainName.Text ?? "").Trim(),
            Aliases = _editAliases.Select(a => new AliasEdit { Id = a.Id, Name = a.Name, Lang = a.Lang }).ToList(),
            Tags = new List<string>(_editTags),
        };
        if (string.IsNullOrEmpty(data.MainName)) return;   // 主名不能空
        RequestSaveEdit?.Invoke(data);
        SetEditMode(false);
    }
}

/// <summary>AuthorInfoPanel 編輯資料。</summary>
public class AuthorEditData
{
    public long AuthorId { get; set; }
    public string MainName { get; set; } = "";
    /// <summary>編輯後的別名清單；Id=0 = 新加的 row（host 走 AddAuthorAlias）；
    /// 不在此清單裡的舊 row 視為刪除（host diff vs DB 後呼叫 DeleteAuthorAlias）。</summary>
    public List<AliasEdit> Aliases { get; set; } = new();
    /// <summary>作者本身的 tag list（會走 SetAuthorTags 整批 reset）。</summary>
    public List<string> Tags { get; set; } = new();
    // 注意：folder_path 不放這裡 — 它是 system-managed（scan 時自動設），不允許 UI 編輯
}

public class AliasEdit
{
    public long Id { get; set; }   // 0 = 新加
    public string Name { get; set; } = "";
    public string? Lang { get; set; }
}

using ComicOrganizer.App.Core;

namespace ComicOrganizer.App.UI;

/// <summary>
/// GalleryControl 搜尋 DSL 區（#48 Phase C-3）。
/// 從 GalleryControl.cs 抽出 — Enter 觸發、TextChanged 即時 hint chip。
/// </summary>
public partial class GalleryControl
{
    /// <summary>DSL 可用 field 列表（顯示 chip 順序就照這個）。</summary>
    private static readonly string[] DslFieldChips =
        { "artist:", "tag:", "language:", "read:" };

    private bool _isSearching;
    private SearchSuggestBox? _suggestBox;
    private System.Windows.Forms.Timer? _suggestDebounceTimer;
    private HintBarMessageFilter? _hintBarFilter;
    private bool _hintBarFilterRegistered;

    /// <summary>WireSearchBox — Enter 觸發 DSL 搜尋；TextChanged 更新輔助 chip 與自動完成選單。</summary>
    private void WireSearchBox()
    {
        _suggestBox = new SearchSuggestBox { OwnerInput = txtSearchDsl };
        splitter2.Panel1.Controls.Add(_suggestBox);
        _suggestBox.ItemSelected += item => ApplySuggestion(item);

        _suggestDebounceTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _suggestDebounceTimer.Tick += async (s, e) =>
        {
            _suggestDebounceTimer.Stop();
            await UpdateSuggestionsAsync();
        };

        // 攔截 Tab 鍵，有選單時作為帶入快捷鍵而不跳出焦點
        txtSearchDsl.PreviewKeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Tab && _suggestBox != null && _suggestBox.Visible && _suggestBox.HasItems)
            {
                e.IsInputKey = true;
            }
        };

        txtSearchDsl.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                bool handled = false;
                if (_suggestBox != null && _suggestBox.Visible)
                {
                    _suggestDebounceTimer?.Stop();
                    _suggestBox.HideSuggestions();
                    handled = true;
                }
                if (searchHintBarHost.Visible)
                {
                    searchHintBarHost.Visible = false;
                    handled = true;
                }
                if (handled)
                {
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (_suggestBox != null && _suggestBox.Visible && _suggestBox.HasItems)
            {
                if (e.KeyCode == Keys.Down)
                {
                    _suggestBox.SelectNext();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Up)
                {
                    _suggestBox.SelectPrev();
                    e.SuppressKeyPress = true;
                    return;
                }
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
                {
                    var item = _suggestBox.SelectedItem;
                    if (item != null)
                    {
                        ApplySuggestion(item);
                        e.SuppressKeyPress = true;
                        return;
                    }
                }
            }

            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;   // 防 Enter 觸發 ding 系統音
            _suggestDebounceTimer?.Stop();
            _suggestBox?.HideSuggestions();
            ExecuteDslSearch();
        };

        txtSearchDsl.TextChanged += (s, e) =>
        {
            UpdateSearchHints();
            _suggestDebounceTimer?.Stop();
            _suggestDebounceTimer?.Start();
        };

        txtSearchDsl.GotFocus += (s, e) =>
        {
            UpdateSearchHints();
        };

        // 當搜尋框失去焦點（例如切換分頁、點擊卡片或 Alt+Tab）時自動收起下拉建議；若游標不在提示列範圍內亦一併收起提示列
        txtSearchDsl.LostFocus += (s, e) =>
        {
            _suggestDebounceTimer?.Stop();
            _suggestBox?.HideSuggestions();
            var mouseInHintBar = searchHintBarHost.Visible &&
                searchHintBarHost.RectangleToScreen(searchHintBarHost.ClientRectangle).Contains(Cursor.Position);
            if (!mouseInHintBar)
            {
                searchHintBarHost.Visible = false;
            }
        };

        // hintBar 與 suggestBox 都是 floating overlay，要 sync parent 寬度
        splitter2.Panel1.SizeChanged += (s, e) =>
        {
            RepositionSearchHintBar();
            RepositionSuggestBox();
        };

        InitHintBarCloseButton();
        searchHintBarHost.VisibleChanged += (s, e) =>
        {
            if (searchHintBarHost.Visible)
            {
                RegisterHintBarFilter();
            }
            else
            {
                UnregisterHintBarFilter();
            }
        };
        searchHintBarHost.Disposed += (s, e) => UnregisterHintBarFilter();

        BuildSearchHintChips();
    }

    /// <summary>把 floating hintBar host 貼到 txtSearchDsl 下緣（searchBoxPanel.Bottom − Padding.Bottom），
    /// 寬度跟 txtSearchDsl 一致。</summary>
    private void RepositionSearchHintBar()
    {
        var parent = splitter2.Panel1;
        var sb = searchBoxPanel;
        var left = parent.Padding.Left + sb.Padding.Left;
        var width = parent.ClientSize.Width - parent.Padding.Horizontal - sb.Padding.Horizontal;
        var top = sb.Bottom - sb.Padding.Bottom;
        searchHintBarHost.SetBounds(left, top, width, searchHintBarHost.Height);
        searchHintBarHost.BringToFront();
    }

    private void RepositionSuggestBox()
    {
        if (_suggestBox == null) return;
        var parent = splitter2.Panel1;
        var sb = searchBoxPanel;
        var left = parent.Padding.Left + sb.Padding.Left;
        var width = parent.ClientSize.Width - parent.Padding.Horizontal - sb.Padding.Horizontal;
        var top = sb.Bottom - sb.Padding.Bottom;
        _suggestBox.SetBounds(left, top, width, _suggestBox.Height);
        _suggestBox.BringToFront();
    }

    private void BuildSearchHintChips()
    {
        searchHintBar.Controls.Clear();
        foreach (var field in DslFieldChips)
        {
            var chip = new SearchHintChip();
            chip.SetMatch(field, 0);
            chip.Click += async (s, e) =>
            {
                txtSearchDsl.Text = SearchQueryParser.ApplyFieldChip(txtSearchDsl.Text, field);
                txtSearchDsl.SelectionStart = txtSearchDsl.Text.Length;
                txtSearchDsl.Focus();
                _suggestDebounceTimer?.Stop();
                await UpdateSuggestionsAsync();
            };
            searchHintBar.Controls.Add(chip);
        }
    }

    /// <summary>依當前輸入更新 chip 樣式 + hintBar 可見性。
    /// 規則：
    ///   空輸入 → 整 bar 隱藏（避免空搜尋框遮擋下方切換列）
    ///   已含 ':' → 已進入 field:value 階段，隱藏
    ///   無任何欄位匹配該前綴（如純文字搜尋 "魔法"） → 隱藏
    ///   有欄位匹配（或剛打完空白 / 只打 '-' 準備輸入新欄位） → 顯示並高亮匹配 chip</summary>
    private void UpdateSearchHints()
    {
        var text = txtSearchDsl.Text ?? "";
        if (string.IsNullOrEmpty(text))
        {
            searchHintBarHost.Visible = false;
            return;
        }

        // 取最後一個 whitespace 切割後的 token
        var lastWs = text.LastIndexOfAny(new[] { ' ', '\t' });
        var rawLastToken = lastWs < 0 ? text : text[(lastWs + 1)..];

        // 剛打完空白（或選完建議自動補空白）或已含冒號（進入 field:value 階段）→ 直接隱藏，不遮擋下方切換列
        if (string.IsNullOrEmpty(rawLastToken) || rawLastToken.Contains(':'))
        {
            searchHintBarHost.Visible = false;
            return;
        }

        // 去掉前置負號（用戶可能在打 -tag）
        var lastToken = rawLastToken.StartsWith('-') ? rawLastToken[1..] : rawLastToken;
        var prefix = lastToken.ToLowerInvariant();

        // 檢查是否有任何欄位符合該前綴；若只打了負號 (rawLastToken == "-") 準備接欄位，亦視為符合
        bool hasMatchingField = rawLastToken == "-" ||
            DslFieldChips.Any(f => f.TrimEnd(':').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!hasMatchingField)
        {
            searchHintBarHost.Visible = false;
            return;
        }

        // 若目前 searchHintBar 顯示的是模糊推薦（沒有 SearchHintChip），重建標準 chip
        if (!searchHintBar.Controls.OfType<SearchHintChip>().Any())
        {
            BuildSearchHintChips();
        }

        if (!searchHintBarHost.Visible)
        {
            searchHintBarHost.Visible = true;
            RepositionSearchHintBar();
        }
        foreach (var ctrl in searchHintBar.Controls.OfType<SearchHintChip>())
        {
            // chip text = "artist:" 之類；prefix 比對「冒號前」部分
            var chipName = (ctrl.Text ?? "").TrimEnd(':');
            int matchLen = !string.IsNullOrEmpty(prefix)
                           && chipName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? prefix.Length : 0;
            ctrl.SetMatch(ctrl.Text!, matchLen);
        }
    }

    /// <summary>parse + execute 搜尋 DSL，結果開到當前 tab。
    /// 在背景執行緒執行避免大資料量凍結 UI；空字串不做事（避免誤按 Enter 把 tab 清空）。</summary>
    private async void ExecuteDslSearch()
    {
        if (_metaStore == null || _isSearching) return;
        var raw = txtSearchDsl.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw)) return;

        var query = SearchQueryParser.Parse(raw);
        if (query.IsEmpty) return;

        _isSearching = true;
        _suggestDebounceTimer?.Stop();
        _suggestBox?.HideSuggestions();
        ReportJobStarted("搜尋中...");

        var lang = GetCurrentTagLang();
        var executor = new SearchExecutor(_metaStore, lang);
        IReadOnlyList<string> paths;
        try
        {
            paths = await Task.Run(() => executor.Execute(query));
        }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null)
                AntdUI.Notification.warn(form,
                    LocalizationManager.GetString("SearchFailedTitle") ?? "搜尋失敗",
                    ex.Message, autoClose: 3, align: AntdUI.TAlignFrom.TR);
            return;
        }
        finally
        {
            _isSearching = false;
            ReportJobFinished();
        }

        if (this.IsDisposed) return;

        // tab 標題 = 原 query 字串 + 筆數；breadcrumb 顯示 SearchOutlined icon
        var tabTitle = $"{raw} ({paths.Count})";
        OpenVirtualWorksTab(tabTitle, paths, iconSvg: "SearchOutlined");

        // 若有結果則收起輔助提示列；若 0 結果則顯示提示與相近詞推薦
        if (paths.Count > 0)
        {
            searchHintBarHost.Visible = false;
        }
        else
        {
            var recs = await Task.Run(() => SearchFuzzyRecommender.FindRecommendations(_metaStore, raw, query, lang));
            if (!this.IsDisposed)
            {
                if (recs.Count > 0)
                {
                    DisplayFuzzyRecommendations(recs);
                }

                var form = this.FindForm();
                if (form != null)
                {
                    var msg = string.Format(LocalizationManager.GetString("SearchNoResultsMsgFmt") ?? "「{0}」找不到符合的作品", raw)
                        + "\n（若確認庫內有此作品，可至「詮釋資料管理」執行重建索引）";
                    AntdUI.Notification.info(form,
                        LocalizationManager.GetString("SearchNoResultsTitle") ?? "沒有結果",
                        msg, autoClose: 4, align: AntdUI.TAlignFrom.TR);
                }
            }
        }
    }

    private void DisplayFuzzyRecommendations(IReadOnlyList<SearchRecommendation> recs)
    {
        searchHintBar.Controls.Clear();

        var lblPrompt = new Label
        {
            Text = "你是不是想找：",
            AutoSize = true,
            ForeColor = Color.FromArgb(140, 140, 140),
            Font = new Font("Microsoft JhengHei UI", 8.5F),
            Margin = new Padding(2, 4, 2, 2)
        };
        searchHintBar.Controls.Add(lblPrompt);

        foreach (var rec in recs)
        {
            var btnRec = new Label
            {
                Text = rec.DisplayText,
                AutoSize = true,
                ForeColor = ThemeColors.Primary,
                Font = new Font("Microsoft JhengHei UI", 8.5F, FontStyle.Underline),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 4, 6, 2)
            };
            btnRec.Click += (s, e) =>
            {
                txtSearchDsl.Text = rec.ReplacementQuery;
                _suggestDebounceTimer?.Stop();
                _suggestBox?.HideSuggestions();
                ExecuteDslSearch();
            };
            searchHintBar.Controls.Add(btnRec);
        }

        searchHintBarHost.Visible = true;
        RepositionSearchHintBar();
    }

    private async Task UpdateSuggestionsAsync()
    {
        if (_metaStore == null || _suggestBox == null || !txtSearchDsl.Focused)
        {
            _suggestBox?.HideSuggestions();
            return;
        }
        var text = txtSearchDsl.Text ?? "";
        var token = SearchQueryParser.ExtractActiveFieldToken(text);
        if (token == null)
        {
            _suggestBox.HideSuggestions();
            return;
        }

        var lang = GetCurrentTagLang();
        var items = await Task.Run(() => FetchSuggestions(token.Field, token.Value, lang));

        if (this.IsDisposed || !txtSearchDsl.Focused || txtSearchDsl.Text != text) return;

        if (items.Count == 0)
        {
            _suggestBox.HideSuggestions();
        }
        else
        {
            _suggestBox.SetItems(items);
            RepositionSuggestBox();
            _suggestBox.ShowSuggestions();
            searchHintBarHost.Visible = false;
        }
    }

    private List<SearchSuggestionItem> FetchSuggestions(SearchField field, string value, string? lang)
    {
        var list = new List<SearchSuggestionItem>();
        if (_metaStore == null) return list;

        switch (field)
        {
            case SearchField.Artist:
                var usageByAuthorId = _metaStore.ListAuthorsByUsage()
                    .ToDictionary(u => u.Author.Id, u => u.WorkCount);
                if (string.IsNullOrWhiteSpace(value))
                {
                    var top = _metaStore.ListAuthorsByUsage().Take(10);
                    foreach (var a in top)
                    {
                        list.Add(new SearchSuggestionItem
                        {
                            DisplayText = a.Author.MainName,
                            InsertValue = a.Author.MainName,
                            SubText = $"{a.WorkCount} 部作品",
                            Category = "作者"
                        });
                    }
                }
                else
                {
                    var authors = _metaStore.SearchAuthors(value, limit: 10);
                    foreach (var a in authors)
                    {
                        var display = a.MainName;
                        // 若主名本身未包含關鍵字，代表是透過 author_alias 命中，附註命中的別名方便辨識
                        if (!a.MainName.Contains(value, StringComparison.OrdinalIgnoreCase))
                        {
                            var matchedAlias = _metaStore.ListAuthorAliases(a.Id)
                                .FirstOrDefault(al => al.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
                            if (matchedAlias != null)
                            {
                                display = $"{a.MainName} ({matchedAlias.Name})";
                            }
                        }

                        usageByAuthorId.TryGetValue(a.Id, out int workCount);
                        list.Add(new SearchSuggestionItem
                        {
                            DisplayText = display,
                            InsertValue = a.MainName,
                            SubText = $"{workCount} 部作品",
                            Category = "作者"
                        });
                    }
                }
                break;

            case SearchField.Tag:
                var targetLang = lang ?? "zh-TW";
                var allTagUsages = _metaStore.ListTagsByUsage();
                if (string.IsNullOrWhiteSpace(value))
                {
                    var top = allTagUsages.Take(10).ToList();
                    var transMap = top.Count > 0
                        ? _metaStore.GetTagTranslations(top.Select(t => t.Id).ToList(), targetLang)
                        : new Dictionary<long, string>();
                    foreach (var t in top)
                    {
                        transMap.TryGetValue(t.Id, out var trans);
                        var display = !string.IsNullOrEmpty(trans) ? $"{trans} ({t.Name})" : t.Name;
                        var insert = !string.IsNullOrEmpty(trans) ? trans : t.Name;
                        list.Add(new SearchSuggestionItem
                        {
                            DisplayText = display,
                            InsertValue = insert,
                            SubText = $"{t.AssetCount} 部作品",
                            Category = "標籤"
                        });
                    }
                }
                else
                {
                    var countByTagId = allTagUsages.ToDictionary(u => u.Id, u => u.AssetCount);
                    var seen = new HashSet<long>();
                    var matchedTags = new List<Tag>();

                    var transIds = _metaStore.SearchTagIdsByTranslatedName(value, lang, limit: 10);
                    if (transIds.Count > 0)
                    {
                        foreach (var t in _metaStore.GetTagsByIds(transIds))
                        {
                            if (seen.Add(t.Id))
                                matchedTags.Add(t);
                        }
                    }

                    if (matchedTags.Count < 10)
                    {
                        foreach (var t in _metaStore.SearchTags(value, limit: 10))
                        {
                            if (seen.Add(t.Id))
                            {
                                matchedTags.Add(t);
                                if (matchedTags.Count >= 10) break;
                            }
                        }
                    }

                    var transMap = matchedTags.Count > 0
                        ? _metaStore.GetTagTranslations(matchedTags.Select(t => t.Id).ToList(), targetLang)
                        : new Dictionary<long, string>();

                    foreach (var t in matchedTags)
                    {
                        transMap.TryGetValue(t.Id, out var trans);
                        countByTagId.TryGetValue(t.Id, out int assetCount);
                        var display = !string.IsNullOrEmpty(trans) ? $"{trans} ({t.Name})" : t.Name;
                        var insert = !string.IsNullOrEmpty(trans) ? trans : t.Name;
                        list.Add(new SearchSuggestionItem
                        {
                            DisplayText = display,
                            InsertValue = insert,
                            SubText = $"{assetCount} 部作品",
                            Category = "標籤"
                        });
                    }
                }
                break;

            case SearchField.Language:
                var buckets = _metaStore.ListLanguageBuckets();
                foreach (var b in buckets)
                {
                    if (b.Language == null) continue;
                    var langName = GetFriendlyLangName(b.Language);
                    if (string.IsNullOrWhiteSpace(value) ||
                        b.Language.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                        langName.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(new SearchSuggestionItem
                        {
                            DisplayText = $"{b.Language} ({langName})",
                            InsertValue = b.Language,
                            SubText = $"{b.WorkCount} 部作品",
                            Category = "語言"
                        });
                    }
                }
                break;

            case SearchField.ReadStatus:
                var statuses = new[]
                {
                    ("unread", "未讀"),
                    ("reading", "進行中"),
                    ("completed", "已完成"),
                    ("dropped", "放棄")
                };
                foreach (var (code, name) in statuses)
                {
                    if (string.IsNullOrWhiteSpace(value) ||
                        code.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(new SearchSuggestionItem
                        {
                            DisplayText = $"{name} ({code})",
                            InsertValue = code,
                            Category = "狀態"
                        });
                    }
                }
                break;
        }

        return list;
    }

    private static string GetFriendlyLangName(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "chinese" or "zh" or "zh-tw" or "zh-cn" => "中文",
            "japanese" or "ja" => "日文",
            "english" or "en" => "英文",
            "korean" or "ko" => "韓文",
            _ => code
        };
    }

    private void ApplySuggestion(SearchSuggestionItem item)
    {
        var text = txtSearchDsl.Text ?? "";
        var token = SearchQueryParser.ExtractActiveFieldToken(text);
        if (token == null) return;

        var newText = SearchQueryParser.ApplySuggestion(text, token, item.InsertValue);
        txtSearchDsl.Text = newText;
        txtSearchDsl.SelectionStart = newText.Length;
        _suggestDebounceTimer?.Stop();
        _suggestBox?.HideSuggestions();
    }

    private void InitHintBarCloseButton()
    {
        var btnCloseHintBar = new HintBarCloseButton
        {
            Text = "✕",
            Dock = DockStyle.Right,
            Width = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(140, 140, 140),
            BackColor = Color.Transparent,
            Font = new Font("Microsoft JhengHei UI", 9F)
        };
        btnCloseHintBar.MouseEnter += (s, e) => btnCloseHintBar.ForeColor = ThemeColors.Text;
        btnCloseHintBar.MouseLeave += (s, e) => btnCloseHintBar.ForeColor = Color.FromArgb(140, 140, 140);
        btnCloseHintBar.Click += (s, e) => searchHintBarHost.Visible = false;

        searchHintBarHost.Controls.Add(btnCloseHintBar);
        btnCloseHintBar.SendToBack();
        searchHintBar.BringToFront();
    }

    private void RegisterHintBarFilter()
    {
        if (!_hintBarFilterRegistered)
        {
            _hintBarFilter ??= new HintBarMessageFilter(this);
            Application.AddMessageFilter(_hintBarFilter);
            _hintBarFilterRegistered = true;
        }
    }

    private void UnregisterHintBarFilter()
    {
        if (_hintBarFilterRegistered && _hintBarFilter != null)
        {
            Application.RemoveMessageFilter(_hintBarFilter);
            _hintBarFilterRegistered = false;
        }
    }

    private sealed class HintBarCloseButton : Label
    {
        public HintBarCloseButton()
        {
            SetStyle(ControlStyles.Selectable, false);
        }
    }

    private sealed class HintBarMessageFilter : IMessageFilter
    {
        private readonly GalleryControl _owner;
        public HintBarMessageFilter(GalleryControl owner) => _owner = owner;

        public bool PreFilterMessage(ref Message m)
        {
            // WM_LBUTTONDOWN (0x0201), WM_RBUTTONDOWN (0x0204), WM_MBUTTONDOWN (0x0207), WM_NCLBUTTONDOWN (0x00A1)
            if (_owner.searchHintBarHost.Visible &&
                (m.Msg == 0x0201 || m.Msg == 0x0204 || m.Msg == 0x0207 || m.Msg == 0x00A1))
            {
                var clicked = Control.FromChildHandle(m.HWnd);
                if (!IsSelfOrChildOf(clicked, _owner.txtSearchDsl) &&
                    !IsSelfOrChildOf(clicked, _owner.searchHintBarHost))
                {
                    _owner.searchHintBarHost.Visible = false;
                }
            }
            return false;
        }

        private static bool IsSelfOrChildOf(Control? c, Control? root)
        {
            if (c == null || root == null) return false;
            if (c == root) return true;
            return root.Contains(c);
        }
    }
}

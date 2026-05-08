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
        { "artist:", "tag:", "language:", "read:", "status:" };

    /// <summary>WireSearchBox — Enter 觸發 DSL 搜尋；TextChanged 更新輔助 chip。</summary>
    private void WireSearchBox()
    {
        txtSearchDsl.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;   // 防 Enter 觸發 ding 系統音
            ExecuteDslSearch();
        };
        txtSearchDsl.TextChanged += (s, e) => UpdateSearchHints();
        // hintBar 是 floating overlay，要 sync parent 寬度
        splitter2.Panel1.SizeChanged += (s, e) => RepositionSearchHintBar();
        BuildSearchHintChips();
    }

    /// <summary>把 floating hintBar 對齊到 searchBoxPanel 下方，寬度撐滿 sidebar。</summary>
    private void RepositionSearchHintBar()
    {
        var parent = splitter2.Panel1;
        searchHintBar.SetBounds(
            parent.Padding.Left,
            searchBoxPanel.Bottom,
            parent.ClientSize.Width - parent.Padding.Horizontal,
            searchHintBar.Height);
        searchHintBar.BringToFront();
    }

    private void BuildSearchHintChips()
    {
        if (searchHintBar.Controls.Count > 0) return;
        foreach (var field in DslFieldChips)
        {
            var chip = new SearchHintChip();
            chip.SetMatch(field, 0);
            searchHintBar.Controls.Add(chip);
        }
    }

    /// <summary>依當前輸入更新 chip 樣式 + hintBar 可見性。
    /// 規則：
    ///   空輸入 → 整 bar 隱藏
    ///   有輸入 → bar 顯示；最後一個 token 是 prefix → 匹配 chip 高亮、其他 dim
    ///   token 已含 ':' → 全部 chip dim（已過 field 名階段）</summary>
    private void UpdateSearchHints()
    {
        var text = txtSearchDsl.Text ?? "";
        if (string.IsNullOrEmpty(text))
        {
            searchHintBar.Visible = false;
            return;
        }

        // 取最後一個 whitespace 切割後的 token
        var lastWs = text.LastIndexOfAny(new[] { ' ', '\t' });
        var lastToken = lastWs < 0 ? text : text[(lastWs + 1)..];
        // 去掉前置負號（用戶可能在打 -tag）
        if (lastToken.StartsWith('-')) lastToken = lastToken[1..];

        // 已含冒號 → 已過 field 名階段，全部 dim
        var hasColon = lastToken.Contains(':');
        var prefix = hasColon ? "" : lastToken.ToLowerInvariant();

        if (!searchHintBar.Visible)
        {
            searchHintBar.Visible = true;
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
    /// 空字串不做事（避免誤按 Enter 把 tab 清空）。</summary>
    private void ExecuteDslSearch()
    {
        if (_metaStore == null) return;
        var raw = txtSearchDsl.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw)) return;

        var query = SearchQueryParser.Parse(raw);
        if (query.IsEmpty) return;

        var lang = GetCurrentTagLang();
        var executor = new SearchExecutor(_metaStore, lang);
        IReadOnlyList<string> paths;
        try { paths = executor.Execute(query); }
        catch (Exception ex)
        {
            var form = this.FindForm();
            if (form != null)
                AntdUI.Notification.warn(form,
                    LocalizationManager.GetString("SearchFailedTitle") ?? "搜尋失敗",
                    ex.Message, autoClose: 3, align: AntdUI.TAlignFrom.TR);
            return;
        }

        // tab 標題 = 原 query 字串；breadcrumb 顯示 SearchOutlined icon
        OpenVirtualWorksTab(raw, paths);

        // 若 0 結果，顯示提示（OpenVirtualWorksTab 會清空內容 + 換 tab title，但要 toast 補一句）
        if (paths.Count == 0)
        {
            var form = this.FindForm();
            if (form != null)
                AntdUI.Notification.info(form,
                    LocalizationManager.GetString("SearchNoResultsTitle") ?? "沒有結果",
                    string.Format(LocalizationManager.GetString("SearchNoResultsMsgFmt") ?? "「{0}」找不到符合的作品", raw),
                    autoClose: 3, align: AntdUI.TAlignFrom.TR);
        }
    }
}

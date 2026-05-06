namespace ComicOrganizer.App.Core;

/// <summary>
/// 讓 AntdUI.Select 變 EhTag 作者 autocomplete 的共用 binding（#53 Phase 4）。
/// </summary>
public static class AutocompleteHelpers
{
    /// <summary>
    /// 綁 artist autocomplete 到 AntdUI.Select。輸入文字 → FilterChanged →
    /// SuggestArtists 動態回傳 dropdown items。SelectItem 結構：
    ///   Text = LocalizedName（中/日/英顯示名，選中後變 select.Text）
    ///   SubText = slug（小字輔助辨識同名作者）
    ///   Tag = slug（給 caller 拿原始 slug 用）
    /// 選中後 select.Text 是乾淨的名字（沒有「(slug)」尾巴），可直接當 author.main_name 寫 DB。
    /// translator 沒載入時 noop（select 退化成普通 input 自由輸入）。
    /// </summary>
    public static void BindArtistAutocomplete(AntdUI.Select select, TagTranslator translator, int limit = 10)
    {
        if (!translator.IsAvailable) return;
        select.FilterChanged += (sender, value) =>
        {
            var items = new List<object>();
            if (string.IsNullOrWhiteSpace(value)) return items;
            foreach (var hit in translator.SuggestArtists(value, limit))
            {
                var displayName = string.IsNullOrEmpty(hit.LocalizedName) ? hit.Slug : hit.LocalizedName;
                items.Add(new AntdUI.SelectItem(displayName, hit.Slug)
                {
                    SubText = hit.Slug,
                });
            }
            return items;
        };
    }
}

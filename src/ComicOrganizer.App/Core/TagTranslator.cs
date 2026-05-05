namespace ComicOrganizer.App.Core;

/// <summary>
/// nhentai tag → 中文翻譯 facade（#53 Phase 2）。
///
/// nhentai type 跟 EhTag namespace 對映：
///   "tag"       → female / male / mixed / other（順序 fallback；nhentai tag 沒分性別細項）
///   "artist"    → artist
///   "group"     → group
///   "parody"    → parody
///   "character" → character
///   "language"  → language
///   "category"  → 無對應（doujinshi/manga 等是 EhTag reclass 跳過）
///
/// 透過 Lazy 載入 — 第一次 Translate 才解壓 + parse 4MB JSON。
/// 載入失敗時靜默退化為「永遠回 null」（caller fallback 到原英文）。
/// </summary>
public class TagTranslator
{
    private readonly Lazy<TagTranslationDb?> _db;

    /// <summary>下游 fallback 順序，給 nhentai 「tag」type 用（多 namespace 試）。</summary>
    private static readonly string[] TagNamespaceFallback =
        { "female", "male", "mixed", "other" };

    /// <summary>單一 namespace 對映（一對一）。</summary>
    private static readonly Dictionary<string, string> NhentaiToEhTagSimple =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "artist",    "artist"    },
            { "group",     "group"     },
            { "parody",    "parody"    },
            { "character", "character" },
            { "language",  "language"  },
        };

    public TagTranslator(Func<TagTranslationDb?> dbLoader)
    {
        _db = new Lazy<TagTranslationDb?>(() =>
        {
            try { return dbLoader(); }
            catch { return null; }
        });
    }

    /// <summary>預設建構子：從 embedded resource 載入。</summary>
    public TagTranslator() : this(() => TagTranslationDb.LoadFromEmbedded()) { }

    /// <summary>翻譯庫是否成功載入。Lazy — 觸發載入。</summary>
    public bool IsAvailable => _db.Value != null;

    /// <summary>翻譯庫 head sha（給設定頁顯示版本用）。</summary>
    public string HeadSha => _db.Value?.HeadSha ?? "";

    /// <summary>翻譯庫條目總數。</summary>
    public int Count => _db.Value?.Count ?? 0;

    /// <summary>nhentai (type, slug) → 中文翻譯名。找不到回 null。
    /// 當前 UI 語言是 zh-TW 自動將簡體轉繁體（EhTag 是中國社群簡體翻譯）。</summary>
    public string? Translate(string nhentaiType, string slug)
    {
        var raw = TranslateRaw(nhentaiType, slug);
        return MaybeToTraditional(raw);
    }

    private string? TranslateRaw(string nhentaiType, string slug)
    {
        var db = _db.Value;
        if (db == null || string.IsNullOrEmpty(slug)) return null;

        // tag → 多 namespace fallback
        if (string.Equals(nhentaiType, "tag", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var ns in TagNamespaceFallback)
            {
                if (db.TryGet(ns, slug, out var entry)) return entry.Name;
            }
            return null;
        }

        // 一對一映射
        if (NhentaiToEhTagSimple.TryGetValue(nhentaiType, out var ehNs)
            && db.TryGet(ehNs, slug, out var e2))
            return e2.Name;

        return null;
    }

    private static string? MaybeToTraditional(string? simplified)
    {
        if (string.IsNullOrEmpty(simplified)) return simplified;
        var lang = System.Globalization.CultureInfo.CurrentUICulture.Name;
        // zh-TW / zh-HK / zh-MO / zh-Hant 才轉；簡體環境保留
        if (lang.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
            || lang.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
            || lang.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase)
            || lang.Contains("Hant", StringComparison.OrdinalIgnoreCase))
        {
            return ZhConverter.ToTraditional(simplified);
        }
        return simplified;
    }

    /// <summary>artist 專用 convenience。</summary>
    public string? TranslateArtist(string slug) => Translate("artist", slug);

    /// <summary>不知道 namespace 時，依序試 EhTag 所有 tag-like namespace。
    /// 給本地 DB 內 tag (沒記 category 來源) 用。</summary>
    public string? TranslateAny(string slug)
    {
        var db = _db.Value;
        if (db == null || string.IsNullOrEmpty(slug)) return null;
        // 一般主題優先 (覆蓋率最高)，artist/character 等次之
        foreach (var ns in new[] { "female", "male", "mixed", "other", "artist",
                                   "group", "parody", "character", "language", "cosplayer" })
        {
            if (db.TryGet(ns, slug, out var entry)) return MaybeToTraditional(entry.Name);
        }
        return null;
    }

    /// <summary>artist 是否在 EhTag DB 內。</summary>
    public bool IsKnownArtist(string slug)
    {
        var db = _db.Value;
        return db != null && db.TryGet("artist", slug, out _);
    }

    /// <summary>artist autocomplete — match slug + 翻譯名（中/英 / 羅馬都比對）。</summary>
    public IEnumerable<ArtistSuggestion> SuggestArtists(string prefix, int limit = 20)
    {
        var db = _db.Value;
        if (db == null || string.IsNullOrWhiteSpace(prefix)) yield break;

        var p = prefix.Trim();
        int matched = 0;
        foreach (var entry in db.EnumerateNamespace("artist"))
        {
            if (matched >= limit) yield break;
            if (entry.Slug.Contains(p, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(entry.Name) && entry.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new ArtistSuggestion(entry.Slug, entry.Name);
                matched++;
            }
        }
    }

    /// <summary>多筆 nhentai tag 一次翻譯（給 UI batch render 用）。</summary>
    public List<TranslatedTag> TranslateMany(IEnumerable<MetadataTag> tags)
    {
        var result = new List<TranslatedTag>();
        foreach (var t in tags)
        {
            var zh = Translate(t.Type, t.Slug);
            result.Add(new TranslatedTag(t.Type, t.Slug, t.Name, zh));
        }
        return result;
    }
}

/// <summary>autocomplete 結果。</summary>
public record ArtistSuggestion(string Slug, string LocalizedName);

/// <summary>nhentai tag + 翻譯後的中文名（null = 翻譯庫沒收錄）。</summary>
public record TranslatedTag(string Type, string Slug, string OriginalName, string? LocalizedName);

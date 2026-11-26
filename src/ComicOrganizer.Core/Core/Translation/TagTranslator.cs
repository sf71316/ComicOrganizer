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

    /// <summary>預設建構子：只載入 embedded resource（給測試 / 不需 disk cache 的場景用）。</summary>
    public TagTranslator() : this(() => TagTranslationDb.LoadFromEmbedded()) { }

    /// <summary>主要建構子：disk cache 優先，找不到 / 損壞 → fallback embedded（永遠可用）。
    /// MainForm 注入 <c>BaseDirectory/translations/db.text.json.gz</c>。</summary>
    public TagTranslator(string? diskPath) : this(() => LoadDiskOrEmbedded(diskPath)) { }

    private static TagTranslationDb LoadDiskOrEmbedded(string? diskPath)
    {
        if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
        {
            try { return TagTranslationDb.LoadFromFile(diskPath); }
            catch { /* 損壞 / IO 錯 → embedded fallback */ }
        }
        return TagTranslationDb.LoadFromEmbedded();
    }

    /// <summary>翻譯庫是否成功載入。Lazy — 觸發載入。</summary>
    public bool IsAvailable => _db.Value != null;

    /// <summary>翻譯庫 head sha（給設定頁顯示版本用）。</summary>
    public string HeadSha => _db.Value?.HeadSha ?? "";

    /// <summary>翻譯庫條目總數。</summary>
    public int Count => _db.Value?.Count ?? 0;

    /// <summary>當前載入的來源（給 UI 顯示「來源：bundled / cache」用）。</summary>
    public TagTranslationDbSource Source => _db.Value?.Source ?? TagTranslationDbSource.Embedded;

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

    private static string? MaybeToTraditional(string? simplified, string? targetLang = null)
    {
        if (string.IsNullOrEmpty(simplified)) return simplified;
        var lang = !string.IsNullOrWhiteSpace(targetLang)
            ? targetLang
            : System.Globalization.CultureInfo.CurrentUICulture.Name;
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
    public string? TranslateAny(string slug) => TranslateAny(slug, targetLang: null);

    public string? TranslateAny(string slug, string? targetLang)
    {
        var db = _db.Value;
        if (db == null || string.IsNullOrEmpty(slug)) return null;
        // 一般主題優先 (覆蓋率最高)，artist/character 等次之
        foreach (var ns in new[] { "female", "male", "mixed", "other", "artist",
                                   "group", "parody", "character", "language", "cosplayer" })
        {
            if (db.TryGet(ns, slug, out var entry)) return MaybeToTraditional(entry.Name, targetLang);
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

    /// <summary>把一批 tag (id+name) 的譯文同步到 DB tag_translation 表（schema v6）。
    /// 使用 TranslateAny — 不知道每個 tag 的 namespace 也能跨 namespace 找。
    /// 只覆寫 source != 'manual' 的紀錄（user 手動編輯的譯文受保護）。
    /// 回傳實際寫入 / 更新筆數。</summary>
    public int SyncTranslationsToDb(IAssetMetaStore store, string lang,
        IReadOnlyDictionary<string, long> nameToId)
    {
        if (!IsAvailable || nameToId.Count == 0) return 0;

        int written = 0;
        store.RunInTransaction(() =>
        {
            // 預讀現有 manual 紀錄，避免覆蓋
            var existingManual = new HashSet<long>();
            foreach (var (_, tagId) in nameToId)
            {
                var existing = store.ListTagTranslations(tagId);
                if (existing.Any(t => t.Lang == lang && t.Source == "manual"))
                    existingManual.Add(tagId);
            }

            foreach (var (name, tagId) in nameToId)
            {
                if (existingManual.Contains(tagId)) continue;   // 手動的不動
                var zh = TranslateAny(name, lang);
                if (string.IsNullOrEmpty(zh) || zh == name) continue;   // 沒翻譯 / 跟原文相同 → 不寫
                store.UpsertTagTranslation(tagId, lang, zh, "ehtag");
                written++;
            }
        });
        return written;
    }
}

/// <summary>autocomplete 結果。</summary>
public record ArtistSuggestion(string Slug, string LocalizedName);

/// <summary>nhentai tag + 翻譯後的中文名（null = 翻譯庫沒收錄）。</summary>
public record TranslatedTag(string Type, string Slug, string OriginalName, string? LocalizedName);

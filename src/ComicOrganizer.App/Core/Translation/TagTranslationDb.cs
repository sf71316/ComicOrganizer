using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace ComicOrganizer.App.Core;

/// <summary>
/// EhTagTranslation/Database 翻譯庫 in-memory 索引（#53 Phase 1）。

/// 載入後 lookup 用 Dictionary<(namespace, slug), Entry>。
/// keys 全 lower-case 比對（slug 本身就是 lower-case）。
/// </summary>
public class TagTranslationDb
{
    private readonly Dictionary<(string Ns, string Slug), TranslationEntry> _entries
        = new();

    /// <summary>翻譯庫的 commit sha（從 head.sha 來）。空字串 = 載入失敗或 fallback。</summary>
    public string HeadSha { get; private set; } = "";

    /// <summary>翻譯庫的 schema version（目前是 7）。</summary>
    public int Version { get; private set; }

    /// <summary>條目總數（13 個 namespace 加總）。</summary>
    public int Count => _entries.Count;

    /// <summary>本筆 DB 的來源（給 UI「來源」欄位用）。</summary>
    public TagTranslationDbSource Source { get; private set; } = TagTranslationDbSource.Embedded;

    /// <summary>從 stream（gzip 壓縮的 JSON）載入。</summary>
    public static TagTranslationDb LoadFromGzipStream(Stream gzippedJson)
    {
        var db = new TagTranslationDb();
        using var gz = new GZipStream(gzippedJson, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        db.LoadFromDocument(doc);
        return db;
    }

    /// <summary>從 plain JSON stream 載入（測試用）。</summary>
    public static TagTranslationDb LoadFromJsonStream(Stream jsonStream)
    {
        var db = new TagTranslationDb();
        using var doc = JsonDocument.Parse(jsonStream);
        db.LoadFromDocument(doc);
        return db;
    }

    /// <summary>從 app embedded resource 載入 bundled 版本（隨 exe 一起發布的 fallback）。</summary>
    public static TagTranslationDb LoadFromEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = "ComicOrganizer.App.Resources.db.text.json.gz";
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. Check .csproj EmbeddedResource entry.");
        var db = LoadFromGzipStream(stream);
        db.Source = TagTranslationDbSource.Embedded;
        return db;
    }

    /// <summary>從磁碟檔（gzipped JSON，由 Updater 下載至 BaseDirectory/translations/）載入。
    /// 檔案損壞 / 不存在 / IO 失敗 → 由 caller 自行 fallback 到 LoadFromEmbedded。</summary>
    public static TagTranslationDb LoadFromFile(string path)
    {
        using var fs = File.OpenRead(path);
        var db = LoadFromGzipStream(fs);
        db.Source = TagTranslationDbSource.DiskCache;
        return db;
    }

    private void LoadFromDocument(JsonDocument doc)
    {
        var root = doc.RootElement;

        if (root.TryGetProperty("version", out var vElem) && vElem.ValueKind == JsonValueKind.Number)
            Version = vElem.GetInt32();
        if (root.TryGetProperty("head", out var headElem) && headElem.ValueKind == JsonValueKind.Object
            && headElem.TryGetProperty("sha", out var shaElem) && shaElem.ValueKind == JsonValueKind.String)
            HeadSha = shaElem.GetString() ?? "";

        if (!root.TryGetProperty("data", out var dataArr) || dataArr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var nsItem in dataArr.EnumerateArray())
        {
            if (!nsItem.TryGetProperty("namespace", out var nsElem) ||
                nsElem.ValueKind != JsonValueKind.String) continue;
            var ns = nsElem.GetString();
            if (string.IsNullOrEmpty(ns)) continue;

            // 跳過 meta namespaces（rows / reclass）
            if (ns == "rows" || ns == "reclass") continue;

            if (!nsItem.TryGetProperty("data", out var nsData) ||
                nsData.ValueKind != JsonValueKind.Object) continue;

            foreach (var entry in nsData.EnumerateObject())
            {
                var slug = entry.Name.ToLowerInvariant();
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;

                var name = entry.Value.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                    ? nm.GetString() ?? "" : "";
                var intro = entry.Value.TryGetProperty("intro", out var it) && it.ValueKind == JsonValueKind.String
                    ? it.GetString() ?? "" : "";
                var links = entry.Value.TryGetProperty("links", out var lk) && lk.ValueKind == JsonValueKind.String
                    ? lk.GetString() ?? "" : "";

                _entries[(ns, slug)] = new TranslationEntry(ns, slug, name, intro, links);
            }
        }
    }

    /// <summary>按 (namespace, slug) 查詢。
    /// 自動試 dash ↔ space 兩種形式（nhentai slug 用 dash 如 "big-breasts"，
    /// EhTag key 用 space 如 "big breasts"）。
    /// </summary>
    public bool TryGet(string ns, string slug, out TranslationEntry entry)
    {
        if (string.IsNullOrEmpty(slug))
        {
            entry = default!;
            return false;
        }
        var lower = slug.ToLowerInvariant();
        if (_entries.TryGetValue((ns, lower), out entry!)) return true;

        // dash → space 轉換（nhentai → EhTag）
        if (lower.Contains('-'))
        {
            var spaced = lower.Replace('-', ' ');
            if (_entries.TryGetValue((ns, spaced), out entry!)) return true;
        }
        // space → dash（反向，少見但保險）
        if (lower.Contains(' '))
        {
            var dashed = lower.Replace(' ', '-');
            if (_entries.TryGetValue((ns, dashed), out entry!)) return true;
        }
        entry = default!;
        return false;
    }

    /// <summary>遍歷整個 namespace 的所有條目（給 autocomplete suggest 用）。</summary>
    public IEnumerable<TranslationEntry> EnumerateNamespace(string ns)
    {
        foreach (var kv in _entries)
        {
            if (kv.Key.Ns == ns) yield return kv.Value;
        }
    }
}

/// <summary>翻譯庫單筆條目。</summary>
public record TranslationEntry(string Namespace, string Slug, string Name, string Intro, string Links);

/// <summary>翻譯庫來源 — UI「來源」欄位用，並讓 Updater 知道是不是已載入 disk cache。</summary>
public enum TagTranslationDbSource
{
    /// <summary>隨 exe 一起 bundle 的 embedded resource。</summary>
    Embedded,
    /// <summary>使用者下載到 BaseDirectory/translations/ 的 disk cache。</summary>
    DiskCache
}

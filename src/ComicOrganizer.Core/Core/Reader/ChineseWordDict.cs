using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 繁體中文常用詞表（裁剪自 APCLab/jieba-tw，MIT，詞頻 &gt;= 10、純漢字 2~8 字）。
/// 供直排譯文「避免詞中斷行」的斷點判斷使用（見 <see cref="WordBreakAdvisor"/>）。
/// 內嵌 gz lazy 解壓成 <see cref="HashSet{T}"/>；載入失敗則為空表 → 斷點顧問自然退化成「哪裡都能斷」（＝舊行為，安全）。
/// </summary>
public sealed class ChineseWordDict
{
    private const string ResourceName = "ComicOrganizer.App.Resources.dict.zh_tw_words.txt.gz";

    private static readonly Lazy<ChineseWordDict> _shared = new(() => new ChineseWordDict());
    /// <summary>共用單例（首次存取才解壓）。</summary>
    public static ChineseWordDict Shared => _shared.Value;

    private readonly HashSet<string> _words;

    /// <summary>詞表中最長詞的字數（用於最大匹配的起始長度；空表時退回 8）。</summary>
    public int MaxWordLen { get; }

    /// <summary>詞數（0 = 載入失敗/空表）。</summary>
    public int Count => _words.Count;

    public ChineseWordDict()
    {
        _words = Load(out int maxLen);
        MaxWordLen = maxLen;
    }

    /// <summary>是否為已知詞。</summary>
    public bool Contains(string word) => _words.Contains(word);

    /// <summary>
    /// 從 <paramref name="start"/> 起的最長已知詞長度（最大匹配用）；無匹配回 0。
    /// 只認 2 字以上的詞（單字一律視為可斷，回 0）。
    /// </summary>
    public int LongestWordLength(string text, int start)
    {
        if (string.IsNullOrEmpty(text) || start < 0 || start >= text.Length) return 0;
        int max = Math.Min(MaxWordLen, text.Length - start);
        for (int len = max; len >= 2; len--)
        {
            if (_words.Contains(text.Substring(start, len))) return len;
        }
        return 0;
    }

    private static HashSet<string> Load(out int maxLen)
    {
        maxLen = 0;
        var set = new HashSet<string>();
        try
        {
            var asm = typeof(ChineseWordDict).Assembly;
            using Stream? s = asm.GetManifestResourceStream(ResourceName);
            if (s == null) { maxLen = 8; return set; }
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length < 2) continue;
                set.Add(line);
                if (line.Length > maxLen) maxLen = line.Length;
            }
        }
        catch { /* 空表 → 退化成哪裡都能斷 */ }
        if (maxLen == 0) maxLen = 8;
        return set;
    }
}

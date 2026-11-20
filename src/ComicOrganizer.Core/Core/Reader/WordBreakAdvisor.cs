using System;

namespace ComicOrganizer.App.Core;

/// <summary>
/// 直排譯文「避免詞中斷行」的斷點顧問：對整段文字跑正向最大匹配（FMM），
/// 標出哪些斷點會切斷詞，供排版（<see cref="FontSizeOptimizer"/> 的氣泡均分）把欄界吸附到詞邊界。
/// 純函式、無狀態，便於單元測試。詞表來自 <see cref="ChineseWordDict"/>（空表時所有斷點皆可斷＝舊行為）。
/// </summary>
public static class WordBreakAdvisor
{
    /// <summary>
    /// 回傳長度 = text.Length + 1 的陣列：<c>forbidden[b] == true</c> 表示「在 index b 之前斷行會切斷一個詞」
    /// （b 落在某個 2 字以上詞的內部）。index 語意：[.., b-1] 在前欄、[b, ..] 在後欄。
    /// 兩端（b=0、b=len）永遠 false（不可能切斷詞）。
    /// </summary>
    public static bool[] BuildForbiddenBreaks(string text, ChineseWordDict? dict = null)
    {
        int len = text?.Length ?? 0;
        var forbidden = new bool[len + 1];
        if (len < 2) return forbidden;
        dict ??= ChineseWordDict.Shared;

        int pos = 0;
        while (pos < len)
        {
            int L = dict.LongestWordLength(text!, pos);
            if (L < 2) { pos++; continue; }     // 單字段 → 無內部、無禁斷
            for (int b = pos + 1; b < pos + L; b++) forbidden[b] = true;   // 詞內部各 index 禁斷
            pos += L;
        }
        return forbidden;
    }

    /// <summary>
    /// 在 [<paramref name="minB"/>, <paramref name="maxB"/>] 範圍內、距 <paramref name="target"/> 不超過
    /// <paramref name="radius"/> 字，找最接近 target 的「可斷」index（forbidden 為 false）。
    /// 優先 target 本身，再依距離由近到遠（同距離先試較小一側＝把斷點往前移）。都找不到回 -1（呼叫端應維持原切點）。
    /// </summary>
    public static int NearestAllowedBreak(bool[] forbidden, int target, int radius, int minB, int maxB)
    {
        if (forbidden == null || forbidden.Length == 0) return target;
        for (int d = 0; d <= radius; d++)
        {
            int lo = target - d, hi = target + d;
            if (d == 0)
            {
                if (Allowed(forbidden, target, minB, maxB)) return target;
                continue;
            }
            if (Allowed(forbidden, lo, minB, maxB)) return lo;
            if (Allowed(forbidden, hi, minB, maxB)) return hi;
        }
        return -1;
    }

    private static bool Allowed(bool[] forbidden, int b, int minB, int maxB)
        => b >= minB && b <= maxB && b >= 0 && b < forbidden.Length && !forbidden[b];
}

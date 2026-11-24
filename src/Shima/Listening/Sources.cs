// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Listening;

/// <summary>
/// 聆聽紀錄的「來源」（<see cref="Listen.Source"/>）：Roon 是固定的 "Roon"，其他播放器是 SMTC 的應用程式名稱
/// （Chrome、Spotify、Edge、Firefox…，見 SmtcSource.AppName；Roon 經由 SMTC 出現時 "Roon.exe" 也會變成 "Roon"）。
/// 使用者可以把某些來源（例如瀏覽器裡的影片）排除：不再記錄、不回報，已有的紀錄留著但不進統計。
/// </summary>
public static class ListenSources
{
    /// <summary>設定頁最多列幾個來源。</summary>
    public const int MaxShown = 8;

    /// <summary>名稱的比對方式：不分大小寫、忽略前後空白。</summary>
    public static string Normalize(string? source) => (source ?? "").Trim();

    /// <summary>這個來源算不算；沒有來源名稱的一律算（沒有東西可以排除它）。</summary>
    public static bool Counts(string? source, IEnumerable<string>? excluded)
    {
        var name = Normalize(source);
        if (name.Length == 0 || excluded == null) return true;
        return !excluded.Any(e => string.Equals(Normalize(e), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>排除之後留下的紀錄。沒有排除任何來源就回傳同一份清單。</summary>
    public static IReadOnlyList<Listen> Filter(IReadOnlyList<Listen> listens, IReadOnlyCollection<string>? excluded) =>
        excluded == null || excluded.Count == 0 ? listens : listens.Where(l => Counts(l.Source, excluded)).ToList();

    /// <summary>
    /// 紀錄裡出現過的來源，出現次數多的在前，最多 <see cref="MaxShown"/> 個；
    /// 已被排除的一定要列出來（不然排除了就沒辦法再改回來）：紀錄裡沒有出現過的（紀錄檔刪了、只還原了設定）也列，次數 0。同一個來源大小寫不同的合併，名稱用最常見的寫法。
    /// </summary>
    public static List<(string Name, int Count, bool Counted)> Seen(IEnumerable<Listen> listens, IReadOnlyCollection<string>? excluded)
    {
        var groups = listens
            .Where(l => Normalize(l.Source).Length > 0)
            .GroupBy(l => Normalize(l.Source), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.GroupBy(l => Normalize(l.Source)).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key,
                          Count: g.Count()))
            .Select(g => (g.Name, g.Count, Counted: Counts(g.Name, excluded)))
            .ToList();
        // 被排除的來源不會再被記下來：紀錄裡沒有的，沒有這一步就永遠不會出現、也改不回來
        foreach (var name in (excluded ?? []).Select(Normalize).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))) groups.Add((name, 0, false));
        return groups
            .OrderBy(g => g.Counted)                      // 被排除的先挑
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(MaxShown, groups.Count(g => !g.Counted)))   // 被排除的全部要在，不受上限限制

            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>點一下切換：回傳新的排除清單（沒有就加進去、有就拿掉）。</summary>
    public static List<string> Toggle(IEnumerable<string> excluded, string source)
    {
        var name = Normalize(source);
        var list = excluded.Where(e => Normalize(e).Length > 0).ToList();
        if (list.RemoveAll(e => string.Equals(Normalize(e), name, StringComparison.OrdinalIgnoreCase)) == 0 && name.Length > 0) list.Add(name);
        return list;
    }
}

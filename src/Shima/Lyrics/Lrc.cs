// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <param name="Synced">每一行都有時間；false 表示只有純文字。</param>
public sealed record LyricSheet(IReadOnlyList<LyricLine> Lines, bool Synced)
{
    /// <summary>每一行的中文翻譯／羅馬拼音，跟 Lines 一一對應（沒有的那行是空字串）；整份都沒有就是 null。</summary>
    public IReadOnlyList<string>? Translation { get; init; }
    public IReadOnlyList<string>? Romaji { get; init; }
}

public static partial class Lrc
{
    /// <summary>
    /// LRC 轉成一行一行：「[mm:ss.xx]歌詞」，一行可以有好幾個時間；[ar:…] 這類標籤略過；
    /// [offset:+250] 表示整份提早 250 毫秒。
    /// </summary>
    public static List<LyricLine> Parse(string lrc)
    {
        var lines = new List<LyricLine>();
        double offset = 0;
        foreach (var raw in lrc.Split('\n'))
        {
            var o = Offset().Match(raw);
            if (o.Success) { offset = double.Parse(o.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            var stamps = Stamp().Matches(raw);
            if (stamps.Count == 0) continue;
            var text = Stamp().Replace(raw, "").Trim();
            foreach (Match m in stamps)
            {
                double seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                               + double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromSeconds(Math.Max(0, seconds - offset / 1000)), text));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>
    /// 另一份有時間的文字（翻譯、羅馬拼音）對到歌詞的每一行：時間差在 0.4 秒內才算同一句。
    /// 一行都對不上就回 null。
    /// </summary>
    public static IReadOnlyList<string>? Align(IReadOnlyList<LyricLine> lines, IReadOnlyList<LyricLine> other)
    {
        if (other.Count == 0) return null;
        var result = new string[lines.Count];
        bool any = false;
        for (int i = 0; i < lines.Count; i++)
        {
            int j = LineAt(other, lines[i].Time + TimeSpan.FromSeconds(0.4));
            bool match = j >= 0 && Math.Abs((other[j].Time - lines[i].Time).TotalSeconds) <= 0.4 && other[j].Text.Length > 0;
            result[i] = match ? other[j].Text : "";
            any |= match;
        }
        return any ? result : null;
    }

    /// <summary>純文字歌詞：一行一句，沒有時間。</summary>
    public static List<LyricLine> Plain(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => new LyricLine(TimeSpan.Zero, l)).ToList();

    /// <summary>時間 t 正在唱的那一行（最後一個已經開始的），還沒開始唱是 −1。</summary>
    public static int LineAt(IReadOnlyList<LyricLine> lines, TimeSpan t)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found;
    }

    /// <summary>
    /// 各家會在開頭放「作词 : 某某」「词：某某」這種製作名單，有的還會放一行「歌名 - 藝人」，都不是歌詞，拿掉。
    /// </summary>
    /// <param name="title">歌名；給了才會拿掉「歌名 - 藝人」那一行。</param>
    public static string StripCredits(string lrc, string title = "")
    {
        var name = Titles.Norm(Titles.Bare(title));
        return string.Join("\n", lrc.Replace("\r", "").Split('\n').Where(l =>
        {
            if (Credit().IsMatch(l) || Role().IsMatch(l)) return false;
            if (name.Length == 0) return true;
            var text = Stamp().Replace(l, "").Trim();
            return !(text.Contains(" - ") && Titles.Norm(text).StartsWith(name, StringComparison.Ordinal) && Stamp().Match(l) is { Success: true } m && m.Groups[1].Value.TrimStart('0').Length == 0 && m.Groups[2].Value.StartsWith("00", StringComparison.Ordinal));
        }));
    }

    [GeneratedRegex(@"\[(\d+):(\d+(?:[.:]\d+)?)\]")]
    private static partial Regex Stamp();

    [GeneratedRegex(@"^\s*\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex Offset();

    [GeneratedRegex(@"^(?:\[[^\]]*\])+\s*[^\[\]:：]{1,12}\s[:：]\s")]
    private static partial Regex Credit();

    [GeneratedRegex(@"^(?:\[[^\]]*\])+\s*(?:作?[词詞曲]|[编編]曲|制作人?|製作人?|监制|監製|混音|母带|录音|和声|吉他|贝斯|键盘|弦乐|出品|发行|演唱|原唱|翻唱)\s*[:：]")]
    private static partial Regex Role();
}

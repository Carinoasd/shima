// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text)
{
    /// <summary>逐字時間（接起來等於 Text）；沒有逐字資料是 null，光就依整句長度估。</summary>
    public IReadOnlyList<LyricWord>? Words { get; init; }
}

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
    /// [offset:+250] 表示整份提早 250 毫秒。加強版 LRC 句中的 &lt;mm:ss.xx&gt; 是逐字時間，放進 Words。
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
            var content = Stamp().Replace(raw, "");
            TimeSpan? first = null;
            foreach (Match m in stamps)
            {
                double seconds = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                               + double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                var time = TimeSpan.FromSeconds(seconds);
                first ??= time;
                var (text, words) = WordTiming.ParseEnhanced(content, first.Value);
                // 同一行有好幾個時間（重複的副歌）：字的時間跟著那一次平移；offset 也要算進去
                var shift = time - first.Value - TimeSpan.FromMilliseconds(offset);
                lines.Add(new LyricLine(TimeSpan.FromSeconds(Math.Max(0, seconds - offset / 1000)), text)
                {
                    Words = words?.Select(w => w with { Start = w.Start + shift < TimeSpan.Zero ? TimeSpan.Zero : w.Start + shift }).ToList(),
                });
            }
        }
        // 要穩定排序：雙語 .lrc 同一個時間的兩行，原文在前、翻譯在後，順序不能被打亂
        return lines.OrderBy(l => l.Time).ToList();
    }

    /// <summary>
    /// 本機 .lrc 轉成一份歌詞：同一個時間有兩三行的是雙語歌詞，第一行是原文，接著是翻譯、羅馬拼音
    /// （Shima 自己存的檔案開頭有 [shima:translation,romaji] 說明順序）。
    /// 沒有標記的檔案，要有一半以上的句子都成對才當成雙語，免得開頭好幾行 [00:00.00] 的製作名單被誤認成翻譯。
    /// </summary>
    public static LyricSheet ParseSheet(string lrc)
    {
        lrc = lrc.Replace("\r", "");
        var lines = Parse(lrc);
        var tag = Layers().Match(lrc);
        var groups = new List<List<LyricLine>>();
        foreach (var line in lines)
            if (groups.Count > 0 && groups[^1][0].Time == line.Time) groups[^1].Add(line); else groups.Add([line]);

        string[] layers;
        if (tag.Success) layers = tag.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        else
        {
            var sung = groups.Where(g => g[0].Text.Length > 0).ToList();
            bool paired = sung.Count > 0 && sung.Count(g => g.Count > 1) * 2 >= sung.Count;
            if (!paired) return new LyricSheet(lines, true);
            layers = ["translation", "romaji"];
        }

        var main = groups.Select(g => g[0]).ToList();
        IReadOnlyList<string>? Layer(string name)
        {
            int slot = Array.IndexOf(layers, name) + 1;
            if (slot == 0) return null;
            var texts = groups.Select(g => slot < g.Count ? g[slot].Text : "").ToList();
            return texts.Any(t => t.Length > 0) ? texts : null;
        }
        return new LyricSheet(main, true) { Translation = Layer("translation"), Romaji = Layer("romaji") };
    }

    /// <summary>
    /// 一份同步歌詞寫成 .lrc：使用者調過的時間（shift，正的是提早）直接算進每一句與每個字；
    /// 有翻譯／羅馬拼音的話，同一個時間接著寫一行；有逐字時間就寫成加強版 LRC（&lt;mm:ss.xxx&gt;）。
    /// 時間取到毫秒；兩句同一個時間的話後一句挪 1 毫秒，讀回來才不會被當成翻譯。
    /// </summary>
    public static string Write(LyricSheet sheet, TimeSpan shift, string title = "", string artist = "")
    {
        var sb = new System.Text.StringBuilder();
        static string Tag(string s) => s.Replace("\r", " ").Replace("\n", " ").Replace("[", "(").Replace("]", ")").Trim();
        if (Tag(title) is { Length: > 0 } ti) sb.Append("[ti:").Append(ti).Append("]\n");
        if (Tag(artist) is { Length: > 0 } ar) sb.Append("[ar:").Append(ar).Append("]\n");

        var layers = new List<IReadOnlyList<string>>();
        var names = new List<string>();
        if (sheet.Translation is { } tr) { layers.Add(tr); names.Add("translation"); }
        if (sheet.Romaji is { } ro) { layers.Add(ro); names.Add("romaji"); }
        if (names.Count > 0) sb.Append("[shima:").Append(string.Join(",", names)).Append("]\n");

        long Ms(TimeSpan t) => Math.Max(0, (long)Math.Round((t - shift).TotalMilliseconds));
        long last = -1;
        for (int i = 0; i < sheet.Lines.Count; i++)
        {
            var line = sheet.Lines[i];
            long at = Math.Max(Ms(line.Time), last + 1);
            last = at;
            var stamp = "[" + Clock(at) + "]";
            sb.Append(stamp);
            if (line.Words is { Count: > 0 } words && WordTiming.Fits(line.Text, words))
            {
                for (int k = 0; k < words.Count; k++)
                {
                    long start = Ms(words[k].Start), end = Ms(words[k].End);
                    sb.Append('<').Append(Clock(start)).Append('>').Append(words[k].Text);
                    // 下一個字不是緊接著開始（中間停頓），或是最後一個字：寫出結束時間，長度才讀得回來
                    if (k + 1 == words.Count || Ms(words[k + 1].Start) != end) sb.Append('<').Append(Clock(end)).Append('>');
                }
            }
            else sb.Append(line.Text);
            sb.Append('\n');

            // 後面的層都是空的就不用寫；中間空的那層要留一行空的，順序才對得上
            int used = layers.FindLastIndex(l => i < l.Count && l[i].Length > 0);
            if (line.Text.Length == 0) used = -1;
            for (int k = 0; k <= used; k++) sb.Append(stamp).Append(i < layers[k].Count ? OneLine(layers[k][i]) : "").Append('\n');
        }
        return sb.ToString();
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Clock(long ms) =>
        $"{ms / 60000:00}:{ms / 1000 % 60:00}.{ms % 1000:000}";

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

    [GeneratedRegex(@"^\s*\[shima:([a-z,\s]+)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex Layers();

    [GeneratedRegex(@"^(?:\[[^\]]*\])+\s*[^\[\]:：]{1,12}\s[:：]\s")]
    private static partial Regex Credit();

    [GeneratedRegex(@"^(?:\[[^\]]*\])+\s*(?:作?[词詞曲]|[编編]曲|制作人?|製作人?|监制|監製|混音|母带|录音|和声|吉他|贝斯|键盘|弦乐|出品|发行|演唱|原唱|翻唱)\s*[:：]")]
    private static partial Regex Role();
}

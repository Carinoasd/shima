// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

/// <summary>一句裡的一個字（或一個音節）：什麼時候開始唱、唱多久。</summary>
public sealed record LyricWord(TimeSpan Start, TimeSpan Length, string Text)
{
    public TimeSpan End => Start + Length;
}

/// <summary>
/// 逐字時間軸。一句的 Words 接起來一定等於那句的 Text（空白也算在前一個字裡），
/// 畫面才能直接用字元位置量寬度。
/// </summary>
public static partial class WordTiming
{
    /// <summary>
    /// 網易雲的 yrc：「[開始ms,長度ms](開始ms,長度ms,0)字(…)字」一行一句。
    /// 開頭的製作名單是一行一個 JSON（{"t":…,"c":[…]}），不是歌詞，略過。
    /// </summary>
    public static List<LyricLine> ParseYrc(string yrc)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in yrc.Replace("\r", "").Split('\n'))
        {
            var head = YrcLine().Match(raw);
            if (!head.Success) continue;
            var body = raw[head.Length..];
            var words = new List<LyricWord>();
            var marks = YrcWord().Matches(body);
            for (int k = 0; k < marks.Count; k++)
            {
                var m = marks[k];
                int end = k + 1 < marks.Count ? marks[k + 1].Index : body.Length;
                var text = body[(m.Index + m.Length)..end];
                if (text.Length == 0) continue;
                words.Add(new LyricWord(Ms(m.Groups[1].Value), Ms(m.Groups[2].Value), text));
            }
            var line = string.Concat(words.Select(w => w.Text)).Trim();
            if (line.Length == 0) continue;
            lines.Add(new LyricLine(Ms(head.Groups[1].Value), line) { Words = Fit(line, words) });
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }

    /// <summary>
    /// 加強版 LRC 一行的內容（行首的 [mm:ss.xx] 已經拿掉）：「&lt;00:12.00&gt;字&lt;00:12.40&gt;字&lt;00:13.00&gt;」。
    /// 回傳去掉字的時間後的文字，以及逐字資料（沒有字的時間就是 null）。
    /// </summary>
    /// <param name="lineStart">這行的時間：第一個字的時間之前的字從這裡開始唱。</param>
    public static (string Text, IReadOnlyList<LyricWord>? Words) ParseEnhanced(string content, TimeSpan lineStart)
    {
        var marks = WordStamp().Matches(content);
        if (marks.Count == 0) return (content.Trim(), null);

        var pieces = new List<(TimeSpan Start, string Text)>();
        if (marks[0].Index > 0) pieces.Add((lineStart, content[..marks[0].Index]));
        for (int k = 0; k < marks.Count; k++)
        {
            int from = marks[k].Index + marks[k].Length, to = k + 1 < marks.Count ? marks[k + 1].Index : content.Length;
            pieces.Add((Stamp(marks[k]), content[from..to]));
        }

        // 每個字唱到下一個時間為止；最後一個字後面沒有結束時間的話，給它其他字的平均長度
        var words = new List<LyricWord>();
        for (int k = 0; k < pieces.Count; k++)
        {
            if (pieces[k].Text.Length == 0) continue;
            var length = k + 1 < pieces.Count ? pieces[k + 1].Start - pieces[k].Start : TimeSpan.Zero;
            words.Add(new LyricWord(pieces[k].Start, length < TimeSpan.Zero ? TimeSpan.Zero : length, pieces[k].Text));
        }
        if (pieces[^1].Text.Length > 0 && words.Count > 0)
        {
            var known = words.Take(words.Count - 1).Where(w => w.Length > TimeSpan.Zero).ToList();
            var guess = known.Count > 0 ? TimeSpan.FromTicks((long)known.Average(w => w.Length.Ticks)) : TimeSpan.FromSeconds(0.5);
            words[^1] = words[^1] with { Length = guess };
        }
        var text = string.Concat(words.Select(w => w.Text)).Trim();
        return (text, text.Length == 0 ? null : Fit(text, words));
    }

    /// <summary>
    /// 把另一份逐字歌詞（yrc）掛到 LRC 的每一句上。句子本身、時間、翻譯的對齊都還是照 LRC；
    /// 只有時間相近（1.5 秒內）而且字一樣（不管空白）的句子才掛，對不上的維持整句估算，總比掃光亂跳好。
    /// </summary>
    public static List<LyricLine> Attach(IReadOnlyList<LyricLine> lines, IReadOnlyList<LyricLine> timed)
    {
        var result = new List<LyricLine>(lines.Count);
        foreach (var line in lines)
        {
            IReadOnlyList<LyricWord>? best = null;
            double gap = 1.5;
            foreach (var other in timed)
            {
                double d = Math.Abs((other.Time - line.Time).TotalSeconds);
                if (d > gap || other.Words is not { Count: > 0 } words || Fit(line.Text, words) is not { } fitted) continue;
                best = fitted;
                gap = d;
            }
            result.Add(best != null ? line with { Words = best } : line);
        }
        return result;
    }

    /// <summary>
    /// 把逐字資料對到 text 的字元上，讓接起來剛好等於 text：空白歸給前一個字，只有空白的字拿掉。
    /// 不是空白的字對不上就回 null。
    /// </summary>
    public static IReadOnlyList<LyricWord>? Fit(string text, IEnumerable<LyricWord> words)
    {
        var result = new List<LyricWord>();
        int pos = 0;
        foreach (var w in words)
        {
            var want = w.Text.Where(c => !char.IsWhiteSpace(c)).ToArray();
            if (want.Length == 0) continue;
            int start = pos;
            // 前一個字之後、這個字之前的空白（只有第一個字會遇到，其他的已經被前一個字吃掉）
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            foreach (var c in want)
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
                if (pos >= text.Length || text[pos] != c) return null;
                pos++;
            }
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            result.Add(w with { Text = text[start..pos] });
        }
        return result.Count > 0 && pos == text.Length ? result : null;
    }

    /// <summary>words 接起來剛好是 text（快取或外面來的資料不一定可信）。</summary>
    public static bool Fits(string text, IReadOnlyList<LyricWord>? words)
    {
        if (words is not { Count: > 0 }) return false;
        int n = 0;
        foreach (var w in words) n += w.Text.Length;
        return n == text.Length;
    }

    /// <summary>
    /// 時間 t 唱到第幾個字、那個字唱了多少（0…1）。還沒開始是 (0, 0)；字和字之間停在前一個字的結尾；全部唱完是 (最後一個, 1)。
    /// </summary>
    public static (int Index, double Fraction) At(IReadOnlyList<LyricWord> words, TimeSpan t)
    {
        if (words.Count == 0 || t < words[0].Start) return (0, 0);
        int i = 0;
        while (i + 1 < words.Count && words[i + 1].Start <= t) i++;
        var w = words[i];
        double f = w.Length <= TimeSpan.Zero ? 1 : Math.Clamp((t - w.Start) / w.Length, 0, 1);
        return (i, f);
    }

    /// <summary>時間 t 唱到這句的第幾個字元（可以是小數），給量寬度用。</summary>
    public static double CharsAt(IReadOnlyList<LyricWord> words, TimeSpan t)
    {
        var (index, fraction) = At(words, t);
        double chars = 0;
        for (int k = 0; k < index; k++) chars += words[k].Text.Length;
        return words.Count == 0 ? 0 : chars + words[index].Text.Length * fraction;
    }

    /// <summary>時間 t 時掃光走到整句的多少（0…1），以字元數近似寬度。外掛那邊沒有字型可以量，用這個就好。</summary>
    public static double Progress(IReadOnlyList<LyricWord> words, TimeSpan t)
    {
        int total = 0;
        foreach (var w in words) total += w.Text.Length;
        return total == 0 ? 0 : Math.Clamp(CharsAt(words, t) / total, 0, 1);
    }

    /// <summary>[[開始秒, 長度秒, "字"], …]，秒數取到毫秒。</summary>
    public static JsonArray ToJson(IReadOnlyList<LyricWord> words)
    {
        var array = new JsonArray();
        foreach (var w in words)
            array.Add(new JsonArray(Math.Round(w.Start.TotalSeconds, 3), Math.Round(w.Length.TotalSeconds, 3), w.Text));
        return array;
    }

    /// <summary><see cref="ToJson"/> 的反向；格式不對的項目略過。</summary>
    public static List<LyricWord> FromJson(JsonArray array)
    {
        var words = new List<LyricWord>();
        foreach (var item in array)
        {
            if (item is not JsonArray { Count: >= 3 } a) continue;
            try { words.Add(new LyricWord(TimeSpan.FromSeconds(a[0]!.GetValue<double>()), TimeSpan.FromSeconds(a[1]!.GetValue<double>()), a[2]!.GetValue<string>())); }
            catch (Exception) { }
        }
        return words;
    }

    private static TimeSpan Ms(string digits) => TimeSpan.FromMilliseconds(long.Parse(digits, CultureInfo.InvariantCulture));

    private static TimeSpan Stamp(Match m) =>
        TimeSpan.FromSeconds(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                           + double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture));

    [GeneratedRegex(@"^\s*\[(\d+),(\d+)\]")]
    private static partial Regex YrcLine();

    [GeneratedRegex(@"\((\d+),(\d+),-?\d+\)")]
    private static partial Regex YrcWord();

    [GeneratedRegex(@"<(\d+):(\d+(?:[.:]\d+)?)>")]
    private static partial Regex WordStamp();
}

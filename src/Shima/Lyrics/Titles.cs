// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

/// <summary>歌名與藝人名的整理：去雜訊、去版本括號、判斷是不是同一首、同一版。</summary>
public static partial class Titles
{
    /// <summary>歌名與藝人進來先截到這麼長：網頁能控制標題，太長的輸入會讓下面的正規表示式跑很久（實測 5000 個括號就要好幾秒）。</summary>
    public const int MaxLength = 512;

    /// <summary>每個正規表示式最久跑這麼久；逾時就當作這一步沒有處理。</summary>
    private const int TimeoutMs = 200;

    /// <summary>截到 <see cref="MaxLength"/>；不要把代理字元對（emoji、罕用字）切成一半。</summary>
    public static string Cap(string? s)
    {
        s ??= "";
        if (s.Length <= MaxLength) return s;
        int end = char.IsHighSurrogate(s[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return s[..end];
    }

    /// <summary>逾時就用原本的字串，不讓例外往外丟。</summary>
    private static string Replace(Regex regex, string input, string replacement)
    {
        try { return regex.Replace(input, replacement); }
        catch (RegexMatchTimeoutException) { return input; }
    }

    /// <summary>逾時就當作沒有找到。</summary>
    private static Match Find(Regex regex, string input)
    {
        try { return regex.Match(input); }
        catch (RegexMatchTimeoutException) { return Match.Empty; }
    }

    /// <summary>比對用：全形轉半形、轉小寫，只留文字與數字。</summary>
    public static string Norm(string s) =>
        new(Wellformed(Cap(s)).Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// 孤立的代理字元（標題被截在 emoji 中間）換成 U+FFFD：string.Normalize 遇到會丟 ArgumentException，
    /// 一首歌名稱不對就連歌詞都查不了。
    /// </summary>
    public static string Wellformed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
            if (!char.IsSurrogate(s[i])) continue;
            var fixedUp = s.ToCharArray();
            for (int k = i; k < fixedUp.Length; k++)
            {
                if (char.IsHighSurrogate(fixedUp[k]) && k + 1 < fixedUp.Length && char.IsLowSurrogate(fixedUp[k + 1])) k++;
                else if (char.IsSurrogate(fixedUp[k])) fixedUp[k] = '\uFFFD';
            }
            return new string(fixedUp);
        }
        return s;
    }

    /// <summary>去掉歌名結尾的括號（半形全形都算）：「Song (104期Ver.)」→「Song」。</summary>
    public static string Bare(string title)
    {
        title = Cap(title);
        var bare = Replace(TrailingBrackets(), title, "").Trim();
        return bare.Length == 0 ? title.Trim() : bare;
    }

    /// <summary>
    /// 分辨同一首歌不同版本的記號，取自歌名結尾的括號：有數字就取數字（「104期Ver.」＝「104th Ver.」→ "104"），
    /// 沒有就取去掉 ver 的文字（「SAKURA Ver.」→ "sakura"）。沒有括號是空字串。
    /// </summary>
    public static string VersionTag(string title)
    {
        var t = Cap(title).Trim();
        var bare = Bare(t);
        if (bare.Length >= t.Length) return "";
        var tail = Wellformed(t[bare.Length..]).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var digits = new string(tail.Where(char.IsDigit).ToArray());
        if (digits.Length > 0) return digits;
        return new string(tail.Where(char.IsLetterOrDigit).ToArray()).Replace("version", "").Replace("ver", "");
    }

    /// <summary>多位藝人取第一位：Roon 用「A / B」，其他地方用「A, B」「A feat. B」「A & B」。</summary>
    public static string MainArtist(string artist)
    {
        artist = Cap(artist);
        string[] parts;
        try { parts = ArtistSplit().Split(artist); }
        catch (RegexMatchTimeoutException) { return artist.Trim(); }
        return parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : artist.Trim();
    }

    public static bool SameArtist(string a, string b)
    {
        string x = Norm(a), y = Norm(b);
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    /// <summary>
    /// 播放器加在歌名上的東西拿掉：「(Official Video)」「【MV】」「[4K]」這類；
    /// 瀏覽器裡的「藝人 - 歌名」「歌名 / 藝人」拆開（<paramref name="split"/>）；頻道名的「- Topic」「VEVO」去掉。
    /// </summary>
    public static (string Artist, string Title) Clean(string artist, string title, bool split)
    {
        title = Cap(title);
        artist = Cap(artist);
        title = Replace(Noise(), title, " ");
        title = Replace(Suffix(), title, "");
        artist = Replace(Channel(), artist, "");

        if (split)
        {
            var dash = Find(Dash(), title);
            var slash = Find(Slash(), title);
            if (dash.Success && dash.Index > 0 && dash.Index + dash.Length < title.Length)
            {
                artist = title[..dash.Index];
                title = title[(dash.Index + dash.Length)..];
            }
            else if (slash.Success && slash.Index > 0 && slash.Index + slash.Length < title.Length)
            {
                artist = title[(slash.Index + slash.Length)..];
                title = title[..slash.Index];
            }
        }
        return (Replace(Spaces(), artist, " ").Trim(), Replace(Spaces(), title, " ").Trim());
    }

    [GeneratedRegex(@"(?:\s*[\(\[（［【〈＜][^\(\[（［【〈＜]*[\)\]）］】〉＞])+\s*$", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex TrailingBrackets();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|x|&|/)\s+|[,、]\s*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex ArtistSplit();

    [GeneratedRegex(@"[\(\[（［【「『][^\)\]）］】」』]*(?:official|video|audio|lyrics?|visuali[sz]er|remaster(?:ed)?|\bm/?v\b|\bpv\b|\bhd\b|\b4k\b|full ver|歌詞|字幕|中字|高音質|公式)[^\)\]）］】」』]*[\)\]）］】」』]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\s+-\s+[^-]*(?:remaster|version|edit|mix|mono|stereo|acoustic|demo)[^-]*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s*-\s*Topic$|\s*VEVO$|\s+Official(?:\s+(?:Channel|YouTube\s+Channel))?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Channel();

    [GeneratedRegex(@"\s+[-–—]\s+", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Dash();

    [GeneratedRegex(@"\s+[/／]\s+", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Slash();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Spaces();
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

/// <summary>歌名與藝人名的整理：去雜訊、去版本括號、判斷是不是同一首、同一版。</summary>
public static partial class Titles
{
    /// <summary>比對用：全形轉半形、轉小寫，只留文字與數字。</summary>
    public static string Norm(string s) =>
        new(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>去掉歌名結尾的括號（半形全形都算）：「Song (104期Ver.)」→「Song」。</summary>
    public static string Bare(string title)
    {
        var bare = TrailingBrackets().Replace(title, "").Trim();
        return bare.Length == 0 ? title.Trim() : bare;
    }

    /// <summary>
    /// 分辨同一首歌不同版本的記號，取自歌名結尾的括號：有數字就取數字（「104期Ver.」＝「104th Ver.」→ "104"），
    /// 沒有就取去掉 ver 的文字（「SAKURA Ver.」→ "sakura"）。沒有括號是空字串。
    /// </summary>
    public static string VersionTag(string title)
    {
        var t = title.Trim();
        var bare = Bare(t);
        if (bare.Length >= t.Length) return "";
        var tail = t[bare.Length..].Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var digits = new string(tail.Where(char.IsDigit).ToArray());
        if (digits.Length > 0) return digits;
        return new string(tail.Where(char.IsLetterOrDigit).ToArray()).Replace("version", "").Replace("ver", "");
    }

    /// <summary>多位藝人取第一位：Roon 用「A / B」，其他地方用「A, B」「A feat. B」「A & B」。</summary>
    public static string MainArtist(string artist)
    {
        var parts = ArtistSplit().Split(artist);
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
        title = Noise().Replace(title, " ");
        title = Suffix().Replace(title, "");
        artist = Channel().Replace(artist, "");

        if (split)
        {
            var dash = Dash().Match(title);
            var slash = Slash().Match(title);
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
        return (Spaces().Replace(artist, " ").Trim(), Spaces().Replace(title, " ").Trim());
    }

    [GeneratedRegex(@"(?:\s*[\(\[（［【〈＜][^\(\[（［【〈＜]*[\)\]）］】〉＞])+\s*$")]
    private static partial Regex TrailingBrackets();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|x|&|/)\s+|[,、]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    [GeneratedRegex(@"[\(\[（［【「『][^\)\]）］】」』]*(?:official|video|audio|lyrics?|visuali[sz]er|remaster(?:ed)?|\bm/?v\b|\bpv\b|\bhd\b|\b4k\b|full ver|歌詞|字幕|中字|高音質|公式)[^\)\]）］】」』]*[\)\]）］】」』]", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\s+-\s+[^-]*(?:remaster|version|edit|mix|mono|stereo|acoustic|demo)[^-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s*-\s*Topic$|\s*VEVO$|\s+Official(?:\s+(?:Channel|YouTube\s+Channel))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Channel();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex Dash();

    [GeneratedRegex(@"\s+[/／]\s+")]
    private static partial Regex Slash();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}

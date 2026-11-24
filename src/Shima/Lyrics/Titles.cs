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
        // 孤立的代理字元（網頁把標題截在 emoji 中間）在這裡就換掉：後面任何一個 string.Normalize 遇到它都會丟例外
        if (s.Length <= MaxLength) return Wellformed(s);
        int end = char.IsHighSurrogate(s[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return Wellformed(s[..end]);
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
        var parts = SplitArtists(artist);
        return parts.Count > 0 ? parts[0] : Cap(artist).Trim();
    }

    /// <summary>把多位藝人字串拆成個別名字（已去空白與空項）。</summary>
    private static List<string> SplitArtists(string artist)
    {
        artist = Cap(artist);
        string[] parts;
        try { parts = ArtistSplit().Split(artist); }
        catch (RegexMatchTimeoutException) { return artist.Trim().Length > 0 ? [artist.Trim()] : []; }
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    public static bool SameArtist(string a, string b)
    {
        string x = Norm(a), y = Norm(b);
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    /// <summary>判斷標題一側是不是在寫頻道藝人（用來辨認日系「歌名 - 藝人」倒過來的寫法）。</summary>
    private static bool ArtistOverlap(string side, string channel)
    {
        string ch = Norm(channel);
        if (ch.Length < 2) return false;
        foreach (var part in SplitArtists(CleanArtist(side)))
        {
            string p = Norm(part);
            if (p.Length == 0) continue;
            if (p == ch) return true;
            if (p.Length >= 3 && ch.Contains(p, StringComparison.Ordinal)) return true;
            if (ch.Length >= 3 && p.Contains(ch, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>藝人字串裡常夾帶的作品名（from 超かぐや姫！）與聲優標註（(cv.早見沙織)）拿掉。</summary>
    public static string CleanArtist(string artist)
    {
        artist = Cap(artist);
        var cleaned = Replace(Franchise(), artist, "");
        cleaned = Replace(CvTag(), cleaned, " ");
        cleaned = Replace(Channel(), cleaned, "");
        cleaned = Replace(Spaces(), cleaned, " ").Trim();
        return cleaned.Length > 0 ? cleaned : Replace(Spaces(), artist, " ").Trim();
    }

    /// <summary>
    /// 播放器加在歌名上的東西拿掉：「(Official Video)」「【MV】」「[4K]」這類；
    /// 瀏覽器裡的「藝人 - 歌名」「歌名 - 藝人(cv.…)」「歌名 / 藝人」「藝人「歌名」」拆開（<paramref name="split"/>）；
    /// 頻道名的「- Topic」「VEVO」去掉。
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
            title = Replace(TrailingMv(), title, "");
            title = Replace(Spaces(), title, " ").Trim();

            var dash = Find(Dash(), title);
            var slash = Find(Slash(), title);
            if (dash.Success && dash.Index > 0 && dash.Index + dash.Length < title.Length)
            {
                var left = title[..dash.Index].Trim();
                var right = title[(dash.Index + dash.Length)..].Trim();
                // 日系 MV 常見「歌名 - 藝人 / 角色(cv.聲優) from 作品」：右邊帶 cv 標註或對到頻道名，且左邊不是頻道名
                bool reversed = !ArtistOverlap(left, artist) &&
                                (Find(CvTag(), right).Success || ArtistOverlap(right, artist));
                if (reversed)
                {
                    artist = CleanArtist(right);
                    title = left;
                }
                else
                {
                    artist = CleanArtist(left);
                    title = right;
                }
            }
            else if (slash.Success && slash.Index > 0 && slash.Index + slash.Length < title.Length)
            {
                artist = CleanArtist(title[(slash.Index + slash.Length)..]);
                title = title[..slash.Index];
            }
            else if (Find(QuoteTitle(), title) is { Success: true } quote)
            {
                var before = quote.Groups[1].Value.Trim();
                var inside = quote.Groups[2].Value.Trim();
                if (inside.Length > 0)
                {
                    if (before.Length > 0 && !Find(MetaPrefix(), before).Success)
                        artist = CleanArtist(before);
                    title = inside;
                }
            }
        }
        return (Replace(Spaces(), artist, " ").Trim(), Replace(Spaces(), title, " ").Trim());
    }

    /// <summary>
    /// 瀏覽器標題第一次查不到歌詞時的備選組合（依精準度排序、最多 4 組）：
    /// 多位藝人的第二位、聲優名、雙語標題的前半、去 from 作品後綴、以及「藝人 - 歌名」左右互換，最後才是不帶藝人的純歌名。
    /// </summary>
    public static List<(string Artist, string Title)> Alternates(string rawArtist, string rawTitle, bool split)
    {
        rawArtist = Cap(rawArtist);
        rawTitle = Cap(rawTitle);
        var (primaryArtist, primaryTitle) = Clean(rawArtist, rawTitle, split);
        var result = new List<(string Artist, string Title)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Remember(string a, string t)
        {
            a = Replace(Spaces(), a, " ").Trim();
            t = Replace(Spaces(), t, " ").Trim();
            string nt = Norm(Bare(t));
            if (nt.Length == 0) return;
            string key = $"{Norm(MainArtist(a))}|{nt}";
            if (seen.Add(key)) result.Add((a, t));
        }

        // 先把主要結果佔住，不重複產生
        string primaryKey = $"{Norm(MainArtist(primaryArtist))}|{Norm(Bare(primaryTitle))}";
        seen.Add(primaryKey);

        if (!split || primaryTitle.Length == 0) return result;

        // 1. 標題後半帶 from 作品名（「星降る海 from 超かぐや姫！」）或雙語斜線（「夜に駆ける / Racing into the Night」）
        var titleNoFrom = Replace(Franchise(), primaryTitle, "").Trim();
        if (titleNoFrom.Length > 0 && titleNoFrom != primaryTitle)
            Remember(primaryArtist, titleNoFrom);
        var slashInTitle = Find(Slash(), primaryTitle);
        if (slashInTitle.Success && slashInTitle.Index > 0)
            Remember(primaryArtist, primaryTitle[..slashInTitle.Index]);

        // 2. 多位藝人的其他名字（例如「Aqu3ra / 月見ヤチヨ」的「月見ヤチヨ」）
        var artistParts = SplitArtists(primaryArtist);
        for (int i = 1; i < artistParts.Count && i < 3; i++)
            Remember(artistParts[i], primaryTitle);

        // 3. 標題或藝人裡標註的聲優名（例如「(cv.早見沙織)」→「早見沙織」）
        var cv = Find(CvTag(), rawTitle);
        if (!cv.Success) cv = Find(CvTag(), rawArtist);
        if (cv.Success)
        {
            var actor = Replace(Spaces(), cv.Groups[1].Value, " ").Trim();
            if (actor.Length > 0) Remember(actor, primaryTitle);
        }

        // 4. 破折號拆開但順序可能倒過來（例如粉絲上傳「歌名 - 藝人」沒有 cv 也沒對上頻道名）
        var cleanedTitle = Replace(Spaces(), Replace(TrailingMv(), Replace(Suffix(), Replace(Noise(), rawTitle, " "), ""), ""), " ").Trim();
        if (Find(Dash(), cleanedTitle).Success)
            Remember(CleanArtist(primaryTitle), primaryArtist);

        // 5. 最後備援：只用歌名（呼叫端會搭配長度限制，避免誤配）
        if (primaryArtist.Length > 0 && Norm(Bare(primaryTitle)).Length >= 2)
            Remember("", primaryTitle);

        return result.Count > 4 ? result.GetRange(0, 4) : result;
    }

    [GeneratedRegex(@"(?:\s*[\(\[（［【〈＜][^\(\[（［【〈＜]*[\)\]）］】〉＞])+\s*$", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex TrailingBrackets();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|x|&|/)\s+|[,、]\s*", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex ArtistSplit();

    [GeneratedRegex(@"[\(\[（［【「『][^\)\]）］】」』]*(?:official|video|audio|lyrics?|visuali[sz]er|remaster(?:ed)?|\bm/?v\b|\bpv\b|\bhd\b|\b4k\b|full ver|歌詞|字幕|中字|高音質|公式)[^\)\]）］】」』]*[\)\]）］】」』]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Noise();

    [GeneratedRegex(@"\s+-\s+[^-]*(?:remaster|version|edit|mix|mono|stereo|acoustic|demo)[^-]*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s+(?:official\s+(?:music\s+)?video|official\s+m/?v|official\s+audio|lyric\s+video|music\s+video)\s*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex TrailingMv();

    [GeneratedRegex(@"\s*-\s*Topic$|\s*VEVO$|\s+Official(?:\s+(?:Channel|YouTube\s+Channel))?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Channel();

    [GeneratedRegex(@"\s+[-–—]\s+", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Dash();

    [GeneratedRegex(@"\s+[/／]\s+", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Slash();

    [GeneratedRegex(@"[\(（]\s*cv[\.:：\s]\s*([^\)）]{1,80})[\)）]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex CvTag();

    [GeneratedRegex(@"\s+from\s+[^\r\n]+$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Franchise();

    [GeneratedRegex(@"^([^「」]{1,120})「([^「」]{1,120})」\s*$", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex QuoteTitle();

    [GeneratedRegex(@"(?:アニメ|anime|主題歌|オープニング|エンディング|挿入歌|イメージソング|テーマ|\bop\b|\bed\b|\bost\b|『)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex MetaPrefix();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, matchTimeoutMilliseconds: TimeoutMs)]
    private static partial Regex Spaces();
}

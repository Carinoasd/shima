// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Lyrics;

/// <summary>搜尋結果裡的一首歌。Quality：3 有時間軸、2 純文字、0 沒有歌詞。</summary>
public sealed record SongCandidate(string Title, string Artist, double Duration, int Quality = 3);

public static class SongPicker
{
    /// <summary>
    /// 從搜尋結果挑出這首歌：歌名相同、藝人相符、長度差 <paramref name="tolerance"/> 秒內（預設 2 秒）。
    /// 同一首歌的各個版本長度幾乎一樣、只差幾句，靠長度分不出來，所以看歌名結尾的版本記號：
    /// 同版本最優先（Exact），其次是沒有版本記號的原曲，絕不拿別的版本。
    /// 放寬容許差（例如 YouTube MV 多了片頭片尾）時，差距超過 2 秒的結果一律標成非 Exact。
    /// </summary>
    /// <remarks>
    /// 藝人名對不上不一定是別首歌：播放器常給羅馬字（kessoku band），資料庫登記的是原文（結束バンド）。
    /// 所以歌名相同、長度又差不到 1.5 秒時也算，只是排在藝人名相符的後面。
    /// </remarks>
    public static (int Index, bool Exact) Pick(IReadOnlyList<SongCandidate> items, string title, string artist, double duration, double tolerance = 2)
    {
        var name = Titles.Norm(Titles.Bare(title));
        if (name.Length == 0) return (-1, false);
        var tag = Titles.VersionTag(title);
        int best = -1, bestScore = 0;
        double bestDiff = double.MaxValue;
        bool bestExact = false;
        for (int i = 0; i < items.Count; i++)
        {
            var c = items[i];
            if (c.Quality <= 0) continue;
            bool hasDuration = duration > 0 && c.Duration > 0;
            double diff = hasDuration ? Math.Abs(c.Duration - duration) : 0;
            if (hasDuration && diff > tolerance) continue;
            if (Titles.Norm(Titles.Bare(c.Title)) != name) continue;
            bool sameArtist = artist.Length == 0 || c.Artist.Length == 0 || Titles.SameArtist(c.Artist, artist);
            if (!sameArtist && !(hasDuration && diff <= 1.5)) continue;

            var theirs = Titles.VersionTag(c.Title);
            int score;
            if (theirs == tag) score = 20;
            else if (theirs.Length == 0) score = 10;
            else continue;
            score += c.Quality + (sameArtist ? 5 : 0);
            if (score > bestScore || (score == bestScore && diff < bestDiff))
            {
                best = i;
                bestScore = score;
                bestDiff = diff;
                bestExact = theirs == tag && (!hasDuration || diff <= 2);
            }
        }
        return (best, bestExact);
    }
}

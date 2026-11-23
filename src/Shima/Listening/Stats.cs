// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Listening;

public enum StatsPeriod { Week, Month, Year, All }

/// <summary>排行榜的一列：名字（歌名／藝人／專輯）、副名（歌的藝人、專輯的藝人）、聽了多久、幾次。</summary>
public sealed record Ranked(string Name, string Detail, TimeSpan Time, int Plays);

public sealed record StatsSummary(
    DateTime From,
    DateTime To,
    TimeSpan Total,
    int Plays,
    IReadOnlyList<Ranked> TopTracks,
    IReadOnlyList<Ranked> TopArtists,
    IReadOnlyList<Ranked> TopAlbums,
    IReadOnlyList<(DateTime Day, TimeSpan Time)> Daily,
    TimeSpan[] Hours,
    int Streak);

/// <summary>
/// 聆聽紀錄的統計，全部是純函式。日期與時段都看紀錄寫下時的當地時間（Start.DateTime），
/// 一首歌整首算在開始的那一天、那個小時。
/// </summary>
public static class Stats
{
    /// <summary>期間的起點（含）與終點（不含）。全部：從最早一筆那天開始。</summary>
    public static (DateTime From, DateTime To) Range(StatsPeriod period, DateTime now, DayOfWeek firstDay, DateTime? earliest = null)
    {
        var today = now.Date;
        var to = today.AddDays(1);
        return period switch
        {
            StatsPeriod.Week => (today.AddDays(-(((int)today.DayOfWeek - (int)firstDay + 7) % 7)), to),
            StatsPeriod.Month => (new DateTime(today.Year, today.Month, 1), to),
            StatsPeriod.Year => (new DateTime(today.Year, 1, 1), to),
            _ => (earliest?.Date is { } first && first < today ? first : today, to),
        };
    }

    public static StatsSummary Compute(IEnumerable<Listen> listens, StatsPeriod period, DateTime now, DayOfWeek firstDay, int top = 5)
    {
        var all = listens.ToList();
        DateTime? earliest = all.Count > 0 ? all.Min(l => l.Start.DateTime) : null;
        var (from, to) = Range(period, now, firstDay, earliest);
        var inRange = all.Where(l => l.Start.DateTime >= from && l.Start.DateTime < to).ToList();

        return new StatsSummary(
            from, to,
            Sum(inRange),
            inRange.Count,
            TopTracks(inRange, top),
            TopArtists(inRange, top),
            TopAlbums(inRange, top),
            Daily(inRange, from, to),
            Hours(inRange),
            Streak(all, now));
    }

    public static TimeSpan Sum(IEnumerable<Listen> listens) => TimeSpan.FromSeconds(listens.Sum(l => Math.Max(0, l.Listened)));

    private static string Fold(string s) => s.Trim().ToLowerInvariant();

    /// <summary>依聽的時間排前幾名；時間一樣就比次數，再比名字，結果固定。名字取最常出現的寫法。</summary>
    private static List<Ranked> Rank(IEnumerable<Listen> listens, Func<Listen, string> key, Func<Listen, string> name, Func<Listen, string> detail, int top) =>
        listens.GroupBy(key)
            .Select(g => new Ranked(
                g.GroupBy(name).OrderByDescending(n => n.Count()).First().Key.Trim(),
                g.GroupBy(detail).OrderByDescending(n => n.Count()).First().Key.Trim(),
                Sum(g), g.Count()))
            .OrderByDescending(r => r.Time).ThenByDescending(r => r.Plays).ThenBy(r => r.Name, StringComparer.Ordinal)
            .Take(top).ToList();

    public static List<Ranked> TopTracks(IEnumerable<Listen> listens, int top = 5) =>
        Rank(listens, l => Fold(l.Title) + "\n" + Fold(l.Artist), l => l.Title, l => l.Artist, top);

    public static List<Ranked> TopArtists(IEnumerable<Listen> listens, int top = 5) =>
        Rank(listens.Where(l => l.Artist.Trim().Length > 0), l => Fold(l.Artist), l => l.Artist, _ => "", top);

    /// <summary>專輯以「專輯＋藝人」分，不然每張叫 Best 的精選輯會混在一起。</summary>
    public static List<Ranked> TopAlbums(IEnumerable<Listen> listens, int top = 5) =>
        Rank(listens.Where(l => l.Album.Trim().Length > 0), l => Fold(l.Album) + "\n" + Fold(l.Artist), l => l.Album, l => l.Artist, top);

    /// <summary>from 到 to（不含）之間每一天聽了多久，沒聽的天是 0。</summary>
    public static List<(DateTime Day, TimeSpan Time)> Daily(IEnumerable<Listen> listens, DateTime from, DateTime to)
    {
        var byDay = listens.GroupBy(l => l.Start.DateTime.Date).ToDictionary(g => g.Key, Sum);
        var days = new List<(DateTime, TimeSpan)>();
        for (var d = from.Date; d < to; d = d.AddDays(1)) days.Add((d, byDay.GetValueOrDefault(d)));
        return days;
    }

    /// <summary>一天 24 個小時各聽了多久。</summary>
    public static TimeSpan[] Hours(IEnumerable<Listen> listens)
    {
        var hours = new TimeSpan[24];
        foreach (var l in listens) hours[l.Start.DateTime.Hour] += TimeSpan.FromSeconds(Math.Max(0, l.Listened));
        return hours;
    }

    /// <summary>
    /// 連續聽歌天數：到今天為止，每天都有聽的連續天數。今天還沒聽不算斷，從昨天往回數；
    /// 昨天也沒聽就是 0。
    /// </summary>
    public static int Streak(IEnumerable<Listen> listens, DateTime now)
    {
        var days = listens.Select(l => l.Start.DateTime.Date).ToHashSet();
        var day = now.Date;
        if (!days.Contains(day)) day = day.AddDays(-1);
        int streak = 0;
        while (days.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    /// <summary>
    /// 長條圖的每一根：週、月是一天一根；年是一個月一根；全部是一年一根（只有一年以內就一個月一根）。
    /// Start 是那一根的第一天。
    /// </summary>
    public static List<(DateTime Start, TimeSpan Time)> Bars(IReadOnlyList<(DateTime Day, TimeSpan Time)> daily, StatsPeriod period)
    {
        bool byYear = period == StatsPeriod.All && daily.Count > 0 && daily[0].Day.Year != daily[^1].Day.Year;
        if (period is StatsPeriod.Week or StatsPeriod.Month) return daily.Select(d => (d.Day, d.Time)).ToList();
        return daily
            .GroupBy(d => byYear ? new DateTime(d.Day.Year, 1, 1) : new DateTime(d.Day.Year, d.Day.Month, 1))
            .Select(g => (g.Key, g.Aggregate(TimeSpan.Zero, (t, d) => t + d.Time)))
            .ToList();
    }
}

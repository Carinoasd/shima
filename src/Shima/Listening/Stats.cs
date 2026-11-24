// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Listening;

public enum StatsPeriod { Week, Month, Year, All }

/// <summary>排行榜的一列：名字（歌名／藝人／專輯）、副名（歌的藝人、專輯的藝人）、聽了多久、幾次。</summary>
public sealed record Ranked(string Name, string Detail, TimeSpan Time, int Plays);

/// <summary>熱力圖的一格：那天、聽了多久、深淺（0 沒聽、1–4）、是否還沒到。</summary>
public readonly record struct HeatCell(DateTime Day, TimeSpan Time, int Level, bool Future);

/// <summary>熱力圖：Cells[週, 列]，列 0 是一週的第一天；不在這一年裡的格子是 null。MonthWeeks 是每個月 1 號所在的欄。</summary>
public sealed record HeatGrid(int Weeks, HeatCell?[,] Cells, IReadOnlyList<(int Month, int Week)> MonthWeeks);

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
    /// 長條圖的每一根：週、月是一天一根；年是一個月一根；全部在兩年內是一個月一根，再長就一年一根。
    /// Start 是那一根的第一天。
    /// </summary>
    public static List<(DateTime Start, TimeSpan Time)> Bars(IReadOnlyList<(DateTime Day, TimeSpan Time)> daily, StatsPeriod period)
    {
        bool byYear = period == StatsPeriod.All && ByYear(daily);
        if (period is StatsPeriod.Week or StatsPeriod.Month) return daily.Select(d => (d.Day, d.Time)).ToList();
        return daily
            .GroupBy(d => byYear ? new DateTime(d.Day.Year, 1, 1) : new DateTime(d.Day.Year, d.Day.Month, 1))
            .Select(g => (g.Key, g.Aggregate(TimeSpan.Zero, (t, d) => t + d.Time)))
            .ToList();
    }

    /// <summary>
    /// 把長條補到整個期間：週補滿七天、月補到月底、年補到十二月（還沒到的是 0）。
    /// 不補的話星期一只有一兩根長條孤零零地站著，每天的位置也會跟著星期幾跑。
    /// </summary>
    public static List<(DateTime Start, TimeSpan Time)> Fill(IReadOnlyList<(DateTime Start, TimeSpan Time)> bars, StatsPeriod period)
    {
        var filled = bars.ToList();
        if (filled.Count == 0 || period == StatsPeriod.All) return filled;
        var first = filled[0].Start;
        int want = period switch { StatsPeriod.Week => 7, StatsPeriod.Month => DateTime.DaysInMonth(first.Year, first.Month), _ => 12 };
        while (filled.Count < want)
        {
            var last = filled[^1].Start;
            filled.Add((period == StatsPeriod.Year ? last.AddMonths(1) : last.AddDays(1), TimeSpan.Zero));
        }
        return filled;
    }

    /// <summary>
    /// 把某一年的每日資料排成「週 × 星期」的格子（像 GitHub 的貢獻圖）：一欄一週、從 firstDay 開始，
    /// 一年從 1 月 1 日排到 12 月 31 日，前後補不到的格子是 null。today 之後的日子標成 Future。
    /// </summary>
    public static HeatGrid Heat(IEnumerable<(DateTime Day, TimeSpan Time)> daily, int year, DayOfWeek firstDay, DateTime today)
    {
        var byDay = new Dictionary<DateTime, TimeSpan>();
        foreach (var (day, time) in daily) if (day.Year == year) byDay[day.Date] = byDay.GetValueOrDefault(day.Date) + time;

        var jan1 = new DateTime(year, 1, 1);
        var dec31 = new DateTime(year, 12, 31);
        var start = jan1.AddDays(-(((int)jan1.DayOfWeek - (int)firstDay + 7) % 7));
        int weeks = (dec31 - start).Days / 7 + 1;

        var cutoffs = HeatCutoffs(byDay.Where(d => d.Key <= today.Date).Select(d => d.Value));
        var cells = new HeatCell?[weeks, 7];
        var months = new List<(int, int)>();
        for (var d = jan1; d <= dec31; d = d.AddDays(1))
        {
            int offset = (d - start).Days, week = offset / 7;
            bool future = d > today.Date;
            var time = future ? TimeSpan.Zero : byDay.GetValueOrDefault(d);
            cells[week, offset % 7] = new HeatCell(d, time, HeatLevel(time, cutoffs), future);
            if (d.Day == 1) months.Add((d.Month, week));
        }
        return new HeatGrid(weeks, cells, months);
    }

    /// <summary>
    /// 深淺的分界：把有聽的日子由短到長排好，取第 1／2／3 個四分位的那一天的時間。
    /// 分界跟著這一年的分布走，不寫死幾分鐘。
    /// </summary>
    public static TimeSpan[] HeatCutoffs(IEnumerable<TimeSpan> times)
    {
        var sorted = times.Where(t => t > TimeSpan.Zero).OrderBy(t => t).ToList();
        if (sorted.Count == 0) return [];
        return [sorted[sorted.Count / 4], sorted[sorted.Count / 2], sorted[sorted.Count * 3 / 4]];
    }

    /// <summary>0 是沒聽；有聽就從 1 起，每達到一個分界加一級，最多 4。</summary>
    public static int HeatLevel(TimeSpan time, IReadOnlyList<TimeSpan> cutoffs)
    {
        if (time <= TimeSpan.Zero) return 0;
        int level = 1;
        foreach (var c in cutoffs) if (time >= c) level++;
        return level;
    }

    /// <summary>「全部」的長條圖要不要一年一根：跨超過 24 個月。</summary>
    public static bool ByYear(IReadOnlyList<(DateTime Day, TimeSpan Time)> daily) =>
        daily.Count > 0 && (daily[^1].Day.Year - daily[0].Day.Year) * 12 + daily[^1].Day.Month - daily[0].Day.Month >= 24;
}

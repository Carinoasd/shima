// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;

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
    public static (DateTime From, DateTime To) Range(StatsPeriod period, DateTime now, DayOfWeek firstDay, DateTime? earliest = null) =>
        Range(period, now, firstDay, 0, earliest);

    /// <summary>
    /// 往前翻 back 段的期間（0 是這一段）。過去的期間是完整的：週七天、整個月、整年；
    /// 只有 back 為 0 的這一段終點是今天（含）。全部沒有「上一段」，一律當作 0。
    /// </summary>
    public static (DateTime From, DateTime To) Range(StatsPeriod period, DateTime now, DayOfWeek firstDay, int back, DateTime? earliest = null)
    {
        back = Math.Max(0, back);
        if (period == StatsPeriod.All || back == 0)
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
        var (from, _) = Range(period, now, firstDay, 0, earliest);
        return period switch
        {
            StatsPeriod.Week => (from.AddDays(-7 * back), from.AddDays(-7 * back + 7)),
            StatsPeriod.Month => (from.AddMonths(-back), from.AddMonths(-back + 1)),
            _ => (from.AddYears(-back), from.AddYears(-back + 1)),
        };
    }

    /// <summary>還能不能再往前翻一段：上一段的最後一天不能比最早的紀錄還早。沒有紀錄或是「全部」就不行。</summary>
    public static bool CanGoBack(StatsPeriod period, DateTime now, DayOfWeek firstDay, DateTime? earliest, int back)
    {
        if (period == StatsPeriod.All || earliest is not { } first) return false;
        try { return Range(period, now, firstDay, Math.Max(0, back) + 1).To > first.Date; }
        catch (ArgumentOutOfRangeException) { return false; }   // 翻到 DateTime 的下限了
    }

    /// <summary>能不能往後翻（回到比較新的一段）：只要不是最新的那一段。</summary>
    public static bool CanGoForward(StatsPeriod period, int back) => period != StatsPeriod.All && back > 0;

    private const int MaxWeekLabel = 24;

    /// <summary>
    /// 目前看的那一段的標題：週「11月2日 – 8日」（跨年、或不是今年的要帶年份）、月「2026年10月」、年「2025」。全部是空字串。
    /// now 是今天，用來判斷這一週是不是今年；不給就只有跨年才帶年份。
    /// 日期太長的語言（西班牙文「2 de noviembre – 8 de noviembre」）改用短日期，標題的位置只有四百像素。
    /// </summary>
    public static string Label(StatsPeriod period, DateTime from, CultureInfo culture, DateTime? now = null)
    {
        var format = culture.DateTimeFormat;
        switch (period)
        {
            case StatsPeriod.Week:
                var last = from.AddDays(6);
                if (from.Year != last.Year || (now is { } today && from.Year != today.Year))
                    return from.ToString(format.ShortDatePattern, culture) + " – " + last.ToString(format.ShortDatePattern, culture);
                var text = from.ToString(format.MonthDayPattern, culture) + " – " + last.ToString(format.MonthDayPattern, culture);
                return text.Length <= MaxWeekLabel ? text
                    : IdleLine.Day(from, culture) + " – " + IdleLine.Day(last, culture);   // 今年的週：退回短格式也不帶年份
            case StatsPeriod.Month: return from.ToString(format.YearMonthPattern, culture);
            case StatsPeriod.Year: return from.Year.ToString(CultureInfo.InvariantCulture);
            default: return "";
        }
    }

    public static StatsSummary Compute(IEnumerable<Listen> listens, StatsPeriod period, DateTime now, DayOfWeek firstDay, int top = 5) =>
        Compute(listens, period, now, 0, firstDay, top);

    /// <summary>往前翻 back 段的統計（back 放在 firstDay 前面，免得跟舊版的 top 參數混淆）。連續天數是到今天為止的數字，跟過去的期間無關，翻到過去時是 0。</summary>
    public static StatsSummary Compute(IEnumerable<Listen> listens, StatsPeriod period, DateTime now, int back, DayOfWeek firstDay, int top = 5)
    {
        var all = listens.Where(Plausible).ToList();
        DateTime? earliest = all.Count > 0 ? all.Min(l => l.Start.DateTime) : null;
        var (from, to) = Range(period, now, firstDay, back, earliest);
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
            back > 0 && period != StatsPeriod.All ? 0 : Streak(all, now));
    }

    /// <summary>時間壞掉的紀錄（例如年份 0001）不拿來統計：往前翻到那裡日期運算會超出範圍。</summary>
    public static bool Plausible(Listen listen) => listen.Start.Year >= 2000;

    public static TimeSpan Sum(IEnumerable<Listen> listens) => TimeSpan.FromSeconds(listens.Sum(l => Seconds(l)));

    /// <summary>
    /// 一筆聽了幾秒。紀錄檔是文字檔，壞掉或手改過的數字（1e300）放進 TimeSpan 會丟 OverflowException，整個統計頁就打不開；
    /// 一筆最多算 MaxListened 秒（約 115 天）：再大的一定是壞的，又遠比真的最長的直播長。
    /// </summary>
    private const double MaxListened = 10_000_000;

    private static double Seconds(Listen listen) => double.IsFinite(listen.Listened) ? Math.Clamp(listen.Listened, 0, MaxListened) : 0;

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
        foreach (var l in listens) hours[l.Start.DateTime.Hour] += TimeSpan.FromSeconds(Seconds(l));
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

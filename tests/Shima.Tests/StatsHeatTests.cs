// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class StatsHeatTests
{
    private static List<(DateTime, TimeSpan)> Year(int year, Func<DateTime, int> minutes, DateTime? until = null)
    {
        var list = new List<(DateTime, TimeSpan)>();
        for (var d = new DateTime(year, 1, 1); d <= (until ?? new DateTime(year, 12, 31)); d = d.AddDays(1)) list.Add((d, TimeSpan.FromMinutes(minutes(d))));
        return list;
    }

    private static HeatGrid Grid(int year, Func<DateTime, int> minutes, DayOfWeek first = DayOfWeek.Monday, DateTime? today = null) =>
        Stats.Heat(Year(year, minutes, today), year, first, today ?? new DateTime(year, 12, 31));

    private static int CountDays(HeatGrid g) { int n = 0; foreach (var c in g.Cells) if (c != null) n++; return n; }

    [Fact]
    public void Jan_first_is_placed_by_the_first_day_of_week()
    {
        // 2026-01-01 是星期四
        var monday = Grid(2026, _ => 1, DayOfWeek.Monday);
        Assert.Null(monday.Cells[0, 0]);
        Assert.Null(monday.Cells[0, 2]);
        Assert.Equal(new DateTime(2026, 1, 1), monday.Cells[0, 3]!.Value.Day);
        Assert.Equal(53, monday.Weeks);

        var sunday = Grid(2026, _ => 1, DayOfWeek.Sunday);
        Assert.Equal(new DateTime(2026, 1, 1), sunday.Cells[0, 4]!.Value.Day);

        var thursday = Grid(2026, _ => 1, DayOfWeek.Thursday);
        Assert.Equal(new DateTime(2026, 1, 1), thursday.Cells[0, 0]!.Value.Day);
    }

    [Fact]
    public void Every_day_of_the_year_appears_exactly_once_in_week_order()
    {
        foreach (var (year, days) in new[] { (2026, 365), (2024, 366), (2000, 366), (2100, 365) })
            foreach (var first in new[] { DayOfWeek.Monday, DayOfWeek.Sunday, DayOfWeek.Saturday })
            {
                var g = Grid(year, _ => 1, first);
                Assert.Equal(days, CountDays(g));
                DateTime? previous = null;
                for (int w = 0; w < g.Weeks; w++)
                    for (int r = 0; r < 7; r++)
                    {
                        if (g.Cells[w, r] is not { } c) continue;
                        Assert.Equal((r + (int)first) % 7, (int)c.Day.DayOfWeek);
                        if (previous != null) Assert.Equal(previous.Value.AddDays(1), c.Day);
                        previous = c.Day;
                    }
            }
    }

    [Fact]
    public void Leap_year_can_need_54_weeks()
    {
        // 2028 年 1 月 1 日是星期六；一週從星期日開始就會讓它落在第一欄的最後一列，366 天排成 54 欄
        var g = Grid(2028, _ => 1, DayOfWeek.Sunday);
        Assert.Equal(54, g.Weeks);
        Assert.Equal(366, CountDays(g));
        Assert.Equal(new DateTime(2028, 12, 31), g.Cells[53, 0]!.Value.Day);
        Assert.Equal(new DateTime(2028, 2, 29), Enumerable.Range(0, g.Weeks).SelectMany(w => Enumerable.Range(0, 7).Select(r => g.Cells[w, r])).First(c => c?.Day.Month == 2 && c.Value.Day.Day == 29)!.Value.Day);
    }

    [Fact]
    public void All_zero_gives_level_zero_everywhere()
    {
        var g = Grid(2026, _ => 0);
        foreach (var c in g.Cells) if (c != null) Assert.Equal(0, c.Value.Level);
        Assert.Empty(Stats.HeatCutoffs([TimeSpan.Zero, TimeSpan.Zero]));
        Assert.Equal(0, Stats.HeatLevel(TimeSpan.Zero, []));
    }

    [Fact]
    public void A_single_day_with_data_gets_the_top_level()
    {
        var g = Grid(2026, d => d == new DateTime(2026, 5, 5) ? 30 : 0);
        var cells = g.Cells.Cast<HeatCell?>().Where(c => c != null).Select(c => c!.Value).ToList();
        Assert.Equal(4, cells.Single(c => c.Level > 0).Level);
        Assert.Equal(new DateTime(2026, 5, 5), cells.Single(c => c.Level > 0).Day);
    }

    [Fact]
    public void Cutoffs_follow_the_distribution_and_boundaries_step_up_at_the_cutoff()
    {
        var times = new[] { 10, 20, 30, 40 }.Select(m => TimeSpan.FromMinutes(m)).ToList();
        var cut = Stats.HeatCutoffs(times);
        Assert.Equal([TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40)], cut);
        Assert.Equal(1, Stats.HeatLevel(TimeSpan.FromMinutes(10), cut));
        Assert.Equal(1, Stats.HeatLevel(TimeSpan.FromMinutes(19.9), cut));
        Assert.Equal(2, Stats.HeatLevel(TimeSpan.FromMinutes(20), cut));
        Assert.Equal(3, Stats.HeatLevel(TimeSpan.FromMinutes(30), cut));
        Assert.Equal(4, Stats.HeatLevel(TimeSpan.FromMinutes(40), cut));
        Assert.Equal(4, Stats.HeatLevel(TimeSpan.FromHours(9), cut));

        // 同樣的形狀放大十倍，各級的分布不變：級距不是寫死的分鐘數
        var scaled = Stats.HeatCutoffs(times.Select(t => t * 10));
        Assert.Equal(cut.Select(c => c * 10), scaled);
    }

    [Fact]
    public void Future_days_and_today_are_marked()
    {
        var today = new DateTime(2026, 11, 23, 20, 0, 0);
        var g = Stats.Heat(Year(2026, _ => 10, today.Date), 2026, DayOfWeek.Monday, today);
        var cells = g.Cells.Cast<HeatCell?>().Where(c => c != null).Select(c => c!.Value).ToList();
        Assert.All(cells.Where(c => c.Day <= today.Date), c => Assert.False(c.Future));
        Assert.All(cells.Where(c => c.Day > today.Date), c => { Assert.True(c.Future); Assert.Equal(0, c.Level); });
        Assert.Equal(38, cells.Count(c => c.Future));
    }

    [Fact]
    public void Month_labels_point_at_the_week_holding_the_first()
    {
        var g = Grid(2026, _ => 1);
        Assert.Equal(12, g.MonthWeeks.Count);
        Assert.Equal((1, 0), g.MonthWeeks[0]);
        // 2026-02-01 是星期日：週一起算在第 4 欄（第 0 欄是 12/29–1/4）
        Assert.Equal((2, 4), g.MonthWeeks[1]);
        Assert.Equal(g.MonthWeeks.OrderBy(m => m.Week), g.MonthWeeks);
    }

    [Fact]
    public void Missing_days_count_as_zero()
    {
        var g = Stats.Heat([(new DateTime(2026, 3, 3), TimeSpan.FromMinutes(5))], 2026, DayOfWeek.Monday, new DateTime(2026, 12, 31));
        Assert.Equal(364, g.Cells.Cast<HeatCell?>().Count(c => c is { Level: 0 }));
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class StatsBrowseTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 22, 0, 0);   // 星期三

    private static Listen L(DateTime start, string title = "a", double listened = 60) =>
        new(new DateTimeOffset(start, TimeSpan.FromHours(8)), title, "x", "Album", "Roon", 240, listened);

    [Fact]
    public void Back_zero_is_the_same_as_the_old_range()
    {
        foreach (var p in new[] { StatsPeriod.Week, StatsPeriod.Month, StatsPeriod.Year, StatsPeriod.All })
            Assert.Equal(Stats.Range(p, Now, DayOfWeek.Monday, new DateTime(2025, 1, 5)), Stats.Range(p, Now, DayOfWeek.Monday, 0, new DateTime(2025, 1, 5)));
    }

    [Fact]
    public void Past_weeks_are_seven_full_days_for_either_first_day()
    {
        Assert.Equal((new DateTime(2026, 11, 16), new DateTime(2026, 11, 23)), Stats.Range(StatsPeriod.Week, Now, DayOfWeek.Monday, 1));
        Assert.Equal((new DateTime(2026, 11, 8), new DateTime(2026, 11, 15)), Stats.Range(StatsPeriod.Week, Now, DayOfWeek.Sunday, 2));
    }

    [Fact]
    public void A_week_can_span_new_year()
    {
        var now = new DateTime(2026, 1, 7);   // 星期三
        Assert.Equal((new DateTime(2025, 12, 29), new DateTime(2026, 1, 5)), Stats.Range(StatsPeriod.Week, now, DayOfWeek.Monday, 1));
        Assert.Equal("12/29/2025 – 1/4/2026", Stats.Label(StatsPeriod.Week, new DateTime(2025, 12, 29), CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Months_cover_month_ends_and_leap_february()
    {
        Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)), Stats.Range(StatsPeriod.Month, Now, DayOfWeek.Monday, 1));
        var march = new DateTime(2024, 3, 31);
        var feb = Stats.Range(StatsPeriod.Month, march, DayOfWeek.Monday, 1);
        Assert.Equal((new DateTime(2024, 2, 1), new DateTime(2024, 3, 1)), feb);
        Assert.Equal(29, (feb.To - feb.From).Days);
        var plain = Stats.Range(StatsPeriod.Month, new DateTime(2025, 3, 31), DayOfWeek.Monday, 1);
        Assert.Equal(28, (plain.To - plain.From).Days);
        Assert.Equal((new DateTime(2025, 12, 1), new DateTime(2026, 1, 1)), Stats.Range(StatsPeriod.Month, new DateTime(2026, 1, 31), DayOfWeek.Monday, 1));
    }

    [Fact]
    public void Years_go_back_whole()
    {
        Assert.Equal((new DateTime(2025, 1, 1), new DateTime(2026, 1, 1)), Stats.Range(StatsPeriod.Year, Now, DayOfWeek.Monday, 1));
        Assert.Equal((new DateTime(2024, 1, 1), new DateTime(2025, 1, 1)), Stats.Range(StatsPeriod.Year, Now, DayOfWeek.Monday, 2));
    }

    [Fact]
    public void All_ignores_back()
    {
        Assert.Equal(Stats.Range(StatsPeriod.All, Now, DayOfWeek.Monday, 0, new DateTime(2025, 3, 2)), Stats.Range(StatsPeriod.All, Now, DayOfWeek.Monday, 3, new DateTime(2025, 3, 2)));
        Assert.False(Stats.CanGoBack(StatsPeriod.All, Now, DayOfWeek.Monday, new DateTime(2020, 1, 1), 0));
        Assert.False(Stats.CanGoForward(StatsPeriod.All, 2));
    }

    [Fact]
    public void Earliest_record_on_the_boundary()
    {
        // 最早一筆剛好是上個月最後一天：能翻到上個月，不能再往前
        var last = new DateTime(2026, 10, 31, 23, 59, 0);
        Assert.True(Stats.CanGoBack(StatsPeriod.Month, Now, DayOfWeek.Monday, last, 0));
        Assert.False(Stats.CanGoBack(StatsPeriod.Month, Now, DayOfWeek.Monday, last, 1));
        // 最早一筆剛好是這個月 1 號：沒有更早的
        Assert.False(Stats.CanGoBack(StatsPeriod.Month, Now, DayOfWeek.Monday, new DateTime(2026, 11, 1), 0));
        // 最早一筆剛好是上個月 1 號：能翻一段，不能翻兩段
        var first = new DateTime(2026, 10, 1);
        Assert.True(Stats.CanGoBack(StatsPeriod.Month, Now, DayOfWeek.Monday, first, 0));
        Assert.False(Stats.CanGoBack(StatsPeriod.Month, Now, DayOfWeek.Monday, first, 1));
        // 週：最早一筆是上週日
        Assert.True(Stats.CanGoBack(StatsPeriod.Week, Now, DayOfWeek.Monday, new DateTime(2026, 11, 22), 0));
        Assert.False(Stats.CanGoBack(StatsPeriod.Week, Now, DayOfWeek.Monday, new DateTime(2026, 11, 23), 0));
    }

    [Fact]
    public void No_records_means_nowhere_to_go()
    {
        foreach (var p in new[] { StatsPeriod.Week, StatsPeriod.Month, StatsPeriod.Year })
            Assert.False(Stats.CanGoBack(p, Now, DayOfWeek.Monday, null, 0));
        Assert.False(Stats.CanGoForward(StatsPeriod.Week, 0));
        Assert.True(Stats.CanGoForward(StatsPeriod.Week, 1));
        var s = Stats.Compute([], StatsPeriod.Month, Now, 1, DayOfWeek.Monday);
        Assert.Equal(0, s.Plays);
        Assert.Equal(31, s.Daily.Count);   // 沒有紀錄也是完整的上個月
    }

    [Fact]
    public void Compute_back_counts_only_that_period_and_hides_streak()
    {
        var listens = new[]
        {
            L(new(2026, 10, 1, 9, 0, 0)), L(new(2026, 10, 31, 23, 0, 0)), L(new(2026, 11, 1, 0, 0, 0)), L(new(2026, 11, 25, 8, 0, 0)), L(new(2026, 11, 24, 8, 0, 0)),
        };
        var past = Stats.Compute(listens, StatsPeriod.Month, Now, 1, DayOfWeek.Monday);
        Assert.Equal(2, past.Plays);
        Assert.Equal(31, past.Daily.Count);
        Assert.Equal(0, past.Streak);
        Assert.Equal(2, past.Hours.Count(h => h > TimeSpan.Zero));
        var now = Stats.Compute(listens, StatsPeriod.Month, Now, 0, DayOfWeek.Monday);
        Assert.Equal(3, now.Plays);
        Assert.Equal(2, now.Streak);
        Assert.Equal(now.Plays, Stats.Compute(listens, StatsPeriod.Month, Now, DayOfWeek.Monday).Plays);
    }

    [Fact]
    public void Labels_follow_the_culture()
    {
        var tw = CultureInfo.GetCultureInfo("zh-TW");
        var en = CultureInfo.GetCultureInfo("en-US");
        Assert.Equal("2026年10月", Stats.Label(StatsPeriod.Month, new DateTime(2026, 10, 1), tw));
        Assert.Equal("October 2026", Stats.Label(StatsPeriod.Month, new DateTime(2026, 10, 1), en));
        Assert.Equal("2025", Stats.Label(StatsPeriod.Year, new DateTime(2025, 1, 1), en));
        Assert.Equal("11月2日 – 11月8日", Stats.Label(StatsPeriod.Week, new DateTime(2026, 11, 2), tw));
        Assert.Equal("", Stats.Label(StatsPeriod.All, Now, en));
    }
}

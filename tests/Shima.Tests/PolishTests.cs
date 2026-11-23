// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class PageTitleTests
{
    [Theory]
    [InlineData("手把開箱_哔哩哔哩_bilibili", "手把開箱")]
    [InlineData("手把開箱_哔哩哔哩", "手把開箱")]
    [InlineData("Some Song (Official Video) - YouTube", "Some Song (Official Video)")]
    [InlineData("Some Song - YouTube Music", "Some Song")]
    [InlineData("  歌名  ", "歌名")]
    [InlineData("歌名", "歌名")]
    [InlineData("A - YouTube - B", "A - YouTube - B")]   // 只看尾巴
    [InlineData(" - YouTube", "- YouTube")]              // 整個標題就是網站名稱：不要變成空的
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 拿掉尾巴的網站名稱(string? title, string expected) => Assert.Equal(expected, PageTitle.Clean(title));
}

public class StatsFillTests
{
    private static List<(DateTime, TimeSpan)> Days(DateTime from, int count) =>
        Enumerable.Range(0, count).Select(i => (from.AddDays(i), TimeSpan.FromMinutes(i + 1))).ToList();

    [Fact]
    public void 週補滿七天_月補到月底_年補到十二月()
    {
        var week = Stats.Fill(Days(new DateTime(2026, 11, 22), 2), StatsPeriod.Week);
        Assert.Equal(7, week.Count);
        Assert.Equal(new DateTime(2026, 11, 28), week[^1].Start);
        Assert.Equal(TimeSpan.FromMinutes(2), week[1].Time);   // 原本的不動
        Assert.Equal(TimeSpan.Zero, week[2].Time);

        var month = Stats.Fill(Days(new DateTime(2026, 2, 1), 10), StatsPeriod.Month);
        Assert.Equal(28, month.Count);
        Assert.Equal(new DateTime(2026, 2, 28), month[^1].Start);

        var year = Stats.Fill([(new DateTime(2026, 1, 1), TimeSpan.FromHours(3)), (new DateTime(2026, 2, 1), TimeSpan.FromHours(1))], StatsPeriod.Year);
        Assert.Equal(12, year.Count);
        Assert.Equal(new DateTime(2026, 12, 1), year[^1].Start);
    }

    [Fact]
    public void 全部的期間與空的清單不補()
    {
        Assert.Empty(Stats.Fill([], StatsPeriod.Week));
        Assert.Equal(3, Stats.Fill(Days(new DateTime(2026, 1, 1), 3), StatsPeriod.All).Count);
        Assert.Equal(7, Stats.Fill(Days(new DateTime(2026, 11, 22), 7), StatsPeriod.Week).Count);   // 已經滿了
    }
}

public class MonthWeeksTests
{
    [Theory]
    [InlineData(2026, 11, DayOfWeek.Sunday, 5)]   // 1 號是星期日，30 天
    [InlineData(2026, 2, DayOfWeek.Sunday, 4)]    // 1 號是星期日，28 天：剛好四列
    [InlineData(2026, 8, DayOfWeek.Sunday, 6)]    // 1 號是星期六，31 天
    [InlineData(2026, 2, DayOfWeek.Monday, 5)]    // 一週從星期一開始：前面空六格
    [InlineData(2024, 2, DayOfWeek.Thursday, 5)]  // 閏年的二月
    public void 這個月佔幾週(int year, int month, DayOfWeek firstDay, int weeks) =>
        Assert.Equal(weeks, MonthView.Weeks(new DateTime(year, month, 15), firstDay));
}

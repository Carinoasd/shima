// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class AlarmFarMonthTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 14, 0, 0);

    [Theory]
    [InlineData(2026, 12)]
    [InlineData(2027, 1)]
    [InlineData(2027, 3)]
    public void 每天重複查未來的月份整個月都有(int year, int month)
    {
        var alarm = new Alarm(1, new DateTime(2026, 11, 25, 8, 0, 0), "吃藥", "daily");
        var days = Alarms.InMonth([alarm], new DateTime(year, month, 1), Now);
        Assert.Equal(DateTime.DaysInMonth(year, month), days.Count);
        Assert.Equal(DateTime.DaysInMonth(year, month), days.Keys.Max());
    }

    [Fact]
    public void 每週重複查未來的月份()
    {
        var alarm = new Alarm(1, new DateTime(2026, 11, 27, 18, 0, 0), "倒垃圾", "fri");
        var days = Alarms.InMonth([alarm], new DateTime(2027, 2, 1), Now);
        Assert.Equal([5, 12, 19, 26], days.Keys.ToArray());
    }

    [Fact]
    public void 過去的月份是空的()
    {
        var alarm = new Alarm(1, new DateTime(2026, 11, 25, 8, 0, 0), "吃藥", "daily");
        Assert.Empty(Alarms.InMonth([alarm], new DateTime(2026, 10, 1), Now));
    }
}

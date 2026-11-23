// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class LocalizedNumbersTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 20, 0, 0);

    private static StatsSummary Summary()
    {
        var (from, to) = Stats.Range(StatsPeriod.Month, Now, DayOfWeek.Monday, new DateTime(2025, 3, 2));
        return new StatsSummary(from, to, TimeSpan.FromMinutes(750), 40,
            [new Ranked("歌", "藝人", TimeSpan.FromMinutes(200), 3)], [new Ranked("藝人", "", TimeSpan.FromMinutes(200), 3)],
            [], [], new TimeSpan[24], 0);
    }

    [Fact]
    public void Hours_on_the_recap_use_the_languages_decimal_separator()
    {
        string Time(string culture) => RecapCard.Content(Summary(), StatsPeriod.Month, Now, CultureInfo.GetCultureInfo(culture), x => x)!.Tracks[0].Time;
        Assert.Equal("3.3 小時", Time("en-US"));
        Assert.Equal("3,3 小時", Time("de-DE"));
        Assert.Equal("3,3 小時", Time("ru-RU"));
    }
}

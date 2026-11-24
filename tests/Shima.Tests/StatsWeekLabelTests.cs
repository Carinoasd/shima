// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class StatsWeekLabelTests
{
    private static readonly DateTime Now = new(2026, 11, 25);

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-TW")]
    [InlineData("ja-JP")]
    [InlineData("es-ES")]
    public void 去年的週標題有年份(string name)
    {
        var label = Stats.Label(StatsPeriod.Week, new DateTime(2025, 12, 22), CultureInfo.GetCultureInfo(name), Now);
        Assert.Contains("2025", label);
    }

    [Fact]
    public void 今年的週不加年份()
    {
        Assert.Equal("11月2日 – 11月8日", Stats.Label(StatsPeriod.Week, new DateTime(2026, 11, 2), CultureInfo.GetCultureInfo("zh-TW"), Now));
        Assert.DoesNotContain("2026", Stats.Label(StatsPeriod.Week, new DateTime(2026, 11, 2), CultureInfo.GetCultureInfo("en-US"), Now));
    }

    [Theory]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("pt-BR")]
    [InlineData("it-IT")]
    [InlineData("ru-RU")]
    public void 長日期的語言改用短日期(string name)
    {
        var label = Stats.Label(StatsPeriod.Week, new DateTime(2026, 11, 2), CultureInfo.GetCultureInfo(name), Now);
        Assert.True(label.Length <= 24, label);
    }

    [Fact]
    public void 不給今天時只有跨年才帶年份()
    {
        Assert.Equal("11月2日 – 11月8日", Stats.Label(StatsPeriod.Week, new DateTime(2025, 11, 2), CultureInfo.GetCultureInfo("zh-TW")));
    }

    [Theory]
    [InlineData("en-US", "9/21 – 9/27")]
    [InlineData("es-ES", "21/9 – 27/9")]
    [InlineData("de-DE", "21.09 – 27.09")]
    [InlineData("zh-TW", "9月21日 – 9月27日")]
    [InlineData("ja-JP", "9月21日 – 9月27日")]
    public void 九月的週今年不帶年份(string name, string expected)
    {
        var label = Stats.Label(StatsPeriod.Week, new DateTime(2026, 9, 21), CultureInfo.GetCultureInfo(name), Now);
        Assert.DoesNotContain("2026", label);
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    [InlineData("zh-TW")]
    [InlineData("ja-JP")]
    public void 九月的週去年帶年份(string name)
    {
        var label = Stats.Label(StatsPeriod.Week, new DateTime(2025, 9, 22), CultureInfo.GetCultureInfo(name), Now);
        Assert.Contains("2025", label);
    }
}

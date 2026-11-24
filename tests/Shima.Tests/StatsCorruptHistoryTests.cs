// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Listening;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：紀錄檔裡聽了「1e300」秒的壞紀錄，統計頁一打開就丟 OverflowException。</summary>
public class StatsCorruptHistoryTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    private static Listen At(double listened) => new(new DateTimeOffset(new DateTime(2026, 11, 24, 10, 0, 0)), "歌", "A", "B", "roon", 200, listened);

    [Fact]
    public void Compute_WithAnAbsurdListenedValue_StillWorks()
    {
        var listens = HistoryFile.Parse("""{"start":"2026-11-24T10:00:00+08:00","title":"x","artist":"a","album":"","source":"s","duration":1,"listened":1e300}""" + "\n" +
                                        """{"start":"2026-11-24T11:00:00+08:00","title":"y","artist":"a","album":"","source":"s","duration":1,"listened":1e400}""");
        Assert.Equal(2, listens.Count);
        foreach (var period in Enum.GetValues<StatsPeriod>())
        {
            var summary = Stats.Compute(listens, period, new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Unspecified), DayOfWeek.Monday);
            Assert.True(summary.Total >= TimeSpan.Zero);
        }
    }

    [Fact]
    public void Sum_And_Hours_IgnoreNonsense_ButKeepRealValues()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), Stats.Sum([At(180)]));
        Assert.Equal(TimeSpan.Zero, Stats.Sum([At(double.PositiveInfinity), At(double.NaN), At(-5)]));
        Assert.Equal(TimeSpan.FromSeconds(10_000_000), Stats.Sum([At(1e300)]));
        Assert.Equal(TimeSpan.FromHours(30), Stats.Sum([At(30 * 3600)]));   // 連續聽三十個小時的直播是真的
        Assert.Equal(TimeSpan.FromSeconds(180), Stats.Hours([At(180), At(double.NaN)])[10]);
    }
}

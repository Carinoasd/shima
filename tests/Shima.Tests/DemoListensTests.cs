// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class DemoListensTests
{
    [Theory]
    [InlineData(2026, 12, 1, 0, 10)]
    [InlineData(2026, 12, 1, 7, 30)]
    [InlineData(2026, 11, 24, 6, 0)]
    [InlineData(2026, 11, 24, 12, 0)]
    [InlineData(2026, 11, 24, 23, 59)]
    [InlineData(2027, 1, 1, 8, 0)]
    public void 任何時間今天都有紀錄且不在現在之後(int y, int mo, int d, int h, int mi)
    {
        var now = new DateTime(y, mo, d, h, mi, 0);
        var listens = Shima.Demo.Listens(now).ToList();
        Assert.Contains(listens, l => l.Start.DateTime.Date == now.Date);
        Assert.All(listens, l => Assert.True(l.Start.DateTime < now));
        // 摘要要有東西，回顧卡片才畫得出來
        var month = Stats.Compute(listens.Where(Stats.Plausible), StatsPeriod.Month, now, DayOfWeek.Monday);
        Assert.True(month.Plays > 0);
    }

    [Fact]
    public void 每次產生的都一樣()
    {
        var now = new DateTime(2026, 12, 1, 7, 30, 0);
        Assert.Equal(Shima.Demo.Listens(now).ToList(), Shima.Demo.Listens(now).ToList());
    }

    [Fact]
    public void 影片仍是每十筆一筆()
    {
        var listens = Shima.Demo.Listens(new DateTime(2026, 11, 24, 15, 0, 0)).ToList();
        Assert.Contains(listens, l => l.Source == "Chrome");
    }
}

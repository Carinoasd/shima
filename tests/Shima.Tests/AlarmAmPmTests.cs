// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class AlarmAmPmTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 14, 0, 0);

    [Theory]
    [InlineData("2026-11-28 3:30pm 開會", 15, 30)]
    [InlineData("2026-11-28 3:30 pm 開會", 15, 30)]
    [InlineData("2026-11-28 3:30PM 開會", 15, 30)]
    [InlineData("2026-11-28 3:30 am 開會", 3, 30)]
    [InlineData("2026-11-28 12:30am 開會", 0, 30)]
    [InlineData("2026-11-28 12:30pm 開會", 12, 30)]
    [InlineData("2026/11/28 7:05 pm 開會", 19, 5)]
    public void 日期加十二小時制(string line, int h, int m)
    {
        var r = Alarms.ParseLine(line, Now);
        Assert.NotNull(r);
        Assert.Equal(new DateTime(2026, 11, 28, h, m, 0), r!.Value.At);
        Assert.Equal("開會", r.Value.Text);
    }

    [Theory]
    [InlineData("2026-11-28 15:30 開會", 15, 30, "開會")]
    [InlineData("2026-11-28 03:30 開會", 3, 30, "開會")]
    [InlineData("2026-11-28 15:30:00 開會", 15, 30, "開會")]
    [InlineData("2026-11-28 3:30 pmx", 3, 30, "pmx")]
    public void 二十四小時制不受影響(string line, int h, int m, string text)
    {
        var r = Alarms.ParseLine(line, Now);
        Assert.NotNull(r);
        Assert.Equal(new DateTime(2026, 11, 28, h, m, 0), r!.Value.At);
        Assert.Equal(text, r.Value.Text);
    }

    [Theory]
    [InlineData("2026-11-28 25:00 pm 開會")]
    [InlineData("2026-11-28 3:75pm 開會")]
    public void 無效的時間是null(string line) => Assert.Null(Alarms.ParseLine(line, Now));

    [Fact]
    public void 只寫日期仍是九點()
    {
        var r = Alarms.ParseLine("2026-11-28 3 個人開會", Now);
        Assert.Equal(new DateTime(2026, 11, 28, 9, 0, 0), r!.Value.At);
    }
}

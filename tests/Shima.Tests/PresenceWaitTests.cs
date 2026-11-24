// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class PresenceWaitTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void 剩不到一毫秒的等待進位成一毫秒()
    {
        var s = new PresenceSchedule();
        s.SetReady(true);
        s.Set(new JsonObject());
        s.Next(T0);
        s.Set(new JsonObject());
        // 離最小間隔還差 0.4 毫秒：不能變成 WaitAsync(0) 空轉
        var wait = s.Next(T0 + PresenceSchedule.MinInterval - TimeSpan.FromTicks(4000)).Wait;
        Assert.Equal(TimeSpan.FromMilliseconds(1), wait);
    }

    [Fact]
    public void 等待永遠是整毫秒且不比實際短()
    {
        for (int ticks = 1; ticks < 100_000; ticks += 37)
        {
            var real = TimeSpan.FromTicks(ticks);
            var wait = PresenceSchedule.CeilMs(real);
            Assert.True(wait >= real);
            Assert.True(wait - real < TimeSpan.FromMilliseconds(1));
            Assert.Equal(0, wait.Ticks % TimeSpan.TicksPerMillisecond);
        }
    }

    [Fact]
    public void 整毫秒不變()
    {
        Assert.Equal(TimeSpan.FromSeconds(3.5), PresenceSchedule.CeilMs(TimeSpan.FromSeconds(3.5)));
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Motion;
using Xunit;

namespace Shima.Tests;

public class FrameGateTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void 還沒畫過就要畫() => Assert.True(FrameGate.Due(TimeSpan.Zero, Ms(5)));

    [Theory]
    [InlineData(1000, 1016.7, false)]   // 60 Hz 的下一幀：跳過
    [InlineData(1000, 1033.3, true)]    // 再下一幀：畫
    [InlineData(1000, 1031.0, true)]    // 幀稍微早到一點也要畫，不然會在隔兩幀與隔三幀之間跳
    [InlineData(1000, 1006.9, false)]   // 144 Hz
    [InlineData(1000, 1027.8, true)]    // 144 Hz 第四幀
    [InlineData(1000, 900, true)]       // 時鐘倒退
    public void 約每秒三十次(double last, double now, bool due) => Assert.Equal(due, FrameGate.Due(Ms(last), Ms(now)));

    [Fact]
    public void 六十赫茲時剛好隔一幀畫一次()
    {
        // 帶一點抖動的 60 Hz：每兩幀畫一次，不會忽快忽慢
        var last = TimeSpan.Zero;
        var drawn = new List<int>();
        var random = new Random(7);
        for (int frame = 1; frame <= 120; frame++)
        {
            var now = Ms(frame * 1000 / 60.0 + random.NextDouble() * 2 - 1);
            if (FrameGate.Due(last, now)) { drawn.Add(frame); last = now; }
        }
        var gaps = drawn.Zip(drawn.Skip(1), (a, b) => b - a).Distinct().ToList();
        Assert.Equal([2], gaps);
    }

    [Theory]
    [InlineData(double.NaN, 3, true)]
    [InlineData(10, 10.4, false)]
    [InlineData(10, 9.6, false)]
    [InlineData(10, 10.5, true)]
    [InlineData(10, 8, true)]           // 往回（拖了進度）也算
    [InlineData(10, double.NaN, true)]
    public void 光頭動了半個像素才畫(double drawn, double now, bool moved) => Assert.Equal(moved, FrameGate.Moved(drawn, now));
}

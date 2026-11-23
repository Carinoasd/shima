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

public class PulseTests
{
    [Fact]
    public void 計時器只在沒有人跟著幀打拍子時才打()
    {
        var t = TimeSpan.FromSeconds(10);
        Assert.False(FrameGate.Quiet(t, t + TimeSpan.FromMilliseconds(33)));   // 掃光剛打過一拍
        Assert.True(FrameGate.Quiet(t, t + TimeSpan.FromMilliseconds(45)));    // 掃光停了，計時器接手
        Assert.True(FrameGate.Quiet(TimeSpan.FromDays(-1), t));                // 從來沒打過
    }

    [Theory]
    [InlineData(0.6, 0.04, 0.6)]      // 原本的間隔：係數不變
    [InlineData(0.25, 0.033, 0.25)]
    [InlineData(0.6, 0, 0)]           // 沒經過時間就不動
    public void 緩動照實際經過的時間換算(double factor, double seconds, double expected) =>
        Assert.Equal(expected, Pulse.Ease(factor, seconds, seconds == 0 ? 0.04 : seconds), 6);

    [Fact]
    public void 兩小步等於一大步()
    {
        // 間隔變短時每一步走少一點，加起來跟原本一步走的一樣多
        double half = Pulse.Ease(0.6, 0.02, 0.04);
        Assert.Equal(0.6, 1 - (1 - half) * (1 - half), 6);
        Assert.True(Pulse.Ease(0.6, 0.033, 0.04) < 0.6);
    }
}

public class BarsTests
{
    [Fact]
    public void 高度動不到零點三個像素就不重畫()
    {
        double[] drawn = [0.5, 0.5, 0.5, 0.5];
        Assert.False(Bars.Worth(drawn, [0.51, 0.5, 0.49, 0.5], 11.5));   // 0.115 px
        Assert.True(Bars.Worth(drawn, [0.5, 0.5, 0.5, 0.53], 11.5));     // 0.345 px
        Assert.False(Bars.Worth(drawn, [0.9, 0.9, 0.9, 0.9], 0));         // 還沒排版
    }
}

public class CollapsedWindowTests
{
    [Theory]
    [InlineData(1, 92)]       // 上面 8、膠囊 36、下面 48 留給陰影與回彈
    [InlineData(0.9, 83)]
    [InlineData(1.3, 120)]
    public void 收著時視窗只留膠囊與陰影的高度(double scale, double expected) =>
        Assert.Equal(expected, IslandScale.WindowHeight(0, 36, scale));

    [Fact]
    public void 展開時照舊至少312() => Assert.Equal(312, IslandScale.WindowHeight(312, 36, 1));
}

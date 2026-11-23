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
    public void 第一幀就是一拍() => Assert.True(new BeatGate().Next(Ms(5)));

    [Theory]
    [InlineData(60, 2)]
    [InlineData(75, 2)]     // 剛好卡在中間：固定隔 2 幀（37.5 次），不會在 2 與 3 之間跳
    [InlineData(90, 3)]
    [InlineData(100, 3)]
    [InlineData(120, 4)]
    [InlineData(144, 5)]
    [InlineData(165, 0)]    // 5.5 幀：隔 5 或 6 都可以，但一旦定了就不換
    public void 各種更新率都是固定隔幾幀一拍_不會忽快忽慢(double hz, int every)
    {
        // 幀的時間帶一點抖動（±1 毫秒），偶爾掉一幀
        var gate = new BeatGate();
        var random = new Random(7);
        var beats = new List<int>();
        for (int frame = 1; frame <= 600; frame++)
        {
            if (frame % 97 == 0) continue;   // 掉一幀
            var now = Ms(1000 + frame * 1000 / hz + random.NextDouble() * 2 - 1);
            if (gate.Next(now)) beats.Add(frame);
        }
        if (every == 0) Assert.InRange(gate.Every, 5, 6); else Assert.Equal(every, gate.Every);
        every = gate.Every;
        // 前面幾幀還在量更新率，之後拍與拍之間都是 every 幀（掉幀的那一拍會晚一幀）
        var gaps = beats.SkipWhile(f => f < 30).Zip(beats.SkipWhile(f => f < 30).Skip(1), (a, b) => b - a).ToList();
        Assert.True(gaps.Count(g => g == every) >= gaps.Count - 8, $"{hz} Hz: " + string.Join(",", gaps));
        Assert.All(gaps, g => Assert.InRange(g, every, every + 1));
    }

    [Fact]
    public void 停了一陣子再開始_不會把更新率量錯()
    {
        var gate = new BeatGate();
        for (int f = 0; f < 30; f++) gate.Next(Ms(f * 1000 / 60.0));
        gate.Next(Ms(5000));   // 停了好幾秒
        for (int f = 1; f < 30; f++) gate.Next(Ms(5000 + f * 1000 / 60.0));
        Assert.Equal(2, gate.Every);
        Assert.InRange(gate.Interval, 0.016, 0.0175);
    }

    [Theory]
    [InlineData(double.NaN, 3, true)]
    [InlineData(10, 10.4, false)]
    [InlineData(10, 9.6, false)]
    [InlineData(10, 10.5, true)]
    [InlineData(10, 8, true)]           // 往回（拖了進度）也算
    [InlineData(10, double.NaN, true)]
    public void 光頭動了半個像素才畫(double drawn, double now, bool moved) => Assert.Equal(moved, FrameGate.Moved(drawn, now));

    [Theory]
    [InlineData(double.NaN, double.NaN, false)]   // 前奏、間奏、空白的一句：沒有光頭，不用每 0.1 秒補畫
    [InlineData(5, double.NaN, false)]
    [InlineData(double.NaN, 3, true)]             // 還沒畫過這句
    [InlineData(10, 30, true)]                    // 暫停時拖了進度
    [InlineData(10, 10.2, false)]
    public void 沒在動時只有光頭真的變了才補畫(double drawn, double now, bool stale) => Assert.Equal(stale, FrameGate.Stale(drawn, now));
}

public class PulseTests
{
    [Fact]
    public void 計時器只在沒有人跟著幀打拍子時才打()
    {
        var t = TimeSpan.FromSeconds(10);
        Assert.False(FrameGate.Quiet(t, t + TimeSpan.FromMilliseconds(40)));   // 掃光剛打過一拍（165 Hz 隔 6 幀是 36 毫秒）
        Assert.True(FrameGate.Quiet(t, t + TimeSpan.FromMilliseconds(55)));    // 掃光停了，計時器接手
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

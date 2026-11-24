// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>閒置的膠囊每 0.1 秒問一次：沒變就不重算，提醒清單只在需要時才拿。</summary>
public class IdleLineCacheTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 14, 5, 0);
    private static readonly CultureInfo Tw = CultureInfo.GetCultureInfo("zh-TW");
    private static readonly Alarm Dinner = new(1, new DateTime(2026, 11, 24, 18, 0, 0), "晚餐");

    [Theory]
    [InlineData(IdleLine.Clock)]
    [InlineData(IdleLine.Date)]
    [InlineData(IdleLine.None)]
    public void 不是提醒模式_不拿提醒清單(string mode)
    {
        var cache = new IdleLineCache();
        int asked = 0;
        for (int i = 0; i < 50; i++)
            Assert.Equal(IdleLine.Text(mode, Now, null, [], Tw, "明天"), cache.Text(mode, Now.AddMilliseconds(i * 100), null, i, () => { asked++; return [Dinner]; }, Tw, "明天"));
        Assert.Equal(0, asked);
    }

    [Fact]
    public void 同一分鐘_清單沒變_只拿一次()
    {
        var cache = new IdleLineCache();
        int asked = 0;
        IEnumerable<Alarm> List() { asked++; return [Dinner]; }
        for (int i = 0; i < 50; i++) Assert.Equal("18:00 晚餐", cache.Text(IdleLine.Alarm, Now.AddMilliseconds(i * 100), null, 7, List, Tw, "明天"));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void 清單變了_重拿()
    {
        var cache = new IdleLineCache();
        Assert.Equal("18:00 晚餐", cache.Text(IdleLine.Alarm, Now, null, 1, () => [Dinner], Tw, "明天"));
        Assert.Equal("14:05", cache.Text(IdleLine.Alarm, Now, null, 2, () => [], Tw, "明天"));
    }

    [Fact]
    public void 分鐘變了_設定變了_語言變了_都重算()
    {
        var cache = new IdleLineCache();
        Assert.Equal("14:05", cache.Text(IdleLine.Clock, Now.AddSeconds(59), null, 0, () => [], Tw, "明天"));
        Assert.Equal("14:06", cache.Text(IdleLine.Clock, Now.AddSeconds(60), null, 0, () => [], Tw, "明天"));
        Assert.Equal(IdleLine.Text(IdleLine.Date, Now.AddSeconds(60), null, [], Tw, "明天"), cache.Text(IdleLine.Date, Now.AddSeconds(60), null, 0, () => [], Tw, "明天"));
        var de = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal(IdleLine.Text(IdleLine.Date, Now.AddSeconds(60), null, [], de, "morgen"), cache.Text(IdleLine.Date, Now.AddSeconds(60), null, 0, () => [], de, "morgen"));
    }

    [Fact]
    public void 番茄鐘優先_結束後回到原本的()
    {
        var cache = new IdleLineCache();
        Assert.Equal("14:05", cache.Text(IdleLine.Clock, Now, null, 0, () => [], Tw, "明天"));
        Assert.Equal("🍅 24:59", cache.Text(IdleLine.Clock, Now, "🍅 24:59", 0, () => [], Tw, "明天"));
        Assert.Equal("14:05", cache.Text(IdleLine.Clock, Now, null, 0, () => [], Tw, "明天"));
    }
}

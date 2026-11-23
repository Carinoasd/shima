// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class SleepTimerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 23, 0, 0);
    private static readonly TimeSpan Len = TimeSpan.FromMinutes(4);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void 一開始是關的()
    {
        var timer = new SleepTimer();
        Assert.Equal(SleepMode.Off, timer.Mode);
        Assert.Null(timer.Left(T0));
        Assert.False(timer.Tick(T0.AddHours(5), "a\nb", S(1), Len, true));
    }

    [Fact]
    public void 時間到了才暫停_而且只暫停一次()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Left(T0));
        Assert.Equal(TimeSpan.FromMinutes(10), timer.Left(T0.AddMinutes(20)));
        Assert.False(timer.Tick(T0.AddMinutes(29.9), "a\nb", S(5), Len, true));
        Assert.True(timer.Tick(T0.AddMinutes(30), "a\nb", S(5), Len, true));
        Assert.Equal(SleepMode.Off, timer.Mode);
        Assert.False(timer.Tick(T0.AddMinutes(31), "a\nb", S(5), Len, true));
    }

    [Fact]
    public void 暫停著倒數照走_到點時不用再暫停()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(15), T0);
        Assert.False(timer.Tick(T0.AddMinutes(10), "a\nb", S(5), Len, false));
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Left(T0.AddMinutes(10)));
        Assert.False(timer.Tick(T0.AddMinutes(16), "a\nb", S(5), Len, false));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 重設會重新計時()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(15), T0);
        timer.After(TimeSpan.FromMinutes(60), T0.AddMinutes(10));
        Assert.False(timer.Tick(T0.AddMinutes(30), null, default, default, true));
        Assert.Equal(TimeSpan.FromMinutes(60), timer.Total);
    }

    [Fact]
    public void 取消後不再暫停()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(1), T0);
        timer.Cancel();
        Assert.Null(timer.Left(T0));
        Assert.False(timer.Tick(T0.AddMinutes(5), "a\nb", S(5), Len, true));
    }

    [Fact]
    public void 沒有歌在播時不能設定這首播完()
    {
        var timer = new SleepTimer();
        Assert.False(timer.AfterTrack(null, default));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 歌名加藝人當作同一首()
    {
        Assert.Equal(SleepTimer.KeyOf("a", "b"), SleepTimer.KeyOf("a", "b"));
        Assert.NotEqual(SleepTimer.KeyOf("a", "b"), SleepTimer.KeyOf("a", "c"));
        Assert.Null(SleepTimer.KeyOf("", ""));
        Assert.Null(SleepTimer.KeyOf(null, null));
    }

    [Fact]
    public void 這首播完_歌換了就暫停()
    {
        var timer = new SleepTimer();
        Assert.True(timer.AfterTrack("a\nb", S(30)));
        Assert.Null(timer.Left(T0));
        Assert.False(timer.Tick(T0, "a\nb", S(60), Len, true));
        Assert.False(timer.Tick(T0, "a\nb", S(239), Len, true));
        Assert.True(timer.Tick(T0, "c\nd", S(0), Len, true));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 這首播完_沒歌了也算()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.True(timer.Tick(T0, null, default, default, true));
    }

    [Fact]
    public void 這首播完_同一首重播算播完()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.False(timer.Tick(T0, "a\nb", S(238), Len, true));
        Assert.True(timer.Tick(T0, "a\nb", S(1), Len, true));
    }

    [Fact]
    public void 這首播完_拖進度條往回不算()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.False(timer.Tick(T0, "a\nb", S(120), Len, true));
        Assert.False(timer.Tick(T0, "a\nb", S(10), Len, true));
        Assert.Equal(SleepMode.EndOfTrack, timer.Mode);
    }

    [Fact]
    public void 這首播完_不知道長度時只認回到開頭()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.False(timer.Tick(T0, "a\nb", S(20), TimeSpan.Zero, true));
        Assert.False(timer.Tick(T0, "a\nb", S(40), TimeSpan.Zero, true));
        Assert.True(timer.Tick(T0, "a\nb", S(1), TimeSpan.Zero, true));
    }

    [Fact]
    public void 這首播完_暫停著歌換了只取消不暫停()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.False(timer.Tick(T0, "c\nd", S(0), Len, false));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 這首播完_暫停著同一首不動()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.False(timer.Tick(T0.AddHours(3), "a\nb", S(30), Len, false));
        Assert.Equal(SleepMode.EndOfTrack, timer.Mode);
    }
}

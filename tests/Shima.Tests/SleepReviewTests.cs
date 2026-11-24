// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>睡眠定時與「暫停所有播放器」的審查修正。</summary>
public class SleepReviewTests
{
    // ---------------------------------------------------------------- 暫停所有播放器（#9 審查 1）

    private sealed record FakeSession(string AppId, bool Playing, bool Broken = false);

    private static (string, bool) Read(FakeSession s) =>
        s.Broken ? throw new COMException("工作階段已關閉") : (s.AppId, s.Playing);

    [Fact]
    public void 有一個工作階段讀不到狀態_其他正在播的照樣暫停()
    {
        FakeSession[] sessions = [new("Chrome", true), new("Edge", true, Broken: true), new("Spotify.exe", true), new("Firefox", false)];
        Assert.Equal([0, 2], SmtcSource.PausePicks(sessions, Read, null));
    }

    [Fact]
    public void 全部讀不到_沒有要暫停的()
    {
        FakeSession[] sessions = [new("A", true, Broken: true), new("B", true, Broken: true)];
        Assert.Empty(SmtcSource.PausePicks(sessions, Read, null));
    }

    [Fact]
    public void 逐一讀狀態時照樣排除不跟的播放器()
    {
        FakeSession[] sessions = [new("Roon.exe", true), new("Chrome", true, Broken: true), new("Spotify.exe", true)];
        Assert.Equal([2], SmtcSource.PausePicks(sessions, Read, id => id.Contains("Roon")));
    }
    // ---------------------------------------------------------------- 到點時剛好在換歌的空檔（#7 審查 1）

    private static readonly DateTime T0 = new(2026, 1, 1, 23, 0, 0);
    private static readonly TimeSpan Len = TimeSpan.FromMinutes(4);
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void 倒數到點撞上換歌的空檔_下一首開始播就暫停()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.False(timer.Tick(T0.AddMinutes(30).AddSeconds(-1), "a\nb", S(239), Len, true));
        // 到點的這一刻：Roon 正在緩衝下一首
        Assert.False(timer.Tick(T0.AddMinutes(30), "c\nd", S(0), Len, false));
        Assert.NotEqual(SleepMode.Off, timer.Mode);
        Assert.False(timer.Tick(T0.AddMinutes(30).AddSeconds(2), "c\nd", S(0), Len, false));
        Assert.True(timer.Tick(T0.AddMinutes(30).AddSeconds(3), "c\nd", S(0), Len, true));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 倒數到點撞上空檔_寬限期過了都沒播就取消_之後再播不暫停()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.False(timer.Tick(T0.AddMinutes(30).AddSeconds(-0.1), "a\nb", S(239), Len, true));
        Assert.False(timer.Tick(T0.AddMinutes(30), "a\nb", Len, Len, false));
        Assert.False(timer.Tick(T0.AddMinutes(30) + SleepTimer.Grace, "a\nb", Len, Len, false));
        Assert.Equal(SleepMode.Off, timer.Mode);
        Assert.False(timer.Tick(T0.AddMinutes(31), "a\nb", S(1), Len, true));
    }

    [Fact]
    public void 使用者早就自己暫停了_到點後手動恢復播放不會被補暫停()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.False(timer.Tick(T0.AddMinutes(20), "a\nb", S(60), Len, true));
        Assert.False(timer.Tick(T0.AddMinutes(20).AddSeconds(1), "a\nb", S(61), Len, false));   // 按了暫停
        Assert.False(timer.Tick(T0.AddMinutes(30), "a\nb", S(61), Len, false));
        Assert.Equal(SleepMode.Off, timer.Mode);
        Assert.False(timer.Tick(T0.AddMinutes(30).AddSeconds(2), "a\nb", S(61), Len, true));
    }

    [Fact]
    public void 一直沒播過_到點後開始播也不暫停()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(1), T0);
        Assert.False(timer.Tick(T0.AddMinutes(1), "a\nb", S(5), Len, false));
        Assert.False(timer.Tick(T0.AddMinutes(1).AddSeconds(1), "a\nb", S(5), Len, true));
    }

    [Fact]
    public void 這首播完_換歌時下一首還在緩衝_開始播就暫停()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(200));
        Assert.False(timer.Tick(T0, "a\nb", S(239.9), Len, true));
        Assert.False(timer.Tick(T0.AddSeconds(0.1), "c\nd", S(0), Len, false));
        Assert.False(timer.Tick(T0.AddSeconds(4), "c\nd", S(0), Len, false));
        Assert.True(timer.Tick(T0.AddSeconds(5), "c\nd", S(0.5), Len, true));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 這首播完_換歌時先短暫停在結尾_下一首開始播就暫停()
    {
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(200));
        Assert.False(timer.Tick(T0, "a\nb", S(239.9), Len, true));
        Assert.False(timer.Tick(T0.AddSeconds(0.1), "a\nb", Len, Len, false));
        Assert.True(timer.Tick(T0.AddSeconds(0.5), "c\nd", S(0), Len, true));
    }

    [Fact]
    public void 寬限期中重新設定定時_重新來過()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(1), T0);
        Assert.False(timer.Tick(T0.AddSeconds(59.9), "a\nb", S(239), Len, true));
        Assert.False(timer.Tick(T0.AddMinutes(1), "c\nd", S(0), Len, false));
        timer.After(TimeSpan.FromMinutes(30), T0.AddMinutes(1));
        Assert.False(timer.Tick(T0.AddMinutes(1).AddSeconds(1), "c\nd", S(0), Len, true));
        Assert.Equal(SleepMode.After, timer.Mode);
    }
    // ---------------------------------------------------------------- 倒數到點問所有播放器（#9 審查 2）

    [Fact]
    public void 倒數到點_島上這首沒在播但別的播放器在播_要暫停()
    {
        // 沒有標題的串流（島上沒有曲目），或 Roon 暫停著、旁邊的瀏覽器在播
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.True(timer.Tick(T0.AddMinutes(30), null, default, default, false, () => true));
        Assert.Equal(SleepMode.Off, timer.Mode);

        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.True(timer.Tick(T0.AddMinutes(30), "a\nb", S(61), Len, false, () => true));
    }

    [Fact]
    public void 倒數到點_什麼都沒在播就只取消()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        Assert.False(timer.Tick(T0.AddMinutes(30), "a\nb", S(61), Len, false, () => false));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 倒數到點撞上空檔_寬限期內別的播放器開始播也算()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        bool any = true;
        Assert.False(timer.Tick(T0.AddMinutes(30).AddSeconds(-0.1), "a\nb", S(239), Len, true, () => any));
        any = false;
        Assert.False(timer.Tick(T0.AddMinutes(30), "a\nb", Len, Len, false, () => any));
        any = true;
        Assert.True(timer.Tick(T0.AddMinutes(30).AddSeconds(2), null, default, default, false, () => any));
    }

    [Fact]
    public void 所有播放器的狀態只在到點之後才問()
    {
        var timer = new SleepTimer();
        timer.After(TimeSpan.FromMinutes(30), T0);
        int asked = 0;
        for (int i = 0; i < 100; i++)
            Assert.False(timer.Tick(T0.AddSeconds(i), "a\nb", S(i), Len, true, () => { asked++; return true; }));
        Assert.Equal(0, asked);
        Assert.True(timer.Tick(T0.AddMinutes(30), "a\nb", S(5), Len, true, () => { asked++; return true; }));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void 這首播完只看島上這一首_不管別的播放器()
    {
        // Roon 暫停著時換了歌，旁邊的瀏覽器在播：「這首」沒有播下去，不該去暫停瀏覽器
        var timer = new SleepTimer();
        timer.AfterTrack("a\nb", S(30));
        Assert.False(timer.Tick(T0, "c\nd", S(0), Len, false, () => true));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }
    // ---------------------------------------------------------------- 停在結尾時按「這首播完」（#7 審查 7）

    [Fact]
    public void 這首播完_已經停在結尾就不能設定()
    {
        var timer = new SleepTimer();
        Assert.False(timer.AfterTrack("a\nb", Len - S(1), Len, playing: false));
        Assert.Equal(SleepMode.Off, timer.Mode);
    }

    [Fact]
    public void 這首播完_在最後幾秒但還在播_照樣設定並在換歌時暫停()
    {
        var timer = new SleepTimer();
        Assert.True(timer.AfterTrack("a\nb", Len - S(1), Len, playing: true));
        Assert.False(timer.Tick(T0, "a\nb", Len - S(0.9), Len, true));
        Assert.True(timer.Tick(T0.AddSeconds(1), "c\nd", S(0), Len, true));
    }

    [Fact]
    public void 這首播完_暫停在中間可以設定_設定後不會馬上取消()
    {
        var timer = new SleepTimer();
        Assert.True(timer.AfterTrack("a\nb", S(60), Len, playing: false));
        Assert.False(timer.Tick(T0, "a\nb", S(60), Len, false));
        Assert.Equal(SleepMode.EndOfTrack, timer.Mode);
    }
}

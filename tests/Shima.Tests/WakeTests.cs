// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class HomeCacheTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (AgentHooks.HomeCache Cache, Func<AgentHooks.Home> Find, Func<int> Calls) Make(string? wsl)
    {
        int calls = 0;
        return (new(), () => { calls++; return new AgentHooks.Home(@"C:\u", wsl); }, () => calls);
    }

    [Fact]
    public void 找到之後五分鐘內不再啟動()
    {
        var (cache, find, calls) = Make(@"\\wsl.localhost\U\home\x");
        cache.Get(T0, find);
        cache.Get(T0.AddMinutes(4), find);
        Assert.Equal(1, calls());
        cache.Get(T0.AddMinutes(5), find);
        Assert.Equal(2, calls());
    }

    [Fact]
    public void 沒找到只記三十秒_之後才裝WSL很快看得到()
    {
        var (cache, find, calls) = Make(null);
        cache.Get(T0, find);
        cache.Get(T0.AddSeconds(29), find);
        Assert.Equal(1, calls());
        cache.Get(T0.AddSeconds(30), find);
        Assert.Equal(2, calls());
    }

    [Fact]
    public void 時鐘往回調就重找()
    {
        var (cache, find, calls) = Make(@"\\wsl.localhost\U\home\x");
        cache.Get(T0, find);
        cache.Get(T0.AddHours(-1), find);
        Assert.Equal(2, calls());
    }
}

public class PresenceScheduleTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private static readonly System.Text.Json.Nodes.JsonObject A = new() { ["n"] = 1 };
    private static readonly System.Text.Json.Nodes.JsonObject B = new() { ["n"] = 2 };

    [Fact]
    public void 沒東西或還沒就緒就不送也沒有時間可等()
    {
        var s = new Shima.Playback.PresenceSchedule();
        Assert.Null(s.Next(T0).Wait);
        s.SetReady(true);
        Assert.Null(s.Next(T0).Activity);
        Assert.Null(s.Next(T0).Wait);
        var t = new Shima.Playback.PresenceSchedule();
        t.Set(A);
        Assert.Null(t.Next(T0).Activity);
        Assert.Null(t.Next(T0).Wait);
    }

    [Fact]
    public void 就緒且有東西就送_送完不重複()
    {
        var s = new Shima.Playback.PresenceSchedule();
        s.SetReady(true);
        s.Set(A);
        Assert.Same(A, s.Next(T0).Activity!.Value.Value);
        Assert.Null(s.Next(T0.AddSeconds(10)).Activity);
    }

    [Fact]
    public void 兩次之間至少隔最小間隔_只送最新的一筆()
    {
        var s = new Shima.Playback.PresenceSchedule();
        s.SetReady(true);
        s.Set(A);
        s.Next(T0);
        s.Set(null);
        s.Set(B);
        var wait = s.Next(T0.AddSeconds(1));
        Assert.Null(wait.Activity);
        Assert.Equal(TimeSpan.FromSeconds(3.5), wait.Wait);
        Assert.Same(B, s.Next(T0.AddSeconds(4.5)).Activity!.Value.Value);
    }

    [Fact]
    public void 清除也是一筆要送的()
    {
        var s = new Shima.Playback.PresenceSchedule();
        s.SetReady(true);
        s.Set(null);
        var step = s.Next(T0);
        Assert.True(step.Activity.HasValue);
        Assert.Null(step.Activity!.Value.Value);
    }

    [Fact]
    public void 斷線重連後把目前的狀態再送一次()
    {
        var s = new Shima.Playback.PresenceSchedule();
        s.SetReady(true);
        s.Set(A);
        s.Next(T0);
        s.Reconnect();
        Assert.Null(s.Next(T0.AddMinutes(1)).Activity);
        s.SetReady(true);
        Assert.Same(A, s.Next(T0.AddMinutes(1)).Activity!.Value.Value);
    }

    [Fact]
    public void 重連時原本就是清除的不用再送()
    {
        var s = new Shima.Playback.PresenceSchedule();
        s.SetReady(true);
        s.Set(null);
        s.Next(T0);
        s.Reconnect();
        s.SetReady(true);
        Assert.Null(s.Next(T0.AddMinutes(1)).Activity);
    }

    [Fact]
    public void 時鐘往回調不會卡住()
    {
        var s = new Shima.Playback.PresenceSchedule();
        s.SetReady(true);
        s.Set(A);
        s.Next(T0);
        s.Set(B);
        Assert.Same(B, s.Next(T0.AddHours(-1)).Activity!.Value.Value);
    }
}

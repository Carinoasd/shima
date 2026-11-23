// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
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

public class AlarmWakeTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0);

    [Fact]
    public void 沒有提醒就只留保險間隔()
    {
        Assert.Equal(Alarms.Safety, Alarms.NextWake([], Now));
    }

    [Fact]
    public void 很近的提醒就睡到那一刻()
    {
        var list = new[] { new Alarm(1, Now.AddSeconds(12.5), "a"), new Alarm(2, Now.AddSeconds(5), "b") };
        Assert.Equal(TimeSpan.FromSeconds(5), Alarms.NextWake(list, Now));
    }

    [Fact]
    public void 很遠的提醒不會讓計時器睡過保險間隔()
    {
        Assert.Equal(Alarms.Safety, Alarms.NextWake([new Alarm(1, Now.AddHours(3), "a")], Now));
    }

    [Fact]
    public void 已經到點的馬上醒()
    {
        Assert.Equal(TimeSpan.Zero, Alarms.NextWake([new Alarm(1, Now.AddHours(1), "a"), new Alarm(2, Now.AddMinutes(-1), "b")], Now));
        Assert.Equal(TimeSpan.Zero, Alarms.NextWake([new Alarm(1, Now, "a")], Now));
    }

    [Fact]
    public void 毫秒以下的零頭進位_不會稍早醒來又空轉()
    {
        var wake = Alarms.NextWake([new Alarm(1, Now.AddTicks(12_345), "a")], Now);
        Assert.Equal(TimeSpan.FromMilliseconds(2), wake);
    }

    [Fact]
    public void 重複的提醒響過之後下一次是新的到點時間()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-wake-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var alarms = new Alarms(path);
            alarms.Add(Now.AddMinutes(1), "吃藥", "daily");
            Assert.Equal(TimeSpan.FromSeconds(30), Alarms.NextWake(alarms.List, Now));
            Assert.Equal(TimeSpan.FromSeconds(10), Alarms.NextWake(alarms.List, Now.AddSeconds(50)));
            Assert.Single(alarms.Check(Now.AddMinutes(1)));
            // 排到明天了：不再是 0（否則計時器會一直空轉）
            Assert.Equal(Alarms.Safety, Alarms.NextWake(alarms.List, Now.AddMinutes(1)));
            Assert.Equal(Now.AddDays(1).AddMinutes(1), alarms.List[0].At);
        }
        finally { File.Delete(path); }
    }
}

public class HistoryCacheTests
{
    private static string Line(string title, int minute) =>
        Shima.Listening.HistoryFile.ToLine(new Shima.Listening.Listen(new DateTimeOffset(2026, 1, 1, 12, minute, 0, TimeSpan.Zero), title, "歌手", "專輯", "smtc", 200, 200)) + "\n";

    [Fact]
    public void 檔案沒變就給同一份_變了才重讀()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-hist-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var cache = new Shima.Listening.HistoryCache();
            Assert.Empty(cache.Read(path).Listens);   // 沒有檔案

            File.WriteAllText(path, Line("a", 0));
            var first = cache.Read(path);
            Assert.True(first.Changed);
            Assert.Single(first.Listens);

            var again = cache.Read(path);
            Assert.False(again.Changed);
            Assert.Same(first.Listens, again.Listens);

            File.AppendAllText(path, Line("b", 5));
            var grown = cache.Read(path);
            Assert.True(grown.Changed);
            Assert.Equal(2, grown.Listens.Count);

            File.Delete(path);
            Assert.Empty(cache.Read(path).Listens);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 同樣大小但修改時間不同也會重讀()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-hist-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            File.WriteAllText(path, Line("a", 0));
            var cache = new Shima.Listening.HistoryCache();
            cache.Read(path);
            File.WriteAllText(path, Line("b", 0));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            var read = cache.Read(path);
            Assert.True(read.Changed);
            Assert.Equal("b", read.Listens[0].Title);
        }
        finally { File.Delete(path); }
    }
}

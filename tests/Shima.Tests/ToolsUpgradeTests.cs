// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class TodoOpsTests
{
    private static List<TodoItem> Items(params string[] texts) => texts.Select(t => new TodoItem { Text = t }).ToList();
    private static string Order(List<TodoItem> list) => string.Concat(list.Select(t => t.Text));

    [Theory]
    [InlineData(0, 3, "BCAD")]   // 往後放：空隙是照移動前算
    [InlineData(0, 4, "BCDA")]
    [InlineData(3, 0, "DABC")]
    [InlineData(2, 1, "ACBD")]
    public void 移動排序(int from, int gap, string expected)
    {
        var list = Items("A", "B", "C", "D");
        Assert.True(TodoOps.Move(list, from, gap));
        Assert.Equal(expected, Order(list));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]   // 放回自己前後的空隙等於沒動
    [InlineData(-1, 2)]
    [InlineData(4, 0)]
    [InlineData(0, 5)]
    [InlineData(0, -1)]
    public void 沒動的情況(int from, int gap)
    {
        var list = Items("A", "B", "C", "D");
        Assert.False(TodoOps.Move(list, from, gap));
        Assert.Equal("ABCD", Order(list));
    }

    [Fact]
    public void 清掉做完的只留沒做完的()
    {
        var list = Items("A", "B", "C");
        list[0].Done = true;
        list[2].Done = true;
        Assert.Equal(2, TodoOps.ClearDone(list));
        Assert.Equal("B", Order(list));
        Assert.Equal(0, TodoOps.ClearDone(list));
    }

    [Fact]
    public void 改文字_空的就刪掉()
    {
        var list = Items("A", "B");
        Assert.True(TodoOps.Rename(list, list[0], "  新的 "));
        Assert.Equal("新的", list[0].Text);
        Assert.False(TodoOps.Rename(list, list[0], "新的"));
        Assert.True(TodoOps.Rename(list, list[0], "   "));
        Assert.Equal("B", Order(list));
    }
}

public class RepeatAlarmTests
{
    private static string NewFile() => Path.Combine(AppContext.BaseDirectory, "alarm-tests", Guid.NewGuid().ToString("N") + ".json");

    [Theory]
    [InlineData("2026-11-23 09:00", "daily", "2026-11-23 08:00", "2026-11-24 08:00")]   // 已過的時刻排明天
    [InlineData("2026-11-23 07:00", "daily", "2026-11-23 08:00", "2026-11-23 08:00")]   // 還沒到的排今天
    [InlineData("2026-11-30 09:00", "daily", "2026-11-30 08:00", "2026-12-01 08:00")]   // 跨月
    [InlineData("2026-12-31 09:00", "daily", "2026-12-31 08:00", "2027-01-01 08:00")]   // 跨年
    [InlineData("2026-11-27 10:00", "weekdays", "2026-11-27 09:30", "2026-11-30 09:30")]   // 週五之後是下週一
    [InlineData("2026-11-28 10:00", "weekdays", "2026-11-28 09:30", "2026-11-30 09:30")]   // 週六
    [InlineData("2026-11-23 10:00", "weekdays", "2026-11-23 09:30", "2026-11-24 09:30")]
    [InlineData("2026-11-27 19:00", "fri", "2026-11-27 18:00", "2026-12-04 18:00")]   // 每週五：同一天已過就是下週
    [InlineData("2026-11-23 19:00", "fri", "2026-11-20 18:00", "2026-11-27 18:00")]
    [InlineData("2026-12-31 19:00", "thu", "2026-12-24 18:00", "2027-01-07 18:00")]
    [InlineData("2026-11-23 08:00", "daily", "2026-11-23 08:00", "2026-11-24 08:00")]   // 剛好到點：下一次一定在未來
    public void 下一次(string now, string repeat, string at, string expected) =>
        Assert.Equal(DateTime.Parse(expected), Alarms.NextAfter(DateTime.Parse(at), repeat, DateTime.Parse(now)));

    [Fact]
    public void 關機好幾天後只補響一次_並排到未來的下一次()
    {
        using var alarms = new Alarms(NewFile());
        var fired = new List<(Alarm, bool)>();
        alarms.Due += (a, late) => fired.Add((a, late));
        alarms.Add(new DateTime(2026, 11, 20, 8, 0, 0), "吃藥", "daily");
        var now = new DateTime(2026, 11, 25, 12, 0, 0);   // 錯過了 20、21、22、23、24、25 號
        Assert.Single(alarms.Check(now));
        Assert.Single(fired);
        Assert.True(fired[0].Item2);
        var next = Assert.Single(alarms.List);
        Assert.Equal(new DateTime(2026, 11, 26, 8, 0, 0), next.At);
        Assert.Equal("daily", next.Repeat);
        Assert.Empty(alarms.Check(now));
    }

    [Fact]
    public void 單次的照舊移除_重複的保留編號()
    {
        using var alarms = new Alarms(NewFile());
        var once = alarms.Add(new DateTime(2026, 11, 23, 8, 0, 0), "單次");
        var weekly = alarms.Add(new DateTime(2026, 11, 23, 9, 0, 0), "每週", "mon");
        Assert.Equal(2, alarms.Check(new DateTime(2026, 11, 23, 9, 0, 1)).Count);
        var left = Assert.Single(alarms.List);
        Assert.Equal(weekly.Id, left.Id);
        Assert.Equal(new DateTime(2026, 11, 30, 9, 0, 0), left.At);
        Assert.DoesNotContain(alarms.List, a => a.Id == once.Id);
    }

    [Fact]
    public void 重複的存檔讀得回來_舊存檔沒有欄位當作不重複()
    {
        var file = NewFile();
        using (var alarms = new Alarms(file)) alarms.Add(new DateTime(2026, 11, 24, 8, 0, 0), "吃藥", "weekdays");
        using var again = new Alarms(file);
        Assert.Equal("weekdays", Assert.Single(again.List).Repeat);

        var old = Alarms.Parse("""[{"Id":3,"At":"2026-11-24T08:00:00","Text":"舊的"}]""");
        Assert.Equal("舊的", Assert.Single(old).Text);
        Assert.Null(old[0].Repeat);
    }

    [Fact]
    public void 不認得的重複方式當作不重複()
    {
        using var alarms = new Alarms(NewFile());
        Assert.Null(alarms.Add(new DateTime(2026, 11, 24, 8, 0, 0), "x", "monthly").Repeat);
        Assert.Equal("fri", Alarms.NormalizeRepeat(" Fri "));
    }

    private static readonly DateTime Now = new(2026, 11, 23, 14, 0, 0);   // 星期一

    [Theory]
    [InlineData("每天 8:00 吃藥", "daily", "2026-11-24 08:00", "吃藥")]
    [InlineData("每天 20:00 吃藥", "daily", "2026-11-23 20:00", "吃藥")]
    [InlineData("每天8點半吃藥", "daily", "2026-11-24 08:30", "吃藥")]
    [InlineData("平日 9:30 站會", "weekdays", "2026-11-24 09:30", "站會")]
    [InlineData("每週五 18:00 倒垃圾", "fri", "2026-11-27 18:00", "倒垃圾")]
    [InlineData("每周一 15:00 週報", "mon", "2026-11-23 15:00", "週報")]
    [InlineData("每星期日 10:00 大掃除", "sun", "2026-11-29 10:00", "大掃除")]
    [InlineData("every day 8:00 take pills", "daily", "2026-11-24 08:00", "take pills")]
    [InlineData("Weekdays 9:30 standup", "weekdays", "2026-11-24 09:30", "standup")]
    [InlineData("every friday 18:00 trash", "fri", "2026-11-27 18:00", "trash")]
    [InlineData("每天 下午3點 散步", "daily", "2026-11-23 15:00", "散步")]
    public void 重複的快速輸入(string line, string repeat, string at, string text)
    {
        var result = Alarms.ParseLine(line, Now);
        Assert.NotNull(result);
        Assert.Equal(repeat, result.Value.Repeat);
        Assert.Equal(DateTime.Parse(at), result.Value.At);
        Assert.Equal(text, result.Value.Text);
    }

    [Theory]
    [InlineData("每天 吃藥")]
    [InlineData("每天 明天 8:00 吃藥")]
    [InlineData("每週 8:00 吃藥")]
    [InlineData("平日")]
    [InlineData("every month 8:00 x")]
    public void 重複的看不懂(string line) => Assert.Null(Alarms.ParseLine(line, Now));

    [Fact]
    public void 單次的寫法沒有重複()
    {
        Assert.Null(Alarms.ParseLine("15:30 開會", Now)!.Value.Repeat);
        Assert.Null(Alarms.ParseLine("20m 喝水", Now)!.Value.Repeat);
    }

    [Fact]
    public void 語音助手的提醒工具可以重複()
    {
        using var alarms = new Alarms(NewFile());
        var reply = Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "08:00", ["repeat"] = "weekdays", ["text"] = "吃藥" }, alarms, new DateTime(2026, 11, 27, 9, 0, 0));
        Assert.StartsWith("已設定重複提醒 #1：平日 08:00，下一次 11/30 08:00", reply);
        var alarm = Assert.Single(alarms.List);
        Assert.Equal("weekdays", alarm.Repeat);
        Assert.Equal(new DateTime(2026, 11, 30, 8, 0, 0), alarm.At);
        Assert.Contains("平日 08:00", Skills.Alarm(new JsonObject { ["action"] = "list" }, alarms, Now));
        Assert.StartsWith("重複的提醒要用 at", Skills.Alarm(new JsonObject { ["action"] = "add", ["minutes"] = 10, ["repeat"] = "daily" }, alarms, Now));
        Assert.StartsWith("看不懂這個重複方式", Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "08:00", ["repeat"] = "monthly" }, alarms, Now));
        Assert.Single(alarms.List);
    }

    [Fact]
    public void 提醒工具的重複參數在定義裡()
    {
        var tool = AssistantTool.All.Single(t => t.Name == "alarm");
        var repeat = (JsonObject)tool.Properties["repeat"]!;
        Assert.Contains("weekdays", ((JsonArray)repeat["enum"]!).Select(v => v!.ToString()));
        Assert.DoesNotContain("repeat", tool.Required);
    }
}

public class PomodoroCountTests
{
    private static readonly DateTime T0 = new(2026, 11, 23, 9, 0, 0);

    private static Pomodoro Fast() => new() { Focus = TimeSpan.FromMinutes(25), Break = TimeSpan.FromMinutes(5) };

    [Fact]
    public void 專注走完才算一個_休息不算()
    {
        var p = Fast();
        p.Toggle(T0);
        Assert.Equal(0, p.CompletedToday(T0));
        Assert.False(p.Tick(T0.AddMinutes(24)));
        Assert.True(p.Tick(T0.AddMinutes(25)));   // 專注結束 → 休息
        Assert.Equal(1, p.CompletedToday(T0.AddMinutes(25)));
        Assert.True(p.Tick(T0.AddMinutes(30)));   // 休息結束 → 專注
        Assert.Equal(1, p.CompletedToday(T0.AddMinutes(30)));
        Assert.True(p.Tick(T0.AddMinutes(55)));
        Assert.Equal(2, p.CompletedToday(T0.AddMinutes(55)));
    }

    [Fact]
    public void 重來和暫停不影響計數()
    {
        var p = Fast();
        p.Toggle(T0);
        p.Tick(T0.AddMinutes(25));
        p.Reset();
        Assert.Equal(1, p.CompletedToday(T0.AddMinutes(26)));
        p.Toggle(T0.AddMinutes(26));
        p.Toggle(T0.AddMinutes(30));   // 暫停
        Assert.Equal(1, p.CompletedToday(T0.AddMinutes(31)));
    }

    [Fact]
    public void 跨日歸零()
    {
        var p = Fast();
        var late = new DateTime(2026, 11, 23, 23, 50, 0);
        p.Toggle(late.AddMinutes(-25));
        p.Tick(late);
        Assert.Equal(1, p.CompletedToday(late));
        Assert.Equal(0, p.CompletedToday(new DateTime(2026, 11, 24, 0, 5, 0)));
        // 隔天再完成一個：從 1 開始，不是接著昨天
        p.Tick(late.AddMinutes(5));
        p.Tick(late.AddMinutes(30));
        Assert.Equal(1, p.CompletedToday(new DateTime(2026, 11, 24, 0, 20, 0)));
    }

    [Theory]
    [InlineData(0, "○○○○")]
    [InlineData(1, "●○○○")]
    [InlineData(3, "●●●○")]
    [InlineData(4, "●●●●")]
    [InlineData(5, "● 5")]
    [InlineData(12, "● 12")]
    public void 小圓點(int count, string expected) => Assert.Equal(expected, Pomodoro.Dots(count));
}

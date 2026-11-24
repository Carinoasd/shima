// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class IdleLineTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 14, 5, 0);
    private static readonly CultureInfo Tw = CultureInfo.GetCultureInfo("zh-TW");

    private static string Say(string? mode, string? timer = null, params Alarm[] alarms) => IdleLine.Text(mode, Now, timer, alarms, Tw, "明天");

    [Fact]
    public void 時間是預設()
    {
        Assert.Equal("14:05", Say("clock"));
        Assert.Equal("14:05", Say(null));
        Assert.Equal("14:05", Say("亂寫的"));
    }

    [Fact]
    public void 日期與時間()
    {
        var text = Say("date");
        Assert.EndsWith("14:05", text);
        Assert.Contains("11", text);
        Assert.Contains("24", text);
        Assert.Contains("週二", text);
    }

    [Fact]
    public void 下一個提醒取最近的那一個()
    {
        var later = new Alarm(1, new DateTime(2026, 11, 24, 18, 0, 0), "晚餐");
        var sooner = new Alarm(2, new DateTime(2026, 11, 24, 15, 30, 0), "跟設計開會");
        Assert.Equal("15:30 跟設計開會", Say("alarm", null, later, sooner));
    }

    [Fact]
    public void 明天的提醒前面有明天()
    {
        Assert.Equal("明天 08:00 吃藥", Say("alarm", null, new Alarm(1, new DateTime(2026, 11, 25, 8, 0, 0), "吃藥", "daily")));
    }

    [Fact]
    public void 沒有提醒就顯示時間()
    {
        Assert.Equal("14:05", Say("alarm"));
    }

    [Fact]
    public void 太長的提醒截斷()
    {
        var text = Say("alarm", null, new Alarm(1, new DateTime(2026, 11, 24, 15, 30, 0), "跟設計部門的所有人一起開今年最後一次的檢討會議"));
        Assert.EndsWith("…", text);
        Assert.True(text.Length < 20);
        var latin = IdleLine.Trim("review the quarterly numbers with finance", 20);
        Assert.Equal(21, latin.Length);
        Assert.EndsWith("…", latin);
    }

    [Fact]
    public void 不顯示是空的()
    {
        Assert.Equal("", Say("none"));
        Assert.Equal("", Say("none", null, new Alarm(1, new DateTime(2026, 11, 24, 15, 30, 0), "開會")));
    }

    [Theory]
    [InlineData("clock")]
    [InlineData("date")]
    [InlineData("alarm")]
    [InlineData("none")]
    public void 番茄鐘優先(string mode)
    {
        Assert.Equal("專注 24:10", Say(mode, "專注 24:10", new Alarm(1, new DateTime(2026, 11, 24, 15, 30, 0), "開會")));
    }

    [Fact]
    public void 各語言的日期都不會出錯也不會太長()
    {
        foreach (var (code, _) in Loc.Languages)
        {
            var culture = CultureInfo.GetCultureInfo(code);
            var text = IdleLine.Text("date", Now, null, [], culture, "x");
            Assert.EndsWith("14:05", text);
            Assert.InRange(text.Length, 8, 24);
            var day = IdleLine.Day(Now, culture);
            Assert.DoesNotContain("2026", day);
            Assert.Contains("24", day);
        }
    }
}

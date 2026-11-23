// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class CalendarAlarmTests
{
    // 2026-11-24 是星期二
    private static readonly DateTime Now = new(2026, 11, 24, 14, 0, 0);

    private static int[] Days(params Alarm[] alarms) => Alarms.InMonth(alarms, Now, Now).Keys.ToArray();

    [Fact]
    public void 單次只標那一天()
    {
        Assert.Equal([28], Days(new Alarm(1, new DateTime(2026, 11, 28, 9, 0, 0), "交報告")));
    }

    [Fact]
    public void 今天還沒到的算_已經過的不算()
    {
        Assert.Equal([24], Days(new Alarm(1, new DateTime(2026, 11, 24, 15, 30, 0), "開會")));
        Assert.Empty(Days(new Alarm(1, new DateTime(2026, 11, 24, 9, 0, 0), "早就過了")));
        Assert.Empty(Days(new Alarm(1, new DateTime(2026, 11, 20, 9, 0, 0), "上週")));
    }

    [Fact]
    public void 別的月份的單次不標()
    {
        Assert.Empty(Days(new Alarm(1, new DateTime(2026, 12, 2, 9, 0, 0), "下個月")));
    }

    [Fact]
    public void 每天從現在之後每一天都標()
    {
        var days = Days(new Alarm(1, new DateTime(2026, 11, 25, 8, 0, 0), "吃藥", "daily"));
        Assert.Equal(Enumerable.Range(25, 6), days);   // 25 到 30
    }

    [Fact]
    public void 每天的今天時間還沒到就算今天()
    {
        var days = Days(new Alarm(1, new DateTime(2026, 11, 24, 18, 0, 0), "散步", "daily"));
        Assert.Equal(Enumerable.Range(24, 7), days);
    }

    [Fact]
    public void 每天的今天時間已過從明天開始()
    {
        var days = Days(new Alarm(1, new DateTime(2026, 11, 24, 8, 0, 0), "吃藥", "daily"));
        Assert.Equal(Enumerable.Range(25, 6), days);
    }

    [Fact]
    public void 平日跳過週末()
    {
        // 11/28 週六、11/29 週日
        Assert.Equal([25, 26, 27, 30], Days(new Alarm(1, new DateTime(2026, 11, 25, 9, 30, 0), "站會", "weekdays")));
    }

    [Fact]
    public void 每週只標那一天()
    {
        Assert.Equal([27], Days(new Alarm(1, new DateTime(2026, 11, 27, 18, 0, 0), "倒垃圾", "fri")));
        Assert.Equal([30], Days(new Alarm(1, new DateTime(2026, 11, 30, 9, 0, 0), "月初", "mon")));
    }

    [Fact]
    public void 重複的提醒下一次在下個月_這個月不標()
    {
        var late = new DateTime(2026, 11, 30, 20, 0, 0);
        Assert.Empty(Alarms.InMonth([new Alarm(1, new DateTime(2026, 12, 1, 8, 0, 0), "月初", "daily")], late, late));
    }

    [Fact]
    public void 跨月看下個月_整個月都在未來()
    {
        var next = new DateTime(2026, 12, 1);
        var days = Alarms.InMonth([new Alarm(1, new DateTime(2026, 11, 25, 9, 30, 0), "站會", "weekdays")], next, Now).Keys.ToArray();
        Assert.Equal(23, days.Length);   // 12 月有 23 個平日
        Assert.Equal(1, days[0]);
        Assert.DoesNotContain(5, days);   // 12/5 週六
    }

    [Fact]
    public void 同一天的提醒照時間排()
    {
        var map = Alarms.InMonth([
            new Alarm(1, new DateTime(2026, 11, 28, 18, 0, 0), "晚餐"),
            new Alarm(2, new DateTime(2026, 11, 28, 9, 0, 0), "交報告"),
            new Alarm(3, new DateTime(2026, 11, 25, 9, 30, 0), "站會", "weekdays")], Now, Now);
        Assert.Equal(["交報告", "晚餐"], map[28].Select(a => a.Text));
        Assert.Equal(new DateTime(2026, 11, 25, 9, 30, 0), map[25][0].At);
    }

    [Fact]
    public void 過期沒處理到的重複提醒從下一次算()
    {
        // At 是好幾天前（關機錯過）：從現在往後推，不補標過去的日子
        Assert.Equal([25, 26, 27, 30], Days(new Alarm(1, new DateTime(2026, 11, 2, 9, 30, 0), "站會", "weekdays")));
    }

    [Theory]
    [InlineData("2026-11-28 交報告", 2026, 11, 28, 9, 0, "交報告")]
    [InlineData("2026-11-28", 2026, 11, 28, 9, 0, "")]
    [InlineData("2026/11/28 交報告", 2026, 11, 28, 9, 0, "交報告")]
    [InlineData("2026-11-28 15:30 交報告", 2026, 11, 28, 15, 30, "交報告")]
    [InlineData("2026-11-28 7點半 交報告", 2026, 11, 28, 7, 30, "交報告")]
    [InlineData("2026-11-28 3個報告", 2026, 11, 28, 9, 0, "3個報告")]
    [InlineData("2026-12-20 開會", 2026, 12, 20, 9, 0, "開會")]
    public void 只有日期的輸入沒寫時間就是早上九點(string line, int y, int mo, int d, int h, int mi, string text)
    {
        var parsed = Alarms.ParseLine(line, Now);
        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), parsed.Value.At);
        Assert.Equal(text, parsed.Value.Text);
        Assert.Null(parsed.Value.Repeat);
    }

    [Theory]
    [InlineData("2026-11-23 昨天")]
    [InlineData("2026-11-24 今天九點已過")]
    [InlineData("2026-02-30 沒這天")]
    [InlineData("2026-13-01 沒這月")]
    [InlineData("2027-03-25 太遠")]
    public void 過去_不存在_太遠的日期不收(string line)
    {
        Assert.Null(Alarms.ParseLine(line, Now));
    }

    [Fact]
    public void 六十天內的日子都排得進去()
    {
        Assert.NotNull(Alarms.ParseLine("2026-12-31 年底", Now));
        Assert.NotNull(Alarms.ParseLine("2027-01-22 07:30 出門", Now));
        Assert.Equal(new DateTime(2027, 1, 22, 7, 30, 0), Alarms.When("2027-01-22 07:30", null, Now));
        Assert.Null(Alarms.When("2027-01-24 07:30", null, Now));
    }
}

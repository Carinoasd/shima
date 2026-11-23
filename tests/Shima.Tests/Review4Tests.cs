// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class Review4Tests
{
    [Theory]
    [InlineData("2026-11-8 07:30 開會")]
    [InlineData("2026/11/8 07:30 開會")]
    [InlineData("2026-11-08 7:30 開會")]
    public void 日期沒補零又帶時間也收(string line)
    {
        var parsed = Alarms.ParseLine(line, new DateTime(2026, 11, 5, 10, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 8, 7, 30, 0), parsed?.At);
        Assert.Equal("開會", parsed?.Text);
    }

    [Fact]
    public void 下午點今天沒寫時間_排在下一個整點()
    {
        var now = new DateTime(2026, 11, 24, 14, 20, 0);
        var parsed = Alarms.ParseLine("2026-11-24 交報告", now);
        Assert.Equal(new DateTime(2026, 11, 24, 15, 0, 0), parsed?.At);
        Assert.Equal("交報告", parsed?.Text);
    }

    [Fact]
    public void 今天早上九點前沒寫時間_還是九點()
    {
        var parsed = Alarms.ParseLine("2026-11-24 交報告", new DateTime(2026, 11, 24, 7, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 24, 9, 0, 0), parsed?.At);
    }

    [Fact]
    public void 今天有寫的過去時間還是不收_二十三點後沒有下一個整點()
    {
        Assert.Null(Alarms.ParseLine("2026-11-24 8:00 交報告", new DateTime(2026, 11, 24, 14, 0, 0)));
        Assert.Null(Alarms.ParseLine("2026-11-24 交報告", new DateTime(2026, 11, 24, 23, 30, 0)));
    }

    [Fact]
    public void 語音助手排遠的提醒_等待時間用天()
    {
        var now = new DateTime(2026, 11, 24, 10, 0, 0);
        using var alarms = new Alarms(Path.Combine(Path.GetTempPath(), "shima-r4-" + Guid.NewGuid().ToString("N") + ".json"));
        var far = Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "2027-01-22 12:00", ["text"] = "遠的" }, alarms, now);
        Assert.Contains("59 天 2 小時後", far);
        var near = Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "2026-11-24 12:30", ["text"] = "近的" }, alarms, now);
        Assert.Contains("2 小時 30 分後", near);
    }
}

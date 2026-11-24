// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：提醒的文字解析。</summary>
public class FuzzAlarmsFindingsTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

    [Fact]
    public void ParseLine_LoneSurrogate_DoesNotThrow()
    {
        Assert.Null(Alarms.ParseLine("\uDE00", Now));
        var parsed = Alarms.ParseLine("15:30 開會\uD83D", Now);
        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 11, 24, 15, 30, 0), parsed!.Value.At);
        Assert.Null(Alarms.When("7\uD83D:30", null, Now));
    }

    [Theory]
    [InlineData("٢٠٢٦-١١-٢٨ 開會")]
    [InlineData("٧:٣٠ 開會")]
    [InlineData("٢٠分鐘 喝水")]
    [InlineData("每天 ٨:٠٠ 吃藥")]
    public void ParseLine_NonAsciiDigits_AreNotNumbers(string line)
    {
        Assert.Null(Alarms.ParseLine(line, Now));
    }

    [Theory]
    [InlineData("9999-12-31")]
    [InlineData("9999-12-31 下午3點")]
    [InlineData("9999-12-31 23:59 x")]
    [InlineData("0001-01-01")]
    public void ParseLine_DatesAtTheEndsOfTheCalendar_AreJustTooFar(string line)
    {
        Assert.Null(Alarms.ParseLine(line, Now));
    }

    [Fact]
    public void ParseLine_FullWidthDigits_StillWork()
    {
        // NFKC 會把全形數字換成半形，所以這個不受影響
        Assert.Equal(new DateTime(2026, 11, 28, 9, 0, 0), Alarms.ParseLine("２０２６－１１－２８ 交報告", Now)?.At is { } at ? at : default);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.0083)]
    [InlineData(double.Epsilon)]
    public void When_MinutesThatRoundToZeroSeconds_AreNotInTheFuture(double minutes)
    {
        Assert.Null(Alarms.When(null, minutes, Now));
        Assert.Null(Alarms.ParseLine($"{minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)}分鐘 x", Now));
        Assert.Equal(Now.AddSeconds(1), Alarms.When(null, 1 / 60.0, Now));
    }
}

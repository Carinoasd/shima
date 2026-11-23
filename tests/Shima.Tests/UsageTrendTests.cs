// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class UsageTrendTests
{
    private static string Line(string id, string model, int input, int output, string time) =>
        "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"timestamp\":\"" + time + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"" + model + "\",\"role\":\"assistant\",\"content\":[],\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + "}}}";

    private static readonly DateOnly Last = new(2026, 5, 14);

    [Fact]
    public void 十四天每天一筆且空的日子補零()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-opus-5-5", 1_000_000, 0, "2026-05-14T03:00:00Z"));
        var trend = ledger.Trend(Last, 14, TimeZoneInfo.Utc);
        Assert.Equal(14, trend.Days.Count);
        Assert.Equal(new DateOnly(2026, 5, 1), trend.Days[0].Day);
        Assert.Equal(Last, trend.Days[^1].Day);
        Assert.All(trend.Days.Take(13), d => { Assert.Equal(0, d.Tokens.Total); Assert.Equal(0m, d.Cost); Assert.True(d.CostKnown); });
        Assert.Equal(4m, trend.Days[^1].Cost);
    }

    [Fact]
    public void 邊界是第十四天的開頭到最後一天的結尾()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("before", "claude-opus-5-5", 100, 0, "2026-04-30T23:59:59Z"));
        ledger.Feed(Line("first", "claude-opus-5-5", 1, 0, "2026-05-01T00:00:00Z"));
        ledger.Feed(Line("last", "claude-opus-5-5", 2, 0, "2026-05-14T23:59:59Z"));
        ledger.Feed(Line("after", "claude-opus-5-5", 100, 0, "2026-05-15T00:00:00Z"));
        var trend = ledger.Trend(Last, 14, TimeZoneInfo.Utc);
        Assert.Equal(1, trend.Days[0].Tokens.Total);
        Assert.Equal(2, trend.Days[^1].Tokens.Total);
        Assert.Equal(3, trend.Tokens.Total);
    }

    [Fact]
    public void 日期照本地時間切()
    {
        var plus8 = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(8), "t", "t");
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-opus-5-5", 5, 0, "2026-05-13T16:30:00Z"));   // 本地已經是 14 號 00:30
        ledger.Feed(Line("b", "claude-opus-5-5", 7, 0, "2026-05-13T15:30:00Z"));   // 本地 13 號 23:30
        var trend = ledger.Trend(Last, 14, plus8);
        Assert.Equal(5, trend.Days[^1].Tokens.Total);
        Assert.Equal(7, trend.Days[^2].Tokens.Total);
    }

    [Fact]
    public void 同一則訊息重複出現只算一次()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("m", "claude-opus-5-5", 10, 1, "2026-05-14T01:00:00Z"));
        ledger.Feed(Line("m", "claude-opus-5-5", 10, 50, "2026-05-14T01:00:01Z"));
        ledger.Feed(Line("m", "claude-opus-5-5", 10, 50, "2026-05-14T01:00:01Z"));
        var trend = ledger.Trend(Last, 14, TimeZoneInfo.Utc);
        Assert.Equal(60, trend.Days[^1].Tokens.Total);
        Assert.Equal(60, trend.Models.Single().Tokens.Total);
    }

    [Fact]
    public void 價格不明的模型花費是不知道而不是零()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-opus-5-5", 1_000_000, 0, "2026-05-13T01:00:00Z"));
        ledger.Feed(Line("b", "claude-unknown-9", 500, 0, "2026-05-14T01:00:00Z"));
        ledger.Feed(Line("c", "claude-opus-5-5", 1_000_000, 0, "2026-05-14T02:00:00Z"));
        var trend = ledger.Trend(Last, 14, TimeZoneInfo.Utc);
        Assert.True(trend.Days[^2].CostKnown);
        Assert.False(trend.Days[^1].CostKnown);
        Assert.Equal(4m, trend.Days[^1].Cost);   // 只算了認得的部分
        Assert.False(trend.CostKnown);
        Assert.Null(trend.Models.Single(m => m.Model == "claude-unknown-9").Cost);
        Assert.Equal(8m, trend.Models.Single(m => m.Model == "claude-opus-5-5").Cost);
    }

    [Fact]
    public void 沒有時間的紀錄不算進哪一天()
    {
        var ledger = new UsageLedger();
        ledger.Feed("""{"type":"assistant","sessionId":"s","message":{"id":"x","model":"claude-opus-5-5","usage":{"input_tokens":99,"output_tokens":1}}}""");
        Assert.Equal(0, ledger.Trend(Last, 14, TimeZoneInfo.Utc).Tokens.Total);
    }

    [Fact]
    public void 各模型合計跨天加總並依token排序()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-haiku-5-5", 10, 0, "2026-05-10T01:00:00Z"));
        ledger.Feed(Line("b", "anthropic.claude-opus-5-5-20260101-v1:0", 100, 0, "2026-05-11T01:00:00Z"));
        ledger.Feed(Line("c", "claude-opus-5-5", 200, 0, "2026-05-12T01:00:00Z"));
        var trend = ledger.Trend(Last, 14, TimeZoneInfo.Utc);
        Assert.Equal(["claude-opus-5-5", "claude-haiku-5-5"], trend.Models.Select(m => m.Model));
        Assert.Equal(300, trend.Models[0].Tokens.Total);
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class Review3Tests
{
    private static string Line(string id, string time, int input = 10) =>
        "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"timestamp\":\"" + time + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[],\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":1}}}";

    [Fact]
    public void 帳本不收超出範圍的舊紀錄()
    {
        var ledger = new UsageLedger { Oldest = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc) };
        Assert.False(ledger.Feed(Line("old", "2026-04-01T03:00:00Z")));
        Assert.True(ledger.Feed(Line("new", "2026-05-02T03:00:00Z")));
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void Trim_只拿掉早於門檻的紀錄_Clear_全清()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "2026-04-01T03:00:00Z"));
        ledger.Feed(Line("b", "2026-05-02T03:00:00Z"));
        ledger.Trim(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, ledger.Count);
        ledger.Clear();
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public async Task 趨勢快取跨日就重讀_最後一根永遠是今天()
    {
        var root = Path.Combine(Path.GetTempPath(), "shima-r3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, ".claude");
            var file = Path.Combine(folder, "projects", "p", "s1.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var now = new DateTime(2026, 5, 14, 23, 59, 40);
            File.WriteAllText(file, Line("a", now.AddMinutes(-5).ToUniversalTime().ToString("o")) + "\n");
            var usage = new AgentUsage(() => [folder], () => now);
            Assert.Equal(new DateOnly(2026, 5, 14), (await usage.TrendAsync()).Days[^1].Day);
            now = now.AddSeconds(30);   // 已經過午夜、還在 60 秒內
            Assert.Equal(new DateOnly(2026, 5, 15), (await usage.TrendAsync()).Days[^1].Day);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task 釋放趨勢帳之後再問會重讀()
    {
        var root = Path.Combine(Path.GetTempPath(), "shima-r3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, ".claude");
            var file = Path.Combine(folder, "projects", "p", "s1.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var now = new DateTime(2026, 5, 14, 12, 0, 0);
            File.WriteAllText(file, Line("a", now.AddMinutes(-5).ToUniversalTime().ToString("o"), 1000) + "\n");
            var usage = new AgentUsage(() => [folder], () => now);
            Assert.Equal(1001, (await usage.TrendAsync()).Tokens.Total);
            await usage.ReleaseTrendAsync();
            Assert.Equal(1001, (await usage.TrendAsync()).Tokens.Total);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task 不存在的_hook_路徑會被移出盯著的清單_而且記錄檔不寫路徑()
    {
        var root = Path.Combine(Path.GetTempPath(), "shima-r3-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, ".claude");
            Directory.CreateDirectory(Path.Combine(folder, "projects"));
            var usage = new AgentUsage(() => [folder], () => new DateTime(2026, 5, 14, 12, 0, 0));
            usage.Watch(Path.Combine(folder, "projects", "x", "gone.jsonl"));   // 要像真的對話紀錄，不然 Watch 一開始就不收
            await usage.TodayAsync();
            var watched = (System.Collections.Generic.HashSet<string>)typeof(AgentUsage).GetField("_watched", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(usage)!;
            Assert.Empty(watched);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void 暫停挑出所有正在播的_不看有沒有標題()
    {
        var picks = SmtcSource.PausePicks([("Chrome", true), ("Spotify.exe", true), ("Roon.exe", true), ("Edge", false)], id => id.Contains("Roon"));
        Assert.Equal([0, 1], picks);
        Assert.Empty(SmtcSource.PausePicks([("A", false)], null));
    }

    [Fact]
    public void Roon_每個正在播的_zone_都要暫停()
    {
        JsonObject Zone(string id, string state) => new() { ["zone_id"] = id, ["state"] = state };
        var ids = RoonSource.PlayingZones([Zone("a", "playing"), Zone("b", "paused"), Zone("c", "playing"), Zone("d", "stopped")]);
        Assert.Equal(["a", "c"], ids);
    }

    [Fact]
    public void 用量的數字跟著語言()
    {
        var de = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("1,2M", UsageView.Amount(1_234_000, de));
        Assert.Equal("$1,50+", UsageView.Money(1.5m, false, de));
        Assert.Equal("1.2M", UsageView.Amount(1_234_000, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void 用量那一行_今天沒用但圖開著或有連線時照樣顯示()
    {
        Assert.False(IslandWindow.ShowsUsageLine(0, false, false));
        Assert.True(IslandWindow.ShowsUsageLine(0, true, false));
        Assert.True(IslandWindow.ShowsUsageLine(0, false, true));
        Assert.True(IslandWindow.ShowsUsageLine(5, false, false));
    }

    [Fact]
    public void 年份壞掉的紀錄不參與統計_一直往前翻也不丟例外()
    {
        var bad = new Listen(new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero), "t", "a", "al", "s", 200, 200);
        var good = new Listen(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), "t", "a", "al", "s", 200, 200);
        var now = new DateTime(2026, 11, 23, 20, 0, 0);
        var summary = Stats.Compute([bad, good], StatsPeriod.All, now, DayOfWeek.Monday);
        Assert.Equal(1, summary.Plays);
        Assert.Equal(new DateTime(2026, 11, 20), summary.From);
        foreach (var period in new[] { StatsPeriod.Week, StatsPeriod.Month, StatsPeriod.Year })
            Assert.False(Stats.CanGoBack(period, now, DayOfWeek.Monday, new DateTime(1, 1, 1), 1_000_000));
    }

    [Fact]
    public void 跨年的那一週標題兩端都有年份()
    {
        var from = new DateTime(2025, 12, 29);
        var summary = new StatsSummary(from, from.AddDays(7), TimeSpan.FromMinutes(30), 5,
            [new Ranked("歌", "藝人", TimeSpan.FromMinutes(20), 3)], [new Ranked("藝人", "", TimeSpan.FromMinutes(20), 3)], [], [], new TimeSpan[24], 0);
        var title = RecapCard.Content(summary, StatsPeriod.Week, new DateTime(2026, 1, 5), CultureInfo.GetCultureInfo("en-US"), x => x)!.Period;
        Assert.Contains("2025", title);
        Assert.Contains("2026", title);
        Assert.Equal(title, Stats.Label(StatsPeriod.Week, from, CultureInfo.GetCultureInfo("en-US")));
    }
}

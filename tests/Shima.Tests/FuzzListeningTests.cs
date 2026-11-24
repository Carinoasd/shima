// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Agents;
using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>聆聽紀錄與統計、Last.fm、Claude Code 的用量紀錄：壞掉的檔案行與隨機的時間。</summary>
public class FuzzListeningTests
{
    private static readonly string[] LineParts =
    [
        """{"start":"2026-11-24T10:00:00+08:00","title":"歌","artist":"A","album":"B","source":"roon","duration":200,"listened":180}""",
        """{"start":"2026-11-24T10:00:00+08:00","title":"歌","artist":"A","album":"","source":"","duration":0,"listened":31.5}""",
        """{"start":"0001-01-01T00:00:00+00:00","title":"x","artist":"","album":"","source":"s","duration":1,"listened":1}""",
        """{"start":"9999-12-31T23:59:59+00:00","title":"x","artist":"","album":"","source":"s","duration":1,"listened":1}""",
        """{"start":"1999-12-31T23:59:59+00:00","title":"old","artist":"","album":"","source":"s","duration":1,"listened":1}""",
        """{"start":"2026-11-24T10:00:00Z","title":"x","artist":"","album":"","source":"s","duration":1,"listened":1e300}""",
        """{"start":"2026-11-24T10:00:00Z","title":"x","artist":"","album":"","source":"s","duration":-5,"listened":-5}""",
        """{"start":"2026-11-24T10:00:00Z","title":"x","artist":"","album":"","source":"s","duration":1e400,"listened":1}""",
        """{"start":"2026-11-24T10:00:00Z","title":null,"artist":"","album":"","source":"s","duration":1,"listened":1}""",
        """{"start":"not a date","title":"x","artist":"","album":"","source":"s","duration":1,"listened":1}""",
        """{"start":5,"title":"x"}""", """{"title":"x"}""", """{}""", "[]", "null", "5", "\"s\"", "{", "}", """{"start":"2026-11-24T10:00:00Z","title":"x","artist":"a\ud83d","album":"","source":"s","duration":1,"listened":1}""",
        """{"start":"2026-11-24T10:00:00+99:00","title":"x","artist":"","album":"","source":"s","duration":1,"listened":1}""", "\uFEFF", "", " ", "\r",
    ];

    private static string HistoryText(Random r)
    {
        var text = string.Join(r.Pick(["\n", "\r\n", "\n\n", "\n"]), Enumerable.Range(0, r.Next(0, 8)).Select(_ => r.Chance(0.2) ? r.Pick(Gen.Noise) : r.Mangle(r.Pick(LineParts))));
        text = r.Chance(0.3) ? r.Mangle(text) : text;
        return Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));   // 檔案是 UTF-8 讀進來的，不會有孤立的代理字元
    }

    [Fact]
    public void HistoryFile_Parse_NeverThrows_And_KeepsOnlyCompleteRecords()
    {
        Fuzz.Run("HistoryFile.FromLine／Parse", 7001, 3000, HistoryText, text =>
        {
            var listens = HistoryFile.Parse(text);
            foreach (var l in listens)
            {
                Assert.NotNull(l.Title); Assert.NotNull(l.Artist); Assert.NotNull(l.Album); Assert.NotNull(l.Source);
                if (!double.IsFinite(l.Duration) || !double.IsFinite(l.Listened)) continue;   // 1e400 讀進來是無限大，寫不回去；寫檔的只有 ListenTracker，不會產生
                var back = HistoryFile.FromLine(HistoryFile.ToLine(l))!;
                Assert.Equal(l.Title, back.Title);
            }
            // 壞掉的紀錄不能讓統計丟例外
            var now = new DateTime(2026, 11, 24, 12, 0, 0);
            foreach (var period in Enum.GetValues<StatsPeriod>()) Stats.Compute(listens, period, now, DayOfWeek.Monday);
        }, Fuzz.Escape, perCallMs: 300);
    }

    private static Listen RandomListen(Random r, DateTime now)
    {
        var start = now.AddMinutes(-r.Next(-2 * 24 * 60, 800 * 24 * 60));
        double listened = r.Pick([0.0, 1, 31, 100, 180, 600, 3600, -5, 0.5]) + r.NextDouble();
        return new Listen(new DateTimeOffset(start, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(r.Pick([0, 8, -5, 9, 14]))),
            r.Pick(["歌", "Song", "song ", "SONG", "", "x"]), r.Pick(["A", "a", " A", "B", ""]), r.Pick(["Best", "best", "", "Z"]), "roon", r.Pick([0.0, 200, 300]), listened);
    }

    [Fact]
    public void Stats_Compute_AddsUp_ForAnyHistoryAndAnyNow()
    {
        Fuzz.Run("Stats.Compute／Range／Fill／Bars／Hours／Streak", 7002, 1000, r =>
        {
            var now = new DateTime(2026, 1, 1).AddMinutes(r.Next(0, 4 * 365 * 24 * 60));
            if (r.Chance(0.1)) now = new DateTime(r.Pick([2026, 2027, 2028]), r.Pick([1, 2, 12]), r.Pick([1, 28, 29, 31]) is var d && d > 28 ? 28 : d, r.Pick([0, 23]), 59, 59);
            var listens = Enumerable.Range(0, r.Next(0, 40)).Select(_ => RandomListen(r, now)).ToList();
            return (listens, now, r.Pick(Enum.GetValues<StatsPeriod>()), r.Pick(Enum.GetValues<DayOfWeek>()), r.Next(0, 4));
        }, v =>
        {
            var (listens, now, period, firstDay, back) = v;
            var earliest = listens.Where(Stats.Plausible).Select(l => l.Start.DateTime).DefaultIfEmpty().Min();
            if (period != StatsPeriod.All && back > 0 && !Stats.CanGoBack(period, now, firstDay, earliest == default ? null : earliest, back - 1)) back = 0;
            var s = Stats.Compute(listens, period, now, back, firstDay, top: 1000);
            Assert.True(s.From <= s.To);
            Assert.True(s.From.TimeOfDay == TimeSpan.Zero && s.To.TimeOfDay == TimeSpan.Zero);
            var inRange = listens.Where(Stats.Plausible).Where(l => l.Start.DateTime >= s.From && l.Start.DateTime < s.To).ToList();
            Assert.Equal(inRange.Count, s.Plays);
            var total = TimeSpan.FromSeconds(inRange.Sum(l => Math.Clamp(l.Listened, 0, 10_000_000)));
            Assert.True((s.Total - total).Duration() < TimeSpan.FromMilliseconds(1));
            var slack = TimeSpan.FromMilliseconds(1);
            Assert.True(s.TopTracks.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) <= s.Total + slack);
            Assert.True(s.TopArtists.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) <= s.Total + slack);
            Assert.True(s.TopAlbums.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) <= s.Total + slack);
            Assert.True((s.TopTracks.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) - s.Total).Duration() < slack, "全部的歌加起來要等於總時間");
            Assert.Equal(s.Plays, s.TopTracks.Sum(x => x.Plays));
            Assert.True(s.TopTracks.Zip(s.TopTracks.Skip(1)).All(p => p.First.Time >= p.Second.Time));
            Assert.Equal(24, s.Hours.Length);
            Assert.True((s.Hours.Aggregate(TimeSpan.Zero, (t, x) => t + x) - s.Total).Duration() < slack);
            Assert.True(s.Daily.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) - s.Total < slack && s.Total - s.Daily.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) < slack);
            Assert.Equal(s.Daily.Select(d => d.Day), Enumerable.Range(0, s.Daily.Count).Select(i => s.From.Date.AddDays(i)));
            Assert.Equal((int)(s.To - s.From.Date).TotalDays, s.Daily.Count);
            Assert.InRange(s.Streak, 0, Math.Max(0, listens.Count));
            if (back > 0 && period != StatsPeriod.All) Assert.Equal(0, s.Streak);

            var bars = Stats.Bars(s.Daily, period);
            var filled = Stats.Fill(bars, period);
            if (period != StatsPeriod.All && bars.Count > 0)
            {
                int want = period switch { StatsPeriod.Week => 7, StatsPeriod.Month => DateTime.DaysInMonth(s.From.Year, s.From.Month), _ => 12 };
                Assert.Equal(want, filled.Count);
                Assert.True(filled.Zip(filled.Skip(1)).All(p => p.First.Start < p.Second.Start));
            }
            Assert.True((bars.Aggregate(TimeSpan.Zero, (t, x) => t + x.Time) - s.Total).Duration() < slack);

            Stats.Label(period, s.From, CultureInfo.GetCultureInfo(Cultures[(int)(now.Ticks % Cultures.Length)]));
            Assert.False(Stats.CanGoBack(StatsPeriod.All, now, firstDay, earliest, back));
            Stats.CanGoBack(period, now, firstDay, earliest == default ? null : earliest, back * 100_000);
            Stats.CanGoForward(period, back);
        }, v => $"now={v.Item2:O} period={v.Item3} firstDay={v.Item4} back={v.Item5} listens={string.Join(" | ", v.Item1.Select(l => $"{l.Start:O} {l.Title}/{l.Artist}/{l.Album} {l.Listened}"))}", perCallMs: 300);
    }

    private static readonly string[] Cultures = ["en-US", "zh-TW", "zh-CN", "ja-JP", "ko-KR", "es-ES", "fr-FR", "de-DE", "pt-BR", "it-IT", "ru-RU", "ar-SA", "th-TH", "fa-IR", "he-IL"];

    [Fact]
    public void Stats_Heat_CoversTheWholeYear()
    {
        Fuzz.Run("Stats.Heat／HeatCutoffs", 7003, 600, r =>
        {
            int year = r.Pick([2000, 2023, 2024, 2025, 2026, 2100]);
            var daily = Enumerable.Range(0, r.Next(0, 60)).Select(_ => (new DateTime(year + r.Next(-1, 2), 1, 1).AddDays(r.Next(0, 366)).AddHours(r.Next(0, 24)), TimeSpan.FromMinutes(r.Pick([0, 1, 5, 30, 120, 600])))).ToList();
            return (daily, year, r.Pick(Enum.GetValues<DayOfWeek>()), new DateTime(year, 1, 1).AddDays(r.Next(-5, 380)));
        }, v =>
        {
            var (daily, year, firstDay, today) = v;
            var heat = Stats.Heat(daily, year, firstDay, today);
            int cells = 0;
            for (int w = 0; w < heat.Weeks; w++)
                for (int d = 0; d < 7; d++)
                    if (heat.Cells[w, d] is { } cell)
                    {
                        cells++;
                        Assert.Equal(year, cell.Day.Year);
                        Assert.InRange(cell.Level, 0, 4);
                        Assert.Equal(cell.Day > today.Date, cell.Future);
                        if (cell.Future || cell.Time == TimeSpan.Zero) Assert.Equal(0, cell.Level);
                        Assert.Equal(((int)cell.Day.DayOfWeek - (int)firstDay + 7) % 7, d);
                    }
            Assert.Equal(DateTime.IsLeapYear(year) ? 366 : 365, cells);
            Assert.Equal(12, heat.MonthWeeks.Count);
            Assert.All(heat.MonthWeeks, m => Assert.InRange(m.Week, 0, heat.Weeks - 1));
            var cutoffs = Stats.HeatCutoffs(daily.Select(d => d.Item2));
            Assert.True(cutoffs.Zip(cutoffs.Skip(1)).All(p => p.First <= p.Second));
        }, v => $"year={v.Item2} first={v.Item3} today={v.Item4:O} daily={string.Join(" ", v.Item1.Select(d => d.Item1.ToString("yyyy-MM-dd") + "/" + d.Item2.TotalMinutes))}");
    }

    private static readonly string[] XmlParts =
    [
        """<lfm status="ok"><nowplaying/></lfm>""", """<lfm status="failed"><error code="9">Invalid session key</error></lfm>""", """<lfm status="failed"><error code="x">bad</error></lfm>""", """<lfm status="failed"><error>no code</error></lfm>""",
        """<lfm status="failed"/>""", """<lfm/>""", """<html><body>502 Bad Gateway</body></html>""", """<lfm status="failed"><error code="99999999999999999999">big</error></lfm>""", """<lfm status="OK"/>""",
        """<?xml version="1.0"?><lfm status="ok"/>""", """<!DOCTYPE lfm [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;"><!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">]><lfm status="ok">&c;</lfm>""",
        """<!DOCTYPE x SYSTEM "http://127.0.0.1:1/x.dtd"><lfm status="ok"/>""", "<lfm", "</lfm>", "<", "&", "\u0000", "", " ", "\uFEFF", "not xml", """<lfm status="ok">""",
    ];

    [Fact]
    public void LastFm_Parse_NeverThrows_OnBrokenXml()
    {
        Fuzz.Run("LastFm.Parse", 7004, 1500, r => (r.Chance(0.5) ? r.Mangle(r.Pick(XmlParts)) : r.Build(XmlParts.Concat(Gen.Noise).ToArray(), 0, 3), r.Pick([0, 200, 403, 500, 502, -1])), v =>
        {
            var reply = LastFm.Parse(v.Item1, v.Item2);
            Assert.Equal(reply.Code == 0, reply.Ok);
            if (reply.Ok) Assert.NotNull(reply.Body);
            if (reply.Code == -1) Assert.Null(reply.Body);
            Assert.NotNull(reply.Message);
            Assert.Equal(reply.Code is -1 or 11 or 16, reply.Retryable);
        }, v => $"{Fuzz.Escape(v.Item1)} http={v.Item2}", perCallMs: 500);
    }

    [Fact]
    public void LastFmApi_Sign_IgnoresOrder_AndTheExcludedParameters()
    {
        Fuzz.Run("LastFmApi.Sign", 7005, 4000, r =>
        {
            var names = new[] { "artist", "track", "album", "timestamp", "method", "api_key", "sk", "artist[10]", "artist[1]", "track[0]", "api_sig", "format", "callback", "中文", "" };
            var pairs = Enumerable.Range(0, r.Next(0, 8)).Select(_ => KeyValuePair.Create(r.Pick(names) + (r.Chance(0.2) ? "x" : ""), r.Pick(["", "a b", "中文", "😀", "a&b=c", "\uD83D", "123", "%20", "é"]))).GroupBy(p => p.Key).Select(g => g.First()).ToList();
            return (pairs, r.Pick(["secret", "", "中文", "\uD83D"]), r.Next());
        }, v =>
        {
            var (pairs, secret, seed) = v;
            var sig = LastFmApi.Sign(pairs, secret);
            Assert.Matches("^[0-9a-f]{32}$", sig);
            var shuffled = pairs.OrderBy(_ => new Random(seed).Next()).Reverse().ToList();
            Assert.Equal(sig, LastFmApi.Sign(shuffled, secret));
            Assert.Equal(sig, LastFmApi.Sign(pairs.Concat(new[] { KeyValuePair.Create("api_sig", "zzz"), KeyValuePair.Create("format", "json"), KeyValuePair.Create("callback", "cb") }.Where(p => !pairs.Any(q => q.Key == p.Key))), secret));
        }, v => $"{string.Join("&", v.Item1.Select(p => p.Key + "=" + p.Value))} secret={v.Item2}");
    }

    // ---------------------------------------------------------------- 用量

    private static string UsageLine(Random r, bool huge)
    {
        string Num() => r.Pick<string>(huge ? ["0", "5", "-5", "1000", "9223372036854775807", "9223372036854775808", "1e30", "1.5", "\"7\"", "null", "true", "123456789012"] : ["0", "5", "-5", "1000", "1.5", "\"7\"", "null", "123456"]);
        string Str(string[] options) => r.Chance(0.1) ? "5" : r.Chance(0.05) ? "null" : "\"" + r.Pick(options) + "\"";
        string usage = "{\"input_tokens\":" + Num() + ",\"output_tokens\":" + Num() + ",\"cache_read_input_tokens\":" + Num() + ",\"cache_creation_input_tokens\":" + Num()
            + ",\"cache_creation\":{\"ephemeral_5m_input_tokens\":" + Num() + ",\"ephemeral_1h_input_tokens\":" + Num() + "},\"speed\":" + Str(["fast", "standard", ""]) + ",\"inference_geo\":" + Str(["us", "eu", "", "not_available"])
            + ",\"iterations\":[{\"type\":\"message\",\"model\":" + Str(["claude-opus-4-1", "x", ""]) + ",\"input_tokens\":" + Num() + ",\"output_tokens\":" + Num() + "},5,null]}";
        var model = Str(["claude-opus-4-5", "claude-sonnet-4-5", "claude-haiku-4-5", "<synthetic>", "", "gpt-x", "claude-opus-4-1"]);
        var id = Str(["msg_1", "msg_2", "msg_3", ""]);
        var ts = Str(["2026-11-24T10:00:00Z", "2026-11-24T10:00:00.123+08:00", "2026-11-24T23:59:59-12:00", "0001-01-01T00:00:00Z", "9999-12-31T23:59:59Z", "bad", "", "2026-11-24", "1999-01-01T00:00:00Z"]);
        string message = "{\"id\":" + id + ",\"model\":" + model + ",\"usage\":" + usage + "}";
        return r.Chance(0.1)
            ? r.Mangle("{\"type\":\"assistant\",\"message\":" + message + "}")
            : "{\"type\":\"" + r.Pick(["assistant", "assistant", "user", "assistant"]) + "\",\"timestamp\":" + ts + ",\"sessionId\":" + Str(["s1", "s2", ""]) + ",\"cwd\":" + Str(["/home/u/proj", "C:\\\\x\\\\y", "", "/"])
              + ",\"requestId\":" + Str(["r1", "r2"]) + ",\"isApiErrorMessage\":" + r.Pick(["false", "true", "5"]) + ",\"message\":" + message + "}";
    }

    [Fact]
    public void UsageLedger_Feed_Summarize_Trend_SurviveBrokenLines()
    {
        Fuzz.Run("UsageLedger.Feed／Summarize／Trend", 7006, 1500, r => (Enumerable.Range(0, r.Next(0, 10)).Select(_ => UsageLine(r, r.Chance(0.3))).ToList(), r.Chance(0.2), r.Next()), v =>
        {
            var (lines, huge, seed) = v;
            var ledger = new UsageLedger { Oldest = seed % 4 == 0 ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null };
            int fed = 0;
            foreach (var line in lines)
            {
                var bytes = Encoding.UTF8.GetBytes(line);
                if (seed % 5 == 0) bytes[(bytes.Length * 3 / 5) % Math.Max(1, bytes.Length)] = 0xFF;
                if (ledger.Feed(bytes, "file")) fed++;
                ledger.Feed(line.Substring(0, line.Length / 2));   // 讀到一半的行
            }
            Assert.True(ledger.Count <= fed + 0 || fed == 0 || ledger.Count <= lines.Count);
            var zone = seed % 2 == 0 ? TimeZoneInfo.Utc : TimeZoneInfo.CreateCustomTimeZone("x", TimeSpan.FromHours(14), "x", "x");
            var day = new DateOnly(2026, 11, 24);
            var summary = ledger.Summarize(day, zone);
            var trend = ledger.Trend(day, 7, zone);
            Assert.Equal(7, trend.Days.Count);
            Assert.True(summary.Cost >= 0 && trend.Cost >= 0, "費用是負的");
            Assert.All(summary.Models, m => Assert.True(m.Cost is null or >= 0));
            ledger.Forget(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
            ledger.Trim(new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc));
            ledger.Summarize(day, zone);
            ledger.Clear();
            Assert.Equal(0, ledger.Count);
        }, v => string.Join("\n", v.Item1), perCallMs: 500);
    }

    [Fact]
    public void ListenTracker_NeverReportsMoreThanWasHeard()
    {
        Fuzz.Run("ListenTracker.Observe／Flush", 7007, 2500, r => r.Next(), seed =>
        {
            var r = new Random(seed);
            var tracker = new ListenTracker();
            var now = new DateTime(2026, 11, 24, 12, 0, 0);
            var clips = new List<Listen>();
            for (int step = 0; step < 60; step++)
            {
                now = now.AddMilliseconds(r.Pick([0, 100, 500, 1000, 1000, 2000, 5000, 60000, -300]));
                Track? track = r.Chance(0.1) ? null : new Track("roon", "app", r.Pick(["A", "B", " ", ""]), r.Pick(["x", "y"]), "", null, r.Chance(0.8), TimeSpan.FromSeconds(r.Pick([0, 5, 100, 190, 199, 400, -3])), r.Chance(0.9) ? new DateTimeOffset(now) : default, TimeSpan.FromSeconds(r.Pick([0, 200, 200, 300, -1])));
                if (tracker.Observe(track, now, out _) is { } done) clips.Add(done);
                Assert.True(tracker.Listened >= 0);
            }
            if (tracker.Flush() is { } last) clips.Add(last);
            foreach (var c in clips)
            {
                Assert.True(c.Listened >= 0);
                if (c.Duration > 0) Assert.True(c.Listened <= c.Duration + 0.05, $"聽了 {c.Listened} 秒但歌只有 {c.Duration} 秒");
                Assert.True(ListenTracker.Counts(c.Listened, c.Duration));
                Assert.NotEqual("", c.Title.Trim());
            }
        }, seed => $"seed={seed}");
    }
}

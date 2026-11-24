// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>提醒：使用者與 AI 打的時間、重複規則、月曆上的圓點、存檔。</summary>
public class FuzzAlarmsTests
{
    private static readonly string[] LineParts =
    [
        "15:30", "7:05", "07:30", "0:00", "23:59", "24:00", "7:60", "99:99", "7點半", "7點", "7點05分", "7点", "７點", "下午3點", "晚上10點", "凌晨3點", "中午12點", "早上", "上午", "傍晚", "3pm", "3 PM", "12am", "12pm", "13pm", "am", "pm",
        "明天", "後天", "今天", "tomorrow", "today", "20分鐘", "20 分鐘", "20分钟後", "20m", "20min", "1h", "1.5小時", "2hours", "0分鐘", "0.001分", "0.01m", "-5分", "99999999999999999999分", "1e5m", "1.5.5m", "分鐘後", "後", "后天",
        "每天", "每日", "天天", "平日", "工作日", "週間", "每週五", "每周一", "每星期日", "每禮拜三", "每週一到五", "every day", "every friday", "everyfri", "weekdays", "weekday", "daily", "every sat", "每週", "每",
        "2026-11-24", "2026/11/28", "2026-11-28 07:30", "2026-11-28T07:30:00", "2026-2-30", "0000-01-01", "0001-01-01 00:00", "9999-12-31", "9999-12-31 23:59", "2026-13-01", "2026-00-10", "2026-11-31", "2026-11-8 7:30",
        "٢٠٢٦-١١-٢٨", "２０２６－１１－２８", "２０２６/１１/２８", "2026-11-28 7點半", "2026-11-28 下午3點", "2026-11-28 09:00",
        " ", "  ", "吃藥", "開會", "關火", "\uD83D", "😀", ":", "-", "/", ".",
    ];

    private static DateTime RandomNow(Random r)
    {
        var now = new DateTime(2026, 1, 1).AddMinutes(r.Next(0, 2 * 365 * 24 * 60));
        return r.Chance(0.1) ? new DateTime(2026, 11, 24, 23, 59, 30) : r.Chance(0.1) ? new DateTime(2026, 12, 31, 23, 30, 0) : r.Chance(0.1) ? now.Date : now;
    }

    private static string LineInput(Random r)
    {
        var text = r.Build(LineParts.Concat(Gen.Noise).ToArray(), 0, 6, 0.15);
        return r.Chance(0.4) ? r.Mangle(text) : text;
    }

    [Fact]
    public void ParseLine_Succeeds_Only_WithATimeInTheFutureWithinTheLimit()
    {
        Fuzz.Run("Alarms.ParseLine", 4001, 6000, r => (LineInput(r), RandomNow(r)), v =>
        {
            var (line, now) = v;
            if (Alarms.ParseLine(line, now) is not { } parsed) return;
            Assert.True(parsed.At > now, $"時間不在現在之後：{parsed.At:O}（現在 {now:O}）");
            Assert.True(parsed.At - now <= Alarms.Farthest, "超過上限");
            Assert.Equal(parsed.Text.Trim(), parsed.Text);
            Assert.Equal(parsed.Repeat, Alarms.NormalizeRepeat(parsed.Repeat));
            if (parsed.Repeat != null)
            {
                Assert.True(Alarms.Matches(parsed.Repeat, parsed.At.DayOfWeek), "不符合重複規則");
                Assert.True(parsed.At - now <= TimeSpan.FromDays(8));
            }
            Alarms.Describe(new Alarm(1, parsed.At, parsed.Text, parsed.Repeat), now);
            Alarms.Short(parsed.At, now, "明天");
        }, v => $"line={Fuzz.Escape(v.Item1)} now={v.Item2:O}", perCallMs: 200);
    }

    [Fact]
    public void When_NeverThrows_And_StaysInTheFutureWithinTheLimit()
    {
        double?[] minutes = [null, 0, -1, 0.0001, 0.001, 0.0083, 0.5, 1, 90, 60 * 24 * 60, 60 * 24 * 60 + 1, 1e300, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon];
        Fuzz.Run("Alarms.When", 4002, 5000, r => (r.Chance(0.2) ? null : LineInput(r), r.Pick(minutes), RandomNow(r)), v =>
        {
            var (at, min, now) = v;
            if (Alarms.When(at, min, now) is not { } when) return;
            Assert.True(when > now, $"不在現在之後：{when:O}");
            Assert.True(when - now <= Alarms.Farthest);
        }, v => $"at={Fuzz.Escape(v.Item1 ?? "null")} minutes={v.Item2} now={v.Item3:O}");
    }

    private static readonly string[] Repeats = ["daily", "weekdays", "sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    [Fact]
    public void NextAfter_IsStrictlyLater_AndFollowsTheRepeatRule()
    {
        Fuzz.Run("Alarms.NextAfter／NormalizeRepeat", 4003, 8000, r =>
        {
            var at = new DateTime(2000, 1, 1).AddMinutes(r.Next(0, 31 * 365 * 24 * 60)).AddSeconds(r.Next(0, 60)).AddTicks(r.Chance(0.3) ? r.Next(1, 9999999) : 0);
            return (at, r.Pick(Repeats), RandomNow(r).AddSeconds(r.Next(0, 60)).AddTicks(r.Chance(0.3) ? r.Next(0, 9999999) : 0));
        }, v =>
        {
            var (at, repeat, now) = v;
            var next = Alarms.NextAfter(at, repeat, now);
            Assert.True(next > now, $"不是嚴格晚於：{next:O}");
            Assert.True(next - now <= TimeSpan.FromDays(8), "隔太久");
            Assert.Equal(at.TimeOfDay, next.TimeOfDay);
            Assert.True(Alarms.Matches(repeat, next.DayOfWeek), "不符合重複規則");
            // 中間沒有漏掉更早的符合日：往前一天（同一時刻）要嘛不符合、要嘛不在 now 之後
            var earlier = next.AddDays(-1);
            Assert.True(earlier <= now || !Alarms.Matches(repeat, earlier.DayOfWeek), $"漏掉了更早的一次：{earlier:O}");
            Assert.Equal(next, Alarms.NextAfter(at.AddDays(-7), repeat, now));   // 起點往前推整週，結果不變
            Assert.Equal(Alarms.NormalizeRepeat(repeat.ToUpperInvariant() + " "), repeat);
        }, v => $"at={v.Item1:O} repeat={v.Item2} now={v.Item3:O}");
    }

    [Fact]
    public void NormalizeRepeat_IsIdempotent_AndOnlyKnowsTheListedNames()
    {
        Fuzz.Run("Alarms.NormalizeRepeat 任意字串", 4004, 3000, r => r.Chance(0.3) ? null : r.Mangle(r.Build(Repeats.Concat(Gen.Noise).Concat(["DAILY", " Mon ", "monday", "weekday", "Fri\n"]).ToArray(), 0, 3, 0.2)), s =>
        {
            var n = Alarms.NormalizeRepeat(s);
            if (n != null) Assert.Contains(n, Repeats);
            Assert.Equal(n, Alarms.NormalizeRepeat(n));
            Assert.Equal(n != null && n is not ("daily" or "weekdays"), Alarms.WeeklyDay(s) != null);
        }, s => Fuzz.Escape(s ?? "null"));
    }

    private static Alarm RandomAlarm(Random r, DateTime now)
    {
        string? repeat = r.Chance(0.5) ? r.Pick(Repeats) : r.Chance(0.1) ? "bogus" : null;
        var at = now.AddMinutes(r.Next(-3 * 24 * 60, 70 * 24 * 60));
        if (repeat != null && Alarms.NormalizeRepeat(repeat) is { } valid && at > now) at = Alarms.NextAfter(at, valid, at.AddDays(-1) < now ? now : at.AddDays(-1));
        return new Alarm(r.Next(1, 99), at, r.Pick(new[] { "吃藥", "開會", "x", "" }), repeat);
    }

    [Fact]
    public void InMonth_MatchesADayByDayCount()
    {
        Fuzz.Run("Alarms.InMonth", 4005, 2500, r =>
        {
            var now = RandomNow(r);
            var alarms = Enumerable.Range(0, r.Next(0, 5)).Select(_ => RandomAlarm(r, now)).ToList();
            // 畫面只畫「現在這個月」（IslandWindow.Tools 傳 DateTime.Today）；更遠的月份 InMonth 最多只往後推四十次，不在這裡測
            var month = r.Chance(0.2) ? now.Date.AddMonths(-1) : now.Date;
            return (alarms, month, now);
        }, v =>
        {
            var (alarms, month, now) = v;
            var days = Alarms.InMonth(alarms, month, now);
            var first = new DateTime(month.Year, month.Month, 1);
            foreach (var (day, list) in days)
            {
                Assert.InRange(day, 1, DateTime.DaysInMonth(first.Year, first.Month));
                Assert.NotEmpty(list);
                for (int i = 0; i < list.Count; i++)
                {
                    Assert.Equal(first.AddDays(day - 1), list[i].At.Date);
                    Assert.True(list[i].At > now);
                    if (i > 0) Assert.True(list[i - 1].At <= list[i].At);
                }
            }
            // 對照：一天一天數
            var expected = new SortedDictionary<int, int>();
            foreach (var a in alarms)
            {
                if (Alarms.NormalizeRepeat(a.Repeat) is not { } repeat) { if (a.At > now && a.At.Year == first.Year && a.At.Month == first.Month) expected[a.At.Day] = expected.GetValueOrDefault(a.At.Day) + 1; continue; }
                var start = a.At > now ? a.At : Alarms.NextAfter(a.At, repeat, now);
                for (var d = first; d.Month == first.Month; d = d.AddDays(1))
                {
                    var at = d + a.At.TimeOfDay;
                    bool counts = a.At > now && at == a.At || at >= start && at > now && Alarms.Matches(repeat, d.DayOfWeek);
                    if (counts) expected[d.Day] = expected.GetValueOrDefault(d.Day) + 1;
                }
            }
            Assert.Equal(expected.Select(p => $"{p.Key}:{p.Value}"), days.Select(p => $"{p.Key}:{p.Value.Count}"));
        }, v => $"month={v.Item2:yyyy-MM} now={v.Item3:O} alarms={string.Join(" | ", v.Item1.Select(a => $"{a.At:O} {a.Repeat}"))}");
    }

    private static readonly string[] JsonParts =
    [
        """[{"Id":1,"At":"2026-11-24T07:30:00","Text":"吃藥","Repeat":"daily"}]""", """[{"Id":2,"At":"2026-11-24T07:30:00","Text":"x"}]""", """[{"Id":"x","At":5,"Text":null}]""",
        """[{"At":"not a date","Text":"x"}]""", """[null]""", """[]""", """{}""", "null", "5", "[", "{", """[{"Text":"a\ud83d"}]""", """[{"Id":99999999999,"At":"0001-01-01T00:00:00","Text":"a"}]""", """[{"Text":"a","Repeat":5}]""",
        """[{"Id":1,"At":"9999-12-31T23:59:59.9999999","Text":"t","Repeat":"daily"}]""",
    ];

    [Fact]
    public void Parse_And_Serialize_AreSafe_AndRoundTrip()
    {
        Fuzz.Run("Alarms.Parse／Serialize", 4006, 3000, r => r.Mangle(r.Pick(JsonParts)), json =>
        {
            var list = Alarms.Parse(json);
            Assert.All(list, a => Assert.NotNull(a.Text));
            var back = Alarms.Parse(Alarms.Serialize(list));
            Assert.Equal(list.OrderBy(a => a.At).Select(a => (a.Id, a.At, a.Text, a.Repeat)), back.Select(a => (a.Id, a.At, a.Text, a.Repeat)));
        }, Fuzz.Escape);
    }

    [Fact]
    public void Check_RemovesOneShots_AndReschedulesRepeatsIntoTheFuture()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shima-fuzz-alarms-" + Guid.NewGuid().ToString("N"));
        try
        {
            Fuzz.Run("Alarms.Add／Check／Cancel", 4007, 60, r =>
            {
                var now = RandomNow(r);
                return (Enumerable.Range(0, r.Next(0, 6)).Select(_ => RandomAlarm(r, now)).ToList(), now, now.AddMinutes(r.Next(0, 5 * 24 * 60)).AddSeconds(r.Next(0, 60)));
            }, v =>
            {
                var (alarms, added, checkAt) = v;
                using var store = new Alarms(Path.Combine(folder, "alarms.json"));
                foreach (var a in store.List.ToList()) store.Cancel("#" + a.Id);
                foreach (var a in alarms) store.Add(a.At, a.Text, a.Repeat);
                var due = new List<Alarm>();
                store.Due += (alarm, late) => due.Add(alarm);
                var fired = store.Check(checkAt);
                Assert.Equal(fired.Count, due.Count);
                Assert.All(fired, a => Assert.True(a.At <= checkAt));
                foreach (var left in store.List)
                {
                    if (Alarms.NormalizeRepeat(left.Repeat) != null) Assert.True(left.At > checkAt, "重複的提醒要排到現在之後");
                    else Assert.True(left.At > checkAt, "單次到點的要拿掉");
                }
                Assert.Equal(alarms.Count - fired.Count(f => Alarms.NormalizeRepeat(f.Repeat) == null), store.List.Count);
                Assert.Empty(store.Check(checkAt));   // 同一個時間再查一次，不會再響
            }, v => $"alarms={string.Join(" | ", v.Item1.Select(a => $"{a.At:O} {a.Repeat}"))} check={v.Item3:O}");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}

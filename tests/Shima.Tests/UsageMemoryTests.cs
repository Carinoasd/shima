// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using Shima.Agents;
using Xunit;
using Xunit.Abstractions;

namespace Shima.Tests;

public class UsageMemoryTests(ITestOutputHelper output)
{
    /// <summary>像 Claude Code 的 assistant 紀錄：content 夾著一大段檔案內容。</summary>
    private static byte[] Big(string id, int size) => Encoding.UTF8.GetBytes(
        "{\"type\":\"assistant\",\"sessionId\":\"s1\",\"timestamp\":\"2026-05-14T03:00:00Z\",\"message\":{\"id\":\"" + id
        + "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + new string('x', size)
        + "\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":1}}}");

    [Fact]
    public void 大行不再整行複製一份()
    {
        var lines = Enumerable.Range(0, 20).Select(i => Big("m" + i, 500_000)).ToList();
        var ledger = new UsageLedger();
        foreach (var line in lines.Take(3)) ledger.Feed(line);   // 先暖身：池子借到夠大的緩衝區
        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var line in lines) ledger.Feed(line);
        long used = GC.GetAllocatedBytesForCurrentThread() - before;
        long total = lines.Sum(l => (long)l.Length);
        output.WriteLine($"餵了 {total:N0} bytes，配置了 {used:N0} bytes");
        Assert.True(used < total / 10, $"配置了 {used:N0} bytes（行的總量 {total:N0}）");
        Assert.Equal(20, ledger.Count);
    }

    private const string Ok = """{"type":"assistant","sessionId":"s","timestamp":"2026-05-14T03:00:00Z","message":{"id":"m","model":"claude-opus-5-5","usage":{"input_tokens":5,"output_tokens":1}}}""";

    [Fact]
    public void 一行後面多出東西照樣不收_空白可以()
    {
        Assert.False(new UsageLedger().Feed(Ok + " junk"));
        Assert.False(new UsageLedger().Feed(Ok + "{}"));
        Assert.True(new UsageLedger().Feed(Ok + "  \r"));
    }

    private static string Of(string id, string session, string cwd, string time) =>
        "{\"type\":\"assistant\",\"sessionId\":\"" + session + "\",\"cwd\":\"" + cwd + "\",\"timestamp\":\"" + time
        + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":5,\"output_tokens\":1}}}";

    private static HashSet<string> Pool(UsageLedger ledger) => (HashSet<string>)typeof(UsageLedger).GetField("_strings", Flags)!.GetValue(ledger)!;

    [Fact]
    public void 工作階段與專案名稱不進程序共用的池子_同樣的值共用同一個物件()
    {
        var session = Guid.NewGuid().ToString();
        var project = "p-" + Guid.NewGuid().ToString("N");
        var other = Guid.NewGuid().ToString();
        var ledger = new UsageLedger();
        ledger.Feed(Of("a", session, "/x/" + project, "2026-05-14T03:00:00Z"));
        ledger.Feed(Of("b", other, "/y/" + project, "2026-05-14T04:00:00Z"));
        Assert.Null(string.IsInterned(session));
        Assert.Null(string.IsInterned(project));
        var sessions = ledger.Summarize(new DateOnly(2026, 5, 14), TimeZoneInfo.Utc).Sessions;
        Assert.Equal(2, sessions.Count);
        Assert.Same(sessions[0].Project, sessions[1].Project);   // 兩行各解析出一份，帳本裡只留一份
    }

    [Fact]
    public void 帳本清掉池子跟著清_Trim_與_Forget_只留還在用的()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Of("old", "s-old", "/x/old", "2026-04-01T03:00:00Z"));
        ledger.Feed(Of("new", "s-new", "/x/new", "2026-05-14T03:00:00Z"));
        Assert.Contains("s-old", Pool(ledger));
        ledger.Trim(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(["claude-opus-5-5", "new", "s-new"], Pool(ledger).Order(StringComparer.Ordinal));
        ledger.Forget(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(0, ledger.Count);
        Assert.Empty(Pool(ledger));
        ledger.Feed(Of("again", "s", "/x/p", "2026-05-14T03:00:00Z"));
        Assert.NotEmpty(Pool(ledger));
        ledger.Clear();
        Assert.Empty(Pool(ledger));
    }

    [Fact]
    public async Task 先問今天再問趨勢只列一次目錄()
    {
        var root = Path.Combine(Path.GetTempPath(), "shima-usage-mem-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, ".claude");
            var dir = Path.Combine(folder, "projects", "p");
            Directory.CreateDirectory(dir);
            var now = DateTime.Today.AddHours(12);
            string Stamp(DateTime local) => local.ToUniversalTime().ToString("o");
            var today = Path.Combine(dir, "today.jsonl");
            var older = Path.Combine(dir, "older.jsonl");
            File.WriteAllText(today, Of("t", "s1", "/x/p", Stamp(now.AddMinutes(-5))) + "\n");
            File.WriteAllText(older, Of("o", "s2", "/x/p", Stamp(now.AddDays(-5))) + "\n");
            File.SetLastWriteTime(today, now.AddMinutes(-5));
            File.SetLastWriteTime(older, now.AddDays(-5));
            var usage = new AgentUsage(() => [folder], () => now);
            var field = typeof(AgentUsage).GetField("_listing", Flags)!;

            Assert.Equal(6, (await usage.TodayAsync()).Tokens.Total);   // 今天只算今天改過的那個檔
            var first = field.GetValue(usage);
            Assert.Equal(12, (await usage.TrendAsync()).Tokens.Total);   // 趨勢也有五天前的那個檔
            Assert.Same(first, field.GetValue(usage));
        }
        finally { Directory.Delete(root, true); }
    }

    private static object EntryExtra(UsageLedger ledger)
    {
        var entries = (System.Collections.IDictionary)typeof(UsageLedger).GetField("_entries", Flags)!.GetValue(ledger)!;
        var entry = entries.Values.Cast<object>().Single();
        return entry.GetType().GetField("Extra")!.GetValue(entry)!;
    }

    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    [Fact]
    public void 沒有別的模型的_iterations_不另外配置清單()
    {
        var empty = new UsageLedger();
        empty.Feed(Ok);
        var shared = EntryExtra(empty);
        var ledger = new UsageLedger();
        ledger.Feed(Ok.Replace("\"output_tokens\":1", "\"output_tokens\":1,\"iterations\":[]"));
        ledger.Feed(Ok.Replace("\"output_tokens\":1", "\"output_tokens\":1,\"iterations\":[{\"model\":\"claude-opus-5-5\",\"input_tokens\":5}]"));
        Assert.Same(shared, EntryExtra(ledger));
    }
}

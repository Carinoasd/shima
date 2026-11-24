// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

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

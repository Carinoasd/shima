// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>「以後都允許」規則的檢視與刪除：只碰暫存資料夾。</summary>
public sealed class AgentRuleListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shima-rules-" + Guid.NewGuid().ToString("N"));
    private string Rules => Path.Combine(_root, "agent-rules.json");
    private const string Project = "/home/someone/proj";

    public AgentRuleListTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static JsonNode Bash(string command) => new JsonObject { ["command"] = command };

    private AgentRules Filled()
    {
        var rules = new AgentRules(Rules);
        Assert.True(rules.Add(new AgentRule("Bash", "git status", Project)));
        Assert.True(rules.Add(new AgentRule("Bash", "npm test", Project)));
        Assert.True(rules.Add(new AgentRule("Edit", null, Project)));
        return rules;
    }

    [Fact]
    public void 刪一筆之後重新讀檔也不見了_其他的還在()
    {
        var rules = Filled();
        Assert.True(rules.Remove(new AgentRule("Bash", "npm test", Project)));
        Assert.Equal([new AgentRule("Bash", "git status", Project), new AgentRule("Edit", null, Project)], new AgentRules(Rules).List());
        Assert.False(rules.Remove(new AgentRule("Bash", "npm test", Project)));
    }

    [Fact]
    public void 刪掉之後同樣的請求不再自動允許()
    {
        var rules = Filled();
        Assert.NotNull(rules.Match("Bash", Bash("git status"), Project));
        rules.Remove(new AgentRule("Bash", "git status", Project));
        Assert.Null(rules.Match("Bash", Bash("git status"), Project));
        Assert.Null(new AgentRules(Rules).Match("Bash", Bash("git status"), Project));
    }

    [Fact]
    public void 舊版寫法的規則也刪得掉()
    {
        File.WriteAllText(Rules, $$"""[ { "tool": "Bash", "command": "Git.exe status", "cwd": "{{Project}}" } ]""");
        var rules = new AgentRules(Rules);
        Assert.Equal([new AgentRule("Bash", "git status", Project)], rules.List());
        Assert.True(rules.Remove(new AgentRule("Bash", "Git.exe status", Project)));
        Assert.Empty(new AgentRules(Rules).List());
    }

    [Fact]
    public void 全部清除_檔案留著但是空的_不留暫存檔()
    {
        var rules = Filled();
        Assert.True(rules.Clear());
        Assert.Empty(rules.List());
        Assert.Empty(new AgentRules(Rules).List());
        Assert.Equal("[]", File.ReadAllText(Rules).Trim());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
        Assert.Null(rules.Match("Edit", new JsonObject { ["file_path"] = Project + "/a.cs" }, Project));
    }

    [Fact]
    public void 沒有規則時清除什麼都不寫()
    {
        var rules = new AgentRules(Rules);
        Assert.False(rules.Clear());
        Assert.False(File.Exists(Rules));
    }

    [Fact]
    public void 有增減才通知()
    {
        var rules = new AgentRules(Rules);
        int changed = 0;
        rules.Changed += () => changed++;
        rules.Add(new AgentRule("Bash", "git status", Project));
        rules.Add(new AgentRule("Bash", "git status", Project));   // 重複的不算
        rules.Add(new AgentRule("Bash", "bash", Project));         // 不給記的不算
        Assert.Equal(1, changed);
        rules.Remove(new AgentRule("Bash", "git log", Project));   // 沒有這條
        rules.Remove(new AgentRule("Bash", "git status", Project));
        rules.Clear();                                             // 已經空了
        Assert.Equal(2, changed);
    }

    [Fact]
    public void 存不了檔就不算刪掉_記憶體也不留假的結果()
    {
        var rules = Filled();
        // 把規則檔換成資料夾：之後的 File.Move 一定失敗
        File.Delete(Rules);
        Directory.CreateDirectory(Rules);
        try
        {
            Assert.False(rules.Remove(new AgentRule("Bash", "git status", Project)));
            Assert.False(rules.Clear());
        }
        finally { Directory.Delete(Rules, true); }
    }

    [Theory]
    [InlineData(@"C:\work\app", 30, @"C:\work\app")]
    [InlineData(@"C:\Users\someone\source\repos\very-long-project-name", 30, @"…\repos\very-long-project-name")]
    [InlineData("/home/someone/projects/shima-wt/agents", 20, "…/shima-wt/agents")]
    [InlineData("/x/abcdefghijklmnopqrstuvwxyz", 10, "…rstuvwxyz")]
    public void 太長的路徑留後面(string path, int max, string shown)
    {
        Assert.Equal(shown, PathText.Tail(path, max));
        Assert.True(shown.Length <= max);
    }
}

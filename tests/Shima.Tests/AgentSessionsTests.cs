// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class AgentSessionsTests
{
    private DateTime _now = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    private static JsonObject Hook(string session, string name, string cwd = @"C:\Users\someone\src\alpha", string? extra = null)
    {
        var o = extra != null ? (JsonObject)JsonNode.Parse(extra)! : [];
        o["session_id"] = session;
        o["hook_event_name"] = name;
        o["cwd"] = cwd;
        o["transcript_path"] = $@"C:\Users\someone\.claude\projects\x\{session}.jsonl";
        return o;
    }

    private AgentSessions New() => new(() => _now);

    [Fact]
    public void 每個工作階段分開記_最近有動靜的在前面()
    {
        var s = New();
        Assert.True(s.Apply(Hook("a", "SessionStart", extra: """{"source":"startup"}""")));
        _now = _now.AddMinutes(1);
        s.Apply(Hook("b", "UserPromptSubmit", "/home/someone/beta/"));
        var list = s.Snapshot();
        Assert.Equal(["b", "a"], list.Select(x => x.Id));
        Assert.Equal("beta", list[0].Project);
        Assert.Equal(AgentState.Working, list[0].State);
        Assert.Equal(AgentState.NeedsInput, list[1].State);
        Assert.Equal(@"C:\Users\someone\src\alpha", list[1].Cwd);
        Assert.EndsWith("a.jsonl", list[1].TranscriptPath);
    }

    [Fact]
    public void 狀態跟著事件走()
    {
        var s = New();
        s.Apply(Hook("a", "UserPromptSubmit"));
        s.Apply(Hook("a", "PermissionRequest", extra: """{"tool_name":"Bash","tool_input":{"command":"ls"}}"""));
        Assert.Equal(AgentState.NeedsPermission, s.Find("a")!.State);
        Assert.True(s.Resumed("a"));
        Assert.Equal(AgentState.Working, s.Find("a")!.State);
        s.Apply(Hook("a", "PermissionRequest", extra: """{"tool_name":"AskUserQuestion","tool_input":{}}"""));
        Assert.Equal(AgentState.NeedsInput, s.Find("a")!.State);
        s.Apply(Hook("a", "PostToolUse", extra: """{"tool_name":"Read","tool_input":{"file_path":"x"}}"""));
        Assert.Equal(AgentState.Working, s.Find("a")!.State);
        s.Apply(Hook("a", "Stop"));
        Assert.Equal(AgentState.Done, s.Find("a")!.State);
        s.Apply(Hook("a", "Notification", extra: """{"notification_type":"idle_prompt"}"""));
        Assert.Equal(AgentState.Done, s.Find("a")!.State);   // 做完了之後的閒置提醒不改狀態
        s.Apply(Hook("a", "Notification", extra: """{"notification_type":"permission_prompt"}"""));
        Assert.Equal(AgentState.NeedsPermission, s.Find("a")!.State);
        s.Apply(Hook("a", "StopFailure"));
        Assert.Equal(AgentState.NeedsInput, s.Find("a")!.State);
    }

    [Fact]
    public void 壓縮對話不改狀態_清除對話待辦重來()
    {
        var s = New();
        s.Apply(Hook("a", "PostToolUse", extra: """{"tool_name":"TodoWrite","tool_input":{"todos":[{"content":"一","status":"in_progress"}]}}"""));
        s.Apply(Hook("a", "SessionStart", extra: """{"source":"compact"}"""));
        Assert.Equal((AgentState.Working, 1), (s.Find("a")!.State, s.Find("a")!.Total));
        s.Apply(Hook("a", "SessionStart", extra: """{"source":"clear"}"""));
        Assert.Equal((AgentState.NeedsInput, 0), (s.Find("a")!.State, s.Find("a")!.Total));
    }

    [Fact]
    public void 待辦進度每個工作階段一份()
    {
        var s = New();
        s.Apply(Hook("a", "PostToolUse", extra: """{"tool_name":"TodoWrite","tool_input":{"todos":[{"content":"讀","status":"completed"},{"content":"寫","status":"in_progress","activeForm":"正在寫"}]}}"""), out bool changed);
        Assert.True(changed);
        s.Apply(Hook("b", "PostToolUse", extra: """{"tool_name":"TaskCreate","tool_input":{"subject":"別的"}}"""));
        var a = s.Find("a")!;
        Assert.Equal((1, 2, "正在寫"), (a.Done, a.Total, a.Current));
        Assert.Equal(1, s.Find("b")!.Total);
        s.Apply(Hook("a", "PostToolUse", extra: """{"tool_name":"Bash","tool_input":{"command":"ls"}}"""), out changed);
        Assert.False(changed);
    }

    [Fact]
    public void 同一次工具呼叫送兩次只算一次()
    {
        var s = New();
        var hook = Hook("a", "PostToolUse", extra: """{"tool_name":"TaskCreate","tool_use_id":"toolu_1","tool_input":{"subject":"一"}}""");
        s.Apply(hook, out bool first);
        s.Apply((JsonObject)hook.DeepClone(), out bool second);
        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public void 結束或太久沒動靜就拿掉()
    {
        var s = New();
        s.Apply(Hook("a", "UserPromptSubmit"));
        s.Apply(Hook("b", "UserPromptSubmit"));
        Assert.True(s.Apply(Hook("a", "SessionEnd", extra: """{"reason":"prompt_input_exit"}""")));
        Assert.Null(s.Find("a"));
        Assert.False(s.Prune());
        _now = _now.AddMinutes(31);
        Assert.True(s.Prune());
        Assert.Equal(0, s.Count);
        Assert.False(s.Apply(Hook("c", "SessionEnd")));   // 沒看過的工作階段結束：什麼都不變
    }

    [Fact]
    public void 結束之後才到的背景_hook_不會讓它又冒出來()
    {
        var s = New();
        s.Apply(Hook("a", "UserPromptSubmit"));
        s.Apply(Hook("a", "SessionEnd"));
        Assert.False(s.Apply(Hook("a", "PostToolUse", extra: """{"tool_name":"Read","tool_input":{}}""")));
        Assert.Equal(0, s.Count);
        _now = _now.AddMinutes(2);
        Assert.True(s.Apply(Hook("a", "UserPromptSubmit")));   // 很久以後又有動靜（例如 resume）：照常記
    }

    [Fact]
    public void 沒有_session_id_的用資料夾當工作階段()
    {
        var s = New();
        var o = new JsonObject { ["hook_event_name"] = "Stop", ["cwd"] = "/home/someone/gamma" };
        s.Apply(o);
        Assert.Equal("gamma", s.Snapshot()[0].Project);
    }

    [Fact]
    public void 狀態沒變的事件不算有變()
    {
        var s = New();
        s.Apply(Hook("a", "UserPromptSubmit"));
        _now = _now.AddSeconds(5);
        Assert.False(s.Apply(Hook("a", "PostToolUse", extra: """{"tool_name":"Read","tool_input":{}}""")));
        Assert.Equal(_now, s.Find("a")!.LastActive);
    }
}

public class AgentRulesTests
{
    private static JsonNode Bash(string command) => new JsonObject { ["command"] = command, ["description"] = "x" };

    [Theory]
    [InlineData("git status", "git status")]
    [InlineData("git status --short", "git status")]
    [InlineData("dotnet test tests/A.csproj", "dotnet test")]
    [InlineData("ls -la", "ls")]
    [InlineData("ls", "ls")]
    [InlineData("cat -n a.txt", "cat")]
    [InlineData("cargo build --release", "cargo build")]
    [InlineData("git\tstatus", "git status")]
    [InlineData("  git   log  -3", "git log")]
    public void 建議的規則是指令的前一兩個字(string command, string label) =>
        Assert.Equal(label, AgentRules.Suggest("Bash", Bash(command), "/home/someone/p")!.Label);

    [Theory]
    [InlineData("git status; rm -rf ~")]
    [InlineData("git status && rm -rf ~")]
    [InlineData("git status & rm -rf ~")]
    [InlineData("git status || rm -rf ~")]
    [InlineData("git status | sh")]
    [InlineData("git status `rm -rf ~`")]
    [InlineData("git status $(rm -rf ~)")]
    [InlineData("git status ${IFS}x")]
    [InlineData("git status $HOME")]
    [InlineData("git status > ~/.bashrc")]
    [InlineData("git status >> x")]
    [InlineData("git status < /etc/passwd")]
    [InlineData("git status 2>&1")]
    [InlineData("git status\nrm -rf ~")]
    [InlineData("git status\rrm -rf ~")]
    [InlineData("git status <(rm x)")]
    [InlineData("git status (rm x)")]
    [InlineData("git status {a,b}")]
    [InlineData("git status \\\nrm")]
    [InlineData("git status 'x;y'")]
    [InlineData("git status \"x\"")]
    [InlineData("git status !!")]
    [InlineData("git status # 註解")]
    [InlineData("git status\u00A0x")]
    [InlineData("git status\u200Bx")]
    [InlineData("git status\u2028rm")]
    [InlineData("git status\0")]
    public void 能串接別的指令的寫法一律不比對(string command)
    {
        var rule = new AgentRule("Bash", "git status");
        Assert.False(AgentRules.Covers(rule, "Bash", Bash(command), null));
        Assert.Null(AgentRules.Suggest("Bash", Bash(command), null));
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("git status -s")]
    [InlineData("git  status  --porcelain")]
    public void 符合規則的指令(string command) =>
        Assert.True(AgentRules.Covers(new AgentRule("Bash", "git status"), "Bash", Bash(command), "/x"));

    [Theory]
    [InlineData("git statusx")]
    [InlineData("git status-evil")]
    [InlineData("git")]
    [InlineData("git push")]
    [InlineData("git -c core.pager=x status")]
    [InlineData("gitx status")]
    [InlineData("/usr/bin/git status")]
    [InlineData("./git status")]
    [InlineData("GIT status")]
    [InlineData("FOO=1 git status")]
    [InlineData("sudo git status")]
    [InlineData("env git status")]
    [InlineData("")]
    public void 字要整個一樣_前綴不算(string command) =>
        Assert.False(AgentRules.Covers(new AgentRule("Bash", "git status"), "Bash", Bash(command), "/x"));

    [Theory]
    [InlineData("sudo apt update")]
    [InlineData("bash -c ls")]
    [InlineData("sh x.sh")]
    [InlineData("python -c print")]
    [InlineData("node x.js")]
    [InlineData("xargs rm")]
    [InlineData("env ls")]
    [InlineData("eval ls")]
    [InlineData("git -C x status")]
    [InlineData("npm")]
    [InlineData("./build.sh")]
    [InlineData("C:/x/y.exe")]
    [InlineData("sed -n 1,20p a.cs")]
    [InlineData("rg --pre=sh TODO")]
    [InlineData("tar --to-command=sh -xf a.tar")]
    [InlineData("find . -delete")]
    [InlineData("vim -c q a.txt")]
    public void 後面接什麼都能執行的指令不給記(string command) =>
        Assert.Null(AgentRules.Suggest("Bash", Bash(command), "/x"));

    [Theory]
    [InlineData("sed", "sed e notes.txt")]
    [InlineData("rg", "rg --pre=sh x")]
    [InlineData("sudo", "sudo ls")]
    [InlineData("bash", "bash -c ls")]
    public void 手改進去的危險規則也不比對(string rule, string command) =>
        Assert.False(AgentRules.Covers(new AgentRule("Bash", rule), "Bash", Bash(command), null));

    [Fact]
    public void 跳出沙箱的指令照常問()
    {
        var input = new JsonObject { ["command"] = "git status", ["dangerouslyDisableSandbox"] = true };
        Assert.False(AgentRules.Covers(new AgentRule("Bash", "git status"), "Bash", input, null));
        input["dangerouslyDisableSandbox"] = "false";   // 型別不對也當成有
        Assert.False(AgentRules.Covers(new AgentRule("Bash", "git status"), "Bash", input, null));
        input["dangerouslyDisableSandbox"] = false;
        Assert.True(AgentRules.Covers(new AgentRule("Bash", "git status"), "Bash", input, null));
    }

    [Fact]
    public void 一個字的規則允許後面接任何參數_兩個字的規則不允許只有一個字()
    {
        Assert.True(AgentRules.Covers(new AgentRule("Bash", "ls"), "Bash", Bash("ls -la src"), null));
        Assert.False(AgentRules.Covers(new AgentRule("Bash", "ls src"), "Bash", Bash("ls"), null));
    }

    [Fact]
    public void 其他工具就是工具名稱_會執行指令或要人回答的不給記()
    {
        var edit = AgentRules.Suggest("Edit", new JsonObject { ["file_path"] = "a.cs" }, "/home/someone/p/")!;
        Assert.Equal(new AgentRule("Edit", null, "/home/someone/p"), edit);
        Assert.Equal("Edit", edit.Label);
        Assert.Null(AgentRules.Suggest("PowerShell", new JsonObject { ["command"] = "Get-ChildItem" }, "/x"));
        Assert.Null(AgentRules.Suggest("Monitor", new JsonObject { ["command"] = "tail -f x" }, "/x"));
        Assert.Null(AgentRules.Suggest("mcp__x__run", new JsonObject { ["command"] = "ls" }, "/x"));
        Assert.Null(AgentRules.Suggest("AskUserQuestion", new JsonObject(), "/x"));
        Assert.Null(AgentRules.Suggest("ExitPlanMode", new JsonObject(), "/x"));
        Assert.False(AgentRules.Covers(new AgentRule("Bash"), "Bash", Bash("ls"), null));                  // 整個 Bash 不行
        Assert.False(AgentRules.Covers(new AgentRule("AskUserQuestion"), "AskUserQuestion", new JsonObject(), null));
        Assert.False(AgentRules.Covers(new AgentRule("Edit", "x"), "Edit", new JsonObject(), null));        // 非 Bash 不該有指令
        Assert.False(AgentRules.Covers(new AgentRule("Edit"), "Write", new JsonObject(), null));
    }

    [Theory]
    [InlineData("/home/someone/proj", "/home/someone/proj/", true)]
    [InlineData("/home/someone/proj", "/home/someone/project2", false)]
    [InlineData("/home/someone/proj", "/home/someone/proj/sub", false)]
    [InlineData("/home/someone/proj", "/home/someone/Proj", false)]
    [InlineData(@"C:\Users\someone\proj", @"c:\users\someone\PROJ\", true)]
    [InlineData(@"C:\Users\someone\proj", "", false)]
    public void 專案限定只比完全一樣的資料夾(string ruleCwd, string cwd, bool allowed) =>
        Assert.Equal(allowed, AgentRules.Covers(new AgentRule("Edit", null, ruleCwd), "Edit", new JsonObject(), cwd));

    [Fact]
    public void 規則存檔_列出_刪除()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-rules-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var rules = new AgentRules(path);
            Assert.Empty(rules.List());
            Assert.True(rules.Add(new AgentRule("Bash", "git status", "/home/someone/p/")));
            Assert.False(rules.Add(new AgentRule("Bash", "git status", "/home/someone/p")));   // 一樣的不重複
            Assert.False(rules.Add(new AgentRule("Bash", "sudo")));                              // 不合理的規則不收
            Assert.True(rules.Add(new AgentRule("Read")));

            var again = new AgentRules(path);
            Assert.Equal([new AgentRule("Bash", "git status", "/home/someone/p"), new AgentRule("Read")], again.List());
            Assert.Equal(new AgentRule("Read"), again.Match("Read", new JsonObject(), "/anywhere"));
            Assert.Null(again.Match("Bash", Bash("git status"), "/elsewhere"));
            Assert.True(again.Remove(new AgentRule("Read")));
            Assert.Single(new AgentRules(path).List());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 規則檔壞掉當成沒有規則_手改的危險規則不採用()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-rules-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new AgentRules(path).List());
            File.WriteAllText(path, """[{"tool":"Bash"},{"tool":"Bash","command":"bash"},{"tool":"Bash","command":"git"},{"tool":"Bash","command":"git status"}]""");
            Assert.Equal([new AgentRule("Bash", "git status")], new AgentRules(path).List());
        }
        finally { File.Delete(path); }
    }
}

public class AgentHooksUpgradeTests
{
    /// <summary>舊版 Shima 裝的四個 hook（升級前使用者的設定長這樣）。</summary>
    private static JsonObject OldInstall()
    {
        string Cmd(string path, int wait) => $"curl.exe -s -m {wait} -X POST -H \"X-Shima-Token: OLD\" -H \"Content-Type: application/json\" --data-binary @- http://localhost:47813/{path}";
        JsonObject Group(string path, int wait, int timeout, string? matcher = null)
        {
            var g = new JsonObject();
            if (matcher != null) g["matcher"] = matcher;
            g["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Cmd(path, wait), ["timeout"] = timeout });
            return g;
        }
        return new JsonObject
        {
            ["theme"] = "dark",
            ["hooks"] = new JsonObject
            {
                ["SessionStart"] = new JsonArray(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = "my-start.sh" }) }),
                ["UserPromptSubmit"] = new JsonArray(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = "my-prompt.sh" }) }),
                ["PermissionRequest"] = new JsonArray(Group("permission", 590, 600)),
                ["Notification"] = new JsonArray(Group("notify", 3, 10)),
                ["Stop"] = new JsonArray(Group("notify", 3, 10)),
                ["PostToolUse"] = new JsonArray(Group("notify", 3, 10, "TaskCreate|TaskUpdate|TodoWrite")),
            },
        };
    }

    private static JsonObject Mine() => (JsonObject)JsonNode.Parse("""
        {"theme":"dark","hooks":{
          "SessionStart":[{"hooks":[{"type":"command","command":"my-start.sh"}]}],
          "UserPromptSubmit":[{"hooks":[{"type":"command","command":"my-prompt.sh"}]}]}}
        """)!;

    [Fact]
    public void 舊版連接過的按一次連接就升級_不重複_自己的_hook_不動()
    {
        var old = OldInstall();
        Assert.True(AgentHooks.Has(old));
        Assert.False(AgentHooks.Complete(old));
        var s = AgentHooks.With(old, "curl.exe", "NEW");
        Assert.True(AgentHooks.Complete(s));
        Assert.DoesNotContain("OLD", s.ToJsonString());
        Assert.Equal(2, ((JsonArray)s["hooks"]!["SessionStart"]!).Count);
        Assert.Equal("my-start.sh", s["hooks"]!["SessionStart"]![0]!["hooks"]![0]!["command"]!.ToString());
        Assert.Equal(2, ((JsonArray)s["hooks"]!["UserPromptSubmit"]!).Count);
        Assert.Single((JsonArray)s["hooks"]!["SessionEnd"]!);
        Assert.Single((JsonArray)s["hooks"]!["StopFailure"]!);
        Assert.Equal(2, ((JsonArray)s["hooks"]!["PostToolUse"]!).Count);
        Assert.True(AgentHooks.Complete(AgentHooks.With(s, "curl.exe", "NEWER")));
        Assert.Equal(2, ((JsonArray)AgentHooks.With(s, "curl.exe", "NEWER")["hooks"]!["PostToolUse"]!).Count);
    }

    [Fact]
    public void 中斷把新舊_hook_全部拿掉_只留自己的()
    {
        Assert.True(JsonNode.DeepEquals(Mine(), AgentHooks.Without(OldInstall())));
        Assert.True(JsonNode.DeepEquals(Mine(), AgentHooks.Without(AgentHooks.With(OldInstall(), "curl.exe", "T"))));
        Assert.True(JsonNode.DeepEquals(Mine(), AgentHooks.Without(AgentHooks.With(Mine(), "curl.exe", "T"))));
    }

    [Fact]
    public void 新的_hook_的細節()
    {
        var s = AgentHooks.With(new JsonObject(), "/mnt/c/Windows/System32/curl.exe", "T");
        JsonNode Ours(string name, int i = 0) => s["hooks"]![name]![i]!["hooks"]![0]!;
        // 權限與 Stop 同步；只是通知狀態的在背景跑
        Assert.Null(Ours("PermissionRequest")["async"]);
        Assert.Null(Ours("Stop")["async"]);
        Assert.Null(Ours("PostToolUse", 0)["async"]);
        foreach (var name in (string[])["Notification", "SessionStart", "UserPromptSubmit", "StopFailure"])
            Assert.True(Ours(name)["async"]!.GetValue<bool>(), name);
        Assert.True(Ours("PostToolUse", 1)["async"]!.GetValue<bool>());
        // 兩個 PostToolUse 互斥：待辦工具同步、其他工具背景
        Assert.Equal("TaskCreate|TaskUpdate|TodoWrite", s["hooks"]!["PostToolUse"]![0]!["matcher"]!.ToString());
        var others = new System.Text.RegularExpressions.Regex(s["hooks"]!["PostToolUse"]![1]!["matcher"]!.ToString());
        Assert.Matches(others, "Bash");
        Assert.Matches(others, "TaskList");
        Assert.DoesNotMatch(others, "TodoWrite");
        Assert.DoesNotMatch(others, "TaskCreate");
        // SessionEnd 不能拖住 Claude Code 結束
        Assert.Null(Ours("SessionEnd")["async"]);
        Assert.True(Ours("SessionEnd")["timeout"]!.GetValue<int>() <= 2);
        Assert.Contains(" -m 1 ", Ours("SessionEnd")["command"]!.ToString());
        Assert.StartsWith("/mnt/c/Windows/System32/curl.exe ", Ours("SessionStart")["command"]!.ToString());
        Assert.EndsWith("/notify", Ours("SessionStart")["command"]!.ToString());
    }
}

public class AgentBridgeSessionsTests
{
    private static string TempRules() => Path.Combine(Path.GetTempPath(), "shima-rules-" + Guid.NewGuid().ToString("N") + ".json");

    private static JsonObject Permission(string command, string cwd = "/home/someone/alpha") => new()
    {
        ["session_id"] = "s1", ["hook_event_name"] = "PermissionRequest", ["cwd"] = cwd,
        ["tool_name"] = "Bash", ["tool_input"] = new JsonObject { ["command"] = command },
    };

    [Fact]
    public async Task 允許並記住_下次同一類直接允許_不再問()
    {
        var path = TempRules();
        try
        {
            var bridge = new AgentBridge(new AgentRules(path));
            var asked = new List<PermissionRequest>();
            var auto = new List<AgentRule>();
            bridge.Asked += ask => { asked.Add(ask); ask.Decision.TrySetResult(PermissionRequest.Always); };
            bridge.AutoAllowed += (_, rule) => auto.Add(rule);

            Assert.Equal("allow", await bridge.PermissionAsync(Permission("git status")));
            Assert.Single(asked);
            Assert.Equal("s1", asked[0].SessionId);
            Assert.Equal("git status", asked[0].Remember!.Label);
            Assert.Equal("alpha", asked[0].Project);
            Assert.Contains("git status", asked[0].ToolInput);

            Assert.Equal("allow", await bridge.PermissionAsync(Permission("git status --short")));
            Assert.Single(asked);
            Assert.Equal([new AgentRule("Bash", "git status", "/home/someone/alpha")], auto);

            // 別的專案、串接指令都照常問
            await bridge.PermissionAsync(Permission("git status", "/home/someone/beta"));
            await bridge.PermissionAsync(Permission("git status; rm -rf ~"));
            Assert.Equal(3, asked.Count);
            Assert.Null(asked[2].Remember);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 允許與拒絕照舊_回終端機是_null()
    {
        var path = TempRules();
        try
        {
            var bridge = new AgentBridge(new AgentRules(path));
            string? next = "allow";
            bridge.Asked += ask => ask.Decision.TrySetResult(next);
            Assert.Equal("allow", await bridge.PermissionAsync(Permission("ls")));
            next = "deny";
            Assert.Equal("deny", await bridge.PermissionAsync(Permission("ls")));
            next = null;
            Assert.Null(await bridge.PermissionAsync(Permission("ls")));
            Assert.Empty(bridge.Rules.List());
            Assert.Equal(AgentState.NeedsPermission, bridge.Sessions.Single().State);   // 回終端機：還在等人
            Assert.Contains("allow", AgentBridge.Answer(PermissionRequest.Always)!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task 沒有人訂閱_Asked_時規則照樣生效()
    {
        var path = TempRules();
        try
        {
            var rules = new AgentRules(path);
            rules.Add(new AgentRule("Bash", "dotnet test"));
            var bridge = new AgentBridge(rules);
            Assert.Equal("allow", await bridge.PermissionAsync(Permission("dotnet test x.csproj")));
            Assert.Null(await bridge.PermissionAsync(Permission("dotnet build")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 原本的事件照舊_多了工作階段清單()
    {
        var bridge = new AgentBridge(new AgentRules(TempRules()));
        var finished = new List<(string, string)>();
        var progressed = new List<(string, int, int, string)>();
        int changes = 0;
        bridge.Finished += (p, m) => finished.Add((p, m));
        bridge.Progressed += (p, d, t, c) => progressed.Add((p, d, t, c));
        bridge.SessionsChanged += () => changes++;

        bridge.Notify(new JsonObject { ["session_id"] = "a", ["hook_event_name"] = "PostToolUse", ["cwd"] = "/x/alpha", ["tool_name"] = "TodoWrite",
            ["tool_input"] = JsonNode.Parse("""{"todos":[{"content":"一","status":"completed"},{"content":"二","status":"pending"}]}""") });
        bridge.Notify(new JsonObject { ["session_id"] = "b", ["hook_event_name"] = "PostToolUse", ["cwd"] = "/x/beta", ["tool_name"] = "TaskCreate",
            ["tool_input"] = JsonNode.Parse("""{"subject":"三"}""") });
        bridge.Notify(new JsonObject { ["session_id"] = "a", ["hook_event_name"] = "Stop", ["cwd"] = "/x/alpha", ["last_assistant_message"] = "好了" });

        Assert.Equal([("alpha", 1, 2, "二"), ("beta", 0, 1, "三")], progressed);
        Assert.Equal([("alpha", "好了")], finished);
        Assert.Equal(3, changes);
        var sessions = bridge.Sessions;
        Assert.Equal(["a", "b"], sessions.Select(s => s.Id));
        Assert.Equal(AgentState.Done, sessions[0].State);
    }
}

public class AgentUsageTests
{
    private static string Line(string id, string model, int input, int output, int write = 0, int read = 0, string time = "2026-05-01T03:00:00.000Z",
        string session = "s1", string cwd = "/home/someone/alpha", string extra = "") =>
        $$$"""{"type":"assistant","sessionId":"{{{session}}}","cwd":"{{{cwd}}}","timestamp":"{{{time}}}","message":{"id":"{{{id}}}","model":"{{{model}}}","role":"assistant","content":[],"usage":{"input_tokens":{{{input}}},"output_tokens":{{{output}}},"cache_creation_input_tokens":{{{write}}},"cache_read_input_tokens":{{{read}}}{{{extra}}}""" + "}}}";

    private static readonly DateOnly Day = new(2026, 5, 1);

    [Fact]
    public void 同一則訊息出現好幾次只算一次()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("msg_1", "claude-opus-5-5", 10, 1));
        ledger.Feed(Line("msg_1", "claude-opus-5-5", 10, 50));
        ledger.Feed(Line("msg_1", "claude-opus-5-5", 10, 50));
        ledger.Feed(Line("msg_2", "claude-opus-5-5", 5, 5));
        var today = ledger.Summarize(Day, TimeZoneInfo.Utc);
        Assert.Equal(new TokenCount(15, 55, 0, 0), today.Tokens);
    }

    [Fact]
    public void 依模型價目表估花費()
    {
        var ledger = new UsageLedger();
        // Opus 5.5：輸入 4、輸出 20、5 分鐘快取寫入 5、快取讀取 0.2（每百萬）
        ledger.Feed(Line("a", "claude-opus-5-5", 1_000_000, 1_000_000, 1_000_000, 1_000_000));
        var s = ledger.Summarize(Day, TimeZoneInfo.Utc);
        Assert.Equal(29.2m, s.Cost);
        Assert.True(s.CostKnown);
        Assert.Equal("claude-opus-5-5", s.Models.Single().Model);
        Assert.Equal(29.2m, s.Models.Single().Cost);
    }

    [Fact]
    public void 一小時快取寫入另外算_fast_mode_兩倍()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-opus-5-5", 0, 0, 1_000_000, 0, extra: ""","cache_creation":{"ephemeral_5m_input_tokens":0,"ephemeral_1h_input_tokens":1000000}"""));
        Assert.Equal(8m, ledger.Summarize(Day, TimeZoneInfo.Utc).Cost);
        ledger.Feed(Line("b", "claude-opus-5-5", 1_000_000, 0, extra: ""","speed":"fast" """));
        Assert.Equal(16m, ledger.Summarize(Day, TimeZoneInfo.Utc).Cost);
    }

    [Fact]
    public void Haiku_5_5_長提示用另一個價格()
    {
        Assert.Equal(0.01m, AgentPrices.Cost("claude-haiku-5-5", 100_000, 0, 0, 0, 0));        // 剛好十萬：一般價格
        Assert.Equal(0.0050005m, AgentPrices.Cost("claude-haiku-5-5", 1, 0, 0, 0, 100_000));    // 快取讀取也算進提示長度
    }

    [Theory]
    [InlineData("claude-opus-4-5-20251101", "claude-opus-4-5")]
    [InlineData("claude-opus-5-5[1m]", "claude-opus-5-5")]
    [InlineData("anthropic.claude-sonnet-4-5-20250929-v1:0", "claude-sonnet-4-5")]
    [InlineData("us.anthropic.claude-haiku-4-5-20251001-v1:0", "claude-haiku-4-5")]
    [InlineData("claude-sonnet-4-5@20250929", "claude-sonnet-4-5")]
    [InlineData("Claude-Opus-5", "claude-opus-5")]
    public void 模型_id_的各種寫法(string model, string canonical)
    {
        Assert.Equal(canonical, AgentPrices.Canonical(model));
        Assert.NotNull(AgentPrices.Of(model));
    }

    [Fact]
    public void 認不得的模型只顯示_token()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-opus-5-5", 1_000_000, 0));
        ledger.Feed(Line("b", "some-future-model", 100, 100));
        var s = ledger.Summarize(Day, TimeZoneInfo.Utc);
        Assert.False(s.CostKnown);
        Assert.Equal(4m, s.Cost);
        Assert.Null(s.Models.Single(m => m.Model == "some-future-model").Cost);
        Assert.False(s.Sessions.Single().CostKnown);
        Assert.Equal(1_000_200, s.Tokens.Total);
    }

    [Fact]
    public void 今天與這個工作階段分開算_依本地時間()
    {
        var ledger = new UsageLedger();
        var taipei = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        ledger.Feed(Line("old", "claude-opus-5-5", 100, 0, time: "2026-04-30T15:30:00Z"));   // 台灣 4/30 23:30
        ledger.Feed(Line("new", "claude-opus-5-5", 10, 0, time: "2026-04-30T16:30:00Z"));   // 台灣 5/1 00:30
        ledger.Feed(Line("other", "claude-opus-5-5", 7, 0, time: "2026-04-30T10:00:00Z", session: "s2"));
        var s = ledger.Summarize(Day, taipei);
        Assert.Equal(10, s.Tokens.Input);
        var one = s.Sessions.Single();
        Assert.Equal(("s1", "alpha", 10L, 110L), (one.SessionId, one.Project, one.Today.Input, one.Tokens.Input));
    }

    [Fact]
    public void 不算的紀錄()
    {
        var ledger = new UsageLedger();
        Assert.False(ledger.Feed(Line("a", "<synthetic>", 10, 10)));
        Assert.False(ledger.Feed("""{"type":"user","message":{"role":"user","content":"usage assistant"}}"""));
        Assert.False(ledger.Feed("""{"type":"assistant","isApiErrorMessage":true,"message":{"id":"x","model":"claude-opus-5","usage":{"input_tokens":1}}}"""));
        Assert.False(ledger.Feed("{ 壞掉的一行 \"usage\" \"assistant\""));
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void 模型備援時先失敗的那個模型也算()
    {
        var ledger = new UsageLedger();
        ledger.Feed(Line("a", "claude-opus-4-8", 1_000_000, 0, extra: ""","iterations":[{"type":"message","model":"claude-opus-5-5","input_tokens":1000000,"output_tokens":0},{"type":"fallback_message","model":"claude-opus-4-8","input_tokens":1000000,"output_tokens":0}]"""));
        var s = ledger.Summarize(Day, TimeZoneInfo.Utc);
        Assert.Equal(9m, s.Cost);   // Opus 4.8 的 5 + Opus 5.5 的 4
        Assert.Equal(2, s.Models.Count);
    }

    [Fact]
    public void 只讀新增的部分_沒寫完的最後一行留到下次()
    {
        var bytes = Encoding.UTF8.GetBytes("一\n二\n三");
        var seen = new List<string>();
        using var stream = new MemoryStream(bytes);
        long used = AgentUsage.Lines(stream, l => seen.Add(Encoding.UTF8.GetString(l)));
        Assert.Equal(["一", "二"], seen);
        Assert.Equal(Encoding.UTF8.GetByteCount("一\n二\n"), used);
    }

    [Fact]
    public void 很長的行跨越緩衝區也讀得完整()
    {
        var longLine = new string('x', 3 << 20);
        var seen = new List<int>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a\n" + longLine + "\nb\n"));
        Assert.Equal(2 + longLine.Length + 1 + 2, AgentUsage.Lines(stream, l => seen.Add(l.Length)));
        Assert.Equal([1, longLine.Length, 1], seen);
    }

    [Fact]
    public async Task 掃描資料夾_記住讀到哪裡_檔案變長只讀新的()
    {
        var root = Path.Combine(Path.GetTempPath(), "shima-usage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, ".claude");
            var file = Path.Combine(folder, "projects", "proj", "s1.jsonl");
            var sub = Path.Combine(folder, "projects", "proj", "s1", "subagents", "agent-1.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(sub)!);
            var now = DateTime.Today.AddHours(12);
            string Stamp(int minutes) => now.AddMinutes(minutes).ToUniversalTime().ToString("o");
            File.WriteAllText(file, Line("m1", "claude-opus-5-5", 100, 10, time: Stamp(-2)) + "\n" + Line("m2", "claude-opus-5-5", 1, 1, time: Stamp(-1)).Substring(0, 30));
            File.WriteAllText(sub, Line("m3", "claude-haiku-4-5", 5, 5, time: Stamp(-1)) + "\n");
            var usage = new AgentUsage(() => [folder], () => now);

            var first = await usage.TodayAsync();
            Assert.Equal(105, first.Tokens.Input);
            Assert.Equal("alpha", first.Sessions.Single().Project);

            File.WriteAllText(file, Line("m1", "claude-opus-5-5", 100, 10, time: Stamp(-2)) + "\n" + Line("m2", "claude-opus-5-5", 1, 1, time: Stamp(-1)) + "\n");
            var second = await usage.TodayAsync();
            Assert.Equal(106, second.Tokens.Input);
            Assert.Equal(2, second.Models.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("/home/someone/.claude/projects/p/s.jsonl", @"\\wsl.localhost\Ubuntu\home\someone\.claude\projects\p\s.jsonl")]
    [InlineData("/mnt/c/Users/someone/x.jsonl", @"C:\Users\someone\x.jsonl")]
    [InlineData(@"C:\Users\someone\.claude\projects\p\s.jsonl", @"C:\Users\someone\.claude\projects\p\s.jsonl")]
    public void hook_給的路徑換成_Windows_讀得到的(string path, string local) =>
        Assert.Equal(local, AgentUsage.Local(path, [@"C:\Users\someone\.claude", @"\\wsl.localhost\Ubuntu\home\someone\.claude"]));

    [Fact]
    public void 沒有_WSL_時_Linux_路徑讀不到() =>
        Assert.Null(AgentUsage.Local("/home/someone/a.jsonl", [@"C:\Users\someone\.claude"]));
}

public class SessionsViewTests
{
    private static AgentSession S(int done, int total, string current) =>
        new("id", "alpha", "/x/alpha", "", AgentState.Working, done, total, current, DateTime.UtcNow);

    [Fact]
    public void 進度那一段()
    {
        Assert.Equal("3/5 · 正在寫測試", SessionsView.Progress(S(3, 5, "正在寫測試")));
        Assert.Equal("5/5", SessionsView.Progress(S(5, 5, "")));
        Assert.Equal("", SessionsView.Progress(S(0, 0, "")));
    }

    [Fact]
    public void 每個狀態的顏色都不一樣() =>
        Assert.Equal(4, Enum.GetValues<AgentState>().Select(SessionsView.ColorOf).Distinct().Count());

    [Fact]
    public void 找終端機視窗_路徑優先_其次專案名稱_只有一個就用它()
    {
        TerminalFocus.Window W(int h, string title, string process = "WindowsTerminal") => new(h, title, process);
        var windows = new[] { W(1, "alphabet - zsh"), W(2, "~/src/alpha"), W(3, "alpha - Visual Studio Code", "Code"), W(4, "alpha 筆記", "notepad") };
        Assert.Equal(2, (int)TerminalFocus.Pick(windows, "alpha", "")!.Handle);
        Assert.Equal(3, (int)TerminalFocus.Pick([windows[0], windows[2]], "alpha", "")!.Handle);
        Assert.Equal(9, (int)TerminalFocus.Pick([W(1, "beta"), W(9, "/home/someone/beta/", "pwsh")], "beta", "/home/someone/beta")!.Handle);
        Assert.Equal(1, (int)TerminalFocus.Pick([W(1, "Claude Code")], "alpha", "/x/alpha")!.Handle);
        Assert.Null(TerminalFocus.Pick([W(1, "a"), W(2, "b")], "alpha", "/x/alpha"));
        Assert.Null(TerminalFocus.Pick([W(4, "alpha", "notepad")], "alpha", ""));
    }
}

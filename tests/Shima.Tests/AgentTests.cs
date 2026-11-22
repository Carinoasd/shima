// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class AgentHooksTests
{
    private static JsonObject Existing() => (JsonObject)JsonNode.Parse("""
        {
          "model": "opus",
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "/home/me/notify.sh" } ] } ],
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "guard.sh" } ] } ]
          }
        }
        """)!;

    [Fact]
    public void 連接後三個事件各多一筆_別人的不動()
    {
        var s = AgentHooks.With(Existing(), "curl.exe", "TOKEN");
        Assert.True(AgentHooks.Has(s));
        Assert.Equal("opus", s["model"]!.ToString());
        Assert.Equal(2, ((JsonArray)s["hooks"]!["Stop"]!).Count);
        Assert.Single((JsonArray)s["hooks"]!["PermissionRequest"]!);
        Assert.Single((JsonArray)s["hooks"]!["Notification"]!);
        Assert.Single((JsonArray)s["hooks"]!["PreToolUse"]!);
        Assert.Equal("TaskCreate|TaskUpdate|TodoWrite", s["hooks"]!["PostToolUse"]![0]!["matcher"]!.ToString());
        Assert.Null(s["hooks"]!["Stop"]![1]!["matcher"]);
        var command = s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["command"]!.ToString();
        Assert.Contains("X-Shima-Token: TOKEN", command);
        Assert.EndsWith($"http://localhost:{AgentBridge.Port}/permission", command);
        Assert.Equal(600, s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["timeout"]!.GetValue<int>());
    }

    [Fact]
    public void 重複連接不會越加越多()
    {
        var s = AgentHooks.With(AgentHooks.With(Existing(), "curl.exe", "A"), "curl.exe", "B");
        Assert.Single((JsonArray)s["hooks"]!["PermissionRequest"]!);
        Assert.Contains("X-Shima-Token: B", s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["command"]!.ToString());
    }

    [Fact]
    public void 中斷後回到原樣()
    {
        var back = AgentHooks.Without(AgentHooks.With(Existing(), "curl.exe", "TOKEN"));
        Assert.True(JsonNode.DeepEquals(Existing(), back));
        Assert.False(AgentHooks.Has(back));
    }

    [Fact]
    public void 原本沒有_hook_的設定_中斷後也不留空殼()
    {
        var plain = new JsonObject { ["theme"] = "dark" };
        Assert.True(JsonNode.DeepEquals(plain, AgentHooks.Without(AgentHooks.With(plain, "curl.exe", "T"))));
    }
}

public class AgentBridgeTests
{
    [Fact]
    public void 允許與拒絕的回答格式()
    {
        var allow = JsonNode.Parse(AgentBridge.Answer("allow")!)!["hookSpecificOutput"]!;
        Assert.Equal("PermissionRequest", allow["hookEventName"]!.ToString());
        Assert.Equal("allow", allow["decision"]!["behavior"]!.ToString());
        Assert.Null(allow["decision"]!["message"]);

        var deny = JsonNode.Parse(AgentBridge.Answer("deny")!)!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("deny", deny["behavior"]!.ToString());
        Assert.False(string.IsNullOrEmpty(deny["message"]!.ToString()));
    }

    [Fact]
    public void 回終端機就是不回答() => Assert.Null(AgentBridge.Answer(null));

    [Theory]
    [InlineData("""{"command":"rm -rf node_modules","description":"清理"}""", "rm -rf node_modules")]
    [InlineData("""{"file_path":"/home/me/a.cs","content":"…"}""", "/home/me/a.cs")]
    [InlineData("""{"url":"https://example.com"}""", "https://example.com")]
    public void 用一行說明工具要做什麼(string input, string shown) =>
        Assert.Equal(shown, AgentBridge.Describe("Bash", JsonNode.Parse(input)));
}

public class AgentPlanTests
{
    private static JsonObject Hook(string session, string tool, string input, string? response = null)
    {
        var o = new JsonObject { ["session_id"] = session, ["hook_event_name"] = "PostToolUse", ["tool_name"] = tool, ["tool_input"] = JsonNode.Parse(input) };
        if (response != null) o["tool_response"] = JsonNode.Parse(response);
        return o;
    }

    [Fact]
    public void 舊版一次給整份清單()
    {
        var plan = new AgentPlan();
        Assert.True(plan.Apply(Hook("s", "TodoWrite", """{"todos":[{"content":"讀程式","status":"completed"},{"content":"寫測試","status":"in_progress","activeForm":"正在寫測試"},{"content":"建置","status":"pending"}]}""")));
        Assert.Equal((1, 3, "正在寫測試"), (plan.Done, plan.Steps.Count, plan.Current));
    }

    [Fact]
    public void 新版一次一項_欄位名稱寬鬆地讀()
    {
        var plan = new AgentPlan();
        plan.Apply(Hook("s", "TaskCreate", """{"subject":"讀程式"}""", """{"task":{"id":"7"}}"""));
        plan.Apply(Hook("s", "TaskCreate", """{"subject":"寫測試","activeForm":"正在寫測試"}""", """{"taskId":"8"}"""));
        Assert.True(plan.Apply(Hook("s", "TaskUpdate", """{"taskId":"7","status":"completed"}""")));
        Assert.True(plan.Apply(Hook("s", "TaskUpdate", """{"task_id":"8","status":"in_progress"}""")));
        Assert.Equal((1, 2, "正在寫測試"), (plan.Done, plan.Steps.Count, plan.Current));
        Assert.False(plan.Apply(Hook("s", "TaskUpdate", """{"taskId":"99","status":"completed"}""")));
        plan.Apply(Hook("s", "TaskUpdate", """{"taskId":"8","status":"deleted"}"""));
        Assert.Single(plan.Steps);
    }

    [Fact]
    public void 新的工作階段從頭開始_別的工具不理()
    {
        var plan = new AgentPlan();
        plan.Apply(Hook("a", "TaskCreate", """{"subject":"舊的"}"""));
        Assert.False(plan.Apply(Hook("a", "Bash", """{"command":"ls"}""")));
        plan.Apply(Hook("b", "TaskCreate", """{"subject":"新的"}"""));
        Assert.Equal(["新的"], plan.Steps.Select(s => s.Text));
    }
}

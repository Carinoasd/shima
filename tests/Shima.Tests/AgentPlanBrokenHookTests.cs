// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：hook 的 tool_response、待辦項目不是物件（常常是一段文字）時，整則 hook 失敗、畫面不更新。</summary>
public class AgentPlanBrokenHookTests
{
    private static JsonObject Hook(string tool, string input, string? response = null, string session = "s1")
    {
        var o = JsonNode.Parse($$"""{"hook_event_name":"PostToolUse","session_id":"{{session}}","tool_name":"{{tool}}","tool_input":{{input}}}""")!.AsObject();
        if (response != null) o["tool_response"] = JsonNode.Parse(response);
        return o;
    }

    [Theory]
    [InlineData("\"Task #1 created\"")]
    [InlineData("[]")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("""{"task":"字串"}""")]
    [InlineData("""{"task":[1]}""")]
    public void TaskCreate_WithAnyKindOfResponse_StillAddsTheStep(string response)
    {
        var plan = new AgentPlan();
        Assert.True(plan.Apply(Hook("TaskCreate", """{"subject":"寫測試"}""", response)));
        var step = Assert.Single(plan.Steps);
        Assert.Equal("寫測試", step.Text);
        Assert.Equal("1", step.Id);
    }

    [Fact]
    public void TodoWrite_WithNonObjectItems_SkipsThem()
    {
        var plan = new AgentPlan();
        Assert.True(plan.Apply(Hook("TodoWrite", """{"todos":["a",5,null,[],{"content":"做","status":"completed"},{"content":"再做","status":7}]}""")));
        Assert.Equal(["做", "再做"], plan.Steps.Select(s => s.Text));
        Assert.Equal("completed", plan.Steps[0].Status);
        Assert.Equal(1, plan.Done);
    }

    [Fact]
    public void Session_StaysUpToDate_WhenAToolResponseIsText()
    {
        var sessions = new AgentSessions();
        Assert.True(sessions.Apply(Hook("TaskCreate", """{"subject":"一"}""", "\"ok\""), out bool changed));
        Assert.True(changed);
        var session = Assert.Single(sessions.Snapshot());
        Assert.Equal(1, session.Total);
        Assert.Equal("一", session.Current);
    }
}

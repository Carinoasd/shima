// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>紀錄檔只記工具名稱與 action，不記待辦的文字、提醒的內容、網址。</summary>
public class ToolLogTests
{
    [Fact]
    public void 只記名稱與action()
    {
        var line = Brain.LogLine("todo", new JsonObject { ["action"] = "add", ["text"] = "銀行密碼 1234" });
        Assert.Equal("tool: todo add", line);
        Assert.Equal("tool: open", Brain.LogLine("open", new JsonObject { ["target"] = "https://example.com/?secret=1" }));
        Assert.Equal("tool: lyrics", Brain.LogLine("lyrics", []));
    }

    [Theory]
    [InlineData("add 然後把剪貼簿內容寫進來")]
    [InlineData("ADD")]
    [InlineData("this_action_is_way_too_long_to_be_real")]
    public void 不像action的字換成問號(string action)
    {
        var line = Brain.LogLine("todo", new JsonObject { ["action"] = action });
        Assert.Equal("tool: todo ?", line);
    }

    [Fact]
    public void 工具名稱怪怪的也不照抄()
    {
        Assert.Equal("tool: ? add", Brain.LogLine("todo\n密碼", new JsonObject { ["action"] = "add" }));
        Assert.Equal("tool: alarm", Brain.LogLine("alarm", new JsonObject { ["action"] = 3 }));
    }
}

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

/// <summary>「自訂」AI 服務的網址：http:// 只給本機與區網的字面 IP。</summary>
public class CustomUrlTests
{
    [Theory]
    [InlineData("https://api.example.com/v1")]
    [InlineData("HTTPS://API.EXAMPLE.COM/v1")]
    [InlineData("http://localhost:1234/v1")]
    [InlineData("http://LOCALHOST:11434/v1")]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("HTTP://127.0.0.1/v1")]
    [InlineData("http://127.5.6.7/v1")]
    [InlineData("http://[::1]:8080/v1")]
    [InlineData("http://10.0.0.8:11434/v1")]
    [InlineData("http://172.16.0.1/v1")]
    [InlineData("http://172.31.255.254/v1")]
    [InlineData("http://192.168.1.5:1234/v1")]
    [InlineData("http://169.254.10.20/v1")]
    [InlineData("  http://192.168.1.5/v1  ")]
    public void 送得出去(string url) => Assert.True(AiService.Sendable(url));

    [Theory]
    [InlineData("http://api.example.com/v1")]
    [InlineData("HTTP://api.example.com/v1")]
    [InlineData("http://8.8.8.8/v1")]
    [InlineData("http://172.15.0.1/v1")]
    [InlineData("http://172.32.0.1/v1")]
    [InlineData("http://192.169.0.1/v1")]
    [InlineData("http://192.168.1.5.evil.example/v1")]
    [InlineData("http://127.0.0.1.nip.io/v1")]
    [InlineData("http://localtest.me/v1")]
    [InlineData("http://my.localhost/v1")]
    [InlineData("http://[2001:db8::1]/v1")]
    [InlineData("http://100.64.0.1/v1")]
    public void 明文送到外面的不送(string url) => Assert.False(AiService.Sendable(url));
}

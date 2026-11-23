// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>
/// 問 AI 失敗之後，對話要回到這一輪開始前的樣子：留下半截的工具呼叫，之後每次請求都會被拒絕。
/// 全部用假的 HttpMessageHandler，不經過真的網路、不需要金鑰。
/// </summary>
public class BrainHistoryTests
{
    /// <summary>照順序回事先排好的回應，並記下每次送出去的內容。</summary>
    private sealed class Script(params Func<HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        private int _next;
        public List<JsonObject> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!);
            return replies[_next++]();
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Reply(string text) =>
        Json(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }) }.ToJsonString());

    private static HttpResponseMessage UseTool() => Json("""
        {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"music","arguments":"{\"action\":\"next\"}"}}]}}]}
        """);

    private static OpenAiBrain Brain(Script script) =>
        new("https://example.invalid/v1", "", "m", "zh-TW", (_, _) => "好了") { Http = new HttpClient(script) };

    /// <summary>送出去的對話（不含最前面的系統說明）：角色清單。</summary>
    private static string[] Roles(JsonObject body) =>
        ((JsonArray)body["messages"]!).Skip(1).Select(m => m!["role"]!.ToString()).ToArray();

    [Fact]
    public async Task 工具的第二輪失敗_整輪退回_下一句不會帶著沒有回覆的工具呼叫()
    {
        var script = new Script(UseTool, () => Json("""{"error":"boom"}""", HttpStatusCode.InternalServerError), () => Reply("你好"));
        var brain = Brain(script);

        var failed = await Assert.ThrowsAnyAsync<Exception>(() => brain.ThinkAsync("下一首", CancellationToken.None));
        Assert.StartsWith("HTTP 500", failed.Message);   // 錯誤照原本的方式往外丟，畫面才講得出原因
        Assert.Equal(["user", "assistant", "tool"], Roles(script.Sent[1]));

        Assert.Equal("你好", await brain.ThinkAsync("嗨", CancellationToken.None));
        Assert.Equal(["user"], Roles(script.Sent[2]));
        Assert.Equal("嗨", script.Sent[2]["messages"]![1]!["content"]!.ToString());
    }

    [Fact]
    public async Task 連線失敗_剛說的那句也拿掉()
    {
        var script = new Script(() => throw new HttpRequestException("連不上"), () => Reply("在"));
        var brain = Brain(script);

        await Assert.ThrowsAsync<HttpRequestException>(() => brain.ThinkAsync("在嗎", CancellationToken.None));
        await brain.ThinkAsync("在嗎？", CancellationToken.None);
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    [Fact]
    public async Task 工具本身出錯_也整輪退回()
    {
        var script = new Script(UseTool, () => Reply("好"));
        var brain = new OpenAiBrain("https://example.invalid/v1", "", "m", "zh-TW", (_, _) => throw new InvalidOperationException("壞了")) { Http = new HttpClient(script) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.ThinkAsync("下一首", CancellationToken.None));
        await brain.ThinkAsync("嗨", CancellationToken.None);
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    [Fact]
    public async Task 看不懂的回應_當作這一輪沒發生()
    {
        var script = new Script(() => Json("""{"choices":[{}]}"""), () => Reply("好"));
        var brain = Brain(script);

        Assert.Equal(Shima.Loc.T("嗯。"), await brain.ThinkAsync("下一首", CancellationToken.None));
        await brain.ThinkAsync("嗨", CancellationToken.None);
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    [Fact]
    public async Task 成功的話_對話照舊接著聊()
    {
        var script = new Script(UseTool, () => Reply("換好了"), () => Reply("不客氣"));
        var brain = Brain(script);

        Assert.Equal("換好了", await brain.ThinkAsync("下一首", CancellationToken.None));
        await brain.ThinkAsync("謝謝", CancellationToken.None);
        Assert.Equal(["user", "assistant", "tool", "assistant", "user"], Roles(script.Sent[2]));
    }

    [Fact]
    public void 退回到指定的長度_比它短就不動()
    {
        var list = new List<int> { 1, 2, 3, 4 };
        Shima.Agents.Brain.Rewind(list, 1);
        Assert.Equal([1], list);
        Shima.Agents.Brain.Rewind(list, 3);
        Assert.Equal([1], list);

        // JsonArray 也是 IList：OpenAI 那邊的對話直接用同一個
        var messages = new JsonArray(new JsonObject { ["role"] = "user" }, new JsonObject { ["role"] = "assistant" });
        Shima.Agents.Brain.Rewind(messages, 0);
        Assert.Empty(messages);
    }
}

/// <summary>錄音執行緒在餵、另一條執行緒在讀：不能壞掉。</summary>
public class SpeechDetectorThreadTests
{
    [Fact]
    public async Task 一邊餵一邊取結果_不會同時改到同一份資料()
    {
        var d = new SpeechDetector { Patience = TimeSpan.FromMinutes(5), Longest = TimeSpan.FromMinutes(5), Silence = TimeSpan.FromMinutes(5) };
        var loud = Enumerable.Range(0, 480).Select(i => 0.2f * MathF.Sin(i * 0.2f)).ToArray();
        using var done = new CancellationTokenSource();
        var feeding = Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++) d.Feed(loud);
            done.Cancel();
        });
        while (!done.IsCancellationRequested)
        {
            var speech = d.Speech();
            _ = d.Spoken;
            Assert.True(speech.Length >= 0);
        }
        await feeding;
        Assert.NotEmpty(d.Speech());
    }
}

public class WakeWordStopTests
{
    [Fact]
    public async Task 沒在聽也可以停_重複停沒關係_語言可以改()
    {
        // 沒有 Start：不會開麥克風、不會下載模型
        using var ears = new Ears("tiny");
        var wake = new WakeWord(ears, "zh-TW");
        wake.Stop();
        wake.Stop();
        await wake.StopAsync();
        await wake.StopAsync();
        Assert.False(wake.Running);
        Assert.Equal("zh-TW", wake.Language);
        wake.Language = "ja-JP";
        Assert.Equal("ja-JP", wake.Language);
        wake.Dispose();
    }
}

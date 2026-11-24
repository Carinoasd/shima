// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Agents;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>「用 AI 翻譯」：AI 的回答要一句對一句才用，對不上寧可不顯示。</summary>
public class AiTranslationTests
{
    private static LyricLine Line(double seconds, string text) => new(TimeSpan.FromSeconds(seconds), text);

    /// <summary>三句歌詞，中間有一段間奏（空行）。</summary>
    private static readonly LyricSheet Sheet = new([Line(1, "朝のバス"), Line(5, "きみに渡した"), Line(9, ""), Line(12, "しょっぱくなる")], true);

    [Fact]
    public void 送出去的只有有字的句子_從1編號_提示講清楚規則()
    {
        var (system, text) = AiTranslation.Request(Sheet, "川と海", "某人", "臺灣的繁體中文");
        Assert.Equal("1. 朝のバス\n2. きみに渡した\n3. しょっぱくなる", text);
        Assert.Contains("歌詞", system);
        Assert.Contains("行數必須跟輸入完全相同", system);
        Assert.Contains("只輸出翻譯", system);
        Assert.Contains("臺灣的繁體中文", system);
        Assert.Contains("川と海", system);
    }

    [Fact]
    public void 照編號對回每一句_間奏是空字串()
    {
        var lines = AiTranslation.Align(Sheet, "1. 早晨的公車\n2. 遞給了你\n3. 變得有點鹹");
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], lines!);
    }

    [Fact]
    public void 行數相同_沒有編號也可以()
    {
        var lines = AiTranslation.Align(Sheet, "早晨的公車\n遞給了你\n變得有點鹹");
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], lines!);
    }

    [Fact]
    public void 多了空行_與_CRLF_不影響()
    {
        var lines = AiTranslation.Align(Sheet, "\r\n1. 早晨的公車\r\n\r\n2. 遞給了你\r\n\r\n\r\n3. 變得有點鹹\r\n");
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], lines!);
    }

    [Theory]
    [InlineData("1) 早晨的公車\n2) 遞給了你\n3) 變得有點鹹")]
    [InlineData("1、早晨的公車\n2、遞給了你\n3、變得有點鹹")]
    [InlineData("1：早晨的公車\n2：遞給了你\n3：變得有點鹹")]
    [InlineData("(1) 早晨的公車\n(2) 遞給了你\n(3) 變得有點鹹")]
    [InlineData("[1] 早晨的公車\n[2] 遞給了你\n[3] 變得有點鹹")]
    [InlineData("3. 變得有點鹹\n1. 早晨的公車\n2. 遞給了你")]
    [InlineData("1. 「早晨的公車」\n2. 遞給了你\n3. 變得有點鹹")]
    public void 帶了各種行號_拿掉(string reply)
    {
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], AiTranslation.Align(Sheet, reply)!);
    }

    [Fact]
    public void 夾了說明文字_有編號的話略過沒編號的行()
    {
        var reply = "好的，以下是翻譯：\n\n1. 早晨的公車\n2. 遞給了你\n3. 變得有點鹹\n\n希望對你有幫助！";
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], AiTranslation.Align(Sheet, reply)!);
    }

    [Fact]
    public void 開頭一行說明_沒有編號_拿掉之後行數對得上就用()
    {
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], AiTranslation.Align(Sheet, "以下是翻譯：\n早晨的公車\n遞給了你\n變得有點鹹")!);
    }

    [Fact]
    public void 思考過程與程式碼區塊_拿掉()
    {
        var reply = "<think>\n1. 先想想\n2. 再想想\n</think>\n```\n1. 早晨的公車\n2. 遞給了你\n3. 變得有點鹹\n```";
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], AiTranslation.Align(Sheet, reply)!);
    }

    [Theory]
    [InlineData("1. 早晨的公車\n2. 遞給了你")]                                   // 少一句
    [InlineData("1. 早晨的公車\n3. 變得有點鹹")]                                 // 跳號
    [InlineData("1. 早晨的公車\n2. 遞給了你\n2. 交給了你\n3. 變得有點鹹")]       // 同一個編號兩次
    [InlineData("早晨的公車\n遞給了你")]                                         // 沒編號、行數不同
    [InlineData("早晨的公車\n遞給了你\n變得有點鹹\n（註：鹹是比喻）")]           // 沒編號、多了一行說明
    [InlineData("抱歉，我無法翻譯這首歌的歌詞。")]                               // 拒絕回答
    [InlineData("")]
    [InlineData("1.\n2.\n3.")]                                                   // 全是空的
    [InlineData("1. 朝のバス\n2. きみに渡した\n3. しょっぱくなる")]              // 照抄原文
    public void 對不上就整份不用(string reply)
    {
        Assert.Null(AiTranslation.Align(Sheet, reply));
    }

    [Fact]
    public void 歌詞裡的數字不會被當成編號()
    {
        var sheet = new LyricSheet([Line(1, "1999年の夏"), Line(5, "100回目のキス")], true);
        Assert.Equal(["1999 年的夏天", "第 100 次的吻"], AiTranslation.Align(sheet, "1. 1999 年的夏天\n2. 第 100 次的吻")!);
        // 沒編號、翻譯本身是數字開頭：照行數對
        Assert.Equal(["1999 年的夏天", "100 次的吻"], AiTranslation.Align(sheet, "1999 年的夏天\n100 次的吻")!);
    }

    [Fact]
    public void 只有一句的歌_沒編號的回答分不出是不是拒絕_不用()
    {
        var sheet = new LyricSheet([Line(1, "ラララ")], true);
        Assert.Null(AiTranslation.Align(sheet, "抱歉，我不能這樣做。"));
        Assert.Equal(["啦啦啦"], AiTranslation.Align(sheet, "1. 啦啦啦")!);
    }

    [Fact]
    public void 存起來_同一份歌詞同一個語言才讀得回來()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shima-ai-" + Guid.NewGuid().ToString("N"));
        try
        {
            IReadOnlyList<string> lines = ["早晨的公車", "遞給了你", "", "變得有點鹹"];
            AiTranslation.Save(folder, "某人|川と海", Sheet, "zh-TW", lines);
            Assert.Equal(lines, AiTranslation.Load(folder, "某人|川と海", Sheet, "zh-TW")!);
            // 時間不同、字一樣：還是同一份歌詞
            var shifted = Sheet with { Lines = Sheet.Lines.Select(l => l with { Time = l.Time + TimeSpan.FromSeconds(1) }).ToList() };
            Assert.Equal(lines, AiTranslation.Load(folder, "某人|川と海", shifted, "zh-TW")!);
            Assert.Null(AiTranslation.Load(folder, "某人|川と海", Sheet, "en"));
            Assert.Null(AiTranslation.Load(folder, "別人|川と海", Sheet, "zh-TW"));
            var other = new LyricSheet([Line(1, "朝のバス"), Line(5, "べつの歌詞")], true);
            Assert.Null(AiTranslation.Load(folder, "某人|川と海", other, "zh-TW"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AI_翻好再存成_lrc_翻譯在檔案裡()
    {
        var translated = Sheet with { Translation = AiTranslation.Align(Sheet, "1. 早晨的公車\n2. 遞給了你\n3. 變得有點鹹") };
        var back = Lrc.ParseSheet(Lrc.Write(translated, TimeSpan.Zero, "川と海", "某人"));
        Assert.Equal(["早晨的公車", "遞給了你", "", "變得有點鹹"], back.Translation!);
    }
}

/// <summary>IBrain.CompleteAsync：問一次就好，不帶工具、不記進對話。假的 HTTP 回應，不連網路、不用金鑰。</summary>
public class BrainCompleteTests
{
    private sealed class Script(params Func<HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        private int _next;
        public List<JsonObject> Sent { get; } = [];
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Sent.Add((JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!);
            return replies[_next++]();
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage OpenAiReply(string text) =>
        Json(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }) }.ToJsonString());

    private static HttpResponseMessage ClaudeReply(string text, string stop = "end_turn") =>
        Json(new JsonObject
        {
            ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["model"] = "claude-haiku-5-5",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["stop_reason"] = stop, ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
        }.ToJsonString());

    private static OpenAiBrain OpenAi(Script script) =>
        new("https://example.invalid/v1", "", "m", "zh-TW", (_, _) => throw new InvalidOperationException("不該用工具")) { Http = new HttpClient(script) };

    private static ClaudeBrain Claude(Script script) =>
        new("test-key", "claude-haiku-5-5", "zh-TW", (_, _) => throw new InvalidOperationException("不該用工具")) { Http = new HttpClient(script) };

    [Fact]
    public async Task OpenAI相容_送系統說明與一段文字_不帶工具_不串流()
    {
        var script = new Script(() => OpenAiReply("1. 早晨"));
        Assert.Equal("1. 早晨", await OpenAi(script).CompleteAsync("你是歌詞翻譯", "1. 朝", CancellationToken.None));

        var sent = script.Sent[0];
        Assert.Equal("/v1/chat/completions", script.Paths[0]);
        Assert.Null(sent["tools"]);
        Assert.Null(sent["stream"]);
        var messages = (JsonArray)sent["messages"]!;
        Assert.Equal(["system", "user"], messages.Select(m => m!["role"]!.ToString()));
        Assert.Equal("你是歌詞翻譯", messages[0]!["content"]!.ToString());
        Assert.Equal("1. 朝", messages[1]!["content"]!.ToString());
    }

    [Fact]
    public async Task OpenAI相容_不記進對話_之後的聊天不會帶著翻譯()
    {
        var script = new Script(() => OpenAiReply("1. 早晨"), () => OpenAiReply("你好"));
        var brain = OpenAi(script);
        await brain.CompleteAsync("你是歌詞翻譯", "1. 朝", CancellationToken.None);
        Assert.Equal("你好", await brain.ThinkAsync("嗨", CancellationToken.None));
        var chat = ((JsonArray)script.Sent[1]["messages"]!).Skip(1).Select(m => m!["content"]!.ToString());
        Assert.Equal(["嗨"], chat);
    }

    [Fact]
    public async Task OpenAI相容_HTTP錯誤與200裡的錯誤都往外丟()
    {
        var failed = new Script(() => Json("""{"error":{"message":"bad key"}}""", HttpStatusCode.Unauthorized));
        var brain = OpenAi(failed);
        var e = await Assert.ThrowsAnyAsync<Exception>(() => brain.CompleteAsync("s", "t", CancellationToken.None));
        Assert.Equal("API 金鑰無效", brain.Explain(e));

        var hidden = new Script(() => Json("""{"error":{"code":429,"message":"slow down"}}"""));
        var other = OpenAi(hidden);
        var limit = await Assert.ThrowsAnyAsync<Exception>(() => other.CompleteAsync("s", "t", CancellationToken.None));
        Assert.Equal("用量已達上限，請稍後再試", other.Explain(limit));
    }

    /// <summary>SDK 可能送字串，也可能送區塊陣列：都轉成不跳脫中文的 JSON 文字來找。</summary>
    private static string Raw(JsonNode? node) =>
        node?.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "";

    [Fact]
    public async Task Claude_送系統說明與一段文字_不帶工具()
    {
        var script = new Script(() => ClaudeReply("1. 早晨"));
        Assert.Equal("1. 早晨", await Claude(script).CompleteAsync("你是歌詞翻譯", "1. 朝", CancellationToken.None));

        var sent = script.Sent[0];
        Assert.Equal("/v1/messages", script.Paths[0]);
        Assert.Null(sent["tools"]);
        Assert.Null(sent["stream"]);
        Assert.Contains("你是歌詞翻譯", Raw(sent["system"]));
        var messages = (JsonArray)sent["messages"]!;
        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]!.ToString());
        Assert.Contains("1. 朝", Raw(messages[0]!["content"]));
    }

    [Fact]
    public async Task Claude_拒絕回答是空字串_也不記進對話()
    {
        var script = new Script(() => ClaudeReply("", "refusal"), () => ClaudeReply("你好"));
        var brain = Claude(script);
        Assert.Equal("", await brain.CompleteAsync("s", "t", CancellationToken.None));
        Assert.Equal("你好", await brain.ThinkAsync("嗨", CancellationToken.None));
        Assert.Single((JsonArray)script.Sent[1]["messages"]!);
    }

    [Fact]
    public void 編號超過句數_某一句被拆成兩行_不採用()
    {
        // 三句歌詞，AI 把第二句拆成兩行、編到 4：1…3 都在，但第三句其實對到的是第二句的後半
        Assert.Null(AiTranslation.Parse("1. 甲\n2. 乙的前半\n3. 乙的後半\n4. 丙", 3));
        Assert.NotNull(AiTranslation.Parse("1. 甲\n2. 乙\n3. 丙", 3));
    }
}

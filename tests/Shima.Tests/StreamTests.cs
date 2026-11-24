// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>OpenAI 相容串流（SSE）的解析：不經過網路，一行一行餵。</summary>
public class ChatStreamTests
{
    private static ChatStream Feed(params string[] lines)
    {
        var stream = new ChatStream();
        foreach (var line in lines) stream.Feed(line);
        stream.End();
        return stream;
    }

    private static string Delta(string delta, string? finish = null) =>
        "data: " + new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = JsonNode.Parse(delta), ["finish_reason"] = finish }) }.ToJsonString();

    private static JsonObject Arguments(JsonObject message, int i) =>
        (JsonObject)JsonNode.Parse(message["tool_calls"]![i]!["function"]!["arguments"]!.ToString())!;

    [Fact]
    public void 每一塊都帶的空字串欄位不收進訊息()
    {
        var stream = new ChatStream();
        stream.Feed(Delta("""{"role":"assistant","content":"好","refusal":"","reasoning_content":""}"""));
        stream.Feed(Delta("""{"content":"的","refusal":""}"""));
        stream.Feed(Delta("{}", "stop"));
        var message = stream.Message();
        Assert.Equal("好的", message["content"]!.ToString());
        Assert.False(message.ContainsKey("refusal"));
        Assert.False(message.ContainsKey("reasoning_content"));
    }

    [Fact]
    public void 內容一段一段來_拼回全文_文字變多才回報()
    {
        var stream = new ChatStream();
        Assert.False(stream.Feed(Delta("""{"role":"assistant","content":""}""")));
        Assert.True(stream.Feed(Delta("""{"content":"你"}""")));
        Assert.False(stream.Feed(""));
        Assert.True(stream.Feed(Delta("""{"content":"好，"}""")));
        Assert.True(stream.Feed(Delta("""{"content":"Shima"}""")));
        Assert.False(stream.Finished);
        Assert.False(stream.Feed(Delta("{}", "stop")));
        Assert.True(stream.Finished);
        Assert.False(stream.Feed("data: [DONE]"));
        Assert.True(stream.Done);

        Assert.Equal("你好，Shima", stream.Text);
        var message = stream.Message();
        Assert.Equal("assistant", message["role"]!.ToString());
        Assert.Equal("你好，Shima", message["content"]!.ToString());
        Assert.Null(message["tool_calls"]);
    }

    [Fact]
    public void 工具參數分好幾段送來_拼回完整的_JSON()
    {
        var stream = Feed(
            Delta("""{"role":"assistant","content":null,"tool_calls":[{"index":0,"id":"call_a","type":"function","function":{"name":"music","arguments":""}}]}"""),
            Delta("""{"tool_calls":[{"index":0,"function":{"arguments":"{\"ac"}}]}"""),
            Delta("""{"tool_calls":[{"index":0,"function":{"arguments":"tion\":"}}]}"""),
            Delta("""{"tool_calls":[{"index":0,"function":{"arguments":"\"next\"}"}}]}"""),
            Delta("{}", "tool_calls"),
            "data: [DONE]");

        Assert.True(stream.Finished);
        var message = stream.Message();
        Assert.Null(message["content"]);   // 跟非串流的回應一樣：只有工具呼叫時 content 是 null
        var call = message["tool_calls"]![0]!;
        Assert.Equal("call_a", call["id"]!.ToString());
        Assert.Equal("function", call["type"]!.ToString());
        Assert.Equal("music", call["function"]!["name"]!.ToString());
        Assert.Equal("next", Arguments(message, 0)["action"]!.ToString());
        Assert.Null(call["index"]);   // index 是串流用的，不放回對話
    }

    [Fact]
    public void 好幾個工具呼叫_用_index_分開_交錯送來也不會混在一起()
    {
        var stream = Feed(
            Delta("""{"tool_calls":[{"index":0,"id":"call_0","function":{"name":"todo","arguments":"{\"action\":"}}]}"""),
            Delta("""{"tool_calls":[{"index":1,"id":"call_1","function":{"name":"timer","arguments":"{\"action\":"}}]}"""),
            Delta("""{"tool_calls":[{"index":1,"function":{"arguments":"\"start\"}"}},{"index":0,"function":{"arguments":"\"list\"}"}}]}"""),
            Delta("{}", "tool_calls"));

        var message = stream.Message();
        Assert.Equal(2, ((JsonArray)message["tool_calls"]!).Count);
        Assert.Equal("todo", message["tool_calls"]![0]!["function"]!["name"]!.ToString());
        Assert.Equal("list", Arguments(message, 0)["action"]!.ToString());
        Assert.Equal("timer", message["tool_calls"]![1]!["function"]!["name"]!.ToString());
        Assert.Equal("start", Arguments(message, 1)["action"]!.ToString());
    }

    [Fact]
    public void 沒有_index_的工具呼叫_用_id_對_參數一次給整段物件也可以()
    {
        var stream = Feed(
            Delta("""{"tool_calls":[{"id":"g1","type":"function","function":{"name":"weather","arguments":{"location":"Taipei"}},"extra_content":{"google":{"thought_signature":"sig"}}}]}"""),
            Delta("""{"tool_calls":[{"id":"g2","type":"function","function":{"name":"datetime","arguments":"{}"}}]}"""),
            Delta("{}", "stop"));   // 有幾家用了工具也回 stop：要看有沒有 tool_calls，不看 finish_reason

        var message = stream.Message();
        Assert.Equal(["g1", "g2"], ((JsonArray)message["tool_calls"]!).Select(c => c!["id"]!.ToString()));
        Assert.Equal("Taipei", Arguments(message, 0)["location"]!.ToString());
        Assert.Equal("sig", message["tool_calls"]![0]!["extra_content"]!["google"]!["thought_signature"]!.ToString());
    }

    [Fact]
    public void 沒有_index_也沒有_id的後續片段_接在上一個後面_沒有_id_就補一個()
    {
        var stream = Feed(
            Delta("""{"tool_calls":[{"function":{"name":"calc","arguments":"{\"expression\":"}}]}"""),
            Delta("""{"tool_calls":[{"function":{"arguments":"\"1+1\"}"}}]}"""),
            Delta("""{"tool_calls":[{"function":{"name":"datetime"}}]}"""),
            Delta("{}", "tool_calls"));

        var message = stream.Message();
        Assert.Equal(2, ((JsonArray)message["tool_calls"]!).Count);
        Assert.Equal("1+1", Arguments(message, 0)["expression"]!.ToString());
        Assert.Equal("call_0", message["tool_calls"]![0]!["id"]!.ToString());
        Assert.Equal("{}", message["tool_calls"]![1]!["function"]!["arguments"]!.ToString());
    }

    [Fact]
    public void 名字每一段都重送_不會接成兩次()
    {
        var stream = Feed(
            Delta("""{"tool_calls":[{"index":0,"id":"c","function":{"name":"music","arguments":"{\"action\":"}}]}"""),
            Delta("""{"tool_calls":[{"index":0,"id":"c","function":{"name":"music","arguments":"\"pause\"}"}}]}"""),
            Delta("{}", "tool_calls"));
        Assert.Equal("music", stream.Message()["tool_calls"]![0]!["function"]!["name"]!.ToString());
        Assert.Equal("pause", Arguments(stream.Message(), 0)["action"]!.ToString());
    }

    [Fact]
    public async Task DONE_之後的東西不讀()
    {
        var reader = new StringReader(Delta("""{"content":"好"}""") + "\n\ndata: [DONE]\n\n" + Delta("""{"content":"多出來的"}""") + "\n\n");
        var stream = await ChatStream.ReadAsync(reader, null, CancellationToken.None);
        Assert.Equal("好", stream.Text);
        Assert.True(stream.Finished);
    }

    [Fact]
    public void 只有用量的區塊_略過()
    {
        var stream = Feed(
            Delta("""{"content":"嗨"}"""),
            Delta("{}", "stop"),
            """data: {"id":"x","choices":[],"usage":{"prompt_tokens":3,"completion_tokens":1}}""",
            """data: {"usage":{"total_tokens":4}}""",
            "data: [DONE]");
        Assert.Equal("嗨", stream.Text);
        Assert.Null(stream.Error);
        Assert.Null(stream.Message()["usage"]);
    }

    [Fact]
    public void 空行_註解_event_id_retry_行_data_後面沒有空格_都讀得懂()
    {
        var stream = Feed(
            ": OPENROUTER PROCESSING",
            "",
            "retry: 3000",
            "event: message",
            "id: 7",
            """data:{"choices":[{"delta":{"content":"沒"}}]}""",
            "",
            "",
            ":keep-alive",
            """data:   {"choices":[{"delta":{"content":"空格"}}]}""" + "\r",
            "\r",
            """data: {"choices":[{"delta":{},"finish_reason":"stop"}]}""",
            "data:[DONE]");
        Assert.Equal("沒空格", stream.Text);
        Assert.True(stream.Done);
    }

    [Fact]
    public void 格式壞掉的行_跳過_後面的照常讀()
    {
        var stream = Feed(
            "data: {\"choices\":[{\"delta\":{\"content\":\"壞",
            Delta("""{"content":"好的"}"""),
            "data: hello",
            "",
            "data: [1,2,3]",
            "",
            "data: {\"choices\":\"不是陣列\"}",
            "data: {\"choices\":[null]}",
            "data: {\"choices\":[{\"delta\":\"不是物件\"}]}",
            "garbage line",
            Delta("""{"content":"！"}""", "stop"));
        Assert.Equal("好的！", stream.Text);
        Assert.True(stream.Finished);
    }

    [Fact]
    public void 一個事件分成好幾行_data_拼起來讀()
    {
        var stream = Feed(
            """data: {"choices":[{"delta":""",
            """data: {"content":"分行"}}]}""",
            "",
            "data: [DONE]");
        Assert.Equal("分行", stream.Text);
    }

    [Fact]
    public void 沒有_DONE_也沒有_finish_reason_就結束_是中途斷線()
    {
        var stream = Feed(Delta("""{"content":"講到一半"}"""));
        Assert.Equal("講到一半", stream.Text);
        Assert.False(stream.Finished);
    }

    [Fact]
    public void 串流裡夾著錯誤()
    {
        var stream = Feed("""data: {"error":{"message":"Model is overloaded","code":503}}""", "");
        Assert.NotNull(stream.Error);
        Assert.False(stream.Started);
    }

    [Fact]
    public void 思考的內容不算回答_但留著放回對話_content_是陣列也讀得懂()
    {
        var stream = Feed(
            Delta("""{"reasoning_content":"使用者想"}"""),
            Delta("""{"reasoning_content":"切歌"}"""),
            Delta("""{"content":[{"type":"text","text":"好"},{"type":"thinking","thinking":"不算"},{"text":"的"}]}"""),
            Delta("""{"tool_calls":[{"index":0,"id":"c","function":{"name":"music","arguments":"{}"}}]}""", "tool_calls"));
        Assert.Equal("好的", stream.Text);
        Assert.Equal("使用者想切歌", stream.Message()["reasoning_content"]!.ToString());
    }

    [Fact]
    public void 服務不理_stream_直接回整段_JSON_也讀得懂()
    {
        var json = """
            {
              "choices": [ { "index": 0, "message": { "role": "assistant", "content": "整段" }, "finish_reason": "stop" } ]
            }
            """;
        var stream = Feed(json.Split('\n'));
        Assert.Equal("整段", stream.Text);
        Assert.True(stream.Finished);

        var calls = Feed("""{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"a","type":"function","function":{"name":"music","arguments":"{}"}},{"id":"b","type":"function","function":{"name":"lyrics","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}""");
        Assert.Equal(["music", "lyrics"], ((JsonArray)calls.Message()["tool_calls"]!).Select(c => c!["function"]!["name"]!.ToString()));
    }

    [Fact]
    public async Task 太久沒有新的一行_當作斷線()
    {
        var reader = new StreamReader(new Stall(Encoding.UTF8.GetBytes(Delta("""{"content":"等"}""") + "\n\n")));
        await Assert.ThrowsAsync<TimeoutException>(() => ChatStream.ReadAsync(reader, null, CancellationToken.None, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>送完一段之後就卡住不動（直到被取消）。</summary>
    private sealed class Stall(byte[] data) : MemoryStream(data)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position < Length) return base.Read(buffer.Span);
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
    }
}

/// <summary>串流接上 OpenAiBrain 之後：工具迴圈、退回非串流、取消與斷線時收回這一輪。</summary>
public class OpenAiStreamTests
{
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

    private static string Chunk(string delta, string? finish = null) =>
        "data: " + new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = JsonNode.Parse(delta), ["finish_reason"] = finish }) }.ToJsonString() + "\n\n";

    private static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private static HttpResponseMessage Words(params string[] words) =>
        Sse(string.Concat(words.Select(w => Chunk(new JsonObject { ["content"] = w }.ToJsonString()))) + Chunk("{}", "stop") + "data: [DONE]\n\n");

    private static HttpResponseMessage UseTool(string say = "") => Sse(
        (say.Length > 0 ? Chunk(new JsonObject { ["content"] = say }.ToJsonString()) : "") +
        Chunk("""{"reasoning_content":"要切歌"}""") +
        Chunk("""{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"music","arguments":"{\"action\""}}]}""") +
        Chunk("""{"tool_calls":[{"index":0,"function":{"arguments":":\"next\"}"}}]}""") +
        Chunk("{}", "tool_calls") + "data: [DONE]\n\n");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static OpenAiBrain Brain(Script script, Brain.Run? run = null) =>
        new("https://example.invalid/v1", "", "m", "zh-TW", run ?? ((_, _) => "好了")) { Http = new HttpClient(script) };

    private static string[] Roles(JsonObject body) =>
        ((JsonArray)body["messages"]!).Skip(1).Select(m => m!["role"]!.ToString()).ToArray();

    [Fact]
    public async Task 邊產生邊收到全文_用了工具的話新的一輪從頭累積()
    {
        var script = new Script(() => UseTool("我查一下"), () => Words("換", "好", "了"), () => Words("不客氣"));
        var brain = Brain(script);
        var seen = new List<string>();

        Assert.Equal("換好了", await brain.ThinkAsync("下一首", CancellationToken.None, seen.Add));
        Assert.Equal(["我查一下", "換", "換好", "換好了"], seen);
        Assert.True(script.Sent[0]["stream"]!.GetValue<bool>());
        Assert.Null(script.Sent[0]["stream_options"]);   // 有幾家看到不認得的欄位會回 400

        // 工具那一輪拼回來的訊息放回對話：工具呼叫、參數、思考內容都在
        var echo = script.Sent[1]["messages"]![2]!;
        Assert.Equal("assistant", echo["role"]!.ToString());
        Assert.Equal("我查一下", echo["content"]!.ToString());
        Assert.Equal("要切歌", echo["reasoning_content"]!.ToString());
        Assert.Equal("""{"action":"next"}""", echo["tool_calls"]![0]!["function"]!["arguments"]!.ToString());
        Assert.Equal("call_1", script.Sent[1]["messages"]![3]!["tool_call_id"]!.ToString());

        await brain.ThinkAsync("謝謝", CancellationToken.None, _ => { });
        Assert.Equal(["user", "assistant", "tool", "assistant", "user"], Roles(script.Sent[2]));
    }

    [Fact]
    public async Task 沒給回呼就照舊不串流()
    {
        var script = new Script(() => Json("""{"choices":[{"message":{"role":"assistant","content":"好"}}]}"""));
        Assert.Equal("好", await Brain(script).ThinkAsync("嗨", CancellationToken.None));
        Assert.Null(script.Sent[0]["stream"]);
    }

    [Fact]
    public async Task 不支援串流_回_400_就改用一般請求_之後也不再串流()
    {
        var script = new Script(
            () => Json("""{"error":{"message":"stream is not supported"}}""", HttpStatusCode.BadRequest),
            () => Json("""{"choices":[{"message":{"role":"assistant","content":"好"}}]}"""),
            () => Json("""{"choices":[{"message":{"role":"assistant","content":"再見"}}]}"""));
        var brain = Brain(script);

        Assert.Equal("好", await brain.ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.Null(script.Sent[1]["stream"]);
        Assert.NotNull(script.Sent[1]["tools"]);   // 錯誤說的是串流：工具留著
        Assert.Equal("再見", await brain.ThinkAsync("掰", CancellationToken.None, _ => { }));
        Assert.Null(script.Sent[2]["stream"]);
    }

    [Fact]
    public async Task 沒說原因的_400_先拿掉工具_還是不行再關串流()
    {
        var script = new Script(
            () => Json("""{"error":"bad request"}""", HttpStatusCode.BadRequest),
            () => Json("""{"error":"bad request"}""", HttpStatusCode.BadRequest),
            () => Json("""{"choices":[{"message":{"role":"assistant","content":"好"}}]}"""));

        Assert.Equal("好", await Brain(script).ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.True(script.Sent[1]["stream"]!.GetValue<bool>());
        Assert.Null(script.Sent[1]["tools"]);
        Assert.Null(script.Sent[2]["stream"]);
    }

    [Fact]
    public async Task 金鑰錯誤_用量上限_不重試()
    {
        var script = new Script(() => Json("""{"error":"invalid api key"}""", HttpStatusCode.BadRequest));
        var failed = await Assert.ThrowsAnyAsync<Exception>(() => Brain(script).ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.StartsWith("HTTP 400", failed.Message);

        var limited = new Script(() => Json("""{"error":"slow down"}""", HttpStatusCode.TooManyRequests));
        await Assert.ThrowsAnyAsync<Exception>(() => Brain(limited).ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.Single(limited.Sent);
    }

    [Fact]
    public async Task 要了串流卻回一般的_JSON_照樣讀_不多發請求()
    {
        var script = new Script(() => Json("""{"choices":[{"message":{"role":"assistant","content":"整段"}}]}"""));
        var seen = new List<string>();
        Assert.Equal("整段", await Brain(script).ThinkAsync("嗨", CancellationToken.None, seen.Add));
        Assert.Single(script.Sent);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task 串流裡的錯誤_還沒開始講就退回一般請求()
    {
        var script = new Script(
            () => Sse("""data: {"error":{"message":"streaming not available for this model","code":400}}""" + "\n\n"),
            () => Json("""{"choices":[{"message":{"role":"assistant","content":"好"}}]}"""));
        Assert.Equal("好", await Brain(script).ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.Null(script.Sent[1]["stream"]);
    }

    [Fact]
    public async Task 串流裡的錯誤_伺服器忙_照常丟出去並收回這一輪()
    {
        var script = new Script(
            () => Sse(Chunk("""{"content":"講"}""") + """data: {"error":{"message":"overloaded","code":503}}""" + "\n\n"),
            () => Words("在"));
        var brain = Brain(script);
        var failed = await Assert.ThrowsAnyAsync<Exception>(() => brain.ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.StartsWith("HTTP 503", failed.Message);
        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    [Fact]
    public async Task 中途斷線_丟出連線錯誤_整輪收回()
    {
        var cut = Encoding.UTF8.GetBytes(Chunk("""{"content":"講到"}""") + Chunk("""{"content":"一半"}"""));
        var script = new Script(
            () => UseTool(),
            () => new(HttpStatusCode.OK) { Content = new StreamContent(new Breaking(cut)) { Headers = { ContentType = new("text/event-stream") } } },
            () => Words("在"));
        var brain = Brain(script);
        var seen = new List<string>();

        await Assert.ThrowsAsync<HttpRequestException>(() => brain.ThinkAsync("下一首", CancellationToken.None, seen.Add));
        Assert.Equal("講到一半", seen[^1]);
        Assert.Equal(["user", "assistant", "tool"], Roles(script.Sent[1]));

        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[2]));
    }

    [Fact]
    public async Task 串流乾淨地結束_卻沒有結尾_也算斷線()
    {
        var script = new Script(() => Sse(Chunk("""{"content":"講到一半"}""")), () => Words("在"));
        var brain = Brain(script);
        await Assert.ThrowsAsync<HttpRequestException>(() => brain.ThinkAsync("嗨", CancellationToken.None, _ => { }));
        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    [Fact]
    public async Task 取消會中斷串流_這一輪收回()
    {
        using var stop = new CancellationTokenSource();
        var script = new Script(() => new(HttpStatusCode.OK) { Content = new StreamContent(new Endless()) { Headers = { ContentType = new("text/event-stream") } } }, () => Words("在"));
        var brain = Brain(script);

        // 收到第一個字就按停止；串流本身永遠不會結束，只有取消停得下來
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => brain.ThinkAsync("講個故事", stop.Token, _ => stop.Cancel()));
        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    /// <summary>送完資料後連線被切斷。</summary>
    private sealed class Breaking(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            Position < Length ? new(base.Read(buffer.Span)) : throw new IOException("connection reset");
        public override int Read(byte[] buffer, int offset, int count) =>
            Position < Length ? base.Read(buffer, offset, count) : throw new IOException("connection reset");
    }

    /// <summary>一直送同一個字，永遠不結束；等的時候看得到取消。</summary>
    private sealed class Endless : Stream
    {
        private readonly byte[] _chunk = Encoding.UTF8.GetBytes(Chunk("""{"content":"從前"}"""));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            await Task.Delay(5, token);
            int n = Math.Min(buffer.Length, _chunk.Length);
            _chunk.AsSpan(0, n).CopyTo(buffer.Span);
            return n;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().Result;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Claude 走 SDK 的串流：用假的 HttpClient 回事先寫好的 SSE，不花錢、不需要金鑰。</summary>
public class ClaudeStreamTests
{
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

    private static string Event(string json) => $"event: {JsonNode.Parse(json)!["type"]}\ndata: {json}\n\n";

    private const string Start = """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}""";

    private static string Text(int index, params string[] pieces) =>
        Event($$$"""{"type":"content_block_start","index":{{{index}}},"content_block":{"type":"text","text":""}}""") +
        string.Concat(pieces.Select(p => Event(new JsonObject { ["type"] = "content_block_delta", ["index"] = index, ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = p } }.ToJsonString()))) +
        Event($$"""{"type":"content_block_stop","index":{{index}}}""");

    private static string End(string reason) =>
        Event($$$"""{"type":"message_delta","delta":{"stop_reason":"{{{reason}}}","stop_sequence":null},"usage":{"output_tokens":20}}""") + Event("""{"type":"message_stop"}""");

    private static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private static HttpResponseMessage UseTool() => Sse(Event(Start) + Text(0, "我查", "一下") +
        Event("""{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"music","input":{}}}""") +
        Event("""{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"action\": \"ne"}}""") +
        Event("""{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"xt\"}"}}""") +
        Event("""{"type":"content_block_stop","index":1}""") + End("tool_use"));

    private static HttpResponseMessage Say(params string[] pieces) => Sse(Event(Start) + Text(0, pieces) + End("end_turn"));

    private static ClaudeBrain Brain(Script script, Brain.Run run) => new("test-key", "claude-haiku-5-5", "zh-TW", run) { Http = new HttpClient(script) };

    private static string[] Roles(JsonObject body) => ((JsonArray)body["messages"]!).Select(m => m!["role"]!.ToString()).ToArray();

    [Fact]
    public async Task 邊產生邊收到全文_工具照常執行_最後回答的是第二輪()
    {
        var script = new Script(UseTool, () => Say("換好", "了"));
        var used = new List<string>();
        var brain = Brain(script, (tool, arguments) => { used.Add($"{tool}:{arguments["action"]}"); return "已切到下一首"; });
        var seen = new List<string>();

        Assert.Equal("換好了", await brain.ThinkAsync("下一首", CancellationToken.None, seen.Add));
        Assert.Equal(["我查", "我查一下", "換好", "換好了"], seen);
        Assert.Equal(["music:next"], used);
        Assert.True(script.Sent[0]["stream"]!.GetValue<bool>());

        // 第二輪送出去的對話：助手那則帶著拼好的工具呼叫，後面是工具結果
        var sent = (JsonArray)script.Sent[1]["messages"]!;
        Assert.Equal(["user", "assistant", "user"], Roles(script.Sent[1]));
        var use = ((JsonArray)sent[1]!["content"]!).Single(b => b!["type"]!.ToString() == "tool_use")!;
        Assert.Equal("toolu_1", use["id"]!.ToString());
        Assert.Equal("next", use["input"]!["action"]!.ToString());
        Assert.Equal("tool_result", sent[2]!["content"]![0]!["type"]!.ToString());
    }

    [Fact]
    public async Task 思考區塊與簽章拼回去_放回對話_思考內容不顯示()
    {
        var thinking = Sse(Event(Start) +
            Event("""{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""") +
            Event("""{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"使用者想"}}""") +
            Event("""{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"切歌"}}""") +
            Event("""{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-abc"}}""") +
            Event("""{"type":"content_block_stop","index":0}""") +
            Text(1, "好") +
            Event("""{"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"toolu_9","name":"music","input":{}}}""") +
            Event("""{"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"{\"action\":\"next\"}"}}""") +
            Event("""{"type":"content_block_stop","index":2}""") + End("tool_use"));
        var script = new Script(() => thinking, () => Say("換好了"));
        var seen = new List<string>();

        await Brain(script, (_, _) => "已切到下一首").ThinkAsync("下一首", CancellationToken.None, seen.Add);
        Assert.Equal(["好", "換好了"], seen);   // 用工具前的那句換成最後的回答；思考內容不顯示

        // 簽章沒拼回去的話，第二輪會被 API 以簽章無效拒絕
        var echo = (JsonArray)script.Sent[1]["messages"]![1]!["content"]!;
        Assert.Equal("thinking", echo[0]!["type"]!.ToString());
        Assert.Equal("使用者想切歌", echo[0]!["thinking"]!.ToString());
        Assert.Equal("sig-abc", echo[0]!["signature"]!.ToString());
        Assert.Equal("text", echo[1]!["type"]!.ToString());
        Assert.Equal("toolu_9", echo[2]!["id"]!.ToString());
    }

    [Fact]
    public async Task 串流被拒_改用一般請求()
    {
        var script = new Script(
            () => new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"type":"error","error":{"type":"invalid_request_error","message":"streaming unsupported"}}""", Encoding.UTF8, "application/json") },
            () => new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"msg_2","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"好"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}""", Encoding.UTF8, "application/json") });

        Assert.Equal("好", await Brain(script, (_, _) => "").ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.Equal(2, script.Sent.Count);
        Assert.Null(script.Sent[1]["stream"]);
    }

    [Fact]
    public async Task 中途斷線_丟出連線錯誤_整輪收回()
    {
        var script = new Script(UseTool, () => Sse(Event(Start) + Text(0, "講到一半")), () => Say("在"));
        var brain = Brain(script, (_, _) => "好了");

        await Assert.ThrowsAsync<HttpRequestException>(() => brain.ThinkAsync("下一首", CancellationToken.None, _ => { }));
        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[2]));
    }

    [Fact]
    public async Task 串流中途的錯誤事件_丟出來_整輪收回()
    {
        var script = new Script(() => Sse(Event(Start) + Event("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""")), () => Say("在"));
        var brain = Brain(script, (_, _) => "");

        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => brain.ThinkAsync("嗨", CancellationToken.None, _ => { }));
        Assert.Contains("overloaded", failed.Message);
        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }

    [Fact]
    public async Task 取消會中斷串流()
    {
        using var stop = new CancellationTokenSource();
        var script = new Script(() => Say("一", "二", "三"), () => Say("在"));
        var brain = Brain(script, (_, _) => "");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => brain.ThinkAsync("數數", stop.Token, _ => stop.Cancel()));
        await brain.ThinkAsync("在嗎", CancellationToken.None, _ => { });
        Assert.Equal(["user"], Roles(script.Sent[1]));
    }
}

public class TrickleTests
{
    [Fact]
    public async Task 來得很密_只顯示最新的_次數有節流()
    {
        var shown = new List<string>();
        var done = new TaskCompletionSource();
        var trickle = new Trickle(TimeSpan.FromMilliseconds(40), work => { lock (shown) work(); }, text => { shown.Add(text); if (text == "199") done.TrySetResult(); });

        for (int i = 0; i < 200; i++) trickle.Put(i.ToString());
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (shown)
        {
            Assert.Equal("199", shown[^1]);
            Assert.True(shown.Count <= 3, $"顯示了 {shown.Count} 次");
        }
    }

    [Fact]
    public async Task 關掉或丟掉之後_還沒顯示的不會出現()
    {
        var shown = new List<string>();
        // 間隔放長：機器忙的時候「放進去」與「關掉」之間可能被排到後面等上幾十毫秒，
        // 間隔只有 20 毫秒的話那一段就先顯示出來了，這個測試會時好時壞
        var gap = TimeSpan.FromMilliseconds(400);
        var closed = new Trickle(gap, work => work(), text => { lock (shown) shown.Add(text); });
        closed.Put("晚到的片段");
        closed.Close();
        closed.Put("關了還送");

        var dropped = new Trickle(gap, work => work(), text => { lock (shown) shown.Add(text); });
        dropped.Put("被整段答案取代");
        dropped.Drop();

        await Task.Delay(gap * 2.5);
        lock (shown) Assert.Empty(shown);

        // 丟掉之後再來的照常顯示（語音模式下一輪的回答）：等到出現為止，不賭一個固定的時間
        dropped.Put("下一輪");
        for (var until = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < until; await Task.Delay(20))
            lock (shown) if (shown.Count > 0) break;
        lock (shown) Assert.Equal(["下一輪"], shown);
    }
}

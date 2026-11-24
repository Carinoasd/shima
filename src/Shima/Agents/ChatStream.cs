// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>
/// OpenAI 相容的 Chat Completions 用 <c>stream: true</c> 回的 SSE：一行一行讀進來，拼回文字與工具呼叫。
/// 同一份程式要接十幾家服務和本機的 Ollama，各家的細節不太一樣，所以盡量寬容：
/// <c>data:</c> 後面不一定有空格、註解與 event／id 行略過、看不懂的行跳過、
/// 工具呼叫沒有 index 就用 id 對、arguments 可能分好幾段也可能一次給整段，
/// 甚至有的服務不理 <c>stream: true</c> 直接回整段 JSON，也照樣讀得懂。
/// </summary>
public sealed class ChatStream
{
    private readonly StringBuilder _text = new();
    private readonly List<JsonObject> _calls = [];
    private readonly List<StringBuilder> _arguments = [];
    private readonly Dictionary<int, int> _byIndex = [];
    // 回答以外的欄位（reasoning_content 之類）：工具那一輪要原樣放回對話，DeepSeek、Kimi 的思考模型需要
    private readonly Dictionary<string, StringBuilder> _extraText = [];
    private readonly JsonObject _extra = [];
    private readonly List<string> _pending = [];
    private readonly StringBuilder _raw = new();

    /// <summary>目前為止的回答文字。</summary>
    public string Text => _text.ToString();
    /// <summary>收到 [DONE] 或 finish_reason：回應是完整的。兩個都沒有就結束了，表示中途斷線。</summary>
    public bool Finished { get; private set; }
    /// <summary>收到 [DONE]，後面不用再讀。</summary>
    public bool Done { get; private set; }
    /// <summary>串流裡夾著的錯誤（HTTP 狀態仍然是 200）。</summary>
    public JsonNode? Error { get; private set; }
    /// <summary>已經收到文字或工具呼叫。</summary>
    public bool Started => _text.Length > 0 || _calls.Count > 0;

    /// <summary>讀進一行（不含換行）。回答的文字有變多就回傳 true。</summary>
    public bool Feed(string line)
    {
        line = line.TrimEnd('\r');
        if (line.Length == 0) return Dispatch();   // 一個事件結束
        if (line[0] == ':') return false;          // 註解，常用來保持連線
        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            var data = line[5..].TrimStart();
            if (data.Trim() == "[DONE]") { _pending.Clear(); Finished = Done = true; return false; }
            if (_pending.Count == 0 && Parse(data) is { } node) return Chunk(node);
            _pending.Add(data);
            // 照規格，一個事件可以分成好幾行 data；拼起來看得懂就處理
            if (Parse(string.Join("\n", _pending)) is { } joined) { _pending.Clear(); return Chunk(joined); }
            // 前面那行是壞的、這行自己是好的（有的服務事件之間不空行）：丟掉壞的
            if (_pending.Count > 1 && Parse(data) is { } alone) { _pending.Clear(); return Chunk(alone); }
            return false;
        }
        if (line.StartsWith("event:", StringComparison.Ordinal) || line.StartsWith("id:", StringComparison.Ordinal) || line.StartsWith("retry:", StringComparison.Ordinal)) return false;
        // 不是 SSE 的行：服務可能根本沒有串流，回的是一般的 JSON，先收著，最後再看
        _raw.Append(line).Append('\n');
        return false;
    }

    /// <summary>讀完了。回答的文字有變多就回傳 true。</summary>
    public bool End()
    {
        bool changed = Dispatch();
        if (_raw.Length > 0 && !Started && Error == null && Parse(_raw.ToString()) is { } whole)
        {
            changed |= Chunk(whole);
            if (whole["choices"] is JsonArray { Count: > 0 }) Finished = true;   // 整段一次回來的，就是完整的
        }
        _raw.Clear();
        return changed;
    }

    private bool Dispatch()
    {
        if (_pending.Count == 0) return false;
        var node = Parse(string.Join("\n", _pending));
        _pending.Clear();
        return node != null && Chunk(node);
    }

    private static JsonNode? Parse(string json)
    {
        if (json.Length == 0 || json.TrimStart() is not ['{', ..]) return null;
        try { return JsonNode.Parse(WithoutLoneSurrogates(json)); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>
    /// 字串裡孤立的 \ud83d 這種代理字元跳脫換成 U+FFFD。解析時看不出來，到取出字串（TryGetValue、ToString）才丟 InvalidOperationException，
    /// 一個 token 剛好把 emoji 切成兩半的服務就會讓整則回答失敗；成對的不動。
    /// </summary>
    private static string WithoutLoneSurrogates(string json)
    {
        if (!json.Contains("\\u", StringComparison.Ordinal)) return json;
        StringBuilder? fixedUp = null;
        int i = 0;
        while (i < json.Length)
        {
            // 其他的跳脫（\\、\"、\n…）整組跳過，免得「\\ud83d」（一個反斜線加字面的 ud83d）被當成跳脫
            if (json[i] != '\\' || i + 1 >= json.Length) { fixedUp?.Append(json[i]); i++; continue; }
            if (json[i + 1] != 'u' || !TryUnit(json, i + 2, out int unit)) { fixedUp?.Append(json, i, 2); i += 2; continue; }
            bool high = unit is >= 0xD800 and <= 0xDBFF, low = unit is >= 0xDC00 and <= 0xDFFF;
            if (high && i + 12 <= json.Length && json[i + 6] == '\\' && json[i + 7] == 'u' && TryUnit(json, i + 8, out int next) && next is >= 0xDC00 and <= 0xDFFF)
            {
                fixedUp?.Append(json, i, 12);
                i += 12;
            }
            else if (high || low)
            {
                fixedUp ??= new StringBuilder().Append(json, 0, i);
                fixedUp.Append("\\uFFFD");
                i += 6;
            }
            else { fixedUp?.Append(json, i, 6); i += 6; }
        }
        return fixedUp?.ToString() ?? json;
    }

    private static bool TryUnit(string s, int at, out int unit)
    {
        unit = 0;
        return at + 4 <= s.Length && int.TryParse(s.AsSpan(at, 4), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out unit);
    }

    private bool Chunk(JsonNode node)
    {
        if (node is not JsonObject chunk) return false;
        if (chunk["error"] is { } error) { Error = error.DeepClone(); return false; }
        // 只有用量、沒有 choices 的區塊（有的服務最後會多送一個）
        if (chunk["choices"] is not JsonArray { Count: > 0 } choices) return false;
        // choices 裡不是物件的項目（數字、陣列）直接略過：對它們取 ["index"] 會丟 InvalidOperationException
        var choice = choices.OfType<JsonObject>().FirstOrDefault(c => c["index"] is JsonValue v && v.TryGetValue(out int i) && i == 0) ?? choices[0] as JsonObject;
        if (choice?["finish_reason"] is JsonValue reason && reason.TryGetValue(out string? why) && why.Length > 0) Finished = true;
        // 正常是 delta；整段一次回來的是 message
        if ((choice?["delta"] ?? choice?["message"]) is not JsonObject delta) return false;

        int before = _text.Length;
        foreach (var (key, value) in delta)
        {
            switch (key)
            {
                case "role": break;
                case "content": AppendContent(value); break;
                case "tool_calls":
                    if (value is JsonArray calls) foreach (var call in calls) if (call is JsonObject c) Merge(c);
                    break;
                default:
                    if (value is JsonValue v && v.TryGetValue(out string? s))
                    {
                        // 有的服務每一塊都帶 "refusal": ""、"reasoning_content": ""：空的不收，免得之後原樣送回去
                        if (string.IsNullOrEmpty(s)) break;
                        if (!_extraText.TryGetValue(key, out var text)) _extraText[key] = text = new();
                        text.Append(s);
                    }
                    else if (value != null) _extra[key] = value.DeepClone();
                    break;
            }
        }
        return _text.Length > before;
    }

    /// <summary>content 通常是字串；有幾家會送成一段一段的陣列（[{type:"text",text:"…"}]）。</summary>
    private void AppendContent(JsonNode? content)
    {
        if (content is JsonValue v && v.TryGetValue(out string? s)) _text.Append(s);
        else if (content is JsonArray parts)
            foreach (var part in parts)
                if (part is JsonObject p && (p["type"]?.ToString() is null or "text") && p["text"] is JsonValue t && t.TryGetValue(out string? piece)) _text.Append(piece);
    }

    private void Merge(JsonObject call)
    {
        int? index = call["index"] is JsonValue iv && iv.TryGetValue(out int i) ? i : null;
        var id = call["id"] is JsonValue idv && idv.TryGetValue(out string? text) ? text : "";
        var function = call["function"] as JsonObject;
        var name = function?["name"] is JsonValue nv && nv.TryGetValue(out string? n) ? n : "";

        int slot;
        if (index is { } known && _byIndex.TryGetValue(known, out var found)) slot = found;
        else if (index == null && id.Length > 0 && _calls.FindIndex(c => c["id"]?.ToString() == id) is >= 0 and var same) slot = same;
        // 沒有 index 也沒有 id：是上一個的後續片段；但它自己帶了名字而上一個也已經有名字，就是新的一個
        else if (index == null && id.Length == 0 && _calls.Count > 0 && !(name.Length > 0 && _calls[^1]["function"]!["name"]!.ToString().Length > 0)) slot = _calls.Count - 1;
        else
        {
            slot = _calls.Count;
            _calls.Add(new JsonObject { ["id"] = "", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "" } });
            _arguments.Add(new());
            if (index is { } fresh) _byIndex[fresh] = slot;
        }

        var target = _calls[slot];
        if (id.Length > 0 && target["id"]!.ToString().Length == 0) target["id"] = id;
        if (call["type"] is JsonValue tv && tv.TryGetValue(out string? type) && type.Length > 0) target["type"] = type;
        // 名字只有第一段有；有的服務每一段都重送，不能接在一起
        if (name.Length > 0 && target["function"]!["name"]!.ToString().Length == 0) target["function"]!["name"] = name;
        switch (function?["arguments"])
        {
            case JsonValue av when av.TryGetValue(out string? piece): _arguments[slot].Append(piece); break;
            case JsonObject whole: _arguments[slot].Clear().Append(whole.ToJsonString()); break;   // 整段給成物件
        }
        // 其他欄位（例如 Gemini 的 extra_content，裡面有下一輪要帶回去的簽章）原樣留著
        foreach (var (key, value) in call)
            if (key is not ("index" or "id" or "type" or "function") && value != null) target[key] = value.DeepClone();
    }

    /// <summary>拼好的助手訊息，跟非串流回應的 choices[0].message 同一個樣子，可以直接放回對話。</summary>
    public JsonObject Message()
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = _text.Length > 0 ? _text.ToString() : null };
        foreach (var (key, text) in _extraText) message[key] = text.ToString();
        foreach (var (key, value) in _extra) message[key] = value?.DeepClone();
        if (_calls.Count > 0)
            message["tool_calls"] = new JsonArray(_calls.Select((call, i) =>
            {
                var copy = (JsonObject)call.DeepClone();
                if (copy["id"]!.ToString().Length == 0) copy["id"] = $"call_{i}";
                copy["function"]!["arguments"] = _arguments[i].Length > 0 ? _arguments[i].ToString() : "{}";
                return (JsonNode)copy;
            }).ToArray());
        return message;
    }

    /// <summary>
    /// 讀完整個回應。progress：文字變多時收到目前為止的全文。
    /// idle：這麼久都沒有新的一行就當作斷線（丟 TimeoutException），免得伺服器卡住時一直等下去。
    /// </summary>
    public static async Task<ChatStream> ReadAsync(TextReader reader, Action<string>? progress, CancellationToken stop, TimeSpan? idle = null)
    {
        var stream = new ChatStream();
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(stop);
        try
        {
            while (true)
            {
                if (idle is { } wait) quiet.CancelAfter(wait);
                if (await reader.ReadLineAsync(quiet.Token) is not { } line) break;
                if (stream.Feed(line)) progress?.Invoke(stream.Text);
                if (stream.Done) break;
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { throw new TimeoutException("串流太久沒有新的資料"); }
        if (stream.End()) progress?.Invoke(stream.Text);
        return stream;
    }
}

/// <summary>
/// 串流的文字來得很密：背景執行緒一直交最新的全文進來，畫面最多每隔 every 才換一次，
/// 中間來的只留最新的那份。post：把工作排到畫面的執行緒；show 一定在那裡執行。
/// </summary>
public sealed class Trickle(TimeSpan every, Action<Action> post, Action<string> show)
{
    private string? _latest;
    private int _scheduled;
    private volatile bool _closed;

    /// <summary>任何執行緒都可以呼叫。</summary>
    public void Put(string text)
    {
        if (_closed) return;
        Volatile.Write(ref _latest, text);
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
            _ = Task.Delay(every).ContinueWith(_ => post(Flush), TaskScheduler.Default);
    }

    private void Flush()
    {
        // 先放開排程再拿文字：這中間進來的會再排一次，不會漏掉
        Volatile.Write(ref _scheduled, 0);
        if (Interlocked.Exchange(ref _latest, null) is { } text && !_closed) show(text);
    }

    /// <summary>丟掉還沒顯示的那份（整段答案要顯示了，晚到的片段不能蓋掉它）。</summary>
    public void Drop() => Interlocked.Exchange(ref _latest, null);

    /// <summary>不再顯示任何東西。要在畫面的執行緒呼叫，之後排到的 Flush 才看得到。</summary>
    public void Close()
    {
        _closed = true;
        Drop();
    }
}

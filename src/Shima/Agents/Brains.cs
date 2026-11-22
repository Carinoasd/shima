// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Shima.Agents;

/// <summary>回答問題的那個 AI。一個物件就是一段對話，會記得前面聊過什麼。</summary>
public interface IBrain
{
    Task<string> ThinkAsync(string said, CancellationToken stop);
    /// <summary>出錯時給人看的一句話。</summary>
    string Explain(Exception e);
    /// <summary>這把金鑰能用的模型。</summary>
    Task<List<string>> ModelsAsync();
}

/// <summary>可以接的 AI 服務。</summary>
/// <param name="BaseUrl">OpenAI 相容的 API 位址；Claude 走官方 SDK 所以是空的。</param>
/// <param name="NeedsKey">本機的 Ollama 不用金鑰。</param>
public sealed record AiService(string Id, string Name, string BaseUrl, bool NeedsKey = true)
{
    public const string Custom = "custom";

    /// <summary>
    /// Claude 以外都用「OpenAI 相容」的格式：同一種寫法就能接 OpenAI、Gemini、DeepSeek、Groq、xAI、Mistral、
    /// OpenRouter、本機的 Ollama，以及任何提供這種介面的服務（選「自訂」填網址）。
    /// </summary>
    public static readonly AiService[] All =
    [
        new("claude", "Claude", ""),
        new("openai", "OpenAI", "https://api.openai.com/v1"),
        new("gemini", "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai"),
        new("deepseek", "DeepSeek", "https://api.deepseek.com/v1"),
        new("xai", "xAI", "https://api.x.ai/v1"),
        new("mistral", "Mistral", "https://api.mistral.ai/v1"),
        new("groq", "Groq", "https://api.groq.com/openai/v1"),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1"),
        new("ollama", "Ollama", "http://localhost:11434/v1", NeedsKey: false),
        new(Custom, "", ""),
    ];

    public static AiService Of(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}

public static class Brain
{
    /// <summary>所有服務共用的角色說明。</summary>
    public static string Prompt(string language) =>
        "你是 Shima 的助手，住在使用者 Windows 桌面頂端的小島裡。你的回答會顯示在很小的面板上或被語音唸出來，所以用口語、簡短的句子，" +
        "通常一到三句；不要用 Markdown、條列、表情符號或網址。使用者用什麼語言說話就用什麼語言回答" +
        (Loc.Lang == "zh-TW" ? "，中文一律用臺灣的繁體中文用語。" : $"；聽不出語言時用 {language}。") +
        "使用者說的話可能來自語音辨識，會有同音錯字，請依上下文理解。";

    public const string MusicDescription = "控制使用者電腦上正在播放的音樂，或查詢現在播的是哪一首。使用者提到播放、暫停、切歌、上一首、下一首、這首歌是什麼時使用。";
    public const string ActionDescription = "play_pause：播放或暫停；next：下一首；previous：上一首；now_playing：查詢現在播放的歌";
    public static readonly string[] Actions = ["play_pause", "next", "previous", "now_playing"];

    /// <summary>照設定建立目前選的服務；金鑰或模型還沒填好就是 null，problem 說缺什麼。</summary>
    public static IBrain? Create(Func<string, string> music, string language, out string problem)
    {
        var s = AppSettings.Current;
        var service = AiService.Of(s.AiService);
        var key = s.ApiKey(service.Id);
        var model = s.AiModels.GetValueOrDefault(service.Id, service.Id == "claude" ? ClaudeBrain.DefaultModel : "");
        problem = "";
        if (service.NeedsKey && key.Length == 0 && service.Id != AiService.Custom) { problem = L.T("先貼上 API 金鑰"); return null; }
        if (service.Id == "claude") return new ClaudeBrain(key, model, language, music);
        var url = service.Id == AiService.Custom ? s.AiCustomUrl.Trim().TrimEnd('/') : service.BaseUrl;
        if (url.Length == 0) { problem = L.T("先填 API 網址"); return null; }
        if (model.Length == 0) { problem = L.T("先選模型"); return null; }
        return new OpenAiBrain(url, key, model, language, music);
    }
}

/// <summary>Claude，走官方的 Anthropic SDK。</summary>
public sealed class ClaudeBrain(string apiKey, string model, string language, Func<string, string> music) : IBrain
{
    /// <remarks>
    /// 回答一兩句話、偶爾切歌用不到最強的模型：Haiku 便宜很多，回應也快，語音對話比較需要的是反應速度。
    /// 想要更聰明的可以在模型選單換成 Sonnet 或 Opus。
    /// </remarks>
    public const string DefaultModel = "claude-haiku-5-5";

    private readonly AnthropicClient _client = new() { ApiKey = apiKey };
    private readonly List<MessageParam> _messages = [];

    private static readonly Tool MusicTool = new()
    {
        Name = "music",
        Description = Brain.MusicDescription,
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["action"] = JsonSerializer.SerializeToElement(new { type = "string", @enum = Brain.Actions, description = Brain.ActionDescription }),
            },
            Required = ["action"],
        },
    };

    public async Task<string> ThinkAsync(string said, CancellationToken stop)
    {
        _messages.Add(new UserMessageParam { Content = said });
        for (int round = 0; round < 4; round++)
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = model.Length > 0 ? model : DefaultModel,
                MaxTokens = 16000,
                System = Brain.Prompt(language),
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Tools = [MusicTool],
                Messages = _messages,
            }, stop);

            if (response.StopReason == "refusal")
            {
                _messages.RemoveAt(_messages.Count - 1);
                return L.T("這個我沒辦法幫忙。");
            }

            // 回應原封不動放回對話（包含思考區塊），下一輪才接得上
            List<ContentBlockParam> echo = [], results = [];
            var spoken = new List<string>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text)) { echo.Add(new TextBlockParam { Text = text.Text }); spoken.Add(text.Text); }
                else if (block.TryPickThinking(out ThinkingBlock? thinking)) echo.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted)) echo.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                else if (block.TryPickToolUse(out ToolUseBlock? use))
                {
                    echo.Add(new ToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                    var action = use.Input.TryGetValue("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
                    results.Add(new ToolResultBlockParam { ToolUseID = use.ID, Content = music(action) });
                }
            }
            _messages.Add(new AssistantMessageParam { Content = echo });
            if (results.Count == 0) return string.Join(" ", spoken).Trim() is { Length: > 0 } reply ? reply : L.T("嗯。");
            _messages.Add(new UserMessageParam { Content = results });
        }
        return L.T("我有點卡住了，再說一次好嗎？");
    }

    public string Explain(Exception e) => e switch
    {
        AnthropicUnauthorizedException => L.T("API 金鑰無效"),
        AnthropicRateLimitException => L.T("用量已達上限，請稍後再試"),
        AnthropicNotFoundException => L.T("找不到這個模型"),
        _ => L.F("{0} API 錯誤", "Claude"),
    };

    public async Task<List<string>> ModelsAsync()
    {
        var page = await _client.Models.List();
        return page.Items.Select(m => m.ID).ToList();
    }
}

/// <summary>OpenAI 相容的 Chat Completions：OpenAI、Gemini、DeepSeek、Ollama 等等。</summary>
public sealed class OpenAiBrain(string baseUrl, string apiKey, string model, string language, Func<string, string> music) : IBrain
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly JsonArray _messages = [];
    private bool _noTools;

    private sealed class ApiError(HttpStatusCode status, string body) : Exception($"HTTP {(int)status}")
    {
        public HttpStatusCode Status { get; } = status;
        public string Body { get; } = body;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body = null)
    {
        var request = new HttpRequestMessage(method, baseUrl + path);
        if (apiKey.Length > 0) request.Headers.Authorization = new("Bearer", apiKey);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>一次請求的內容。tools：要不要附上音樂工具。</summary>
    public static JsonObject Body(string model, string system, JsonArray messages, bool tools)
    {
        var all = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system });
        foreach (var m in messages) all.Add(m?.DeepClone());
        var body = new JsonObject { ["model"] = model, ["messages"] = all };
        if (tools)
            body["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "music",
                    ["description"] = Brain.MusicDescription,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["action"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray(Brain.Actions.Select(a => (JsonNode)a).ToArray()),
                                ["description"] = Brain.ActionDescription,
                            },
                        },
                        ["required"] = new JsonArray("action"),
                    },
                },
            });
        return body;
    }

    public async Task<string> ThinkAsync(string said, CancellationToken stop)
    {
        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = said });
        for (int round = 0; round < 4; round++)
        {
            using var response = await Http.SendAsync(Request(HttpMethod.Post, "/chat/completions", Body(model, Brain.Prompt(language), _messages, !_noTools)), stop);
            var text = await response.Content.ReadAsStringAsync(stop);
            if (!response.IsSuccessStatusCode)
            {
                // 有些模型（尤其本機的）不支援工具：拿掉工具再試一次
                if (response.StatusCode == HttpStatusCode.BadRequest && !_noTools && !text.Contains("key", StringComparison.OrdinalIgnoreCase)) { _noTools = true; round--; continue; }
                _messages.RemoveAt(_messages.Count - 1);
                throw new ApiError(response.StatusCode, text);
            }

            var message = JsonNode.Parse(text)?["choices"]?[0]?["message"] as JsonObject;
            if (message == null) return L.T("嗯。");
            var calls = message["tool_calls"] as JsonArray;
            if (calls is not { Count: > 0 })
            {
                var reply = message["content"]?.ToString().Trim() ?? "";
                _messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = reply });
                return reply.Length > 0 ? reply : L.T("嗯。");
            }

            _messages.Add(message.DeepClone());
            foreach (var call in calls)
            {
                string action = "";
                try { action = JsonNode.Parse(call?["function"]?["arguments"]?.ToString() ?? "{}")?["action"]?.ToString() ?? ""; } catch { }
                _messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call?["id"]?.ToString() ?? "", ["content"] = music(action) });
            }
        }
        return L.T("我有點卡住了，再說一次好嗎？");
    }

    public string Explain(Exception e)
    {
        var name = AiService.All.FirstOrDefault(s => s.BaseUrl == baseUrl)?.Name ?? "API";
        if (e is not ApiError error) return e is HttpRequestException or TaskCanceledException ? L.F("連不上 {0}", name) : L.F("{0} API 錯誤", name);
        bool key = error.Body.Contains("api key", StringComparison.OrdinalIgnoreCase) || error.Body.Contains("api_key", StringComparison.OrdinalIgnoreCase);
        return error.Status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => L.T("API 金鑰無效"),
            HttpStatusCode.BadRequest when key => L.T("API 金鑰無效"),
            HttpStatusCode.TooManyRequests => L.T("用量已達上限，請稍後再試"),
            HttpStatusCode.NotFound => L.T("找不到這個模型"),
            _ => L.F("{0} API 錯誤", name),
        };
    }

    public async Task<List<string>> ModelsAsync()
    {
        using var response = await Http.SendAsync(Request(HttpMethod.Get, "/models"));
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new ApiError(response.StatusCode, text);
        return Models(text);
    }

    /// <summary>/models 的回應 → 模型代號，照字母排。Gemini 的代號前面有「models/」，拿掉。</summary>
    public static List<string> Models(string json)
    {
        var data = JsonNode.Parse(json)?["data"] as JsonArray ?? [];
        return data.Select(m => m?["id"]?.ToString() ?? "").Where(id => id.Length > 0)
            .Select(id => id.StartsWith("models/", StringComparison.Ordinal) ? id[7..] : id).Distinct().Order(StringComparer.Ordinal).ToList();
    }
}

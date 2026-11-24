// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Helpers;
using Anthropic.Models.Messages;

namespace Shima.Agents;

/// <summary>回答問題的那個 AI。一個物件就是一段對話，會記得前面聊過什麼。</summary>
public interface IBrain
{
    /// <param name="progress">
    /// 給了就用串流：回答邊產生邊收到「這一輪目前為止的全文」（不是增量，晚到或漏掉一次都不要緊）。
    /// 在背景執行緒呼叫。用了工具的話，新的一輪從頭開始累積，所以畫面上的「我查一下」會被之後的回答換掉。
    /// 回傳值跟不串流時一樣，只有最後一輪的回答。
    /// </param>
    Task<string> ThinkAsync(string said, CancellationToken stop, Action<string>? progress = null);
    /// <summary>出錯時給人看的一句話。</summary>
    string Explain(Exception e);
    /// <summary>這把金鑰能用的模型。</summary>
    Task<List<string>> ModelsAsync();
}

/// <summary>
/// 一家 AI 服務。DefaultModel：還沒選模型時用的那一款（有幾家不提供模型清單，沒有預設就沒得選）。
/// KeyVariable：那一家文件慣用的環境變數；留空就是「識別碼大寫 + _API_KEY」。
/// </summary>
public sealed record AiService(string Id, string Name, string BaseUrl, bool NeedsKey = true, string DefaultModel = "", string KeyVariable = "")
{
    public const string Custom = "custom";

    /// <summary>
    /// Claude 以外都用「OpenAI 相容」的格式：同一種寫法就能接 OpenAI、Gemini、DeepSeek、Groq、xAI、Mistral、
    /// OpenRouter、本機的 Ollama，以及任何提供這種介面的服務（選「自訂」填網址）。
    /// 中國大陸的幾家分中國站與國際站，兩邊的金鑰不通用，所以各列一項。
    /// 網址與預設模型是 2026-11-23 對照各家官方文件填的。
    /// </summary>
    public static readonly AiService[] All =
    [
        new("claude", "Claude", ""),
        new("openai", "OpenAI", "https://api.openai.com/v1"),
        new("gemini", "Gemini", "https://generativelanguage.googleapis.com/v1beta/openai"),
        new("deepseek", "DeepSeek", "https://api.deepseek.com", DefaultModel: "deepseek-flash"),
        new("xai", "xAI", "https://api.x.ai/v1"),
        new("mistral", "Mistral", "https://api.mistral.ai/v1"),
        new("groq", "Groq", "https://api.groq.com/openai/v1"),
        new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1"),
        new("qwen", "通義千問 Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1", DefaultModel: "qwen3.8-flash", KeyVariable: "DASHSCOPE_API_KEY"),
        new("qwen-intl", "通義千問 Qwen（國際）", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1", DefaultModel: "qwen3.8-flash", KeyVariable: "DASHSCOPE_API_KEY"),
        new("zhipu", "智譜 GLM", "https://open.bigmodel.cn/api/paas/v4", DefaultModel: "glm-4.7-flash", KeyVariable: "ZAI_API_KEY"),
        new("moonshot", "Kimi", "https://api.moonshot.cn/v1", KeyVariable: "MOONSHOT_API_KEY"),
        new("moonshot-intl", "Kimi（國際）", "https://api.moonshot.ai/v1", KeyVariable: "MOONSHOT_API_KEY"),
        new("doubao", "豆包 Doubao", "https://ark.cn-beijing.volces.com/api/v3", DefaultModel: "doubao-seed-2-0-mini-260428", KeyVariable: "ARK_API_KEY"),
        new("qianfan", "文心 ERNIE", "https://qianfan.baidubce.com/v2", DefaultModel: "ernie-5.1", KeyVariable: "QIANFAN_API_KEY"),
        new("hunyuan", "混元 Hunyuan", "https://tokenhub.tencentmaas.com/v1", DefaultModel: "hy4-flash", KeyVariable: "HUNYUAN_API_KEY"),
        new("hunyuan-intl", "混元 Hunyuan（國際）", "https://tokenhub-intl.tencentmaas.com/v1", DefaultModel: "hy4-flash", KeyVariable: "HUNYUAN_API_KEY"),
        new("minimax", "MiniMax", "https://api.minimax.cn/v1", DefaultModel: "MiniMax-M3.1-Flash", KeyVariable: "MINIMAX_API_KEY"),
        new("minimax-intl", "MiniMax（國際）", "https://api.minimax.io/v1", DefaultModel: "MiniMax-M3.1-Flash", KeyVariable: "MINIMAX_API_KEY"),
        new("stepfun", "階躍星辰 StepFun", "https://api.stepfun.com/v1", DefaultModel: "step-3.7-flash", KeyVariable: "STEP_API_KEY"),
        new("stepfun-intl", "階躍星辰 StepFun（國際）", "https://api.stepfun.ai/v1", DefaultModel: "step-3.7-flash", KeyVariable: "STEP_API_KEY"),
        new("siliconflow", "硅基流動 SiliconFlow", "https://api.siliconflow.cn/v1", KeyVariable: "SILICONFLOW_API_KEY"),
        new("siliconflow-intl", "硅基流動 SiliconFlow（國際）", "https://api.siliconflow.com/v1", KeyVariable: "SILICONFLOW_API_KEY"),
        new("ollama", "Ollama", "http://localhost:11434/v1", NeedsKey: false),
        new(Custom, "", ""),
    ];

    /// <summary>這一家現在用哪個模型：使用者選的，沒選就是預設的（Claude 與幾家不給模型清單的服務有預設）。</summary>
    public string ModelFor(AppSettings settings) =>
        settings.AiModels.GetValueOrDefault(Id, "") is { Length: > 0 } chosen ? chosen : Id == "claude" ? ClaudeBrain.DefaultModel : DefaultModel;

    /// <summary>沒存金鑰時去讀的環境變數；本機的 Ollama 與自訂網址沒有。</summary>
    public string Variable => Id switch
    {
        "claude" => "ANTHROPIC_API_KEY",
        "gemini" => "GEMINI_API_KEY",
        Custom or "ollama" => "",
        _ => KeyVariable.Length > 0 ? KeyVariable : Id.ToUpperInvariant() + "_API_KEY",
    };

    public static AiService Of(string id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];
}

/// <summary>助手能用的一個工具。Properties 是 JSON Schema 的 properties。</summary>
public sealed record AssistantTool(string Name, string Description, JsonObject Properties, string[] Required)
{
    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Choice(string description, params string[] values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()), ["description"] = description };
    private static JsonObject Number(string description) => new() { ["type"] = "number", ["description"] = description };

    /// <summary>
    /// 助手能做的事：除了聊天，就是操作 Shima 自己的功能。說明是寫給 AI 看的，要講清楚什麼時候該用。
    /// </summary>
    public static readonly AssistantTool[] All =
    [
        new("music", "控制使用者電腦上正在播放的音樂，或查詢現在播的是哪一首。使用者提到播放、暫停、切歌、上一首、下一首、這首歌是什麼時使用。",
            new() { ["action"] = Choice("play：開始或繼續播放；pause：暫停；next：下一首；previous：上一首；now_playing：查詢現在播放的歌", "play", "pause", "next", "previous", "now_playing") }, ["action"]),
        new("lyrics", "讀取現在這首歌的完整歌詞（有的話附中文翻譯），並標出正在唱的那一句。使用者問「這句歌詞什麼意思」「這首歌在唱什麼」「剛剛那句是什麼」時，先用這個工具拿到歌詞再回答。",
            [], []),
        new("todo", "管理使用者的待辦清單。使用者說「提醒我…」「幫我記…」「加一個待辦」時用 add；問有哪些待辦用 list；說做完了用 done；要刪掉用 remove。",
            new() { ["action"] = Choice("add：新增；list：列出全部；done：標成已完成；remove：刪除", "add", "list", "done", "remove"), ["text"] = Text("add 時是待辦的內容；done、remove 時是要找的那一項的關鍵字") }, ["action"]),
        new("timer", "控制番茄鐘（專注計時器）。使用者說「開始專注」「計時 25 分鐘」「暫停計時」「還剩多久」時使用。",
            new() { ["action"] = Choice("start：開始（可指定分鐘數）；pause：暫停；reset：歸零；status：查詢剩餘時間", "start", "pause", "reset", "status"), ["minutes"] = Number("start 時要專注幾分鐘；不給就用使用者原本的設定") }, ["action"]),
        new("volume", "調整音量。使用者說「大聲一點」「小聲一點」「音量調到差不多一半」時使用。",
            new() { ["direction"] = Choice("up：大聲；down：小聲", "up", "down"), ["steps"] = Number("調幾格，預設 2；「一點點」用 1，「很多」用 5") }, ["direction"]),
        new("lyrics_fix", "歌詞有問題時校正。歌詞內容不對、抓到別首歌或別的版本時用 other 換一份來源；歌詞比歌聲慢了用 earlier 提早；比歌聲快了用 later 延後；要回到原本用 reset。",
            new() { ["action"] = Choice("other：換另一個來源的歌詞；earlier：提早顯示；later：延後顯示；reset：還原", "other", "earlier", "later", "reset"), ["seconds"] = Number("earlier、later 時要移多少秒，預設 0.3") }, ["action"]),
        new("sleep", "睡眠定時：過一段時間、或這首歌播完後自動暫停音樂（睡前聽歌用）。使用者說「30 分鐘後停止播放」「這首播完就停」「取消睡眠定時」「睡眠定時還剩多久」時使用。" +
                     "跟番茄鐘和提醒不同：這個是讓音樂停下來。",
            new() { ["action"] = Choice("start：幾分鐘後暫停；end_of_track：這首播完就暫停；cancel：取消；status：查詢還剩多久", "start", "end_of_track", "cancel", "status"), ["minutes"] = Number("start 時，幾分鐘後暫停播放") }, ["action"]),
        // 以下由 Skills 執行：天氣、開網站或程式、提醒、時間、計算、剪貼簿
        new("weather", "查某個地方現在的天氣和今明兩天的預報。使用者問天氣、氣溫、會不會下雨、要不要帶傘時使用。使用者沒說地點就先問他要查哪裡，不要自己猜。",
            new() { ["location"] = Text("地名，請一律轉成英文寫法，例如 Taipei、Kaohsiung、Osaka、New York"), ["country"] = Text("可省略。同名地點很多時用兩個字母的國家代碼縮小範圍，例如 TW、JP、US") }, ["location"]),
        new("open", "用預設瀏覽器開網站，或開啟電腦上的常用程式。使用者說「打開 YouTube」「開記事本」「開設定」時使用。" +
                    "只能開 http／https 網址和這些程式：" + string.Join("、", Opener.Names) + "。不能開檔案、資料夾路徑，也不能帶命令列參數。",
            new() { ["target"] = Text("網址（例如 https://www.youtube.com 或 youtube.com），或上面清單裡的程式名稱") }, ["target"]),
        new("alarm", "在指定時間提醒使用者一句話（到時間會顯示在島上並唸出來）。使用者說「十分鐘後提醒我關火」「七點半叫我」「有哪些提醒」「取消提醒」時使用。" +
                     "跟待辦清單不同：有明確時間的用這個，沒有時間的用 todo。",
            new()
            {
                ["action"] = Choice("add：新增；list：列出全部；cancel：取消一個", "add", "list", "cancel"),
                ["minutes"] = Number("add 時，幾分鐘後提醒（相對時間用這個，例如「半小時後」給 30）"),
                ["at"] = Text("add 時，幾點提醒（絕對時間用這個），24 小時制 HH:mm，例如 07:30、19:05；明天的寫「明天 07:30」。只給時間而今天已經過了就是明天。minutes 和 at 擇一"),
                ["text"] = Text("add 時是到時要說的內容（例如「關火」）；cancel 時是提醒的編號或內容關鍵字"),
            }, ["action"]),
        new("datetime", "查詢現在的日期、時間、星期幾。使用者問幾點、今天幾號、星期幾，或你需要知道現在時間才能回答（例如「還有幾天到週末」）時使用。",
            [], []),
        new("calc", "精確計算算式。使用者要你算數字（加減乘除、次方、百分比）時用這個，不要心算。",
            new() { ["expression"] = Text("算式，只能有數字、+ - * / ^、括號和 %（50% 表示 0.5；「200 的 15%」寫成 200*15%）") }, ["expression"]),
        new("clipboard", "讀取使用者剪貼簿裡的文字（最多約 4000 字）。使用者說「我剛複製的」「剪貼簿裡的」「幫我翻譯／摘要這段」而沒有貼出內容時使用。",
            [], []),
    ];
}

public static class Brain
{
    /// <summary>所有服務共用的角色說明。</summary>
    public static string Prompt(string language) =>
        "你是 Shima 的助手，住在使用者 Windows 桌面頂端的小島裡。你的回答會顯示在很小的面板上或被語音唸出來，所以用口語、簡短的句子，" +
        "通常一到三句；不要用 Markdown、條列、表情符號或網址。使用者用什麼語言說話就用什麼語言回答" +
        (Loc.Lang == "zh-TW" ? "，中文一律用臺灣的繁體中文用語。" : $"；聽不出語言時用 {language}。") +
        "使用者說的話可能來自語音辨識，會有同音錯字，請依上下文理解。" +
        "你可以用工具替使用者操作這座島：控制音樂、讀現在這首歌的歌詞、管理待辦、番茄鐘、音量、校正歌詞。做完動作後用一句話說你做了什麼。";

    /// <summary>
    /// 把對話退回到這一輪開始前的長度。一輪裡會加好幾則（使用者的話、助手要用工具、工具的結果），
    /// 中途失敗只刪最後一則的話，會留下沒有對應結果的工具呼叫，之後每一次請求都會被 API 拒絕。
    /// </summary>
    public static void Rewind<T>(IList<T> messages, int count)
    {
        while (messages.Count > count) messages.RemoveAt(messages.Count - 1);
    }

    /// <summary>執行一個工具：名稱、參數 → 給 AI 看的結果。</summary>
    public delegate string Run(string tool, JsonObject arguments);

    /// <summary>照設定建立目前選的服務；金鑰或模型還沒填好就是 null，problem 說缺什麼。</summary>
    public static IBrain? Create(Run run, string language, out string problem)
    {
        var s = AppSettings.Current;
        var service = AiService.Of(s.AiService);
        var key = s.ApiKey(service.Id);
        var model = service.ModelFor(s);
        problem = "";
        if (service.NeedsKey && key.Length == 0 && service.Id != AiService.Custom) { problem = L.T("先貼上 API 金鑰"); return null; }
        if (service.Id == "claude") return new ClaudeBrain(key, model, language, run);
        var url = service.Id == AiService.Custom ? s.AiCustomUrl.Trim().TrimEnd('/') : service.BaseUrl;
        if (url.Length == 0) { problem = L.T("先填 API 網址"); return null; }
        if (model.Length == 0) { problem = L.T("先選模型"); return null; }
        return new OpenAiBrain(url, key, model, language, run);
    }
}

/// <summary>Claude，走官方的 Anthropic SDK。</summary>
public sealed class ClaudeBrain(string apiKey, string model, string language, Brain.Run run) : IBrain
{
    /// <remarks>
    /// 回答一兩句話、偶爾切歌用不到最強的模型：Haiku 便宜很多，回應也快，語音對話比較需要的是反應速度。
    /// 想要更聰明的可以在模型選單換成 Sonnet 或 Opus。
    /// </remarks>
    public const string DefaultModel = "claude-haiku-5-5";

    private AnthropicClient? _made;
    private readonly List<MessageParam> _messages = [];

    /// <summary>SDK 送請求用的 HttpClient；不給就用 SDK 自己的。測試用來換成假的，不經過真的網路。</summary>
    public HttpClient? Http { get; init; }

    private AnthropicClient Client => _made ??= Http == null ? new() { ApiKey = apiKey } : new() { ApiKey = apiKey, HttpClient = Http };

    private static readonly ToolUnion[] Tools = AssistantTool.All.Select(t => (ToolUnion)new Tool
    {
        Name = t.Name,
        Description = t.Description,
        InputSchema = new()
        {
            Properties = t.Properties.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
            Required = t.Required,
        },
    }).ToArray();

    public async Task<string> ThinkAsync(string said, CancellationToken stop, Action<string>? progress = null)
    {
        // 這一輪失敗（API 錯誤、按了停止、工具出錯、拒絕回答、串流到一半斷線）就整段退回，下一句從乾淨的對話接著聊
        int start = _messages.Count;
        try { return await Converse(said, start, stop, progress); }
        catch
        {
            Brain.Rewind(_messages, start);
            throw;
        }
    }

    private async Task<string> Converse(string said, int start, CancellationToken stop, Action<string>? progress)
    {
        _messages.Add(new UserMessageParam { Content = said });
        for (int round = 0; round < 4; round++)
        {
            var response = await Send(new MessageCreateParams
            {
                Model = model.Length > 0 ? model : DefaultModel,
                MaxTokens = 16000,
                System = Brain.Prompt(language),
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Tools = Tools,
                Messages = _messages,
            }, stop, progress);

            if (response.StopReason == "refusal")
            {
                // 用過工具之後才拒絕的話，最後一則是工具結果：只刪它會留下沒有結果的工具呼叫
                Brain.Rewind(_messages, start);
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
                    var arguments = new JsonObject();
                    foreach (var (key, value) in use.Input) arguments[key] = JsonNode.Parse(value.GetRawText());
                    results.Add(new ToolResultBlockParam { ToolUseID = use.ID, Content = run(use.Name, arguments) });
                }
            }
            _messages.Add(new AssistantMessageParam { Content = echo });
            if (results.Count == 0) return string.Join(" ", spoken).Trim() is { Length: > 0 } reply ? reply : L.T("嗯。");
            _messages.Add(new UserMessageParam { Content = results });
        }
        // 工具一路叫到上限：歷史的尾巴停在工具的結果上，下一句接上去會被 API 退回，所以整輪收回
        Brain.Rewind(_messages, start);
        return L.T("我有點卡住了，再說一次好嗎？");
    }

    /// <summary>
    /// 送出一輪。有 progress 就用串流，邊收邊把文字交出去；收完由 SDK 的 MessageContentAggregator 拼回整則 Message
    /// （思考區塊的簽章、分段送來的工具參數都由它拼好），後面的處理跟不串流時完全一樣。
    /// </summary>
    private async Task<Message> Send(MessageCreateParams request, CancellationToken stop, Action<string>? progress)
    {
        if (progress == null) return await Client.Messages.Create(request, stop);
        var text = new StringBuilder();
        bool started = false;
        var whole = new MessageContentAggregator();
        try
        {
            await foreach (var e in whole.CollectAsync(Client.Messages.CreateStreaming(request, stop)).WithCancellation(stop))
            {
                started = true;
                // 一則回答有好幾個文字區塊時，不串流的做法是用空格接起來，這裡也一樣
                if (e.TryPickContentBlockStart(out var begin) && begin.ContentBlock.TryPickText(out _) && text.Length > 0) text.Append(' ');
                else if (e.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var piece) && piece.Text.Length > 0)
                {
                    text.Append(piece.Text);
                    progress(text.ToString());
                }
                // 串流中途的錯誤（例如 overloaded）SDK 平常會丟 AnthropicSseException；萬一當成一般事件交出來也要擋下
                else if (e.Type.Raw() == "error")
                    throw new HttpRequestException("Claude 串流錯誤：" + (e.Json.TryGetProperty("error", out var error) ? error.GetRawText() : e.Json.GetRawText()));
            }
            // 沒收到 message_stop 就結束（斷線）時，這裡會丟 AnthropicInvalidDataException
            return whole.Message();
        }
        // 請求本身被拒（還沒收到任何事件）：可能是這個環境不給串流，改用一般的請求再問一次，使用者不會看到失敗。
        // 真的是請求有問題的話，一般的請求也會回一樣的錯誤，照常往外丟
        catch (Exception e) when (!started && !stop.IsCancellationRequested && e is AnthropicBadRequestException or AnthropicUnprocessableEntityException or AnthropicNotImplementedException)
        {
            Log.Write($"ai: Claude 串流被拒（{e.GetType().Name}），改用一般請求");
            return await Client.Messages.Create(request, stop);
        }
        // 中途斷線、伺服器在串流裡送 error 事件（SDK 丟 AnthropicSseException）：包成 HttpRequestException，
        // 語音助手才認得出是連線或服務的問題，不會說成「無法啟動」
        catch (Exception e) when (!stop.IsCancellationRequested && e is IOException or AnthropicInvalidDataException or AnthropicIOException or AnthropicSseException)
        {
            throw new HttpRequestException("Claude 串流中斷：" + e.Message, e);
        }
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
        var page = await Client.Models.List();
        return page.Items.Select(m => m.ID).ToList();
    }
}

/// <summary>OpenAI 相容的 Chat Completions：OpenAI、Gemini、DeepSeek、Ollama 等等。</summary>
public sealed class OpenAiBrain(string baseUrl, string apiKey, string model, string language, Brain.Run run) : IBrain
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(90) };

    /// <summary>送請求用的 HttpClient；不給就用共用的那一個。測試用來換成假的，不經過真的網路。</summary>
    public HttpClient Http { get; init; } = Shared;

    private readonly JsonArray _messages = [];
    private bool _noTools, _noStream;

    /// <summary>串流這麼久都沒有新的一行，就當作斷線。本機的大模型第一個字可能要等很久，所以給寬一點。</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(90);

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
            body["tools"] = new JsonArray(AssistantTool.All.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = t.Properties.DeepClone(),
                        ["required"] = new JsonArray(t.Required.Select(r => (JsonNode)r).ToArray()),
                    },
                },
            }).ToArray());
        return body;
    }

    public async Task<string> ThinkAsync(string said, CancellationToken stop, Action<string>? progress = null)
    {
        // 這一輪失敗就整段退回：工具呼叫的第二輪才失敗時，助手那則帶 tool_calls 的訊息也要拿掉，
        // 不然它沒有對應的 tool 回覆，同一段對話之後每次請求都會被回 400
        int start = _messages.Count;
        try { return await Converse(said, start, stop, progress); }
        catch
        {
            Brain.Rewind(_messages, start);
            throw;
        }
    }

    private async Task<string> Converse(string said, int start, CancellationToken stop, Action<string>? progress)
    {
        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = said });
        for (int round = 0; round < 4; round++)
        {
            bool streaming = progress != null && !_noStream;
            var body = Body(model, Brain.Prompt(language), _messages, !_noTools);
            // 只加 stream，不加 stream_options：有幾家看到不認得的欄位會回 400
            if (streaming) body["stream"] = true;
            // 串流要在標頭一到就開始讀；預設會先把整段回應收完才交回來，等於沒有串流。
            // 這樣 HttpClient 的逾時只管到標頭為止，之後由 ChatStream 的 idle 接手
            using var response = await Http.SendAsync(Request(HttpMethod.Post, "/chat/completions", body), streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, stop);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(stop);
                if (Retry(response.StatusCode, error, streaming)) { round--; continue; }
                throw new ApiError(response.StatusCode, error);   // 由 ThinkAsync 退回對話
            }

            JsonObject? message;
            // 有的服務不理 stream: true，直接回一般的 JSON：照不串流的方式讀
            if (!streaming || response.Content.Headers.ContentType?.MediaType == "application/json")
            {
                var plain = JsonNode.Parse(await response.Content.ReadAsStringAsync(stop));
                // 有的代理出錯時照樣回 200，錯誤放在內文裡：當成錯誤處理，不要回一句「嗯。」帶過
                if ((plain as JsonObject)?["error"] is { } refused)
                {
                    var kind = (refused as JsonObject)?["code"] is JsonValue given && given.TryGetValue(out int http) && http is >= 400 and < 600 ? (HttpStatusCode)http : HttpStatusCode.InternalServerError;
                    throw new ApiError(kind, refused.ToJsonString());
                }
                message = plain?["choices"]?[0]?["message"] as JsonObject;
            }
            else
            {
                var chat = await Read(response, progress!, stop);
                if (chat.Error is { } failure)
                {
                    var status = failure["code"] is JsonValue code && code.TryGetValue(out int number) && number is >= 400 and < 600 ? (HttpStatusCode)number : HttpStatusCode.InternalServerError;
                    if (!chat.Started && Retry(status, failure.ToJsonString(), streaming)) { round--; continue; }
                    throw new ApiError(status, failure.ToJsonString());
                }
                if (!chat.Finished) throw new HttpRequestException("串流沒有結束就斷了");
                message = chat.Message();
            }
            if (message == null)
            {
                // 回應看不懂：當作這一輪沒發生
                Brain.Rewind(_messages, start);
                return L.T("嗯。");
            }
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
                JsonObject arguments = [];
                try { arguments = JsonNode.Parse(call?["function"]?["arguments"]?.ToString() is { Length: > 0 } raw ? raw : "{}") as JsonObject ?? []; } catch { }
                _messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call?["id"]?.ToString() ?? "", ["content"] = run(call?["function"]?["name"]?.ToString() ?? "", arguments) });
            }
        }
        // 工具一路叫到上限：歷史的尾巴停在工具的結果上，下一句接上去會被 API 退回，所以整輪收回
        Brain.Rewind(_messages, start);
        return L.T("我有點卡住了，再說一次好嗎？");
    }

    /// <summary>
    /// 請求被拒時能不能換個方式再試：有些服務或模型（尤其本機的）不支援串流或工具。
    /// 錯誤提到 stream 就先關串流；不然先拿掉工具（跟以前一樣）；工具已經拿掉了還是不行，再關串流。
    /// 金鑰、用量、伺服器錯誤都不重試。
    /// </summary>
    private bool Retry(HttpStatusCode status, string error, bool streaming)
    {
        if (error.Contains("key", StringComparison.OrdinalIgnoreCase)) return false;
        bool rejected = status is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotImplemented;
        if (!rejected) return false;
        if (streaming && error.Contains("stream", StringComparison.OrdinalIgnoreCase)) _noStream = true;
        else if (!_noTools && status == HttpStatusCode.BadRequest) _noTools = true;
        else if (streaming) _noStream = true;
        else return false;
        Log.Write($"ai: {baseUrl} 回 {(int)status}，改用{(_noStream ? "不串流" : "")}{(_noTools ? "不帶工具" : "")}的請求");
        return true;
    }

    private static async Task<ChatStream> Read(HttpResponseMessage response, Action<string> progress, CancellationToken stop)
    {
        try
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stop), Encoding.UTF8);
            return await ChatStream.ReadAsync(reader, progress, stop, Idle);
        }
        // 中途斷線、太久沒資料：包成 HttpRequestException，畫面說「連不上」，語音助手也認得出是連線的問題
        catch (Exception e) when (!stop.IsCancellationRequested && e is IOException or TimeoutException)
        {
            throw new HttpRequestException("串流中斷：" + e.Message, e);
        }
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

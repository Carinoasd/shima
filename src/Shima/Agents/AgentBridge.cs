// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>
/// Claude Code 想用某個工具，等人回答。Decision：「allow」、「deny」、「always」（允許並記住 Remember 這條規則），
/// 或 null（回終端機照常問）。
/// </summary>
public sealed class PermissionRequest
{
    public const string Always = "always";

    public required string Project { get; init; }
    public required string Tool { get; init; }
    public required string Details { get; init; }
    public string SessionId { get; init; } = "";
    public string Cwd { get; init; } = "";
    /// <summary>tool_input 的原始 JSON。</summary>
    public string ToolInput { get; init; } = "{}";
    /// <summary>如果選「以後都允許」，會記成這條規則；null 表示這種請求不給記（不要顯示那顆按鈕）。</summary>
    public AgentRule? Remember { get; init; }
    /// <summary>「以後都允許 git status」按鈕的文字；不給記就是 null。</summary>
    public string? RememberText => Remember is { } r ? L.F("以後都允許 {0}", r.Label) : null;
    public TaskCompletionSource<string?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Claude Code 的 hook 會連到這個只開在本機的 HTTP 端點。權限請求會等使用者在島上按下去；
/// 通知和「做完了」送了就走。每次呼叫都要帶只有這個 Windows 使用者讀得到的 token。
/// </summary>
public sealed class AgentBridge
{
    public const int Port = 47813;
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(570);

    private HttpListener? _listener;

    public event Action<PermissionRequest>? Asked;
    /// <summary>專案名稱、最後一句話。</summary>
    public event Action<string, string>? Finished;
    /// <summary>Claude Code 的待辦清單變了：專案名稱、做完幾項、總共幾項、正在做的那一項。</summary>
    public event Action<string, int, int, string>? Progressed;
    /// <summary>工作階段清單有變（新的、狀態、進度、結束）。用 Sessions 讀目前的清單。</summary>
    public event Action? SessionsChanged;
    /// <summary>符合使用者記住的規則，直接允許了（沒有觸發 Asked）。</summary>
    public event Action<PermissionRequest, AgentRule>? AutoAllowed;

    private readonly AgentSessions _sessions;
    /// <summary>裝箱的 ImmutableArray：整個換掉，讀的人拿到的一定是完整的一份。</summary>
    private volatile object _snapshot = ImmutableArray<AgentSession>.Empty;
    private Timer? _pruner;

    /// <summary>「以後都允許」的規則。</summary>
    public AgentRules Rules { get; }

    /// <summary>目前的工作階段（不可變的快照，哪個執行緒都可以讀）。</summary>
    public ImmutableArray<AgentSession> Sessions
    {
        get
        {
            Prune();
            return (ImmutableArray<AgentSession>)_snapshot;
        }
    }

    public AgentBridge(AgentRules? rules = null, Func<DateTime>? clock = null)
    {
        Rules = rules ?? new AgentRules();
        _sessions = new AgentSessions(clock);
    }

    /// <summary>Stream Dock 外掛看的內容，由島更新。</summary>
    public volatile DeckState Deck = new(null, null, null, TimeSpan.Zero);
    public volatile DeckNotice Notice = DeckNotice.None;
    public volatile DeckDials Dials = DeckDials.None;
    /// <summary>電腦此刻播出的聲音大小（0–1）；沒有在擷取就是 -1。</summary>
    public Func<float>? Level;
    private long _deckSeen = -100000;
    /// <summary>Stream Dock 外掛這幾秒內有沒有來問過（有的話，就算島藏起來了也要繼續擷取聲音）。</summary>
    public bool DeckWatching => Environment.TickCount64 - Interlocked.Read(ref _deckSeen) < 5000;
    /// <summary>此刻的頻譜（低音到高音，每段 0–1）。</summary>
    public Func<int, float[]>? Bands;
    /// <summary>Stream Dock 外掛按了按鍵：playpause、next、previous、allow、deny、ask、zone、mute。</summary>
    public event Action<string>? DeckPressed;
    /// <summary>Stream Dock 的旋鈕轉了幾格：volume、seek、shift、zone。</summary>
    public event Action<string, int>? DeckTurned;

    public static string TokenFile => Path.Combine(AppSettings.Folder, "bridge.token");

    public static string Token()
    {
        try
        {
            if (File.Exists(TokenFile) && File.ReadAllText(TokenFile).Trim() is { Length: >= 32 } existing) return existing;
            Directory.CreateDirectory(AppSettings.Folder);
            var fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            File.WriteAllText(TokenFile, fresh);
            return fresh;
        }
        catch (Exception e) { Log.Write("bridge token: " + e.Message); return ""; }
    }

    public void Start()
    {
        if (_listener != null) return;
        var token = Token();
        if (token.Length == 0) return;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(() => LoopAsync(_listener, token));
            _pruner = new Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        catch (Exception e) { Log.Write("bridge: " + e.Message); _listener = null; }
    }

    private async Task LoopAsync(HttpListener listener, string token)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(context, token));
        }
    }

    private async Task HandleAsync(HttpListenerContext context, string token)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            if (request.Url?.AbsolutePath.StartsWith("/deck/") == true) { await DeckAsync(request, response, token); return; }
            // 只接受 hook：網頁（帶 Origin）、token 不對、內容太大的一律拒絕
            if (request.HttpMethod != "POST" || request.Headers["Origin"] != null || request.ContentLength64 > 512 * 1024 ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Headers["X-Shima-Token"] ?? ""), Encoding.UTF8.GetBytes(token)))
            {
                response.StatusCode = 403;
                return;
            }

            string text;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) text = await reader.ReadToEndAsync();
            if (JsonNode.Parse(text) is not JsonObject json) { response.StatusCode = 400; return; }
            if (request.Url?.AbsolutePath == "/permission")
            {
                var behavior = await PermissionAsync(json);
                if (Answer(behavior) is { } body)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    response.ContentType = "application/json";
                    await response.OutputStream.WriteAsync(bytes);
                }
                else response.StatusCode = 204;   // 沒有決定：Claude Code 照常在終端機問
                return;
            }

            // /notify 永遠不回內容：SessionStart、UserPromptSubmit 的輸出會被 Claude Code 當成給模型看的內容
            Notify(json);
            response.StatusCode = 204;
        }
        catch (Exception e) { Log.Write("bridge request: " + e.Message); try { response.StatusCode = 500; } catch { } }
        finally { try { response.Close(); } catch { } }
    }

    /// <summary>
    /// Stream Dock 外掛的請求。外掛是 Stream Dock 內嵌瀏覽器裡的網頁，所以帶 Origin、也沒辦法加自訂標頭；
    /// token 改放在網址上（外掛安裝時寫進它自己的資料夾），沒有 token 的網頁一樣進不來。
    /// </summary>
    private async Task DeckAsync(HttpListenerRequest request, HttpListenerResponse response, string token)
    {
        var query = request.QueryString;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(query["t"] ?? ""), Encoding.UTF8.GetBytes(token)))
        {
            response.StatusCode = 403;
            return;
        }
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Cache-Control", "no-store");
        var deck = Deck;
        switch (request.Url!.AbsolutePath)
        {
            case "/deck/now":
                Interlocked.Exchange(ref _deckSeen, Environment.TickCount64);
                var now = deck.ToJson(query["have"] ?? "\0");
                var notice = Notice;
                now["notice"] = new JsonObject { ["kind"] = notice.Kind, ["title"] = notice.Title, ["text"] = notice.Text, ["listening"] = notice.Listening };
                now["timer"] = notice.Timer;
                now["plan"] = notice.Plan;
                now["level"] = Level?.Invoke() ?? -1;
                if (int.TryParse(query["bands"], out int wanted) && wanted is > 0 and <= 96 && Bands?.Invoke(wanted) is { } bands)
                    now["bands"] = new JsonArray(bands.Select(b => (JsonNode)JsonValue.Create(MathF.Round(b, 3))).ToArray());
                var dials = Dials;
                now["dials"] = new JsonObject { ["volume"] = dials.Volume, ["volumeText"] = dials.VolumeText, ["muted"] = dials.Muted, ["zone"] = dials.Zone, ["shift"] = dials.Shift };
                var bytes = Encoding.UTF8.GetBytes(now.ToJsonString());
                response.ContentType = "application/json; charset=utf-8";
                await response.OutputStream.WriteAsync(bytes);
                break;
            case "/deck/cover":
                if (deck.CoverJpeg() is { } jpeg)
                {
                    response.ContentType = "image/jpeg";
                    await response.OutputStream.WriteAsync(jpeg);
                }
                else response.StatusCode = 404;
                break;
            case "/deck/press" when query["do"] is "playpause" or "next" or "previous" or "allow" or "deny" or "ask" or "zone" or "mute":
                DeckPressed?.Invoke(query["do"]!);
                response.StatusCode = 204;
                break;
            case "/deck/turn" when query["what"] is "volume" or "seek" or "shift" or "zone" && int.TryParse(query["ticks"], out int ticks) && ticks != 0:
                DeckTurned?.Invoke(query["what"]!, Math.Clamp(ticks, -20, 20));
                response.StatusCode = 204;
                break;
            case "/deck/log":
                Log.Write("stream dock: " + (query["m"] ?? "")[..Math.Min(600, (query["m"] ?? "").Length)]);
                response.StatusCode = 204;
                break;
            default:
                response.StatusCode = 404;
                break;
        }
    }

    /// <summary>
    /// 一則權限請求：先查規則，符合就直接允許；不符合才問島上的人。回傳 "allow"、"deny" 或 null。
    /// </summary>
    public async Task<string?> PermissionAsync(JsonObject json)
    {
        var tool = json["tool_name"]?.ToString() ?? "?";
        var input = json["tool_input"];
        var cwd = json["cwd"]?.ToString() ?? "";
        var ask = new PermissionRequest
        {
            Project = AgentSessions.ProjectOf(cwd),
            Tool = tool,
            Details = Describe(tool, input),
            SessionId = json["session_id"]?.ToString() ?? "",
            Cwd = cwd,
            ToolInput = input?.ToJsonString() ?? "{}",
            Remember = AgentRules.Suggest(tool, input, cwd),
        };
        var key = AgentSessions.Key(json);

        if (Rules.Match(tool, input, cwd) is { } rule)
        {
            Touch(json);
            Change(s => s.Resumed(key));
            AutoAllowed?.Invoke(ask, rule);
            return "allow";
        }

        Touch(json);
        if (Asked == null) return null;
        Asked.Invoke(ask);
        var done = await Task.WhenAny(ask.Decision.Task, Task.Delay(Patience));
        var behavior = done == ask.Decision.Task ? ask.Decision.Task.Result : null;
        ask.Decision.TrySetResult(null);
        if (behavior == PermissionRequest.Always)
        {
            if (ask.Remember is { } remember) Rules.Add(remember);
            behavior = "allow";
        }
        if (behavior is "allow" or "deny") Change(s => s.Resumed(key));
        return behavior;
    }

    /// <summary>不用等回答的 hook：Stop、PostToolUse、Notification、SessionStart、SessionEnd…</summary>
    public void Notify(JsonObject json)
    {
        var name = json["hook_event_name"]?.ToString();
        var project = AgentSessions.ProjectOf(json["cwd"]?.ToString());
        bool planChanged = false;
        AgentSession? session = null;
        Change(s =>
        {
            bool changed = s.Apply(json, out planChanged);
            session = s.Find(AgentSessions.Key(json));
            return changed;
        });
        if (name == "Stop") Finished?.Invoke(project, json["last_assistant_message"]?.ToString() ?? "");
        else if (planChanged && session != null) Progressed?.Invoke(project, session.Done, session.Total, session.Current);
    }

    private void Touch(JsonObject json) => Change(s => s.Apply(json));

    private void Prune() => Change(s => s.Prune());

    /// <summary>在鎖裡改工作階段；有變就換一份新的快照，並在鎖外通知。</summary>
    private void Change(Func<AgentSessions, bool> change)
    {
        bool changed;
        lock (_sessions)
        {
            changed = change(_sessions);
            if (changed) _snapshot = _sessions.Snapshot();
        }
        if (changed) SessionsChanged?.Invoke();
    }

    /// <summary>給 Claude Code 的回答；null 表示不回答（它會照常自己問）。「always」當成允許。</summary>
    public static string? Answer(string? behavior)
    {
        if (behavior == PermissionRequest.Always) behavior = "allow";
        if (behavior is not ("allow" or "deny")) return null;
        var decision = new JsonObject { ["behavior"] = behavior };
        if (behavior == "deny") decision["message"] = "使用者在 Shima 上拒絕了這個動作。";
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = decision },
        }.ToJsonString();
    }

    /// <summary>工具要做什麼，用一兩行講完：指令、檔案路徑、網址。</summary>
    public static string Describe(string? tool, JsonNode? input)
    {
        if (input is not JsonObject o) return "";
        foreach (var key in (string[])["command", "file_path", "path", "url", "pattern", "query", "prompt", "description"])
            if (o[key]?.ToString() is { Length: > 0 } value)
                return value.Length > 400 ? value[..400] + "…" : value;
        var all = o.ToJsonString();
        return all.Length > 400 ? all[..400] + "…" : all;
    }
}

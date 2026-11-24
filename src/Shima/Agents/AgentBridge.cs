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

    /// <summary>隨機的識別碼：Stream Dock 按允許時要指名是哪一則。</summary>
    public string Id { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    public required string Project { get; init; }
    /// <summary>哪一個 agent 在問。</summary>
    public AgentKind Agent { get; init; } = AgentKind.Claude;
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
    /// <summary>每個 agent 同時等人回答的權限請求最多幾則；再來的回終端機照常問。</summary>
    public const int MaxWaiting = 8;
    /// <summary>整個伺服器同時處理中的連線最多幾條；比各來源的名額加起來（4 組）大，每個來源才一定拿得到自己的份。</summary>
    public const int MaxOpen = 128;
    /// <summary>驗證通過之後，每個來源（每個 agent、Stream Dock）同時最多幾條（等回答的那幾條、外掛輪詢、通知，綽綽有餘）。</summary>
    public const int MaxPerSource = 24;
    /// <summary>整個內容要在這段時間內送完；慢慢送的連線不能一直佔著名額。</summary>
    public static readonly TimeSpan BodyPatience = TimeSpan.FromSeconds(10);

    private HttpListener? _listener;
    private readonly Lanes _lanes = new(MaxOpen, MaxPerSource);
    private readonly DeckLog _deckLog = new();
    private readonly Dictionary<AgentKind, Slots> _waiting = Enum.GetValues<AgentKind>().ToDictionary(a => a, _ => new Slots(MaxWaiting));

    public event Action<PermissionRequest>? Asked;
    /// <summary>Claude Code 做完了：專案名稱、最後一句話。</summary>
    public event Action<string, string>? Finished;
    /// <summary>任何一個 agent 做完了：哪一個、專案名稱、最後一句話。</summary>
    public event Action<AgentKind, string, string>? AgentFinished;
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
    /// <summary>Stream Dock 外掛按了按鍵：playpause、next、previous、allow、deny、ask、zone、mute；allow、deny 還帶著外掛畫出來的那一則權限請求的識別碼（舊版外掛不帶）。</summary>
    public event Action<string, string?>? DeckPressed;
    /// <summary>Stream Dock 的旋鈕轉了幾格：volume、seek、shift、zone。</summary>
    public event Action<string, int>? DeckTurned;

    public static string TokenFile => Path.Combine(AppSettings.Folder, "bridge.token");

    /// <summary>
    /// Stream Dock 外掛用的是另一把金鑰。它得寫進外掛的資料夾、還會出現在網址上，比 hook 那把容易外流；
    /// 分開之後，拿到它的人只碰得到 /deck/ 底下的東西，沒辦法假裝成 Claude Code 的 hook。
    /// </summary>
    public static string DeckTokenFile => Path.Combine(AppSettings.Folder, "deck.token");

    public static string Token() => TokenIn(TokenFile);
    public static string DeckToken() => TokenIn(DeckTokenFile);

    /// <summary>
    /// /deck/ 認的金鑰：這台電腦有 Stream Dock、外掛也裝著（設定裡的 Stream Dock 開關開著）才有，否則空字串（一律拒絕）。
    /// 這裡只讀不產生：金鑰由 StreamDock.Install() 寫外掛時才產生。
    /// </summary>
    public static readonly DeckDoor DeckDoor = new(() => StreamDock.Available && StreamDock.Installed, () => PeekToken(DeckTokenFile));

    /// <summary>沒在用 Stream Dock：把 deck 金鑰整個收回（檔案刪掉、記住的那份也忘掉），之後再裝外掛會產生新的一把。</summary>
    public static void DropDeckToken()
    {
        Known.TryRemove(DeckTokenFile, out _);
        try { File.Delete(DeckTokenFile); }
        catch (Exception e) { Log.Write("deck token: " + e.Message); }
        DeckDoor.Forget();
    }

    /// <summary>
    /// 每個 agent 的 hook 各用一把金鑰（Claude Code 沿用 bridge.token）。Codex、Gemini 的那把只進得了自己的網址，
    /// 外流了也冒充不了 Claude Code、碰不到 Stream Dock；Gemini 的連權限請求那扇門都沒有。
    /// </summary>
    public static string Token(AgentKind agent) => agent == AgentKind.Claude ? Token() : TokenIn(Path.Combine(AppSettings.Folder, $"{agent.ToString().ToLowerInvariant()}.token"));

    /// <summary>
    /// 讀過或剛產生的金鑰：程式開著的時候固定不變。Shima 啟動時就讀好每一把，之後才連接的 agent 寫進 hook 的
    /// 一定是同一把（不然檔案在中途被刪掉時，hook 拿到新的、這邊還在比舊的，每個請求都會被拒絕）。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase);

    private static string TokenIn(string file)
    {
        if (Known.TryGetValue(file, out var known)) return known;
        try
        {
            if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: >= 32 } existing) return Known.GetOrAdd(file, existing);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            File.WriteAllText(file, fresh);
            return Known.GetOrAdd(file, fresh);
        }
        catch (Exception e) { Log.Write("bridge token: " + e.Message); return ""; }
    }

    /// <summary>讀已經有的金鑰，沒有就是空字串（不產生）。</summary>
    private static string PeekToken(string file)
    {
        if (Known.TryGetValue(file, out var known)) return known;
        try { if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: >= 32 } existing) return Known.GetOrAdd(file, existing); }
        catch (Exception e) { Log.Write("bridge token: " + e.Message); }
        return "";
    }

    public enum Door { Refuse, Hook, Deck }

    /// <summary>走 Hook 那扇門的請求是哪個 agent 的、是不是權限請求。</summary>
    public readonly record struct Admission(Door Door, AgentKind Agent = AgentKind.Claude, bool Permission = false);

    /// <summary>
    /// 一個請求可以走哪一扇門。兩把金鑰各管各的：
    /// hook 只收 POST、金鑰放在標頭、不能帶 Origin（瀏覽器裡的網頁一定會帶）；
    /// /deck/ 的金鑰放在網址上（外掛是內嵌瀏覽器裡的網頁，加不了自訂標頭），會改變狀態的 press、turn 只收 POST，
    /// 免得一張 &lt;img&gt; 或一個連結就能替人按下去。
    /// </summary>
    public static Door Gate(string method, string path, string? header, string? query, bool hasOrigin, string hookToken, string deckToken) =>
        Admit(method, path, header, query, hasOrigin, agent => agent == AgentKind.Claude ? hookToken : "", deckToken).Door;

    /// <summary>
    /// 跟 Gate 一樣，再加上別的 agent：/codex/、/gemini/ 底下的網址只認那個 agent 自己的金鑰，規則跟 Claude Code 的 hook 一樣
    /// （只收 POST、金鑰在標頭、不能帶 Origin）。其餘的網址照舊是 Claude Code 的。
    /// </summary>
    public static Admission Admit(string method, string path, string? header, string? query, bool hasOrigin, Func<AgentKind, string> tokens, string deckToken)
    {
        static bool Same(string? given, string wanted) =>
            wanted.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given ?? ""), Encoding.UTF8.GetBytes(wanted));

        if (path.StartsWith("/deck/", StringComparison.Ordinal))
        {
            if (!Same(query, deckToken)) return new(Door.Refuse);
            bool acts = path is "/deck/press" or "/deck/turn";
            return new(method == (acts ? "POST" : "GET") ? Door.Deck : Door.Refuse);
        }
        if (method != "POST" || hasOrigin || Route(path) is not { } route) return new(Door.Refuse);
        return Same(header, tokens(route.Agent)) ? new(Door.Hook, route.Agent, route.Permission) : new(Door.Refuse);
    }

    /// <summary>
    /// 請求是不是從這台電腦送來、而且是衝著 localhost 來的。HttpListener 只聽 localhost，但不看對方是誰；
    /// Host 也要檢查：瀏覽器裡的網頁可以把自己的網域在解析後換成 127.0.0.1（DNS rebinding），這時 Host 是那個網域，不是 localhost。
    /// hook（curl.exe）與 Stream Dock 外掛連的都是 http://localhost:47813。
    /// </summary>
    public static bool Local(IPAddress? remote, string? host)
    {
        if (remote == null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();   // IsLoopback 不認 ::ffff:127.0.0.1
        if (!IPAddress.IsLoopback(remote)) return false;
        return host is { } h && (h.Equals("localhost", StringComparison.OrdinalIgnoreCase) || h.Equals($"localhost:{Port}", StringComparison.OrdinalIgnoreCase)
            || h == "127.0.0.1" || h == $"127.0.0.1:{Port}");
    }

    /// <summary>網址是哪個 agent 的哪一種 hook；null 表示沒有這扇門（例如 Gemini 的權限請求）。</summary>
    private static (AgentKind Agent, bool Permission)? Route(string path)
    {
        foreach (var agent in (AgentKind[])[AgentKind.Codex, AgentKind.Gemini])
        {
            var prefix = AgentKinds.Prefix(agent);
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = path[prefix.Length..];
            if (rest == "notify") return (agent, false);
            if (rest == "permission" && AgentKinds.Asks(agent)) return (agent, true);
            return null;
        }
        return (AgentKind.Claude, path == "/permission");
    }

    public const int MaxBody = 512 * 1024;

    /// <summary>
    /// 讀請求的內容，最多 limit 個位元組；超過就是 null。
    /// 不能只看 Content-Length：分塊傳輸（chunked）沒有這個標頭，照樣可以一直送。
    /// 整個內容要在 within（預設 <see cref="BodyPatience"/>）內讀完，否則丟 TimeoutException：
    /// 宣稱很大、然後每分鐘送一個位元組的連線，不能一直佔著名額。HttpListener 的串流不理會取消，所以是跟計時器賽跑。
    /// </summary>
    public static async Task<string?> ReadLimitedAsync(Stream body, int limit, TimeSpan? within = null)
    {
        using var kept = new MemoryStream();
        var buffer = new byte[8192];
        using var stop = new CancellationTokenSource();
        var deadline = Task.Delay(within ?? BodyPatience, stop.Token);
        try
        {
            while (true)
            {
                var pending = body.ReadAsync(buffer).AsTask();
                if (await Task.WhenAny(pending, deadline).ConfigureAwait(false) != pending)
                {
                    _ = pending.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // 放棄的那次讀，之後出錯也不用理
                    throw new TimeoutException("request body too slow");
                }
                int read = await pending.ConfigureAwait(false);
                if (read <= 0) break;
                if (kept.Length + read > limit) return null;
                kept.Write(buffer, 0, read);
            }
        }
        finally { stop.Cancel(); }
        return Encoding.UTF8.GetString(kept.GetBuffer(), 0, (int)kept.Length);
    }

    /// <summary>
    /// 開始收請求。回傳 false 表示沒開成（金鑰讀寫不了、或 47813 被別的程式佔住）：這時 hook 的請求會送到佔住那個埠的程式，
    /// 不會到 Shima，要讓使用者知道。
    /// </summary>
    public bool Start()
    {
        if (_listener != null) return true;
        string token = Token();
        if (token.Length == 0) return false;
        if (!(StreamDock.Available && StreamDock.Installed)) DropDeckToken();   // 沒在用 Stream Dock：之前留下的 deck 金鑰不要再認
        // 每一把都先讀好（沒有就產生），之後連接才裝的 hook 用的也是這幾把
        var tokens = Enum.GetValues<AgentKind>().ToDictionary(a => a, Token);
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(() => LoopAsync(_listener, tokens));
            _pruner = new Timer(_ => Prune(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            return true;
        }
        catch (Exception e) { Log.Write("bridge: " + e.Message); _listener = null; return false; }
    }

    /// <summary>程式結束時關掉：不再收請求，也停掉清理用的計時器。</summary>
    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        try { listener?.Close(); } catch { }
        _pruner?.Dispose();
        _pruner = null;
    }

    private async Task LoopAsync(HttpListener listener, IReadOnlyDictionary<AgentKind, string> tokens)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { return; }
            if (!_lanes.TryOpen())
            {
                // 太多了：不讀內容、馬上回。hook 拿到空的回應，Claude Code 照常在終端機問
                try { context.Response.StatusCode = 503; context.Response.Close(); } catch { }
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await HandleAsync(context, tokens); }
                finally { _lanes.Close(); }
            });
        }
    }

    private async Task HandleAsync(HttpListenerContext context, IReadOnlyDictionary<AgentKind, string> tokens)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            if (!Local(request.RemoteEndPoint?.Address, request.Headers["Host"])) { response.StatusCode = 403; return; }
            var (door, agent, permission) = Admit(request.HttpMethod, request.Url?.AbsolutePath ?? "", request.Headers["X-Shima-Token"], request.QueryString["t"],
                request.Headers["Origin"] != null, a => tokens.GetValueOrDefault(a, ""), DeckDoor.Key);
            if (door == Door.Refuse) { response.StatusCode = 403; return; }
            if (request.ContentLength64 > MaxBody) { response.StatusCode = 413; return; }
            // 驗證通過了：之後佔的是這個來源自己的名額，一個來源用光不影響別人
            var lane = door == Door.Deck ? "deck" : agent.ToString();
            if (!await _lanes.RunAsync(lane, () => ServeAsync(request, response, door, agent, permission)).ConfigureAwait(false)) response.StatusCode = 503;
        }
        catch (Exception e) { Log.Write("bridge request: " + e.Message); try { response.StatusCode = 500; } catch { } }
        finally { try { response.Close(); } catch { } }
    }

    private async Task ServeAsync(HttpListenerRequest request, HttpListenerResponse response, Door door, AgentKind agent, bool permission)
    {
        try
        {
            if (door == Door.Deck) { await DeckAsync(request, response); return; }

            if (await ReadLimitedAsync(request.InputStream, MaxBody) is not { } text) { response.StatusCode = 413; return; }
            if (JsonNode.Parse(text) is not JsonObject json) { response.StatusCode = 400; return; }
            if (permission)
            {
                var behavior = await PermissionAsync(json, agent);
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
            Notify(json, agent);
            if (agent == AgentKind.Gemini)
            {
                // Gemini CLI 把 hook 的 stdout 當成 JSON 解析：給一個空物件（不帶 decision，AfterAgent 的 deny 會讓它重來一次）
                response.ContentType = "application/json";
                await response.OutputStream.WriteAsync("{}"u8.ToArray());
            }
            else response.StatusCode = 204;
        }
        catch (TimeoutException e) { Log.Write("bridge request: " + e.Message); try { response.Abort(); } catch { } }   // 內容送太慢：直接斷線
        catch (Exception e) { Log.Write("bridge request: " + e.Message); try { response.StatusCode = 500; } catch { } }
    }

    /// <summary>
    /// Stream Dock 外掛的請求。外掛是 Stream Dock 內嵌瀏覽器裡的網頁，所以帶 Origin、也沒辦法加自訂標頭；
    /// token 改放在網址上（外掛安裝時寫進它自己的資料夾），沒有 token 的網頁一樣進不來。
    /// </summary>
    private async Task DeckAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        var query = request.QueryString;   // 金鑰與方法已經在 Gate 檢查過
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Cache-Control", "no-store");
        var deck = Deck;
        switch (request.Url!.AbsolutePath)
        {
            case "/deck/now":
                Interlocked.Exchange(ref _deckSeen, Environment.TickCount64);
                var now = deck.ToJson(query["have"] ?? "\0");
                var notice = Notice;
                now["notice"] = new JsonObject { ["kind"] = notice.Kind, ["title"] = notice.Title, ["text"] = notice.Text, ["listening"] = notice.Listening, ["id"] = notice.Id };
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
                DeckPressed?.Invoke(query["do"]!, query["id"]);
                response.StatusCode = 204;
                break;
            case "/deck/turn" when query["what"] is "volume" or "seek" or "shift" or "zone" && int.TryParse(query["ticks"], out int ticks) && ticks != 0:
                DeckTurned?.Invoke(query["what"]!, Math.Clamp(ticks, -20, 20));
                response.StatusCode = 204;
                break;
            case "/deck/log":
                if (_deckLog.Admit(query["m"]) is { } line) Log.Write("stream dock: " + line);
                response.StatusCode = 204;
                break;
            default:
                response.StatusCode = 404;
                break;
        }
    }

    /// <summary>
    /// 一則權限請求：先查規則，符合就直接允許；不符合才問島上的人。回傳 "allow"、"deny" 或 null。
    /// 「以後都允許」的規則是照 Claude Code 的工具語意設計的：別的 agent 的請求既不比對規則、也不給記，每次都問人。
    /// </summary>
    public async Task<string?> PermissionAsync(JsonObject json, AgentKind agent = AgentKind.Claude)
    {
        bool claude = agent == AgentKind.Claude;
        if (AgentKinds.Normalize(agent, json) is not { } normalized) return null;
        json = normalized;
        var tool = json["tool_name"]?.ToString() ?? "?";
        var input = json["tool_input"];
        var cwd = json["cwd"]?.ToString() ?? "";
        var ask = new PermissionRequest
        {
            Project = AgentSessions.ProjectOf(cwd),
            Agent = agent,
            Tool = tool,
            Details = Describe(tool, input),
            SessionId = json["session_id"]?.ToString() ?? "",
            Cwd = cwd,
            ToolInput = input?.ToJsonString() ?? "{}",
            Remember = claude ? AgentRules.Suggest(tool, input, cwd) : null,
        };
        var key = AgentSessions.Key(json);

        if (claude && Rules.Match(tool, input, cwd) is { } rule)
        {
            Touch(json, agent);
            Change(s => s.Resumed(key));
            AutoAllowed?.Invoke(ask, rule);
            return "allow";
        }

        Touch(json, agent);
        if (Asked == null) return null;
        // 同一個 agent 已經有好幾則在等：不再排上島，照「Shima 不處理」回終端機問
        var waiting = _waiting[agent];
        if (!waiting.TryTake()) return null;
        try
        {
            Asked.Invoke(ask);
            // 人按了之後就把計時器取消，不然每問一次就留一個活九分多鐘的計時器
            using var giveUp = new CancellationTokenSource();
            var done = await Task.WhenAny(ask.Decision.Task, Task.Delay(Patience, giveUp.Token));
            giveUp.Cancel();
            var behavior = done == ask.Decision.Task ? ask.Decision.Task.Result : null;
            ask.Decision.TrySetResult(null);
            if (behavior == PermissionRequest.Always)
            {
                if (claude && ask.Remember is { } remember) Rules.Add(remember);
                behavior = "allow";
            }
            if (behavior is "allow" or "deny") Change(s => s.Resumed(key));
            return behavior;
        }
        finally { waiting.Give(); }
    }

    /// <summary>這個 agent 此刻有幾則權限請求在等人回答。</summary>
    public int Waiting(AgentKind agent) => _waiting[agent].Taken;

    /// <summary>不用等回答的 hook：Stop、PostToolUse、Notification、SessionStart、SessionEnd…</summary>
    public void Notify(JsonObject json, AgentKind agent = AgentKind.Claude)
    {
        if (AgentKinds.Normalize(agent, json) is not { } normalized) return;
        json = normalized;
        var name = json["hook_event_name"]?.ToString();
        var project = AgentSessions.ProjectOf(json["cwd"]?.ToString());
        bool planChanged = false;
        AgentSession? session = null;
        Change(s =>
        {
            bool changed = s.Apply(json, out planChanged, agent);
            session = s.Find(AgentSessions.Key(json));
            return changed;
        });
        if (name == "Stop")
        {
            var last = json["last_assistant_message"]?.ToString() ?? "";
            if (agent == AgentKind.Claude) Finished?.Invoke(project, last);
            AgentFinished?.Invoke(agent, project, last);
        }
        else if (planChanged && session != null) Progressed?.Invoke(project, session.Done, session.Total, session.Current);
    }

    private void Touch(JsonObject json, AgentKind agent) => Change(s => s.Apply(json, out _, agent));

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

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Shima.Playback;

/// <summary>
/// Roon 給 Windows 媒體控制的只有歌名和封面，沒有播放位置與長度，所以直接走它的擴充功能 API：
/// 一條連到 Roon Core 的 WebSocket。第一次要到 Roon「設定 → 擴充功能」啟用 Shima。
/// </summary>
public sealed class RoonSource : IMediaSource
{
    public const string AppId = "Roon";
    private const string Transport = "com.roonlabs.transport:2";
    private const int QueueSize = 100;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly object _lock = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Dictionary<string, JsonObject> _zones = [];
    private readonly Dictionary<string, string> _pending = [];
    private readonly Dictionary<string, TaskCompletionSource<MooMessage>> _calls = [];
    private readonly List<RoonQueueItem> _queue = [];
    private readonly SemaphoreSlim _browsing = new(1, 1);
    private readonly Dictionary<string, Task<BitmapImage?>> _thumbs = [];
    private string _queueZone = "", _queueRequest = "", _hierarchy = "search";
    private int _queueKey = -1, _nextQueueKey = 1;
    private bool _browseAvailable;
    private ClientWebSocket? _ws;
    private int _requestId, _httpPort;
    private bool _started;
    private string _host = "", _coreId = "";

    private string _pinned = "";
    private string _zoneId = "", _trackKey = "", _title = "", _artist = "", _album = "", _imageKey = "";
    private bool _playing, _stepped;
    private double _lastReported;
    private TimeSpan _position, _duration;
    private DateTimeOffset _at;
    private BitmapImage? _cover;

    public event Action? Changed;
    /// <summary>播放佇列變了（換歌、加歌、換區域）。從背景執行緒發出。</summary>
    public event Action? QueueChanged;

    public Track? Current
    {
        get
        {
            lock (_lock)
                return _zoneId.Length == 0 ? null
                    : new Track(AppId, AppId, _title, _artist, _album, _cover, _playing, _position, _at, _duration);
        }
    }

    public void Start()
    {
        var s = AppSettings.Current;
        if (_started || string.IsNullOrWhiteSpace(s.RoonHost)) return;
        _started = true;
        _host = s.RoonHost.Trim();
        _ = Task.Run(() => RunAsync(s.RoonPort));
    }

    private async Task RunAsync(int port)
    {
        bool logged = false;
        while (true)
        {
            try
            {
                using var ws = new ClientWebSocket();
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    await ws.ConnectAsync(new Uri($"ws://{_host}:{port}/api"), timeout.Token);
                lock (_lock) { _ws = ws; _pending.Clear(); _httpPort = port; }
                logged = false;
                await RequestAsync("com.roonlabs.registry:1/info", "info");
                await ReceiveAsync(ws);
            }
            catch (Exception e)
            {
                if (!logged) Log.Write("roon: " + e.Message);
                logged = true;
            }

            bool had, hadQueue;
            List<TaskCompletionSource<MooMessage>> calls;
            lock (_lock)
            {
                had = _zoneId.Length > 0; _ws = null; _zones.Clear(); Show(null);
                _registered = false;
                hadQueue = _queue.Count > 0;
                _queue.Clear(); _queueZone = _queueRequest = ""; _queueKey = -1;
                _browseAvailable = false;
                calls = [.. _calls.Values]; _calls.Clear();
            }
            foreach (var c in calls) c.TrySetException(new IOException("Roon Core 斷線"));
            if (had) Changed?.Invoke();
            if (hadQueue) QueueChanged?.Invoke();
            await Task.Delay(5000);
        }
    }

    private async Task ReceiveAsync(ClientWebSocket ws)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var r = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue;
            var msg = Moo.Parse(message.GetBuffer().AsSpan(0, (int)message.Length));
            message.SetLength(0);
            if (msg != null) await HandleAsync(msg);
        }
    }

    private async Task HandleAsync(MooMessage msg)
    {
        if (msg.Verb == "REQUEST")
        {
            // Core 定期確認我們還活著
            bool ping = msg.Name == "com.roonlabs.ping:1/ping";
            await SendAsync(Moo.Build("COMPLETE", ping ? "Success" : "InvalidRequest", msg.RequestId));
            return;
        }

        string? kind;
        TaskCompletionSource<MooMessage>? call;
        lock (_lock)
        {
            _pending.TryGetValue(msg.RequestId, out kind);
            if (_calls.TryGetValue(msg.RequestId, out call)) _calls.Remove(msg.RequestId);
            if (msg.Verb == "COMPLETE") _pending.Remove(msg.RequestId);
        }
        call?.TrySetResult(msg);

        switch (kind)
        {
            case "info":
                _coreId = msg.Body?["core_id"]?.ToString() ?? "";
                bool known = AppSettings.Current.RoonTokens.TryGetValue(_coreId, out var token);
                var reg = Registration(AppSettings.Current.RoonExtensionId,
                    typeof(RoonSource).Assembly.GetName().Version?.ToString(3) ?? "0.1.0", known ? token : null, AppSettings.Current.RoonSearch);
                if (!known) Log.Write("roon: 等待在 Roon「設定 → 擴充功能」啟用 Shima");
                await RequestAsync("com.roonlabs.registry:1/register", "register", reg);
                break;

            case "register" when msg.Name != "Registered":
                Log.Write($"roon: 註冊沒有成功（{msg.Verb} {msg.Name}）");
                break;

            case "register" when msg.Name == "Registered":
                var fresh = msg.Body?["token"]?.ToString() ?? "";
                var core = _coreId;
                if (fresh.Length > 0 && core.Length > 0)
                    AppSettings.Change(s =>
                    {
                        if (s.RoonTokens.TryGetValue(core, out var old) && old == fresh) return false;
                        s.RoonTokens[core] = fresh;
                        return true;
                    });
                Log.Write("roon: 已連上 " + msg.Body?["display_name"]);
                _registered = true;
                bool browse = Provides(msg.Body, RoonBrowse.Service);
                lock (_lock) _browseAvailable = browse;
                if (!browse && AppSettings.Current.RoonSearch) Log.Write("roon: Core 沒有提供瀏覽服務，搜尋不能用");
                await RequestAsync(Transport + "/subscribe_zones", "zones", new JsonObject { ["subscription_key"] = 0 });
                break;

            case "zones" when msg.Body is JsonObject body:
                bool newCover;
                string? remember = null;
                lock (_lock)
                {
                    if (msg.Name == "Subscribed") _zones.Clear();
                    foreach (var key in (string[])["zones", "zones_added", "zones_changed"])
                        foreach (var z in body[key] as JsonArray ?? [])
                            if (z is JsonObject zone && zone["zone_id"]?.ToString() is { } id) _zones[id] = zone;
                    foreach (var id in body["zones_removed"] as JsonArray ?? [])
                        _zones.Remove(id?.ToString() ?? "");
                    foreach (var s in body["zones_seek_changed"] as JsonArray ?? [])
                        if (_zones.TryGetValue(s?["zone_id"]?.ToString() ?? "", out var zone) && zone["now_playing"] is JsonObject np)
                            np["seek_position"] = Number(s?["seek_position"]);
                    var shownZone = Pick(_zones.Values, _zoneId, AppSettings.Current.RoonZone, _pinned);
                    newCover = Show(shownZone);
                    // 記住最後在播的區域：全部暫停或重開 Shima 之後，按播放要控制的是它
                    if (shownZone?["state"]?.ToString() == "playing" && AppSettings.Current.RoonZone != _zoneId) remember = _zoneId;
                }
                // 存檔要寫磁碟，不能拿著鎖做（畫面那邊讀現在的歌會被卡住），也不在這條執行緒做
                if (remember != null) AppSettings.Change(s => { if (s.RoonZone == remember) return false; s.RoonZone = remember; return true; });
                Changed?.Invoke();
                if (newCover) _ = LoadCoverAsync();
                await SyncQueueAsync();
                break;

            case "queue":
                bool queueChanged;
                int queued;
                lock (_lock) { queueChanged = msg.RequestId == _queueRequest && RoonQueue.Apply(_queue, msg.Name, msg.Body); queued = _queue.Count; }
                if (msg.Name == "Subscribed") Log.Write($"roon: 佇列 {queued} 首");
                if (queueChanged) QueueChanged?.Invoke();
                break;
        }
    }

    /// <summary>
    /// 註冊擴充功能的內容。瀏覽服務放在 optional_services：舊版只要求 transport，
    /// 這樣 Core 不給瀏覽也照樣能連，只是搜尋不能用。
    /// </summary>
    private volatile bool _registered;
    /// <summary>Core 接受我們了沒。連得上卻一直是 false，就是在等人到 Roon「設定 → 擴充功能」啟用。</summary>
    public bool Registered => _registered;

    public static JsonObject Registration(string extensionId, string version, string? token, bool browse = true)
    {
        var reg = new JsonObject
        {
            ["extension_id"] = extensionId,
            ["display_name"] = "Shima",
            ["display_version"] = version,
            ["publisher"] = "Carinoasd",
            ["email"] = "",
            ["website"] = "",
            ["required_services"] = new JsonArray(Transport),
            // 多要一項服務，Roon 會把已經配對過的擴充功能當成新的、等人重新啟用；所以搜尋要使用者自己打開才要
            ["optional_services"] = browse ? new JsonArray(RoonBrowse.Service) : new JsonArray(),
            ["provided_services"] = new JsonArray("com.roonlabs.ping:1"),
        };
        if (token != null) reg["token"] = token;
        return reg;
    }

    /// <summary>Registered 的回應裡 provided_services 有沒有這個服務（Core 實際給我們用的服務）。</summary>
    public static bool Provides(JsonNode? registered, string service) =>
        (registered?["provided_services"] as JsonArray ?? []).Any(s => s?.ToString() == service);

    /// <summary>顯示的區域換了，就把佇列訂閱搬過去。</summary>
    private readonly SemaphoreSlim _queueSync = new(1, 1);

    private async Task SyncQueueAsync()
    {
        // 畫面與接收訊息的執行緒都會叫到這裡；一次只讓一個做，取消舊訂閱與訂新的才不會交錯著送出去
        await _queueSync.WaitAsync();
        try { await SyncQueueLockedAsync(); }
        finally { _queueSync.Release(); }
    }

    private async Task SyncQueueLockedAsync()
    {
        string zone; int oldKey, key = -1; string id = "";
        lock (_lock)
        {
            if (_zoneId == _queueZone) return;
            if (_ws == null) return;   // 還沒連上：不要先記成「已經訂了」，等連上後那一次再訂
            zone = _zoneId; oldKey = _queueKey;
            _queueZone = zone; _queue.Clear(); _queueRequest = ""; _queueKey = -1;
            if (zone.Length > 0)
            {
                key = _queueKey = _nextQueueKey++;
                id = _queueRequest = (_requestId++).ToString();
                _pending[id] = "queue";
            }
        }
        QueueChanged?.Invoke();
        try
        {
            if (oldKey >= 0) await RequestAsync(Transport + "/unsubscribe_queue", "command", RoonQueue.Unsubscribe(oldKey));
            if (zone.Length > 0) await SendAsync(Moo.Build("REQUEST", Transport + "/subscribe_queue", id, RoonQueue.Subscribe(zone, QueueSize, key)));
        }
        catch (Exception e) { Log.Write("roon 佇列: " + e.Message); }
    }

    /// <summary>
    /// 要顯示（也就是按鍵要控制）的 zone。正在播的優先：指定的、目前顯示的、其他的；
    /// 都沒在播就用最後播過的那個，再不然留著目前的，最後才猜（停在歌曲中間的比停在開頭的像是剛剛在聽的）。
    /// </summary>
    public static JsonObject? Pick(IEnumerable<JsonObject> zones, string shownId, string lastPlayedId = "", string pinnedId = "")
    {
        var withTrack = zones.Where(z => z["now_playing"] is JsonObject).ToList();
        static bool Playing(JsonObject z) => z["state"]?.ToString() == "playing";
        JsonObject? Find(string id) => id.Length == 0 ? null : withTrack.FirstOrDefault(z => z["zone_id"]?.ToString() == id);
        JsonObject? pinned = Find(pinnedId), shown = Find(shownId);
        if (pinned != null && Playing(pinned)) return pinned;
        if (shown != null && Playing(shown)) return shown;
        return withTrack.FirstOrDefault(Playing) ?? pinned ?? Find(lastPlayedId) ?? shown
            ?? withTrack.OrderByDescending(z => z["state"]?.ToString() == "paused" && Number(z["now_playing"]!["seek_position"]) > 0).FirstOrDefault();
    }

    /// <summary>把 zone 的歌讀進欄位（呼叫前要先鎖）。回傳 true 表示要去抓新封面。</summary>
    private bool Show(JsonObject? zone)
    {
        if (zone?["now_playing"] is not JsonObject np)
        {
            _zoneId = _trackKey = _title = _artist = _album = _imageKey = "";
            _playing = false;
            _cover = null;
            return false;
        }

        var now = DateTimeOffset.Now;
        var lines = np["three_line"];
        _zoneId = zone["zone_id"]?.ToString() ?? "";
        _title = lines?["line1"]?.ToString() ?? "";
        _artist = lines?["line2"]?.ToString() ?? "";
        _album = lines?["line3"]?.ToString() ?? "";
        _duration = TimeSpan.FromSeconds(Number(np["length"]));
        bool playing = zone["state"]?.ToString() == "playing";
        double reported = Number(np["seek_position"]);
        var expected = _position + (_playing ? now - _at : TimeSpan.Zero);

        // Roon 每秒回報一次整數秒。數字跳動的那一瞬間才是準的，所以在那一刻對時，之後自己往下算；
        // 跟推算差太多就代表使用者拖了進度。
        var key = $"{_zoneId}\n{_title}\n{_artist}\n{_album}\n{_duration.Ticks}";
        if (key != _trackKey)
        {
            _trackKey = key;
            _position = TimeSpan.FromSeconds(reported);
            _at = now;
            _stepped = false;
        }
        else if (playing != _playing)
        {
            _position = expected;
            _at = now;
        }
        else if (reported != _lastReported)
        {
            bool step = playing && reported == _lastReported + 1;
            // 暫停時位置不會自己走，回報的數字變了就是有人拖了進度，差一秒也要跟
            if (!playing || Math.Abs(reported - expected.TotalSeconds) > 1.5 || step && !_stepped)
            {
                _position = TimeSpan.FromSeconds(reported);
                _at = now;
                _stepped = step;
            }
        }
        _lastReported = reported;
        _playing = playing;

        var image = np["image_key"]?.ToString() ?? "";
        if (image == _imageKey) return false;
        _imageKey = image;
        _cover = null;
        return image.Length > 0;
    }

    private static double Number(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    private async Task LoadCoverAsync()
    {
        string key; int port;
        lock (_lock) { key = _imageKey; port = _httpPort; }
        try
        {
            var bytes = await Http.GetByteArrayAsync($"http://{_host}:{port}/api/image/{Uri.EscapeDataString(key)}?scale=fit&width=320&height=320&format=image/jpeg");
            var image = Images.Decode(bytes);
            lock (_lock) { if (key != _imageKey) return; _cover = image; }
            Changed?.Invoke();
        }
        catch (Exception e) { Log.Write("roon 封面: " + e.Message); }
    }

    /// <summary>目前這個播放區域的名字。</summary>
    public string ZoneName { get { lock (_lock) return _zones.TryGetValue(_zoneId, out var z) ? z["display_name"]?.ToString() ?? "" : ""; } }

    /// <summary>換到下一個有歌的播放區域，並固定在那裡。回傳新的名字（只有一個區域就是空字串）。</summary>
    public string NextZone()
    {
        bool cover;
        string name;
        lock (_lock)
        {
            var ids = _zones.Where(z => z.Value["now_playing"] is JsonObject).Select(z => z.Key).OrderBy(k => k).ToList();
            if (ids.Count < 2) return "";
            _pinned = ids[(ids.IndexOf(_zoneId) + 1) % ids.Count];
            cover = Show(_zones[_pinned]);
            name = _zones[_pinned]["display_name"]?.ToString() ?? "";
        }
        Changed?.Invoke();
        if (cover) _ = LoadCoverAsync();
        _ = SyncQueueAsync();
        return name;
    }

    /// <summary>音量加減幾格。回傳調整後的音量；這個輸出不能調音量就是 null。</summary>
    public int? Nudge(int steps)
    {
        string output;
        double value, min, max, step;
        lock (_lock)
        {
            if (!_zones.TryGetValue(_zoneId, out var zone) || (zone["outputs"] as JsonArray)?.FirstOrDefault(o => o?["volume"] is JsonObject) is not { } o) return null;
            var volume = o["volume"]!;
            output = o["output_id"]?.ToString() ?? "";
            value = Number(volume["value"]); min = Number(volume["min"]); max = Number(volume["max"]);
            step = Math.Max(1, Number(volume["step"]));
            value = Math.Clamp(value + steps * step, min, max);
            volume["value"] = value;   // 先當作成功，Core 之後會回報實際的值
        }
        _ = RequestAsync(Transport + "/change_volume", "command", new JsonObject { ["output_id"] = output, ["how"] = "absolute", ["value"] = value });
        return (int)value;
    }

    /// <summary>把音量設到上下限之間的某個百分比（對齊它的最小刻度）。回傳設定後的音量；這個輸出不能調音量就是 null。</summary>
    public int? SetVolume(int percent)
    {
        string output;
        double value;
        lock (_lock)
        {
            if (!_zones.TryGetValue(_zoneId, out var zone) || (zone["outputs"] as JsonArray)?.FirstOrDefault(o => o?["volume"] is JsonObject) is not { } o) return null;
            var volume = o["volume"]!;
            output = o["output_id"]?.ToString() ?? "";
            double min = Number(volume["min"]), max = Number(volume["max"]), step = Math.Max(1, Number(volume["step"]));
            value = Math.Clamp(min + Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * (max - min) / step) * step, min, max);
            volume["value"] = value;
        }
        _ = RequestAsync(Transport + "/change_volume", "command", new JsonObject { ["output_id"] = output, ["how"] = "absolute", ["value"] = value });
        return (int)value;
    }

    /// <summary>目前區域的音量、上下限與是否靜音；這個區域不能調音量就是 null。</summary>
    public (double Value, double Min, double Max, bool Muted)? Sound
    {
        get
        {
            lock (_lock)
            {
                if (!_zones.TryGetValue(_zoneId, out var zone) || (zone["outputs"] as JsonArray)?.FirstOrDefault(o => o?["volume"] is JsonObject) is not { } o) return null;
                var volume = o["volume"]!;
                return (Number(volume["value"]), Number(volume["min"]), Number(volume["max"]), volume["is_muted"] is JsonValue v && v.TryGetValue<bool>(out var m) && m);
            }
        }
    }

    /// <summary>靜音開關。回傳現在是不是靜音；這個區域不能調音量就是 null。</summary>
    public bool? ToggleMute()
    {
        string output;
        bool muted;
        lock (_lock)
        {
            if (!_zones.TryGetValue(_zoneId, out var zone) || (zone["outputs"] as JsonArray)?.FirstOrDefault(o => o?["volume"] is JsonObject) is not { } o) return null;
            var volume = o["volume"]!;
            output = o["output_id"]?.ToString() ?? "";
            muted = !(volume["is_muted"] is JsonValue v && v.TryGetValue<bool>(out var m) && m);
            volume["is_muted"] = muted;
        }
        _ = RequestAsync(Transport + "/mute", "command", new JsonObject { ["output_id"] = output, ["how"] = muted ? "mute" : "unmute" });
        return muted;
    }

    // ───── 佇列與播放設定 ─────

    /// <summary>目前區域的播放佇列，第 0 首是正在播的那首。</summary>
    public IReadOnlyList<RoonQueueItem> Queue { get { lock (_lock) return [.. _queue]; } }

    /// <summary>目前區域的隨機、循環、自動電台；沒有區域就是 null。設定變了會發 Changed。</summary>
    public RoonZoneSettings? Settings { get { lock (_lock) return _zones.TryGetValue(_zoneId, out var z) ? RoonQueue.Settings(z) : null; } }

    /// <summary>從佇列裡的這一首開始播。</summary>
    public void PlayFromHere(long queueItemId)
    {
        string zone;
        lock (_lock) zone = _zoneId;
        if (zone.Length > 0) _ = Safe(RequestAsync(Transport + "/play_from_here", "command", RoonQueue.PlayFromHere(zone, queueItemId)));
    }

    public void ToggleShuffle() => ChangeSettings(s => RoonQueue.ChangeSettings("", shuffle: !s.Shuffle));
    /// <summary>循環：關 → 整份 → 單曲 → 關（交給 Roon 輪替）。</summary>
    public void CycleLoop() => ChangeSettings(_ => RoonQueue.ChangeSettings("", loop: "next"));
    public void ToggleAutoRadio() => ChangeSettings(s => RoonQueue.ChangeSettings("", autoRadio: !s.AutoRadio));

    private void ChangeSettings(Func<RoonZoneSettings, JsonObject> make)
    {
        string zone; JsonObject body;
        lock (_lock)
        {
            if (!_zones.TryGetValue(_zoneId, out var z) || RoonQueue.Settings(z) is not { } s) return;
            zone = _zoneId;
            body = make(s);
        }
        body["zone_or_output_id"] = zone;
        _ = Safe(RequestAsync(Transport + "/change_settings", "command", body));
    }

    private static async Task Safe(Task t) { try { await t; } catch { /* Core 斷線了 */ } }

    // ───── 瀏覽與搜尋 ─────

    /// <summary>Core 有沒有給我們瀏覽服務。沒有的話搜尋、瀏覽都會回 null。</summary>
    public bool BrowseAvailable { get { lock (_lock) return _browseAvailable; } }

    /// <summary>搜尋。回傳最上層的結果（通常是「藝人、專輯、曲目…」幾組，要再往下走）。</summary>
    public Task<RoonBrowsePage?> SearchAsync(string query) => BrowseAsync("search", zone => RoonBrowse.Search(query, zone));

    /// <summary>從最上層開始一般瀏覽（音樂庫、播放清單、電台…）。</summary>
    public Task<RoonBrowsePage?> HomeAsync() => BrowseAsync("browse", zone => RoonBrowse.Home(zone));

    /// <summary>
    /// 點了一項：Page 不是 null 就是下一層；是 null 表示這一項是動作，已經執行了
    /// （Result.Message 有字就該顯示，Result.IsError 表示失敗）。
    /// 有 input_prompt 的項目（例如瀏覽裡的「搜尋」）把使用者輸入的字放在 input。
    /// </summary>
    public async Task<(RoonBrowseResult Result, RoonBrowsePage? Page)> OpenAsync(RoonBrowseItem item, string? input = null)
    {
        await _browsing.WaitAsync();
        try { return await OpenLockedAsync(item, input); }
        finally { _browsing.Release(); }
    }

    /// <summary>回上一層。</summary>
    public Task<RoonBrowsePage?> BackAsync()
    {
        string hierarchy;
        lock (_lock) hierarchy = _hierarchy;
        return BrowseAsync(hierarchy, zone => RoonBrowse.Back(1, zone, hierarchy));
    }

    /// <summary>
    /// 直接播放某個結果：一路往下走到動作清單，執行第一個動作（Roon 的預設動作，通常是「立即播放」）。
    /// 回傳執行的結果；走不到可以播的動作就是 null。
    /// </summary>
    public async Task<RoonBrowseResult?> PlayAsync(RoonBrowseItem item)
    {
        await _browsing.WaitAsync();
        try { return await PlayLockedAsync(item); }
        finally { _browsing.Release(); }   // 每一條路都要放掉，包含中途就播成功直接回傳的那一條
    }

    private async Task<RoonBrowseResult?> PlayLockedAsync(RoonBrowseItem item)
    {
        int opened = 0;
        try
        {
            for (int depth = 0; depth < 6 && !item.IsHeader; depth++)
            {
                var (result, page) = await OpenLockedAsync(item, null);
                if (page == null) return result;   // 動作執行了，Roon 會自己退回去
                opened++;
                if (RoonBrowse.NextStep(page) is not { } next) break;
                item = next;
            }
        }
        catch (Exception e) { Log.Write("roon 瀏覽: " + e.Message); }
        try
        {
            // 沒播成：把剛剛往下走的層數退回去，Core 那邊的位置才會跟畫面上的清單一致（pop_levels 要給剛好的數字）
            if (opened > 0)
            {
                string zone, hierarchy;
                lock (_lock) { zone = _zoneId; hierarchy = _hierarchy; }
                await BrowseCallAsync(RoonBrowse.Back(opened, zone, hierarchy));
            }
        }
        catch (Exception e) { Log.Write("roon 瀏覽: " + e.Message); }
        return null;
    }

    private async Task<RoonBrowsePage?> BrowseAsync(string hierarchy, Func<string, JsonObject> make)
    {
        await _browsing.WaitAsync();
        try
        {
            string zone;
            lock (_lock) { if (!_browseAvailable) return null; zone = _zoneId; _hierarchy = hierarchy; }
            var result = await BrowseCallAsync(make(zone));
            return result.Action == "list" ? await LoadAsync(hierarchy) : null;
        }
        catch (Exception e) { Log.Write("roon 瀏覽: " + e.Message); return null; }
        finally { _browsing.Release(); }
    }

    private async Task<(RoonBrowseResult Result, RoonBrowsePage? Page)> OpenLockedAsync(RoonBrowseItem item, string? input)
    {
        string zone, hierarchy;
        lock (_lock)
        {
            if (!_browseAvailable) return (new RoonBrowseResult("message", "", true, null), null);
            zone = _zoneId; hierarchy = _hierarchy;
        }
        var result = await BrowseCallAsync(RoonBrowse.Open(item.ItemKey, zone, hierarchy, input));
        return result.Action == "list" ? (result, await LoadAsync(hierarchy)) : (result, null);
    }

    private async Task<RoonBrowseResult> BrowseCallAsync(JsonObject body)
    {
        var msg = await CallAsync(RoonBrowse.Service + "/browse", body);
        if (msg.Name != "Success") return new RoonBrowseResult("message", msg.Body?["message"]?.ToString() ?? msg.Name, true, null);
        return RoonBrowse.Result(msg.Body);
    }

    private async Task<RoonBrowsePage?> LoadAsync(string hierarchy)
    {
        var msg = await CallAsync(RoonBrowse.Service + "/load", RoonBrowse.Load(hierarchy));
        return msg.Name == "Success" ? RoonBrowse.Page(msg.Body) : null;
    }

    /// <summary>送出請求並等 Core 回 COMPLETE。斷線或逾時會丟例外。</summary>
    private async Task<MooMessage> CallAsync(string name, JsonNode body)
    {
        string id;
        var done = new TaskCompletionSource<MooMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_ws == null) throw new IOException("Roon Core 沒有連上");
            id = (_requestId++).ToString();
            _calls[id] = done;
        }
        try
        {
            await SendAsync(Moo.Build("REQUEST", name, id, body));
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { lock (_lock) _calls.Remove(id); }
    }

    /// <summary>
    /// 佇列或瀏覽結果的小封面（同一個 image_key 只抓一次）。抓不到就是 null。
    /// </summary>
    public Task<BitmapImage?> ThumbnailAsync(string imageKey, int size = 96)
    {
        if (imageKey.Length == 0) return Task.FromResult<BitmapImage?>(null);
        lock (_lock)
        {
            var cacheKey = $"{imageKey}@{size}";
            if (_thumbs.TryGetValue(cacheKey, out var cached)) return cached;
            if (_thumbs.Count > 300) _thumbs.Clear();
            string url = ImageUrl(_host, _httpPort, imageKey, size);
            return _thumbs[cacheKey] = FetchAsync(url, size);
        }

        async Task<BitmapImage?> FetchAsync(string url, int size)
        {
            BitmapImage? image = null;
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = size;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
            }
            catch (Exception e) { Log.Write("roon 縮圖: " + e.Message); image = null; }
            // 抓不到的不要記住，下次再試
            if (image == null) lock (_lock) _thumbs.Remove($"{imageKey}@{size}");
            return image;
        }
    }

    public static string ImageUrl(string host, int port, string imageKey, int size) =>
        $"http://{host}:{port}/api/image/{Uri.EscapeDataString(imageKey)}?scale=fit&width={size}&height={size}&format=image/jpeg";

    public void PlayPause() => Command("control", new JsonObject { ["control"] = "playpause" });
    public void Play() => Command("control", new JsonObject { ["control"] = "play" });
    public void Pause() => Command("control", new JsonObject { ["control"] = "pause" });

    /// <summary>所有正在播的 zone 的 id（睡眠定時要全部暫停，不只是島上顯示的那個）。</summary>
    public static List<string> PlayingZones(IEnumerable<JsonObject> zones) =>
        zones.Where(z => z["state"]?.ToString() == "playing" && z["zone_id"]?.ToString() is { Length: > 0 }).Select(z => z["zone_id"]!.ToString()).ToList();

    /// <summary>每個正在播的 zone 各送一次暫停（送法與 Command 相同）。</summary>
    public async void PauseAll()
    {
        try
        {
            List<string> ids;
            lock (_lock) ids = PlayingZones(_zones.Values);
            foreach (var id in ids)
                await RequestAsync($"{Transport}/control", "command", new JsonObject { ["control"] = "pause", ["zone_or_output_id"] = id });
        }
        catch { /* Core 斷線了 */ }
    }
    public void Next() => Command("control", new JsonObject { ["control"] = "next" });
    public void Previous() => Command("control", new JsonObject { ["control"] = "previous" });
    public void Seek(TimeSpan position) =>
        Command("seek", new JsonObject { ["how"] = "absolute", ["seconds"] = Math.Round(position.TotalSeconds) });

    private async void Command(string name, JsonObject body)
    {
        try
        {
            lock (_lock) { if (_zoneId.Length == 0) return; body["zone_or_output_id"] = _zoneId; }
            await RequestAsync($"{Transport}/{name}", "command", body);
        }
        catch { /* Core 斷線了 */ }
    }

    private Task RequestAsync(string name, string kind, JsonNode? body = null)
    {
        string id;
        lock (_lock) { id = (_requestId++).ToString(); _pending[id] = kind; }
        return SendAsync(Moo.Build("REQUEST", name, id, body));
    }

    private async Task SendAsync(byte[] data)
    {
        await _sending.WaitAsync();
        try
        {
            // 排到了才看連線：等的時候可能已經斷線、那條連線也被釋放了
            ClientWebSocket? ws;
            lock (_lock) ws = _ws;
            if (ws is not { State: WebSocketState.Open }) return;
            await ws.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        catch (Exception e) when (e is ObjectDisposedException or WebSocketException or InvalidOperationException) { /* 剛好斷線：重連後會重新訂閱 */ }
        finally { _sending.Release(); }
    }
}

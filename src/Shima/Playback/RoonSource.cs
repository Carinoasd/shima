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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly object _lock = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Dictionary<string, JsonObject> _zones = [];
    private readonly Dictionary<string, string> _pending = [];
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

            bool had;
            lock (_lock) { had = _zoneId.Length > 0; _ws = null; _zones.Clear(); Show(null); }
            if (had) Changed?.Invoke();
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
        lock (_lock)
        {
            _pending.TryGetValue(msg.RequestId, out kind);
            if (msg.Verb == "COMPLETE") _pending.Remove(msg.RequestId);
        }

        switch (kind)
        {
            case "info":
                _coreId = msg.Body?["core_id"]?.ToString() ?? "";
                var reg = new JsonObject
                {
                    ["extension_id"] = AppSettings.Current.RoonExtensionId,
                    ["display_name"] = "Shima",
                    ["display_version"] = typeof(RoonSource).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    ["publisher"] = "Carinoasd",
                    ["email"] = "",
                    ["website"] = "",
                    ["required_services"] = new JsonArray(Transport),
                    ["optional_services"] = new JsonArray(),
                    ["provided_services"] = new JsonArray("com.roonlabs.ping:1"),
                };
                if (AppSettings.Current.RoonTokens.TryGetValue(_coreId, out var token)) reg["token"] = token;
                else Log.Write("roon: 等待在 Roon「設定 → 擴充功能」啟用 Shima");
                await RequestAsync("com.roonlabs.registry:1/register", "register", reg);
                break;

            case "register" when msg.Name == "Registered":
                var fresh = msg.Body?["token"]?.ToString() ?? "";
                var tokens = AppSettings.Current.RoonTokens;
                if (fresh.Length > 0 && _coreId.Length > 0 && (!tokens.TryGetValue(_coreId, out var old) || old != fresh))
                {
                    tokens[_coreId] = fresh;
                    AppSettings.Current.Save();
                }
                Log.Write("roon: 已連上 " + msg.Body?["display_name"]);
                await RequestAsync(Transport + "/subscribe_zones", "zones", new JsonObject { ["subscription_key"] = 0 });
                break;

            case "zones" when msg.Body is JsonObject body:
                bool newCover;
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
                    // 使用者指定過播放區域就一直看它（它還在、還有歌的話）
                    var pinned = _pinned.Length > 0 && _zones.TryGetValue(_pinned, out var p) && p["now_playing"] is JsonObject ? p : null;
                    newCover = Show(pinned ?? Pick(_zones.Values, _zoneId));
                }
                Changed?.Invoke();
                if (newCover) _ = LoadCoverAsync();
                break;
        }
    }

    /// <summary>要顯示的 zone：目前這個還在播就不換，否則換到正在播的，再不然留著暫停的那個。</summary>
    public static JsonObject? Pick(IEnumerable<JsonObject> zones, string shownId)
    {
        var withTrack = zones.Where(z => z["now_playing"] is JsonObject).ToList();
        static bool Playing(JsonObject z) => z["state"]?.ToString() == "playing";
        var shown = withTrack.FirstOrDefault(z => z["zone_id"]?.ToString() == shownId);
        if (shown != null && Playing(shown)) return shown;
        return withTrack.FirstOrDefault(Playing) ?? shown ?? withTrack.FirstOrDefault();
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
            if (Math.Abs(reported - expected.TotalSeconds) > 1.5 || step && !_stepped)
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

    public void PlayPause() => Command("control", new JsonObject { ["control"] = "playpause" });
    public void Play() => Command("control", new JsonObject { ["control"] = "play" });
    public void Pause() => Command("control", new JsonObject { ["control"] = "pause" });
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
        ClientWebSocket? ws;
        lock (_lock) ws = _ws;
        if (ws == null) return;
        await _sending.WaitAsync();
        try { await ws.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None); }
        finally { _sending.Release(); }
    }
}

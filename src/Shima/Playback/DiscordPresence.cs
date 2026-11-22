// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shima.Playback;

/// <summary>
/// 在 Discord 個人狀態顯示「正在聽」：歌名、藝人、專輯、封面、進度。
/// 走 Discord 桌面版的本機管線，Discord 重開會自動重連。Discord 限制每 20 秒最多 5 次更新，
/// 所以兩次送出至少隔 4.5 秒，中間只留最新的一筆。
/// </summary>
public sealed class DiscordPresence
{
    private const int Handshake = 0, Frame = 1, Close = 2, Ping = 3, Pong = 4;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(4.5);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly string _clientId;
    private readonly object _lock = new();
    private readonly Dictionary<string, string?> _covers = [];
    private NamedPipeClientStream? _pipe;
    private bool _ready, _started, _hasPending;
    private volatile bool _stopped;
    private JsonObject? _pending;
    private DateTime _sentAt;
    private string _shownKey = "";
    private long _shownStart;
    private int _nonce, _revision;

    public DiscordPresence(string clientId) => _clientId = clientId;

    public static bool ValidId(string id) => id.Length is >= 17 and <= 20 && id.All(char.IsAsciiDigit);

    public void Start()
    {
        if (_started || !ValidId(_clientId)) return;
        _started = true;
        _ = Task.Run(RunAsync);
    }

    /// <summary>不再使用（換了 Application ID）：關掉連線，Discord 上的狀態會跟著消失。</summary>
    public void Stop() => _stopped = true;

    /// <summary>正在播的歌變了（或暫停、拖了進度）。沒在播就清掉狀態。</summary>
    public void Update(Track? track)
    {
        if (!_started) return;
        if (track is not { Playing: true } || track.Title.Length == 0)
        {
            if (_shownKey.Length > 0) { _shownKey = ""; _revision++; Queue(null); }
            return;
        }

        long start = DateTimeOffset.Now.ToUnixTimeMilliseconds() - (long)track.Now.TotalMilliseconds;
        // 同一首歌只在進度明顯跳動時才重送
        if (track.Key == _shownKey && Math.Abs(start - _shownStart) < 3000) return;
        _shownKey = track.Key;
        _shownStart = start;
        int revision = ++_revision;
        _ = Task.Run(async () =>
        {
            var cover = AppSettings.Current.DiscordCover ? await CoverAsync(track) : null;
            if (revision == _revision) Queue(Activity(track.Title, track.Artist, track.Album, start, track.Duration, cover));
        });
    }

    /// <summary>Discord 的 activity。文字欄位要 2 到 128 個字元，不符合整筆會被默默拒收。</summary>
    public static JsonObject Activity(string title, string artist, string album, long startMs, TimeSpan duration, string? coverUrl)
    {
        var timestamps = new JsonObject { ["start"] = startMs };
        if (duration > TimeSpan.Zero) timestamps["end"] = startMs + (long)duration.TotalMilliseconds;
        var activity = new JsonObject
        {
            ["type"] = 2,                  // 正在聽
            ["status_display_type"] = 2,   // 成員清單顯示歌名
            ["details"] = Fit(title),
            ["timestamps"] = timestamps,
        };
        if (Fit(artist) is { Length: > 0 } state) activity["state"] = state;
        if (coverUrl is { Length: <= 256 } && coverUrl.StartsWith("https://", StringComparison.Ordinal))
        {
            var assets = new JsonObject { ["large_image"] = coverUrl };
            if (Fit(album) is { Length: > 0 } text) assets["large_text"] = text;
            activity["assets"] = assets;
        }
        return activity;
    }

    public static string Fit(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "";
        if (text.Length > 128)
        {
            int cut = 127;
            if (char.IsHighSurrogate(text[cut - 1])) cut--;   // 不要把一個 emoji 切成兩半
            text = text[..cut] + "…";
        }
        while (text.Length < 2) text += "​";
        return text;
    }

    /// <summary>
    /// Discord 要公開網址的圖，本機的封面用不上，所以到 iTunes Search 查一張（歌名、藝人、專輯會送到 Apple）。
    /// 同一張專輯在各國商店譯名不同，依序查台灣、日本、美國。
    /// </summary>
    private async Task<string?> CoverAsync(Track track)
    {
        var key = $"{track.Title}\n{track.Artist}\n{track.Album}";
        lock (_covers) if (_covers.TryGetValue(key, out var known)) return known;

        static string N(string s) => new(s.ToLowerInvariant().Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).ToArray());
        static bool Close(string a, string b) { string x = N(a), y = N(b); return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x)); }
        var artist = Lyrics.Titles.MainArtist(track.Artist);
        string? found = null;
        try
        {
            foreach (var country in (string[])["TW", "JP", "US"])
            {
                foreach (var (term, entity, name) in new[] { ($"{artist} {track.Album}", "album", "collectionName"), ($"{track.Title} {artist}", "song", "trackName") })
                {
                    if (entity == "album" && track.Album.Length == 0) continue;
                    var json = await Http.GetStringAsync($"https://itunes.apple.com/search?term={Uri.EscapeDataString(term)}&entity={entity}&limit=10&country={country}");
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("results", out var results)) continue;
                    foreach (var r in results.EnumerateArray())
                    {
                        string Get(string p) => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                        if (!Close(Get("artistName"), artist) || !Close(Get(name), entity == "album" ? track.Album : track.Title)) continue;
                        if (Get("artworkUrl100") is { Length: > 0 } art) { found = art.Replace("100x100bb", "512x512bb"); break; }
                    }
                    if (found != null) break;
                }
                if (found != null) break;
            }
        }
        catch (Exception e) { Log.Write("discord cover: " + e.Message); return null; }
        lock (_covers) _covers[key] = found;
        return found;
    }

    private void Queue(JsonObject? activity)
    {
        lock (_lock) { _pending = activity; _hasPending = true; }
    }

    private async Task RunAsync()
    {
        while (!_stopped)
        {
            try
            {
                var pipe = await ConnectAsync();
                if (pipe != null)
                {
                    lock (_lock) { _pipe = pipe; _ready = false; }
                    await WriteAsync(pipe, Handshake, new JsonObject { ["v"] = 1, ["client_id"] = _clientId });
                    var reader = ReadAsync(pipe);
                    while (pipe.IsConnected && !reader.IsCompleted && !_stopped)
                    {
                        JsonObject? activity = null;
                        bool send = false;
                        lock (_lock)
                        {
                            if (_ready && _hasPending && DateTime.Now - _sentAt >= MinInterval)
                            {
                                activity = _pending; _hasPending = false; _sentAt = DateTime.Now; send = true;
                            }
                        }
                        if (send)
                            await WriteAsync(pipe, Frame, new JsonObject
                            {
                                ["cmd"] = "SET_ACTIVITY",
                                ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity?.DeepClone() },
                                ["nonce"] = (++_nonce).ToString(),
                            });
                        await Task.Delay(300);
                    }
                    pipe.Dispose();
                }
            }
            catch (Exception e) { Log.Write("discord: " + e.Message); }

            // 斷線後重連時把目前的狀態再送一次
            lock (_lock) { _pipe = null; _ready = false; _hasPending = _pending != null; }
            if (!_stopped) await Task.Delay(10000);
        }
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(400); return pipe; }
            catch { pipe.Dispose(); }
        }
        return null;
    }

    private static async Task WriteAsync(Stream pipe, int op, JsonObject payload)
    {
        var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        await pipe.WriteAsync(frame);
        await pipe.FlushAsync();
    }

    private async Task ReadAsync(NamedPipeClientStream pipe)
    {
        var header = new byte[8];
        try
        {
            while (pipe.IsConnected)
            {
                await pipe.ReadExactlyAsync(header);
                int op = BinaryPrimitives.ReadInt32LittleEndian(header), length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                if (length is < 0 or > 1 << 20) return;
                var body = new byte[length];
                await pipe.ReadExactlyAsync(body);
                if (op == Close) return;
                if (op == Ping) { await WriteAsync(pipe, Pong, JsonNode.Parse(body) as JsonObject ?? []); continue; }
                if (op == Frame && JsonNode.Parse(body)?["evt"]?.ToString() == "READY")
                {
                    lock (_lock) _ready = true;
                    Log.Write("discord: 已連上");
                }
            }
        }
        catch { /* Discord 關掉了 */ }
    }
}

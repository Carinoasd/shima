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
/// 程式結束或換 Application ID 時呼叫 Stop()（或 Dispose()，兩者相同）：馬上關掉管線，Discord 上的狀態跟著消失。
/// </summary>
public sealed class DiscordPresence : IDisposable
{
    private const int Handshake = 0, Frame = 1, Close = 2, Ping = 3, Pong = 4;
    private static readonly TimeSpan MinInterval = PresenceSchedule.MinInterval;
    private static readonly TimeSpan SafetyWait = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 2 * 1024 * 1024 };   // iTunes 搜尋結果只是十筆 JSON

    private readonly string _clientId;
    private readonly object _lock = new();
    private readonly Dictionary<string, string?> _covers = [];
    private NamedPipeClientStream? _pipe;
    private bool _started;
    private volatile bool _stopped;
    private readonly CancellationTokenSource _stopping = new();
    private readonly PresenceSchedule _schedule = new();
    /// <summary>有事要做（新的狀態、連線就緒、連線斷了、要停了）就叫醒送出的迴圈；沒事時它整個睡著，不輪詢。</summary>
    private readonly SemaphoreSlim _wake = new(0, 1);
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

    /// <summary>
    /// 不再使用（換了 Application ID、程式要結束）：關掉連線，Discord 上的狀態會跟著消失。
    /// 直接關掉管線、打斷重連前的等待，不用等背景迴圈下一輪才發現。呼叫幾次都可以。
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        NamedPipeClientStream? pipe;
        lock (_lock) { pipe = _pipe; _pipe = null; }
        try { _stopping.Cancel(); } catch (ObjectDisposedException) { }
        Wake();
        try { pipe?.Dispose(); } catch { /* 已經斷了 */ }
    }

    public void Dispose() => Stop();

    /// <summary>正在播的歌變了（或暫停、拖了進度）。沒在播就清掉狀態。</summary>
    public void Update(Track? track)
    {
        if (!_started) return;
        if (track is not { Playing: true } || track.Title.Length == 0)
        {
            if (_shownKey.Length > 0) { _shownKey = ""; Interlocked.Increment(ref _revision); Queue(null); }
            return;
        }

        long start = DateTimeOffset.Now.ToUnixTimeMilliseconds() - (long)track.Now.TotalMilliseconds;
        // 同一首歌只在進度明顯跳動時才重送
        if (track.Key == _shownKey && Math.Abs(start - _shownStart) < 3000) return;
        _shownKey = track.Key;
        _shownStart = start;
        // Update 在畫面的執行緒，查封面在背景：查完時歌已經又換了就丟掉這筆，所以兩邊要看到同一個 _revision
        int revision = Interlocked.Increment(ref _revision);
        _ = Task.Run(async () =>
        {
            var cover = AppSettings.Current.DiscordCover ? await CoverAsync(track) : null;
            if (revision == Volatile.Read(ref _revision)) Queue(Activity(track.Title, track.Artist, track.Album, start, track.Duration, cover));
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
        _schedule.Set(activity);
        Wake();
    }

    private void Wake()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { /* 已經有人叫過了 */ }
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
                    // 送狀態（這個迴圈）和回 Pong（ReadAsync）會同時寫同一條管線；不排隊的話兩個封包的位元組會交錯，
                    // Discord 讀到壞掉的封包就斷線。每條連線一把，斷線重連就換新的，不會被上一條卡住的寫入拖累。
                    // 沒用到 AvailableWaitHandle，不 Dispose 也不會留下系統資源，所以不必等 ReadAsync 真的結束。
                    var gate = new SemaphoreSlim(1, 1);
                    lock (_lock)
                    {
                        if (_stopped) { pipe.Dispose(); break; }
                        _pipe = pipe;
                    }
                    _schedule.SetReady(false);
                    // 寫到一半斷線會丟例外，也要把這條管線關掉
                    try
                    {
                        await WriteAsync(pipe, gate, Handshake, new JsonObject { ["v"] = 1, ["client_id"] = _clientId });
                        var reader = ReadAsync(pipe, gate);
                        _ = reader.ContinueWith(_ => Wake(), TaskScheduler.Default);
                        while (pipe.IsConnected && !reader.IsCompleted && !_stopped)
                        {
                            var step = _schedule.Next(DateTime.Now);
                            if (step.Activity is { } send)
                            {
                                await WriteAsync(pipe, gate, Frame, new JsonObject
                                {
                                    ["cmd"] = "SET_ACTIVITY",
                                    ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = send.Value?.DeepClone() },
                                    ["nonce"] = (++_nonce).ToString(),
                                });
                                continue;
                            }
                            // 有東西要送但還沒到最小間隔就睡到那一刻；沒東西就一直睡，事件會叫醒。長的上限只是保險（IsConnected 偶爾看一下）
                            try { await _wake.WaitAsync(step.Wait ?? SafetyWait, _stopping.Token); }
                            catch (OperationCanceledException) { break; }
                        }
                    }
                    finally { pipe.Dispose(); }
                }
            }
            catch (Exception e) { Log.Write("discord: " + e.Message); }

            // 斷線後重連時把目前的狀態再送一次
            lock (_lock) _pipe = null;
            _schedule.Reconnect();
            if (_stopped) break;
            try { await Task.Delay(10000, _stopping.Token); }
            catch (OperationCanceledException) { break; }
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

    /// <summary>送一個封包。同一條連線的寫入一定要經過同一把 gate，封包才不會互相插隊。</summary>
    public static async Task WriteAsync(Stream pipe, SemaphoreSlim gate, int op, JsonObject payload)
    {
        var json = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        await gate.WaitAsync();
        try
        {
            await pipe.WriteAsync(frame);
            await pipe.FlushAsync();
        }
        finally { gate.Release(); }
    }

    private async Task ReadAsync(NamedPipeClientStream pipe, SemaphoreSlim gate)
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
                if (op == Ping) { await WriteAsync(pipe, gate, Pong, JsonNode.Parse(body) as JsonObject ?? []); continue; }
                if (op == Frame && JsonNode.Parse(body)?["evt"]?.ToString() == "READY")
                {
                    _schedule.SetReady(true);
                    Wake();
                    Log.Write("discord: 已連上");
                }
            }
        }
        catch { /* Discord 關掉了 */ }
    }
}

/// <summary>
/// 決定什麼時候該把哪一筆狀態送給 Discord（不碰管線，所以可以測）：連線就緒、有東西等著、離上次送出已經過了最小間隔，三者都成立才送；
/// 還差間隔就回報還要等多久，沒東西可送就不回報時間（睡到有人叫醒為止）。時鐘由呼叫端給。
/// </summary>
public sealed class PresenceSchedule
{
    /// <summary>Discord 每 20 秒最多收 5 次更新。</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(4.5);

    /// <summary>Activity.HasValue 就是要送（Value 為 null 表示清除狀態）；否則 Wait 是還要等多久，null 表示沒東西可送。</summary>
    public readonly record struct Step(Pending? Activity, TimeSpan? Wait);

    public readonly record struct Pending(JsonObject? Value);

    private readonly object _lock = new();
    private JsonObject? _pending;
    private bool _hasPending, _ready;
    private DateTime _sentAt;

    /// <summary>只留最新的一筆（null 是清除）。</summary>
    public void Set(JsonObject? activity) { lock (_lock) { _pending = activity; _hasPending = true; } }

    public void SetReady(bool ready) { lock (_lock) _ready = ready; }

    /// <summary>斷線重連：還沒就緒，而且把目前的狀態再送一次（原本就是清除的就不用）。</summary>
    public void Reconnect() { lock (_lock) { _ready = false; _hasPending = _pending != null; } }

    /// <summary>現在該做什麼；回報「要送」的同時就當作已經送出了。</summary>
    public Step Next(DateTime now)
    {
        lock (_lock)
        {
            if (!_ready || !_hasPending) return new(null, null);
            var elapsed = now - _sentAt;
            // 時鐘被往回調：不要因此卡住很久
            if (elapsed < TimeSpan.Zero) elapsed = MinInterval;
            if (elapsed < MinInterval) return new(null, MinInterval - elapsed);
            _hasPending = false;
            _sentAt = now;
            return new(new Pending(_pending), null);
        }
    }
}

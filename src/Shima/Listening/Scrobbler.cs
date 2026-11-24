// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Playback;

namespace Shima.Listening;

/// <summary>
/// ListenBrainz 的 POST /1/submit-listens 要送的 JSON（https://listenbrainz.readthedocs.io/en/latest/users/json.html）。
/// playing_now：剛開始聽，只暫存、不能有 listened_at；single：聽完一首，listened_at 是開始播放的 UNIX 秒（UTC）。
/// 沒有資料的欄位整個不寫。
/// </summary>
public static class ListenBrainzJson
{
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0";

    /// <summary>官方建議：聽了一半或 4 分鐘（取比較短的）才算；不知道長度就要聽滿 4 分鐘。</summary>
    public static bool ShouldSubmit(double listened, double duration) =>
        listened >= (duration > 0 ? Math.Min(240, duration / 2) : 240);

    /// <summary>歌名與藝人是必填，缺一個就不送。</summary>
    public static bool CanSubmit(string title, string artist) => title.Trim().Length > 0 && artist.Trim().Length > 0;

    public static string PlayingNow(string title, string artist, string album, double duration, string player) =>
        Body("playing_now", Metadata(title, artist, album, duration, player), null);

    public static string Single(Listen listen) =>
        Body("single", Metadata(listen.Title, listen.Artist, listen.Album, listen.Duration, listen.Source), listen.Start.ToUnixTimeSeconds());

    private static string Body(string type, JsonObject metadata, long? listenedAt)
    {
        var listen = new JsonObject();
        if (listenedAt is { } at) listen["listened_at"] = at;
        listen["track_metadata"] = metadata;
        return new JsonObject { ["listen_type"] = type, ["payload"] = new JsonArray(listen) }.ToJsonString();
    }

    private static JsonObject Metadata(string title, string artist, string album, double duration, string player)
    {
        var info = new JsonObject();
        if (duration > 0) info["duration_ms"] = (long)Math.Round(duration * 1000);
        if (player.Trim().Length > 0) info["media_player"] = player.Trim();
        info["submission_client"] = "Shima";
        info["submission_client_version"] = Version;

        var metadata = new JsonObject { ["artist_name"] = artist.Trim(), ["track_name"] = title.Trim() };
        if (album.Trim().Length > 0) metadata["release_name"] = album.Trim();
        metadata["additional_info"] = info;
        return metadata;
    }
}

/// <summary>回報目的地：ListenBrainz、Last.fm 各一個，兩邊可以同時開。</summary>
public interface IListenSink
{
    /// <summary>剛開始播一首。</summary>
    Task NowPlaying(Track track);
    /// <summary>聽完一首；回傳有沒有送成功。</summary>
    Task<bool> Submit(Listen listen);
    /// <summary>聽完一首，並說明沒送成功是暫時的還是被拒絕了（待送清單要靠這個決定留不留）。</summary>
    Task<SubmitResult> Deliver(Listen listen);
    /// <summary>有沒有設定好、可以送。</summary>
    bool Enabled { get; }
    /// <summary>這一筆送不送得出去（缺資料、沒聽到門檻就不用排進待送清單）。</summary>
    bool Wants(Listen listen);
}

/// <summary>
/// 回報到 ListenBrainz。token 由呼叫端給（使用者在設定填的 user token），空的就什麼都不送。
/// 網路錯誤只寫進紀錄，不丟例外；token 不會出現在紀錄裡。
/// 官方要求每個程式一秒最多一次請求，所以請求一個一個排隊、至少間隔一秒；
/// 「正在播放」快速切歌時只送最後一首。
/// </summary>
public sealed class Scrobbler : IListenSink, IDisposable
{
    public const string Endpoint = "https://api.listenbrainz.org/1/submit-listens";

    private readonly HttpClient _http;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly TimeSpan _spacing;
    private DateTime _lastSent = DateTime.MinValue;
    private int _nowPlayingTicket;

    public string Token { get; set; }

    /// <param name="handler">測試用：換掉真的網路。</param>
    /// <param name="spacing">兩個請求之間至少隔多久（預設一秒）。</param>
    public Scrobbler(string token, HttpMessageHandler? handler = null, Action<string>? log = null, TimeSpan? spacing = null)
    {
        Token = token ?? "";
        _log = log ?? Log.Write;
        _spacing = spacing ?? TimeSpan.FromSeconds(1);
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.MaxResponseContentBufferSize = 1024 * 1024;   // 回應只是一小段 JSON／XML
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Shima/{ListenBrainzJson.Version} ( https://github.com/Carinoasd/shima )");
    }

    public bool Enabled => Token.Trim().Length > 0;

    /// <summary>剛開始播一首。</summary>
    public async Task NowPlaying(Track track)
    {
        if (!Enabled || !ListenBrainzJson.CanSubmit(track.Title, track.Artist)) return;
        int ticket = Interlocked.Increment(ref _nowPlayingTicket);
        var body = ListenBrainzJson.PlayingNow(track.Title, track.Artist, track.Album, track.Duration.TotalSeconds, track.Source);
        await Send(body, "playing_now", () => ticket == Volatile.Read(ref _nowPlayingTicket)).ConfigureAwait(false);
    }

    /// <summary>聽完一首（ListeningLog.Listened 給的紀錄）；沒聽到官方的門檻就不送。回傳有沒有送成功。</summary>
    public async Task<bool> Submit(Listen listen) => await Deliver(listen).ConfigureAwait(false) == SubmitResult.Sent;

    public bool Wants(Listen listen) =>
        Enabled && ListenBrainzJson.CanSubmit(listen.Title, listen.Artist) && ListenBrainzJson.ShouldSubmit(listen.Listened, listen.Duration);

    public async Task<SubmitResult> Deliver(Listen listen)
    {
        if (!Wants(listen)) return SubmitResult.Rejected;
        return await Send(ListenBrainzJson.Single(listen), "single", () => true).ConfigureAwait(false);
    }

    /// <summary>
    /// 暫時的：網路不通、逾時、5xx、429（太頻繁）；401 也留著，使用者可能只是 token 填錯、改對了還能補送。
    /// 其他 4xx 是這筆內容本身被拒絕，再送也一樣。
    /// </summary>
    public static SubmitResult Classify(int status) =>
        status is >= 200 and < 300 ? SubmitResult.Sent
        : status is 401 or 408 or 429 or >= 500 ? SubmitResult.Retry
        : SubmitResult.Rejected;

    private async Task<SubmitResult> Send(string body, string what, Func<bool> stillWanted)
    {
        await _queue.WaitAsync().ConfigureAwait(false);
        try
        {
            var wait = _lastSent + _spacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait).ConfigureAwait(false);
            if (!stillWanted()) return SubmitResult.Rejected;   // 排隊的時候又換歌了（只有「正在播放」會走到這裡）

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", Token.Trim());
            _lastSent = DateTime.UtcNow;
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return SubmitResult.Sent;
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (text.Length > 300) text = text[..300];
            _log($"listenbrainz {what}: {(int)response.StatusCode} {text}");
            return Classify((int)response.StatusCode);
        }
        catch (Exception e)
        {
            _log($"listenbrainz {what}: {e.GetType().Name} {e.Message}");
            return SubmitResult.Retry;
        }
        finally { _queue.Release(); }
    }

    public void Dispose()
    {
        _http.Dispose();
        _queue.Dispose();
    }
}

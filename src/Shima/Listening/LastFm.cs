// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Shima.Playback;

namespace Shima.Listening;

/// <summary>Last.fm 的簽章與回報規則（https://www.last.fm/api/authspec、https://www.last.fm/api/scrobbling）。</summary>
public static class LastFmApi
{
    public const string Endpoint = "https://ws.audioscrobbler.com/2.0/";
    public const string AuthPage = "https://www.last.fm/api/auth/";

    /// <summary>
    /// api_sig：參數依名稱排序（ASCII 順序，所以 artist[10] 在 artist[1] 前面），
    /// 每個接成「名稱＋值」，最後接上 shared secret，取 UTF-8 的 MD5（小寫十六進位）。
    /// format、callback 不參與簽章；api_sig 自己當然也不算。
    /// </summary>
    public static string Sign(IEnumerable<KeyValuePair<string, string>> parameters, string secret)
    {
        var text = new StringBuilder();
        foreach (var (name, value) in parameters.Where(p => p.Key is not ("api_sig" or "format" or "callback")).OrderBy(p => p.Key, StringComparer.Ordinal))
            text.Append(name).Append(value);
        text.Append(secret);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>使用者授權的網頁。</summary>
    public static string AuthUrl(string apiKey, string token) =>
        $"{AuthPage}?api_key={Uri.EscapeDataString(apiKey)}&token={Uri.EscapeDataString(token)}";

    /// <summary>官方：要比 30 秒長，而且聽了一半或 4 分鐘（取短的）。不知道長度（0）就只看聽的時間。</summary>
    public static bool ShouldSubmit(double listened, double duration) =>
        (duration <= 0 || duration > 30) && ListenBrainzJson.ShouldSubmit(listened, duration);
}

/// <summary>Last.fm 一次請求的結果。Code 0 是成功；-1 是網路或伺服器出問題（沒有可讀的回應）。</summary>
public sealed record LastFmReply(int Code, string Message, XElement? Body)
{
    public bool Ok => Code == 0;
    /// <summary>官方：只有 11（服務離線）與 16（暫時錯誤）該重試；其他代碼是請求本身有問題。網路不通也算暫時的。</summary>
    public bool Retryable => Code is -1 or 11 or 16;
}

public sealed record LastFmLogin(string Name, string Key);

/// <summary>
/// 回報到 Last.fm。api key、shared secret、session key 由呼叫端給（設定頁讓使用者自己填、自己授權）；
/// 缺任何一個就什麼都不送。網路錯誤只寫進紀錄、不丟例外；secret 與 session key 不會出現在紀錄裡。
/// 請求一個一個排隊、至少間隔一段時間；「正在播放」快速切歌時只送最後一首，失敗也不重試（官方要求）。
/// 聽完的紀錄遇到 11、16 或網路不通會等一下再試，最多三次；session key 被拒（9）就清掉並通知外面。
/// </summary>
public sealed class LastFm : IListenSink, IDisposable
{
    private const int Attempts = 3;

    private readonly HttpClient _http;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _spacing;
    private readonly TimeSpan _retryDelay;
    private DateTime _lastSent = DateTime.MinValue;
    private string _lastLogged = "";
    private DateTime _lastLoggedAt = DateTime.MinValue;
    private int _nowPlayingTicket;

    /// <summary>
    /// 三個值綁在一起、整組換：背景的請求（排隊中的、等著重試的）每次送出前拿一份，
    /// 不會簽章用新的 secret、sk 卻還是舊的。
    /// </summary>
    private sealed record Keys(string ApiKey, string Secret, string Session)
    {
        public bool HasApp => ApiKey.Length > 0 && Secret.Length > 0;
    }

    private readonly object _keyGate = new();
    private Keys _keys = new("", "", "");
    private Keys Now => Volatile.Read(ref _keys);

    public string ApiKey { get => Now.ApiKey; set { lock (_keyGate) _keys = _keys with { ApiKey = value.Trim() }; } }
    public string Secret { get => Now.Secret; set { lock (_keyGate) _keys = _keys with { Secret = value.Trim() }; } }
    public string Session { get => Now.Session; set { lock (_keyGate) _keys = _keys with { Session = value.Trim() }; } }

    /// <summary>三個一起換（設定改了、連接或中斷）。</summary>
    public void Use(string apiKey, string secret, string session)
    {
        lock (_keyGate) _keys = new(apiKey.Trim(), secret.Trim(), session.Trim());
    }

    /// <summary>session key 被 Last.fm 拒絕（使用者在 Last.fm 撤銷了授權）。參數是被拒的那一把，要請使用者重新連接。</summary>
    public event Action<string>? SessionRejected;

    /// <param name="handler">測試用：換掉真的網路。</param>
    /// <param name="spacing">兩個請求之間至少隔多久（預設一秒）。</param>
    /// <param name="retryDelay">重試前等多久（預設 30 秒，第二次加倍）。</param>
    public LastFm(HttpMessageHandler? handler = null, Action<string>? log = null, TimeSpan? spacing = null, TimeSpan? retryDelay = null)
    {
        _log = log ?? Log.Write;
        _spacing = spacing ?? TimeSpan.FromSeconds(1);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(30);
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.MaxResponseContentBufferSize = 1024 * 1024;   // 回應只是一小段 JSON／XML
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Shima/{ListenBrainzJson.Version} ( https://github.com/Carinoasd/shima )");
    }

    public bool Enabled => Now is { HasApp: true, Session.Length: > 0 };

    // ---------------------------------------------------------------- 回報

    /// <summary>剛開始播一首。</summary>
    public async Task NowPlaying(Track track)
    {
        if (!Enabled || !ListenBrainzJson.CanSubmit(track.Title, track.Artist)) return;
        int ticket = Interlocked.Increment(ref _nowPlayingTicket);
        var p = TrackParameters("track.updateNowPlaying", track.Title, track.Artist, track.Album, track.Duration.TotalSeconds);
        await Call(p, true, () => ticket == Volatile.Read(ref _nowPlayingTicket)).ConfigureAwait(false);
    }

    /// <summary>聽完一首；沒聽到門檻就不送。回傳有沒有送成功（被 Last.fm 忽略也算沒成功）。</summary>
    public async Task<bool> Submit(Listen listen) => await Deliver(listen).ConfigureAwait(false) == SubmitResult.Sent;

    public bool Wants(Listen listen) =>
        Enabled && ListenBrainzJson.CanSubmit(listen.Title, listen.Artist) && LastFmApi.ShouldSubmit(listen.Listened, listen.Duration);

    /// <summary>
    /// 暫時的（留著以後再送）：網路不通、11、16，以及跟這一筆無關、使用者改好就能送的 9（session 失效）、10、26（api key 有問題）。
    /// 其他錯誤代碼與被忽略的是這一筆本身有問題，再送也一樣。
    /// </summary>
    public async Task<SubmitResult> Deliver(Listen listen)
    {
        if (!Wants(listen)) return SubmitResult.Rejected;
        var p = TrackParameters("track.scrobble", listen.Title, listen.Artist, listen.Album, listen.Duration);
        p.Add(new("timestamp", listen.Start.ToUnixTimeSeconds().ToString()));
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                var reply = await Call(p, true, () => true).ConfigureAwait(false);
                if (reply.Ok)
                {
                    // 200 不代表收下了：被過濾的歌 accepted 是 0，重送也沒用
                    if (reply.Body?.Descendants("scrobbles").FirstOrDefault()?.Attribute("accepted")?.Value == "0")
                    {
                        Report($"track.scrobble: ignored {reply.Body.Descendants("ignoredMessage").FirstOrDefault()?.Attribute("code")?.Value}");
                        return SubmitResult.Rejected;
                    }
                    return SubmitResult.Sent;
                }
                bool keep = reply.Retryable || reply.Code is 9 or 10 or 26 or -2;
                if (!reply.Retryable || attempt >= Attempts) return keep ? SubmitResult.Retry : SubmitResult.Rejected;
                await Task.Delay(_retryDelay * attempt, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { return SubmitResult.Retry; }
    }

    private List<KeyValuePair<string, string>> TrackParameters(string method, string title, string artist, string album, double duration)
    {
        var p = new List<KeyValuePair<string, string>> { new("method", method), new("artist", artist.Trim()), new("track", title.Trim()) };
        if (album.Trim().Length > 0) p.Add(new("album", album.Trim()));
        if (duration > 0) p.Add(new("duration", ((long)Math.Round(duration)).ToString()));
        return p;
    }

    // ---------------------------------------------------------------- 連接（桌面程式的驗證流程）

    /// <summary>
    /// auth.getToken → 開瀏覽器請使用者授權 → 輪詢 auth.getSession（14 是還沒授權，等一下再問）。
    /// 成功回傳使用者名稱與 session key；失敗、逾時或取消回傳 null。session 不會自動設進來，由呼叫端存。
    /// </summary>
    public async Task<LastFmLogin?> Connect(Action<string> openBrowser, CancellationToken cancel, TimeSpan? poll = null, TimeSpan? timeout = null)
    {
        var keys = Now;
        if (!keys.HasApp) return null;
        var interval = poll ?? TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
        try
        {
            var got = await Call([new("method", "auth.getToken")], false, () => true, cancel).ConfigureAwait(false);
            var token = got.Body?.Element("token")?.Value ?? "";
            if (!got.Ok || token.Length == 0) return null;
            openBrowser(LastFmApi.AuthUrl(keys.ApiKey, token));
            do
            {
                // 瀏覽器才剛開，使用者還沒按授權：先等一下再問，省掉一次必定是「還沒授權」的請求
                await Task.Delay(interval, cancel).ConfigureAwait(false);
                var reply = await Call([new("method", "auth.getSession"), new("token", token)], false, () => true, cancel).ConfigureAwait(false);
                if (reply.Ok && reply.Body?.Element("session") is { } s && s.Element("key")?.Value is { Length: > 0 } key)
                    return new LastFmLogin(s.Element("name")?.Value ?? "", key);
                if (reply.Code != 14 && !reply.Retryable) return null;   // 14 是還沒授權，暫時連不上也再問
            } while (DateTime.UtcNow < deadline);
        }
        catch (OperationCanceledException) { }
        return null;
    }

    // ---------------------------------------------------------------- 送請求

    /// <summary>加上 api_key（與 sk）、簽章，排隊送出，解析回應。</summary>
    private async Task<LastFmReply> Call(List<KeyValuePair<string, string>> parameters, bool withSession, Func<bool> stillWanted, CancellationToken cancel = default)
    {
        string method = parameters.First(p => p.Key == "method").Value;
        try
        {
            using var link = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancel);
            await _queue.WaitAsync(link.Token).ConfigureAwait(false);
            try
            {
                var wait = _lastSent + _spacing - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, link.Token).ConfigureAwait(false);
                if (!stillWanted()) return new LastFmReply(-2, "superseded", null);   // 排隊的時候又換歌了

                var keys = Now;
                // 排隊或等重試的時候中斷了：不要拿空的 session 去送
                if (withSession && keys.Session.Length == 0) return new LastFmReply(-2, "disconnected", null);
                var form = new List<KeyValuePair<string, string>>(parameters) { new("api_key", keys.ApiKey) };
                if (withSession) form.Add(new("sk", keys.Session));
                form.Add(new("api_sig", LastFmApi.Sign(form, keys.Secret)));

                // 寫入的方法一定要 POST；驗證那兩個文件的範例是 GET，但 POST 一樣收（pylast 等函式庫全部用 POST），
                // 所以統一放在內文（FormUrlEncodedContent 以 UTF-8 編碼），api_sig 也不會留在網址裡
                using var request = new HttpRequestMessage(HttpMethod.Post, LastFmApi.Endpoint) { Content = new FormUrlEncodedContent(form) };
                _lastSent = DateTime.UtcNow;
                using var response = await _http.SendAsync(request, link.Token).ConfigureAwait(false);
                var reply = Parse(await response.Content.ReadAsStringAsync(link.Token).ConfigureAwait(false), (int)response.StatusCode);
                if (!reply.Ok) Report($"{method}: {reply.Code} {reply.Message}");
                if (reply.Code == 9 && withSession)
                {
                    // 只清被拒的那一把：這個請求送出去之後，使用者可能已經重新連接了
                    lock (_keyGate) { if (_keys.Session == keys.Session) _keys = _keys with { Session = "" }; }
                    SessionRejected?.Invoke(keys.Session);
                }
                return reply;
            }
            finally { _queue.Release(); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested || cancel.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            Report($"{method}: {e.GetType().Name} {e.Message}");
            return new LastFmReply(-1, e.Message, null);
        }
    }

    /// <summary>
    /// 看 lfm 的 status，不看 HTTP 狀態碼（官方：200 不一定成功，失敗也可能不是 200）。
    /// 讀不出 lfm 的（例如 5xx 的網頁）當成暫時的錯誤。
    /// </summary>
    public static LastFmReply Parse(string text, int httpStatus)
    {
        try
        {
            var root = XDocument.Parse(text).Root;
            if (root?.Name.LocalName == "lfm")
            {
                if (root.Attribute("status")?.Value == "ok") return new LastFmReply(0, "", root);
                var error = root.Element("error");
                if (int.TryParse(error?.Attribute("code")?.Value, out int code)) return new LastFmReply(code, error!.Value, root);
            }
        }
        catch (System.Xml.XmlException) { }
        return new LastFmReply(-1, $"HTTP {httpStatus}", null);
    }

    /// <summary>同一句錯誤十分鐘內只寫一次：離線的時候每首歌都會失敗，不要洗版。</summary>
    private void Report(string message)
    {
        var now = DateTime.UtcNow;
        if (message == _lastLogged && now - _lastLoggedAt < TimeSpan.FromMinutes(10)) return;
        _lastLogged = message;
        _lastLoggedAt = now;
        _log("lastfm " + message);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _http.Dispose();   // _stop 不 Dispose：還在等的重試會用到它的 token
    }
}

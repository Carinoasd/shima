// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http;
using System.Web;
using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class LastFmTests
{
    private static KeyValuePair<string, string> P(string k, string v) => new(k, v);

    [Fact]
    public void 簽章_官方文件的範例排列()
    {
        // 文件的範例：api_keyxxxxxxxxxxmethodauth.getSessiontokenyyyyyy + secret，取 MD5
        var sig = LastFmApi.Sign([P("token", "yyyyyy"), P("method", "auth.getSession"), P("api_key", "xxxxxxxxxx")], "zzzz");
        Assert.Equal("3c5d3ffae946b8c94fdf1fdd9066abd5", sig);
    }

    [Fact]
    public void 簽章_format_callback_api_sig_不參與()
    {
        var plain = LastFmApi.Sign([P("a", "1")], "s");
        Assert.Equal(plain, LastFmApi.Sign([P("format", "json"), P("a", "1"), P("callback", "x"), P("api_sig", "old")], "s"));
    }

    [Fact]
    public void 簽章_依_ASCII_排序_陣列的_10_在_1_前面()
    {
        var sig = LastFmApi.Sign([P("artist[1]", "c"), P("method", "track.scrobble"), P("artist[10]", "b"), P("album[0]", "x"), P("artist[0]", "a")], "secret");
        Assert.Equal("a2facecc99b91f043957a63589b35065", sig);   // album[0]x artist[0]a artist[10]b artist[1]c …
    }

    [Fact]
    public void 簽章_UTF8_中日文與撇號()
    {
        Assert.Equal("fdd283da18844264815b7c5003c9fce0",
            LastFmApi.Sign([P("api_key", "abc"), P("artist", "μ's"), P("method", "track.scrobble"), P("sk", "sk1"), P("timestamp", "1790000000"), P("track", "Snow halation")], "secret"));
        Assert.Equal("e188cb57842005e94c8fb1c8d61276a9",
            LastFmApi.Sign([P("track", "夏色"), P("sk", "S"), P("method", "track.updateNowPlaying"), P("artist", "葉月ゆら"), P("api_key", "K")], "SEC"));
    }

    [Theory]
    [InlineData(30, 30, false)]     // 要比 30 秒長
    [InlineData(10, 31, false)]     // 比 30 秒長的歌，聽了不到一半
    [InlineData(16, 31, true)]
    [InlineData(120, 240, true)]
    [InlineData(119, 240, false)]
    [InlineData(240, 900, true)]
    [InlineData(100, 0, false)]     // 不知道長度：要四分鐘
    [InlineData(240, 0, true)]
    public void 門檻(double listened, double duration, bool submit) => Assert.Equal(submit, LastFmApi.ShouldSubmit(listened, duration));

    // ---------------------------------------------------------------- 假的 Last.fm

    private const string Ok = """<lfm status="ok"><scrobbles accepted="1" ignored="0"><scrobble><track corrected="0">x</track></scrobble></scrobbles></lfm>""";
    private static string Fail(int code) => $"""<lfm status="failed"><error code="{code}">msg</error></lfm>""";

    private sealed class Fake(params (HttpStatusCode Status, string Body)[] replies) : HttpMessageHandler
    {
        private int _next;
        public List<(Uri Url, HttpMethod Method, string ContentType, string Body)> Sent { get; } = [];
        public Dictionary<string, string> Form(int i) =>
            HttpUtility.ParseQueryString(Sent[i].Body).AllKeys.ToDictionary(k => k!, k => HttpUtility.ParseQueryString(Sent[i].Body)[k]!);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request.RequestUri!, request.Method, request.Content!.Headers.ContentType!.MediaType!, await request.Content.ReadAsStringAsync(cancellationToken)));
            var (status, body) = replies[Math.Min(_next++, replies.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static Fake Always(string body, HttpStatusCode status = HttpStatusCode.OK) => new((status, body));

    private static LastFm Make(Fake fake, List<string>? log = null, string session = "SK") =>
        new(fake, m => log?.Add(m), TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "KEY", Secret = "SECRET", Session = session };

    private static Listen Done(string title = "Snow halation", string artist = "μ's", string album = "Snow halation", double duration = 259.5, double listened = 200) =>
        new(new DateTimeOffset(2026, 11, 23, 21, 0, 0, TimeSpan.FromHours(8)), title, artist, album, "Roon", duration, listened);

    private static Track Playing(string title = "Snow halation", string artist = "μ's", string album = "Snow halation") =>
        new("Roon", "Roon", title, artist, album, null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(259.5));

    // ---------------------------------------------------------------- 請求內容

    [Fact]
    public async Task 聽完一首_scrobble_的內容與簽章()
    {
        var fake = Always(Ok);
        using var lastfm = Make(fake);
        Assert.True(await lastfm.Submit(Done()));

        var sent = Assert.Single(fake.Sent);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://ws.audioscrobbler.com/2.0/", sent.Url.ToString());
        Assert.Equal("application/x-www-form-urlencoded", sent.ContentType);
        var form = fake.Form(0);
        Assert.Equal("track.scrobble", form["method"]);
        Assert.Equal("KEY", form["api_key"]);
        Assert.Equal("SK", form["sk"]);
        Assert.Equal("μ's", form["artist"]);
        Assert.Equal("Snow halation", form["track"]);
        Assert.Equal("Snow halation", form["album"]);
        Assert.Equal("260", form["duration"]);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 13, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds().ToString(), form["timestamp"]);
        Assert.False(form.ContainsKey("format"));
        Assert.Equal(LastFmApi.Sign(form.Where(p => p.Key != "api_sig").Select(p => P(p.Key, p.Value)), "SECRET"), form["api_sig"]);
        Assert.DoesNotContain("SECRET", sent.Body);
    }

    [Fact]
    public async Task 中日文歌名_以_UTF8_百分比編碼送出()
    {
        var fake = Always(Ok);
        using var lastfm = Make(fake);
        await lastfm.Submit(Done("夏色ハイライト", "葉月ゆら", ""));
        Assert.Contains("track=%E5%A4%8F%E8%89%B2%E3%83%8F%E3%82%A4%E3%83%A9%E3%82%A4%E3%83%88", fake.Sent[0].Body);
        var form = fake.Form(0);
        Assert.Equal("夏色ハイライト", form["track"]);
        Assert.Equal("葉月ゆら", form["artist"]);
        Assert.False(form.ContainsKey("album"));   // 沒有資料的欄位不送
    }

    [Fact]
    public async Task 正在播放_沒有_timestamp()
    {
        var fake = Always("""<lfm status="ok"><nowplaying><track corrected="0">x</track></nowplaying></lfm>""");
        using var lastfm = Make(fake);
        await lastfm.NowPlaying(Playing());
        var form = fake.Form(0);
        Assert.Equal("track.updateNowPlaying", form["method"]);
        Assert.False(form.ContainsKey("timestamp"));
        Assert.Equal("SK", form["sk"]);
        Assert.Equal("μ's", form["artist"]);
        Assert.Equal("260", form["duration"]);
    }

    [Fact]
    public async Task 沒聽到門檻_或缺歌名藝人_不送()
    {
        var fake = Always(Ok);
        using var lastfm = Make(fake);
        Assert.False(await lastfm.Submit(Done(listened: 100)));
        Assert.False(await lastfm.Submit(Done(duration: 25, listened: 25)));
        Assert.False(await lastfm.Submit(Done(artist: " ")));
        await lastfm.NowPlaying(Playing(title: ""));
        Assert.Empty(fake.Sent);
    }

    [Theory]
    [InlineData("", "SECRET", "SK")]
    [InlineData("KEY", " ", "SK")]
    [InlineData("KEY", "SECRET", "")]   // 沒連接
    public async Task 沒連接時完全不送(string key, string secret, string session)
    {
        var fake = Always(Ok);
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = key, Secret = secret, Session = session };
        Assert.False(lastfm.Enabled);
        Assert.False(await lastfm.Submit(Done()));
        await lastfm.NowPlaying(Playing());
        Assert.Empty(fake.Sent);
    }

    [Fact]
    public async Task 沒填_API_key_或_secret_不能開始連接()
    {
        var fake = Always(Ok);
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "K", Secret = "" };
        bool opened = false;
        Assert.Null(await lastfm.Connect(_ => opened = true, CancellationToken.None, TimeSpan.Zero));
        Assert.False(opened);
        Assert.Empty(fake.Sent);
    }

    [Fact]
    public async Task 兩首歌不會同時送_快速換歌正在播放只送最後一首()
    {
        var fake = Always("""<lfm status="ok"/>""");
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.FromMilliseconds(300), TimeSpan.Zero) { ApiKey = "K", Secret = "S", Session = "X" };
        var first = lastfm.NowPlaying(Playing("A"));
        var second = lastfm.NowPlaying(Playing("B"));
        var third = lastfm.NowPlaying(Playing("C"));
        await Task.WhenAll(first, second, third);
        Assert.Equal(["A", "C"], fake.Sent.Select((_, i) => fake.Form(i)["track"]).ToArray());
    }

    // ---------------------------------------------------------------- 驗證流程

    [Fact]
    public async Task 驗證三步_取得_token_開瀏覽器_換_session()
    {
        var fake = new Fake(
            (HttpStatusCode.OK, "<lfm status=\"ok\"><token>TOK123</token></lfm>"),
            (HttpStatusCode.Forbidden, Fail(14)),
            (HttpStatusCode.OK, "<lfm status=\"ok\"><session><name>carino</name><key>SESSIONKEY</key><subscriber>0</subscriber></session></lfm>"));
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "KEY", Secret = "SECRET" };
        string? opened = null;
        var login = await lastfm.Connect(url => opened = url, CancellationToken.None, TimeSpan.Zero);

        Assert.Equal(new LastFmLogin("carino", "SESSIONKEY"), login);
        Assert.Equal("https://www.last.fm/api/auth/?api_key=KEY&token=TOK123", opened);
        Assert.Equal(3, fake.Sent.Count);

        var getToken = fake.Form(0);
        Assert.Equal("auth.getToken", getToken["method"]);
        Assert.False(getToken.ContainsKey("sk"));
        Assert.Equal(LastFmApi.Sign([P("method", "auth.getToken"), P("api_key", "KEY")], "SECRET"), getToken["api_sig"]);

        var getSession = fake.Form(2);
        Assert.Equal("auth.getSession", getSession["method"]);
        Assert.Equal("TOK123", getSession["token"]);
        Assert.False(getSession.ContainsKey("sk"));
        Assert.Equal(LastFmApi.Sign([P("method", "auth.getSession"), P("api_key", "KEY"), P("token", "TOK123")], "SECRET"), getSession["api_sig"]);
    }

    [Theory]
    [InlineData(15)]    // token 過期
    [InlineData(4)]
    [InlineData(10)]    // API key 不對
    [InlineData(13)]    // 簽章不對（shared secret 填錯）
    public async Task 驗證失敗_回傳_null_不再問(int code)
    {
        var fake = new Fake((HttpStatusCode.OK, "<lfm status=\"ok\"><token>T</token></lfm>"), (HttpStatusCode.Forbidden, Fail(code)));
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "K", Secret = "S" };
        Assert.Null(await lastfm.Connect(_ => { }, CancellationToken.None, TimeSpan.Zero));
        Assert.Equal(2, fake.Sent.Count);
    }

    [Fact]
    public async Task 取不到_token_不開瀏覽器()
    {
        var fake = Always(Fail(10), HttpStatusCode.Forbidden);
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "K", Secret = "S" };
        bool opened = false;
        Assert.Null(await lastfm.Connect(_ => opened = true, CancellationToken.None, TimeSpan.Zero));
        Assert.False(opened);
    }

    [Fact]
    public async Task 一直沒授權_逾時回傳_null_取消也是()
    {
        var fake = new Fake((HttpStatusCode.OK, "<lfm status=\"ok\"><token>T</token></lfm>"), (HttpStatusCode.Forbidden, Fail(14)));
        using var lastfm = new LastFm(fake, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "K", Secret = "S" };
        Assert.Null(await lastfm.Connect(_ => { }, CancellationToken.None, TimeSpan.Zero, TimeSpan.Zero));

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Null(await lastfm.Connect(_ => { }, cancel.Token, TimeSpan.Zero));
    }

    // ---------------------------------------------------------------- 錯誤處理

    [Theory]
    [InlineData(11)]
    [InlineData(16)]
    public async Task 暫時的錯誤_重試最多三次(int code)
    {
        var fake = Always(Fail(code), HttpStatusCode.ServiceUnavailable);
        using var lastfm = Make(fake);
        Assert.False(await lastfm.Submit(Done()));
        Assert.Equal(3, fake.Sent.Count);
        Assert.Equal(fake.Form(0)["timestamp"], fake.Form(2)["timestamp"]);   // 重送的是同一首
    }

    [Fact]
    public async Task 重試後成功就停()
    {
        var fake = new Fake((HttpStatusCode.OK, Fail(16)), (HttpStatusCode.OK, Ok));
        using var lastfm = Make(fake);
        Assert.True(await lastfm.Submit(Done()));
        Assert.Equal(2, fake.Sent.Count);
    }

    [Fact]
    public async Task 網路不通或伺服器吐網頁_也算暫時的()
    {
        var fake = Always("<html>Bad Gateway</html>", HttpStatusCode.BadGateway);
        using var lastfm = Make(fake);
        Assert.False(await lastfm.Submit(Done()));
        Assert.Equal(3, fake.Sent.Count);
    }

    [Theory]
    [InlineData(6)]     // 缺參數
    [InlineData(13)]    // 簽章不對
    [InlineData(10)]
    [InlineData(26)]
    [InlineData(29)]
    public async Task 其他錯誤_不重試(int code)
    {
        var fake = Always(Fail(code), HttpStatusCode.Forbidden);
        using var lastfm = Make(fake);
        Assert.False(await lastfm.Submit(Done()));
        Assert.Single(fake.Sent);
    }

    [Fact]
    public async Task 正在播放失敗_不重試()
    {
        var fake = Always(Fail(16));
        using var lastfm = Make(fake);
        await lastfm.NowPlaying(Playing());
        Assert.Single(fake.Sent);
    }

    [Fact]
    public async Task HTTP_200_也要看_status_被忽略的不算成功()
    {
        var ignored = """<lfm status="ok"><scrobbles accepted="0" ignored="1"><scrobble><ignoredMessage code="1">filtered</ignoredMessage></scrobble></scrobbles></lfm>""";
        var fake = Always(ignored);
        using var lastfm = Make(fake);
        Assert.False(await lastfm.Submit(Done()));
        Assert.Single(fake.Sent);

        var failed = Always(Fail(6));   // 200 但 status="failed"
        using var other = Make(failed);
        Assert.False(await other.Submit(Done()));
    }

    [Fact]
    public async Task session_key_被拒_清掉並通知_不重試()
    {
        var fake = Always(Fail(9), HttpStatusCode.Forbidden);
        using var lastfm = Make(fake);
        int rejected = 0;
        lastfm.SessionRejected += () => rejected++;
        Assert.False(await lastfm.Submit(Done()));
        Assert.Single(fake.Sent);
        Assert.Equal(1, rejected);
        Assert.Equal("", lastfm.Session);
        Assert.False(lastfm.Enabled);
        Assert.False(await lastfm.Submit(Done()));   // 之後不再送
        Assert.Single(fake.Sent);
    }

    [Fact]
    public async Task 紀錄不含機密_同一句錯誤不洗版()
    {
        var log = new List<string>();
        var fake = Always(Fail(11), HttpStatusCode.ServiceUnavailable);
        using var lastfm = Make(fake, log);
        for (int i = 0; i < 4; i++) await lastfm.Submit(Done());
        Assert.Single(log);
        Assert.StartsWith("lastfm track.scrobble: 11", log[0]);
        Assert.DoesNotContain("SECRET", log[0]);
        Assert.DoesNotContain("SK", log[0]);
    }
}

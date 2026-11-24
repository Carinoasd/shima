// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class PendingScrobblesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shima-pending-" + Guid.NewGuid().ToString("N"));
    private string PendingPath => Path.Combine(_folder, "pending-scrobbles.jsonl");

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    private PendingScrobbles Store(Func<DateTimeOffset>? clock = null) => new(PendingPath, clock ?? (() => Now), _ => { });

    /// <summary>第 n 首：每首差一分鐘，n 越大越晚。</summary>
    private static Listen Song(int n, string source = "Roon") =>
        new(Now.AddHours(-1).AddMinutes(n), $"歌 {n}", "藝人", "專輯", source, 240, 200);

    /// <summary>照腳本回應的假網路；handler 的 Online 關掉就丟網路例外。</summary>
    private sealed class Net : HttpMessageHandler
    {
        public bool Online = true;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "";
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Online) throw new HttpRequestException("offline");
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private static List<long> ListenedAt(Net net) =>
        [.. net.Bodies.Select(b => (long)JsonNode.Parse(b)!["payload"]![0]!["listened_at"]!)];

    private static List<string> Tracks(Net net) =>
        [.. net.Bodies.Select(b => (string)JsonNode.Parse(b)!["payload"]![0]!["track_metadata"]!["track_name"]!)];

    private static QueuedSink Brainz(Net net, PendingScrobbles store, string token = "secret-token-xyz") =>
        new("listenbrainz", new Scrobbler(token, net, _ => { }, TimeSpan.Zero), store, _ => { });

    private const string FmOk = """<lfm status="ok"><scrobbles accepted="1" ignored="0"></scrobbles></lfm>""";

    private static (QueuedSink Sink, LastFm Inner) Fm(Net net, PendingScrobbles store)
    {
        var inner = new LastFm(net, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "KEY", Secret = "SECRET-xyz", Session = "SESSION-xyz" };
        return (new QueuedSink("lastfm", inner, store, _ => { }), inner);
    }

    // ---------------------------------------------------------------- ListenBrainz：離線、補送、重開

    [Fact]
    public async Task 離線時累積_恢復後依序補送()
    {
        var net = new Net { Online = false };
        var store = Store();
        var sink = Brainz(net, store);

        Assert.False(await sink.Submit(Song(2)));
        Assert.False(await sink.Submit(Song(1)));   // 晚到的、開始時間比較早：補送照時間順序
        Assert.Equal([Song(1).Start, Song(2).Start], store.Items("listenbrainz").Select(l => l.Start));

        net.Online = true;
        Assert.True(await sink.Submit(Song(3)));
        Assert.Equal(["歌 1", "歌 2", "歌 3"], Tracks(net));
        Assert.Equal(new[] { 1, 2, 3 }.Select(n => Song(n).Start.ToUnixTimeSeconds()), ListenedAt(net));   // 照原本的時間戳記
        Assert.Empty(store.Items("listenbrainz"));
    }

    [Fact]
    public async Task 補送遇到暫時的失敗就停_不一首一首都試()
    {
        var net = new Net { Online = false };
        var store = Store();
        var sink = Brainz(net, store);
        for (int i = 1; i <= 4; i++) await sink.Submit(Song(i));
        Assert.Equal(4, store.Items("listenbrainz").Count);
        net.Online = true;
        await sink.Drain();
        Assert.Equal(4, net.Bodies.Count);
        Assert.Empty(store.Items("listenbrainz"));

        var failing = new Net { Status = HttpStatusCode.ServiceUnavailable };
        var stuck = Brainz(failing, store);
        for (int i = 5; i <= 8; i++) await stuck.Submit(Song(i));
        Assert.Equal(4, store.Items("listenbrainz").Count);
        Assert.Equal(4, failing.Bodies.Count);   // 每次新的一筆只試最前面那一筆（共四次），不會把清單裡每一筆都送
    }

    [Fact]
    public async Task 重開之後讀同一個檔案補送()
    {
        var net = new Net { Online = false };
        var first = Store();
        await Brainz(net, first).Submit(Song(1));
        await Brainz(net, first).Submit(Song(2));

        // 重新建立所有物件
        var again = Store();
        Assert.Equal(2, again.Items("listenbrainz").Count);
        var online = new Net();
        await Brainz(online, again).Drain();
        Assert.Equal(["歌 1", "歌 2"], Tracks(online));
        Assert.Empty(Store().Items("listenbrainz"));   // 再重開一次也是空的
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task 被明確拒絕的不重送(HttpStatusCode status)
    {
        var net = new Net { Status = status, Body = """{"code":400,"error":"bad"}""" };
        var store = Store();
        var sink = Brainz(net, store);
        Assert.Equal(SubmitResult.Rejected, await sink.Deliver(Song(1)));
        Assert.Empty(store.Items("listenbrainz"));
        net.Status = HttpStatusCode.OK;
        await sink.Drain();
        Assert.Empty(net.Bodies.Skip(1));   // 只有最初那一次
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task 暫時的錯誤留著(HttpStatusCode status)
    {
        var store = Store();
        var sink = Brainz(new Net { Status = status }, store);
        Assert.Equal(SubmitResult.Retry, await sink.Deliver(Song(1)));
        Assert.Single(store.Items("listenbrainz"));
    }

    [Fact]
    public async Task 沒有達到門檻或沒設定的不進清單()
    {
        var net = new Net();
        var store = Store();
        await Brainz(net, store).Submit(Song(1) with { Listened = 10 });
        await Brainz(net, store, "").Submit(Song(2));
        Assert.Empty(store.Items("listenbrainz"));
        Assert.False(File.Exists(PendingPath));
        Assert.Empty(net.Bodies);
    }

    [Fact]
    public async Task 正在播放不進清單()
    {
        var net = new Net { Online = false };
        var store = Store();
        await Brainz(net, store).NowPlaying(new Shima.Playback.Track("Roon", "Roon", "歌", "人", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(200)));
        Assert.False(File.Exists(PendingPath));
    }

    // ---------------------------------------------------------------- 清單本身

    [Fact]
    public void 上限五百筆_超過丟最舊的()
    {
        var store = Store(() => Now);
        for (int i = 0; i < 505; i++) store.Add("lastfm", Song(i % 55) with { Title = $"歌 {i}", Start = Now.AddHours(-10).AddSeconds(i * 10) });
        var items = store.Items("lastfm");
        Assert.Equal(500, items.Count);
        Assert.Equal("歌 5", items[0].Title);
        Assert.Equal("歌 504", items[^1].Title);
        Assert.Equal(500, Store().Items("lastfm").Count);   // 檔案裡也是
        Assert.Equal(500, File.ReadAllLines(PendingPath).Length);
    }

    [Fact]
    public void 同一首同一個開始時間不重複()
    {
        var store = Store();
        Assert.True(store.Add("lastfm", Song(1)));
        Assert.False(store.Add("lastfm", Song(1) with { Listened = 220 }));
        Assert.True(store.Add("listenbrainz", Song(1)));   // 另一個目的地是另一份
        Assert.True(store.Add("lastfm", Song(1) with { Start = Song(1).Start.AddMinutes(30) }));   // 同一首、另一次
        Assert.Equal(2, store.Items("lastfm").Count);
    }

    [Fact]
    public void 超過十四天的丟掉()
    {
        var clock = Now;
        var store = Store(() => clock);
        var old = Song(1) with { Start = Now.AddDays(-13) };
        Assert.True(store.Add("lastfm", old));
        Assert.False(store.Add("lastfm", Song(2) with { Start = Now.AddDays(-15) }));   // 一開始就太舊
        clock = Now.AddDays(2);   // 過了兩天：原本那筆也超過十四天了
        Assert.Empty(store.Items("lastfm"));
        Assert.Empty(Store(() => clock).Items("lastfm"));
        Assert.Equal("", File.ReadAllText(PendingPath));
    }

    [Fact]
    public void 壞掉的行略過_暫存檔不留下()
    {
        Directory.CreateDirectory(_folder);
        var good = Store();
        good.Add("lastfm", Song(1));
        var line = File.ReadAllLines(PendingPath)[0];
        File.WriteAllText(PendingPath, "{壞掉\n\n" + line + "\n{\"to\":\"\",\"listen\":{}}\n[1,2]\n{\"to\":\"lastfm\"}\n" + line[..20]);
        Assert.Single(Store().Items("lastfm"));

        File.WriteAllText(PendingPath + ".tmp", "當掉時留下的半份");
        var store = Store();
        store.Add("lastfm", Song(2));
        Assert.False(File.Exists(PendingPath + ".tmp"));
        Assert.Equal(2, Store().Items("lastfm").Count);
    }

    [Fact]
    public async Task 檔案裡不含任何金鑰()
    {
        var store = Store();
        var net = new Net { Online = false };
        await Brainz(net, store, "secret-token-xyz").Submit(Song(1));
        var (fm, _) = Fm(net, store);
        await fm.Submit(Song(2));
        var text = File.ReadAllText(PendingPath);
        foreach (var secret in new[] { "secret-token-xyz", "KEY", "SECRET-xyz", "SESSION-xyz" }) Assert.DoesNotContain(secret, text);
        Assert.Contains("歌 1", text);   // 中文直接寫
    }

    // ---------------------------------------------------------------- 兩個目的地

    [Fact]
    public async Task 兩個目的地互不影響()
    {
        var store = Store();
        var brainzNet = new Net { Online = false };
        var fmNet = new Net { Body = FmOk };
        var brainz = Brainz(brainzNet, store);
        var (fm, _) = Fm(fmNet, store);

        await brainz.Submit(Song(1));
        await fm.Submit(Song(1));
        Assert.Single(store.Items("listenbrainz"));
        Assert.Empty(store.Items("lastfm"));
        Assert.Single(fmNet.Bodies);

        // Last.fm 中斷：只清它自己的
        store.Add("lastfm", Song(2));
        store.Clear("lastfm");
        Assert.Empty(store.Items("lastfm"));
        Assert.Single(store.Items("listenbrainz"));
        Assert.Single(Store().Items("listenbrainz"));
    }

    // ---------------------------------------------------------------- Last.fm

    [Fact]
    public async Task Lastfm_離線累積_恢復後依序補送()
    {
        var net = new Net { Online = false };
        var store = Store();
        var (fm, _) = Fm(net, store);
        Assert.False(await fm.Submit(Song(2)));
        Assert.False(await fm.Submit(Song(1)));
        Assert.Equal(2, store.Items("lastfm").Count);

        net.Online = true;
        net.Body = FmOk;
        Assert.True(await fm.Submit(Song(3)));
        var stamps = net.Bodies.Select(b => System.Web.HttpUtility.ParseQueryString(b)["timestamp"]).ToList();
        Assert.Equal(new[] { 1, 2, 3 }.Select(n => Song(n).Start.ToUnixTimeSeconds().ToString()), stamps);
        Assert.Empty(store.Items("lastfm"));
    }

    [Theory]
    [InlineData(16, true)]     // 暫時錯誤
    [InlineData(11, true)]     // 服務離線
    [InlineData(9, true)]      // session 失效：中斷連接時才清
    [InlineData(6, false)]     // 參數不對
    [InlineData(13, false)]    // 簽章錯
    public async Task Lastfm_錯誤代碼決定留不留(int code, bool kept)
    {
        var store = Store();
        var (fm, _) = Fm(new Net { Body = $"""<lfm status="failed"><error code="{code}">x</error></lfm>""" }, store);
        Assert.False(await fm.Submit(Song(1)));
        Assert.Equal(kept ? 1 : 0, store.Items("lastfm").Count);
    }

    [Fact]
    public async Task Lastfm_被忽略的不重送()
    {
        var store = Store();
        var net = new Net { Body = """<lfm status="ok"><scrobbles accepted="0" ignored="1"><scrobble><ignoredMessage code="1">x</ignoredMessage></scrobble></scrobbles></lfm>""" };
        var (fm, _) = Fm(net, store);
        Assert.Equal(SubmitResult.Rejected, await fm.Deliver(Song(1)));
        Assert.Empty(store.Items("lastfm"));
    }

    [Fact]
    public async Task Lastfm_中斷後不再送舊的()
    {
        var net = new Net { Online = false };
        var store = Store();
        var (fm, inner) = Fm(net, store);
        await fm.Submit(Song(1));
        store.Clear("lastfm");               // 視窗在中斷時做的事
        inner.Use("", "", "");
        net.Online = true;
        await fm.Drain();
        Assert.Empty(net.Bodies);
        Assert.Empty(store.Items("lastfm"));
    }
}

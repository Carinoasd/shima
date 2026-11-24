// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

/// <summary>每個請求照順序回一段內容（用完就一直回最後一段）。</summary>
internal sealed class Script(params string[] replies) : HttpMessageHandler
{
    public int Calls;
    public HttpStatusCode Status = HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = replies[Math.Min(Calls, replies.Length - 1)];
        Calls++;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body) });
    }
}

/// <summary>補送：跟這一筆無關的錯誤不能把待送清單刪光。</summary>
public class ScrobbleDrainTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shima-drain-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    private PendingScrobbles Store() => new(Path.Combine(_folder, "pending-scrobbles.jsonl"), () => Now, _ => { });

    private static Listen Song(int n, string source = "Roon") =>
        new(Now.AddHours(-3).AddMinutes(n), $"歌 {n}", "藝人", "專輯", source, 240, 200);

    private static string Failed(int code) => $"""<lfm status="failed"><error code="{code}">x</error></lfm>""";
    private const string Ok = """<lfm status="ok"><scrobbles accepted="1" ignored="0"></scrobbles></lfm>""";

    private (QueuedSink, PendingScrobbles) Fm(Script net, int pending)
    {
        var store = Store();
        for (int i = 1; i <= pending; i++) store.Add("lastfm", Song(i));
        var inner = new LastFm(net, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "KEY", Secret = "SECRET-xyz", Session = "SESSION-xyz" };
        return (new QueuedSink("lastfm", inner, store, _ => { }), store);
    }

    private (QueuedSink, PendingScrobbles) Brainz(Script net, int pending)
    {
        var store = Store();
        for (int i = 1; i <= pending; i++) store.Add("listenbrainz", Song(i));
        return (new QueuedSink("listenbrainz", new Scrobbler("token-xyz", net, _ => { }, TimeSpan.Zero), store, _ => { }), store);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(13)]    // 簽章不符：API key 與 secret 配不起來
    [InlineData(16)]
    [InlineData(26)]
    [InlineData(29)]    // 太頻繁
    [InlineData(99)]    // 沒看過的代碼
    public async Task Lastfm_跟這一筆無關的錯誤_一筆都不刪(int code)
    {
        var net = new Script(Failed(code));
        var (sink, store) = Fm(net, 40);
        await sink.Drain();
        Assert.Equal(40, store.Items("lastfm").Count);
        Assert.True(net.Calls <= 3 + 1, "遇到會留著的錯誤就停，不要一筆一筆試");
    }

    [Theory]
    [InlineData(0, SubmitResult.Sent)]
    [InlineData(6, SubmitResult.Rejected)]
    [InlineData(7, SubmitResult.Rejected)]
    [InlineData(13, SubmitResult.Retry)]
    [InlineData(29, SubmitResult.Retry)]
    [InlineData(-1, SubmitResult.Retry)]
    public void Lastfm_錯誤代碼分類(int code, SubmitResult expected) => Assert.Equal(expected, LastFmApi.Classify(code));

    [Fact]
    public async Task 這一筆內容有問題_拿掉_後面的照常送()
    {
        var net = new Script(Failed(6), Ok);   // 第一筆被說參數無效，之後都收下
        var (sink, store) = Fm(net, 5);
        await sink.Drain();
        Assert.Empty(store.Items("lastfm"));
        Assert.Equal(5, net.Calls);
    }

    [Fact]
    public async Task 連續幾筆被拒絕_停下來_也不刪()
    {
        var net = new Script(Failed(6));   // 每一筆都說參數無效：不是每首歌都有問題，是系統性的
        var (sink, store) = Fm(net, 30);
        await sink.Drain();
        Assert.Equal(30, store.Items("lastfm").Count);
        Assert.Equal(QueuedSink.RejectedStreak, net.Calls);
        await sink.Drain();   // 下一次也一樣，不會愈送愈少
        Assert.Equal(30, store.Items("lastfm").Count);
    }

    [Fact]
    public async Task 兩筆被拒絕後面送成功_兩筆都拿掉()
    {
        var net = new Script(Failed(6), Failed(7), Ok);
        var (sink, store) = Fm(net, 6);
        await sink.Drain();
        Assert.Empty(store.Items("lastfm"));
        Assert.Equal(6, net.Calls);
    }

    [Fact]
    public async Task 被拒絕之後遇到暫時的失敗_被拒的先留著()
    {
        var net = new Script(Failed(6), Failed(16));
        var (sink, store) = Fm(net, 4);
        await sink.Drain();
        Assert.Equal(4, store.Items("lastfm").Count);   // 還沒有證據說第一筆是它自己的問題
    }

    [Fact]
    public async Task 只有一兩筆而且都被拒絕_照原樣拿掉()
    {
        var net = new Script(Failed(6));
        var (sink, store) = Fm(net, 2);
        await sink.Drain();
        Assert.Empty(store.Items("lastfm"));
    }

    [Fact]
    public async Task 新的一筆被拒絕_回報給呼叫的人_清單裡舊的不受牽連()
    {
        var net = new Script(Failed(13));
        var (sink, store) = Fm(net, 10);
        Assert.Equal(SubmitResult.Retry, await sink.Deliver(Song(50)));
        Assert.Equal(11, store.Items("lastfm").Count);
    }

    [Fact]
    public async Task ListenBrainz_連續被拒絕也一樣停下來()
    {
        var net = new Script("""{"code":400,"error":"bad"}""") { Status = HttpStatusCode.BadRequest };
        var (sink, store) = Brainz(net, 25);
        await sink.Drain();
        Assert.Equal(25, store.Items("listenbrainz").Count);
        Assert.Equal(QueuedSink.RejectedStreak, net.Calls);

        net.Status = HttpStatusCode.OK;   // 之後恢復：全部補送出去
        await sink.Drain();
        Assert.Empty(store.Items("listenbrainz"));
    }
}

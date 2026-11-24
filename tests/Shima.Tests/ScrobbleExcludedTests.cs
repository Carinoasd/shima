// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

/// <summary>排除之前已經在待送清單裡的，補送時也不送。</summary>
public class ScrobbleExcludedTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shima-excl-" + Guid.NewGuid().ToString("N"));
    private const string Ok = """<lfm status="ok"><scrobbles accepted="1" ignored="0"></scrobbles></lfm>""";

    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    private PendingScrobbles Store() => new(Path.Combine(_folder, "pending-scrobbles.jsonl"), () => Now, _ => { });

    private static Listen Song(int n, string source = "Roon") =>
        new(Now.AddHours(-3).AddMinutes(n), $"歌 {n}", "藝人", "專輯", source, 240, 200);


    [Fact]
    public async Task 補送時已被排除的來源直接拿掉_不送()
    {
        var net = new Script(Ok);
        var store = Store();
        store.Add("lastfm", Song(1, "Chrome"));
        store.Add("lastfm", Song(2, "Roon"));
        store.Add("lastfm", Song(3, "chrome"));   // 大小寫不同也算
        var inner = new LastFm(net, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "KEY", Secret = "SECRET-xyz", Session = "SESSION-xyz" };
        var excluded = new[] { "Chrome" };
        var sink = new QueuedSink("lastfm", inner, store, _ => { }, source => ListenSources.Counts(source, excluded));
        await sink.Drain();
        Assert.Equal(1, net.Calls);   // 只有 Roon 那一首
        Assert.Empty(store.Items("lastfm"));
    }

    [Fact]
    public async Task 被排除的來源不算連續被拒絕()
    {
        var net = new Script(Ok);
        var store = Store();
        for (int i = 1; i <= 6; i++) store.Add("listenbrainz", Song(i, "Chrome"));
        store.Add("listenbrainz", Song(7, "Roon"));
        var sink = new QueuedSink("listenbrainz", new Scrobbler("token-xyz", net, _ => { }, TimeSpan.Zero), store, _ => { }, s => s != "Chrome");
        await sink.Drain();
        Assert.Equal(1, net.Calls);
        Assert.Empty(store.Items("listenbrainz"));
    }

    [Fact]
    public async Task 新的一筆來自被排除的來源_不送也不留()
    {
        var net = new Script(Ok);
        var store = Store();
        var sink = new QueuedSink("listenbrainz", new Scrobbler("token-xyz", net, _ => { }, TimeSpan.Zero), store, _ => { }, s => s != "Chrome");
        Assert.Equal(SubmitResult.Rejected, await sink.Deliver(Song(1, "Chrome")));
        Assert.Equal(0, net.Calls);
        Assert.Empty(store.Items("listenbrainz"));
    }
}

/// <summary>被排除、紀錄裡卻沒有的來源，設定頁仍然要有膠囊可以改回來。</summary>
public class ExcludedSourceChipTests
{
    private static Listen From(string source) => new(new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero), "歌", "人", "專輯", source, 240, 200);

    [Fact]
    public void 紀錄是空的但有排除_仍然列出來()
    {
        var seen = ListenSources.Seen([], ["Spotify"]);
        var spotify = Assert.Single(seen);
        Assert.Equal(("Spotify", 0, false), spotify);
    }

    [Fact]
    public void 紀錄裡沒有的排除來源接在後面_有的照常算()
    {
        var seen = ListenSources.Seen([From("Roon"), From("Roon"), From("Chrome")], ["chrome", " Spotify ", "", "SPOTIFY"]);
        Assert.Equal(["Chrome", "Roon", "Spotify"], seen.Select(x => x.Name).Order());
        Assert.Equal(1, seen.Single(x => x.Name == "Chrome").Count);
        Assert.False(seen.Single(x => x.Name == "Chrome").Counted);
        Assert.Equal((0, false), (seen.Single(x => x.Name == "Spotify").Count, seen.Single(x => x.Name == "Spotify").Counted));
        Assert.True(seen.Single(x => x.Name == "Roon").Counted);
    }

    [Fact]
    public void 點一下可以改回來()
    {
        var excluded = new List<string> { "Spotify" };
        Assert.Equal("Spotify", Assert.Single(ListenSources.Seen([], excluded)).Name);
        excluded = ListenSources.Toggle(excluded, "Spotify");
        Assert.Empty(excluded);
        Assert.Empty(ListenSources.Seen([], excluded));   // 改回來之後紀錄是空的，那一排也就沒了
    }

    [Fact]
    public void 排除很多個也全部列出()
    {
        var excluded = Enumerable.Range(0, 12).Select(i => "X" + i).ToList();
        Assert.Equal(12, ListenSources.Seen([From("Roon")], excluded).Count(x => !x.Counted));
    }
}

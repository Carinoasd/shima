// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>測試用的暫存資料夾：放在測試程式旁邊，用完就刪，不碰使用者的資料夾。</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "listening-tmp", Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
}

public class ListenTrackerTests
{
    private static readonly DateTime T0 = new(2026, 11, 23, 21, 0, 0);

    private static Track Song(string title, bool playing = true, double position = 0, DateTime? at = null, double duration = 240) =>
        new("Roon", "Roon", title, "μ's", "School idol paradise", null, playing, TimeSpan.FromSeconds(position),
            new DateTimeOffset(at ?? T0), TimeSpan.FromSeconds(duration));

    /// <summary>從 from 秒播到 to 秒，每 0.1 秒一個 tick；回傳途中交出的紀錄。</summary>
    private static List<Listen> Play(ListenTracker tracker, Track track, double from, double to)
    {
        var done = new List<Listen>();
        for (double t = from; t <= to + 1e-9; t += 0.1)
            if (tracker.Observe(track, T0.AddSeconds(t), out _) is { } l) done.Add(l);
        return done;
    }

    [Fact]
    public void 聽超過三十秒再換歌_記一筆()
    {
        var tracker = new ListenTracker();
        Assert.Empty(Play(tracker, Song("Snow halation"), 0, 40));
        var done = tracker.Observe(Song("START:DASH!!"), T0.AddSeconds(40.1), out var started);
        Assert.NotNull(done);
        Assert.Equal("Snow halation", done!.Title);
        Assert.Equal(new DateTimeOffset(T0), done.Start);
        Assert.InRange(done.Listened, 39.9, 40.3);
        Assert.Equal(240, done.Duration);
        Assert.Equal("START:DASH!!", started!.Title);
    }

    [Fact]
    public void 聽不到三十秒也沒過一半_不記()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song("Snow halation"), 0, 20);
        Assert.Null(tracker.Observe(Song("START:DASH!!"), T0.AddSeconds(20.1), out _));
    }

    [Fact]
    public void 暫停的時間不算()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song("Snow halation"), 0, 20);
        Play(tracker, Song("Snow halation", playing: false, position: 20), 20.1, 80);
        Play(tracker, Song("Snow halation", position: 20, at: T0.AddSeconds(80)), 80.1, 85);
        Assert.InRange(tracker.Listened, 24.5, 25.5);
        Assert.Null(tracker.Observe(null, T0.AddSeconds(85.1), out _));
    }

    [Fact]
    public void 拖進度不會讓秒數暴增()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song("Snow halation"), 0, 10);
        Play(tracker, Song("Snow halation", position: 200, at: T0.AddSeconds(10)), 10.1, 12);
        Assert.InRange(tracker.Listened, 11.5, 12.5);
        Assert.Null(tracker.Observe(Song("START:DASH!!"), T0.AddSeconds(12.1), out _));
    }

    [Fact]
    public void 兩個_tick_隔很久_只算一小段()
    {
        var tracker = new ListenTracker();
        tracker.Observe(Song("Snow halation"), T0, out _);
        tracker.Observe(Song("Snow halation"), T0.AddHours(1), out _);
        Assert.Equal(ListenTracker.MaxStep.TotalSeconds, tracker.Listened, 3);
    }

    [Fact]
    public void 短歌聽超過一半就算()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song("Short", duration: 40), 0, 21);
        Assert.NotNull(tracker.Observe(null, T0.AddSeconds(21.1), out _));
    }

    [Theory]
    [InlineData(30, 240, true)]
    [InlineData(29.9, 240, false)]
    [InlineData(21, 40, true)]
    [InlineData(20, 40, false)]
    [InlineData(10, 0, false)]   // 不知道長度：不能因為「超過 0 的一半」就算
    public void 門檻(double listened, double duration, bool counts) =>
        Assert.Equal(counts, ListenTracker.Counts(listened, duration));

    [Fact]
    public void 停止或程式結束時結算()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song("Snow halation"), 0, 35);
        Assert.NotNull(tracker.Flush());
        Assert.Null(tracker.Flush());   // 已經交出去了，不會重複
    }

    [Fact]
    public void 長度晚到_不會拆成兩首()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song("Snow halation", duration: 0), 0, 3);
        Assert.Empty(Play(tracker, Song("Snow halation", position: 3, at: T0.AddSeconds(3)), 3.1, 40));
        var done = tracker.Flush();
        Assert.NotNull(done);
        Assert.Equal(240, done!.Duration);
        Assert.InRange(done.Listened, 39.5, 40.5);
    }

    [Fact]
    public void 單曲循環_每一遍各算一次()
    {
        var tracker = new ListenTracker();
        Assert.Empty(Play(tracker, Song("Short", duration: 60), 0, 60));
        var done = Play(tracker, Song("Short", duration: 60, at: T0.AddSeconds(60.1)), 60.1, 100);
        Assert.Single(done);
        Assert.InRange(done[0].Listened, 59.5, 60);
        Assert.Equal(new DateTimeOffset(T0.AddSeconds(60.1)), tracker.Flush()!.Start);
    }

    [Fact]
    public void 暫停中的歌_開始播放才開始算()
    {
        var tracker = new ListenTracker();
        tracker.Observe(Song("Snow halation", playing: false), T0, out var started);
        Assert.Null(started);
        Assert.Null(tracker.Current);
        Play(tracker, Song("Snow halation", at: T0.AddSeconds(10)), 10, 45);
        Assert.Equal(new DateTimeOffset(T0.AddSeconds(10)), tracker.Flush()!.Start);
    }

    [Fact]
    public void 沒有歌名的不算()
    {
        var tracker = new ListenTracker();
        Play(tracker, Song(" "), 0, 60);
        Assert.Null(tracker.Flush());
    }
}

public class ListeningLogTests
{
    private static Track Song(string title, DateTime at) =>
        new("Spotify", "Spotify.exe", title, "蓮ノ空", "Dream Believers", null, true, TimeSpan.Zero, new DateTimeOffset(at), TimeSpan.FromSeconds(200));

    [Fact]
    public void 寫成_JSON_Lines_讀得回來()
    {
        using var temp = new TempFolder();
        var path = temp.File("sub/history.jsonl");
        var log = new ListeningLog(path, _ => { });
        var heard = new List<Listen>();
        log.Listened += heard.Add;
        var t0 = new DateTime(2026, 11, 23, 8, 0, 0);
        for (int i = 0; i <= 400; i++) log.Observe(Song("Dream Believers", t0), t0.AddSeconds(i * 0.1));
        for (int i = 0; i <= 400; i++) log.Observe(Song("On your mark", t0.AddSeconds(40.1)), t0.AddSeconds(40.1 + i * 0.1));
        log.Flush();

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"title\":\"Dream Believers\"", lines[0]);
        Assert.Contains("蓮ノ空", lines[0]);   // 不寫成 \uXXXX
        var back = HistoryFile.Read(path);
        Assert.Equal(heard, back);
        Assert.Equal("On your mark", back[1].Title);
        Assert.Equal("Spotify", back[1].Source);
        Assert.Equal(new DateTimeOffset(t0), back[0].Start);
    }

    [Fact]
    public void 寫不進去_不丟例外_只記錄()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("blocker"), "");
        var errors = new List<string>();
        var log = new ListeningLog(temp.File("blocker/history.jsonl"), errors.Add);   // 上層是檔案，建不了資料夾
        var t0 = new DateTime(2026, 11, 23, 8, 0, 0);
        for (int i = 0; i <= 400; i++) log.Observe(Song("Dream Believers", t0), t0.AddSeconds(i * 0.1));
        log.Flush();
        Assert.Single(errors);
    }

    [Fact]
    public void 壞掉的行跳過()
    {
        var good = HistoryFile.ToLine(new Listen(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)), "A", "B", "C", "Roon", 100, 60));
        var list = HistoryFile.Parse(good + "\n{壞掉\n\n[1,2]\n" + good + "\r\n");
        Assert.Equal(2, list.Count);
        Assert.Equal(TimeSpan.FromHours(8), list[0].Start.Offset);
    }

    [Fact]
    public void 沒有檔案就是空的()
    {
        using var temp = new TempFolder();
        Assert.Empty(HistoryFile.Read(temp.File("none.jsonl")));
    }
}

public class StatsTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 22, 0, 0);   // 星期三

    private static Listen L(DateTime start, string title, string artist, double listened, string album = "Album") =>
        new(new DateTimeOffset(start, TimeSpan.FromHours(8)), title, artist, album, "Roon", 240, listened);

    [Fact]
    public void 期間的範圍()
    {
        Assert.Equal((new DateTime(2026, 11, 23), new DateTime(2026, 11, 26)), Stats.Range(StatsPeriod.Week, Now, DayOfWeek.Monday));
        Assert.Equal(new DateTime(2026, 11, 22), Stats.Range(StatsPeriod.Week, Now, DayOfWeek.Sunday).From);
        Assert.Equal(new DateTime(2026, 11, 1), Stats.Range(StatsPeriod.Month, Now, DayOfWeek.Monday).From);
        Assert.Equal(new DateTime(2026, 1, 1), Stats.Range(StatsPeriod.Year, Now, DayOfWeek.Monday).From);
        Assert.Equal(new DateTime(2024, 5, 6), Stats.Range(StatsPeriod.All, Now, DayOfWeek.Monday, new DateTime(2024, 5, 6, 13, 0, 0)).From);
        Assert.Equal(Now.Date, Stats.Range(StatsPeriod.All, Now, DayOfWeek.Monday).From);
    }

    [Fact]
    public void 總時間與次數_只算期間內()
    {
        var listens = new[]
        {
            L(new(2026, 11, 23, 9, 0, 0), "Snow halation", "μ's", 240),
            L(new(2026, 11, 25, 21, 0, 0), "Snow halation", "μ's", 120),
            L(new(2026, 11, 22, 23, 59, 0), "上週的歌", "μ's", 600),
        };
        var s = Stats.Compute(listens, StatsPeriod.Week, Now, DayOfWeek.Monday);
        Assert.Equal(TimeSpan.FromSeconds(360), s.Total);
        Assert.Equal(2, s.Plays);
        Assert.Equal(3, Stats.Compute(listens, StatsPeriod.Month, Now, DayOfWeek.Monday).Plays);
    }

    [Fact]
    public void 排行依聽的時間_不是次數()
    {
        var day = new DateTime(2026, 11, 24, 12, 0, 0);
        var listens = new[]
        {
            L(day, "Short", "Aqours", 40), L(day, "Short", "Aqours", 40), L(day, "short ", "aqours", 40),
            L(day, "Long", "Liella!", 300),
        };
        var top = Stats.TopTracks(listens);
        Assert.Equal(["Long", "Short"], top.Select(t => t.Name));
        Assert.Equal(3, top[1].Plays);
        Assert.Equal("Aqours", top[1].Detail);
        Assert.Equal(["Liella!", "Aqours"], Stats.TopArtists(listens).Select(t => t.Name));
        Assert.Single(Stats.TopTracks(listens, top: 1));
    }

    [Fact]
    public void 同名專輯依藝人分開()
    {
        var day = new DateTime(2026, 11, 24);
        var top = Stats.TopAlbums([L(day, "a", "Aqours", 100, "Best"), L(day, "b", "μ's", 50, "Best"), L(day, "c", "μ's", 50, "")]);
        Assert.Equal(2, top.Count);
        Assert.Equal(("Best", "Aqours"), (top[0].Name, top[0].Detail));
    }

    [Fact]
    public void 每天與每小時()
    {
        var listens = new[] { L(new(2026, 11, 23, 9, 30, 0), "a", "x", 100), L(new(2026, 11, 25, 9, 5, 0), "b", "x", 50), L(new(2026, 11, 25, 23, 0, 0), "c", "x", 10) };
        var s = Stats.Compute(listens, StatsPeriod.Week, Now, DayOfWeek.Monday);
        Assert.Equal(3, s.Daily.Count);
        Assert.Equal(TimeSpan.Zero, s.Daily[1].Time);
        Assert.Equal(TimeSpan.FromSeconds(60), s.Daily[2].Time);
        Assert.Equal(TimeSpan.FromSeconds(150), s.Hours[9]);
        Assert.Equal(TimeSpan.FromSeconds(10), s.Hours[23]);
        Assert.Equal(24, s.Hours.Length);
    }

    [Fact]
    public void 連續天數_今天還沒聽不算斷()
    {
        var days = new[] { 25, 24, 23, 21 }.Select(d => L(new(2026, 11, d, 12, 0, 0), "a", "x", 60)).ToList();
        Assert.Equal(3, Stats.Streak(days, Now));
        Assert.Equal(2, Stats.Streak(days.Skip(1), Now));                  // 今天還沒聽：從昨天往回數
        Assert.Equal(0, Stats.Streak(days.Skip(3), Now));                  // 昨天也沒聽
        Assert.Equal(0, Stats.Streak([], Now));
    }

    [Fact]
    public void 長條圖_年是一個月一根_好幾年是一年一根()
    {
        var year = Stats.Compute([L(new(2026, 3, 3), "a", "x", 60), L(new(2026, 3, 9), "a", "x", 60)], StatsPeriod.Year, Now, DayOfWeek.Monday);
        var bars = Stats.Bars(year.Daily, StatsPeriod.Year);
        Assert.Equal(11, bars.Count);
        Assert.Equal(TimeSpan.FromSeconds(120), bars[2].Time);

        var all = Stats.Compute([L(new(2024, 6, 1), "a", "x", 60)], StatsPeriod.All, Now, DayOfWeek.Monday);
        Assert.Equal([2024, 2025, 2026], Stats.Bars(all.Daily, StatsPeriod.All).Select(b => b.Start.Year));

        var week = Stats.Compute([], StatsPeriod.Week, Now, DayOfWeek.Monday);
        Assert.Equal(3, Stats.Bars(week.Daily, StatsPeriod.Week).Count);
    }
}

public class LyricFavoritesTests
{
    private static readonly DateTimeOffset When = new(2026, 11, 23, 20, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void 加入_列出_刪除_重開還在()
    {
        using var temp = new TempFolder();
        var path = temp.File("favorites.json");
        var store = new LyricFavorites(path, _ => { });
        var a = store.Add("届かない星だとしても", "就算是遙不可及的星星", "Snow halation", "μ's", "Snow halation", TimeSpan.FromSeconds(62), When)!;
        var b = store.Add("夢を夢で終わらせない", "", "Dream Believers", "蓮ノ空", "Dream Believers", TimeSpan.FromSeconds(30), When.AddMinutes(1))!;
        Assert.Equal([b.Id, a.Id], store.All.Select(f => f.Id));   // 新的在前

        var again = new LyricFavorites(path, _ => { });
        Assert.Equal(store.All, again.All);
        Assert.True(again.Remove(a.Id));
        Assert.False(again.Remove(a.Id));
        Assert.Equal([b.Id], new LyricFavorites(path, _ => { }).All.Select(f => f.Id));
    }

    [Fact]
    public void 搜尋不分大小寫_每個欄位都找()
    {
        using var temp = new TempFolder();
        var store = new LyricFavorites(temp.File("favorites.json"), _ => { });
        store.Add("Snow halation", "", "Snow halation", "μ's", "", TimeSpan.Zero, When);
        store.Add("夢を夢で終わらせない", "不讓夢想只是夢想", "Dream Believers", "蓮ノ空", "", TimeSpan.Zero, When);
        Assert.Single(store.Search("SNOW"));
        Assert.Single(store.Search("dream"));
        Assert.Single(store.Search("夢想"));
        Assert.Single(store.Search("蓮ノ空"));
        Assert.Equal(2, store.Search("  ").Count);
        Assert.Empty(store.Search("Aqours"));
    }

    [Fact]
    public void 同一句不重複_空白不收()
    {
        using var temp = new TempFolder();
        var store = new LyricFavorites(temp.File("favorites.json"), _ => { });
        var a = store.Add("Snow halation", "", "Snow halation", "μ's", "", TimeSpan.Zero, When);
        Assert.Equal(a, store.Add(" Snow halation ", "", "Snow halation", "μ's", "", TimeSpan.Zero, When.AddDays(1)));
        Assert.Null(store.Add("   ", "", "Snow halation", "μ's", "", TimeSpan.Zero, When));
        Assert.Single(store.All);
    }

    [Fact]
    public void 檔案壞掉_不丟例外_先備份()
    {
        using var temp = new TempFolder();
        var path = temp.File("favorites.json");
        File.WriteAllText(path, "{ 這不是 JSON");
        var errors = new List<string>();
        var store = new LyricFavorites(path, errors.Add);
        Assert.Empty(store.All);
        Assert.Single(errors);
        Assert.True(File.Exists(path + ".bad"));
    }
}

public class ListenBrainzTests
{
    private static Listen Done(string album = "Snow halation") =>
        new(new DateTimeOffset(2026, 11, 23, 21, 0, 0, TimeSpan.FromHours(8)), "Snow halation", "μ's", album, "Roon", 259.5, 200);

    [Fact]
    public void 正在播放_沒有_listened_at()
    {
        var json = JsonNode.Parse(ListenBrainzJson.PlayingNow("Snow halation", "μ's", "", 259.5, "Spotify"))!;
        Assert.Equal("playing_now", json["listen_type"]!.ToString());
        var listen = json["payload"]!.AsArray().Single()!.AsObject();
        Assert.False(listen.ContainsKey("listened_at"));
        var meta = listen["track_metadata"]!.AsObject();
        Assert.Equal("μ's", meta["artist_name"]!.ToString());
        Assert.Equal("Snow halation", meta["track_name"]!.ToString());
        Assert.False(meta.ContainsKey("release_name"));   // 沒有資料的欄位整個不寫
        var info = meta["additional_info"]!.AsObject();
        Assert.Equal(259500, info["duration_ms"]!.GetValue<long>());
        Assert.False(info.ContainsKey("duration"));
        Assert.Equal("Spotify", info["media_player"]!.ToString());
        Assert.Equal("Shima", info["submission_client"]!.ToString());
        Assert.False(string.IsNullOrEmpty(info["submission_client_version"]!.ToString()));
    }

    [Fact]
    public void 聽完一首_listened_at_是開始時間的_UNIX_秒()
    {
        var json = JsonNode.Parse(ListenBrainzJson.Single(Done()))!;
        Assert.Equal("single", json["listen_type"]!.ToString());
        var listen = json["payload"]![0]!;
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 13, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), listen["listened_at"]!.GetValue<long>());
        Assert.Equal("Snow halation", listen["track_metadata"]!["release_name"]!.ToString());
        Assert.Equal("Roon", listen["track_metadata"]!["additional_info"]!["media_player"]!.ToString());
    }

    [Theory]
    [InlineData(120, 240, true)]    // 一半
    [InlineData(119, 240, false)]
    [InlineData(240, 600, true)]    // 長歌：四分鐘就夠
    [InlineData(239, 600, false)]
    [InlineData(239, 0, false)]     // 不知道長度：要四分鐘
    [InlineData(240, 0, true)]
    public void 官方的門檻(double listened, double duration, bool submit) =>
        Assert.Equal(submit, ListenBrainzJson.ShouldSubmit(listened, duration));

    private sealed class FakeHandler(HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent("""{"code":401,"error":"Invalid authorization token."}""") };
        }
    }

    [Fact]
    public async Task 送出的請求格式_不經過真的網路()
    {
        var handler = new FakeHandler();
        using var scrobbler = new Scrobbler("fake-token", handler, _ => { }, TimeSpan.Zero);
        Assert.True(await scrobbler.Submit(Done()));
        var (request, body) = handler.Sent.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.listenbrainz.org/1/submit-listens", request.RequestUri!.ToString());
        Assert.Equal("Token fake-token", request.Headers.Authorization!.ToString());
        Assert.StartsWith("Shima/", request.Headers.UserAgent.ToString());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("single", JsonNode.Parse(body)!["listen_type"]!.ToString());
    }

    [Fact]
    public async Task 沒有_token_或沒聽夠_什麼都不送()
    {
        var handler = new FakeHandler();
        using (var off = new Scrobbler("  ", handler, _ => { }, TimeSpan.Zero))
        {
            Assert.False(off.Enabled);
            Assert.False(await off.Submit(Done()));
            await off.NowPlaying(new Track("Roon", "Roon", "Snow halation", "μ's", "", null, true, TimeSpan.Zero, default, TimeSpan.FromMinutes(4)));
        }
        using (var on = new Scrobbler("fake-token", handler, _ => { }, TimeSpan.Zero))
        {
            Assert.False(await on.Submit(Done() with { Listened = 60 }));
            Assert.False(await on.Submit(Done() with { Artist = "" }));
        }
        Assert.Empty(handler.Sent);
    }

    [Fact]
    public async Task 伺服器拒絕_只記錄_不洩漏_token()
    {
        var errors = new List<string>();
        using var scrobbler = new Scrobbler("secret-token-123", new FakeHandler(HttpStatusCode.Unauthorized), errors.Add, TimeSpan.Zero);
        Assert.False(await scrobbler.Submit(Done()));
        var error = Assert.Single(errors);
        Assert.Contains("401", error);
        Assert.DoesNotContain("secret-token-123", error);
    }

    [Fact]
    public async Task 正在播放_快速切歌只送最後一首()
    {
        var handler = new FakeHandler();
        using var scrobbler = new Scrobbler("fake-token", handler, _ => { }, TimeSpan.FromMilliseconds(300));
        Track Song(string t) => new("Roon", "Roon", t, "Aqours", "", null, true, TimeSpan.Zero, default, TimeSpan.FromMinutes(4));
        await scrobbler.Submit(Done());   // 先送一個，接下來的都要排隊等
        await Task.WhenAll(scrobbler.NowPlaying(Song("青空Jumping Heart")), scrobbler.NowPlaying(Song("恋になりたいAQUARIUM")), scrobbler.NowPlaying(Song("MIRACLE WAVE")));
        Assert.Equal(2, handler.Sent.Count);
        Assert.Contains("MIRACLE WAVE", handler.Sent[1].Body);
    }
}

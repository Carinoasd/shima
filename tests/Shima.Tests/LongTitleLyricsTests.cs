// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Shima.Lyrics;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class LongTitleLyricsTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "shima-lyrics-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); } catch { }
    }

    // ---------------------------------------------------------------- 長標題解析

    [Theory]
    [InlineData("『超かぐや姫 ! 』公式和Aqu3ra", "【Official MV】星降る海 - Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！", "Aqu3ra / 月見ヤチヨ", "星降る海")]
    [InlineData("『超かぐや姫 ! 』公式和Aqu3ra", "【Official MV】星降る海 – Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！", "Aqu3ra / 月見ヤチヨ", "星降る海")]
    [InlineData("", "【Official MV】星降る海 - Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！", "Aqu3ra / 月見ヤチヨ", "星降る海")]
    [InlineData("Aqu3ra", "【Official MV】星降る海 - Aqu3ra", "Aqu3ra", "星降る海")]
    [InlineData("YOASOBI", "YOASOBI - 夜に駆ける / Racing into the Night", "YOASOBI", "夜に駆ける / Racing into the Night")]
    [InlineData("Ayase / YOASOBI", "YOASOBI「アイドル」 Official Music Video", "YOASOBI", "アイドル")]
    [InlineData("YOASOBI", "TVアニメ『推しの子』OP主題歌「アイドル」", "YOASOBI", "アイドル")]
    [InlineData("Bruce Springsteen", "Greetings from Asbury Park", "Bruce Springsteen", "Greetings from Asbury Park")]
    public void 長標題能正確抽出歌名與藝人(string artist, string title, string wantArtist, string wantTitle)
    {
        Assert.Equal((wantArtist, wantTitle), Titles.Clean(artist, title, split: true));
    }

    [Fact]
    public void Roon_不拆也不會剝掉括號與_from()
    {
        var raw = "星降る海 - Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！";
        Assert.Equal(("Aqu3ra", raw), Titles.Clean("Aqu3ra", raw, split: false));
    }

    [Fact]
    public void 備選清單包含多位藝人_聲優_雙語前半與左右互換()
    {
        var alts = Titles.Alternates(
            "『超かぐや姫 ! 』公式和Aqu3ra",
            "【Official MV】星降る海 - Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！",
            split: true);
        Assert.InRange(alts.Count, 2, 4);
        Assert.Contains(("月見ヤチヨ", "星降る海"), alts);
        Assert.Contains(("早見沙織", "星降る海"), alts);

        var fanUpload = Titles.Alternates("AnimeFanChannel", "【Official MV】夜に駆ける - YOASOBI", split: true);
        Assert.Contains(("YOASOBI", "夜に駆ける"), fanUpload);

        var bilingual = Titles.Alternates("YOASOBI", "YOASOBI - 夜に駆ける / Racing into the Night", split: true);
        Assert.Contains(("YOASOBI", "夜に駆ける"), bilingual);

        var fromTitle = Titles.Alternates("Aqu3ra", "【Official MV】星降る海 from 超かぐや姫！", split: true);
        Assert.Contains(("Aqu3ra", "星降る海"), fromTitle);
    }

    // ---------------------------------------------------------------- SongPicker 放寬容許差

    [Fact]
    public void 放寬長度容許時接受_MV_片頭片尾差但標記非_Exact()
    {
        var items = new[]
        {
            new SongCandidate("星降る海", "Aqu3ra", 262),   // +10 秒
            new SongCandidate("星降る海", "Aqu3ra", 258),   // +6 秒（較近）
        };

        // 預設 ±2 秒：都不算
        Assert.Equal(-1, SongPicker.Pick(items, "星降る海", "Aqu3ra", 252).Index);

        // 放寬到 ±15 秒：挑比較近的，且 Exact = false
        Assert.Equal((1, false), SongPicker.Pick(items, "星降る海", "Aqu3ra", 252, tolerance: 15));

        // 距離 <= 2 秒的仍是 Exact = true
        var exactItems = new[] { new SongCandidate("星降る海", "Aqu3ra", 253.5) };
        Assert.Equal((0, true), SongPicker.Pick(exactItems, "星降る海", "Aqu3ra", 252, tolerance: 15));

        // 放寬長度仍不接受別的藝人（除非差 <= 1.5 秒）
        Assert.Equal(-1, SongPicker.Pick(items, "星降る海", "別人", 252, tolerance: 15).Index);
    }

    // ---------------------------------------------------------------- LyricsFinder 第二輪回退

    private sealed class RecordingProvider(string name, Func<SongQuery, LyricsHit?> answer) : ILyricsProvider
    {
        public List<SongQuery> Seen { get; } = [];
        public string Name => name;
        public Task<LyricsHit?> FindAsync(SongQuery q)
        {
            Seen.Add(q);
            return Task.FromResult(answer(q));
        }
    }

    private static LyricsHit SampleHit(string provider, string text = "星降る海へ", bool exact = true) =>
        new(new LyricSheet([new LyricLine(TimeSpan.FromSeconds(1), text)], true), exact, provider);

    [Fact]
    public async Task 瀏覽器曲目第一輪沒中會自動放寬並嘗試備選組合()
    {
        var provider = new RecordingProvider("lrclib", q =>
            q.Artist == "YOASOBI" && q.Title == "夜に駆ける" && q.Tolerance >= 15
                ? SampleHit("lrclib", "沈むように溶けてゆくように", exact: false)
                : null);

        var finder = new LyricsFinder([provider]);
        var track = new Track("Chrome", "chrome.exe", "【Official MV】夜に駆ける - YOASOBI", "AnimeFan", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(265));
        var hit = await finder.FindAsync(track);

        Assert.NotNull(hit);
        Assert.Equal("沈むように溶けてゆくように", hit.Sheet.Lines[0].Text);
        Assert.Contains(provider.Seen, q => q.Tolerance == 2);
        Assert.Contains(provider.Seen, q => q.Artist == "YOASOBI" && q.Title == "夜に駆ける" && q.Tolerance == 15);
    }

    [Fact]
    public async Task Roon_曲目不做放寬備選回退()
    {
        var provider = new RecordingProvider("lrclib", q => q.Tolerance > 2 ? SampleHit("lrclib") : null);
        var finder = new LyricsFinder([provider]);
        var track = new Track("Roon", RoonSource.AppId, "星降る海", "Aqu3ra", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(252));

        Assert.Null(await finder.FindAsync(track));
        Assert.Single(provider.Seen);
        Assert.Equal(2, provider.Seen[0].Tolerance);
    }

    // ---------------------------------------------------------------- 手動綁定記憶

    [Fact]
    public async Task 手動綁定歌詞會存到磁碟且優先於自動搜尋_解除後恢復自動()
    {
        var manualDir = Path.Combine(_temp, "manual");
        var cacheDir = Path.Combine(_temp, "cache");
        var provider = new RecordingProvider("lrclib", _ => SampleHit("lrclib", "自動歌詞"));

        var track = new Track("Chrome", "chrome.exe",
            "【Official MV】星降る海 - Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！",
            "『超かぐや姫 ! 』公式和Aqu3ra", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(252));

        var finder1 = new LyricsFinder([provider], cacheDir, manualDir);
        finder1.Bind(track, SampleHit("netease", "手動挑中的歌詞"));

        // 重建新的 LyricsFinder，模擬程式重開：仍直接讀回手動綁定，完全不問線上來源
        var finder2 = new LyricsFinder([provider], cacheDir, manualDir);
        var bound = await finder2.FindAsync(track);
        Assert.NotNull(bound);
        Assert.Equal("netease", bound.Provider);
        Assert.Equal("手動挑中的歌詞", bound.Sheet.Lines[0].Text);
        Assert.Empty(provider.Seen);

        // 解除綁定後再查：恢復走正常來源
        finder2.Unbind(track);
        var finder3 = new LyricsFinder([provider], cacheDir, manualDir);
        var after = await finder3.FindAsync(track);
        Assert.NotNull(after);
        Assert.Equal("lrclib", after.Provider);
        Assert.Equal("自動歌詞", after.Sheet.Lines[0].Text);
    }

    // ---------------------------------------------------------------- 關鍵字搜尋（四個來源）

    private sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task 四個來源都支援關鍵字搜尋並可載入歌詞()
    {
        var lrclib = new LrclibProvider(new StubHttp(_ => Json(new[]
        {
            new { trackName = "星降る海", artistName = "Aqu3ra", duration = 254.0, syncedLyrics = "[00:01.00]星降る海\n[00:05.00]光る波", plainLyrics = "" },
            new { trackName = "星降る海 (Plain)", artistName = "Aqu3ra", duration = 254.0, syncedLyrics = "", plainLyrics = "純文字歌詞" },
        })));

        var netease = new NetEaseProvider(new StubHttp(req =>
            req.RequestUri!.AbsolutePath.Contains("search")
                ? Json(new { result = new { songs = new[] { new { id = 12345L, name = "星降る海", duration = 254000, artists = new[] { new { name = "Aqu3ra" } } } } } })
                : Json(new { lrc = new { lyric = "[00:01.00]星降る海\n[00:05.00]網易雲版" }, tlyric = new { lyric = "[00:01.00]繁星墜落之海" } })));

        var qq = new QQProvider(new StubHttp(req =>
            req.RequestUri!.Host.Contains("u.y.qq.com")
                ? Json(new { req = new { data = new { body = new { song = new { list = new[] { new { mid = "001abc", title = "星降る海", name = "星降る海", interval = 254, singer = new[] { new { name = "Aqu3ra" } } } } } } } } })
                : Json(new { lyric = "[00:01.00]星降る海 QQ 版", trans = "" })));

        var kugouLrc = Convert.ToBase64String(Encoding.UTF8.GetBytes("[00:01.00]星降る海 酷狗版"));
        var kugou = new KugouProvider(new StubHttp(req =>
            req.RequestUri!.Host.Contains("songsearch")
                ? Json(new { data = new { lists = new[] { new { FileHash = "HASH1", SongName = "<em>星降る海</em>", SingerName = "Aqu3ra", Duration = 254 } } } })
                : req.RequestUri.Host.Contains("krcs")
                    ? Json(new { candidates = new[] { new { id = "99", accesskey = "KEY99" } } })
                    : Json(new { content = kugouLrc })));

        var finder = new LyricsFinder([lrclib, netease, qq, kugou]);
        var choices = await finder.SearchAsync("星降る海");

        Assert.Equal(5, choices.Count);
        // 有時間軸的排在純文字前面
        Assert.All(choices.Take(4), c => Assert.True(c.Synced));
        Assert.False(choices[4].Synced);

        foreach (var choice in choices)
        {
            var sheet = await choice.LoadAsync(CancellationToken.None);
            Assert.NotNull(sheet);
            Assert.NotEmpty(sheet.Lines);
        }

        // 網易雲翻譯一起帶回
        var neSheet = await choices.First(c => c.Provider == "netease").LoadAsync(CancellationToken.None);
        Assert.Equal("繁星墜落之海", neSheet!.Translation![0]);
    }

    [Fact]
    public async Task 關鍵字搜尋有一個來源失敗不影響其他來源()
    {
        var broken = new LrclibProvider(new StubHttp(_ => throw new HttpRequestException("offline")));
        var ok = new NetEaseProvider(new StubHttp(_ =>
            Json(new { result = new { songs = new[] { new { id = 1L, name = "星降る海", duration = 250000, artists = new[] { new { name = "Aqu3ra" } } } } } })));

        var choices = await new LyricsFinder([broken, ok]).SearchAsync("星降る海");
        Assert.Single(choices);
        Assert.Equal("netease", choices[0].Provider);
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

public class LrcTests
{
    [Fact]
    public void 時間標記轉成排好序的行()
    {
        var lines = Lrc.Parse("[00:12.50]第二句\n[ar:某人]\n[00:01.00][01:00.00]副歌\n沒有時間的行");
        Assert.Equal(["副歌", "第二句", "副歌"], lines.Select(l => l.Text));
        Assert.Equal([1, 12.5, 60], lines.Select(l => l.Time.TotalSeconds));
    }

    [Fact]
    public void Offset_讓整份提早()
    {
        var lines = Lrc.Parse("[offset:+500]\n[00:10.00]a");
        Assert.Equal(9.5, lines[0].Time.TotalSeconds);
    }

    [Fact]
    public void 找出正在唱的那一行()
    {
        var lines = Lrc.Parse("[00:05.00]a\n[00:10.00]b\n[00:15.00]c");
        Assert.Equal(-1, Lrc.LineAt(lines, TimeSpan.FromSeconds(4)));
        Assert.Equal(0, Lrc.LineAt(lines, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Lrc.LineAt(lines, TimeSpan.FromSeconds(14.9)));
        Assert.Equal(2, Lrc.LineAt(lines, TimeSpan.FromSeconds(99)));
    }

    [Fact]
    public void 製作名單不是歌詞()
    {
        var lrc = "[00:00.000] 作词 : 畑 亜貴\n[00:00.000] 作曲 : 渡辺拓也\n[00:00.000]一緒に見たいんだ\n[01:00.00]Re: start";
        Assert.Equal(["一緒に見たいんだ", "Re: start"], Lrc.Parse(Lrc.StripCredits(lrc)).Select(l => l.Text));
    }
}

public class CreditsTests
{
    [Fact]
    public void QQ_與酷狗開頭的歌名行和製作名單都拿掉()
    {
        var lrc = "[ti:Dream Believers]\r\n[00:00.00]Dream Believers (SAKURA Ver.) - 蓮ノ空女学院スクールアイドルクラブ\r\n[00:00.66]词：畑 亜貴\r\n[00:01.32]曲：渡辺拓也\n[00:01.98]编曲：渡辺拓也\n[00:03.00]一緒に見たいんだ\n[01:00.00]Dream Believers - you believe!";
        Assert.Equal(["一緒に見たいんだ", "Dream Believers - you believe!"], Lrc.Parse(Lrc.StripCredits(lrc, "Dream Believers (SAKURA Ver.)")).Select(l => l.Text));
    }
}

public class TitlesTests
{
    [Theory]
    [InlineData("ツバサ・ラ・リベルテ (104期Ver.)", "ツバサ・ラ・リベルテ")]
    [InlineData("Dream Believers（105期Ver.）", "Dream Believers")]
    [InlineData("Song [TV Size] (Instrumental)", "Song")]
    [InlineData("(They Long to Be) Close to You", "(They Long to Be) Close to You")]
    [InlineData("(Everything)", "(Everything)")]
    public void 結尾的括號拿掉(string title, string bare) => Assert.Equal(bare, Titles.Bare(title));

    [Theory]
    [InlineData("Dream Believers (104期 Ver.)", "104")]
    [InlineData("Dream Believers (104th Ver.)", "104")]
    [InlineData("Dream Believers（１０４期Ver.）", "104")]
    [InlineData("Dream Believers (SAKURA Ver.)", "sakura")]
    [InlineData("Dream Believers (B. G. P. Ver.)", "bgp")]
    [InlineData("Dream Believers", "")]
    public void 版本記號取自括號(string title, string tag) => Assert.Equal(tag, Titles.VersionTag(title));

    [Theory]
    [InlineData("A / B", "A")]
    [InlineData("A feat. B", "A")]
    [InlineData("A, B", "A")]
    [InlineData("蓮ノ空女学院スクールアイドルクラブ", "蓮ノ空女学院スクールアイドルクラブ")]
    public void 多位藝人取第一位(string artist, string main) => Assert.Equal(main, Titles.MainArtist(artist));

    [Theory]
    [InlineData("YOASOBI - Topic", "アイドル", "YOASOBI", "アイドル")]
    [InlineData("SomeVEVO", "Artist - Song (Official Video)", "Artist", "Song")]
    [InlineData("Channel", "【MV】曲名 / 歌手", "歌手", "曲名")]
    [InlineData("Artist", "Song [4K] (Lyric Video)", "Artist", "Song")]
    public void 瀏覽器的標題拆成藝人與歌名(string artist, string title, string a, string t) =>
        Assert.Equal((a, t), Titles.Clean(artist, title, split: true));

    [Fact]
    public void Roon_的歌名不拆也不動版本括號()
    {
        Assert.Equal(("ISSA", "Justiφ's"), Titles.Clean("ISSA", "Justiφ's", split: false));
        Assert.Equal(("A", "Dream Believers (SAKURA Ver.)"), Titles.Clean("A", "Dream Believers (SAKURA Ver.)", split: false));
        Assert.Equal(("A", "Round ZERO～BLADE BRAVE"), Titles.Clean("A", "Round ZERO～BLADE BRAVE", split: false));
    }
}

public class SongPickerTests
{
    private const string Hasu = "蓮ノ空女学院スクールアイドルクラブ";
    private static SongCandidate S(string title, double seconds = 317.7, string artist = Hasu, int quality = 3) => new(title, artist, seconds, quality);

    private static readonly SongCandidate[] Found =
    [
        S("Dream Believers"), S("On your mark", 236.1), S("Dream Believers (B. G. P. Ver.)", 318.4), S("Dream Believers (104期 Ver.)"),
        S("Dream Believers (SAKURA Ver.)"), S("Dream Believers (105期Ver.)", 318.4),
    ];

    [Theory]
    [InlineData("Dream Believers (SAKURA Ver.)", 4, true)]
    [InlineData("Dream Believers (104期Ver.)", 3, true)]
    [InlineData("Dream Believers (104th Ver.)", 3, true)]
    [InlineData("Dream Believers", 0, true)]
    [InlineData("Dream Believers (Off Vocal)", 0, false)]
    public void 同版本優先_其次原曲(string title, int index, bool exact) =>
        Assert.Equal((index, exact), SongPicker.Pick(Found, title, Hasu, 317));

    [Fact]
    public void 絕不拿別的版本()
    {
        var others = new[] { S("Dream Believers (104期 Ver.)"), S("Dream Believers (B. G. P. Ver.)") };
        Assert.Equal(-1, SongPicker.Pick(others, "Dream Believers (SAKURA Ver.)", Hasu, 317).Index);
        Assert.Equal(-1, SongPicker.Pick(others, "Dream Believers", Hasu, 317).Index);
    }

    [Fact]
    public void 別首歌_別的藝人_長度不對都不算()
    {
        Assert.Equal(-1, SongPicker.Pick(Found, "Hello, new dream!", Hasu, 246).Index);
        Assert.Equal(-1, SongPicker.Pick(Found, "Dream Believers", "DOLLCHESTRA", 0).Index);
        Assert.Equal(-1, SongPicker.Pick(Found, "Dream Believers", "DOLLCHESTRA", 310).Index);
        Assert.Equal(-1, SongPicker.Pick(Found, "Dream Believers", Hasu, 200).Index);
    }

    [Fact]
    public void 藝人是羅馬字也認得_但要長度吻合()
    {
        var items = new[] { S("ひとりぼっち東京", 232.2, "結束バンド"), S("ひとりぼっち東京", 190, "別人") };
        Assert.Equal((0, true), SongPicker.Pick(items, "ひとりぼっち東京", "kessoku band", 232));
        Assert.Equal(-1, SongPicker.Pick(items, "ひとりぼっち東京", "kessoku band", 0).Index);
        Assert.Equal(-1, SongPicker.Pick(items, "ひとりぼっち東京", "kessoku band", 236).Index);
    }

    [Fact]
    public void 藝人名相符的排在前面()
    {
        var items = new[] { S("Song", 200, "Someone Else"), S("Song", 200.5, "Artist") };
        Assert.Equal(1, SongPicker.Pick(items, "Song", "Artist", 200).Index);
    }

    [Fact]
    public void 有時間軸的優先於純文字()
    {
        var items = new[] { S("Song", quality: 2), S("Song", quality: 0), S("Song") };
        Assert.Equal(2, SongPicker.Pick(items, "Song", Hasu, 317).Index);
    }
}

public class LocalProviderTests
{
    [Fact]
    public void 檔名有沒有曲目編號或藝人都找得到()
    {
        var q = new SongQuery("ツバサ・ラ・リベルテ (104期Ver.)", "蓮ノ空女学院スクールアイドルクラブ", "", 297);
        var wanted = LocalProvider.Keys(q).ToHashSet();
        Assert.Contains(wanted, k => LocalProvider.KeysOf(@"D:\Music\03 ツバサ・ラ・リベルテ (104期Ver.).lrc").Contains(k));
        Assert.Contains(wanted, k => LocalProvider.KeysOf("蓮ノ空女学院スクールアイドルクラブ - ツバサ・ラ・リベルテ (104期Ver.).lrc").Contains(k));
        Assert.DoesNotContain(wanted, k => LocalProvider.KeysOf("ツバサ・ラ・リベルテ.lrc").Contains(k));
    }
}

public class FinderTests
{
    private sealed class Fake(string name, LyricsHit? hit) : ILyricsProvider
    {
        public int Asked;
        public string Name => name;
        public Task<LyricsHit?> FindAsync(SongQuery q) { Asked++; return Task.FromResult(hit); }
    }

    private static LyricsHit Hit(string provider, bool exact) =>
        new(new LyricSheet([new LyricLine(TimeSpan.Zero, provider)], true), exact, provider);

    private static Shima.Playback.Track Song() =>
        new("Test", "test", "finder-test-" + Guid.NewGuid().ToString("N"), "A", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(100));

    [Fact]
    public async Task 同版本的歌詞勝過排在前面的原曲()
    {
        Fake first = new("a", Hit("a", exact: false)), second = new("b", Hit("b", exact: true)), third = new("c", Hit("c", exact: true));
        var hit = await new LyricsFinder([first, second, third]).FindAsync(Song());
        Assert.Equal("b", hit!.Provider);
        Assert.Equal(0, third.Asked);
    }

    [Fact]
    public async Task 都沒有同版本就用最前面的原曲()
    {
        Fake first = new("a", null), second = new("b", Hit("b", exact: false)), third = new("c", Hit("c", exact: false));
        var hit = await new LyricsFinder([first, second, third]).FindAsync(Song());
        Assert.Equal("b", hit!.Provider);
    }

    [Fact]
    public async Task 同一首歌不問第二次()
    {
        var only = new Fake("a", Hit("a", exact: true));
        var finder = new LyricsFinder([only]);
        var song = Song();
        await finder.FindAsync(song);
        await finder.FindAsync(song);
        Assert.Equal(1, only.Asked);
    }
}

public class SecondLineTests
{
    [Fact]
    public void 翻譯依時間對到每一行_差太多的不算()
    {
        var lines = Lrc.Parse("[00:00.00]一緒に見たいんだ\n[00:06.50]消えない夢\n[00:24.84]胸に舞いおりた");
        var other = Lrc.Parse("[00:00.10]想要和你一同見證\n[00:06.50]永不消失的夢想\n[00:30.00]別的句子");
        Assert.Equal(["想要和你一同見證", "永不消失的夢想", ""], Lrc.Align(lines, other));
    }

    [Fact]
    public void 一行都對不上就是沒有()
    {
        var lines = Lrc.Parse("[00:10.00]a");
        Assert.Null(Lrc.Align(lines, Lrc.Parse("[00:50.00]b")));
        Assert.Null(Lrc.Align(lines, []));
    }

    [Fact]
    public void 簡體字轉成繁體字() => Assert.Equal("想要和你一同見證 永不消失的夢想", Chinese.ToTraditional("想要和你一同见证 永不消失的梦想"));
}

public class LyricsChoiceTests
{
    private sealed class Fake(string name, LyricsHit? hit) : ILyricsProvider
    {
        public string Name => name;
        public Task<LyricsHit?> FindAsync(SongQuery q) => Task.FromResult(hit);
    }

    private static LyricsHit Hit(string provider, bool exact) =>
        new(new LyricSheet([new LyricLine(TimeSpan.Zero, provider)], true), exact, provider);

    [Fact]
    public async Task 換一份時列出每個來源找到的_同版本排前面()
    {
        var finder = new LyricsFinder([new Fake("a", Hit("a", false)), new Fake("b", null), new Fake("c", Hit("c", true))]);
        var track = new Shima.Playback.Track("Test", "test", "choice-" + Guid.NewGuid().ToString("N"), "A", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(100));
        Assert.Equal(["c", "a"], (await finder.AllAsync(track)).Select(h => h.Provider));
    }

    [Fact]
    public void 同一首歌不同長度是同一個身分()
    {
        Shima.Playback.Track T(double seconds) => new("Roon", "Roon", "Dream Believers (SAKURA Ver.)", "蓮ノ空 / 誰か", "", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(seconds));
        Assert.Equal(LyricsFinder.IdOf(T(317)), LyricsFinder.IdOf(T(0)));
        Assert.NotEqual("", LyricsFinder.IdOf(T(317)));
    }
}

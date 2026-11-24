// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Shima.Lyrics;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>網頁能控制歌名：病態的輸入不能讓 Titles 跑很久，也不能丟例外。</summary>
public class TitlesLimitTests
{
    // 沒有截長度之前實測：5000 個「(」的 Clean 要 3.5 秒、50000 個要超過 15 秒；5 萬個空白的 Bare 要 8 秒
    public static TheoryData<string, string> Pathological => new()
    {
        { "幾千個開括號", new string('(', 50000) },
        { "開括號加關鍵字", string.Concat(Enumerable.Repeat("(official", 20000)) },
        { "很多組括號再接一個字", string.Concat(Enumerable.Repeat("(a)", 50000)) + "x" },
        { "大量空白", new string(' ', 50000) + "x" },
        { "空白加連字號", new string(' ', 30000) + "-x" },
        { "重複的 - mi", "a" + string.Concat(Enumerable.Repeat(" - mi", 20000)) },
        { "關鍵字前綴反覆出現", "a - " + string.Concat(Enumerable.Repeat("remaste", 20000)) },
        { "未關的方括號加前綴", "[" + string.Concat(Enumerable.Repeat("officia", 20000)) },
        { "artist 分隔字前綴", string.Concat(Enumerable.Repeat(" fe", 20000)) + "x" },
        { "超長一般文字", new string('字', 1_000_000) },
    };

    [Theory]
    [MemberData(nameof(Pathological))]
    public void 病態輸入_每個公開函式都很快跑完(string name, string text)
    {
        var sw = Stopwatch.StartNew();
        Titles.Norm(text);
        Titles.Bare(text);
        Titles.VersionTag(text);
        Titles.MainArtist(text);
        Titles.SameArtist(text, text);
        Titles.Clean(text, text, true);
        Titles.Clean(text, text, false);
        PageTitle.Clean(text);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{name}: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void 結果也被截在上限內()
    {
        var text = new string('a', 100_000);
        Assert.True(Titles.Bare(text).Length <= Titles.MaxLength);
        var (artist, title) = Titles.Clean(text, text, true);
        Assert.True(artist.Length <= Titles.MaxLength && title.Length <= Titles.MaxLength);
        Assert.True(PageTitle.Clean(text).Length <= Titles.MaxLength);
    }

    [Fact]
    public void 截長度不切開代理字元對()
    {
        var text = new string('a', Titles.MaxLength - 1) + "😀" + "tail";
        var cut = Titles.Cap(text);
        Assert.Equal(Titles.MaxLength - 1, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
        Assert.Equal("短", Titles.Cap("短"));
        Assert.Equal("", Titles.Cap(null));
    }

    [Fact]
    public void 一般長度的行為沒變()
    {
        Assert.Equal(("Artist", "Song"), Titles.Clean("Artist - Topic", "Song (Official Video)", false));
        Assert.Equal(("Artist", "Song"), Titles.Clean("", "Artist - Song 【MV】", true));
        Assert.Equal("Song", Titles.Bare("Song (104期Ver.)"));
        Assert.Equal("A", Titles.MainArtist("A feat. B"));
        Assert.Equal("歌名", PageTitle.Clean("歌名 - YouTube"));
    }
}

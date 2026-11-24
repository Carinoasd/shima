// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：歌名被截在 emoji 中間（孤立的代理字元）。</summary>
public class FuzzSurrogateTests
{
    [Fact]
    public void Norm_LoneSurrogate_DoesNotThrow()
    {
        Assert.Equal("", Titles.Norm("\uD83D"));
        Assert.Equal("ab", Titles.Norm("a\uDE00b"));
        Assert.Equal("a歌", Titles.Norm("a\uD83D歌"));
        Assert.Equal("a😀".Length, Titles.Wellformed("a😀").Length);   // 成對的不動
        Assert.Equal("a�b", Titles.Wellformed("a\uD83Db"));
    }

    [Fact]
    public void VersionTag_LoneSurrogateInBrackets_DoesNotThrow()
    {
        Assert.Equal("", Titles.VersionTag("Song (\uD83D)"));
        Assert.Equal("104", Titles.VersionTag("Song (104\uD83D Ver.)"));
    }

    [Fact]
    public void FileName_LoneSurrogate_BecomesUnderscore()
    {
        var name = LocalProvider.FileName(new SongQuery("Song\uD83D", "", "", 0));
        Assert.Equal("Song_.lrc", name);
        // 太長被切在 emoji 的兩個字元中間
        var cut = LocalProvider.FileName(new SongQuery(new string('a', 149) + "😀", "", "", 0));
        Assert.NotNull(cut);
        Assert.DoesNotContain(cut!, c => char.IsSurrogate(c));
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：歌詞檔裡的數字大到 int／TimeSpan 放不下，整份歌詞讀不進來。</summary>
public class FuzzOverflowTests
{
    [Fact]
    public void Parse_HugeTimestamps_AreNotTimestamps()
    {
        var lines = Lrc.Parse("[99999999999:00]a\n[00:99999999999999999999]b\n[offset:99999999999999999999]\n[00:12.99999999999999999999]c\n[00:01.00]ok");
        Assert.Equal(["c", "ok"], lines.Select(l => l.Text).OrderBy(t => t));
        Assert.All(lines, l => Assert.True(l.Time >= TimeSpan.Zero));
    }

    [Fact]
    public void Parse_NonAsciiDigits_AreNotTimestamps()
    {
        Assert.Empty(Lrc.Parse("[٣:٠٠]a\n[０１:０２]b"));
        Assert.Equal("<٣:٠٠>c", Assert.Single(Lrc.Parse("[00:01.00]<٣:٠٠>c")).Text);
    }

    [Fact]
    public void ParseYrc_HugeNumbers_AreSkipped()
    {
        Assert.Empty(WordTiming.ParseYrc("[99999999999999999999,5](0,1,0)a"));
        var lines = WordTiming.ParseYrc("[1000,500](99999999999999999999,2,0)x(1000,200,0)ok\n[5,5](٣,٤,٠)y");
        Assert.Single(lines);
        Assert.Equal("ok", lines[0].Text);
    }

    [Fact]
    public void ParseEnhanced_HugeStamp_IsPlainText()
    {
        var (text, words) = WordTiming.ParseEnhanced("<99999999999:00>字", TimeSpan.Zero);
        Assert.Null(words);
        Assert.Equal("<99999999999:00>字", text);
    }
}

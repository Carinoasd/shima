// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>「複製全文」：一行一句，有翻譯的那句下面一行翻譯。</summary>
public class LyricsCopyTests
{
    private static LyricLine Line(double seconds, string text) => new(TimeSpan.FromSeconds(seconds), text);

    [Fact]
    public void 一行一句_間奏的空行略過()
    {
        var sheet = new LyricSheet([Line(1, "a"), Line(5, ""), Line(9, "b")], true);
        Assert.Equal("a\nb", Lrc.PlainText(sheet));
    }

    [Fact]
    public void 有翻譯_每句下面一行翻譯_沒翻的那句不留空行()
    {
        var sheet = new LyricSheet([Line(1, "朝"), Line(5, "ラララ"), Line(7, ""), Line(9, "夜")], true) { Translation = ["早晨", "", "", "夜晚"] };
        Assert.Equal("朝\n早晨\nラララ\n夜\n夜晚", Lrc.PlainText(sheet));
    }

    [Fact]
    public void 只有羅馬拼音_不放進去()
    {
        var sheet = new LyricSheet([Line(1, "朝")], true) { Romaji = ["asa"] };
        Assert.Equal("朝", Lrc.PlainText(sheet));
    }

    [Fact]
    public void 純文字歌詞也可以()
    {
        Assert.Equal("a\nb", Lrc.PlainText(new LyricSheet(Lrc.Plain("a\n\nb\n"), false)));
    }
}

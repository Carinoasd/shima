// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：提醒文字被截斷時，切在 emoji 的兩個字元中間，留下半個代理字元。</summary>
public class IdleLineEmojiTests
{
    [Fact]
    public void Trim_DoesNotSplitAnEmoji()
    {
        // 前面剛好 19 格，emoji 的高位在第 20 格、低位超出
        var text = new string('a', 19) + "😀bc";
        var trimmed = IdleLine.Trim(text, 20);
        Assert.Equal(new string('a', 19) + "…", trimmed);
        Assert.DoesNotContain(trimmed, char.IsSurrogate);
    }

    [Fact]
    public void Trim_KeepsAWholeEmojiThatFits()
    {
        Assert.Equal("a😀…", IdleLine.Trim("a😀bcdef", 3));
        Assert.Equal("😀", IdleLine.Trim("😀", 2));
        Assert.Equal("…", IdleLine.Trim("😀bc", 1));
    }
}

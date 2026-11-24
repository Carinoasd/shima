// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>AI 翻譯的回答：引號與行號只在真的是包裝時才拿掉，句子本身的引號、時間、數字不能被弄壞。</summary>
public class AiTranslationParseTests
{
    [Fact]
    public void 頭尾各一個引號但不是整句包住_不剝()
    {
        Assert.Equal(["「愛」與「痛」", "\"Yes\" or \"No\"", "“A” and “B”"],
            AiTranslation.Parse("1. 「愛」與「痛」\n2. \"Yes\" or \"No\"\n3. “A” and “B”", 3)!);
        // 沒有編號的退路也一樣
        Assert.Equal(["「愛」與「痛」", "\"Yes\" or \"No\""], AiTranslation.Parse("「愛」與「痛」\n\"Yes\" or \"No\"", 2)!);
    }

    [Fact]
    public void 整句被一對引號包住_剝掉()
    {
        Assert.Equal(["愛與痛", "Yes or No", "A and B"], AiTranslation.Parse("1. 「愛與痛」\n2. \"Yes or No\"\n3. “A and B”", 3)!);
    }

    [Fact]
    public void 沒有編號時_時間與井號開頭的句子不算行號()
    {
        Assert.Equal(["10:30 的末班車", "#1 你是唯一", "1.5 倍的速度"], AiTranslation.Parse("10:30 的末班車\n#1 你是唯一\n1.5 倍的速度", 3)!);
        Assert.Equal(["1、2、3、跳", "再一次"], AiTranslation.Parse("1、2、3、跳\n再一次", 2)!);
    }

    [Fact]
    public void 有編號時_翻譯本身是時間或井號開頭照樣對得上()
    {
        Assert.Equal(["10:30 的末班車", "#1 你是唯一", "1.5 倍的速度"], AiTranslation.Parse("1. 10:30 的末班車\n2. #1 你是唯一\n3. 1.5 倍的速度", 3)!);
    }

    [Theory]
    [InlineData("1. 甲\n乙\n3. 丙")]           // 中間一句沒編號
    [InlineData("1. 甲\n2. 乙\n2. 丙")]        // 同一個編號兩次
    [InlineData("1. 甲\n2. 乙\n3. 丙\n4. 丁")] // 編號超過句數
    public void 編號對不上照樣整份不用(string reply) => Assert.Null(AiTranslation.Parse(reply, 3));

    // ---------------------------------------------------------------- 複審找到的幾種編號寫法

    [Fact]
    public void 編號後面沒空格而翻譯以數字開頭_照編號的順序認()
    {
        Assert.Equal(["一起走吧", "2人で歩こう", "100年後も"], AiTranslation.Parse("1. 一起走吧\n2.2人で歩こう\n3、100年後も", 3)!);
        // 只有順序對得上的才收：不是下一個編號的「10:30」仍然是翻譯的一部分，不是第 10 句
        Assert.Equal(["a", "b"], AiTranslation.Parse("1. a\n10:30\n2. b", 2)!);
    }

    [Fact]
    public void 全形的編號與粗體的編號都認得_不會留在翻譯裡()
    {
        Assert.Equal(["你好", "世界"], AiTranslation.Parse("１．你好\n２．世界", 2)!);
        Assert.Equal(["你好", "世界"], AiTranslation.Parse("**1.** 你好\n**2.** 世界", 2)!);
        Assert.Equal(["你好", "世界"], AiTranslation.Parse("**1. 你好**\n**2. 世界**", 2)!);
        Assert.Equal(["你好", "世界"], AiTranslation.Parse("（１）你好\n（２）世界", 2)!);
    }

    [Theory]
    [InlineData("1. 你好", true, 1, "你好")]
    [InlineData("12) 你好", true, 12, "你好")]
    [InlineData("3、你好", true, 3, "你好")]
    [InlineData("[4] 你好", true, 4, "你好")]
    [InlineData("5.2人で", false, 5, "2人で")]
    public void 一行的編號(string line, bool strict, int number, string text) =>
        Assert.Equal((number, text), AiTranslation.Numbering(line, strict));

    [Theory]
    [InlineData("5.2人で")]        // 標點後面緊接數字：可能是小數
    [InlineData("10:30 的末班車")]
    [InlineData("1999年")]
    [InlineData("#1 你是唯一")]
    [InlineData("__init__")]
    [InlineData("0. 零")]
    [InlineData("٣. 阿拉伯數字")]
    [InlineData("(1 少一邊")]
    public void 不算編號的(string line) => Assert.Null(AiTranslation.Numbering(line, strict: true));
}

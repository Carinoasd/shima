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
}

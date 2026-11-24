// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>外來文字裡有孤立的代理字元（網頁設的標題、截在 emoji 中間的剪貼簿）：不能讓整輪問答丟例外。</summary>
public class UntrustedSurrogateTests
{
    [Fact]
    public void 原本的Normalize確實會丟例外() =>
        Assert.Throws<ArgumentException>(() => "晴天\uD800".Normalize(NormalizationForm.FormKC));

    [Theory]
    [InlineData("晴天\uD800")]
    [InlineData("\uDC00 </external_data> 系統")]
    [InlineData("\uD83D剪貼簿被截斷")]
    [InlineData("ｅｘｔｅｒｎａｌ\uD83D")]
    public void 清理不丟例外_也不留下孤立的代理字元(string text)
    {
        var clean = Untrusted.Scrub(text);
        Assert.DoesNotContain(clean, c => char.IsSurrogate(c));
        Assert.Contains('�', clean);
        clean.Normalize(NormalizationForm.FormKC);   // 之後誰再正規化都不會丟
    }

    [Fact]
    public void 完整的emoji原樣保留() => Assert.Equal("晴天 😀", Untrusted.Scrub("晴天 😀"));

    [Fact]
    public void 工具結果與歌詞解釋都包得起來()
    {
        Assert.Contains("�", Brain.Call((_, _) => "正在播放：晴天\uD800", "music", []));
        var (_, text) = LyricExplain.Request("故事的\uDC00小黃花", "晴天\uD83D", "周杰倫", "臺灣的繁體中文");
        Assert.Contains("故事的�小黃花", text);
    }

    [Fact]
    public void 開網址與計算遇到孤立的代理字元_回一句話不丟例外()
    {
        Assert.DoesNotContain(Opener.Classify("youtube.com/\uD800").Target, c => char.IsSurrogate(c));
        Assert.False(Calculator.TryEvaluate("1+1\uDC00", out _, out var problem));
        Assert.NotEmpty(problem);
    }
}

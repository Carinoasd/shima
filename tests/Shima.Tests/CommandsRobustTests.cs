// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>快速指令的解析不丟例外：孤立的代理字元、其他文字的數字。</summary>
public class CommandsRobustTests
{
    [Theory]
    [InlineData("/vol\uD800")]
    [InlineData("/\uDC00")]
    [InlineData("/vol \uD83D")]
    [InlineData("/sleep 30\uD800")]
    [InlineData("/todo 買\uD83D")]
    [InlineData("/vol ٥٠")]          // 阿拉伯-印度數字
    [InlineData("/sleep ३०")]         // 天城文數字
    [InlineData("/focus ٢٥")]
    [InlineData("/vol 𝟓𝟎")]          // 數學粗體數字（NFKC 之後是 50）
    public void 解析不丟例外(string line)
    {
        var c = Commands.Parse(line);
        Assert.NotEqual(CommandState.NotCommand, c.State);
        Commands.Suggest(line);
    }

    [Theory]
    [InlineData("/vol ٥٠")]
    [InlineData("/sleep ३०")]
    [InlineData("/focus ٢٥")]
    public void 其他文字的數字當作用法不對(string line) => Assert.Equal(CommandState.BadArgs, Commands.Parse(line).State);

    [Theory]
    [InlineData("/vol ５０", 50)]
    [InlineData("/vol 50", 50)]
    [InlineData("/vol 𝟓𝟎", 50)]
    public void 全形與相容數字照常認得(string line, int level)
    {
        var c = Commands.Parse(line);
        Assert.Equal(CommandState.Ok, c.State);
        Assert.Equal(level, c.Number);
    }

    [Fact]
    public void 提示清單遇到孤立的代理字元也不丟例外()
    {
        Assert.Empty(Commands.Suggest("/\uD800"));
        Assert.Empty(Commands.Suggest("/v\uDC00"));
        Assert.NotEmpty(Commands.Suggest("/v"));
    }
}

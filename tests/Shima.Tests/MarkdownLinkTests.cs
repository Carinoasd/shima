// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class MarkdownLinkTests
{
    private static string Plain(string s) => string.Concat(Markdown.Inline(s).Select(x => x.Text));

    [Fact]
    public void 網址裡成對的括號不會截斷()
    {
        var spans = Markdown.Inline("[C](https://en.wikipedia.org/wiki/C_(programming_language)#Syntax)");
        Assert.Equal("C (https://en.wikipedia.org/wiki/C_(programming_language)#Syntax)", string.Concat(spans.Select(x => x.Text)));
        Assert.Equal(2, spans.Count);
        Assert.Equal("C", spans[0].Text);
        Assert.Equal(MdStyle.None, spans[1].Style);
        Assert.True(spans[0].Style.HasFlag(MdStyle.Link));
    }

    [Fact]
    public void 網址後面的字不受括號影響()
    {
        Assert.Equal("看 C (https://x.y/a_(b)) 結束", Plain("看 [C](https://x.y/a_(b)) 結束"));
    }

    [Fact]
    public void 粗體裡的連結網址含星號底線_不被拆壞()
    {
        var spans = Markdown.Inline("**看 [這裡](https://a.example/x_y**z) 的說明**");
        Assert.Equal("看 這裡 (https://a.example/x_y**z) 的說明", string.Concat(spans.Select(x => x.Text)));
        Assert.All(spans, x => Assert.True(x.Style.HasFlag(MdStyle.Bold)));
    }

    [Fact]
    public void 斜體裡的連結網址含底線()
    {
        var spans = Markdown.Inline("*看 [這裡](https://a.example/x*) 吧*");
        Assert.Equal("看 這裡 (https://a.example/x*) 吧", string.Concat(spans.Select(x => x.Text)));
        Assert.All(spans, x => Assert.True(x.Style.HasFlag(MdStyle.Italic)));
    }

    [Fact]
    public void 相鄰兩個連結_網址已在文字裡的不重複附上()
    {
        Assert.Equal("https://a.examplehttps://b.example",
            Plain("[https://a.example](https://a.example)[https://b.example](https://b.example)"));
    }

    [Fact]
    public void 相鄰兩個連結_各附各的網址()
    {
        Assert.Equal("A (https://a.example)B (https://b.example)", Plain("[A](https://a.example)[B](https://b.example)"));
    }

    [Theory]
    [InlineData("[x](https://a.com/?q=(1)", "x (https://a.com/?q=(1)")]
    [InlineData("[x](https://a.com/?q=(1) 後面還有字", "x (https://a.com/?q=(1) 後面還有字")]
    [InlineData("[a](http://x(y)", "a (http://x(y)")]
    public void 括號少一個時退回第一個右括號當結尾(string input, string expected)
    {
        var spans = Markdown.Inline(input);
        Assert.Equal(expected, string.Concat(spans.Select(x => x.Text)));
        Assert.True(spans[0].Style.HasFlag(MdStyle.Link));
    }

    [Theory]
    [InlineData("[a](https://a.com/(b")]
    [InlineData("[a](https://a.com/C_(programming")]
    [InlineData("[a](https://a.com/x")]
    [InlineData("[a](")]
    public void 串流到一半網址還沒寫完不是連結(string input)
    {
        Assert.Equal(input, Plain(input));
        Assert.DoesNotContain(Markdown.Inline(input), x => x.Style.HasFlag(MdStyle.Link));
    }

    [Fact]
    public void 串流寫完整之後才變成連結()
    {
        const string full = "[C](https://x.y/C_(programming_language))";
        for (int n = 1; n < full.Length; n++)
            if (n < full.IndexOf("(programming") + 12) Assert.DoesNotContain(Markdown.Inline(full[..n]), x => x.Style.HasFlag(MdStyle.Link));
        Assert.Contains(Markdown.Inline(full), x => x.Style.HasFlag(MdStyle.Link));
    }
}

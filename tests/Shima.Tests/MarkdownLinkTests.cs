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
        Assert.Equal("C", spans[0].Text);
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
        var spans = Markdown.Inline("**看 [這裡](https://a.example/x_y*z) 的說明**");
        Assert.Equal("看 這裡 (https://a.example/x_y*z) 的說明", string.Concat(spans.Select(x => x.Text)));
        Assert.All(spans, x => Assert.True(x.Style.HasFlag(MdStyle.Bold)));
    }

    [Fact]
    public void 斜體裡的連結網址含底線()
    {
        var spans = Markdown.Inline("*看 [這裡](https://a.example/x*y) 吧*");
        Assert.Equal("看 這裡 (https://a.example/x*y) 吧", string.Concat(spans.Select(x => x.Text)));
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

    [Fact]
    public void 括號沒收尾就是普通文字()
    {
        Assert.Equal("[a](http://x(y)", Plain("[a](http://x(y)"));
    }
}

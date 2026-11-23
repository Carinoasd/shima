// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>回答的 Markdown：區塊、行內樣式、串流途中的半成品。</summary>
public class MarkdownTests
{
    private static MdBlock One(string text) => Assert.Single(Markdown.Parse(text));

    private static string Styled(IReadOnlyList<MdSpan> spans) => string.Concat(spans.Select(s =>
        s.Style == MdStyle.None ? s.Text : $"<{(s.Style.HasFlag(MdStyle.Bold) ? "b" : "")}{(s.Style.HasFlag(MdStyle.Italic) ? "i" : "")}{(s.Style.HasFlag(MdStyle.Code) ? "c" : "")}{(s.Style.HasFlag(MdStyle.Link) ? "a" : "")}>{s.Text}</>"));

    private static string Inline(string text) => Styled(Markdown.Inline(text));

    // ---------------------------------------------------------------- 區塊

    [Fact]
    public void EmptyAndBlank()
    {
        Assert.Empty(Markdown.Parse(""));
        Assert.Empty(Markdown.Parse(null!));
        Assert.Empty(Markdown.Parse("\n\n  \n"));
        Assert.Empty(Markdown.Inline(""));
    }

    [Fact]
    public void ParagraphsKeepLineBreaks()
    {
        var blocks = Markdown.Parse("第一行\n第二行\n\n第二段");
        Assert.Equal(2, blocks.Count);
        Assert.Equal("第一行\n第二行", blocks[0].Source);
        Assert.Equal("第二段", blocks[1].Source);
        Assert.All(blocks, b => Assert.Equal(MdKind.Paragraph, b.Kind));
    }

    [Fact]
    public void WindowsLineEndings()
    {
        var blocks = Markdown.Parse("a\r\nb\r\n\r\nc");
        Assert.Equal(["a\nb", "c"], blocks.Select(b => b.Source));
    }

    [Theory]
    [InlineData("# 標題", 1)]
    [InlineData("## 標題", 2)]
    [InlineData("### 標題", 3)]
    [InlineData("#### 標題", 3)]
    public void Headings(string text, int level)
    {
        var block = One(text);
        Assert.Equal(MdKind.Heading, block.Kind);
        Assert.Equal(level, block.Level);
        Assert.Equal("標題", block.Plain);
    }

    [Theory]
    [InlineData("#標籤")]
    [InlineData("####### 太多")]
    [InlineData("3.14 是圓周率")]
    [InlineData("-5 度")]
    [InlineData("**粗體**開頭")]
    [InlineData("*斜體*開頭")]
    public void NotBlockMarkers(string text) => Assert.Equal(MdKind.Paragraph, One(text).Kind);

    [Fact]
    public void Bullets()
    {
        var blocks = Markdown.Parse("清單：\n- 一\n* 二\n+ 三");
        Assert.Equal([MdKind.Paragraph, MdKind.Bullet, MdKind.Bullet, MdKind.Bullet], blocks.Select(b => b.Kind));
        Assert.Equal(["清單：", "一", "二", "三"], blocks.Select(b => b.Plain));
        Assert.All(blocks.Skip(1), b => Assert.Equal(0, b.Depth));
    }

    [Fact]
    public void NumberedKeepsItsNumber()
    {
        var blocks = Markdown.Parse("1. 甲\n2. 乙\n10) 丙");
        Assert.All(blocks, b => Assert.Equal(MdKind.Numbered, b.Kind));
        Assert.Equal(["1.", "2.", "10)"], blocks.Select(b => b.Marker));
        Assert.Equal(["甲", "乙", "丙"], blocks.Select(b => b.Plain));
    }

    [Fact]
    public void NestedLists()
    {
        var blocks = Markdown.Parse("- a\n  - b\n    - c\n  - d\n- e");
        Assert.Equal([0, 1, 2, 1, 0], blocks.Select(b => b.Depth));
        // 編號清單底下用三格縮排的也一樣
        blocks = Markdown.Parse("1. a\n   - b\n2. c");
        Assert.Equal([0, 1, 0], blocks.Select(b => b.Depth));
    }

    [Fact]
    public void ListItemContinuation()
    {
        var blocks = Markdown.Parse("- 第一項\n  接著寫\n- 第二項\n\n  第二項的段落\n\n結尾");
        Assert.Equal(["第一項\n接著寫", "第二項", "第二項的段落", "結尾"], blocks.Select(b => b.Source));
        Assert.Equal(MdKind.Paragraph, blocks[2].Kind);
        Assert.Equal(1, blocks[2].Depth);
        Assert.Equal(0, blocks[3].Depth);
    }

    [Fact]
    public void FencedCode()
    {
        var blocks = Markdown.Parse("看這段：\n```csharp\nvar x = **1**;\n  縮排 *不* 變\n```\n完");
        Assert.Equal([MdKind.Paragraph, MdKind.Code, MdKind.Paragraph], blocks.Select(b => b.Kind));
        Assert.Equal("var x = **1**;\n  縮排 *不* 變", blocks[1].Source);
        Assert.Equal(MdStyle.Code, Assert.Single(blocks[1].Spans).Style);
    }

    [Fact]
    public void UnclosedFenceRunsToTheEnd()
    {
        var blocks = Markdown.Parse("```\n**不是粗體**\n# 不是標題");
        var code = Assert.Single(blocks);
        Assert.Equal(MdKind.Code, code.Kind);
        Assert.Equal("**不是粗體**\n# 不是標題", code.Source);
        // 只收到開頭那排反引號：空的程式碼區塊
        Assert.Equal("", One("```py").Source);
    }

    [Fact]
    public void FenceClosesOnlyWithEnoughBackticks()
    {
        var code = One("````\n```\n內容\n````");
        Assert.Equal("```\n內容", code.Source);
    }

    [Fact]
    public void Quote()
    {
        var blocks = Markdown.Parse("> 引用一\n>引用二\n\n後面");
        Assert.Equal(MdKind.Quote, blocks[0].Kind);
        Assert.Equal("引用一\n引用二", blocks[0].Source);
        Assert.Equal(MdKind.Paragraph, blocks[1].Kind);
    }

    [Fact]
    public void UnsupportedSyntaxStaysAsIs()
    {
        const string table = "| 歌名 | 歌手 |\n|---|---|\n| 僕らは今のなかで | μ's |";
        Assert.Equal(table, One(table).Plain);
        Assert.Equal("![封面](cover.png)", One("![封面](cover.png)").Plain);
        Assert.Equal("<b>粗</b> <br>", One("<b>粗</b> <br>").Plain);
        Assert.Equal("---", One("---").Plain);
    }

    // ---------------------------------------------------------------- 行內

    [Theory]
    [InlineData("**粗體**", "<b>粗體</>")]
    [InlineData("這是**粗體**的字", "這是<b>粗體</>的字")]
    [InlineData("**「歌詞」**的意思", "<b>「歌詞」</>的意思")]
    [InlineData("*斜體*", "<i>斜體</>")]
    [InlineData("一段_斜體_嗎", "一段_斜體_嗎")]
    [InlineData("這是 _斜體_ 喔", "這是 <i>斜體</> 喔")]
    [InlineData("中文*斜體*中文", "中文<i>斜體</>中文")]
    [InlineData("***都有***", "<bi>都有</>")]
    [InlineData("`code`", "<c>code</>")]
    [InlineData("用 ``a`b`` 吧", "用 <c>a`b</> 吧")]
    [InlineData("看 [官網](https://lovelive-anime.jp) 吧", "看 <a>官網</> 吧")]
    [InlineData("[**粗的連結**](https://x.y)", "<ba>粗的連結</>")]
    public void InlineStyles(string text, string expected) => Assert.Equal(expected, Inline(text));

    [Theory]
    [InlineData("snake_case_name")]
    [InlineData("my_file_name.txt")]
    [InlineData("__init__.py")]
    [InlineData("2*3*4 = 24")]
    [InlineData("2**10 是 1024")]
    [InlineData("foo*bar*baz")]
    [InlineData("*.txt 和 *.md")]
    [InlineData("a * b * c")]
    [InlineData("** 不是 **")]
    [InlineData("C:\\Users\\name\\Music")]
    [InlineData("[1] 註腳")]
    [InlineData("[沒網址]")]
    [InlineData("[有空白](a b)")]
    public void ConservativeLiterals(string text) => Assert.Equal(text, Inline(text));

    [Fact]
    public void CodeInsideBold()
    {
        var spans = Markdown.Inline("**用 `git status` 看**");
        Assert.Equal("<b>用 </><bc>git status</><b> 看</>", Styled(spans));
    }

    [Fact]
    public void ItalicInsideBoldAndBoldInsideItalic()
    {
        Assert.Equal("<b>a </><bi>b</><b> c</>", Inline("**a *b* c**"));
        Assert.Equal("<i>a </><bi>b</><i> c</>", Inline("*a **b** c*"));
    }

    [Fact]
    public void MarkersInsideCodeAreLiteral()
    {
        Assert.Equal("<c>**x** _y_ [a](b)</>", Inline("`**x** _y_ [a](b)`"));
    }

    [Fact]
    public void Escapes()
    {
        Assert.Equal("*不是斜體*", Inline("\\*不是斜體\\*"));
        Assert.Equal("1. 不是清單", One("1\\. 不是清單").Plain);
    }

    [Theory]
    [InlineData("**粗")]
    [InlineData("**粗*")]
    [InlineData("前面 **粗體 還沒收")]
    [InlineData("`沒收尾的程式碼")]
    [InlineData("*斜")]
    [InlineData("[連結](https://exa")]
    [InlineData("[連結]")]
    public void UnclosedMarkersAreText(string text) => Assert.Equal(text, Inline(text));

    [Fact]
    public void UnclosedBoldDoesNotSwallowTheRest()
    {
        Assert.Equal("<b>好</>，**後面", Inline("**好**，**後面"));
    }

    [Fact]
    public void Japanese()
    {
        var blocks = Markdown.Parse("## 歌詞の意味\n\n「**僕らは今のなかで**」は、*今*を大切にする歌です。\n\n- `μ's` の曲\n- 作詞：畑亜貴");
        Assert.Equal([MdKind.Heading, MdKind.Paragraph, MdKind.Bullet, MdKind.Bullet], blocks.Select(b => b.Kind));
        Assert.Equal("「<b>僕らは今のなかで</>」は、<i>今</>を大切にする歌です。", Styled(blocks[1].Spans));
        Assert.Equal("<c>μ's</> の曲", Styled(blocks[2].Spans));
    }

    // ---------------------------------------------------------------- 串流

    private const string Long = """
        ## 這句歌詞的意思

        「**輝こう**」是*一起閃耀*的意思，出自 `Snow halation`。

        1. 第一點：**用 `git` 看**
        2. 第二點
           - 巢狀 snake_case_name
        - 2*3*4 不是斜體

        > 引用一句
        > 第二行

        ```python
        print("**不是粗體**")
        ```

        | 表格 | 原樣 |
        結尾 [官網](https://lovelive-anime.jp) 😀 完。
        """;

    [Fact]
    public void EveryPrefixIsSafeAndSettledBlocksNeverChange()
    {
        var final = Markdown.Parse(Long);
        for (int n = 0; n <= Long.Length; n++)
        {
            var prefix = Long[..n];
            var blocks = Markdown.Parse(prefix);
            // 最後一塊還在長；前面的每一塊都已經是最後的樣子
            for (int i = 0; i < blocks.Count - 1; i++)
                Assert.True(blocks[i].Equals(final[i]), $"第 {n} 個字時第 {i} 塊是「{blocks[i]}」，最後是「{final[i]}」");
            // 看得到的字不會比原文多（標記沒有被重複或憑空產生）
            Assert.True(blocks.Sum(b => b.Plain.Length) <= prefix.Length);
        }
    }

    [Fact]
    public void SplitSurrogateDoesNotThrow()
    {
        const string text = "表情 😀 **粗**";
        int split = text.IndexOf("😀", StringComparison.Ordinal) + 1;   // 停在半個表情符號上
        var block = One(text[..split]);
        Assert.Equal(text[..split], block.Plain);
    }

    [Fact]
    public void FeedingByCharacterOnlyStylesWhenClosed()
    {
        const string text = "這是**粗體**與`程式碼`";
        var seen = new List<string>();
        for (int n = 1; n <= text.Length; n++) seen.Add(Inline(text[..n]));
        // 收尾之前一律是原樣的字
        Assert.Equal("這是**粗體*", seen[text.IndexOf("體*", StringComparison.Ordinal) + 1]);
        Assert.DoesNotContain(seen, s => s.Contains("<i>"));
        Assert.Equal("這是<b>粗體</>與`程式", seen[text.LastIndexOf('式')]);
        Assert.Equal("這是<b>粗體</>與<c>程式碼</>", seen[^1]);
    }

    [Fact]
    public void OpeningFenceDoesNotRestyleEarlierText()
    {
        // 程式碼區塊開了還沒關：後面的星號、井號都不會變成樣式
        var steps = new[] { "說明\n```", "說明\n```\n**x", "說明\n```\n**x**\n# y" };
        foreach (var step in steps)
        {
            var blocks = Markdown.Parse(step);
            Assert.Equal("說明", blocks[0].Plain);
            Assert.Equal(MdKind.Code, blocks[^1].Kind);
            Assert.All(blocks[^1].Spans, s => Assert.Equal(MdStyle.Code, s.Style));
        }
    }

    [Fact]
    public void BlockEqualityIgnoresSpanListIdentity()
    {
        Assert.Equal(Markdown.Parse("- **a**")[0], Markdown.Parse("- **a**")[0]);
        Assert.NotEqual(Markdown.Parse("- a")[0], Markdown.Parse("1. a")[0]);
    }
}

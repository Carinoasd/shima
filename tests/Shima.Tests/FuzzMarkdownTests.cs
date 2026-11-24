// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>AI 回答的 Markdown：隨機輸入、病態輸入的耗時、串流途中每一步的半成品。</summary>
public class FuzzMarkdownTests
{
    private static readonly string[] Parts =
    [
        "# ", "## ", "### ", "####### ", "#", "- ", "* ", "+ ", "-", "1. ", "12) ", "1.", "> ", ">", ">>", "```", "```csharp\n", "````", "~~~", "`", "``", "` `",
        "**", "*", "***", "_", "__", "___", "[", "]", "](", ")", "](http://x.y)", "[a](http://a.b)", "[a](b c)", "[](x)", "![", "![img](u)", "\\", "\\*", "\\`", "!", "|", "| a | b |", "---", "~~", "<b>", "&amp;", "<",
        "    ", "\t", "  ", "text", "snake_case", "2*3*4", "*.txt", "中文", "日本語", "😀", "\n", "\n", "\n", "\r\n", "\n\n", "\n  ", "\n    ", "\n- ", "\n  - ", "\n    - ", "\n1. ", "\n> ", "\n```\n",
    ];

    private static string Input(Random r)
    {
        var text = r.Build(Parts.Concat(Gen.Noise).ToArray(), 0, 60, 0.2);
        return r.Chance(0.5) ? r.Mangle(text) : text;
    }

    private static void CheckBlocks(IReadOnlyList<MdBlock> blocks)
    {
        foreach (var b in blocks)
        {
            Assert.InRange(b.Level, 0, 3);
            Assert.InRange(b.Depth, 0, 4);
            Assert.All(b.Spans, s => Assert.True(s.Text.Length > 0, "空的片段"));
            _ = b.Plain;
            _ = b.ToString();
            _ = b.GetHashCode();
        }
    }

    [Fact]
    public void Markdown_Parse_NeverThrows_And_EarlierBlocksNeverChange()
    {
        Fuzz.Run("Markdown.Parse／Inline", 2001, 3000, r => (Input(r), r.Next()), v =>
        {
            var (text, cuts) = v;
            var blocks = Markdown.Parse(text);
            CheckBlocks(blocks);
            Markdown.Inline(text);

            // 串流：前面已經結束的區塊，後面再來多少字都不會變（只有最後一塊還在長）。
            // 切在一行的中間不算：那一行還沒寫完，「```abc」是程式碼區塊的開頭，寫到「```abc`」就變成普通的一行（見 Fence 測試）
            var random = new Random(cuts);
            for (int n = 0; n < 4 && text.Length > 0; n++)
            {
                int cut = text.IndexOf('\n', random.Next(text.Length)) + 1;
                if (cut == 0) cut = text.Length;
                var head = Markdown.Parse(text[..cut]);
                for (int i = 0; i < head.Count - 1; i++)
                    Assert.True(i < blocks.Count && head[i].Equals(blocks[i]), $"切在 {cut} 時第 {i} 塊跟完整解析的不一樣：{head[i]}  vs  {(i < blocks.Count ? blocks[i].ToString() : "(沒有)")}");
            }
        }, v => Fuzz.Escape(v.Item1), perCallMs: 300);
    }

    [Fact]
    public void Markdown_Streaming_EveryPrefixIsParsedWithoutThrowing()
    {
        Fuzz.Run("Markdown 逐字加長（串流）", 2002, 25, r => r.Build(Parts.Concat(Gen.Noise).ToArray(), 5, 60, 0.2), text =>
        {
            for (int n = 0; n <= text.Length; n++)
            {
                if (n > 0 && char.IsHighSurrogate(text[n - 1]) && n < text.Length) continue;
                CheckBlocks(Markdown.Parse(text[..n]));
            }
        }, v => Fuzz.Escape(v), perCallMs: 1500);
    }
}

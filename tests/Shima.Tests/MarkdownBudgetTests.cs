// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：兩萬字以內全是沒收尾的標記時，行內排版要花上好幾秒（每個標記都掃到結尾）。</summary>
public class MarkdownBudgetTests
{
    private static readonly (string Name, Func<int, string> Make)[] Pathological =
    [
        ("星號", n => new string('*', n)),
        ("底線", n => new string('_', n)),
        ("方括號", n => new string('[', n)),
        ("反引號", n => new string('`', n)),
        ("星號加字", n => string.Concat(Enumerable.Repeat("*a ", n / 3))),
        ("底線加字", n => string.Concat(Enumerable.Repeat("_a ", n / 3))),
        ("沒收尾的連結", n => string.Concat(Enumerable.Repeat("[a](", n / 4))),
        ("沒收尾的括號文字", n => string.Concat(Enumerable.Repeat("[a ", n / 3))),
        ("粗斜體交錯", n => string.Concat(Enumerable.Repeat("**a *b _c ", n / 10))),
        ("巢狀方括號", n => new string('[', n / 2) + "a" + new string(']', n / 2) + "(http://x.y)"),
        ("巢狀粗體", n => string.Concat(Enumerable.Repeat("**a ", n / 4)) + string.Concat(Enumerable.Repeat("b** ", n / 4))),
        ("反引號交錯", n => string.Concat(Enumerable.Repeat("`a``", n / 4))),
        ("反斜線", n => new string('\\', n)),
        ("連結文字夾星號", n => string.Concat(Enumerable.Repeat("[*a", n / 3))),
        ("一路縮排的清單", n => string.Concat(Enumerable.Range(0, n / 8).Select(i => new string(' ', i % 400) + "- x\n"))),
        ("很多行很短的段落", n => string.Concat(Enumerable.Repeat("a\n\n", n))),
        ("很多個清單項目", n => string.Concat(Enumerable.Repeat("- *a*\n", n))),
        ("一行很多連結", n => string.Concat(Enumerable.Repeat("[a](http://b.c)", n / 15))),
        ("很多程式碼區塊", n => string.Concat(Enumerable.Repeat("```\nx\n```\n", n / 10))),
        ("沒收尾的程式碼區塊", n => "```\n" + string.Concat(Enumerable.Repeat("`` * [ \n", n / 8))),
    ];

    [Fact]
    public void Markdown_PathologicalInputs_FinishWithinBudget()
    {
        int[] sizes = [1_000, 5_000, 19_999, 20_001, 60_000];
        var cases = Pathological.SelectMany(p => sizes.Select(n => (p.Name, p.Make, n))).ToList();
        int next = 0;
        Fuzz.Run("Markdown 病態輸入（時間）", 2003, cases.Count, _ => cases[next++], c =>
        {
            var text = c.Make(c.n);
            foreach (var b in Markdown.Parse(text)) _ = b.Plain;
            Markdown.Inline(text);
        }, c => $"{c.Name} ×{c.n} 字", perCallMs: 200);
    }
}

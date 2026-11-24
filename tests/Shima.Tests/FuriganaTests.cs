// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

public class FuriganaTests
{
    private const ushort X = 0xFFFF;

    private static string Show(IReadOnlyList<Ruby>? line) =>
        line == null ? "null" : string.Join("／", line.Select(r => r.Reading == null ? r.Text : $"{r.Text}({r.Reading})"));

    // 下面的讀音與對照表都是這台電腦的 MSIME.Japan 實際回的（GetJMorphResult、REV、MONORUBY）

    [Fact]
    public void 送氣假名對齊在各自的漢字上()
    {
        var line = Furigana.Split("走り続ける", "はしりつづける", [0, 2, 3, 5, 6, 7]);
        Assert.Equal("走(はし)／り／続(つづ)／ける", Show(line));
    }

    [Fact]
    public void 漢字與假名交錯_空白照原文()
    {
        Assert.Equal("朝(あさ)／のバスは まだ／眠(ねむ)／そうで",
            Show(Furigana.Split("朝のバスは まだ眠そうで", "あさのばすは　まだねむそうで", [0, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14])));
        Assert.Equal("潮(しお)／の／匂(にお)／いが ／髪(かみ)／にのこる",
            Show(Furigana.Split("潮の匂いが 髪にのこる", "しおのにおいが　かみにのこる", [0, 2, 3, 5, 6, 7, 8, 10, 11, 12, 13, 14])));
    }

    [Fact]
    public void 相鄰的漢字併成一段()
    {
        Assert.Equal("イヤホンの／片方(かたほう)／を きみに／渡(わた)／した",
            Show(Furigana.Split("イヤホンの片方を きみに渡した", "いやほんのかたほうを　きみにわたした", [0, 1, 2, 3, 4, 5, 7, 9, 10, 11, 12, 13, 14, 16, 17, 18])));
        Assert.Equal("言葉(ことば)／はすこし しょっぱくなる",
            Show(Furigana.Split("言葉はすこし しょっぱくなる", "ことばはすこし　しょっぱくなる", [0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15])));
    }

    [Fact]
    public void 熟字訓_沒有自己讀音的字跟前一個字一起()
    {
        var line = Furigana.Split("Hello 世界、今日は!", "Ｈｅｌｌｏ　せかい、きょうは！", [0, 1, 2, 3, 4, 5, 6, 7, 9, 10, X, 13, 14, 15]);
        Assert.Equal("Hello ／世界(せかい)／、／今日(きょう)／は!", Show(line));
        Assert.Equal("Hello せかい、きょうは!", Furigana.Hiragana(line!));   // 英數與標點照原文，不會變成全形
    }

    [Fact]
    public void 片假名與純漢字()
    {
        Assert.Equal("東京(とうきょう)／タワー", Show(Furigana.Split("東京タワー", "とうきょうたわー", [0, 2, 5, 6, 7, 8])));
        Assert.Equal("運命(うんめい)", Show(Furigana.Split("運命", "うんめい", [0, 2, 4])));
    }

    [Fact]
    public void 純假名沒有讀音()
    {
        var line = Furigana.Split("ひかり", "ひかり", [0, 1, 2, 3]);
        Assert.Equal("ひかり", Show(line));
        Assert.All(line!, r => Assert.Null(r.Reading));
    }

    [Fact]
    public void 輸入法把送氣假名併進同一段_頭尾的假名拆出來()
    {
        Assert.Equal("走(はし)／り", Show(Furigana.Split("走り", "はしり", [0, X, 3])));
        Assert.Equal("お／茶(ちゃ)", Show(Furigana.Split("お茶", "おちゃ", [0, X, 3])));
    }

    [Fact]
    public void 讀音不是假名的漢字不標()
    {
        // 輸入法不認得的字會原樣丟回來
        Assert.Equal("の𠮷", Show(Furigana.Split("の𠮷", "の𠮷", [0, 1, X, 3])));   // 代理對也不會出事
        Assert.Equal("龘が", Show(Furigana.Split("龘が", "龘が", [0, 1, 2])));
    }

    [Fact]
    public void 對照表怪怪的_整行不標而不是丟例外()
    {
        Assert.Null(Furigana.Split("走り続ける", "はしりつづける", null));
        Assert.Null(Furigana.Split("走り続ける", "はしりつづける", [0, 2, 3]));               // 長度不對
        Assert.Null(Furigana.Split("走り続ける", "はし", [0, 2, 3, 5, 6, 7]));                 // 讀音比對照表短
        Assert.Null(Furigana.Split("走り続ける", "はしりつづける", [0, 2, 3, 5, 6, 9]));       // 最後一個不是讀音的長度
        Assert.Null(Furigana.Split("走り続ける", "はしりつづける", [0, 4, 3, 5, 6, 7]));       // 倒退
        Assert.Null(Furigana.Split("走り続ける", "はしりつづける", [X, 2, 3, 5, 6, 7]));       // 第一個字沒有位置
        Assert.Null(Furigana.Split("走り続ける", "はしりつづける", [0, 1, 3, 5, 6, 7]));       // 錯位：「り」讀成「し」
        Assert.Null(Furigana.Split("", "", [0]));
        Assert.Equal("走", Show(Furigana.Split("走", "", [0, 0])));                             // 讀音是空的：不標
    }

    [Fact]
    public void 整句讀音_沒讀音的行是空字串()
    {
        var sheet = new RubySheet([Furigana.Split("走り続ける", "はしりつづける", [0, 2, 3, 5, 6, 7]), null, Furigana.Split("ひかり", "ひかり", [0, 1, 2, 3])]);
        Assert.Equal(["はしりつづける", "", ""], sheet.Readings);
    }

    [Fact]
    public async Task 中文歌不標日文讀音()
    {
        static LyricSheet Sheet(params string[] lines) => new(lines.Select((l, i) => new LyricLine(TimeSpan.FromSeconds(i), l)).ToList(), true);
        Assert.False(Furigana.Wanted(Sheet("我們的影子", "跟著搖晃")));
        Assert.False(Furigana.Wanted(Sheet("ひかり", "あしたもきっと")));   // 全是假名：沒東西可標
        Assert.True(Furigana.Wanted(Sheet("朝のバスは", "運命")));
        Assert.Null(await Furigana.ForAsync(Sheet("我們的影子")));
    }

    [Fact]
    public async Task 有輸入法的電腦_實際呼叫元件()
    {
        if (!Furigana.Available) return;   // 沒裝日文輸入法的電腦上這項功能不出現
        var sheet = new LyricSheet([new LyricLine(TimeSpan.Zero, "走り続ける"), new LyricLine(TimeSpan.FromSeconds(1), "ひかり"), new LyricLine(TimeSpan.FromSeconds(2), "")], true);
        var ruby = await Furigana.ForAsync(sheet).WaitAsync(TimeSpan.FromSeconds(20));
        if (!Furigana.Available) return;   // 登錄裡有、但元件建立不起來（語言套件沒裝完整）：一樣當成沒有
        Assert.NotNull(ruby);
        Assert.Equal("走(はし)／り／続(つづ)／ける", Show(ruby!.Lines[0]));
        Assert.Null(ruby.Lines[1]);   // 沒有漢字的行不送去查
        Assert.Same(Furigana.ForAsync(sheet with { Translation = ["x", "y", "z"] }), Furigana.ForAsync(sheet));   // 以內容為鍵快取
    }
}

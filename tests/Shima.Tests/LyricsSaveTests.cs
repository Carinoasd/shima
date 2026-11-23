// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>「存成 .lrc」：寫出去的檔案本機來源要讀得回來，時間、翻譯、逐字都不能掉。</summary>
public class LyricsSaveTests
{
    private static LyricLine Line(double seconds, string text) => new(TimeSpan.FromSeconds(seconds), text);

    private static long Ms(TimeSpan t) => (long)Math.Round(t.TotalMilliseconds);

    [Fact]
    public void 一般的歌詞_一行一個時間()
    {
        var sheet = new LyricSheet([Line(1.5, "第一句"), Line(12.34, "第二句"), Line(75, "")], true);
        var lrc = Lrc.Write(sheet, TimeSpan.Zero, "歌名", "藝人");
        Assert.Equal("[ti:歌名]\n[ar:藝人]\n[00:01.500]第一句\n[00:12.340]第二句\n[01:15.000]\n", lrc);
    }

    [Fact]
    public void 調過的時間算進去_正的是提早_不會變成負的()
    {
        var sheet = new LyricSheet([Line(0.2, "a"), Line(10, "b")], true);
        var lrc = Lrc.Write(sheet, TimeSpan.FromMilliseconds(500), "", "");
        Assert.Equal("[00:00.000]a\n[00:09.500]b\n", lrc);

        var later = Lrc.Write(sheet, TimeSpan.FromMilliseconds(-300), "", "");
        Assert.Equal("[00:00.500]a\n[00:10.300]b\n", later);
    }

    [Fact]
    public void 兩句被擠到同一個時間_後一句挪一毫秒_讀回來不會變成翻譯()
    {
        var sheet = new LyricSheet([Line(0.1, "a"), Line(0.3, "b"), Line(5, "c")], true);
        var back = Lrc.ParseSheet(Lrc.Write(sheet, TimeSpan.FromSeconds(1), "", ""));
        Assert.Equal(["a", "b", "c"], back.Lines.Select(l => l.Text));
        Assert.Null(back.Translation);
    }

    [Fact]
    public void 有翻譯_同一個時間接著寫一行_空的不寫()
    {
        var sheet = new LyricSheet([Line(1, "朝のバス"), Line(5, "ラララ"), Line(9, "")], true) { Translation = ["早晨的公車", "", ""] };
        var lrc = Lrc.Write(sheet, TimeSpan.Zero, "", "");
        Assert.Equal("[shima:translation]\n[00:01.000]朝のバス\n[00:01.000]早晨的公車\n[00:05.000]ラララ\n[00:09.000]\n", lrc);
    }

    [Fact]
    public void 翻譯與羅馬拼音都存_讀回來各歸各的()
    {
        var sheet = new LyricSheet([Line(1, "朝"), Line(2, "夜")], true) { Translation = ["早晨", ""], Romaji = ["asa", "yoru"] };
        var back = Lrc.ParseSheet(Lrc.Write(sheet, TimeSpan.Zero, "", ""));
        Assert.Equal(["朝", "夜"], back.Lines.Select(l => l.Text));
        Assert.Equal(["早晨", ""], back.Translation!);
        Assert.Equal(["asa", "yoru"], back.Romaji!);
    }

    [Fact]
    public void 只有羅馬拼音_不會被當成翻譯()
    {
        var sheet = new LyricSheet([Line(1, "朝"), Line(2, "夜")], true) { Romaji = ["asa", "yoru"] };
        var back = Lrc.ParseSheet(Lrc.Write(sheet, TimeSpan.Zero, "", ""));
        Assert.Null(back.Translation);
        Assert.Equal(["asa", "yoru"], back.Romaji!);
    }

    [Fact]
    public void 逐字時間寫成加強版_停頓與最後一個字的長度都留著()
    {
        var words = new List<LyricWord>
        {
            new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(0.4), "Hello "),
            new(TimeSpan.FromSeconds(10.4), TimeSpan.FromSeconds(0.3), "my "),
            new(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(0.8), "love"),
        };
        var sheet = new LyricSheet([Line(10, "Hello my love") with { Words = words }], true);
        var lrc = Lrc.Write(sheet, TimeSpan.FromMilliseconds(200), "", "");
        Assert.Equal("[00:09.800]<00:09.800>Hello <00:10.200>my <00:10.500><00:10.800>love<00:11.600>\n", lrc);

        var back = Lrc.ParseSheet(lrc).Lines[0];
        Assert.Equal("Hello my love", back.Text);
        Assert.Equal(["Hello ", "my ", "love"], back.Words!.Select(w => w.Text));
        Assert.Equal([9800L, 10200, 10800], back.Words!.Select(w => Ms(w.Start)));
        Assert.Equal([400L, 300, 800], back.Words!.Select(w => Ms(w.Length)));
    }

    [Fact]
    public void 存了再讀回來_內容一致()
    {
        var words = new List<LyricWord>
        {
            new(TimeSpan.FromMilliseconds(6000), TimeSpan.FromMilliseconds(500), "朝の"),
            new(TimeSpan.FromMilliseconds(6500), TimeSpan.FromMilliseconds(700), "バスは"),
        };
        var sheet = new LyricSheet(
        [
            Line(6, "朝のバスは") with { Words = words },
            Line(12.345, "イヤホンの片方を"),
            Line(33, ""),
            Line(38.07, "ひかりは水面で"),
        ], true) { Translation = ["早晨的公車", "把耳機的一邊", "", "光在水面上"] };

        var back = Lrc.ParseSheet(Lrc.Write(sheet, TimeSpan.Zero, "川と海", "某人"));
        Assert.True(back.Synced);
        Assert.Equal(sheet.Lines.Select(l => l.Text), back.Lines.Select(l => l.Text));
        Assert.Equal(sheet.Lines.Select(l => Ms(l.Time)), back.Lines.Select(l => Ms(l.Time)));
        Assert.Equal(sheet.Translation, back.Translation!);
        Assert.Null(back.Romaji);
        Assert.Equal(words.Select(w => (Ms(w.Start), Ms(w.Length), w.Text)), back.Lines[0].Words!.Select(w => (Ms(w.Start), Ms(w.Length), w.Text)));
        Assert.Null(back.Lines[1].Words);

        // 再存一次也一樣（時間不會一點一點漂掉）
        Assert.Equal(Lrc.Write(sheet, TimeSpan.Zero, "川と海", "某人"), Lrc.Write(back, TimeSpan.Zero, "川と海", "某人"));
    }

    [Fact]
    public void 別人做的雙語檔_同一個時間兩行_第二行是翻譯()
    {
        var sheet = Lrc.ParseSheet("[ar:x]\r\n[00:01.00]Hello\r\n[00:01.00]你好\r\n[00:03.00]World\r\n[00:03.00]世界\r\n");
        Assert.Equal(["Hello", "World"], sheet.Lines.Select(l => l.Text));
        Assert.Equal(["你好", "世界"], sheet.Translation!);
    }

    [Fact]
    public void 開頭幾行同一個時間的製作名單_不當成翻譯()
    {
        var sheet = Lrc.ParseSheet("[00:00.00]作詞：某人\n[00:00.00]作曲：某人\n[00:05.00]a\n[00:09.00]b\n[00:12.00]c");
        Assert.Null(sheet.Translation);
        Assert.Equal(5, sheet.Lines.Count);
    }

    [Fact]
    public void 標籤裡的方括號與換行不會弄壞檔案()
    {
        var lrc = Lrc.Write(new LyricSheet([Line(1, "a")], true), TimeSpan.Zero, "Song [Live]\n", "A]B");
        Assert.StartsWith("[ti:Song (Live)]\n[ar:A)B]\n", lrc);
        Assert.Equal(["a"], Lrc.ParseSheet(lrc).Lines.Select(l => l.Text));
    }

    private static SongQuery Q(string title, string artist = "") => new(title, artist, "", 200);

    [Theory]
    [InlineData("Who? / What: \"Why\" <*>|", "Artist", "Artist - Who_ _ What_ _Why_ ____.lrc")]
    [InlineData("ツバサ・ラ・リベルテ", "蓮ノ空 / 某人", "蓮ノ空 - ツバサ・ラ・リベルテ.lrc")]
    [InlineData("歌名.", "", "歌名.lrc")]
    [InlineData("CON", "", "_CON.lrc")]
    [InlineData("..\\..\\evil", "", ".._.._evil.lrc")]
    public void 檔名_不允許的字元換掉_本機來源認得(string title, string artist, string expected)
    {
        var q = Q(title, artist);
        var name = LocalProvider.FileName(q);
        Assert.Equal(expected, name);
        Assert.Contains(LocalProvider.KeysOf(name!), k => LocalProvider.Keys(q).Contains(k));
        Assert.Equal(-1, name!.IndexOfAny(Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToArray()));
    }

    [Fact]
    public void 歌名全是符號_認不出來就不存()
    {
        Assert.Null(LocalProvider.FileName(Q("???")));
    }

    [Fact]
    public void 已經有同名檔案_備份最原本的那一份_之後不再蓋掉()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shima-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = LocalProvider.Save(folder, "a.lrc", "1");
            LocalProvider.Save(folder, "a.lrc", "2");
            LocalProvider.Save(folder, "a.lrc", "3");
            Assert.Equal("3", File.ReadAllText(path));
            // 「1」可能是使用者自己放的檔案：第二次存檔不可以拿「2」把它的備份蓋掉
            Assert.Equal("1", File.ReadAllText(path + ".bak"));
            Assert.Equal(["a.lrc", "a.lrc.bak"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("..\\a.lrc")]
    [InlineData("../a.lrc")]
    [InlineData("sub\\a.lrc")]
    [InlineData("C:\\a.lrc")]
    public void 路徑不可以跑出歌詞資料夾(string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), "shima-save-" + Guid.NewGuid().ToString("N"));
        try { Assert.Throws<ArgumentException>(() => LocalProvider.Save(folder, name, "x")); }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void 別人的檔案_合唱與多行名單不當成雙語()
    {
        // 對唱：一半的時間點有兩位歌手各一行。以前過半就當成雙語，第二行會從歌詞裡消失
        var duet = "[00:01.00]A1\n[00:01.00]B1\n[00:05.00]A2\n[00:09.00]A3\n[00:09.00]B3\n[00:13.00]A4\n";
        var sheet = Lrc.ParseSheet(duet);
        Assert.Null(sheet.Translation);
        Assert.Equal(6, sheet.Lines.Count);

        // 開頭同一個時間好幾行的製作名單：超過三行，整份照原本的讀法
        var credits = "[00:00.00]作詞\n[00:00.00]作曲\n[00:00.00]編曲\n[00:00.00]演唱\n[00:05.00]歌詞一\n[00:05.00]翻譯一\n[00:09.00]歌詞二\n[00:09.00]翻譯二\n";
        Assert.Null(Lrc.ParseSheet(credits).Translation);
    }

    [Fact]
    public void 別人的檔案_每一句都成對才當成雙語()
    {
        var lrc = "[00:01.00]歌詞一\n[00:01.00]翻譯一\n[00:05.00]歌詞二\n[00:05.00]翻譯二\n[00:09.00]歌詞三\n[00:09.00]翻譯三\n[00:13.00]\n";
        var sheet = Lrc.ParseSheet(lrc);
        Assert.NotNull(sheet.Translation);
        Assert.Equal("翻譯二", sheet.Translation![1]);
    }
}

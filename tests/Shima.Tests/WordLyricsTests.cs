// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

public class YrcTests
{
    // 製作名單那幾行是網易雲 v1 介面實際回的樣子；逐字那幾行是照 yrc 的格式手寫的
    private const string Sample =
        "{\"t\":0,\"c\":[{\"tx\":\"作词: \"},{\"tx\":\"Ayase\"}]}\n" +
        "[1430,6200](1430,400,0)沈(1830,300,0)む(2130,500,0)よう(2630,300,0)に\n" +
        "[31481,2500](31481,300,0)「さよなら」(31781,200,0)だけ(31981,400,0)だった";

    [Fact]
    public void 一行一句_字和時間都拆出來()
    {
        var lines = WordTiming.ParseYrc(Sample);
        Assert.Equal(["沈むように", "「さよなら」だけだった"], lines.Select(l => l.Text));
        Assert.Equal(1.43, lines[0].Time.TotalSeconds);
        Assert.Equal(["沈", "む", "よう", "に"], lines[0].Words!.Select(w => w.Text));
        Assert.Equal(1.83, lines[0].Words![1].Start.TotalSeconds);
        Assert.Equal(0.5, lines[0].Words![2].Length.TotalSeconds);
    }

    [Fact]
    public void 製作名單的JSON行略過()
    {
        Assert.DoesNotContain(WordTiming.ParseYrc(Sample), l => l.Text.Contains("Ayase"));
    }

    [Fact]
    public void 字裡的括號不是時間()
    {
        var line = Assert.Single(WordTiming.ParseYrc("[0,2000](0,1000,0)Hey (1000,1000,0)(yeah)"));
        Assert.Equal("Hey (yeah)", line.Text);
        Assert.Equal(["Hey ", "(yeah)"], line.Words!.Select(w => w.Text));
    }

    [Fact]
    public void 掛到LRC上_句子與翻譯照舊()
    {
        var lrc = Lrc.Parse("[00:01.43]沈むように\n[00:20.00]間奏\n[00:31.48]「さよなら」だけだった");
        var lines = WordTiming.Attach(lrc, WordTiming.ParseYrc(Sample));
        Assert.Equal(lrc.Select(l => (l.Time, l.Text)), lines.Select(l => (l.Time, l.Text)));
        Assert.NotNull(lines[0].Words);
        Assert.Null(lines[1].Words);
        Assert.NotNull(lines[2].Words);
        var translation = Lrc.Align(lines, Lrc.Parse("[00:01.43]像是沉溺\n[00:31.48]只有再见"));
        Assert.Equal(["像是沉溺", "", "只有再见"], translation!);
    }

    [Fact]
    public void 字不一樣的句子不掛()
    {
        var lrc = Lrc.Parse("[00:01.43]沈むやうに");
        Assert.Null(WordTiming.Attach(lrc, WordTiming.ParseYrc(Sample))[0].Words);
    }

    [Fact]
    public void 時間差太多的句子不掛()
    {
        var lrc = Lrc.Parse("[00:05.00]沈むように");
        Assert.Null(WordTiming.Attach(lrc, WordTiming.ParseYrc(Sample))[0].Words);
    }

    [Fact]
    public void 空白不同也對得上_空白歸前一個字()
    {
        var lrc = Lrc.Parse("[00:00.00]Hello  world");
        var yrc = WordTiming.ParseYrc("[0,2000](0,1000,0)Hello(1000,1000,0)world");
        var words = WordTiming.Attach(lrc, yrc)[0].Words!;
        Assert.Equal(["Hello  ", "world"], words.Select(w => w.Text));
        Assert.Equal("Hello  world", string.Concat(words.Select(w => w.Text)));
    }
}

public class EnhancedLrcTests
{
    [Fact]
    public void 句中的時間拿掉_變成逐字資料()
    {
        var line = Assert.Single(Lrc.Parse("[00:12.00]<00:12.00>夜<00:12.40>に<00:13.00>駆ける<00:14.00>"));
        Assert.Equal("夜に駆ける", line.Text);
        Assert.Equal(["夜", "に", "駆ける"], line.Words!.Select(w => w.Text));
        Assert.Equal([12, 12.4, 13], line.Words!.Select(w => w.Start.TotalSeconds));
        Assert.Equal(1, line.Words![2].Length.TotalSeconds, 6);
    }

    [Fact]
    public void 沒有結束時間的最後一個字用平均長度()
    {
        var line = Assert.Single(Lrc.Parse("[00:00.00]<00:00.00>a <00:00.50>b <00:01.50>c"));
        Assert.Equal("a b c", line.Text);
        Assert.Equal(0.75, line.Words![2].Length.TotalSeconds, 6);
    }

    [Fact]
    public void 第一個字前面沒有時間就從行首開始()
    {
        var line = Assert.Single(Lrc.Parse("[00:10.00]君の<00:11.00>名は"));
        Assert.Equal(10, line.Words![0].Start.TotalSeconds);
        Assert.Equal(1, line.Words![0].Length.TotalSeconds, 6);
    }

    [Fact]
    public void 重複的副歌_字的時間跟著平移()
    {
        var lines = Lrc.Parse("[00:10.00][01:00.00]<00:10.00>ラ<00:10.50>ラ<00:11.00>");
        Assert.Equal(60.5, lines[1].Words![1].Start.TotalSeconds, 6);
        Assert.Equal(10.5, lines[0].Words![1].Start.TotalSeconds, 6);
    }

    [Fact]
    public void Offset_字也一起提早()
    {
        var line = Assert.Single(Lrc.Parse("[offset:+500]\n[00:10.00]<00:10.00>a<00:11.00>b<00:12.00>"));
        Assert.Equal(9.5, line.Time.TotalSeconds);
        Assert.Equal([9.5, 10.5], line.Words!.Select(w => w.Start.TotalSeconds));
    }

    [Fact]
    public void 一般LRC沒有逐字資料()
    {
        Assert.Null(Assert.Single(Lrc.Parse("[00:01.00]普通の歌詞")).Words);
    }
}

public class WordProgressTests
{
    private static readonly List<LyricWord> Words =
    [
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), "夜"),
        new(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(1), "に"),
        new(TimeSpan.FromSeconds(13), TimeSpan.FromSeconds(2), "駆ける"),
    ];

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void 還沒開始是零_唱完是一()
    {
        Assert.Equal((0, 0.0), WordTiming.At(Words, S(9)));
        Assert.Equal(0, WordTiming.Progress(Words, S(9)));
        Assert.Equal((2, 1.0), WordTiming.At(Words, S(20)));
        Assert.Equal(1, WordTiming.Progress(Words, S(20)));
    }

    [Fact]
    public void 字裡面的比例()
    {
        Assert.Equal((0, 0.5), WordTiming.At(Words, S(10.5)));
        Assert.Equal((2, 0.25), WordTiming.At(Words, S(13.5)));
    }

    [Fact]
    public void 字和字之間停在前一個字的結尾()
    {
        Assert.Equal((1, 1.0), WordTiming.At(Words, S(12.5)));
        Assert.Equal(2.0 / 5, WordTiming.Progress(Words, S(12.5)), 6);
    }

    [Fact]
    public void 進度以字元數近似()
    {
        // 「駆ける」三個字元唱了一半：2 + 1.5 = 3.5 / 5
        Assert.Equal(3.5, WordTiming.CharsAt(Words, S(14)), 6);
        Assert.Equal(0.7, WordTiming.Progress(Words, S(14)), 6);
    }

    [Fact]
    public void 進度只會往前()
    {
        double last = 0;
        for (double t = 9; t < 16; t += 0.05)
        {
            double p = WordTiming.Progress(Words, S(t));
            Assert.True(p >= last);
            last = p;
        }
    }

    [Fact]
    public void 寬度比例跟著字元位置()
    {
        double[] prefixes = [0, 10, 20, 40];
        Assert.Equal(0.5, Singing.WidthAt(2, prefixes), 6);
        Assert.Equal(0.75, Singing.WidthAt(2.5, prefixes), 6);
        Assert.Equal(1, Singing.WidthAt(99, prefixes), 6);
    }

    [Fact]
    public void 序列化成秒數陣列_來回不變()
    {
        var json = WordTiming.ToJson(Words);
        Assert.Equal("[[10,1,\"夜\"],[11,1,\"に\"],[13,2,\"駆ける\"]]", json.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Equal(Words, WordTiming.FromJson((JsonArray)JsonNode.Parse(json.ToJsonString())!));
    }

    [Fact]
    public void 字對不上整句就不算有逐字資料()
    {
        Assert.True(WordTiming.Fits("夜に駆ける", Words));
        Assert.False(WordTiming.Fits("夜に駆けるよ", Words));
        Assert.False(WordTiming.Fits("夜に駆ける", null));
    }
}

public class WordCacheTests
{
    [Fact]
    public void 逐字資料存進快取再讀回來()
    {
        var lines = Lrc.Parse("[00:12.00]<00:12.00>夜<00:12.40>に<00:13.00>\n[00:20.00]普通");
        var hit = new LyricsHit(new LyricSheet(lines, true) { Translation = ["夜裡", ""] }, true, "netease");
        var back = LyricsFinder.Unpack(LyricsFinder.Pack(hit, DateTime.Now))!.Value.Hit!;
        Assert.Equal(lines[0].Words, back.Sheet.Lines[0].Words);
        Assert.Null(back.Sheet.Lines[1].Words);
        Assert.Equal(["夜裡", ""], back.Sheet.Translation!);
    }

    [Fact]
    public void 舊的快取沒有逐字資料也讀得進來()
    {
        const string old = "{\"Found\":true,\"Synced\":true,\"Exact\":true,\"Provider\":\"lrclib\",\"Times\":[1000,2000],\"Texts\":[\"a\",\"b\"],\"At\":\"2026-11-01T00:00:00\",\"Translation\":null,\"Romaji\":null}";
        var (hit, at) = LyricsFinder.Unpack(old)!.Value;
        Assert.Equal(["a", "b"], hit!.Sheet.Lines.Select(l => l.Text));
        Assert.All(hit.Sheet.Lines, l => Assert.Null(l.Words));
        Assert.Equal(new DateTime(2026, 11, 1), at);
    }

    [Fact]
    public void 找不到也記得()
    {
        var (hit, _) = LyricsFinder.Unpack(LyricsFinder.Pack(null, DateTime.Now))!.Value;
        Assert.Null(hit);
    }
}

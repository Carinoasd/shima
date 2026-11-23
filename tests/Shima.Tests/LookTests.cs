// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows.Media;
using Xunit;

namespace Shima.Tests;

public class AccentSettingTests
{
    [Theory]
    [InlineData("#6CC4FF", 0x6C, 0xC4, 0xFF)]
    [InlineData("#6cc4ff", 0x6C, 0xC4, 0xFF)]
    [InlineData("  #D97757 ", 0xD9, 0x77, 0x57)]
    [InlineData("4fd1c5", 0x4F, 0xD1, 0xC5)]   // 少了 # 也認
    public void 色碼解析(string text, byte r, byte g, byte b)
    {
        Assert.True(Accent.TryParseHex(text, out var c));
        Assert.Equal(Color.FromRgb(r, g, b), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#FFF")]          // 三位不收
    [InlineData("#FF00FF00")]     // 八位不收
    [InlineData("#GG0000")]
    [InlineData("#12 456")]
    [InlineData("##123456")]
    [InlineData("+12345")]
    public void 不合法的色碼(string? text) => Assert.False(Accent.TryParseHex(text, out _));

    [Fact]
    public void 輸入框_合法就整理成大寫的井號六位_不合法維持原本的值()
    {
        Assert.Equal("#4FD1C5", Accent.Commit(" 4fd1c5 ", "#FFFFFF"));
        Assert.Equal("#FFFFFF", Accent.Commit("藍色", "#FFFFFF"));
        Assert.Equal("#FFFFFF", Accent.Commit("", "#FFFFFF"));
        Assert.Equal("#0A0B0C", Accent.Hex(Color.FromRgb(10, 11, 12)));
    }

    [Fact]
    public void 舊設定檔的值照舊有效()
    {
        Assert.Null(Accent.Fixed("cover", ""));
        Assert.Equal(Colors.White, Accent.Fixed("white", ""));
        Assert.Equal(Color.FromRgb(0xD9, 0x77, 0x57), Accent.Fixed("clay", ""));
    }

    [Fact]
    public void 自訂用色碼_壞掉或不認得的值跟著封面()
    {
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Accent.Fixed("custom", "#123456"));
        Assert.Null(Accent.Fixed("custom", ""));
        Assert.Null(Accent.Fixed("custom", "#12"));
        Assert.Null(Accent.Fixed("rainbow", "#123456"));
        Assert.Null(Accent.Fixed(null, null));
        // 自訂的色碼只在選了自訂時才用
        Assert.Equal(Colors.White, Accent.Fixed("white", "#123456"));
    }

    [Fact]
    public void 預設色_id_不重複_顏色彼此不同_在黑底上夠亮()
    {
        var presets = Accent.Presets;
        Assert.True(presets.Count >= 7);   // 白、橘，加上五個新的
        Assert.Equal(presets.Count, presets.Select(p => p.Id).Distinct().Count());
        Assert.Equal(presets.Count, presets.Select(p => p.Color).Distinct().Count());
        Assert.DoesNotContain(presets, p => p.Id is "cover" or "custom");
        foreach (var (_, _, c) in presets) Assert.True(Math.Max(c.R, Math.Max(c.G, c.B)) >= 0xD0);
        foreach (var (id, _, c) in presets) Assert.Equal(c, Accent.Fixed(id, ""));
    }
}

public class PillProgressTests
{
    [Fact]
    public void 沒有長度就不顯示()
    {
        Assert.Null(PillProgress.Fraction(TimeSpan.FromSeconds(30), TimeSpan.Zero));
        Assert.Null(PillProgress.Fraction(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void 播到幾成_超出範圍夾在零到一()
    {
        Assert.Equal(0.25, PillProgress.Fraction(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(240)));
        Assert.Equal(0, PillProgress.Fraction(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(240)));
        Assert.Equal(1, PillProgress.Fraction(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(240)));
    }

    [Fact]
    public void 移動不到半個像素就不重畫()
    {
        Assert.True(PillProgress.Worth(-1, 0.5, 0));          // 還沒畫過
        Assert.False(PillProgress.Worth(0.5, 0.501, 200));    // 0.2 像素
        Assert.True(PillProgress.Worth(0.5, 0.503, 200));     // 0.6 像素
        Assert.True(PillProgress.Worth(0.9, 0.1, 200));       // 拖回前面
    }
}

public class LyricsLinesTests
{
    [Fact]
    public void 五行是原本的高度()
    {
        Assert.Equal(150, LyricsView.HeightFor(5));
        Assert.True(LyricsView.HeightFor(3) < LyricsView.HeightFor(5));
        Assert.True(LyricsView.HeightFor(5) < LyricsView.HeightFor(7));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    [InlineData(6, 5)]
    [InlineData(9, 7)]
    public void 不在選項裡的值落到最近的那一檔(int lines, int expected) =>
        Assert.Equal(LyricsView.HeightFor(expected), LyricsView.HeightFor(lines));
}

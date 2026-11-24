// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：存歌詞的檔名漏掉 COM¹ 這類上標、以及名稱與副檔名之間夾空白的裝置名稱。</summary>
public class ReservedFileNameTests
{
    [Theory]
    [InlineData("CON", "_CON.lrc")]
    [InlineData("nul", "_nul.lrc")]
    [InlineData("COM1", "_COM1.lrc")]
    [InlineData("COM0", "_COM0.lrc")]
    [InlineData("COM¹", "_COM¹.lrc")]
    [InlineData("LPT²", "_LPT².lrc")]
    [InlineData("lpt³", "_lpt³.lrc")]
    [InlineData("NUL .txt", "_NUL .txt.lrc")]
    [InlineData("aux.v2", "_aux.v2.lrc")]
    [InlineData("COM10", "COM10.lrc")]
    [InlineData("CONSOLE", "CONSOLE.lrc")]
    [InlineData("Song", "Song.lrc")]
    public void FileName_AvoidsDeviceNames(string title, string expected)
    {
        Assert.Equal(expected, LocalProvider.FileName(new SongQuery(title, "", "", 0)));
    }

    [Fact]
    public void FileName_DeviceNameAsTheSecondPart_IsFine()
    {
        Assert.Equal("NUL - Song.lrc", LocalProvider.FileName(new SongQuery("Song", "NUL", "", 0)));
    }
}

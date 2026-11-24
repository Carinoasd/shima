// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima;
using Xunit;

namespace Shima.Tests;

public class IslandFitTests
{
    [Theory]
    [InlineData(1040, 0.9)]
    [InlineData(1040, 1)]
    [InlineData(1040, 1.15)]
    [InlineData(1040, 1.3)]
    [InlineData(1000, 1)]
    public void 一般一千零八十p夠高時各頁照舊(double work, double scale)
    {
        Assert.Equal(IslandFit.ChatHeight, IslandFit.Chat(work, scale));
        Assert.Equal(IslandFit.SettingsMax, IslandFit.Settings(work, scale));
        Assert.Equal(IslandFit.RecentMax, IslandFit.Recent(work, scale));
    }

    [Theory]
    [InlineData(1, 960)]       // 1080p、100%，工作區 1040，高度綽綽有餘
    [InlineData(1, 720)]
    public void 一倍時不管螢幕多矮都不超過原本的高度(double scale, double work) =>
        Assert.True(IslandFit.Chat(work, scale) <= IslandFit.ChatHeight);

    [Theory]
    [InlineData(728, 1.3)]     // 768 高的螢幕
    [InlineData(728, 1.15)]
    [InlineData(680, 1.15)]    // 150% DPI 的 1080p 筆電：720 DIP 扣掉工作列
    [InlineData(680, 1.3)]
    [InlineData(1040, 1.3)]
    public void 算出來的高度乘上比例之後塞得進螢幕(double work, double scale)
    {
        // 島的單位：上緣 8 + 固定的 66 + 頁面（設定多一排子分頁 36、通知多 48）
        Assert.True((IslandFit.TopGap + 66 + IslandFit.Chat(work, scale)) * scale <= work);
        Assert.True((IslandFit.TopGap + 66 + 36 + IslandFit.Settings(work, scale)) * scale <= work);
        Assert.True((IslandFit.TopGap + 66 + 48 + IslandFit.Recent(work, scale)) * scale <= work);
    }

    [Fact]
    public void 一百三十加七百二十八時聊天頁夾到四百七十八()
    {
        Assert.Equal(478, IslandFit.Chat(728, 1.3));
        Assert.Equal(442, IslandFit.Settings(728, 1.3));
    }

    [Fact]
    public void 螢幕小到不合理時不壓成一條()
    {
        Assert.Equal(IslandFit.MinPage, IslandFit.Chat(300, 1.3));
        Assert.Equal(IslandFit.MinPage, IslandFit.Settings(100, 1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-5)]
    public void 螢幕高度不合理時照原本的高度(double work) =>
        Assert.Equal(IslandFit.ChatHeight, IslandFit.Chat(work, 1.3));

    [Fact]
    public void 比例不合理時當成一倍() =>
        Assert.Equal(IslandFit.Chat(700, 1), IslandFit.Chat(700, double.NaN));
}

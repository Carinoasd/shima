// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

public class SettingsPageTests
{
    [Theory]
    [InlineData(0, 0, false)]       // 放得下：不用捲
    [InlineData(0, 0.4, false)]     // 差不到一個像素，當作放得下
    [InlineData(0, 120, true)]      // 超出、還在最上面
    [InlineData(60, 120, true)]     // 捲到一半
    [InlineData(119.7, 120, false)] // 捲到底
    [InlineData(120, 120, false)]
    public void 只有下面還有內容時才淡出(double offset, double scrollable, bool expected) =>
        Assert.Equal(expected, IslandWindow.MoreBelow(offset, scrollable));
}

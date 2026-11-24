// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using Xunit;

namespace Shima.Tests;

/// <summary>「開啟網址」的按鈕剛出現、或還在跟著島變形移動時，點擊不算。</summary>
public class OpenGuardTests
{
    [Fact]
    public void 剛出現的600毫秒內不算()
    {
        var guard = new OpenGuard();
        Assert.False(guard.Counts(0));   // 還沒畫出來
        guard.Seen(new Point(100, 40), 1000);
        Assert.False(guard.Counts(1000));
        Assert.False(guard.Counts(1599));
        Assert.True(guard.Counts(1600));
    }

    [Fact]
    public void 還在移動就重新計時()
    {
        var guard = new OpenGuard();
        guard.Seen(new Point(100, 40), 0);
        guard.Seen(new Point(120, 60), 500);   // 島還在展開
        Assert.False(guard.Counts(700));
        guard.Seen(new Point(120, 60), 900);   // 位置沒變：不重新計時
        Assert.True(guard.Counts(1100));
    }

    [Fact]
    public void 看不到之後再出現_重新計時()
    {
        var guard = new OpenGuard();
        guard.Seen(new Point(100, 40), 0);
        guard.Seen(null, 2000);                // 切到別的分頁
        Assert.False(guard.Counts(3000));
        guard.Seen(new Point(100, 40), 3000);
        Assert.False(guard.Counts(3100));
        Assert.True(guard.Counts(3600));
    }
}

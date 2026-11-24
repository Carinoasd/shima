// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

public class UsageScheduleTests
{
    [Fact]
    public void 關_重開_再關_第一次的計時醒來不釋放_第二次的才釋放()
    {
        int toggles = 0;
        bool open = true;
        void Toggle(bool to) { open = to; toggles++; }

        Toggle(false); int a = toggles;   // 第一次收起，排了 A
        Toggle(true);                     // 四分五十秒後重開
        Toggle(false); int b = toggles;   // 四分五十九秒再收起，排了 B
        Assert.False(IslandWindow.ReleasesTrend(a, toggles, open));   // 一秒後 A 醒來：過期了
        Assert.True(IslandWindow.ReleasesTrend(b, toggles, open));    // 再五分鐘 B 醒來
    }

    private static readonly DateTime At = new(2026, 5, 14, 12, 0, 0);

    [Fact]
    public void 一般情況六十秒內不重查()
    {
        Assert.True(IslandWindow.UsageDue(At, default, false));   // 第一次
        Assert.False(IslandWindow.UsageDue(At.AddSeconds(59), At, false));
        Assert.True(IslandWindow.UsageDue(At.AddSeconds(60), At, false));
    }

    [Fact]
    public void 跨過午夜立刻重查()
    {
        var late = new DateTime(2026, 5, 14, 23, 59, 50);
        Assert.True(IslandWindow.UsageDue(late.AddSeconds(15), late, false));
        Assert.False(IslandWindow.UsageDue(late.AddSeconds(5), late, false));
    }

    [Fact]
    public void 時鐘往回調立刻重查() =>
        Assert.True(IslandWindow.UsageDue(At.AddHours(-1), At, false));

    [Fact]
    public void 有新的工作階段立刻重查() =>
        Assert.True(IslandWindow.UsageDue(At.AddSeconds(10), At, true));

    [Fact]
    public void 圖開著就不釋放()
    {
        Assert.False(IslandWindow.ReleasesTrend(3, 3, true));
        Assert.True(IslandWindow.ReleasesTrend(3, 3, false));
    }
}

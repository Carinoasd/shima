// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

public class DoNotDisturbTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 14, 0, 0);

    [Fact]
    public void ActiveUntilEnd()
    {
        var end = DoNotDisturb.EndFrom(Now);
        Assert.Equal(Now.AddHours(1), end);
        Assert.True(DoNotDisturb.IsActive(end, Now));
        Assert.True(DoNotDisturb.IsActive(end, end.AddSeconds(-1)));
    }

    [Fact]
    public void EndsAtDeadline()
    {
        var end = DoNotDisturb.EndFrom(Now);
        Assert.False(DoNotDisturb.IsActive(end, end));
        Assert.False(DoNotDisturb.IsActive(end, end.AddHours(5)));
    }

    [Fact]
    public void UnsetIsInactive() => Assert.False(DoNotDisturb.IsActive(null, Now));

    [Fact]
    public void ClockMovedBackEndsIt()
    {
        var end = DoNotDisturb.EndFrom(Now);
        Assert.False(DoNotDisturb.IsActive(end, Now.AddHours(-3)));
        Assert.False(DoNotDisturb.IsActive(end, Now.AddMinutes(-5)));
    }

    [Fact]
    public void SmallClockAdjustmentIsTolerated() => Assert.True(DoNotDisturb.IsActive(DoNotDisturb.EndFrom(Now), Now.AddSeconds(-30)));

    [Fact]
    public void SurvivesRestartWithinWindow() => Assert.True(DoNotDisturb.IsActive(DoNotDisturb.EndFrom(Now), Now.AddMinutes(40)));
}

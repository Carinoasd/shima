// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using Xunit;

namespace Shima.Tests;

public class RecentTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 14, 0, 0);

    private static void Fill(RecentLog log, int count, int firstMinute = 0)
    {
        for (var i = 0; i < count; i++) log.Add(Now.AddMinutes(firstMinute + i), RecentKind.Message, "Discord", "t" + i, "b" + i);
    }

    [Fact]
    public void KeepsLatestThirty()
    {
        var log = new RecentLog();
        Fill(log, 35);
        Assert.Equal(30, log.Count);
        Assert.Equal("t34", log.Items[0].Title);
        Assert.Equal("t5", log.Items[^1].Title);
    }

    [Fact]
    public void NewestFirst()
    {
        var log = new RecentLog();
        Fill(log, 3);
        Assert.Equal(["t2", "t1", "t0"], log.Items.Select(i => i.Title));
    }

    [Fact]
    public void ClockMovedBackKeepsOrder()
    {
        var log = new RecentLog();
        log.Add(Now, RecentKind.Agent, "Claude Code", "a", "");
        log.Add(Now.AddHours(-2), RecentKind.Message, "LINE", "b", "");
        Assert.Equal("b", log.Items[0].Title);
        Assert.True(log.Items[0].At >= log.Items[1].At);
    }

    [Fact]
    public void UnreadCountsNewSinceLastLook()
    {
        var log = new RecentLog();
        Fill(log, 2);
        Assert.Equal(2, log.Unread);
        log.MarkRead();
        Assert.Equal(0, log.Unread);
        Fill(log, 1, 10);
        Assert.Equal(1, log.Unread);
    }

    [Fact]
    public void UnreadNeverExceedsKept()
    {
        var log = new RecentLog();
        Fill(log, 50);
        Assert.Equal(30, log.Unread);
    }

    [Fact]
    public void ClearEmptiesEverything()
    {
        var log = new RecentLog();
        Fill(log, 4);
        log.Clear();
        Assert.Equal(0, log.Count);
        Assert.Equal(0, log.Unread);
        Assert.Empty(log.Items);
    }

    [Fact]
    public void ChangedFiresOnlyOnRealChange()
    {
        var log = new RecentLog();
        var fired = 0;
        log.Changed += () => fired++;
        log.MarkRead();
        log.Clear();
        Assert.Equal(0, fired);
        Fill(log, 1);
        log.MarkRead();
        log.Clear();
        Assert.Equal(3, fired);
    }

    [Fact]
    public void WhenShowsClockToday()
    {
        Assert.Equal("09:05", RecentLog.When(new DateTime(2026, 5, 1, 9, 5, 0), Now));
        Assert.Equal("April 30", RecentLog.When(new DateTime(2026, 4, 30, 23, 59, 0), Now, CultureInfo.GetCultureInfo("en-US")));
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

public class UpdatedTests
{
    private static readonly Version Now = new(0, 8, 0, 0);

    [Theory]
    [InlineData("", Updater.VersionChange.First)]
    [InlineData(null, Updater.VersionChange.First)]
    [InlineData("垃圾", Updater.VersionChange.First)]
    [InlineData("0.7.1", Updater.VersionChange.Upgraded)]
    [InlineData("v0.7.9", Updater.VersionChange.Upgraded)]
    [InlineData("0.7.1+abc1234", Updater.VersionChange.Upgraded)]
    [InlineData("0.7.1.0", Updater.VersionChange.Upgraded)]
    [InlineData("0.8.0", Updater.VersionChange.Same)]
    [InlineData("0.8", Updater.VersionChange.Same)]
    [InlineData("0.8.0+abc1234", Updater.VersionChange.Same)]
    [InlineData("0.8.0-beta", Updater.VersionChange.Same)]
    [InlineData("0.9.0", Updater.VersionChange.Downgraded)]
    [InlineData("1.0.0+deadbee", Updater.VersionChange.Downgraded)]
    public void 跟上次記下的版本比(string? last, Updater.VersionChange expected) => Assert.Equal(expected, Updater.Compare(last, Now));

    [Fact]
    public void 現在的版本只看前三段()
    {
        Assert.Equal(Updater.VersionChange.Same, Updater.Compare("0.8.0", new Version(0, 8, 0, 123)));
        Assert.Equal(Updater.VersionChange.Same, Updater.Compare("0.8.0", new Version(0, 8)));   // Build 是 -1
    }

    [Fact]
    public void 說明頁網址()
    {
        Assert.Equal("https://github.com/Carinoasd/shima/releases/tag/v0.8.0", Updater.NotesUrl(Now));
        Assert.Equal("https://github.com/Carinoasd/shima/releases/tag/v0.8.0", Updater.NotesUrl(new Version(0, 8)));
    }
}

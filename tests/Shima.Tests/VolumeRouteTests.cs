// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>/vol：在播的是 Roon 而它的輸出調不了時，不退回去改這台電腦的系統音量。</summary>
public class VolumeRouteTests
{
    private readonly List<string> _touched = [];

    private Func<int?> Roon(int? level) => () => { _touched.Add("roon"); return level; };
    private Func<int?> System(int? level) => () => { _touched.Add("system"); return level; };

    [Fact]
    public void Roon在播而且調得了_只調Roon()
    {
        Assert.Equal(new MediaHub.VolumeResult(42, false), MediaHub.Route(true, Roon(42), System(100)));
        Assert.Equal(["roon"], _touched);
    }

    [Fact]
    public void Roon在播但只能加減或不能調_不碰系統音量()
    {
        var result = MediaHub.Route(true, Roon(null), System(100));
        Assert.Null(result.Level);
        Assert.True(result.RoonCannot);
        Assert.Equal(["roon"], _touched);
    }

    [Fact]
    public void 不是Roon_調系統音量()
    {
        Assert.Equal(new MediaHub.VolumeResult(30, false), MediaHub.Route(false, Roon(42), System(30)));
        Assert.Equal(["system"], _touched);
    }

    [Fact]
    public void 都調不了_不是Roon的問題()
    {
        Assert.Equal(new MediaHub.VolumeResult(null, false), MediaHub.Route(false, Roon(null), System(null)));
    }
}

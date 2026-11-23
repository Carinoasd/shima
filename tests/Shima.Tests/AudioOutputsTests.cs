// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class AudioOutputsTests
{
    private static AudioOutput O(string id, string name, bool def = false) => new(id, name, def);

    [Fact]
    public void 名稱整理掉多餘空白且不會是空的()
    {
        Assert.Equal("Speakers (Realtek Audio)", AudioOutputs.Tidy("  Speakers \t (Realtek   Audio) "));
        Assert.Equal("?", AudioOutputs.Tidy("   "));
        Assert.Equal("?", AudioOutputs.Tidy(null));
    }

    [Fact]
    public void 依名稱排序且與誰是預設無關()
    {
        var a = AudioOutputs.Order([O("3", "headphones", true), O("1", "Speakers"), O("2", "DAC")]);
        var b = AudioOutputs.Order([O("3", "headphones"), O("1", "Speakers", true), O("2", "DAC")]);
        Assert.Equal(["2", "3", "1"], a.Select(o => o.Id));
        Assert.Equal(a.Select(o => o.Id), b.Select(o => o.Id));
    }

    [Fact]
    public void 重複的Id只留一個()
    {
        Assert.Single(AudioOutputs.Order([O("1", "A"), O("1", "A")]));
    }

    [Fact]
    public void 下一個裝置會繞回頭()
    {
        var list = AudioOutputs.Order([O("1", "A"), O("2", "B"), O("3", "C")]);
        Assert.Equal("2", AudioOutputs.Next(list, "1")!.Id);
        Assert.Equal("1", AudioOutputs.Next(list, "3")!.Id);
    }

    [Fact]
    public void 少於兩個裝置沒有下一個()
    {
        Assert.Null(AudioOutputs.Next([], "1"));
        Assert.Null(AudioOutputs.Next([O("1", "A")], "1"));
    }

    [Fact]
    public void 找不到目前裝置時從第一個開始()
    {
        var list = AudioOutputs.Order([O("1", "A"), O("2", "B")]);
        Assert.Equal("1", AudioOutputs.Next(list, "zzz")!.Id);
    }
}

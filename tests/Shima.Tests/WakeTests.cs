// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class HomeCacheTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (AgentHooks.HomeCache Cache, Func<AgentHooks.Home> Find, Func<int> Calls) Make(string? wsl)
    {
        int calls = 0;
        return (new(), () => { calls++; return new AgentHooks.Home(@"C:\u", wsl); }, () => calls);
    }

    [Fact]
    public void 找到之後五分鐘內不再啟動()
    {
        var (cache, find, calls) = Make(@"\\wsl.localhost\U\home\x");
        cache.Get(T0, find);
        cache.Get(T0.AddMinutes(4), find);
        Assert.Equal(1, calls());
        cache.Get(T0.AddMinutes(5), find);
        Assert.Equal(2, calls());
    }

    [Fact]
    public void 沒找到只記三十秒_之後才裝WSL很快看得到()
    {
        var (cache, find, calls) = Make(null);
        cache.Get(T0, find);
        cache.Get(T0.AddSeconds(29), find);
        Assert.Equal(1, calls());
        cache.Get(T0.AddSeconds(30), find);
        Assert.Equal(2, calls());
    }

    [Fact]
    public void 時鐘往回調就重找()
    {
        var (cache, find, calls) = Make(@"\\wsl.localhost\U\home\x");
        cache.Get(T0, find);
        cache.Get(T0.AddHours(-1), find);
        Assert.Equal(2, calls());
    }
}

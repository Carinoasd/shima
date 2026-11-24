// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>橋接的安全性：deck 金鑰只在用 Stream Dock 時有效。</summary>
public class DeckDoorTests
{
    private const string Key = "deck-token-0123456789abcdef0123456789abcdef";

    [Fact]
    public void 沒在用_Stream_Dock_就沒有金鑰()
    {
        int asked = 0;
        var door = new DeckDoor(() => false, () => { asked++; return Key; }, () => 0);
        Assert.Equal("", door.Key);
        Assert.Equal(0, asked);   // 連金鑰檔都不碰
        Assert.Equal(AgentBridge.Door.Refuse, AgentBridge.Gate("GET", "/deck/now", null, "", false, "hook", door.Key));
        Assert.Equal(AgentBridge.Door.Refuse, AgentBridge.Gate("GET", "/deck/now", null, null, false, "hook", door.Key));
    }

    [Fact]
    public void 結果記一陣子_Forget_之後馬上重查()
    {
        long now = 0;
        bool inUse = false;
        int checks = 0;
        var door = new DeckDoor(() => { checks++; return inUse; }, () => Key, () => now);
        Assert.Equal("", door.Key);
        inUse = true;   // 之後才裝 Stream Dock
        now = DeckDoor.Remember - 1;
        Assert.Equal("", door.Key);
        Assert.Equal(1, checks);   // 記住的期間不再摸磁碟
        now = DeckDoor.Remember;
        Assert.Equal(Key, door.Key);
        Assert.Equal(AgentBridge.Door.Deck, AgentBridge.Gate("GET", "/deck/now", null, Key, true, "hook", door.Key));

        inUse = false;   // 關掉 Stream Dock 開關（移除外掛）
        door.Forget();
        Assert.Equal("", door.Key);
        Assert.Equal(3, checks);
    }
}

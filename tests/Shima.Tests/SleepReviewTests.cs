// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>睡眠定時與「暫停所有播放器」的審查修正。</summary>
public class SleepReviewTests
{
    // ---------------------------------------------------------------- 暫停所有播放器（#9 審查 1）

    private sealed record FakeSession(string AppId, bool Playing, bool Broken = false);

    private static (string, bool) Read(FakeSession s) =>
        s.Broken ? throw new COMException("工作階段已關閉") : (s.AppId, s.Playing);

    [Fact]
    public void 有一個工作階段讀不到狀態_其他正在播的照樣暫停()
    {
        FakeSession[] sessions = [new("Chrome", true), new("Edge", true, Broken: true), new("Spotify.exe", true), new("Firefox", false)];
        Assert.Equal([0, 2], SmtcSource.PausePicks(sessions, Read, null));
    }

    [Fact]
    public void 全部讀不到_沒有要暫停的()
    {
        FakeSession[] sessions = [new("A", true, Broken: true), new("B", true, Broken: true)];
        Assert.Empty(SmtcSource.PausePicks(sessions, Read, null));
    }

    [Fact]
    public void 逐一讀狀態時照樣排除不跟的播放器()
    {
        FakeSession[] sessions = [new("Roon.exe", true), new("Chrome", true, Broken: true), new("Spotify.exe", true)];
        Assert.Equal([2], SmtcSource.PausePicks(sessions, Read, id => id.Contains("Roon")));
    }
}

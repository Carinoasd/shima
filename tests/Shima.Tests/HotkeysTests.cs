// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Xunit;

namespace Shima.Tests;

public class HotkeysTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", Hotkeys.Ctrl | Hotkeys.Alt, 0x20u)]
    [InlineData("Ctrl+Shift+F9", Hotkeys.Ctrl | Hotkeys.Shift, 0x78u)]
    [InlineData("Win+Alt+K", Hotkeys.Win | Hotkeys.Alt, 0x4Bu)]
    [InlineData("  ctrl + ALT + space ", Hotkeys.Ctrl | Hotkeys.Alt, 0x20u)]
    [InlineData("control+shift+f24", Hotkeys.Ctrl | Hotkeys.Shift, 0x87u)]
    [InlineData("Alt+1", Hotkeys.Alt, 0x31u)]
    [InlineData("Shift+f1", Hotkeys.Shift, 0x70u)]
    public void Parses(string text, uint mods, uint key)
    {
        Assert.True(Hotkeys.TryParse(text, out var m, out var k));
        Assert.Equal(mods, m);
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Space")]
    [InlineData("F9")]
    [InlineData("K")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl++K")]
    [InlineData("Ctrl+K+L")]
    [InlineData("Ctrl+Ctrl+K")]
    [InlineData("K+Ctrl")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+;")]
    public void Rejects(string text) => Assert.False(Hotkeys.TryParse(text, out _, out _));

    [Fact]
    public void RejectsNull() => Assert.False(Hotkeys.TryParse(null, out _, out _));

    [Theory]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("Ctrl+Alt+Shift+Win+Z")]
    [InlineData("Ctrl+0")]
    [InlineData("Alt+F1")]
    [InlineData("Alt+F24")]
    [InlineData("Win+Enter")]
    [InlineData("Ctrl+Tab")]
    [InlineData("Ctrl+PageDown")]
    [InlineData("Shift+Left")]
    [InlineData("Ctrl+Delete")]
    public void RoundTrips(string text)
    {
        Assert.True(Hotkeys.TryParse(text, out var m, out var k));
        Assert.Equal(text, Hotkeys.Format(m, k));
    }

    [Fact]
    public void FormatsInFixedOrder() => Assert.Equal("Ctrl+Alt+Shift+Win+A", Hotkeys.Normalize("win+shift+alt+ctrl+a"));

    [Fact]
    public void FormatRefusesBareKeysAndUnknownCodes()
    {
        Assert.Null(Hotkeys.Format(0, 0x41));
        Assert.Null(Hotkeys.Format(Hotkeys.Ctrl, 0xBA));
        Assert.Null(Hotkeys.Normalize("Ctrl"));
    }

    [Fact]
    public void EveryLetterDigitAndFunctionKeyRoundTrips()
    {
        for (uint k = 'A'; k <= 'Z'; k++) Assert.Equal(k, Back(Hotkeys.Format(Hotkeys.Ctrl, k)!));
        for (uint k = '0'; k <= '9'; k++) Assert.Equal(k, Back(Hotkeys.Format(Hotkeys.Ctrl, k)!));
        for (uint k = 0x70; k <= 0x87; k++) Assert.Equal(k, Back(Hotkeys.Format(Hotkeys.Ctrl, k)!));
    }

    private static uint Back(string text)
    {
        Assert.True(Hotkeys.TryParse(text, out _, out var k));
        return k;
    }
}

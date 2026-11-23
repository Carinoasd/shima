// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;

namespace Shima;

/// <summary>快捷鍵字串（"Ctrl+Alt+Space"）與 RegisterHotKey 用的修飾鍵、虛擬鍵碼互轉。不依賴畫面，可測試。</summary>
public static class Hotkeys
{
    public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

    private static readonly (string Name, uint Vk)[] Named =
    [
        ("Space", 0x20), ("Enter", 0x0D), ("Tab", 0x09), ("Backspace", 0x08), ("Insert", 0x2D), ("Delete", 0x2E),
        ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
    ];

    /// <summary>解析 "Ctrl + alt + space" 這類寫法；沒有修飾鍵、只有修飾鍵、不認得的鍵都回 false。</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool haveKey = false;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) return false;
            uint mod = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Ctrl,
                "alt" => Alt,
                "shift" => Shift,
                "win" or "windows" => Win,
                _ => 0,
            };
            if (mod != 0)
            {
                if (haveKey || (modifiers & mod) != 0) return false; // 修飾鍵要在前面，也不能重複
                modifiers |= mod;
                continue;
            }
            if (haveKey || KeyFromName(part) is not { } vk) return false;
            key = vk;
            haveKey = true;
        }
        return haveKey && modifiers != 0;
    }

    /// <summary>反向轉換；沒有修飾鍵或鍵碼不支援回 null。順序固定是 Ctrl、Alt、Shift、Win。</summary>
    public static string? Format(uint modifiers, uint key)
    {
        modifiers &= Alt | Ctrl | Shift | Win;
        if (modifiers == 0 || NameFromKey(key) is not { } name) return null;
        var sb = new StringBuilder();
        if ((modifiers & Ctrl) != 0) sb.Append("Ctrl+");
        if ((modifiers & Alt) != 0) sb.Append("Alt+");
        if ((modifiers & Shift) != 0) sb.Append("Shift+");
        if ((modifiers & Win) != 0) sb.Append("Win+");
        return sb.Append(name).ToString();
    }

    /// <summary>整理成標準寫法；不合法回 null。</summary>
    public static string? Normalize(string? text) => TryParse(text, out var m, out var k) ? Format(m, k) : null;

    private static uint? KeyFromName(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c; // 虛擬鍵碼與 ASCII 相同
            return null;
        }
        if ((name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out int f) && f is >= 1 and <= 24 && name.Skip(1).All(char.IsAsciiDigit)) return (uint)(0x6F + f);
        foreach (var (n, vk) in Named)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return vk;
        return null;
    }

    private static string? NameFromKey(uint vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        foreach (var (n, v) in Named) if (v == vk) return n;
        return null;
    }
}

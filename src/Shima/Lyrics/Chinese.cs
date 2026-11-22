// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Shima.Lyrics;

public static class Chinese
{
    private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

    /// <summary>簡體字轉繁體字（Windows 內建的逐字對照；不會改用詞，「软件」只會變成「軟件」）。</summary>
    public static string ToTraditional(string text)
    {
        if (text.Length == 0) return text;
        try
        {
            var buffer = new char[text.Length];
            int n = LCMapStringEx("zh-CN", LCMAP_TRADITIONAL_CHINESE, text, text.Length, buffer, buffer.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return n > 0 ? new string(buffer, 0, n) : text;
        }
        catch { return text; }
    }

    /// <summary>歌詞的中文翻譯來源多半是簡體：介面是簡體就原樣，其他語言一律轉成繁體。</summary>
    public static string Localize(string text) => Loc.Lang == "zh-CN" ? text : ToTraditional(text);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string locale, uint flags, string source, int sourceLength, [Out] char[] destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sort);
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Windows;

namespace Shima;

/// <summary>浮在最上層的小視窗共用的視窗樣式：不出現在 Alt+Tab、不搶焦點，需要時讓滑鼠穿過去。</summary>
internal static class Overlay
{
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>設好視窗樣式；passThrough 為 true 時滑鼠完全穿透。</summary>
    public static void Style(IntPtr hwnd, bool passThrough)
    {
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        style = passThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        if (style != GetWindowLong(hwnd, GWL_EXSTYLE)) SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    /// <summary>再排到最上層一次。無邊框全螢幕的遊戲有時會把置頂的視窗壓下去。</summary>
    public static void Raise(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>整個虛擬桌面（所有螢幕合起來）的範圍，裝置無關像素。</summary>
    public static Rect VirtualScreen => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

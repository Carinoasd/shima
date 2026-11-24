// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Playback;

public static class PageTitle
{
    /// <summary>
    /// 瀏覽器拿不到媒體資訊時會把分頁標題當成歌名，後面掛著網站的名字。
    /// 長的排前面：先拿掉「_哔哩哔哩_bilibili」，不要只拿掉後半截。
    /// </summary>
    private static readonly string[] Tails = ["_哔哩哔哩_bilibili", "_哔哩哔哩", " - YouTube Music", " - YouTube"];

    /// <summary>拿掉標題尾巴的網站名稱；拿掉之後什麼都不剩（標題就是網站名稱）就保持原樣。</summary>
    public static string Clean(string? title)
    {
        var text = Lyrics.Titles.Cap(title).Trim();   // 網頁能控制標題：先截長度，後面的比對才不會被長輸入拖慢
        foreach (var tail in Tails)
            if (text.Length > tail.Length && text.EndsWith(tail, StringComparison.OrdinalIgnoreCase) && text[..^tail.Length].Trim() is { Length: > 0 } rest)
                return rest;
        return text;
    }
}

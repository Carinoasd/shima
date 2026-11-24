// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima;

/// <summary>
/// 島展開後要塞得進螢幕：聊天、設定、通知這幾頁原本是寫死的高度，放大到 115%、130% 或在 150% DPI 的小螢幕上，
/// 乘上比例之後會超出螢幕底部。這裡算「這個螢幕還剩多少，各頁最多可以多高」，不依賴畫面。
/// </summary>
public static class IslandFit
{
    /// <summary>島離視窗上緣的距離（跟 <see cref="IslandScale.WindowHeight"/> 的 8 同一個）。</summary>
    public const double TopGap = 8;

    /// <summary>展開面板在內容頁上面佔的固定高度：實量約 66（上下邊距加分頁列），再留 8 給螢幕下緣。</summary>
    public const double Chrome = 74;

    /// <summary>設定頁在捲動區上面多一排子分頁。</summary>
    public const double SettingsExtra = 36;

    /// <summary>通知頁在清單上下多出來的間距與「全部清除」按鈕。</summary>
    public const double RecentExtra = 58;

    /// <summary>音樂頁在佇列／搜尋抽屜上面佔的固定高度（封面＋進度列＋按鈕列＋間距）。</summary>
    public const double MusicDrawerExtra = 164;

    /// <summary>再矮就沒法用了：螢幕小到這個地步寧可超出，也不要把頁面壓成一條。</summary>
    public const double MinPage = 240, MinMusicDrawer = 180;

    /// <summary>各頁原本的高度（100% 時不變）。</summary>
    public const double ChatHeight = 560, SettingsMax = 540, RecentMax = 440, MusicDrawerHeight = 300;

    /// <summary>視窗上緣到工作區下緣的高度；上緣不合理（NaN）時當成貼在工作區最上面（座標 0）。</summary>
    public static double Below(double workBottom, double windowTop) =>
        double.IsFinite(windowTop) ? workBottom - windowTop : workBottom;

    /// <summary>
    /// 某一頁最多能多高：從視窗上緣到螢幕可用區下緣的高度（<see cref="Below"/>；島在最上面時就是工作區高度）換算成島的單位（除以比例），扣掉上緣、固定的面板高度與這一頁自己的額外高度；
    /// 不會超過原本的高度，所以螢幕夠高時（一般 1080p、100%）跟以前一模一樣。
    /// </summary>
    public static double Page(double workHeight, double scale, double extra, double native)
    {
        if (!double.IsFinite(workHeight) || workHeight <= 0) return native;
        scale = IslandScale.Sanitize(scale);
        double room = Math.Floor(workHeight / scale - TopGap - Chrome - extra);
        return Math.Min(native, Math.Max(MinPage, room));
    }

    public static double Chat(double workHeight, double scale) => Page(workHeight, scale, 0, ChatHeight);
    public static double Settings(double workHeight, double scale) => Page(workHeight, scale, SettingsExtra, SettingsMax);
    public static double Recent(double workHeight, double scale) => Page(workHeight, scale, RecentExtra, RecentMax);

    public static double MusicDrawer(double workHeight, double scale)
    {
        if (!double.IsFinite(workHeight) || workHeight <= 0) return MusicDrawerHeight;
        scale = IslandScale.Sanitize(scale);
        double room = Math.Floor(workHeight / scale - TopGap - Chrome - MusicDrawerExtra);
        return Math.Min(MusicDrawerHeight, Math.Max(MinMusicDrawer, room));
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;

namespace Shima;

/// <summary>
/// 島的大小（設定 → 外觀「大小」）。整座島在視窗裡用一個縮放排版，視窗本身跟著放大縮小；
/// 這裡是不依賴畫面的那一半：比例夾在範圍內、視窗多大、放在哪裡。
/// </summary>
public static class IslandScale
{
    public const double Min = 0.9, Max = 1.3;

    /// <summary>設定頁的四個選項。</summary>
    public static readonly double[] Steps = [0.9, 1, 1.15, 1.3];

    /// <summary>不合理的值（NaN、無限大、太小、太大）一律當成原本的大小，不去猜使用者要多大。</summary>
    public static double Sanitize(double value) =>
        double.IsFinite(value) && value >= Min - 1e-9 && value <= Max + 1e-9 ? value : 1;

    /// <summary>設定頁要顯示哪一個選項：最接近的那一格（手改設定檔填了 1.2 也有一格亮著）。</summary>
    public static double Nearest(double value)
    {
        double scale = Sanitize(value);
        return Steps.MinBy(step => Math.Abs(step - scale));
    }

    /// <summary>視窗的寬或高：島的單位乘上比例。100% 時原封不動，跟加這個設定以前一模一樣。</summary>
    /// <remarks>扣一點點再進位：560 × 1.15 算出來是 644.0000000001 之類，不能因此變成 645。</remarks>
    public static double Window(double size, double scale) => scale == 1 ? size : Math.Ceiling(size * scale - 1e-6);

    /// <summary>
    /// 視窗要多高：平常留固定的高度，島展開得比它高時才長高（透明視窗越大，重畫越吃力）。
    /// 上面 8 是島離視窗上緣的距離，下面 48 留給陰影與回彈。
    /// </summary>
    public static double WindowHeight(double baseHeight, double islandHeight, double scale) =>
        Window(Math.Max(baseHeight, 8 + islandHeight + 48), scale);

    /// <summary>
    /// 視窗的左上角。位置存的是島中心的 X 與視窗上緣的 Y；沒存過，或存的位置已經不在任何螢幕上（換了螢幕），
    /// 就放在主螢幕工作區上緣正中間。島在視窗裡水平置中，所以左邊 = 中心 − 視窗寬的一半。
    /// </summary>
    /// <param name="work">主螢幕的工作區。</param>
    /// <param name="screens">所有螢幕合起來的範圍。</param>
    /// <param name="island">島展開時的寬（已乘上比例）：存的位置靠邊時把中心往內推，放大後面板才不會有一截在螢幕外。</param>
    public static Point TopLeft(Rect work, Rect screens, double? x, double? y, double windowWidth, double island)
    {
        double centre = work.Left + work.Width / 2, top = work.Top;
        if (x is { } savedX && y is { } savedY &&
            savedX > screens.Left && savedX < screens.Right && savedY >= screens.Top && savedY < screens.Bottom - 40)
        {
            centre = Inside(savedX, screens.Left, screens.Right, island);
            top = savedY;
        }
        return new Point(centre - windowWidth / 2, top);
    }

    /// <summary>把中心夾在 [left + 寬/2, right − 寬/2]；範圍比寬還窄就放正中間。</summary>
    public static double Inside(double centre, double left, double right, double width) =>
        right - left <= width ? (left + right) / 2 : Math.Clamp(centre, left + width / 2, right - width / 2);
}

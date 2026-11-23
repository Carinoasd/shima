// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Media;

namespace Shima;

/// <summary>音樂分頁的進度條：每 0.1 秒都在跟，但盡量不讓整個面板重新排版。</summary>
public partial class IslandWindow
{
    /// <summary>
    /// 進度條本身鋪滿整條，用一塊圓角的裁切決定露出多少：改裁切只要重畫，改寬度卻要整個面板重新排版。
    /// 裁切的圓角跟進度條一樣，兩端看起來跟原本照寬度畫的一模一樣（縮放的話短的時候圓角會被壓扁）。
    /// </summary>
    private readonly RectangleGeometry _barClip = new(new Rect(0, 0, 0, 4), 2, 2);
    private (int, int) _clockShown = (-1, -1);

    /// <summary>fraction 是 0…1；沒有長度的歌傳 0。進度條的前端移動不到半個像素就不改。</summary>
    private void ShowBar(double fraction)
    {
        if (!ReferenceEquals(BarFill.Clip, _barClip))
        {
            BarFill.ClearValue(WidthProperty);
            BarFill.HorizontalAlignment = HorizontalAlignment.Stretch;
            BarFill.Clip = _barClip;
        }
        double width = Math.Max(0, Bar.ActualWidth) * Math.Clamp(fraction, 0, 1);
        var shown = _barClip.Rect;
        if (!PillProgress.Worth(shown.Width, width, 1) && (width > 0 || shown.Width == 0)) return;
        _barClip.Rect = new Rect(0, 0, width, 4);
    }

    /// <summary>經過與剩下的時間：秒數變了才重新組字串。</summary>
    private void ShowClock(TimeSpan position, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            if (_clockShown == (-2, -2)) return;
            _clockShown = (-2, -2);
            Elapsed.Text = Remaining.Text = "";
            return;
        }
        var left = duration - position;
        var shown = ((int)Math.Floor(position.TotalSeconds), (int)Math.Floor(left.TotalSeconds));
        if (shown == _clockShown) return;
        _clockShown = shown;
        Elapsed.Text = Clock(position);
        Remaining.Text = "-" + Clock(left);
    }
}

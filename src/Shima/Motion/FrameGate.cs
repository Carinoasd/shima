// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Motion;

/// <summary>
/// 慢動作（歌詞掃光）要不要在這一幀重畫。島是透明的分層視窗，裡面任何東西重畫一次就要整塊重新呈現，
/// 跟著螢幕每一幀（60–144 Hz）畫的話大部分的格子使用者根本分不出來。
/// 換句、捲動、展開收起這種彈簧動畫不走這裡，照樣每一幀都畫。
/// </summary>
public static class FrameGate
{
    /// <summary>
    /// 兩次重畫至少隔這麼久。比 1/30 秒短一點：60 Hz 時剛好每兩幀畫一次；
    /// 剛好 1/30 的話，幀與幀之間一點點的抖動就會讓它在「隔兩幀」與「隔三幀」之間跳，反而看起來一頓一頓的。
    /// </summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(26);

    /// <summary>離上次重畫夠久了嗎。還沒畫過（Zero）或時鐘倒退（換了螢幕）都算夠久。</summary>
    public static bool Due(TimeSpan lastDrawn, TimeSpan now) =>
        lastDrawn == TimeSpan.Zero || now < lastDrawn || now - lastDrawn >= Gap;

    /// <summary>光的前緣（像素）跟上次畫的差了至少半個像素嗎。還沒畫過（NaN）就是要畫。</summary>
    public static bool Moved(double drawn, double now) =>
        double.IsNaN(drawn) || double.IsNaN(now) || Math.Abs(now - drawn) >= 0.5;
}

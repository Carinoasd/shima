// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shima;

/// <summary>
/// 島的大小（設定 → 外觀「大小」）。整座島放在 <c>Root</c> 裡用一個 LayoutTransform 縮放：
/// 裡面的版面照原本的數字排（展開多大、膠囊多寬、彈簧的目標都不用改），字與圖示在最後的大小重新排版、重新點陣化，
/// 不是把畫好的圖拉大，所以不會糊。視窗本身沒有縮放可用，它的寬高另外乘上比例。
/// </summary>
public partial class IslandWindow
{
    /// <summary>跟 XAML 裡視窗的 Width 一樣：放得下最寬的聊天分頁（528）再加上兩側的陰影。</summary>
    private const double BaseWindowWidth = 560;

    private double _scale = 1;

    /// <summary>
    /// 島展開到這麼高時視窗要多高（視窗的單位，已乘上比例）。收著的時候只留膠囊加上陰影要的高度（約 92）：
    /// 透明視窗裡任何東西重畫都要整塊重新呈現，膠囊一直在動，視窗越矮越省（量到播歌時約省 1.5% 一核）。
    /// 展開前 <see cref="Morph"/> 會先把視窗長高，收合的動畫停下來才縮回去，所以途中不會被切掉。
    /// </summary>
    private double WindowHeightFor(double islandHeight) => IslandScale.WindowHeight(_expanded ? BaseWindowHeight : 0, islandHeight, _scale);

    /// <summary>照設定套用大小：啟動時一次，設定頁改了再一次，不用重開。</summary>
    private void ApplyScale()
    {
        _scale = IslandScale.Sanitize(AppSettings.Current.Scale);
        if (_scale == 1)
        {
            // 100% 不掛任何東西，畫出來跟加這個設定以前一模一樣
            Root.ClearValue(LayoutTransformProperty);
            Root.ClearValue(TextOptions.TextFormattingModeProperty);
        }
        else
        {
            var scale = new ScaleTransform(_scale, _scale);
            scale.Freeze();
            Root.LayoutTransform = scale;
            // Display 模式的字距是照沒縮放的大小對齊像素算的，縮放後字距會不均；Ideal 才是照最後的大小排
            TextOptions.SetTextFormattingMode(Root, TextFormattingMode.Ideal);
        }
        // 島的中心不動：寬度變了，左邊重新算（Place 會照存的中心或螢幕正中間放）
        Width = IslandScale.Window(BaseWindowWidth, _scale);
        Height = WindowHeightFor(_h.Target);
        Place();
        ScaleChat();
        if (_expanded) Morph();
    }

    // ---------------------------------------------------------------- 聊天分頁的內嵌瀏覽器

    /// <summary>
    /// 包著內嵌瀏覽器的一層，做反向的縮放：瀏覽器本身拿到的是放大後實際的大小，一個點對螢幕上一個像素，
    /// 字才清楚；網頁內容另外用瀏覽器自己的縮放放大，排版跟 100% 一樣，只是整頁變大。
    /// </summary>
    private readonly Grid _chatFrame = new();

    private void ScaleChat()
    {
        if (_scale == 1) _chatFrame.ClearValue(LayoutTransformProperty);
        else
        {
            var back = new ScaleTransform(1 / _scale, 1 / _scale);
            back.Freeze();
            _chatFrame.LayoutTransform = back;
        }
        if (_web != null) _web.ZoomFactor = _scale;
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shima.Listening;

namespace Shima;

/// <summary>設定 → 音樂：選哪些來源算聆聽。</summary>
public partial class IslandWindow
{
    /// <summary>紀錄裡出現過的來源，一個來源一顆膠囊（亮的算、暗的不算），寫法跟歌詞來源那排一樣。紀錄是空的、也沒有排除任何來源才沒有這一排。</summary>
    private WrapPanel? HistorySourceChips(AppSettings s)
    {
        var seen = ListenSources.Seen(_historyCache.Read().Listens, s.HistoryExcluded);
        if (seen.Count == 0) return null;
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var (name, _, counted) in seen)
        {
            var chip = new Button
            {
                Content = name,
                Style = (Style)FindResource("Pill"), Height = 28, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(11, 0, 11, 0),
                Background = counted ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                Foreground = counted ? Brushes.Black : Brushes.White,
            };
            chip.Click += (_, _) =>
            {
                s.HistoryExcluded = ListenSources.Toggle(s.HistoryExcluded, name);
                s.Save();
                _statsShown = null;   // 統計下次打開要照新的排除重算
                RefreshSettings();
            };
            panel.Children.Add(chip);
        }
        return panel;
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shima.Agents;

namespace Shima;

/// <summary>
/// AI 要開的網址不在常用網站裡（Opener.Trusted）：在 AI 分頁顯示完整網址，使用者按「開啟」才開。
/// 刻意不走 Claude Code 權限請求那一條佇列：那邊的「允許」可以從 Stream Dock 按，這裡只能在島上按
/// （沒有快速鍵、沒有語音、沒有預設按鈕），免得被注入的指令連同一個按鍵就把資料送出去。
/// </summary>
public partial class IslandWindow
{
    private TaskCompletionSource<bool?>? _opening;
    private Border? _openCard;
    private bool _expandedBeforeOpen;

    private void InitOpenConfirm() => Skills.Confirm = ConfirmOpenAsync;

    /// <summary>從背景執行緒呼叫；畫面的事排到 UI 執行緒，這裡不等它。</summary>
    private Task<bool?> ConfirmOpenAsync(string url, CancellationToken giveUp)
    {
        var answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(() =>
        {
            // 島因為全螢幕藏起來、正在問 Claude Code 的權限、已經有一個網址在等：現在沒辦法問，讓 AI 把網址告訴使用者
            if (_closed || _hiddenForFullscreen || _tab == Tab.Ask || _opening != null) { answer.TrySetResult(null); return; }
            _opening = answer;
            ShowOpenCard(url, answer);
            giveUp.Register(() => answer.TrySetResult(false));
            _ = answer.Task.ContinueWith(_ => Dispatcher.BeginInvoke(() => HideOpenCard(answer)), TaskScheduler.Default);
        });
        return answer.Task;
    }

    private void ShowOpenCard(string url, TaskCompletionSource<bool?> answer)
    {
        var uri = new Uri(url);
        // 主機用 punycode 也列出來：gооgle.com（西里爾字母）這種看起來一樣的網域才分得出來
        var host = uri.IdnHost == uri.Host ? uri.Host : $"{uri.Host}  ({uri.IdnHost})";
        var dim = new SolidColorBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
        var card = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        card.Children.Add(new TextBlock { Text = L.T("開啟網址"), FontSize = 13, Foreground = dim });
        // 按鈕跟主機放在同一列：網址再長，按鈕都在上面看得到的地方
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("Pill"), Content = L.T("取消") };
        var open = new Button { Style = (Style)FindResource("Pill"), Content = L.T("開啟"), Margin = new Thickness(8, 0, 0, 0), Background = Brushes.White, Foreground = Brushes.Black };
        cancel.Click += (_, _) => answer.TrySetResult(false);
        open.Click += (_, _) => answer.TrySetResult(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(open);
        DockPanel.SetDock(buttons, Dock.Right);
        var head = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        head.Children.Add(buttons);
        head.Children.Add(new TextBlock { Text = host, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        card.Children.Add(head);
        // 完整網址，不截斷也不捲動：要外送的資料通常就接在網址的最後面，整段都要看得到（Classify 已經限制最長 2048 字）
        card.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 8, 10, 8),
            Child = new TextBlock { Text = url, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap },
        });

        _openCard = new Border { Child = card };
        AiPage.Children.Insert(AiPage.Children.IndexOf(VoiceText), _openCard);
        _expandedBeforeOpen = _expanded;
        TabAi.IsChecked = true;
        if (_expanded) Morph(); else SetExpanded(true);
        Buddy.Cheer();
    }

    private void HideOpenCard(TaskCompletionSource<bool?> answer)
    {
        if (_opening != answer) return;
        _opening = null;
        if (_openCard != null) AiPage.Children.Remove(_openCard);
        _openCard = null;
        // 本來收著（例如用快速鍵問一句）就收回去；滑鼠還在島上就留著
        if (!_expandedBeforeOpen && !Host.IsMouseOver && _tab != Tab.Ask) SetExpanded(false);
        else Morph();
    }
}

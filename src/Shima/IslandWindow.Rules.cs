// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shima.Agents;

namespace Shima;

/// <summary>AI 分頁 Claude Code 那一列的「以後都允許」規則：數量膠囊，按了在下面列出來，可以單筆刪或全部清除。</summary>
public partial class IslandWindow
{
    private const int RuleRow = 30, RulesShown = 6;
    private bool _rulesOpen;
    private bool _rulesHooked;
    /// <summary>清單上現在畫的那幾條；null 表示收著。規則沒變就不重畫，捲到一半的位置才不會跳回最上面、讓 × 對到別條。</summary>
    private IReadOnlyList<AgentRule>? _rulesShown;
    private ScrollViewer? _rulesScroll;

    /// <summary>規則數量變了（AI 分頁打開時、「以後都允許」記了新的、刪掉之後）。</summary>
    private void RefreshRules()
    {
        if (!_rulesHooked)
        {
            _rulesHooked = true;
            // 「以後都允許」是在 hook 的執行緒上、按下去之後才記的：等規則真的存好了再更新
            _bridge.Rules.Changed += () => Dispatcher.BeginInvoke(RefreshRules);
        }
        var rules = _bridge.Rules.List();
        RulesButton.Visibility = rules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RulesButton.Content = Glyph("\uE73E", rules.Count.ToString(Loc.Culture));
        if (rules.Count == 0) _rulesOpen = false;
        RulesBox.Visibility = _rulesOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_rulesOpen) _rulesShown = null;
        else if (_rulesShown == null || !_rulesShown.SequenceEqual(rules)) BuildRules(rules);
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    private void Rules_Click(object sender, RoutedEventArgs e)
    {
        _rulesOpen = !_rulesOpen;
        RefreshRules();
    }

    private void BuildRules(IReadOnlyList<AgentRule> rules)
    {
        _rulesShown = rules;
        var list = new StackPanel();
        foreach (var rule in rules)
        {
            var row = new Grid { Height = RuleRow, Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            var what = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0) };
            what.Inlines.Add(new System.Windows.Documents.Run(rule.Tool) { Foreground = Secondary });
            if (rule.Command != null) what.Inlines.Add(new System.Windows.Documents.Run("  " + rule.Command));
            what.ToolTip = rule.Command != null ? $"{rule.Tool}  {rule.Command}" : rule.Tool;

            var folder = new TextBlock
            {
                Text = rule.Cwd is { } cwd ? PathText.Tail(cwd, 30) : L.T("所有專案"),
                ToolTip = rule.Cwd ?? L.T("所有專案"),
                FontSize = 12, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 190,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(folder, 1);

            var remove = new Button { Content = "\uE711", Style = (Style)FindResource("Key"), Width = 28, Height = 28, FontSize = 11, ToolTip = L.T("刪除") };
            remove.Click += (_, _) => { _bridge.Rules.Remove(rule); RefreshRules(); };
            Grid.SetColumn(remove, 2);

            row.Children.Add(what);
            row.Children.Add(folder);
            row.Children.Add(remove);
            list.Children.Add(row);
        }
        if (_rulesScroll == null)
        {
            // 超過六筆就限制高度，用滾輪捲（跟設定頁一樣不畫捲軸）
            _rulesScroll = new ScrollViewer { MaxHeight = RuleRow * RulesShown + RuleRow / 2, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
            RulesBox.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(0, 2, 0, 2),
                Child = _rulesScroll,
            });
            var clear = new Button { Content = L.T("全部清除"), Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            clear.Click += (_, _) => { _bridge.Rules.Clear(); RefreshRules(); };
            RulesBox.Children.Add(clear);
        }
        // 換掉內容但留著捲動位置：刪掉一條之後，下面那幾條還在原來的地方
        double offset = _rulesScroll.VerticalOffset;
        _rulesScroll.Content = list;
        _rulesScroll.UpdateLayout();
        _rulesScroll.ScrollToVerticalOffset(offset);
    }

    private static readonly Brush Secondary = Frozen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

    /// <summary>膠囊裡的「圖示＋數字」：圖示要用 Segoe Fluent Icons，數字用一般字型。</summary>
    private static StackPanel Glyph(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 5, 0) });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
}

/// <summary>顯示用的路徑縮短。</summary>
public static class PathText
{
    /// <summary>
    /// 太長的路徑留後面：專案資料夾最有用的是最後幾段（C:\Users\me\src\shima → …\src\shima）。
    /// WPF 的 TextTrimming 只會剪掉結尾，所以自己剪開頭；盡量從分隔符切，不把資料夾名字切一半。
    /// </summary>
    public static string Tail(string path, int max)
    {
        if (max < 2 || path.Length <= max) return path;
        int from = path.Length - (max - 1);
        int cut = path.IndexOfAny(['\\', '/'], from);
        // 最後一段本身就太長，只好從中間切
        if (cut < 0 || cut == path.Length - 1) return "…" + path[from..];
        return "…" + path[cut..];
    }
}

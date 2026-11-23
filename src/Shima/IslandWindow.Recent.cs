// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shima;

/// <summary>最近的通知：工具分頁裡的鈴鐺與清單，以及膠囊上的未讀圓點。資料在 <see cref="RecentLog"/>。</summary>
public partial class IslandWindow
{
    private readonly RecentLog _recent = new();
    private bool _recentOpen;

    private static readonly Brush RecentDim = Frozen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));
    private static readonly Brush RecentBody = Frozen(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));

    private void InitRecent()
    {
        _recent.Changed += UpdateRecent;
        if (Demo.On) Demo.FillRecent(_recent, DateTime.Now);
    }

    /// <summary>勿擾期間也照記：那正是之後要回頭看的時候。內容只留在記憶體，不寫紀錄。</summary>
    private void Remember(RecentKind kind, string source, string title, string body) =>
        _recent.Add(DateTime.Now, kind, source, title, body);

    /// <summary>清單正攤在眼前：算看過了。</summary>
    private bool RecentInView => _recentOpen && _tab == Tab.Tools && _expanded;

    /// <summary>展開或切到工具分頁時清單已經攤著：這時不會有 Changed，要自己標成已讀。</summary>
    private void SeeRecent()
    {
        if (RecentInView && _recent.Unread > 0) _recent.MarkRead();
    }

    private void SetRecentOpen(bool open)
    {
        _recentOpen = open && _recent.Count > 0;
        UpdateRecent();
    }

    private void Recent_Click(object sender, RoutedEventArgs e) => SetRecentOpen(!_recentOpen);

    private void RecentClear_Click(object sender, RoutedEventArgs e) => _recent.Clear();

    private void UpdateRecent()
    {
        // MarkRead 會再觸發一次 Changed，剩下的交給那一次
        if (RecentInView && _recent.Unread > 0) { _recent.MarkRead(); return; }
        if (_recent.Count == 0) _recentOpen = false;

        RecentToggle.Visibility = _recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentCount.Visibility = _recent.Unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentCount.Text = _recent.Unread.ToString();
        RecentButton.Foreground = _recentOpen ? DropLine : Brushes.White;
        ToolsBody.Visibility = _recentOpen ? Visibility.Collapsed : Visibility.Visible;
        RecentPage.Visibility = _recentOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_recentOpen) BuildRecentList();
        UpdateRecentDot();
        if (_tab == Tab.Tools && _expanded) Morph();
    }

    /// <summary>膠囊收著而且有沒看過的：角色旁邊亮一個小圓點。</summary>
    private void UpdateRecentDot() =>
        PillDot.Visibility = _recent.Unread > 0 && !_expanded ? Visibility.Visible : Visibility.Collapsed;

    private void BuildRecentList()
    {
        RecentList.Children.Clear();
        var now = DateTime.Now;
        foreach (var item in _recent.Items)
        {
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var line = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.White };
            line.Inlines.Add(new System.Windows.Documents.Run(Flat(item.Source)) { Foreground = RecentDim, FontWeight = FontWeights.SemiBold });
            line.Inlines.Add(new System.Windows.Documents.Run("  " + Flat(item.Title)));
            var when = new TextBlock { Text = RecentLog.When(item.At, now, Loc.Culture), FontSize = 12, Foreground = RecentDim, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(when, 1);
            head.Children.Add(line);
            head.Children.Add(when);

            var stack = new StackPanel();
            stack.Children.Add(head);
            if (Flat(item.Body) is { Length: > 0 } body)
                stack.Children.Add(new TextBlock
                {
                    Text = body, FontSize = 12, Foreground = RecentBody, Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 32,
                });
            RecentList.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), Child = stack });
        }
        RecentScroll.ScrollToTop();
    }

    private static string Flat(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

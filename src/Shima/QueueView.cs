// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shima.Playback;

namespace Shima;

/// <summary>
/// Roon 的播放佇列與搜尋。上面一條搜尋框（旁邊是隨機、循環），下面是清單：
/// 沒有輸入字的時候是佇列，點一首就從那首開始播；有字就是搜尋結果，點了往下走，走到動作就執行，
/// 執行完回到佇列。滑鼠移到可以整個播放的項目上，右邊會出現播放鈕。
/// </summary>
public sealed class QueueView : UserControl
{
    private const string Icons = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private static readonly Brush Text = Frozen(Colors.White),
        Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        Faint = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
        Hover = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        Field = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        Placeholder = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    private readonly RoonSource _roon;
    private readonly TextBox _search;
    private readonly Border _searchBar;
    private readonly Button _back, _shuffle, _loop;
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _depth, _generation;

    public QueueView(RoonSource roon)
    {
        _roon = roon;
        Foreground = Text;
        FontFamily = Loc.Font;

        _back = IconButton("", L.T("返回"));
        _back.Visibility = Visibility.Collapsed;
        _back.Click += async (_, _) => await BackAsync();

        _search = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Text, CaretBrush = Text,
            SelectionBrush = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0), Margin = new Thickness(6, 0, 8, 0), FontFamily = Loc.Font,
        };
        AutomationProperties.SetName(_search, L.T("搜尋"));
        _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { _debounce.Stop(); _ = SearchAsync(); e.Handled = true; }
            else if (e.Key == Key.Escape && _search.Text.Length > 0) { _search.Clear(); e.Handled = true; }
        };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = SearchAsync(); };

        var bar = new Grid { Height = 32 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        var glass = new TextBlock { Text = "", FontFamily = new FontFamily(Icons), FontSize = 13, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(_back, 0); Grid.SetColumn(glass, 1); Grid.SetColumn(_search, 2);
        bar.Children.Add(_back); bar.Children.Add(glass); bar.Children.Add(_search);
        _searchBar = new Border { Background = Field, CornerRadius = new CornerRadius(16), Child = bar, Cursor = Cursors.IBeam };
        _searchBar.MouseLeftButtonDown += (_, _) => _search.Focus();

        _shuffle = IconButton("", L.T("隨機播放"));
        _shuffle.Click += (_, _) => _roon.ToggleShuffle();
        _loop = IconButton("", L.T("循環播放"));
        _loop.Click += (_, _) => _roon.CycleLoop();

        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_shuffle, 1); Grid.SetColumn(_loop, 2);
        _shuffle.Margin = new Thickness(6, 0, 0, 0);
        top.Children.Add(_searchBar); top.Children.Add(_shuffle); top.Children.Add(_loop);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _rows, PanningMode = PanningMode.VerticalOnly };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_scroll, 1);
        root.Children.Add(top); root.Children.Add(_scroll);
        Content = root;

        Loaded += (_, _) =>
        {
            _roon.QueueChanged += OnQueueChanged;
            _roon.Changed += OnChanged;
            ShowQueue();
            UpdateSettings();
        };
        Unloaded += (_, _) =>
        {
            _roon.QueueChanged -= OnQueueChanged;
            _roon.Changed -= OnChanged;
            _debounce.Stop();
        };
    }

    private bool Searching => _search.Text.Trim().Length > 0;

    private void OnQueueChanged() => Dispatcher.BeginInvoke(() => { if (!Searching) ShowQueue(); });
    private void OnChanged() => Dispatcher.BeginInvoke(UpdateSettings);

    /// <summary>隨機、循環按鈕跟著區域設定；搜尋框只在 Core 有給瀏覽服務時出現。</summary>
    private void UpdateSettings()
    {
        var s = _roon.Settings;
        _shuffle.Visibility = _loop.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        _searchBar.Visibility = _roon.BrowseAvailable ? Visibility.Visible : Visibility.Hidden;
        if (s == null) return;
        _shuffle.Opacity = s.Shuffle ? 1 : 0.4;
        _loop.Opacity = s.Loop == "disabled" ? 0.4 : 1;
        ((TextBlock)_loop.Content).Text = s.Loop == "loop_one" ? "" : "";
    }

    // ───── 佇列 ─────

    private void ShowQueue()
    {
        _generation++;
        _depth = 0;
        _back.Visibility = Visibility.Collapsed;
        _rows.Children.Clear();
        var queue = _roon.Queue;
        for (int i = 0; i < queue.Count; i++)
        {
            var item = queue[i];
            var row = Row(item.Title, item.Artist, item.ImageKey, Clock(item.Length), null, current: i == 0);
            row.MouseLeftButtonUp += (_, _) => _roon.PlayFromHere(item.Id);
            _rows.Children.Add(row);
        }
    }

    private static string Clock(TimeSpan t) => t <= TimeSpan.Zero ? "" : t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ───── 搜尋與瀏覽 ─────

    private async Task SearchAsync()
    {
        var query = _search.Text.Trim();
        if (query.Length == 0) { ShowQueue(); _scroll.ScrollToTop(); return; }
        int generation = ++_generation;
        var page = await _roon.SearchAsync(query);
        if (generation != _generation) return;
        _depth = 0;
        ShowPage(page);
    }

    private async Task BackAsync()
    {
        if (_depth == 0) return;
        int generation = ++_generation;
        var page = await _roon.BackAsync();
        if (generation != _generation) return;
        _depth--;
        ShowPage(page);
    }

    private async Task OpenAsync(RoonBrowseItem item)
    {
        int generation = ++_generation;
        var (result, page) = await _roon.OpenAsync(item, item.InputPrompt.Length > 0 ? _search.Text.Trim() : null);
        if (generation != _generation) return;
        if (page != null) { _depth++; ShowPage(page); }
        else Finished(result);
    }

    private async Task PlayAsync(RoonBrowseItem item)
    {
        int generation = ++_generation;
        var result = await _roon.PlayAsync(item);
        if (generation != _generation) return;
        if (result != null) Finished(result);
    }

    /// <summary>動作做完了：失敗就把 Roon 的訊息留在清單上，成功就回到佇列。</summary>
    private void Finished(RoonBrowseResult result)
    {
        if (result.IsError)
        {
            if (result.Message.Length > 0)
                _rows.Children.Insert(0, new TextBlock { Text = result.Message, Foreground = Dim, FontSize = 12, Margin = new Thickness(10, 4, 10, 8), TextWrapping = TextWrapping.Wrap });
            return;
        }
        _search.Clear();
        _debounce.Stop();   // 清空搜尋框會啟動延遲搜尋，等一下又重畫一次；這裡已經要顯示佇列了
        ShowQueue();
        _scroll.ScrollToTop();
    }

    private void ShowPage(RoonBrowsePage? page)
    {
        _rows.Children.Clear();
        _scroll.ScrollToTop();
        _back.Visibility = _depth > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (page == null) return;
        foreach (var item in page.Items)
        {
            if (item.IsHeader)
            {
                _rows.Children.Add(Label(item.Title, 12, Faint, new Thickness(10, 10, 10, 4)));
                continue;
            }
            // 不是最上層的分組、而且往下走得到動作的，滑過去可以直接播
            bool playable = page.Level > 0 && !page.IsActionList && item.Hint is "list" or "action_list";
            var row = Row(item.Title, item.Subtitle, item.ImageKey, "", playable ? () => _ = PlayAsync(item) : null, current: false);
            row.MouseLeftButtonUp += (_, _) => _ = OpenAsync(item);
            _rows.Children.Add(row);
        }
    }

    // ───── 版面 ─────

    private Border Row(string title, string subtitle, string imageKey, string trailing, Action? play, bool current)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(6), Background = Placeholder, Margin = new Thickness(0, 0, 10, 0) };
        if (imageKey.Length > 0) _ = FillAsync(thumb, imageKey);
        grid.Children.Add(thumb);

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Label(title, 13, Text, default, current ? FontWeights.SemiBold : FontWeights.Normal));
        if (subtitle.Length > 0) lines.Children.Add(Label(subtitle, 12, Dim, new Thickness(0, 1, 0, 0)));
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);

        FrameworkElement? side = null;
        if (play != null)
        {
            var button = IconButton("", L.T("播放"));
            button.Visibility = Visibility.Hidden;
            button.Click += (_, e) => { e.Handled = true; play(); };
            side = button;
        }
        else if (trailing.Length > 0)
            side = new TextBlock { Text = trailing, FontSize = 12, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        if (side != null) { Grid.SetColumn(side, 2); grid.Children.Add(side); }

        var row = new Border { Child = grid, Padding = new Thickness(6), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => { row.Background = Hover; if (play != null) side!.Visibility = Visibility.Visible; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; if (play != null) side!.Visibility = Visibility.Hidden; };
        return row;
    }

    private async Task FillAsync(Border thumb, string imageKey)
    {
        var image = await _roon.ThumbnailAsync(imageKey);
        if (image != null) thumb.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
    }

    private static TextBlock Label(string text, double size, Brush brush, Thickness margin, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = brush, Margin = margin, FontWeight = weight ?? FontWeights.Normal,
        FontFamily = Fonts.For(Fonts.HasKana(text)), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static Button IconButton(string glyph, string name)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily(Icons), FontSize = 13, Foreground = Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 32, Height = 32, Cursor = Cursors.Hand, Focusable = false, Template = RoundTemplate(),
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>圓形、沒有邊框的按鈕，滑過去亮一點。</summary>
    private static ControlTemplate RoundTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "Back"));
        template.Triggers.Add(hover);
        return template;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

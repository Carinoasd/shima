// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Listening;

namespace Shima;

/// <summary>
/// 收藏的歌詞：上面是搜尋框，下面每列一句（歌詞一行、下面小字是歌名與藝人），
/// 滑過去右邊出現刪除鈕，點一列觸發 Picked。
/// </summary>
public sealed class FavoritesView : UserControl
{
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                                  Hover = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                                  Line = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly LyricFavorites _store;
    private readonly TextBox _search;
    private readonly StackPanel _rows = new();

    /// <summary>點了一句（整合者拿去做歌詞卡片）。</summary>
    public event Action<Favorite>? Picked;

    public FavoritesView(LyricFavorites store)
    {
        _store = store;
        Width = 400;
        Height = 320;
        Foreground = Brushes.White;
        FontFamily = Loc.Font;

        _search = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            SelectionBrush = Dim,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _search.TextChanged += (_, _) => Refresh();
        _search.KeyDown += (_, e) => { if (e.Key == Key.Escape && _search.Text.Length > 0) { _search.Clear(); e.Handled = true; } };

        var glyph = new TextBlock { Text = "", FontFamily = Icons, FontSize = 13, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var bar = new DockPanel { Height = 30 };
        DockPanel.SetDock(glyph, Dock.Left);
        bar.Children.Add(glyph);
        bar.Children.Add(_search);
        var searchBox = new Border
        {
            Child = bar,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.IBeam,
        };
        searchBox.MouseLeftButtonDown += (_, _) => _search.Focus();

        var scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,   // 系統的捲軸是淺色的，放在深色面板上很突兀；滾輪照樣能捲
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var root = new DockPanel();
        DockPanel.SetDock(searchBox, Dock.Top);
        root.Children.Add(searchBox);
        root.Children.Add(scroll);
        Content = root;

        void Changed() => Dispatcher.BeginInvoke(new Action(Refresh));
        Loaded += (_, _) => { _store.Changed -= Changed; _store.Changed += Changed; Refresh(); };
        Unloaded += (_, _) => _store.Changed -= Changed;
        Refresh();
    }

    public string Query
    {
        get => _search.Text;
        set => _search.Text = value ?? "";
    }

    public void Refresh()
    {
        _rows.Children.Clear();
        foreach (var f in _store.Search(_search.Text)) _rows.Children.Add(Row(f));
    }

    private FrameworkElement Row(Favorite f)
    {
        bool kana = Fonts.HasKana(f.Line + f.Title + f.Artist);
        var family = Fonts.For(kana);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = f.Line, FontFamily = family, FontSize = 13.5, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
        string where = f.Artist.Length > 0 && f.Title.Length > 0 ? f.Title + " · " + f.Artist : f.Title + f.Artist;
        if (where.Length > 0)
            text.Children.Add(new TextBlock { Text = where, FontFamily = family, FontSize = 11, Foreground = Dim, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var delete = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = Icons, FontSize = 11, Foreground = Dim },
            Width = 26,
            Height = 26,
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
            Visibility = Visibility.Hidden,
            VerticalAlignment = VerticalAlignment.Center,
            Template = BareButton(),
        };
        delete.Click += (_, e) => { e.Handled = true; _store.Remove(f.Id); };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(delete, 1);
        grid.Children.Add(text);
        grid.Children.Add(delete);

        var row = new Border
        {
            Child = grid,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 4, 6),
            Cursor = Cursors.Hand,
            ToolTip = f.Second.Length > 0 ? f.Second : null,
        };
        row.MouseEnter += (_, _) => { row.Background = Hover; delete.Visibility = Visibility.Visible; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; delete.Visibility = Visibility.Hidden; };
        row.MouseLeftButtonUp += (_, e) => { if (!e.Handled) Picked?.Invoke(f); };
        return row;
    }

    /// <summary>沒有系統外框的按鈕：只有內容。</summary>
    private static ControlTemplate BareButton()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;
        return template;
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

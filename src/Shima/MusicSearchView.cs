// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

public enum MusicSearchMode { YouTube, YouTubeMusic, Lyrics }

/// <summary>
/// 音樂分頁的搜尋抽屜：上面切換「YouTube」「YT Music」「找歌詞」，中間是搜尋框，下面是結果清單。
/// YouTube／YT Music 點了會在預設瀏覽器開啟播放（找不到或連線失敗也附一鍵去瀏覽器搜尋）；
/// 找歌詞點了直接把歌詞套用到現在播的這首歌並記住。
/// </summary>
public sealed class MusicSearchView : UserControl
{
    private const string Icons = "Segoe Fluent Icons, Segoe MDL2 Assets";
    private static readonly Brush Text = Frozen(Colors.White),
        Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        Faint = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
        Hover = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        Field = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        PillIdle = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
        PillActive = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        BadgeBg = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
        Placeholder = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    private readonly YouTubeSearch _youtube;
    private readonly Func<LyricsFinder> _lyrics;
    private readonly Button _tabYt, _tabYtm, _tabLyrics, _clear;
    private readonly TextBox _search;
    private readonly TextBlock _hint;
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;

    private MusicSearchMode _mode;
    private Track? _track;
    private int _generation;
    private CancellationTokenSource? _inflight;

    public event Action<YouTubeItem>? ItemPicked;
    public event Action<string, bool>? BrowserSearchRequested;
    public event Action<Track, LyricsChoice, LyricSheet>? LyricsPicked;
    public event Action<string>? Notice;
    public event Action? CloseRequested;

    public MusicSearchView(YouTubeSearch youtube, Func<LyricsFinder> lyrics)
    {
        _youtube = youtube;
        _lyrics = lyrics;
        _mode = AppSettings.Current.YouTubeMusic ? MusicSearchMode.YouTubeMusic : MusicSearchMode.YouTube;

        Foreground = Text;
        FontFamily = Loc.Font;

        _tabYt = ModePill("YouTube", MusicSearchMode.YouTube);
        _tabYtm = ModePill("YT Music", MusicSearchMode.YouTubeMusic);
        _tabLyrics = ModePill(L.T("找歌詞"), MusicSearchMode.Lyrics);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        _tabYtm.Margin = new Thickness(6, 0, 6, 0);
        tabs.Children.Add(_tabYt);
        tabs.Children.Add(_tabYtm);
        tabs.Children.Add(_tabLyrics);

        _search = new TextBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Text, CaretBrush = Text,
            SelectionBrush = Frozen(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0), Margin = new Thickness(6, 0, 4, 0), FontFamily = Loc.Font,
        };
        AutomationProperties.SetName(_search, L.T("搜尋"));
        _hint = new TextBlock
        {
            FontSize = 12.5, Foreground = Faint, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0), IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _clear = IconButton("\uE711", L.T("清除"));
        _clear.Visibility = Visibility.Collapsed;
        _clear.Click += (_, _) => { _search.Clear(); _search.Focus(); ShowIdle(); };

        _search.TextChanged += (_, _) =>
        {
            bool empty = _search.Text.Length == 0;
            _hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _clear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { _ = SearchAsync(); e.Handled = true; }
            else if (e.Key == Key.Escape)
            {
                if (_search.Text.Length > 0) { _search.Clear(); ShowIdle(); }
                else CloseRequested?.Invoke();
                e.Handled = true;
            }
        };

        var bar = new Grid { Height = 32 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glass = new TextBlock
        {
            Text = "\uE721", FontFamily = new FontFamily(Icons), FontSize = 13, Foreground = Dim,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(glass, 0);
        Grid.SetColumn(_hint, 1);
        Grid.SetColumn(_search, 1);
        Grid.SetColumn(_clear, 2);
        bar.Children.Add(glass);
        bar.Children.Add(_hint);
        bar.Children.Add(_search);
        bar.Children.Add(_clear);

        var searchBar = new Border
        {
            Background = Field, CornerRadius = new CornerRadius(16), Child = bar,
            Cursor = Cursors.IBeam, Margin = new Thickness(0, 0, 0, 8),
        };
        searchBar.MouseLeftButtonDown += (_, _) => _search.Focus();

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Content = _rows,
            PanningMode = PanningMode.VerticalOnly,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(tabs, 0);
        Grid.SetRow(searchBar, 1);
        Grid.SetRow(_scroll, 2);
        root.Children.Add(tabs);
        root.Children.Add(searchBar);
        root.Children.Add(_scroll);
        Content = root;

        Unloaded += (_, _) => CancelInflight();
        UpdateModeUi();
        ShowIdle();
    }

    public void SetTrack(Track? track)
    {
        _track = track;
        _tabLyrics.Opacity = _track != null ? (_mode == MusicSearchMode.Lyrics ? 1 : 0.85) : 0.4;
    }

    public void FocusInput()
    {
        _search.Focus();
        _search.CaretIndex = _search.Text.Length;
    }

    /// <summary>切到指定模式；有給初始關鍵字時直接填入並開始搜尋。</summary>
    public void OpenMode(MusicSearchMode mode, string? initialQuery = null)
    {
        SetMode(mode, rerun: false);
        if (initialQuery != null)
        {
            _search.Text = initialQuery;
            _search.SelectAll();
            if (initialQuery.Trim().Length > 0) _ = SearchAsync();
            else ShowIdle();
        }
        FocusInput();
    }

    private void SetMode(MusicSearchMode mode, bool rerun)
    {
        if (mode == MusicSearchMode.Lyrics && _track == null)
        {
            Notice?.Invoke(L.T("沒有正在播放的音樂"));
            return;
        }
        bool changed = _mode != mode;
        _mode = mode;
        if (mode is MusicSearchMode.YouTube or MusicSearchMode.YouTubeMusic)
        {
            bool wantMusic = mode == MusicSearchMode.YouTubeMusic;
            if (AppSettings.Current.YouTubeMusic != wantMusic)
            {
                AppSettings.Current.YouTubeMusic = wantMusic;
                AppSettings.Current.Save();
            }
        }
        UpdateModeUi();
        if (!changed) return;
        if (rerun && _search.Text.Trim().Length > 0) _ = SearchAsync();
        else ShowIdle();
    }

    private void UpdateModeUi()
    {
        StylePill(_tabYt, _mode == MusicSearchMode.YouTube);
        StylePill(_tabYtm, _mode == MusicSearchMode.YouTubeMusic);
        StylePill(_tabLyrics, _mode == MusicSearchMode.Lyrics);
        if (_track == null) _tabLyrics.Opacity = 0.4;
        _hint.Text = _mode switch
        {
            MusicSearchMode.YouTube => L.T("搜尋 YouTube 影片或歌曲，按 Enter"),
            MusicSearchMode.YouTubeMusic => L.T("搜尋 YouTube Music 歌曲，按 Enter"),
            _ => L.T("輸入歌名或關鍵字找歌詞，按 Enter"),
        };
    }

    private void CancelInflight()
    {
        try { _inflight?.Cancel(); } catch { }
        _inflight?.Dispose();
        _inflight = null;
    }

    private void ShowIdle()
    {
        CancelInflight();
        _generation++;
        _rows.Children.Clear();
        _rows.Children.Add(StatusText(_mode == MusicSearchMode.Lyrics
            ? L.T("輸入歌名或藝人關鍵字，替現在這首歌挑選歌詞")
            : L.T("輸入關鍵字後按 Enter 搜尋")));
    }

    // ───── 搜尋 ─────

    private async Task SearchAsync()
    {
        var query = _search.Text.Trim();
        if (query.Length == 0) { ShowIdle(); return; }
        if (_mode == MusicSearchMode.Lyrics && _track == null)
        {
            Notice?.Invoke(L.T("沒有正在播放的音樂"));
            return;
        }

        CancelInflight();
        var cts = _inflight = new CancellationTokenSource();
        int generation = ++_generation;
        var mode = _mode;

        _rows.Children.Clear();
        _rows.Children.Add(StatusText(L.T("搜尋中…")));
        _scroll.ScrollToTop();

        if (mode == MusicSearchMode.Lyrics)
            await SearchLyricsAsync(query, generation, cts.Token);
        else
            await SearchYouTubeAsync(query, mode == MusicSearchMode.YouTubeMusic, generation, cts.Token);
    }

    private async Task SearchYouTubeAsync(string query, bool music, int generation, CancellationToken stop)
    {
        List<YouTubeItem> items;
        bool failed = false;
        try
        {
            items = await _youtube.SearchAsync(query, music, Loc.Lang, limit: 10, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        catch (Exception e)
        {
            Log.Write($"YouTube 搜尋失敗: {e.Message}");
            items = [];
            failed = true;
        }
        if (generation != _generation) return;

        _rows.Children.Clear();
        if (items.Count == 0)
        {
            _rows.Children.Add(StatusText(failed ? L.T("搜尋失敗，可直接用瀏覽器開啟搜尋結果") : L.T("找不到結果")));
            _rows.Children.Add(BrowserRow(query, music));
            return;
        }
        foreach (var item in items)
        {
            var row = YouTubeRow(item, generation);
            row.MouseLeftButtonUp += (_, _) => ItemPicked?.Invoke(item);
            _rows.Children.Add(row);
        }
        _rows.Children.Add(BrowserRow(query, music));
    }

    private async Task SearchLyricsAsync(string query, int generation, CancellationToken stop)
    {
        List<LyricsChoice> items;
        bool failed = false;
        try
        {
            items = await _lyrics().SearchAsync(query, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        catch (Exception e)
        {
            Log.Write($"歌詞搜尋失敗: {e.Message}");
            items = [];
            failed = true;
        }
        if (generation != _generation) return;

        _rows.Children.Clear();
        if (items.Count == 0)
        {
            _rows.Children.Add(StatusText(failed ? L.T("搜尋失敗，請稍後再試") : L.T("找不到符合的歌詞")));
            return;
        }
        foreach (var choice in items)
        {
            var row = LyricsRow(choice);
            row.MouseLeftButtonUp += (_, _) => _ = PickLyricsAsync(choice, generation);
            _rows.Children.Add(row);
        }
    }

    private async Task PickLyricsAsync(LyricsChoice choice, int generation)
    {
        if (_track is not { } target) { Notice?.Invoke(L.T("沒有正在播放的音樂")); return; }
        Notice?.Invoke(L.T("載入歌詞中…"));
        LyricSheet? sheet;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            sheet = await choice.LoadAsync(limit.Token);
        }
        catch (Exception e)
        {
            Log.Write($"載入搜尋歌詞失敗 ({choice.Provider}): {e.Message}");
            sheet = null;
        }
        if (generation != _generation) return;
        if (sheet == null || sheet.Lines.Count == 0)
        {
            Notice?.Invoke(L.T("這筆結果沒有歌詞"));
            return;
        }
        LyricsPicked?.Invoke(target, choice, sheet);
    }

    // ───── 版面 ─────

    private Border YouTubeRow(YouTubeItem item, int generation)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var thumb = new Border
        {
            Width = item.Music ? 38 : 54,
            Height = item.Music ? 38 : 30,
            CornerRadius = new CornerRadius(item.Music ? 6 : 5),
            Background = Placeholder,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (item.ThumbnailUrl.Length > 0) _ = FillThumbAsync(thumb, item.ThumbnailUrl, generation);
        grid.Children.Add(thumb);

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Label(item.Title, 13, Text, default, FontWeights.Normal));
        if (item.Channel.Length > 0) lines.Children.Add(Label(item.Channel, 11.5, Dim, new Thickness(0, 1, 0, 0)));
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);

        if (item.Duration.Length > 0)
        {
            var dur = new TextBlock
            {
                Text = item.Duration, FontSize = 11.5, Foreground = Dim,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0),
            };
            Grid.SetColumn(dur, 2);
            grid.Children.Add(dur);
        }

        return InteractiveRow(grid);
    }

    private Border LyricsRow(LyricsChoice choice)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Background = BadgeBg, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), MinWidth = 44,
            Child = new TextBlock
            {
                Text = ProviderLabel(choice.Provider), FontSize = 10.5, Foreground = Dim,
                HorizontalAlignment = HorizontalAlignment.Center, FontFamily = Loc.Font,
            },
        };
        grid.Children.Add(badge);

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Label(choice.Title, 13, Text, default, FontWeights.Normal));
        if (choice.Artist.Length > 0) lines.Children.Add(Label(choice.Artist, 11.5, Dim, new Thickness(0, 1, 0, 0)));
        Grid.SetColumn(lines, 1);
        grid.Children.Add(lines);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0) };
        if (choice.Duration > 0)
            right.Children.Add(new TextBlock { Text = Clock(choice.Duration), FontSize = 11.5, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
        if (!choice.Synced)
            right.Children.Add(new TextBlock { Text = " " + L.T("純文字"), FontSize = 10.5, Foreground = Faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        return InteractiveRow(grid);
    }

    private Border BrowserRow(string query, bool music)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = "\uE8A7", FontFamily = new FontFamily(Icons), FontSize = 12, Foreground = Dim,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = L.T("在瀏覽器開啟搜尋結果"), FontSize = 12, Foreground = Dim,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = Loc.Font,
        });
        var row = InteractiveRow(panel);
        row.Margin = new Thickness(0, 4, 0, 0);
        row.MouseLeftButtonUp += (_, _) => BrowserSearchRequested?.Invoke(query, music);
        return row;
    }

    private async Task FillThumbAsync(Border thumb, string url, int generation)
    {
        var image = await _youtube.ThumbnailAsync(url);
        if (image == null || generation != _generation) return;
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        thumb.Background = brush;
    }

    private static string ProviderLabel(string name) => name switch
    {
        "lrclib" => "LRCLIB",
        "netease" => L.T("網易雲"),
        "qq" => "QQ",
        "kugou" => L.T("酷狗"),
        _ => name,
    };

    private static string Clock(double seconds)
    {
        if (seconds <= 0) return "";
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private static Border InteractiveRow(UIElement child)
    {
        var row = new Border
        {
            Child = child, Padding = new Thickness(6), CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => row.Background = Hover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private static TextBlock StatusText(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = Faint, HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(8, 22, 8, 8), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        FontFamily = Loc.Font,
    };

    private static TextBlock Label(string text, double size, Brush brush, Thickness margin, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, Foreground = brush, Margin = margin, FontWeight = weight ?? FontWeights.Normal,
        FontFamily = Fonts.For(Fonts.HasKana(text)), TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private Button ModePill(string label, MusicSearchMode mode)
    {
        var button = new Button
        {
            Content = label, Height = 26, Padding = new Thickness(12, 0, 12, 0), FontSize = 12,
            Foreground = Text, Background = PillIdle, Cursor = Cursors.Hand, Focusable = false,
            FontFamily = Loc.Font, Template = PillTemplate(),
        };
        button.Click += (_, _) => SetMode(mode, rerun: true);
        return button;
    }

    private static void StylePill(Button button, bool active)
    {
        button.Background = active ? PillActive : PillIdle;
        button.Foreground = active ? Text : Dim;
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Opacity = 1;
    }

    private static ControlTemplate PillTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static Button IconButton(string glyph, string name)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily(Icons), FontSize = 11, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 28, Height = 28, Cursor = Cursors.Hand, Focusable = false, Template = RoundTemplate(),
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static ControlTemplate RoundTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "Back");
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
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

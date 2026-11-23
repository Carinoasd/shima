// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Shima;

/// <summary>
/// 島因為全螢幕藏起來時，換歌從螢幕上緣滑出來的一張小卡片：封面、歌名、藝人，停一下再滑回去。
/// 滑鼠完全穿透、不搶焦點。
/// </summary>
public sealed class MiniToast : Window
{
    private const double MaxCard = 380, Room = 14;

    private readonly Border _card, _cover;
    private readonly Grid _frame = new();
    private readonly TextBlock _title, _artist;
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private IntPtr _hwnd;
    private int _generation;

    /// <summary>卡片出現在哪個螢幕（裝置無關像素的範圍，通常是全螢幕程式所在的螢幕）；null 是主螢幕。</summary>
    public Rect? Area { get; set; }

    /// <summary>停留多久才滑走。</summary>
    public TimeSpan Hold
    {
        get => _hold.Interval;
        set => _hold.Interval = value;
    }

    /// <summary>跟著島的大小（設定 → 外觀「大小」）。卡片在裡面照原本的數字排，整張縮放；視窗跟著變大變小。</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            _scale = IslandScale.Sanitize(value);
            if (_scale == 1) _frame.ClearValue(LayoutTransformProperty);
            else
            {
                var scale = new ScaleTransform(_scale, _scale);
                scale.Freeze();
                _frame.LayoutTransform = scale;
            }
            Width = IslandScale.Window(MaxCard + Room * 2, _scale);
            Height = IslandScale.Window(64 + Room * 2, _scale);
        }
    }
    private double _scale = 1;

    public MiniToast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Title = "Shima";
        FontFamily = Loc.Font;
        Width = MaxCard + Room * 2;
        Height = 64 + Room * 2;

        _cover = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
        _artist = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _title, _artist } };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_cover, Dock.Left);
        row.Children.Add(_cover);
        row.Children.Add(words);
        _card = new Border
        {
            Child = row,
            Height = 64,
            MinWidth = 220,
            MaxWidth = MaxCard,
            Padding = new Thickness(9, 9, 18, 9),
            CornerRadius = new CornerRadius(32),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x12, 0x12, 0x14)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, Room, 0, 0),
            RenderTransform = _slide,
        };
        _frame.Children.Add(_card);
        Content = _frame;
        _slide.Y = Away;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Overlay.Style(_hwnd, passThrough: true);
        };
        _hold.Tick += (_, _) => Leave();
    }

    /// <summary>藏起來時卡片在視窗上緣外面。</summary>
    private double Away => -(Room + 64 + 6);

    /// <summary>換歌了：滑出來（已經在畫面上就換內容、重新計時）。</summary>
    public void Show(ImageSource? cover, string title, string artist, Color accent)
    {
        _title.Text = title ?? "";
        _artist.Text = artist ?? "";
        _artist.Visibility = _artist.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _title.FontFamily = _artist.FontFamily = Fonts.For(Fonts.HasKana(_title.Text + _artist.Text));
        _cover.Background = cover != null ? new ImageBrush(cover) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        _card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, accent.R, accent.G, accent.B));

        var area = Area ?? new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var at = Place(area, Width);
        Left = at.X;
        Top = at.Y;

        _generation++;
        if (!IsVisible)
        {
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.Y = Away;
            Show();
        }
        Overlay.Raise(_hwnd);
        // 從現在的位置接著滑（正在滑走的話就掉頭回來），不會跳一下
        Slide(0, 300, EasingMode.EaseOut, null);
        _hold.Stop();
        _hold.Start();
    }

    private void Leave()
    {
        _hold.Stop();
        int generation = _generation;
        Slide(Away, 240, EasingMode.EaseIn, () => { if (generation == _generation) Hide(); });
    }

    private void Slide(double to, int ms, EasingMode mode, Action? done)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = mode } };
        if (done != null) animation.Completed += (_, _) => done();
        _slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    /// <summary>視窗放在範圍的上緣正中間。</summary>
    public static Point Place(Rect area, double width) => new(Math.Round(area.Left + (area.Width - width) / 2), area.Top);
}

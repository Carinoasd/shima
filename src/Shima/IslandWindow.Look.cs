// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Shima;

// 外觀：強調色那一排色塊、膠囊上的進度線、歌詞區的行數。
public partial class IslandWindow
{
    // ---------------------------------------------------------------- 膠囊上的進度線

    private double _pillProgressShown = -1;

    /// <summary>Tick 每 0.5 秒呼叫一次：只改一個縮放，而且移動不到半個像素就不改，膠囊不會因為它每一格都重畫。</summary>
    private void UpdatePillProgress()
    {
        var track = _track;
        if (!AppSettings.Current.PillProgress || track == null || PillProgress.Fraction(track.Now, track.Duration) is not { } fraction)
        {
            PillTrack.Visibility = Visibility.Collapsed;
            _pillProgressShown = -1;
            return;
        }
        PillTrack.Visibility = Visibility.Visible;
        // 完全展開後膠囊收起來了，看不到就不用跟
        if (Pill.Visibility != Visibility.Visible) return;
        if (!PillProgress.Worth(_pillProgressShown, fraction, PillTrack.ActualWidth)) return;
        _pillProgressShown = fraction;
        PillProgressScale.ScaleX = fraction;
    }

    // ---------------------------------------------------------------- 歌詞行數

    private void ApplyLyricsLines()
    {
        LyricsList.Height = LyricsView.HeightFor(AppSettings.Current.LyricsLines);
        if (_expanded) Morph();
    }

    // ---------------------------------------------------------------- 強調色：一排色塊

    /// <summary>「跟著封面」的圓點：幾個顏色斜斜地漸層過去，看得出是會變的。</summary>
    private static Brush AutoSwatch()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        string[] ids = ["sakura", "lemon", "mint", "sky", "lavender"];
        for (int i = 0; i < ids.Length; i++) brush.GradientStops.Add(new GradientStop(Accent.Fixed(ids[i], null)!.Value, i / (ids.Length - 1.0)));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// 「顏色」那一列：右邊一排圓點直接點，選中的有一圈白框。自訂是最後一個，選了它左邊出現色碼輸入框
    /// （放左邊：圓點的位置不會因為輸入框出現而移動，滑鼠底下還是剛點的那一個）。
    /// </summary>
    private DockPanel AccentRow()
    {
        var s = AppSettings.Current;
        var rings = new List<(string Id, Ellipse Ring)>();
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -3, 0) };

        var hex = new TextBox { Style = (Style)FindResource("Input"), Height = 26, Width = 92, MaxLength = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var customDot = new Ellipse { Width = 16, Height = 16 };
        var customGlyph = new TextBlock { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };

        void ShowCustom()
        {
            if (Accent.TryParseHex(s.AccentCustom, out var c))
            {
                customDot.Fill = new SolidColorBrush(c);
                customGlyph.Text = "\uE70F";   // 筆：看得出這顆是可以改的
                customGlyph.Foreground = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Brushes.Black : Brushes.White;
            }
            else
            {
                customDot.Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                customGlyph.Text = "\uE710";   // 加號：還沒設定過
                customGlyph.Foreground = Brushes.White;
            }
        }

        void Show()
        {
            // 不認得的值（舊版或手改的設定檔）當作跟著封面，跟 Accent.Fixed 一致
            bool known = rings.Any(r => r.Id == s.AccentMode);
            foreach (var (id, ring) in rings) ring.Stroke = id == s.AccentMode || !known && id == "cover" ? Brushes.White : Brushes.Transparent;
            hex.Visibility = s.AccentMode == "custom" ? Visibility.Visible : Visibility.Collapsed;
            ShowCustom();
        }

        void Pick(string id)
        {
            if (id == "custom" && !Accent.TryParseHex(s.AccentCustom, out _))
                s.AccentCustom = Accent.Hex(ThemeAccent(_coverShown));   // 第一次點自訂：從現在看到的顏色開始改
            s.AccentMode = id;
            s.Save();
            ApplyTheme();
            hex.Text = s.AccentCustom;
            Show();
            if (id == "custom") { hex.Focus(); hex.SelectAll(); }
        }

        void Add(string id, string name, Brush fill, UIElement? glyph = null, Ellipse? dot = null)
        {
            var ring = new Ellipse { Width = 24, Height = 24, StrokeThickness = 1.6 };
            var cell = new Grid { Width = 26, Height = 26, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name, Margin = new Thickness(1, 0, 1, 0) };
            cell.Children.Add(ring);
            cell.Children.Add(dot ?? new Ellipse { Width = 16, Height = 16, Fill = fill });
            if (glyph != null) cell.Children.Add(glyph);
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(id); };
            rings.Add((id, ring));
            dots.Children.Add(cell);
        }

        Add("cover", L.T("跟著封面"), AutoSwatch());
        foreach (var (id, name, color) in Accent.Presets) Add(id, L.T(name), Frozen(color));
        Add("custom", L.T("自訂"), Brushes.Transparent, customGlyph, customDot);

        // 輸入框：Enter 或離開時存；不合法就回到原本的色碼，Esc 也是
        void Commit()
        {
            var next = Accent.Commit(hex.Text, s.AccentCustom);
            hex.Text = next;
            if (next == s.AccentCustom) return;
            s.AccentCustom = next;
            s.Save();
            ApplyTheme();
            ShowCustom();
        }
        hex.Text = s.AccentCustom;
        hex.LostKeyboardFocus += (_, _) => Commit();
        hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { hex.Text = s.AccentCustom; Keyboard.ClearFocus(); e.Handled = true; }
        };
        Show();

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(hex);
        right.Children.Add(dots);
        return SettingsRow("顏色", right);
    }

}

/// <summary>膠囊進度線的計算，不碰畫面才能測。</summary>
public static class PillProgress
{
    /// <summary>播到整首的幾成；沒有長度（直播、長度 0）就是 null，不顯示。</summary>
    public static double? Fraction(TimeSpan now, TimeSpan duration) =>
        duration <= TimeSpan.Zero ? null : Math.Clamp(now / duration, 0, 1);

    /// <summary>值得重畫嗎：還沒畫過，或線的前端在 length 寬的線上移動了至少半個像素。</summary>
    public static bool Worth(double shown, double next, double length) =>
        shown < 0 || Math.Abs(next - shown) * Math.Max(0, length) >= 0.5;
}

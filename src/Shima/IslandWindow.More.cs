// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

// 主題、膠囊上的手勢、設定頁下半部、第一次啟動的引導。
public partial class IslandWindow
{
    private static readonly Color Clay = Color.FromRgb(0xD9, 0x77, 0x57);

    // ---------------------------------------------------------------- 主題

    /// <summary>掃光與進度條用的顏色：預設取自封面，也可以固定。</summary>
    private Color ThemeAccent(ImageSource? cover) => AppSettings.Current.AccentMode switch
    {
        "white" => Colors.White,
        "clay" => Clay,
        _ => Accent.From(cover),
    };

    private void ApplyTheme()
    {
        double opacity = Math.Clamp(AppSettings.Current.Opacity, 0.55, 1);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), 0, 0, 0));
        brush.Freeze();
        Body.Background = brush;
        _coverShown = null;   // 讓下一次 Refresh 重新套顏色
        Refresh();
    }

    // ---------------------------------------------------------------- 膠囊上的手勢

    /// <summary>在膠囊上滾滾輪調音量；按住 Shift 滾是上一首／下一首。</summary>
    private void Pill_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_expanded || _track == null) return;
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (e.Delta > 0) _media.Previous(); else _media.Next();
            return;
        }
        int steps = e.Delta > 0 ? 1 : -1;
        if ((_media.Nudge(steps) ?? _system.Nudge(steps * 2)) is { } volume) Toast(L.F("音量 {0}", volume), 2, false);
    }

    /// <summary>在膠囊上按滑鼠中鍵：播放／暫停。</summary>
    private void Pill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _track == null) return;
        e.Handled = true;
        _media.PlayPause();
    }

    // ---------------------------------------------------------------- 現在這句歌詞

    /// <summary>現在唱到的那一句與它的第二行；沒有同步歌詞或正在間奏就是空字串。</summary>
    private (string Line, string Second) CurrentLine()
    {
        if (_track is not { } track || _sheet is not { Synced: true } sheet) return ("", "");
        int i = Lrc.LineAt(sheet.Lines, track.Now + LyricLead + _lyricShift);
        if (i < 0 || sheet.Lines[i].Text.Length == 0) return ("", "");
        var extra = AppSettings.Current.LyricsSecondLine switch
        {
            "translation" => sheet.Translation ?? sheet.Romaji,
            "romaji" => sheet.Romaji ?? sheet.Translation,
            _ => null,
        };
        return (sheet.Lines[i].Text, extra != null && i < extra.Count ? extra[i] : "");
    }

    /// <summary>請 AI 解釋現在這句：讀音、單字、文法。</summary>
    private async void ExplainLine_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track || CurrentLine().Line is not { Length: > 0 } line) { Toast(L.T("現在沒有歌詞"), 3, false); return; }
        TabAi.IsChecked = true;
        SetExpanded(true);
        await AskAsync(L.F("解釋這句歌詞的讀音、單字與文法：「{0}」（{1} — {2}）", line, track.Title, track.Artist));
    }

    // ---------------------------------------------------------------- 設定頁下半部

    private Border Heading(string text) => new()
    {
        Margin = new Thickness(2, 12, 0, 4),
        Child = new TextBlock { Text = L.T(text), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)) },
    };

    private CheckBox Toggle(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Style = (Style)FindResource("Switch"), Content = L.T(text), IsChecked = value };
        box.Click += (_, _) => { changed(box.IsChecked == true); AppSettings.Current.Save(); };
        return box;
    }

    /// <summary>一列：左邊文字，右邊一顆按了就換下一個選項的按鈕。</summary>
    private DockPanel Choice(string text, (string Id, string Name)[] options, Func<string> get, Action<string> set)
    {
        var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0) };
        void Show() => button.Content = L.T(options.FirstOrDefault(o => o.Id == get(), options[0]).Name);
        button.Click += (_, _) =>
        {
            int at = Array.FindIndex(options, o => o.Id == get());
            set(options[(at + 1) % options.Length].Id);
            AppSettings.Current.Save();
            Show();
        };
        Show();
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = L.T(text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>一列：左邊文字，右邊一個輸入框，離開輸入框時存起來。</summary>
    private DockPanel Entry(string text, string value, double width, Action<string> changed)
    {
        var box = new TextBox { Style = (Style)FindResource("Input"), Height = 26, Width = width, Text = value };
        box.LostFocus += (_, _) => { changed(box.Text.Trim()); AppSettings.Current.Save(); };
        DockPanel.SetDock(box, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = L.T(text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static string Percent(double value) => ((int)Math.Round(value * 100)).ToString(CultureInfo.InvariantCulture);

    /// <summary>原本只能改 settings.json 的選項。每次打開設定頁重建，才會是現在的值。</summary>
    private void BuildMoreSettings()
    {
        var s = AppSettings.Current;
        var rows = MoreSettings.Children;
        rows.Clear();

        rows.Add(Heading("外觀"));
        rows.Add(Choice("底色", [("1", "不透明"), ("0.85", "85%"), ("0.7", "70%"), ("0.55", "55%")],
            () => s.Opacity >= 0.95 ? "1" : s.Opacity >= 0.8 ? "0.85" : s.Opacity >= 0.65 ? "0.7" : "0.55",
            v => { s.Opacity = double.Parse(v, CultureInfo.InvariantCulture); ApplyTheme(); }));
        rows.Add(Choice("顏色", [("cover", "跟著封面"), ("white", "白"), ("clay", "橘")], () => s.AccentMode, v => { s.AccentMode = v; ApplyTheme(); }));
        rows.Add(CharacterImageRow());

        rows.Add(Heading("歌詞"));
        rows.Add(Toggle("桌面歌詞", s.DesktopLyrics, v => { s.DesktopLyrics = v; ApplyDesktopLyrics(); }));
        rows.Add(Toggle("全螢幕時換歌提示", s.FullscreenToast, v => s.FullscreenToast = v));

        rows.Add(Heading("聆聽"));
        rows.Add(Toggle("記下聽了什麼", s.History, v => s.History = v));
        rows.Add(Entry("ListenBrainz", s.ListenBrainzToken, 190, v => s.ListenBrainzToken = v));
        rows.Add(Toggle("Discord 顯示封面", s.DiscordCover, v => s.DiscordCover = v));
        rows.Add(Toggle("Roon 搜尋", s.RoonSearch, v => { s.RoonSearch = v; Toast(L.T("重新啟動 Shima，再到 Roon「設定 → 擴充功能」啟用一次"), 10, false); }));
        rows.Add(Entry("Roon", s.RoonHost, 150, v => { if (v != s.RoonHost) { s.RoonHost = v; Toast(L.T("重新啟動後生效"), 4, false); } }));

        rows.Add(Heading("語音"));
        rows.Add(Choice("辨識模型", [("tiny", "tiny"), ("base", "base"), ("small", "small"), ("medium", "medium")], () => s.WhisperModel, v => s.WhisperModel = v));
        rows.Add(Toggle("聽的時候暫停音樂", s.PauseWhileListening, v => s.PauseWhileListening = v));
        rows.Add(Toggle("一直對話到再按一次", s.VoiceContinuous, v => s.VoiceContinuous = v));
        rows.Add(Toggle("說「Shima」就開始聽", s.WakeWord, v => { s.WakeWord = v; ApplyWakeWord(); }));

        rows.Add(Heading("番茄鐘"));
        rows.Add(Entry("專注", s.FocusMinutes.ToString(CultureInfo.InvariantCulture), 52, v => { if (int.TryParse(v, out int m) && m is >= 1 and <= 180) s.FocusMinutes = m; }));
        rows.Add(Entry("休息", s.BreakMinutes.ToString(CultureInfo.InvariantCulture), 52, v => { if (int.TryParse(v, out int m) && m is >= 1 and <= 60) s.BreakMinutes = m; }));
    }

    /// <summary>自己的圖當角色：選一張 GIF 或 PNG。</summary>
    private DockPanel CharacterImageRow()
    {
        var s = AppSettings.Current;
        var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0), Content = L.T("選圖片") };
        button.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "GIF / PNG|*.gif;*.png" };
            if (dialog.ShowDialog(this) != true) return;
            s.CharacterImage = dialog.FileName;
            s.Character = "custom";
            s.Save();
            ApplyCharacter();
        };
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = L.T("自己的角色"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>打開了 Roon 搜尋、Roon 卻還沒放行：提醒去 Roon 啟用（Roon 在這種時候完全不回應，島上看起來就像沒在播歌）。</summary>
    private async void RemindRoon()
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        if (!AppSettings.Current.RoonSearch || AppSettings.Current.RoonHost.Length == 0 || _media.Roon.Registered) return;
        Log.Write("roon: 還沒連上，可能在等 Roon「設定 → 擴充功能」啟用 Shima");
        Toast(L.T("到 Roon「設定 → 擴充功能」啟用 Shima"), 12, false);
    }

    // ---------------------------------------------------------------- 第一次啟動

    /// <summary>第一次啟動：展開島，告訴人最容易錯過的三件事。按「知道了」之後不再出現。</summary>
    private void Welcome()
    {
        var s = AppSettings.Current;
        if (s.Welcomed || _smoke) return;
        // 這項功能出現之前就在用的人不用再看一次
        if (s.RoonTokens.Count > 0 || s.LyricsShift.Count > 0 || s.Todos.Count > 0) { s.Welcomed = true; s.Save(); return; }
        WelcomeCard();
    }

    private void WelcomeCard()
    {
        var card = new StackPanel { Margin = new Thickness(4, 6, 4, 2) };
        void Line(string glyph, string text)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 16, Width = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap });
            card.Children.Add(row);
        }
        Line("", L.T("用 Roon 的話，到 Roon「設定 → 擴充功能」啟用 Shima"));
        Line("", L.T("在島上按右鍵：換角色、校正歌詞、桌面歌詞"));
        Line("", ToggleHotkey.Length > 0 ? L.F("{0} 展開，Ctrl+Alt+V 問 AI", ToggleHotkey) : L.T("滑鼠移到島上就會展開"));
        var ok = new Button { Style = (Style)FindResource("Pill"), Height = 28, Padding = new Thickness(18, 0, 18, 0), HorizontalAlignment = HorizontalAlignment.Right, Content = L.T("知道了") };
        ok.Click += (_, _) =>
        {
            AppSettings.Current.Welcomed = true;
            AppSettings.Current.Save();
            WelcomeHost.Children.Clear();
            WelcomeHost.Visibility = Visibility.Collapsed;
            Morph();
        };
        card.Children.Add(ok);
        WelcomeHost.Children.Add(card);
        WelcomeHost.Visibility = Visibility.Visible;
        TabMusic.IsChecked = true;
        SetExpanded(true);
    }
}

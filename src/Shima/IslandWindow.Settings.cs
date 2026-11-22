// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

/// <summary>設定分頁與系統匣圖示。</summary>
public partial class IslandWindow
{
    private System.Windows.Forms.NotifyIcon? _tray;
    private readonly NotificationMirror _notifications = new();
    private bool _userHidden, _loadingSettings;

    private void InitSettings()
    {
        Closed += (_, _) => { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } };
        if (_smoke) return;
        _notifications.Arrived += (app, title, body) => Dispatcher.BeginInvoke(() => Toast(NotificationMirror.Line(app, title, body), 7));
        if (AppSettings.Current.Notifications) _ = _notifications.StartAsync();
        try
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("顯示／隱藏", null, (_, _) => ToggleHidden());
            menu.Items.Add("結束", null, (_, _) => Close());
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "Shima",
                Icon = Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : System.Drawing.SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true,
            };
            _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) ToggleHidden(); };
        }
        catch (Exception e) { Log.Write("tray: " + e.Message); }
    }

    private void ToggleHidden()
    {
        _userHidden = !_userHidden;
        Root.Visibility = _userHidden || _hiddenForFullscreen ? Visibility.Hidden : Visibility.Visible;
    }

    private void RefreshSettings()
    {
        var s = AppSettings.Current;
        _loadingSettings = true;
        StartupSwitch.IsChecked = Startup.Enabled;
        FullscreenSwitch.IsChecked = s.HideInFullscreen;
        AudioSwitch.IsChecked = s.AudioReactive;
        NotifySwitch.IsChecked = s.Notifications;
        DiscordBox.Text = s.DiscordClientId;
        FocusBox.Text = s.FocusMinutes.ToString();
        BreakBox.Text = s.BreakMinutes.ToString();
        _loadingSettings = false;

        // 歌詞來源：點一下開關，亮的才會問
        SourcesBox.Children.Clear();
        foreach (var name in LyricsFinder.Known.Keys)
        {
            bool on = s.LyricsProviders.Contains(name);
            var chip = new Button
            {
                Content = name switch { "local" => "自己的 .lrc", "lrclib" => "LRCLIB", "netease" => "網易雲", "qq" => "QQ 音樂", "kugou" => "酷狗", _ => name },
                Style = (Style)FindResource("Pill"), Height = 28, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(11, 0, 11, 0),
                Background = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
                Foreground = on ? Brushes.Black : Brushes.White,
            };
            chip.Click += (_, _) =>
            {
                if (!s.LyricsProviders.Remove(name)) s.LyricsProviders.Add(name);
                // 順序固定，照來源清單的順序
                s.LyricsProviders = LyricsFinder.Known.Keys.Where(s.LyricsProviders.Contains).ToList();
                s.Save();
                _lyrics = new LyricsFinder();
                RefreshSettings();
            };
            SourcesBox.Children.Add(chip);
        }

        FoldersBox.Children.Clear();
        foreach (var folder in s.LyricsFolders.ToList())
        {
            var chip = new Button { Content = folder, ToolTip = "按一下移除", Style = (Style)FindResource("Pill"), Height = 26, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 400 };
            chip.Click += (_, _) => { s.LyricsFolders.Remove(folder); s.Save(); _lyrics = new LyricsFinder(); RefreshSettings(); };
            FoldersBox.Children.Add(chip);
        }
        if (_tab == Tab.Settings && _expanded) Morph();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var s = AppSettings.Current;
        Startup.Enabled = StartupSwitch.IsChecked == true;
        s.HideInFullscreen = FullscreenSwitch.IsChecked == true;
        s.AudioReactive = AudioSwitch.IsChecked == true;
        s.Save();
    }

    private async void Notify_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        bool on = NotifySwitch.IsChecked == true;
        // 打開時 Windows 會問要不要允許；不允許就維持關閉
        if (on && !await _notifications.StartAsync()) { on = false; Toast("Windows 沒有允許通知存取", 4); }
        if (!on) _notifications.Stop();
        AppSettings.Current.Notifications = on;
        AppSettings.Current.Save();
        NotifySwitch.IsChecked = on;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "放 .lrc 歌詞檔的資料夾" };
        if (dialog.ShowDialog(this) != true) return;
        var s = AppSettings.Current;
        if (!s.LyricsFolders.Contains(dialog.FolderName)) s.LyricsFolders.Add(dialog.FolderName);
        s.Save();
        _lyrics = new LyricsFinder();
        RefreshSettings();
    }

    private void SettingsText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        var s = AppSettings.Current;
        var id = DiscordBox.Text.Trim();
        if (id != s.DiscordClientId && (id.Length == 0 || DiscordPresence.ValidId(id)))
        {
            s.DiscordClientId = id;
            _discord.Stop();
            _discord = new DiscordPresence(id);
            _discord.Start();
        }
        if (int.TryParse(FocusBox.Text, out int focus) && int.TryParse(BreakBox.Text, out int rest) &&
            (focus != s.FocusMinutes || rest != s.BreakMinutes))
        {
            s.FocusMinutes = Math.Clamp(focus, 1, 180);
            s.BreakMinutes = Math.Clamp(rest, 1, 60);
            _pomodoro = new Pomodoro { Focus = TimeSpan.FromMinutes(s.FocusMinutes), Break = TimeSpan.FromMinutes(s.BreakMinutes) };
        }
        s.Save();
        RefreshSettings();
    }
}

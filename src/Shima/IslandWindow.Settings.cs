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
            menu.Items.Add(L.T("顯示／隱藏"), null, (_, _) => ToggleHidden());
            menu.Items.Add(L.T("結束"), null, (_, _) => Close());
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
        SystemSwitch.IsChecked = s.SystemStatus;
        WaterSwitch.IsChecked = s.Water;
        WaterBox.Text = s.WaterMinutes.ToString();
        DiscordBox.Text = s.DiscordClientId;
        FocusBox.Text = s.FocusMinutes.ToString();
        BreakBox.Text = s.BreakMinutes.ToString();
        LanguageButton.Content = (Loc.Languages.FirstOrDefault(l => l.Code == s.Language).Name ?? L.T("跟著 Windows")) + "  \u25BE";
        _loadingSettings = false;

        // 歌詞來源：點一下開關，亮的才會問
        SourcesBox.Children.Clear();
        foreach (var name in LyricsFinder.Known.Keys)
        {
            bool on = s.LyricsProviders.Contains(name);
            var chip = new Button
            {
                Content = name switch { "local" => L.T("自己的 .lrc"), "lrclib" => "LRCLIB", "netease" => L.T("網易雲"), "qq" => L.T("QQ 音樂"), "kugou" => L.T("酷狗"), _ => name },
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
            var chip = new Button { Content = folder, ToolTip = L.T("按一下移除"), Style = (Style)FindResource("Pill"), Height = 26, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 400 };
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
        s.SystemStatus = SystemSwitch.IsChecked == true;
        if (s.Water != (WaterSwitch.IsChecked == true)) { s.Water = WaterSwitch.IsChecked == true; _waterAt = DateTime.Now.AddMinutes(s.WaterMinutes); }
        s.Save();
    }

    private async void Notify_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        bool on = NotifySwitch.IsChecked == true;
        // 打開時 Windows 會問要不要允許；不允許就維持關閉
        if (on && !await _notifications.StartAsync()) { on = false; Toast(L.T("Windows 沒有允許通知存取"), 4); }
        if (!on) _notifications.Stop();
        AppSettings.Current.Notifications = on;
        AppSettings.Current.Save();
        NotifySwitch.IsChecked = on;
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = LanguageButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (code, name) in Loc.Languages.Prepend(("", L.T("跟著 Windows"))))
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = code == AppSettings.Current.Language };
            item.Click += (_, _) =>
            {
                if (code == AppSettings.Current.Language) return;
                AppSettings.Current.Language = code;
                AppSettings.Current.Save();
                // 介面是啟動時翻譯的：真的換了語言才重新啟動
                if (Loc.Resolve(code) != Loc.Lang) ((App)Application.Current).Restart(); else RefreshSettings();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.T("放 .lrc 歌詞檔的資料夾") };
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
        if (int.TryParse(WaterBox.Text, out int water) && water != s.WaterMinutes)
        {
            s.WaterMinutes = Math.Clamp(water, 5, 480);
            _waterAt = DateTime.Now.AddMinutes(s.WaterMinutes);
        }
        s.Save();
        RefreshSettings();
    }

    // ---------------------------------------------------------------- 喝水提醒、系統狀態

    private readonly SystemWatch _system = new();
    private DateTime _waterAt = DateTime.Now.AddMinutes(Math.Clamp(AppSettings.Current.WaterMinutes, 5, 480));

    private void InitStatus()
    {
        if (_smoke) return;
        _system.Changed += line => Dispatcher.BeginInvoke(() => { if (AppSettings.Current.SystemStatus) Toast(line, 2.5, cheer: false); });
        _system.Start();
        Closed += (_, _) => _system.Dispose();
    }

    /// <summary>約每兩秒呼叫。</summary>
    private void PollStatus()
    {
        _system.Poll();
        var s = AppSettings.Current;
        if (!s.Water || DateTime.Now < _waterAt) return;
        _waterAt = DateTime.Now.AddMinutes(Math.Clamp(s.WaterMinutes, 5, 480));
        System.Media.SystemSounds.Asterisk.Play();
        Toast(L.T("該喝水了"), 10);
    }
}

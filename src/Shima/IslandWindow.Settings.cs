// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

/// <summary>設定分頁與系統匣圖示。</summary>
public partial class IslandWindow
{
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private System.Windows.Forms.ToolStripItem? _updateItem;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromSeconds(40) };
    private bool _updating;

    /// <summary>啟動後不久查一次，之後每天一次。有新版就提示，並在系統匣的選單多一項「更新到 x」。</summary>
    private void InitUpdates()
    {
        Updater.Cleanup();
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(24);
            if (!AppSettings.Current.CheckUpdates || _updateItem != null || await Updater.CheckAsync() is not { } found) return;
            if (_closed) return;   // 查的時候視窗已經關了，系統匣的選單也釋放了
            string version = found.Version.ToString(3);
            Log.Write("update: 有新版 " + version);
            Toast(L.F("有新版 {0}", version), 8);
            if (_trayMenu == null || !Updater.CanApply) return;
            _updateItem = new System.Windows.Forms.ToolStripMenuItem(L.F("更新到 {0}", version), null, async (_, _) =>
            {
                if (_updating) return;
                _updating = true;
                Toast(L.T("正在下載新版…"), 120, false);
                if (await Updater.ApplyAsync(found)) { ((App)Application.Current).Restart(); Close(); }
                else { _updating = false; Toast(L.T("更新失敗"), 6, false); }
            });
            _trayMenu.Items.Insert(0, _updateItem);
        };
        _updateTimer.Start();
        Closed += (_, _) => _updateTimer.Stop();
    }
    private readonly NotificationMirror _notifications = new();
    private bool _userHidden, _loadingSettings;

    private void InitSettings()
    {
        Closed += (_, _) => { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } };
        if (_smoke) return;
        InitUpdates();
        _notifications.Arrived += (app, title, body) => Dispatcher.BeginInvoke(() => { if (!InDoNotDisturb) Toast(NotificationMirror.Line(app, title, body), 7); });
        if (AppSettings.Current.Notifications) _ = _notifications.StartAsync();
        try
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            _trayMenu = menu;
            menu.Items.Add(L.T("顯示／隱藏"), null, (_, _) => ToggleHidden());
            var quiet = new System.Windows.Forms.ToolStripMenuItem(L.T("勿擾（1 小時）"), null, (_, _) => ToggleDoNotDisturb());
            menu.Items.Add(quiet);
            menu.Opening += (_, _) => quiet.Checked = InDoNotDisturb;
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

    /// <summary>勿擾中：喝水、通訊軟體通知、系統狀態先不顯示。權限請求、提醒、番茄鐘、換歌照常。</summary>
    private bool InDoNotDisturb => DoNotDisturb.IsActive(AppSettings.Current.DoNotDisturbUntil, DateTime.Now);

    private void ToggleDoNotDisturb()
    {
        AppSettings.Current.DoNotDisturbUntil = InDoNotDisturb ? null : DoNotDisturb.EndFrom(DateTime.Now);
        AppSettings.Current.Save();
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
        BuildMoreSettings();
        StartupSwitch.IsChecked = Startup.Enabled;
        DeckSwitch.Visibility = Agents.StreamDock.Available ? Visibility.Visible : Visibility.Collapsed;
        DeckSwitch.IsChecked = Agents.StreamDock.Installed;
        FullscreenSwitch.IsChecked = s.HideInFullscreen;
        AudioSwitch.IsChecked = s.AudioReactive;
        NotifySwitch.IsChecked = s.Notifications;
        SystemSwitch.IsChecked = s.SystemStatus;
        UpdateSwitch.IsChecked = s.CheckUpdates;
        DictationSwitch.IsChecked = s.SpeechEngine == "windows";
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

    private void Deck_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        if (DeckSwitch.IsChecked == true)
        {
            DeckSwitch.IsChecked = Agents.StreamDock.Install();
            if (DeckSwitch.IsChecked == true) Toast(L.T("重新開啟 Stream Dock 後，把 Shima 拖到按鍵上"), 6);
        }
        else Agents.StreamDock.Remove();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        CommitSettingsText(WaterBox);
        var s = AppSettings.Current;
        Startup.Enabled = StartupSwitch.IsChecked == true;
        s.HideInFullscreen = FullscreenSwitch.IsChecked == true;
        s.AudioReactive = AudioSwitch.IsChecked == true;
        s.SystemStatus = SystemSwitch.IsChecked == true;
        s.CheckUpdates = UpdateSwitch.IsChecked == true;
        s.SpeechEngine = DictationSwitch.IsChecked == true ? "windows" : "whisper";
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

    /// <summary>開關、按鈕與分頁都設成 Focusable=False，點它們時文字框不會失去焦點；這裡主動放開，讓設定輸入框立刻存檔。</summary>
    private void Panel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox) return;
        if (!InsideTextInput(e.OriginalSource as DependencyObject)) Keyboard.ClearFocus();
    }

    private static bool InsideTextInput(DependencyObject? source)
    {
        for (var d = source; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TextBox or PasswordBox) return true;
        return false;
    }

    private void SettingsText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitSettingsText(sender as TextBox);

    private void SettingsText_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Enter) { CommitSettingsText(box); Keyboard.ClearFocus(); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            var s = AppSettings.Current;
            box.Text = box == WaterBox ? s.WaterMinutes.ToString()
                : box == FocusBox ? s.FocusMinutes.ToString()
                : box == BreakBox ? s.BreakMinutes.ToString()
                : s.DiscordClientId;
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void CommitSettingsText(TextBox? box = null)
    {
        if (_loadingSettings) return;
        var s = AppSettings.Current;
        bool dirty = false;

        if (box == null || box == DiscordBox)
        {
            var id = DiscordBox.Text.Trim();
            if (id != s.DiscordClientId && (id.Length == 0 || DiscordPresence.ValidId(id)))
            {
                s.DiscordClientId = id;
                _discord.Stop();
                _discord = new DiscordPresence(id);
                _discord.Start();
                dirty = true;
            }
            if (DiscordBox.Text != s.DiscordClientId) DiscordBox.Text = s.DiscordClientId;
        }

        if (box == null || box == FocusBox || box == BreakBox)
        {
            int focus = box == null || box == FocusBox ? AppSettings.ParseMinutes(FocusBox.Text, s.FocusMinutes, 1, 180) : s.FocusMinutes;
            int rest = box == null || box == BreakBox ? AppSettings.ParseMinutes(BreakBox.Text, s.BreakMinutes, 1, 60) : s.BreakMinutes;
            if (focus != s.FocusMinutes || rest != s.BreakMinutes)
            {
                s.FocusMinutes = focus;
                s.BreakMinutes = rest;
                _pomodoro = new Pomodoro { Focus = TimeSpan.FromMinutes(s.FocusMinutes), Break = TimeSpan.FromMinutes(s.BreakMinutes) };
                dirty = true;
            }
            if ((box == null || box == FocusBox) && FocusBox.Text != s.FocusMinutes.ToString()) FocusBox.Text = s.FocusMinutes.ToString();
            if ((box == null || box == BreakBox) && BreakBox.Text != s.BreakMinutes.ToString()) BreakBox.Text = s.BreakMinutes.ToString();
        }

        if (box == null || box == WaterBox)
        {
            int water = AppSettings.ParseMinutes(WaterBox.Text, s.WaterMinutes, 5, 480);
            if (water != s.WaterMinutes)
            {
                s.WaterMinutes = water;
                _waterAt = DateTime.Now.AddMinutes(s.WaterMinutes);
                dirty = true;
            }
            if (WaterBox.Text != s.WaterMinutes.ToString()) WaterBox.Text = s.WaterMinutes.ToString();
        }

        if (dirty) s.Save();
    }

    // ---------------------------------------------------------------- 喝水提醒、系統狀態

    private readonly SystemWatch _system = new();
    private DateTime _waterAt = DateTime.Now.AddMinutes(Math.Clamp(AppSettings.Current.WaterMinutes, 5, 480));

    private void InitStatus()
    {
        if (_smoke) return;
        _system.Changed += line => Dispatcher.BeginInvoke(() => { if (AppSettings.Current.SystemStatus && !InDoNotDisturb) Toast(line, 2.5, cheer: false); });
        _system.Start();
        InitAudioOutputs();
        Closed += (_, _) => _system.Dispose();
    }

    /// <summary>約每兩秒呼叫。</summary>
    private void PollStatus()
    {
        _system.Poll();
        UpdateAudioButton();
        var s = AppSettings.Current;
        if (!s.Water || DateTime.Now < _waterAt) return;
        _waterAt = DateTime.Now.AddMinutes(Math.Clamp(s.WaterMinutes, 5, 480));
        if (InDoNotDisturb) return; // 勿擾期間到期的直接略過，結束後不補發
        System.Media.SystemSounds.Asterisk.Play();
        Toast(L.T("該喝水了"), 10);
    }
}

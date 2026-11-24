// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Lyrics;

namespace Shima;

// 設定頁：上面一排切換「一般／外觀／音樂／AI 與工具」，每一頁的列都在這裡用 Toggle／Choice／Entry／Heading 組出來。
public partial class IslandWindow
{
    private enum SettingsSection { General, Look, Music, Ai }

    /// <summary>上次看的子分頁。只記在這次執行裡：下次啟動從「一般」開始。</summary>
    private SettingsSection _settingsSection = SettingsSection.General;
    private readonly Dictionary<SettingsSection, StackPanel> _settingsPages = [];

    // 設定頁裡要被其他方法讀寫的控制項（CommitSettingsText、Notify_Click、Deck_Click、Language_Click 會用到）。
    // 每次 RefreshSettings 都會重建，所以只在設定頁開著的時候有意義。
    private Button LanguageButton = null!;
    private CheckBox NotifySwitch = null!, DeckSwitch = null!, DeckAllowSwitch = null!;
    private TextBox WaterBox = null!, DiscordBox = null!, FocusBox = null!, BreakBox = null!;

    /// <summary>打開設定頁、或列的內容變了（歌詞來源、資料夾、語言）時重建四個子分頁，顯示目前那一頁。</summary>
    private void RefreshSettings()
    {
        if (_loadingSettings) return;
        // 正在打字的輸入框會被移走：先放開焦點讓它存檔，數字才不會跟著舊的列一起消失
        if (Keyboard.FocusedElement is Visual focused && SettingsRows.IsAncestorOf(focused)) Keyboard.ClearFocus();
        _loadingSettings = true;
        try
        {
            if (SettingsSections.Children.Count == 0) BuildSettingsSwitcher();
            SettingsRows.Children.Clear();
            _settingsPages.Clear();
            foreach (var (section, build) in new (SettingsSection, Action<UIElementCollection>)[]
            {
                (SettingsSection.General, BuildGeneralSettings),
                (SettingsSection.Look, BuildLookSettings),
                (SettingsSection.Music, BuildMusicSettings),
                (SettingsSection.Ai, BuildAiSettings),
            })
            {
                var page = new StackPanel();
                build(page.Children);
                _settingsPages[section] = page;
                SettingsRows.Children.Add(page);
            }
        }
        finally { _loadingSettings = false; }
        ShowSettingsSection();
    }

    /// <summary>切換膠囊：跟聊天分頁選網站的那排一樣，選中的是白底黑字。</summary>
    private void BuildSettingsSwitcher()
    {
        foreach (var (section, name) in new[]
        {
            (SettingsSection.General, L.T("一般")),
            (SettingsSection.Look, L.T("外觀")),
            (SettingsSection.Music, L.T("音樂")),
            (SettingsSection.Ai, L.T("AI 與工具")),
        })
        {
            var button = new Button { Content = name, Tag = section, Style = (Style)FindResource("Pill"), Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(3, 0, 3, 0) };
            button.Click += (_, _) =>
            {
                if (_settingsSection == section) return;
                _settingsSection = section;
                SettingsScroll.ScrollToVerticalOffset(0);
                ShowSettingsSection();
            };
            SettingsSections.Children.Add(button);
        }
    }

    private void ShowSettingsSection()
    {
        foreach (var (section, page) in _settingsPages) page.Visibility = section == _settingsSection ? Visibility.Visible : Visibility.Collapsed;
        foreach (Button b in SettingsSections.Children)
        {
            bool on = (SettingsSection)b.Tag == _settingsSection;
            b.Background = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            b.Foreground = on ? Brushes.Black : Brushes.White;
        }
        if (_tab == Tab.Settings && _expanded) Morph();
    }

    /// <summary>每一頁平常都放得下；真的超出 540 時，底部淡出，看得出下面還有。捲到底就不淡。</summary>
    private void SettingsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var view = SettingsScroll;
        if (!MoreBelow(view.VerticalOffset, view.ScrollableHeight)) { view.OpacityMask = null; return; }
        double h = view.ViewportHeight;
        // 用絕對座標，淡出的高度固定 36，不會跟著頁面長短變
        var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, Math.Max(0, h - 36)), EndPoint = new Point(0, h) };
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        fade.Freeze();
        view.OpacityMask = fade;
    }

    /// <summary>下面還有沒捲到的內容。留半個像素的餘地：版面四捨五入後常差一點點，不能因此一直淡著。</summary>
    public static bool MoreBelow(double offset, double scrollable) => scrollable > 0.5 && offset < scrollable - 0.5;

    // ---------------------------------------------------------------- 四個子分頁

    private void BuildGeneralSettings(UIElementCollection rows)
    {
        var s = AppSettings.Current;
        LanguageButton = new Button
        {
            Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0),
            Content = (Loc.Languages.FirstOrDefault(l => l.Code == s.Language).Name ?? L.T("跟著 Windows")) + "  ▾",
        };
        LanguageButton.Click += Language_Click;
        rows.Add(SettingsRow("語言", LanguageButton));
        rows.Add(Toggle("開機時啟動", Startup.Enabled, v => Startup.Enabled = v));
        rows.Add(Toggle("全螢幕時隱藏", s.HideInFullscreen, v => s.HideInFullscreen = v));
        rows.Add(Toggle("檢查新版", s.CheckUpdates, v => s.CheckUpdates = v));
        rows.Add(Gap());
        rows.Add(Toggle("顯示音量、電量、網路的變化", s.SystemStatus, v => s.SystemStatus = v));
        // 通知要先問 Windows、Stream Dock 要裝外掛：開關的結果不一定是點下去的那個，各自有處理的方法
        rows.Add(NotifySwitch = SettingsSwitch("顯示訊息通知", s.Notifications, Notify_Click));
        DeckSwitch = SettingsSwitch("Stream Dock", Agents.StreamDock.Installed, Deck_Click);
        if (Agents.StreamDock.Available) rows.Add(DeckSwitch);
        // 只在 Stream Dock 開著時出現：開關跟著 Deck_Click 一起顯示、收起
        DeckAllowSwitch = Toggle("Stream Dock 可以按允許", s.DeckAllow, v => s.DeckAllow = v);
        DeckAllowSwitch.Visibility = DeckSwitch.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (Agents.StreamDock.Available) rows.Add(DeckAllowSwitch);

        rows.Add(Gap());
        foreach (var row in HotkeySettingsRows()) rows.Add(row);

        rows.Add(Gap());
        rows.Add(BackupRow());

        // —— 其他功能的設定列接在這裡（一般）——
    }

    private void BuildLookSettings(UIElementCollection rows)
    {
        var s = AppSettings.Current;
        rows.Add(Choice("底色", [("1", "不透明"), ("0.85", "85%"), ("0.7", "70%"), ("0.55", "55%")],
            () => s.Opacity >= 0.95 ? "1" : s.Opacity >= 0.8 ? "0.85" : s.Opacity >= 0.65 ? "0.7" : "0.55",
            v => { s.Opacity = double.Parse(v, CultureInfo.InvariantCulture); ApplyTheme(); }));
        rows.Add(AccentRow());
        rows.Add(Choice("大小", [("0.9", "90%"), ("1", "100%"), ("1.15", "115%"), ("1.3", "130%")],
            () => IslandScale.Nearest(s.Scale).ToString(CultureInfo.InvariantCulture),
            v => { s.Scale = double.Parse(v, CultureInfo.InvariantCulture); ApplyScale(); }));

        // 膠囊
        rows.Add(Gap());
        rows.Add(CharacterImageRow());
        rows.Add(Toggle("跟著聲音動", s.AudioReactive, v => s.AudioReactive = v));
        rows.Add(Toggle("膠囊顯示進度", s.PillProgress, v => { s.PillProgress = v; UpdatePillProgress(); }));
        rows.Add(Choice("沒在播歌時顯示", [("clock", "時間"), ("date", "日期與時間"), ("alarm", "下一個提醒"), ("none", "不顯示")],
            () => IdleLine.Normalize(s.IdleShows), v => { s.IdleShows = v; UpdateTexts(); }));

        // 歌詞與換歌
        rows.Add(Gap());
        rows.Add(Choice("歌詞行數", [("3", "少"), ("5", "中"), ("7", "多")],
            () => s.LyricsLines <= 3 ? "3" : s.LyricsLines >= 7 ? "7" : "5",
            v => { s.LyricsLines = int.Parse(v, CultureInfo.InvariantCulture); ApplyLyricsLines(); }));
        rows.Add(Toggle("全螢幕時換歌提示", s.FullscreenToast, v => s.FullscreenToast = v));

        // —— 其他功能的設定列接在這裡（外觀）——
    }

    private void BuildMusicSettings(UIElementCollection rows)
    {
        var s = AppSettings.Current;
        rows.Add(Toggle("桌面歌詞", s.DesktopLyrics, v => { s.DesktopLyrics = v; ApplyDesktopLyrics(); }));

        // 歌詞來源：點一下開關，亮的才會問
        rows.Add(Heading("歌詞來源"));
        var sources = new WrapPanel();
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
            sources.Children.Add(chip);
        }
        rows.Add(sources);

        var add = new Button { Style = (Style)FindResource("Pill"), Height = 24, Padding = new Thickness(10, 0, 10, 0), Content = L.T("加入") };
        add.Click += AddFolder_Click;
        DockPanel.SetDock(add, Dock.Right);
        var folders = new DockPanel { Margin = new Thickness(2, 6, 0, 6) };
        folders.Children.Add(add);
        folders.Children.Add(new TextBlock { Text = L.T("歌詞資料夾"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center });
        rows.Add(folders);
        foreach (var folder in s.LyricsFolders.ToList())
        {
            var chip = new Button { Content = folder, ToolTip = L.T("按一下移除"), Style = (Style)FindResource("Pill"), Height = 26, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 400 };
            chip.Click += (_, _) => { s.LyricsFolders.Remove(folder); s.Save(); _lyrics = new LyricsFinder(); RefreshSettings(); };
            rows.Add(chip);
        }

        rows.Add(Gap());
        rows.Add(Entry("Roon", s.RoonHost, 190, v => { if (v != s.RoonHost) { s.RoonHost = v; Toast(L.T("重新啟動後生效"), 4, false); } }));
        rows.Add(Toggle("Roon 搜尋", s.RoonSearch, v => { s.RoonSearch = v; Toast(L.T("重新啟動 Shima，再到 Roon「設定 → 擴充功能」啟用一次"), 10, false); }));
        rows.Add(Gap());
        rows.Add(Toggle("記下聽了什麼", s.History, v => s.History = v));
        if (HistorySourceChips(s) is { } sourceChips) rows.Add(sourceChips);
        // 清空才算中斷（還沒送的一併丟掉）；只是把填錯的 token 改對，等著的那些要留著補送
        rows.Add(Entry("ListenBrainz", s.ListenBrainzTokenText(), 190, v => { if (v.Length == 0) _pending.Clear("listenbrainz"); s.SetListenBrainzToken(v); }));
        foreach (var row in LastFmSettingsRows()) rows.Add(row);

        rows.Add(Gap());
        rows.Add(SettingsRow("Discord Application ID", DiscordBox = SettingsBox(s.DiscordClientId, 190, 26)));
        rows.Add(Toggle("Discord 顯示封面", s.DiscordCover, v => s.DiscordCover = v));

        // —— 其他功能的設定列接在這裡（音樂）——
    }

    private void BuildAiSettings(UIElementCollection rows)
    {
        var s = AppSettings.Current;
        rows.Add(Toggle("語音辨識用 Windows 的聽寫", s.SpeechEngine == "windows", v => s.SpeechEngine = v ? "windows" : "whisper"));
        rows.Add(Choice("辨識模型", [("tiny", "tiny"), ("base", "base"), ("small", "small"), ("medium", "medium")], () => s.WhisperModel, v => s.WhisperModel = v));
        rows.Add(Toggle("聽的時候暫停音樂", s.PauseWhileListening, v => s.PauseWhileListening = v));
        rows.Add(Toggle("一直對話到再按一次", s.VoiceContinuous, v => s.VoiceContinuous = v));
        rows.Add(Toggle("說「Shima」就開始聽", s.WakeWord, v => { s.WakeWord = v; ApplyWakeWord(); }));

        rows.Add(Gap());
        // 喝水提醒：開關，右邊是幾分鐘一次
        WaterBox = SettingsBox(s.WaterMinutes.ToString(), 52, 26);
        WaterBox.Margin = new Thickness(8, 0, 0, 0);
        var unit = new TextBlock { Text = L.T("分鐘"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        var water = Toggle("喝水提醒", s.Water, v =>
        {
            CommitSettingsText(WaterBox);
            if (s.Water != v) { s.Water = v; _waterAt = DateTime.Now.AddMinutes(s.WaterMinutes); }
        });
        DockPanel.SetDock(unit, Dock.Right);
        DockPanel.SetDock(WaterBox, Dock.Right);
        var waterRow = new DockPanel { Height = 32 };
        waterRow.Children.Add(unit);
        waterRow.Children.Add(WaterBox);
        waterRow.Children.Add(water);
        rows.Add(waterRow);

        // 番茄鐘：專注／休息各幾分鐘，排成跟喝水提醒一樣的一列
        FocusBox = SettingsBox(s.FocusMinutes.ToString(), 52, 26);
        FocusBox.ToolTip = L.T("專注");
        BreakBox = SettingsBox(s.BreakMinutes.ToString(), 52, 26);
        BreakBox.ToolTip = L.T("休息");
        var dim = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255));
        var slash = new TextBlock { Text = "／", FontSize = 12, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) };
        var minutes = new TextBlock { Text = L.T("分鐘"), FontSize = 12, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
        var pomodoro = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        foreach (var part in new FrameworkElement[] { minutes, BreakBox, slash, FocusBox })
        {
            DockPanel.SetDock(part, Dock.Right);
            pomodoro.Children.Add(part);
        }
        pomodoro.Children.Add(new TextBlock { Text = L.T("番茄鐘"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) });
        rows.Add(pomodoro);

        // —— 其他功能的設定列接在這裡（AI 與工具）——
    }

    // ---------------------------------------------------------------- 只有這一頁用的小零件

    /// <summary>兩組設定之間的空隙：用距離分組，不另外放小標題。</summary>
    private static Border Gap() => new() { Height = 10 };

    /// <summary>一列：左邊文字，右邊一個控制項（語言按鈕、輸入框）。</summary>
    private static DockPanel SettingsRow(string text, FrameworkElement right)
    {
        DockPanel.SetDock(right, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(right);
        row.Children.Add(new TextBlock { Text = L.T(text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) });
        return row;
    }

    /// <summary>不經過 Toggle 的開關：點下去之後要自己決定結果（例如 Windows 不允許通知時維持關閉）。</summary>
    private CheckBox SettingsSwitch(string text, bool value, RoutedEventHandler click)
    {
        var box = new CheckBox { Style = (Style)FindResource("Switch"), Content = L.T(text), IsChecked = value };
        box.Click += click;
        return box;
    }

    /// <summary>數字與 Discord ID 的輸入框：Enter 存、Esc 還原、失焦存（CommitSettingsText 會檢查範圍）。</summary>
    private TextBox SettingsBox(string value, double width, double height)
    {
        var box = new TextBox { Style = (Style)FindResource("Input"), Height = height, Width = width, Text = value };
        box.LostKeyboardFocus += SettingsText_LostKeyboardFocus;
        box.KeyDown += SettingsText_KeyDown;
        return box;
    }
}

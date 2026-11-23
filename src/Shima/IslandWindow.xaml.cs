// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Shima.Lyrics;
using Shima.Motion;
using Shima.Playback;

namespace Shima;

/// <summary>
/// 島：一個透明、置頂、不搶焦點的視窗，裡面一塊黑色圓角。
/// 平常是一顆膠囊，滑鼠移上去展開成播放面板；寬與高各由一條彈簧帶動。
/// </summary>
public partial class IslandWindow : Window
{
    private const double PillHeight = 36, IdleWidth = 96, MinPill = 170, MaxPill = 500, BaseWindowHeight = 312;

    private enum Tab { Music, Chat, Tools, Ai, Settings, Ask }
    private Tab _tab = Tab.Music;

    private readonly bool _smoke;
    private readonly MediaHub _media = new();
    private LyricsFinder _lyrics = new();
    private readonly AudioMeter _meter = new();
    private DiscordPresence _discord = new(AppSettings.Current.DiscordClientId);
    private ImageSource? _coverShown;
    private readonly Spring _w = new(IdleWidth), _h = new(PillHeight);
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _open = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _close = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private Track? _track;
    private LyricSheet? _sheet;
    private string _trackKey = "";
    private bool _expanded, _rendering, _hiddenForFullscreen;
    private TimeSpan _lastFrame;
    private int _ticks, _lyricIndex = int.MinValue;
    private double _pillWidth = IdleWidth;
    private IntPtr _hwnd;

    public IslandWindow(bool smoke, string? shotFolder = null, List<string>? asks = null)
    {
        _smoke = smoke;
        InitializeComponent();
        FontFamily = Loc.Font;
        Loc.Translate(this);

        Place();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (!_smoke) InitHotkeys();
            // 不出現在 Alt+Tab
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        Deactivated += (_, _) => { if (_expanded && _tab is Tab.Chat or Tab.Tools or Tab.Ai or Tab.Settings && !Host.IsMouseOver) SetExpanded(false); };
        InitChat();
        InitTools();
        InitSettings();
        InitStatus();
        InitAgents();
        Loaded += async (_, _) =>
        {
            _media.Changed += () => Dispatcher.BeginInvoke(Refresh);
            if (!_smoke) _discord.Start();
            await _media.StartAsync();
            Refresh();
            _tick.Start();
            if (shotFolder != null) { await ShootAsync(shotFolder); return; }
            if (asks is { Count: > 0 })
            {
                await Task.Delay(7000);
                Refresh();
                foreach (var question in asks)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    await AskAsync(question);
                    Log.Write($"ask-test（{clock.Elapsed.TotalSeconds:0.0}s）問「{question}」→ 答「{VoiceText.Text.Replace('\n', ' ')}」");
                }
                Close();
                return;
            }
            if (_smoke)
            {
                await Task.Delay(2500);
                Log.Write($"smoke ok: track={(_track == null ? "none" : _track.Source + " / " + _track.Title)} island={Host.Width:0}x{Host.Height:0}");
                Close();
            }
        };

        Buddy.Kind = AppSettings.Current.Character;
        Buddy.Level = () => _meter.Running ? _meter.Level : 0.35f;
        Meter.Level = () => _micOpen ? Agents.Ears.Level : _meter.Running ? _meter.Level : 0.35f;
        Closed += (_, _) => _meter.Dispose();
        foreach (var (id, name) in Mascot.All)
        {
            var item = new System.Windows.Controls.MenuItem { Header = L.T(name), IsCheckable = true, IsChecked = id == Buddy.Kind, Tag = id };
            item.Click += (_, _) =>
            {
                Buddy.Kind = AppSettings.Current.Character = id;
                AppSettings.Current.Save();
                foreach (System.Windows.Controls.MenuItem other in CharacterMenu.Items) other.IsChecked = (string)other.Tag == id;
                _pillWidth = 0;   // 角色佔的寬度變了，重新量
                UpdateTexts();
            };
            CharacterMenu.Items.Add(item);
        }
        foreach (var (id, name) in ((string, string)[])[("translation", "中文翻譯"), ("romaji", "羅馬拼音"), ("none", "不顯示")])
        {
            var item = new System.Windows.Controls.MenuItem { Header = L.T(name), IsCheckable = true, IsChecked = id == AppSettings.Current.LyricsSecondLine, Tag = id };
            item.Click += (_, _) =>
            {
                AppSettings.Current.LyricsSecondLine = id;
                AppSettings.Current.Save();
                foreach (System.Windows.Controls.MenuItem other in SecondLineMenu.Items) other.IsChecked = (string)other.Tag == id;
                // 換一種第二行：這首歌重新查一次
                _trackKey = "";
                Refresh();
            };
            SecondLineMenu.Items.Add(item);
        }
        LyricsList.Seek += time => _media.Seek(time);
        PillLine.Clock = LyricsList.Clock = () => _track is { } t ? (t.Now + LyricLead + _lyricShift, t.Playing) : (TimeSpan.Zero, false);
        _tick.Tick += (_, _) => Tick();
        _open.Tick += (_, _) => { _open.Stop(); SetExpanded(true); };
        _close.Tick += (_, _) =>
        {
            _close.Stop();
            if (Host.IsMouseOver || Host.ContextMenu?.IsOpen == true || _tab == Tab.Ask) return;
            // 正在打字的分頁不因為滑鼠離開就收起來，點到別的視窗才收
            if (_tab is Tab.Chat or Tab.Tools or Tab.Ai or Tab.Settings && IsActive) return;
            SetExpanded(false);
        };
    }

    // ---------------------------------------------------------------- 內容

    private void Refresh()
    {
        var track = _media.Current;
        _track = track;
        bool has = track != null;

        if (has)
        {
            TitleText.Text = track!.Title.Length > 0 ? track.Title : "—";
            TitleText.FontFamily = ArtistText.FontFamily = Fonts.For(Fonts.HasKana(track.Title + track.Artist + track.Album));
            ArtistText.Text = track.Artist;
            SourceText.Text = _media.ZoneName is { Length: > 0 } zone ? $"{track.Source} · {zone}" : track.Source;
            PlayButton.Content = track.Playing ? "" : "";
            Brush cover = track.Cover != null ? new ImageBrush(track.Cover) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            Cover.Background = PillCover.Background = cover;
        }
        else
        {
            TitleText.Text = L.T("沒有正在播放的音樂");
            ArtistText.Text = SourceText.Text = Elapsed.Text = Remaining.Text = "";
            BarFill.Width = 0;
            Cover.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        }
        PillCover.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        Pill.Margin = has ? new Thickness(7, 0, 8, 0) : new Thickness(12, 0, 8, 0);
        Buddy.Dancing = track?.Playing == true;
        _discord.Update(track);
        if (!ReferenceEquals(track?.Cover, _coverShown))
        {
            _coverShown = track?.Cover;
            var accent = Accent.From(_coverShown);
            PillLine.Light = LyricsList.Light = Meter.Color = accent;
            BarFill.Background = new SolidColorBrush(accent);
            BackdropImage.Background = Accent.Blurred(_coverShown) is { } blur ? new ImageBrush(blur) { Stretch = Stretch.UniformToFill } : null;
        }

        var key = track?.Key ?? "";
        if (key != _trackKey)
        {
            if (_trackKey.Length > 0 && has) Buddy.Cheer();
            _trackKey = key;
            _lyricShift = has && AppSettings.Current.LyricsShift.TryGetValue(LyricsFinder.IdOf(track!), out int ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
            _sheet = null;
            _lyricIndex = int.MinValue;
            if (has) _ = LoadLyricsAsync(track!, key);
        }

        UpdateTexts();
        Morph();
    }

    private async Task LoadLyricsAsync(Track track, string key)
    {
        var hit = await _lyrics.FindAsync(track);
        Log.Write($"歌詞: {track.Title} → {(hit == null ? "找不到" : hit.Provider + (hit.Exact ? "" : "（原曲，版本不同）"))}");
        if (key != _trackKey) return;   // 查的時候已經換歌了
        _sheet = hit?.Sheet;
        _lyricsFrom = hit?.Provider ?? "";
        _lyricIndex = int.MinValue;
        UpdateTexts();
        Morph();
    }

    private void Tick()
    {
        _ticks++;
        if (_ticks % 10 == 0)
        {
            _media.Poll();
            WatchFullscreen();
            _discord.Update(_track);   // 拖了進度的話這裡會發現
            _ = _notifications.PollAsync();
            if (_ticks % 20 == 0) PollStatus();
            // 只在真的有歌在播、島也看得到的時候才擷取聲音
            bool listen = AppSettings.Current.AudioReactive && _track?.Playing == true && !_hiddenForFullscreen;
            if (listen && !_meter.Running) _meter.Start(); else if (!listen && _meter.Running) _meter.Stop();
            Meter.Visibility = listen || _micOpen ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdateTimer();
        UpdateTexts();
    }

    /// <summary>歌詞比聲音早一點點亮，看起來才是跟著唱。</summary>
    private static readonly TimeSpan LyricLead = TimeSpan.FromMilliseconds(180);

    /// <summary>島上音樂以外正在發生的事，給 Stream Dock 的長條。</summary>
    private void UpdateNotice()
    {
        Agents.DeckNotice notice;
        string timer = TimerLine() ?? "";
        if (_asking is { } ask && !ask.Decision.Task.IsCompleted)
            notice = new("ask", ask.Project.Length > 0 ? $"Claude Code · {ask.Project}" : "Claude Code", $"{ask.Tool}  {ask.Details}", timer, _planLine, false);
        else if (_voice != null)
            notice = new("voice", "", _voiceLine, timer, _planLine, _micOpen);
        else if (_toast.Length > 0 && DateTime.Now < _toastUntil)
            notice = new("toast", "", _toast, timer, _planLine, false);
        else
            notice = new("", "", "", timer, _planLine, false);
        if (notice != _bridge.Notice) _bridge.Notice = notice;
    }

    private void UpdateTexts()
    {
        UpdateNotice();
        var track = _track;
        // 語音助手正在聽或回答：優先顯示，音量條換成麥克風的音量
        if (_voice != null && _voiceLine.Length > 0)
        {
            Meter.Visibility = _micOpen ? Visibility.Visible : Visibility.Collapsed;
            Pill_Say(_voiceLine);
            return;
        }
        if (_toast.Length > 0 && DateTime.Now < _toastUntil) { Pill_Say(_toast); return; }
        _toast = "";
        if (track == null)
        {
            if (_bridge.Deck.Track != null) _bridge.Deck = new Agents.DeckState(null, null, null, TimeSpan.Zero);
            PillLine.Say(TimerLine() ?? DateTime.Now.ToString("HH:mm"));
            double idle = Math.Max(IdleWidth, Math.Ceiling(PillLine.TextWidth) + 40) + (Buddy.Kind == "none" ? 0 : 30);
            if (Math.Abs(idle - _pillWidth) > 0.5) { _pillWidth = idle; Morph(); }
            return;
        }

        var pos = track.Now;
        bool timed = track.Duration > TimeSpan.Zero;
        Elapsed.Text = timed ? Clock(pos) : "";
        Remaining.Text = timed ? "-" + Clock(track.Duration - pos) : "";
        BarFill.Width = timed && Bar.ActualWidth > 0 ? Bar.ActualWidth * Math.Clamp(pos / track.Duration, 0, 1) : 0;

        var name = track.Artist.Length > 0 ? $"{track.Title} — {track.Artist}" : track.Title;
        if (_sheet is { Synced: true } sheet)
        {
            int i = Lrc.LineAt(sheet.Lines, pos + LyricLead + _lyricShift);
            string text = i >= 0 ? sheet.Lines[i].Text : "";
            _lyricIndex = i;
            if (text.Length > 0)
            {
                var start = sheet.Lines[i].Time;
                var length = Singing.Length(text, start, i + 1 < sheet.Lines.Count ? sheet.Lines[i + 1].Time : null);
                // 暫停時膠囊回到歌名，繼續播才換回歌詞
                if (track.Playing) PillLine.Sing(text, start, length); else PillLine.Say(name);
            }
            else PillLine.Say(name);
        }
        else PillLine.Say(name);

        var shown = _sheet is { Synced: true } ? _sheet : null;
        var second = AppSettings.Current.LyricsSecondLine switch
        {
            "translation" => shown?.Translation ?? shown?.Romaji,
            "romaji" => shown?.Romaji ?? shown?.Translation,
            _ => null,
        };
        LyricsList.Show(shown, second);
        var deck = _bridge.Deck;
        if (!ReferenceEquals(deck.Track, track) || !ReferenceEquals(deck.Sheet, shown) || !ReferenceEquals(deck.Second, second) || deck.Shift != LyricLead + _lyricShift)
            _bridge.Deck = new Agents.DeckState(track, shown, second, LyricLead + _lyricShift);
        LyricsList.Active = _expanded && _tab == Tab.Music;
        LyricsList.Wake();

        // 膠囊跟著這一行字的長短伸縮
        double buddy = (Buddy.Kind == "none" ? 8 : 32) + (Meter.Visibility == Visibility.Visible ? 21 : 0);
        double wanted = Math.Clamp(Math.Ceiling(PillLine.TextWidth) + 7 + 22 + 9 + 8 + buddy, MinPill, MaxPill);
        if (Math.Abs(wanted - _pillWidth) > 0.5) { _pillWidth = wanted; if (!_expanded) Morph(); }
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---------------------------------------------------------------- 形狀

    private bool HasLyrics => _sheet is { Synced: true };
    private string _toast = "";
    private DateTime _toastUntil;

    /// <summary>膠囊上暫時顯示一句話（例如「Claude Code 完成了」）。</summary>
    private void Toast(string text, double seconds = 6, bool cheer = true)
    {
        _toast = text;
        _toastUntil = DateTime.Now.AddSeconds(seconds);
        if (cheer) Buddy.Cheer();
        UpdateTexts();
    }

    private void Pill_Say(string text)
    {
        PillLine.Say(text);
        double wanted = Math.Clamp(Math.Ceiling(PillLine.TextWidth) + 7 + (_track != null ? 31 : 5) + 8 + (Buddy.Kind == "none" ? 8 : 32) + (Meter.Visibility == Visibility.Visible ? 21 : 0), MinPill, MaxPill);
        if (Math.Abs(wanted - _pillWidth) > 0.5) { _pillWidth = wanted; Morph(); }
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (!expanded && _tab == Tab.Ask) return;
        if (expanded && _tab == Tab.Chat) _ = EnsureChatAsync();
        Morph();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ShowTab(sender == TabChat ? Tab.Chat : sender == TabTools ? Tab.Tools : sender == TabAi ? Tab.Ai : sender == TabSettings ? Tab.Settings : Tab.Music);
    }

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        if (tab == Tab.Chat && _expanded) _ = EnsureChatAsync();
        if (tab == Tab.Ai) RefreshAi();
        if (tab == Tab.Settings) RefreshSettings();
        Morph();
    }

    /// <summary>展開時的大小：寬度看分頁，高度量內容實際需要多少。</summary>
    private Size OpenSize()
    {
        MusicPage.Visibility = _tab == Tab.Music ? Visibility.Visible : Visibility.Collapsed;
        ChatPage.Visibility = _tab == Tab.Chat ? Visibility.Visible : Visibility.Collapsed;
        AiPage.Visibility = _tab == Tab.Ai ? Visibility.Visible : Visibility.Collapsed;
        ToolsPage.Visibility = _tab == Tab.Tools ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = _tab == Tab.Settings ? Visibility.Visible : Visibility.Collapsed;
        AskPage.Visibility = _tab == Tab.Ask ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = _tab == Tab.Ask ? Visibility.Collapsed : Visibility.Visible;
        LyricsBox.Visibility = HasLyrics ? Visibility.Visible : Visibility.Collapsed;

        double width = _tab == Tab.Chat ? 528 : 440;
        Panel.Width = width - 32;
        Panel.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, Math.Ceiling(Panel.DesiredSize.Height));
    }

    private void Morph()
    {
        var open = OpenSize();
        _w.Target = _expanded ? open.Width : _pillWidth;
        _h.Target = _expanded ? open.Height : PillHeight;
        Panel.IsHitTestVisible = _expanded;

        // 透明視窗有東西在動就要整塊重畫，所以平常只留夠用的高度，要展開更大時才長高
        double needed = Math.Max(BaseWindowHeight, 8 + _h.Target + 48);
        if (needed > Height) Height = needed;
        // 要打字的分頁才讓視窗拿鍵盤焦點
        SetActivatable(_expanded && _tab is Tab.Chat or Tab.Tools or Tab.Ai or Tab.Settings);

        if (_rendering || (_w.Settled && _h.Settled)) return;
        _rendering = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void SetActivatable(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(_hwnd, GWL_EXSTYLE);
        int wanted = on ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE;
        if (wanted != style) SetWindowLong(_hwnd, GWL_EXSTYLE, wanted);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        _w.Step(dt);
        _h.Step(dt);
        Apply();

        if (_w.Settled && _h.Settled)
        {
            CompositionTarget.Rendering -= OnFrame;
            _rendering = false;
            double needed = Math.Max(BaseWindowHeight, 8 + _h.Target + 48);
            if (Math.Abs(Height - needed) > 1) Height = needed;
        }
    }

    private void Apply()
    {
        double w = Math.Max(40, _w.Value), h = Math.Max(24, _h.Value);
        Host.Width = w;
        Host.Height = h;
        double r = Math.Min(h / 2, 26);
        Body.CornerRadius = Shadow.CornerRadius = new CornerRadius(r);
        Inside.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);

        // 高度長出來之前是膠囊的內容，之後淡入面板
        double open = Math.Clamp((h - PillHeight) / 150, 0, 1);
        Pill.Opacity = 1 - Math.Clamp(open * 3, 0, 1);
        Panel.Opacity = Backdrop.Opacity = Math.Clamp((open - 0.35) / 0.65, 0, 1);
    }

    // ---------------------------------------------------------------- 操作

    private void Host_MouseEnter(object sender, MouseEventArgs e) { _close.Stop(); if (!_expanded && !_dragging) _open.Start(); }
    private void Host_MouseLeave(object sender, MouseEventArgs e) { _open.Stop(); if (_expanded) _close.Start(); }

    private void Play_Click(object sender, RoutedEventArgs e) => _media.PlayPause();

    /// <summary>點來源那一行：換到 Roon 的下一個播放區域。</summary>
    private void Source_Click(object sender, MouseButtonEventArgs e) => SwitchZone();

    private void SwitchZone()
    {
        if (_media.NextZone() is { Length: > 0 } zone) Toast(L.F("播放區域：{0}", zone), 3);
    }

    /// <summary>在音樂分頁滾滾輪：調 Roon 的音量。</summary>
    private void Music_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_media.Nudge(e.Delta > 0 ? 1 : -1) is { } volume) { Toast(L.F("音量 {0}", volume), 2); e.Handled = true; }
    }
    private void Next_Click(object sender, RoutedEventArgs e) => _media.Next();
    private void Previous_Click(object sender, RoutedEventArgs e) => _media.Previous();

    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track is not { } t || t.Duration <= TimeSpan.Zero || Bar.ActualWidth <= 0) return;
        _media.Seek(t.Duration * Math.Clamp(e.GetPosition(Bar).X / Bar.ActualWidth, 0, 1));
    }

    private void LyricsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(LocalProvider.Folder);
            Process.Start(new ProcessStartInfo(LocalProvider.Folder) { UseShellExecute = true });
        }
        catch (Exception x) { Log.Write("開啟資料夾: " + x.Message); }
    }

    // ---------------------------------------------------------------- 歌詞校正

    private TimeSpan _lyricShift;
    private string _lyricsFrom = "";

    /// <summary>抓錯歌詞時換下一個來源的；選擇會記住。</summary>
    private async void LyricsOther_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) return;
        var key = _trackKey;
        var all = await _lyrics.AllAsync(track);
        if (key != _trackKey) return;
        if (all.Count == 0) { Toast(L.T("找不到其他歌詞"), 3); return; }
        var next = all[(all.FindIndex(h => h.Provider == _lyricsFrom) + 1) % all.Count];
        AppSettings.Current.LyricsPick[LyricsFinder.IdOf(track)] = next.Provider;
        AppSettings.Current.Save();
        _sheet = next.Sheet;
        _lyricsFrom = next.Provider;
        _lyricIndex = int.MinValue;
        Toast(all.Count == 1 ? L.F("只有 {0} 有這首歌的歌詞", next.Provider) : L.F("歌詞：{0}", next.Provider), 3);
        Morph();
    }

    private void LyricsEarlier_Click(object sender, RoutedEventArgs e) => ShiftLyrics(100);
    private void LyricsLater_Click(object sender, RoutedEventArgs e) => ShiftLyrics(-100);

    private void ShiftLyrics(int ms)
    {
        if (_track is not { } track) return;
        _lyricShift += TimeSpan.FromMilliseconds(ms);
        int total = (int)Math.Round(_lyricShift.TotalMilliseconds);
        var id = LyricsFinder.IdOf(track);
        if (total == 0) AppSettings.Current.LyricsShift.Remove(id); else AppSettings.Current.LyricsShift[id] = total;
        AppSettings.Current.Save();
        Toast(total == 0 ? L.T("歌詞時間：原本") : L.F(total > 0 ? "歌詞提早 {0} 秒" : "歌詞延後 {0} 秒", (Math.Abs(total) / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), 2);
    }

    private void LyricsReset_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) return;
        var id = LyricsFinder.IdOf(track);
        AppSettings.Current.LyricsPick.Remove(id);
        AppSettings.Current.LyricsShift.Remove(id);
        AppSettings.Current.Save();
        _trackKey = "";
        Refresh();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- 位置：可以拖，會記住

    /// <summary>島的中心 X 與視窗上緣；沒拖過就在主螢幕頂端正中間。存的位置跑出螢幕外（換了螢幕）就回到預設。</summary>
    private void Place()
    {
        var s = AppSettings.Current;
        double left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2, top = SystemParameters.WorkArea.Top;
        if (s.IslandX is { } x && s.IslandY is { } y &&
            x > SystemParameters.VirtualScreenLeft && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            y >= SystemParameters.VirtualScreenTop && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            left = x - Width / 2;
            top = y;
        }
        Left = _smoke ? -5000 : left;
        Top = top;
    }

    private Point? _dragFrom;
    private bool _dragging;

    private void Host_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_expanded) return;   // 展開時按鈕要能按，只有膠囊能拖
        _dragFrom = PointToScreen(e.GetPosition(this));
        Host.CaptureMouse();
    }

    private void Host_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
        var now = PointToScreen(e.GetPosition(this));
        if (!_dragging && (now - from).Length < 6) return;
        if (!_dragging) { _dragging = true; _open.Stop(); }
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left += (now.X - from.X) / scale;
        Top = Math.Max(SystemParameters.VirtualScreenTop, Top + (now.Y - from.Y) / scale);
        _dragFrom = now;
    }

    private void Host_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Host.ReleaseMouseCapture();
        _dragFrom = null;
        if (!_dragging) return;
        _dragging = false;
        AppSettings.Current.IslandX = Left + Width / 2;
        AppSettings.Current.IslandY = Top;
        AppSettings.Current.Save();
    }

    private void ResetPlace_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.IslandX = AppSettings.Current.IslandY = null;
        AppSettings.Current.Save();
        Place();
    }

    // ---------------------------------------------------------------- 全域快速鍵

    private const int HotkeyToggle = 1, HotkeyAsk = 2, WM_HOTKEY = 0x0312;
    /// <summary>實際註冊到的展開快速鍵。</summary>
    public string ToggleHotkey { get; private set; } = "";

    /// <summary>Ctrl+Alt+Space：展開／收起。Ctrl+Alt+V：問語音助手一句。被別的程式佔用就記在紀錄檔。</summary>
    private void InitHotkeys()
    {
        const uint control = 2, alt = 1, noRepeat = 0x4000;
        // 展開的快速鍵被別的程式佔走就換下一組
        const uint shift = 4;
        (uint Modifiers, uint Key, string Name)[] toggles = [(control | alt, 0x20, "Ctrl+Alt+Space"), (control | alt, 0x49, "Ctrl+Alt+I"), (control | shift, 0x20, "Ctrl+Shift+Space")];
        ToggleHotkey = toggles.FirstOrDefault(t => RegisterHotKey(_hwnd, HotkeyToggle, t.Modifiers | noRepeat, t.Key)).Name ?? "";
        if (ToggleHotkey != toggles[0].Name)
        {
            Log.Write($"快速鍵 Ctrl+Alt+Space 被別的程式佔用，改用 {(ToggleHotkey.Length > 0 ? ToggleHotkey : "（都被佔用）")}");
            if (ToggleHotkey.Length > 0) Dispatcher.BeginInvoke(() => Toast(L.F("展開的快速鍵：{0}", ToggleHotkey), 8, false));
        }
        if (!RegisterHotKey(_hwnd, HotkeyAsk, control | alt | noRepeat, 0x56)) Log.Write("快速鍵 Ctrl+Alt+V 被別的程式佔用");
        HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message != WM_HOTKEY) return IntPtr.Zero;
            handled = true;
            if (w.ToInt32() == HotkeyToggle) { _close.Stop(); _open.Stop(); SetExpanded(!_expanded); }
            else if (w.ToInt32() == HotkeyAsk) AskOnce();
            return IntPtr.Zero;
        });
        Closed += (_, _) => { UnregisterHotKey(_hwnd, HotkeyToggle); UnregisterHotKey(_hwnd, HotkeyAsk); };
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    // ---------------------------------------------------------------- 全螢幕時讓路

    private void WatchFullscreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool hide = AppSettings.Current.HideInFullscreen && FullscreenInFront();
        if (hide != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = hide;
            Root.Visibility = hide || _userHidden ? Visibility.Hidden : Visibility.Visible;
        }
        if (!hide) SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>前景視窗蓋滿它所在的整個螢幕（遊戲、全螢幕影片），而且不是桌面本身。</summary>
    private bool FullscreenInFront()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd) return false;
        var name = new StringBuilder(64);
        GetClassName(fg, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow" or "Windows.UI.Core.CoreWindow") return false;

        var monitor = MonitorFromWindow(fg, 2);
        if (monitor != MonitorFromWindow(_hwnd, 2)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(fg, out var r)) return false;
        return r.Left <= info.rcMonitor.Left && r.Top <= info.rcMonitor.Top && r.Right >= info.rcMonitor.Right && r.Bottom >= info.rcMonitor.Bottom;
    }

    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
}

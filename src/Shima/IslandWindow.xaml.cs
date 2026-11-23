// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.IO;
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

    private enum Tab { Music, Chat, Tools, Library, Ai, Settings, Ask }
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
        ApplyLyricsLines();
        ApplyScale();   // 視窗的大小跟著島的大小，也會呼叫 Place()

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (!_smoke) InitHotkeys();
            // 不出現在 Alt+Tab
            SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        Deactivated += (_, _) => { if (_expanded && _tab is Tab.Chat or Tab.Tools or Tab.Library or Tab.Ai or Tab.Settings && !Host.IsMouseOver) SetExpanded(false); };
        InitChat();
        InitTools();
        InitRecent();
        InitSettings();
        InitStatus();
        InitAgents();
        InitListening();
        if (Demo.On && _smoke) InitAlarms();   // 截圖要看得到示範的提醒；不是截圖時下面那一行會做
        if (!_smoke)
        {
            InitAlarms();
            Loaded += (_, _) =>
            {
                if (AppSettings.Current.Opacity < 0.95 || AppSettings.Current.AccentMode != "cover") ApplyTheme();
                ApplyDesktopLyrics();
                ApplyWakeWord();
                Welcome();
                RemindRoon();
            };
        }
        Loaded += async (_, _) =>
        {
            _media.Changed += () => Dispatcher.BeginInvoke(Refresh);
            if (!_smoke) _discord.Start();
            // 讀不到系統的媒體資訊也要繼續：後面的計時器沒啟動的話，整座島就不會動了
            if (Demo.On) _media.Demo = Demo.Track();
            else
                try { await _media.StartAsync(); }
                catch (Exception x) { Log.Write("media: " + x.Message); }
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

        Buddy.ImagePath = AppSettings.Current.CharacterImage;
        Buddy.Kind = AppSettings.Current.Character;
        Buddy.Level = () => Demo.On ? Demo.Level : _meter.Running ? _meter.Level : 0.35f;
        Meter.Level = () => _micOpen ? Agents.Ears.Level : _meter.Running ? _meter.Level : 0.35f;
        Meter.Bands = () => Demo.On ? Demo.Bands(4) : !_micOpen && _meter.Running ? _meter.Bands(4) : null;
        Spectrum.Bands = () => Demo.On ? Demo.Bands(SpectrumView.Count) : _meter.Running && _track?.Playing == true ? _meter.Bands(SpectrumView.Count) : null;
        Buddy.Bass = () => _meter.Running ? (float)_meter.Bands(64).Take(7).Average(b => SpectrumView.Scale(b)) : 0;
        _bridge.Level = () => _meter.Running ? _meter.Level : -1;
        _bridge.Bands = count => _meter.Bands(count);
        Closed += (_, _) =>
        {
            _closed = true;
            _meter.Dispose();
            _bridge.Stop();
            _discord.Dispose();   // 每次都讀欄位：設定頁改了 Discord 的 ID 會換一個新的
            _voice?.Stop();
            _ears?.Dispose();
            if (_rendering) { CompositionTarget.Rendering -= OnFrame; _rendering = false; }   // 動畫跑到一半就關了
        };
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
            if (_tab is Tab.Chat or Tab.Tools or Tab.Library or Tab.Ai or Tab.Settings && IsActive) return;
            SetExpanded(false);
        };
    }

    // ---------------------------------------------------------------- 內容

    private ImageSource? _coverBrushFor;
    private static readonly Brush NoCover = Frozen(Color.FromRgb(0x2A, 0x2A, 0x2A));
    private bool _closed;

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
            // Roon 每秒回報一次進度都會走到這裡；封面沒換就不要每秒做一支新的筆刷
            if (!ReferenceEquals(track.Cover, _coverBrushFor) || Cover.Background == null)
            {
                _coverBrushFor = track.Cover;
                Brush cover = NoCover;
                if (track.Cover != null) { cover = new ImageBrush(track.Cover) { Stretch = Stretch.UniformToFill }; cover.Freeze(); }
                Cover.Background = PillCover.Background = cover;
            }
        }
        else
        {
            TitleText.Text = L.T("沒有正在播放的音樂");
            ArtistText.Text = SourceText.Text = Elapsed.Text = Remaining.Text = "";
            BarFill.Width = 0;
            Cover.Background = NoCover;
            _coverBrushFor = null;   // 同一張封面的歌再回來時要重新套上
        }
        PillCover.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        Pill.Margin = has ? new Thickness(7, 0, 8, 0) : new Thickness(12, 0, 8, 0);
        Buddy.Dancing = track?.Playing == true;
        _discord.Update(track);
        if (!ReferenceEquals(track?.Cover, _coverShown))
        {
            _coverShown = track?.Cover;
            ApplyAccent();
            BackdropImage.Background = Accent.Blurred(_coverShown) is { } blur ? new ImageBrush(blur) { Stretch = Stretch.UniformToFill } : null;
        }

        var key = track?.Key ?? "";
        if (key != _trackKey)
        {
            if (_trackKey.Length > 0 && has) { Buddy.Cheer(); AnnounceTrack(track!); }
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
        var hit = Demo.On ? Demo.Lyrics() : await _lyrics.FindAsync(track);
        Log.Write($"歌詞: {track.Title} → {(hit == null ? "找不到" : hit.Provider + (hit.Exact ? "" : "（原曲，版本不同）"))}");
        if (key != _trackKey) return;   // 查的時候已經換歌了
        _sheet = WithAiTranslation(track, hit?.Sheet);
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
            // Stream Dock 在看的話，島因為全螢幕藏起來了也照樣擷取
            bool wanted = AppSettings.Current.AudioReactive && _track?.Playing == true;
            bool listen = wanted && !_hiddenForFullscreen;
            bool capture = !Demo.On && (listen || wanted && _bridge.DeckWatching);   // 示範模式的頻譜是假的，不擷取聲音
            if (capture && !_meter.Running) _meter.Start(); else if (!capture && _meter.Running) _meter.Stop(); else if (capture) _meter.FollowDefault(_system.OutputId);
            Meter.Visibility = listen || _micOpen ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_ticks % 5 == 0) UpdatePillProgress();
        SleepTick();
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
            notice = new("ask", ask.Project.Length > 0 ? $"{Agents.AgentKinds.Name(ask.Agent)} · {ask.Project}" : Agents.AgentKinds.Name(ask.Agent), $"{ask.Tool}  {ask.Details}", timer, _planLine, false);
        else if (_voice != null)
            notice = new("voice", "", _voiceLine, timer, _planLine, _micOpen);
        else if (_toast.Length > 0 && DateTime.Now < _toastUntil)
            notice = new("toast", "", _toast, timer, _planLine, false);
        else
            notice = new("", "", "", timer, _planLine, false);
        if (notice != _bridge.Notice) _bridge.Notice = notice;

        Agents.DeckDials dials;
        string zone = _media.ZoneName;
        double shift = Math.Round(_lyricShift.TotalSeconds, 1);
        if (_media.Sound is { } roon)
            dials = new(roon.Max > roon.Min ? (roon.Value - roon.Min) / (roon.Max - roon.Min) : -1, ((int)roon.Value).ToString(), roon.Muted, zone, shift);
        else if (_system.Sound is { Volume: >= 0 } pc)
            dials = new(pc.Volume / 100.0, pc.Volume.ToString(), pc.Muted, zone, shift);
        else
            dials = new(-1, "", false, zone, shift);
        if (dials != _bridge.Dials) _bridge.Dials = dials;
    }

    private Lyrics.LyricSheet? _deskSheet;
    private IReadOnlyList<string>? _deskSecond;
    private string _deskTitle = "";

    private void UpdateTexts()
    {
        UpdateNotice();
        UpdateRecentDot();
        ObserveListening();
        _musicPlaying = _track?.Playing == true;
        // 佇列是 Roon 才有的東西
        bool roon = _track != null && MediaHub.IsRoon(_track.AppId);
        QueueButton.Visibility = roon ? Visibility.Visible : Visibility.Collapsed;
        if (!roon && _queueOpen) { _queueOpen = false; Morph(); }
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
            Spectrum.Active = false;
            if (_desk != null && _deskTitle.Length > 0) { _deskTitle = ""; _deskSheet = null; FeedDesktopLyrics(); }
            var line = IdleLine.Text(AppSettings.Current.IdleShows, DateTime.Now, TimerLine(), _alarms?.List ?? [], Loc.Culture, L.T("明天"));
            PillLine.Say(line);
            // 不顯示：只剩角色，左右邊距一樣、寬度縮到剛好包住它（邊距 8＋8、角色 5＋26）
            Pill.Margin = line.Length == 0 ? new Thickness(8, 0, 8, 0) : new Thickness(12, 0, 8, 0);
            double idle = line.Length == 0 ? (Buddy.Kind == "none" ? 36 : 47)
                : Math.Max(IdleWidth, Math.Ceiling(PillLine.TextWidth) + 40) + (Buddy.Kind == "none" ? 0 : 30);
            if (Math.Abs(idle - _pillWidth) > 0.5) { _pillWidth = idle; Morph(); }
            return;
        }

        var pos = track.Now;
        bool timed = track.Duration > TimeSpan.Zero;
        // 進度條每 0.1 秒都在變，改它會讓整個面板重新排版；島收著的時候看不到，就不用做
        if (_expanded && _tab == Tab.Music || _rendering)
        {
            Elapsed.Text = timed ? Clock(pos) : "";
            Remaining.Text = timed ? "-" + Clock(track.Duration - pos) : "";
            BarFill.Width = timed && Bar.ActualWidth > 0 ? Bar.ActualWidth * Math.Clamp(pos / track.Duration, 0, 1) : 0;
        }

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
                if (track.Playing) PillLine.Sing(text, start, length, sheet.Lines[i].Words); else PillLine.Say(name);
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
        if (_desk != null && (!ReferenceEquals(_deskSheet, shown) || !ReferenceEquals(_deskSecond, second) || _deskTitle != track.Title))
        {
            _deskSheet = shown; _deskSecond = second; _deskTitle = track.Title;
            FeedDesktopLyrics();
        }
        var deck = _bridge.Deck;
        if (!ReferenceEquals(deck.Track, track) || !ReferenceEquals(deck.Sheet, shown) || !ReferenceEquals(deck.Second, second) || deck.Shift != LyricLead + _lyricShift)
            _bridge.Deck = new Agents.DeckState(track, shown, second, LyricLead + _lyricShift);
        LyricsList.Active = _expanded && _tab == Tab.Music;
        Spectrum.Active = LyricsList.Active && track.Playing && AppSettings.Current.AudioReactive;
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
        // 權限請求顯示中不能收起來。要先擋，再改狀態：不然狀態說「收著」、畫面卻還開著，之後就再也收不起來
        if (!expanded && _tab == Tab.Ask) return;
        _expanded = expanded;
        if (expanded && _tab == Tab.Chat) _ = EnsureChatAsync();
        Morph();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ShowTab(sender == TabChat ? Tab.Chat : sender == TabTools ? Tab.Tools : sender == TabLibrary ? Tab.Library : sender == TabAi ? Tab.Ai : sender == TabSettings ? Tab.Settings : Tab.Music);
    }

    private void ShowTab(Tab tab)
    {
        _tab = tab;
        if (tab == Tab.Chat && _expanded) _ = EnsureChatAsync();
        if (tab == Tab.Ai) RefreshAi();
        if (tab == Tab.Settings) RefreshSettings();
        if (tab == Tab.Library) RefreshLibrary();
        Morph();
    }

    /// <summary>展開時的大小：寬度看分頁，高度量內容實際需要多少。</summary>
    private Size OpenSize()
    {
        MusicPage.Visibility = _tab == Tab.Music ? Visibility.Visible : Visibility.Collapsed;
        ChatPage.Visibility = _tab == Tab.Chat ? Visibility.Visible : Visibility.Collapsed;
        AiPage.Visibility = _tab == Tab.Ai ? Visibility.Visible : Visibility.Collapsed;
        ToolsPage.Visibility = _tab == Tab.Tools ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = _tab == Tab.Library ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = _tab == Tab.Settings ? Visibility.Visible : Visibility.Collapsed;
        AskPage.Visibility = _tab == Tab.Ask ? Visibility.Visible : Visibility.Collapsed;
        Tabs.Visibility = _tab == Tab.Ask ? Visibility.Collapsed : Visibility.Visible;
        LyricsBox.Visibility = HasLyrics && !_queueOpen ? Visibility.Visible : Visibility.Collapsed;
        QueueHost.Visibility = _queueOpen ? Visibility.Visible : Visibility.Collapsed;

        double width = _tab == Tab.Chat ? 528 : 440;
        Panel.Width = width - 32;
        // 先把排著隊的版面算完。深處的內容剛換過（設定的子分頁、清單多一列）時，中間幾層還留著舊的高度，
        // 直接量會拿到上一次的結果，島就停在舊的大小，要等下一次變形才對回來
        Panel.UpdateLayout();
        Panel.Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, Math.Ceiling(Panel.DesiredSize.Height));
    }

    private void Morph()
    {
        // 收著的時候不需要知道展開有多大：量一次要把整個面板重新排版，而換歌、每秒的進度回報都會走到這裡
        var open = _expanded ? OpenSize() : default;
        _w.Target = _expanded ? open.Width : _pillWidth;
        _h.Target = _expanded ? open.Height : PillHeight;
        Panel.IsHitTestVisible = _expanded;

        // 透明視窗有東西在動就要整塊重畫，所以平常只留夠用的高度，要展開更大時才長高
        double needed = WindowHeightFor(_h.Target);
        if (needed > Height) Height = needed;
        // 要打字的分頁才讓視窗拿鍵盤焦點
        SetActivatable(_expanded && (_tab is Tab.Chat or Tab.Tools or Tab.Library or Tab.Ai or Tab.Settings || _tab == Tab.Music && _queueOpen));

        if (_rendering || (_w.Settled && _h.Settled)) return;
        _rendering = true;
        // 真的要變形了才把膠囊叫回來（途中它會跟面板交叉淡入淡出）。
        // 不能放在前面：Roon 每秒回報進度都會走到這個方法，展開著的時候膠囊就一直被叫醒
        Pill.Visibility = Visibility.Visible;
        Lighten(true);
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private System.Windows.Media.Effects.Effect? _shadowEffect;

    /// <summary>
    /// 島在變大變小的那零點幾秒，每一格都要重算整塊的陰影模糊、重新放大背景圖，面板越高越吃力
    /// （從工具、設定這種高的分頁切出去最明顯）。變形期間先拿掉陰影、背景用便宜的縮放，停下來再放回去。
    /// </summary>
    private void Lighten(bool moving)
    {
        _shadowEffect ??= Shadow.Effect;
        bool large = Math.Max(Host.Height, _h.Target) > 120;
        Shadow.Effect = moving && large ? null : _shadowEffect;
        RenderOptions.SetBitmapScalingMode(BackdropImage, moving ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);
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
            Lighten(false);
            // 完全展開後膠囊被面板蓋住、透明度是 0，但裡面的角色、音量條、歌詞還在動，每動一次整個透明視窗都要重畫。
            // 收起來不佔位置，它們自己就會停。（面板不能這樣做：展開前要先量它有多大。）
            if (_expanded && Pill.Opacity < 0.01) Pill.Visibility = Visibility.Collapsed;
            double needed = WindowHeightFor(_h.Target);
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
        List<LyricsHit> all;
        try { all = await _lyrics.AllAsync(track); }
        catch (Exception x) { Log.Write("歌詞: " + x.Message); Toast(L.T("找不到其他歌詞"), 3); return; }
        if (key != _trackKey) return;
        if (all.Count == 0) { Toast(L.T("找不到其他歌詞"), 3); return; }
        var next = all[(all.FindIndex(h => h.Provider == _lyricsFrom) + 1) % all.Count];
        AppSettings.Current.LyricsPick[LyricsFinder.IdOf(track)] = next.Provider;
        AppSettings.Current.Save();
        _sheet = WithAiTranslation(track, next.Sheet);
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

    /// <summary>現在這句歌詞（沒有歌詞就是歌名）做成一張圖：放進剪貼簿，同時存到「圖片\Shima」。</summary>
    private void LyricCard_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track) return;
        try
        {
            var card = MakeCard(track);
            LyricCard.Save(card, LyricCard.Folder, track.Title);
            Clipboard.SetImage(card);
            Toast(L.T("歌詞卡片已複製"), 4);
        }
        catch (Exception x) { Log.Write("歌詞卡片: " + x.Message); }
    }

    private System.Windows.Media.Imaging.BitmapSource MakeCard(Track track)
    {
        string line = "", second = "";
        if (_sheet is { Synced: true } sheet && Lrc.LineAt(sheet.Lines, track.Now + LyricLead + _lyricShift) is >= 0 and var i)
        {
            line = sheet.Lines[i].Text;
            var extra = AppSettings.Current.LyricsSecondLine switch
            {
                "translation" => sheet.Translation ?? sheet.Romaji,
                "romaji" => sheet.Romaji ?? sheet.Translation,
                _ => null,
            };
            if (line.Length > 0 && extra != null && i < extra.Count) second = extra[i];
        }
        return LyricCard.Render(track, line, second, ThemeAccent(track.Cover));
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
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var at = IslandScale.TopLeft(SystemParameters.WorkArea, screens, s.IslandX, s.IslandY, Width, 440 * _scale);
        Left = _smoke ? -5000 : at.X;
        Top = at.Y;
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
    private const uint NoRepeat = 0x4000;
    /// <summary>實際註冊到的快速鍵（沒註冊成功是空字串）。</summary>
    public string ToggleHotkey { get; private set; } = "";
    public string AskHotkey { get; private set; } = "";
    /// <summary>錄製新快速鍵時先拆掉自己的，免得按下去就觸發。</summary>
    private bool _recordingHotkey;

    /// <summary>預設 Ctrl+Alt+Space 展開／收起、Ctrl+Alt+V 問語音助手一句，都可以在設定改。被別的程式佔用就記在紀錄檔。</summary>
    private void InitHotkeys()
    {
        RegisterHotkeys(notify: true);
        HwndSource.FromHwnd(_hwnd)?.AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (message != WM_HOTKEY) return IntPtr.Zero;
            handled = true;
            if (_recordingHotkey) return IntPtr.Zero;
            if (w.ToInt32() == HotkeyToggle) { _close.Stop(); _open.Stop(); SetExpanded(!_expanded); }
            else if (w.ToInt32() == HotkeyAsk) AskOnce();
            return IntPtr.Zero;
        });
        Closed += (_, _) => { UnregisterHotKey(_hwnd, HotkeyToggle); UnregisterHotKey(_hwnd, HotkeyAsk); };
    }

    /// <summary>
    /// 兩組一起註冊。自己指定的先註冊：展開用預設、問 AI 指定了 Ctrl+Alt+Space 時，
    /// 先註冊展開的話它會把那一組拿走，指定的反而失效。
    /// </summary>
    /// <param name="notify">展開的快速鍵改用備援時要不要在島上提示（只有啟動時提示）。</param>
    private void RegisterHotkeys(bool notify = false)
    {
        if (AppSettings.Current.HotkeyToggle.Length == 0 && AppSettings.Current.HotkeyAsk.Length > 0) { RegisterAsk(); RegisterToggle(notify); }
        else { RegisterToggle(notify); RegisterAsk(); }
    }

    /// <summary>依設定註冊展開快速鍵；設定的那組註冊不成（或沒設定）就走預設備援順序。回傳 false 表示設定的那組被佔用。</summary>
    /// <param name="notify">改用備援時在島上提示。錄製或取消時重新註冊不提示：每次都跳一次，還會蓋掉「被佔用」的訊息。</param>
    private bool RegisterToggle(bool notify = false)
    {
        UnregisterHotKey(_hwnd, HotkeyToggle);
        ToggleHotkey = "";
        bool custom = Hotkeys.TryParse(AppSettings.Current.HotkeyToggle, out var m, out var k);
        if (custom && RegisterHotKey(_hwnd, HotkeyToggle, m | NoRepeat, k))
        {
            ToggleHotkey = Hotkeys.Format(m, k)!;
            return true;
        }
        const uint control = 2, alt = 1, shift = 4;
        // 預設的展開快速鍵被別的程式佔走就換下一組
        (uint Modifiers, uint Key, string Name)[] toggles = [(control | alt, 0x20, "Ctrl+Alt+Space"), (control | alt, 0x49, "Ctrl+Alt+I"), (control | shift, 0x20, "Ctrl+Shift+Space")];
        ToggleHotkey = toggles.FirstOrDefault(t => RegisterHotKey(_hwnd, HotkeyToggle, t.Modifiers | NoRepeat, t.Key)).Name ?? "";
        if (ToggleHotkey != toggles[0].Name)
        {
            Log.Write($"快速鍵 Ctrl+Alt+Space 被別的程式佔用，改用 {(ToggleHotkey.Length > 0 ? ToggleHotkey : "（都被佔用）")}");
            if (notify && ToggleHotkey.Length > 0) Dispatcher.BeginInvoke(() => Toast(L.F("展開的快速鍵：{0}", ToggleHotkey), 8, false));
        }
        return !custom;
    }

    private bool RegisterAsk()
    {
        UnregisterHotKey(_hwnd, HotkeyAsk);
        AskHotkey = "";
        bool custom = Hotkeys.TryParse(AppSettings.Current.HotkeyAsk, out var m, out var k);
        if (custom && RegisterHotKey(_hwnd, HotkeyAsk, m | NoRepeat, k)) { AskHotkey = Hotkeys.Format(m, k)!; return true; }
        if (RegisterHotKey(_hwnd, HotkeyAsk, 2 | 1 | NoRepeat, 0x56)) AskHotkey = "Ctrl+Alt+V";
        else Log.Write("快速鍵 Ctrl+Alt+V 被別的程式佔用");
        return !custom;
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

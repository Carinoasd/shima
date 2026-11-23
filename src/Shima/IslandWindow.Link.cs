// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shima.Agents;
using Shima.Listening;
using Shima.Playback;

namespace Shima;

// 把各個獨立的模組接到島上：佇列、聆聽紀錄與收藏、桌面歌詞、換歌提示、自訂角色、
// Claude Code 的工作階段與用量、鬧鐘、喚醒詞。
public partial class IslandWindow
{
    // ---------------------------------------------------------------- Roon 佇列

    private QueueView? _queue;
    private bool _queueOpen;

    /// <summary>音樂分頁下半部在歌詞與「接下來播放」之間切換。佇列裡有搜尋框，開著的時候視窗要能收鍵盤。</summary>
    private void Queue_Click(object sender, RoutedEventArgs e)
    {
        _queueOpen = !_queueOpen;
        if (_queueOpen && _queue == null) QueueHost.Children.Add(_queue = new QueueView(_media.Roon));
        Morph();
    }

    // ---------------------------------------------------------------- 聆聽紀錄、統計、收藏

    private readonly ListeningLog _listening = new();
    private readonly LyricFavorites _favorites = new();
    private Scrobbler? _scrobbler;
    private StatsView? _stats;
    private FavoritesView? _favoritesView;
    private bool _libraryShowsFavorites;

    private void InitListening()
    {
        _listening.Listened += listen => { if (Scrobbling() is { } s) _ = s.Submit(listen); };
        _listening.Started += track => { if (Scrobbling() is { } s) _ = s.NowPlaying(track); };
        Closed += (_, _) => { _listening.Flush(); _scrobbler?.Dispose(); };
    }

    /// <summary>有填 ListenBrainz 的 token 才回報；token 改了就換一個。</summary>
    private Scrobbler? Scrobbling()
    {
        string token = AppSettings.Current.ListenBrainzToken;
        if (token.Length == 0) return null;
        if (_scrobbler == null) _scrobbler = new Scrobbler(token); else if (_scrobbler.Token != token) _scrobbler.Token = token;
        return _scrobbler;
    }

    private void ObserveListening()
    {
        if (AppSettings.Current.History) _listening.Observe(_track, DateTime.Now);
    }

    /// <summary>聆聽分頁：統計與收藏的歌詞，上面兩顆按鈕切換。</summary>
    private void RefreshLibrary()
    {
        if (_stats == null)
        {
            _stats = new StatsView();
            _favoritesView = new FavoritesView(_favorites);
            _favoritesView.Picked += favorite =>
            {
                try
                {
                    var card = LyricCard.Render(new Track("", "", favorite.Title, favorite.Artist, favorite.Album, null, false, TimeSpan.Zero, default, TimeSpan.Zero), favorite.Line, favorite.Second);
                    LyricCard.Save(card, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shima"), favorite.Title);
                    Clipboard.SetImage(card);
                    Toast(L.T("歌詞卡片已複製"), 4);
                }
                catch (Exception x) { Log.Write("歌詞卡片: " + x.Message); }
            };
            var switcher = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (glyph, favorites) in new[] { ("", false), ("", true) })
            {
                var button = new Button { Style = (Style)FindResource("Key"), FontSize = 14, Content = glyph };
                button.Click += (_, _) => { _libraryShowsFavorites = favorites; RefreshLibrary(); Morph(); };
                switcher.Children.Add(button);
            }
            LibraryPage.Children.Add(switcher);
            LibraryPage.Children.Add(_stats);
            LibraryPage.Children.Add(_favoritesView);
        }
        _stats.Visibility = _libraryShowsFavorites ? Visibility.Collapsed : Visibility.Visible;
        _favoritesView!.Visibility = _libraryShowsFavorites ? Visibility.Visible : Visibility.Collapsed;
        if (_libraryShowsFavorites) _favoritesView.Refresh(); else _stats.Load();
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_track is not { } track || CurrentLine() is not { Line.Length: > 0 } now) { Toast(L.T("現在沒有歌詞"), 3, false); return; }
        _favorites.Add(now.Line, now.Second, track.Title, track.Artist, track.Album, track.Now, DateTimeOffset.Now);
        Toast(L.T("已收藏這句歌詞"), 3);
    }

    // ---------------------------------------------------------------- 桌面歌詞、換歌提示、自訂角色

    private DesktopLyrics? _desk;
    private MiniToast? _mini;

    private void ApplyDesktopLyrics()
    {
        var s = AppSettings.Current;
        DesktopLyricsItem.IsChecked = s.DesktopLyrics;
        DesktopLockItem.IsChecked = s.DesktopLyricsLocked;
        DesktopLockItem.IsEnabled = s.DesktopLyrics;
        if (!s.DesktopLyrics) { _desk?.Hide(); return; }
        if (_desk == null)
        {
            _desk = new DesktopLyrics
            {
                Clock = () => _track is { } t ? (t.Now + LyricLead + _lyricShift, t.Playing) : (TimeSpan.Zero, false),
                LyricSize = s.DesktopLyricsSize,
                TopLeft = s.DesktopLyricsX is { } x && s.DesktopLyricsY is { } y ? new Point(x, y) : null,
            };
            _desk.PlacementChanged += () =>
            {
                var set = AppSettings.Current;
                set.DesktopLyricsSize = _desk.LyricSize;
                set.DesktopLyricsX = _desk.TopLeft?.X;
                set.DesktopLyricsY = _desk.TopLeft?.Y;
                set.Save();
            };
            Closed += (_, _) => _desk.Close();
        }
        _desk.Locked = s.DesktopLyricsLocked;
        _desk.Light = ThemeAccent(_coverShown);
        FeedDesktopLyrics();
        _desk.Show();
    }

    /// <summary>歌或歌詞變了就告訴桌面歌詞。</summary>
    private void FeedDesktopLyrics()
    {
        if (_desk == null || !AppSettings.Current.DesktopLyrics) return;
        var shown = _sheet is { Synced: true } ? _sheet : null;
        var second = AppSettings.Current.LyricsSecondLine switch
        {
            "translation" => shown?.Translation ?? shown?.Romaji,
            "romaji" => shown?.Romaji ?? shown?.Translation,
            _ => null,
        };
        _desk.SetTrack(_track?.Title ?? "", _track?.Artist ?? "");
        _desk.Show(shown, second);
        _desk.Wake();
    }

    private void DesktopLyrics_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.DesktopLyrics = DesktopLyricsItem.IsChecked;
        AppSettings.Current.Save();
        ApplyDesktopLyrics();
        if (AppSettings.Current.DesktopLyrics && AppSettings.Current.DesktopLyricsLocked) Toast(L.T("要移動桌面歌詞，先在右鍵選單取消鎖定"), 6, false);
    }

    private void DesktopLock_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.DesktopLyricsLocked = DesktopLockItem.IsChecked;
        AppSettings.Current.Save();
        ApplyDesktopLyrics();
    }

    /// <summary>島因為全螢幕藏起來時換歌：從螢幕上緣滑出一張小卡片。</summary>
    private void AnnounceTrack(Track track)
    {
        if (!_hiddenForFullscreen || !AppSettings.Current.FullscreenToast) return;
        (_mini ??= NewMini()).Show(track.Cover, track.Title, track.Artist, ThemeAccent(track.Cover));
    }

    private MiniToast NewMini()
    {
        var mini = new MiniToast();
        Closed += (_, _) => mini.Close();
        return mini;
    }

    private void ApplyCharacter()
    {
        Buddy.ImagePath = AppSettings.Current.CharacterImage;
        Buddy.Kind = AppSettings.Current.Character;
        foreach (MenuItem item in CharacterMenu.Items) item.IsChecked = (string)item.Tag == AppSettings.Current.Character;
        UpdateTexts();
    }

    // ---------------------------------------------------------------- Claude Code：工作階段、用量、自動允許

    private readonly AgentUsage _usage = new();
    private SessionsView? _sessions;
    private TextBlock? _usageLine;
    private DateTime _usageAt;

    private void InitSessions()
    {
        _sessions = new SessionsView();
        _sessions.Picked += session => TerminalFocus.Bring(session);
        _usageLine = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), Margin = new Thickness(2, 2, 0, 8), Visibility = Visibility.Collapsed };
        SessionsHost.Children.Add(_sessions);
        SessionsHost.Children.Add(_usageLine);
        _bridge.SessionsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            var sessions = _bridge.Sessions;
            _sessions.Sessions = sessions;
            // 用量只算 Claude Code 的：別的 agent 的對話紀錄格式不一樣，它們的 hook 也沒有給可靠的 token 數
            foreach (var session in sessions) if (session.Agent == AgentKind.Claude && session.TranscriptPath.Length > 0) _usage.Watch(session.TranscriptPath);
            if (_tab == Tab.Ai && _expanded) { RefreshUsage(); Morph(); }
        });
        _bridge.AutoAllowed += (ask, rule) => Dispatcher.BeginInvoke(() => Toast(L.F("已自動允許 {0}", rule.Label), 3, false));
    }

    /// <summary>今天用了多少：打開 AI 分頁時算一次，之後最多一分鐘算一次（要掃很多檔案）。</summary>
    private async void RefreshUsage()
    {
        if (_usageLine == null || DateTime.Now - _usageAt < TimeSpan.FromSeconds(60)) return;
        _usageAt = DateTime.Now;
        try
        {
            var today = await _usage.TodayAsync();
            long tokens = today.Tokens.Total;
            if (tokens <= 0) { _usageLine.Visibility = Visibility.Collapsed; return; }
            string amount = tokens >= 1_000_000 ? (tokens / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M" : (tokens / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "K";
            string cost = today.Cost > 0 ? "$" + today.Cost.ToString("0.00", CultureInfo.InvariantCulture) + (today.CostKnown ? "" : "+") + " · " : "";
            _usageLine.Text = L.F("今天 {0}", cost + amount + " tokens");
            _usageLine.Visibility = Visibility.Visible;
            if (_tab == Tab.Ai && _expanded) Morph();
        }
        catch (Exception e) { Log.Write("usage: " + e.Message); }
    }

    private void AskAlways_Click(object sender, RoutedEventArgs e) => Decide(PermissionRequest.Always);

    // ---------------------------------------------------------------- 鬧鐘、喚醒詞

    private WakeWord? _wake;
    private volatile bool _musicPlaying;

    private void InitAlarms()
    {
        var alarms = new Alarms();
        alarms.Due += (alarm, missed) => Dispatcher.BeginInvoke(() =>
        {
            Toast(alarm.Text.Length > 0 ? alarm.Text : L.T("時間到了"), 20);
            if (!_expanded) SetExpanded(false);
        });
        Skills.Alarms = alarms;
        alarms.Start();
        Closed += (_, _) => alarms.Dispose();
    }

    /// <summary>喚醒詞：設定開著、而且語音助手沒在用麥克風的時候才聽。</summary>
    private void ApplyWakeWord()
    {
        if (!AppSettings.Current.WakeWord || _voice != null) { _wake?.Stop(); return; }
        if (_wake == null)
        {
            var language = AppSettings.Current.VoiceLanguage is { Length: > 0 } spoken ? spoken : Loc.Speech;
            _wake = new WakeWord(_ears ??= new Ears(), language) { MusicPlaying = () => _musicPlaying };
            _wake.Heard += () => Dispatcher.BeginInvoke(() => { if (_voice == null) StartVoice(once: true); });
            _wake.Problem += problem => Dispatcher.BeginInvoke(() => Toast(problem, 6, false));
            Closed += (_, _) => _wake.Stop();
        }
        // 語言設定可能改過；下一次開始聽的時候才會套用
        _wake.Language = AppSettings.Current.VoiceLanguage is { Length: > 0 } now ? now : Loc.Speech;
        _wake.Start();
    }
}

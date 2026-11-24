// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
    private LastFm? _lastFm;
    // 還沒回報出去的（離線、關掉程式之前沒送成的）：寫在檔案裡，之後補送
    private readonly PendingScrobbles _pending = new();
    private QueuedSink? _scrobblerQueue, _lastFmQueue;
    private StatsView? _stats;
    private FavoritesView? _favoritesView;
    private bool _libraryShowsFavorites;

    /// <summary>這個來源有沒有被使用者排除：寫紀錄、回報、補送都看這一個。</summary>
    private static bool SourceCounts(string? source) => ListenSources.Counts(source, AppSettings.Current.HistoryExcluded);

    private void InitListening()
    {
        _listening.Accepts = SourceCounts;
        _listening.Listened += listen => { foreach (var s in Scrobbling()) _ = s.Submit(listen); };
        _listening.Started += track => { foreach (var s in Scrobbling()) _ = s.NowPlaying(track); };
        foreach (var sink in Scrobbling()) _ = sink.Drain();   // 上次沒送出去的
        Closed += (_, _) => { _lastFmConnecting?.Cancel(); _listening.Flush(); _scrobbler?.Dispose(); _lastFm?.Dispose(); };
    }

    /// <summary>現在要回報的目的地：ListenBrainz 有填 token、Last.fm 有連接的才算；設定改了就換成新的。</summary>
    private List<QueuedSink> Scrobbling()
    {
        var sinks = new List<QueuedSink>();
        var s = AppSettings.Current;
        // 解密的結果由 AppSettings 記著，這裡每次呼叫都很便宜
        var token = s.ListenBrainzTokenText();
        if (token.Length > 0)
        {
            if (_scrobbler == null) _scrobbler = new Scrobbler(token); else if (_scrobbler.Token != token) _scrobbler.Token = token;
            sinks.Add(_scrobblerQueue ??= new QueuedSink("listenbrainz", _scrobbler, _pending, null, SourceCounts));
        }
        if (s.LastFmSession.Length > 0 && s.LastFmApiKey.Length > 0)
        {
            if (_lastFm == null)
            {
                _lastFm = new LastFm();
                _lastFm.SessionRejected += rejected => Dispatcher.BeginInvoke(() =>
                {
                    // 被拒的是舊的那一把、使用者已經重新連接了：不要把新的清掉
                    if (s.LastFmSessionText() != rejected) return;
                    // 還沒送的留著：重新連接之後補送。自己按「中斷」才清掉（LastFmDisconnect）
                    s.SetLastFmSession("", "");
                    RefreshLastFmButton();
                    Toast(L.T("Last.fm 授權失效，請重新連接"), 6, false);
                });
            }
            _lastFm.Use(s.LastFmApiKey, s.LastFmSecretText(), s.LastFmSessionText());
            if (_lastFm.Enabled) sinks.Add(_lastFmQueue ??= new QueuedSink("lastfm", _lastFm, _pending, null, SourceCounts));
        }
        return sinks;
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
                    var card = LyricCard.Render(new Track("", "", favorite.Title, favorite.Artist, favorite.Album, null, false, TimeSpan.Zero, default, TimeSpan.Zero), favorite.Line, favorite.Second, ThemeAccent(null));
                    LyricCard.Save(card, LyricCard.Folder, favorite.Title);
                    Clipboard.SetImage(card);
                    Toast(L.T("歌詞卡片已複製"), 4);
                }
                catch (Exception x) { Log.Write("歌詞卡片: " + x.Message); }
            };
            _stats.ShareRequested += ShareRecap;
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
        if (_libraryShowsFavorites) _favoritesView.Refresh(); else LoadStats();
    }

    /// <summary>把聆聽分頁目前選的期間做成回顧卡片：複製、存到圖片\Shima。</summary>
    private void ShareRecap()
    {
        try
        {
            if (_stats?.Summary is not { } summary) return;
            var card = RecapCard.Render(summary, _stats.Period, _stats.Now, _track?.Cover, ThemeAccent(_track?.Cover));
            if (card == null) { Toast(L.T("這個期間還沒有聽歌"), 3, false); return; }
            var saved = LyricCard.Save(card, LyricCard.Folder, "Shima Recap");
            try { Clipboard.SetImage(card); }
            catch (Exception x)
            {
                // 剪貼簿被別的程式佔用時會丟例外：圖已經存好了，要讓使用者知道去哪裡拿
                Log.Write("聆聽回顧剪貼簿: " + x.Message);
                Toast(L.F("剪貼簿被佔用，圖片已存到 {0}", Path.GetDirectoryName(saved) ?? LyricCard.Folder), 6, false);
                return;
            }
            Toast(L.T("聆聽回顧已複製"), 4);
        }
        catch (Exception x) { Log.Write("聆聽回顧: " + x.Message); }
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
        _desk.Show(shown, DesktopSecond(shown, second));
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
        var mini = _mini ??= NewMini();
        mini.Scale = _scale;
        mini.Area = MonitorArea();
        mini.Show(track.Cover, track.Title, track.Artist, ThemeAccent(track.Cover));
    }

    /// <summary>島所在螢幕的範圍（裝置無關像素，跟 SystemParameters 同一套座標）；查不到是 null，卡片就放主螢幕。</summary>
    private Rect? MonitorArea()
    {
        try
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (_hwnd == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(_hwnd, 2), ref info)) return null;
            var dpi = VisualTreeHelper.GetDpi(this);
            var m = info.rcMonitor;
            return new Rect(m.Left / dpi.DpiScaleX, m.Top / dpi.DpiScaleY, (m.Right - m.Left) / dpi.DpiScaleX, (m.Bottom - m.Top) / dpi.DpiScaleY);
        }
        catch { return null; }
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
    private HashSet<string> _usageSessions = [];   // 上次查用量時連著的 Claude Code 工作階段
    private UsageView? _usageView;
    private bool _usageOpen;
    private int _trendToggles;   // 圖開關過幾次：讓排好的「晚點釋放」知道自己過期了
    private CancellationTokenSource? _trendCts;

    private void InitSessions()
    {
        _sessions = new SessionsView();
        _sessions.Picked += session => TerminalFocus.Bring(session);
        _usageLine = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x90, 255, 255, 255)), Margin = new Thickness(2, 2, 0, 8), Visibility = Visibility.Collapsed };
        SessionsHost.Children.Add(_sessions);
        SessionsHost.Children.Add(_usageLine);
        _usageView = new UsageView { Visibility = Visibility.Collapsed, Margin = new Thickness(2, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        SessionsHost.Children.Add(_usageView);
        // 那一行可以點：移上去變亮、游標變手形，點一下展開最近 14 天的圖
        var dim = _usageLine.Foreground;
        _usageLine.Cursor = System.Windows.Input.Cursors.Hand;
        _usageLine.MouseEnter += (_, _) => _usageLine.Foreground = Brushes.White;
        _usageLine.MouseLeave += (_, _) => _usageLine.Foreground = dim;
        _usageLine.MouseLeftButtonUp += (_, _) => ToggleUsage(!_usageOpen);
        _bridge.SessionsChanged += () => Dispatcher.BeginInvoke(() =>
        {
            var sessions = _bridge.Sessions;
            _sessions.Sessions = sessions;
            // 用量只算 Claude Code 的：別的 agent 的對話紀錄格式不一樣，它們的 hook 也沒有給可靠的 token 數
            foreach (var session in sessions) if (session.Agent == AgentKind.Claude && session.TranscriptPath.Length > 0) _usage.Watch(session.TranscriptPath);
            if (_tab == Tab.Ai && _expanded) { RefreshUsage(); Morph(); }
        });
        Closed += (_, _) => _trendCts?.Cancel();
        _bridge.AutoAllowed += (ask, rule) => Dispatcher.BeginInvoke(() => Toast(L.F("已自動允許 {0}", rule.Label), 3, false));
    }

    /// <summary>今天用了多少：打開 AI 分頁時算一次，之後最多一分鐘算一次（要掃很多檔案）。</summary>
    private async void RefreshUsage()
    {
        if (_usageLine == null) return;
        if (Demo.On)   // 示範模式用編出來的數字，不讀自己的用量
        {
            var demo = Demo.Usage(DateTime.Now);
            ShowUsageLine(demo.Days[^1].Tokens.Total, demo.Days[^1].Cost, true);
            _usageView!.Trend = demo;
            return;
        }
        var now = DateTime.Now;
        var claude = _bridge.Sessions.Where(x => x.Agent == AgentKind.Claude).Select(x => x.Id).ToHashSet();
        if (!UsageDue(now, _usageAt, claude.Any(id => !_usageSessions.Contains(id)))) return;
        _usageAt = now;
        _usageSessions = claude;
        try
        {
            var today = await _usage.TodayAsync();
            ShowUsageLine(today.Tokens.Total, today.Cost, today.CostKnown);
            if (_usageOpen) LoadTrend();
        }
        catch (Exception e) { Log.Write("usage: " + e.GetType().Name); }   // 訊息可能有含專案名稱的路徑
    }

    /// <summary>
    /// 現在要不要重新查今天的用量（要掃很多檔案，平常最多一分鐘一次）。
    /// 跨過午夜（「今天」換了）、時鐘往回調（不然要等時間追上才會再查）、有沒看過的 Claude Code 工作階段連上（那一行要馬上出現）都立刻查。
    /// newSession 只看「上次查的時候沒有的工作階段」：每個 hook 事件都會觸發，連進度更新也算的話等於沒有節流。
    /// </summary>
    public static bool UsageDue(DateTime now, DateTime last, bool newSession) =>
        newSession || now < last || now.Date != last.Date || now - last >= TimeSpan.FromSeconds(60);

    private void ShowUsageLine(long tokens, decimal cost, bool known)
    {
        if (!ShowsUsageLine(tokens, _usageOpen, _bridge.Sessions.Any(x => x.Agent == AgentKind.Claude)))
        {
            bool shown = _usageLine!.Visibility == Visibility.Visible;
            _usageLine.Visibility = Visibility.Collapsed;
            _usageView!.Visibility = Visibility.Collapsed;
            if (shown && _tab == Tab.Ai && _expanded) Morph();   // 收起來了，島也要跟著縮
            return;
        }
        string money = cost > 0 ? UsageView.Money(cost, known) + " · " : "";
        _usageLine!.Text = L.F("今天 {0}", money + UsageView.Amount(tokens) + " tokens");
        _usageLine.Visibility = Visibility.Visible;
        _usageView!.Visibility = _usageOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    /// <summary>
    /// 那一行要不要顯示：今天有用量、或圖已經展開、或正有 Claude Code 的工作階段連著。
    /// 今天還沒用（早上、剛過午夜）時圖只能從這一行打開，不能把它收掉。
    /// </summary>
    public static bool ShowsUsageLine(long todayTokens, bool chartOpen, bool claudeConnected) => todayTokens > 0 || chartOpen || claudeConnected;

    private void ToggleUsage(bool open)
    {
        _usageOpen = open;
        _trendToggles++;   // 之前排好的釋放都過期了
        if (!open)
        {
            _trendCts?.Cancel();   // 還在讀的就別讀了
            _ = ReleaseTrendLater(_trendToggles);
        }
        _usageView!.Visibility = open && _usageLine!.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (open && !Demo.On) LoadTrend();
        Morph();
    }

    /// <summary>最近 14 天的趨勢：在背景讀（第一次可能要一陣子，先顯示讀取中），讀完換上去。</summary>
    private async void LoadTrend()
    {
        _trendCts?.Cancel();
        var cts = _trendCts = new CancellationTokenSource();
        try
        {
            var trend = await _usage.TrendAsync(cancel: cts.Token);
            if (_usageOpen && !cts.IsCancellationRequested) { _usageView!.Trend = trend; Morph(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Write("usage: " + e.GetType().Name); }
    }

    /// <summary>圖表收起超過五分鐘還沒再打開，就把趨勢帳釋放掉（幾百則紀錄的記憶體）。ticket：收起那一刻的 _trendToggles。</summary>
    private async Task ReleaseTrendLater(int ticket)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5));   // 醒來時回到 UI 執行緒，讀 _trendToggles 不用鎖
            if (ReleasesTrend(ticket, _trendToggles, _usageOpen)) await _usage.ReleaseTrendAsync();
        }
        catch (Exception e) { Log.Write("usage: " + e.GetType().Name); }
    }

    /// <summary>
    /// 五分鐘到了要不要釋放：圖還關著，而且收起之後沒再開關過。
    /// 只看「現在關著」不夠：關、四分多鐘後重開、再關，第一次的計時醒來時圖也是關著的，會提早清掉剛剛才讀的帳。
    /// </summary>
    public static bool ReleasesTrend(int ticket, int toggles, bool chartOpen) => !chartOpen && ticket == toggles;

    private void AskAlways_Click(object sender, RoutedEventArgs e) => Decide(PermissionRequest.Always);

    // ---------------------------------------------------------------- 鬧鐘、喚醒詞

    private WakeWord? _wake;
    private volatile bool _musicPlaying;

    private void InitAlarms()
    {
        var alarms = new Alarms();
        alarms.Due += (alarm, missed) => Dispatcher.BeginInvoke(() =>
        {
            var due = alarm.Text.Length > 0 ? alarm.Text : L.T("時間到了");
            Remember(RecentKind.Reminder, L.T("提醒"), due, "");
            Toast(due, 20);
            if (!_expanded) SetExpanded(false);
        });
        Skills.Alarms = alarms;
        _alarms = alarms;
        alarms.Changed += () => Dispatcher.BeginInvoke(RefreshAlarms);
        Month.DayPicked += Month_DayPicked;
        RefreshAlarms();
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

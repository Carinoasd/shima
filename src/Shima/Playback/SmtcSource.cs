// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace Shima.Playback;

/// <summary>
/// Windows 的媒體控制：瀏覽器裡的 YouTube、Spotify、Apple Music 等等，只要會出現在音量面板的播放器都讀得到。
/// </summary>
public sealed class SmtcSource : IMediaSource
{
    private readonly object _lock = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Session? _session;
    private string _appId = "", _title = "", _artist = "", _album = "";
    private BitmapImage? _cover;
    private bool _playing;
    private TimeSpan _position, _duration;
    private DateTimeOffset _at;

    public event Action? Changed;

    /// <summary>不要跟的播放器（例如已經改走 Roon API 的 Roon）。</summary>
    public Func<string, bool>? Skip { get; set; }

    public Track? Current
    {
        get
        {
            lock (_lock)
            {
                if (_session == null || (_title.Length == 0 && _artist.Length == 0)) return null;
                return new Track(AppName(_appId), _appId, _title, _artist, _album, _cover, _playing, _position, _at, _duration);
            }
        }
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => Choose();
        _manager.CurrentSessionChanged += (_, _) => Choose();
        Choose();
    }

    /// <summary>挑要跟的播放器：目前這個還在播就不換，否則換到正在播的那一個。約每秒呼叫一次。</summary>
    public void Choose()
    {
        var manager = _manager;
        if (manager == null) return;
        try
        {
            var sessions = manager.GetSessions().Where(s => Skip?.Invoke(s.SourceAppUserModelId ?? "") != true).ToList();
            static bool Playing(Session s) => s.GetPlaybackInfo()?.PlaybackStatus == Status.Playing;

            Session? now;
            lock (_lock) now = _session;
            var mine = now == null ? null : sessions.FirstOrDefault(s => s.SourceAppUserModelId == now.SourceAppUserModelId);
            Session? pick = mine != null && Playing(mine) ? mine
                : sessions.FirstOrDefault(Playing) ?? mine ?? sessions.FirstOrDefault();
            if (pick?.SourceAppUserModelId != now?.SourceAppUserModelId || (pick == null) != (now == null)) Attach(pick);
        }
        catch (Exception e) { Log.Write("smtc: " + e.Message); }
    }

    private void Attach(Session? session)
    {
        lock (_lock)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnProperties;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = session;
            _appId = session?.SourceAppUserModelId ?? "";
            _title = _artist = _album = "";
            _cover = null;
            _playing = false;
            _position = _duration = TimeSpan.Zero;
            _at = default;
            if (session != null)
            {
                session.MediaPropertiesChanged += OnProperties;
                session.PlaybackInfoChanged += OnPlayback;
                session.TimelinePropertiesChanged += OnTimeline;
                ReadPlayback(session);
                ReadTimeline(session);
            }
        }
        Changed?.Invoke();
        if (session != null) _ = ReadPropertiesAsync(session);
    }

    private void OnProperties(Session s, MediaPropertiesChangedEventArgs e) => _ = ReadPropertiesAsync(s);

    private void OnPlayback(Session s, PlaybackInfoChangedEventArgs e)
    {
        lock (_lock) { if (s == _session) ReadPlayback(s); }
        Changed?.Invoke();
    }

    private void OnTimeline(Session s, TimelinePropertiesChangedEventArgs e)
    {
        lock (_lock) { if (s == _session) ReadTimeline(s); }
        Changed?.Invoke();
    }

    private void ReadPlayback(Session s)
    {
        try
        {
            bool playing = s.GetPlaybackInfo()?.PlaybackStatus == Status.Playing;
            var now = DateTimeOffset.Now;
            // 暫停的瞬間把推算的位置定住；恢復播放時從現在重新起算
            if (_playing && !playing && _at != default) _position += now - _at;
            if (_playing != playing) _at = now;
            _playing = playing;
        }
        catch { /* 播放器剛關掉 */ }
    }

    private void ReadTimeline(Session s)
    {
        try
        {
            var t = s.GetTimelineProperties();
            _position = t.Position;
            _duration = t.EndTime - t.StartTime;
            // 有些播放器不給回報時間（會是 1601 年），那就當作是現在
            _at = t.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : t.LastUpdatedTime;
        }
        catch { /* 播放器剛關掉 */ }
    }

    private async Task ReadPropertiesAsync(Session s)
    {
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (p == null) return;
            var cover = p.Thumbnail != null ? await LoadAsync(p.Thumbnail) : null;
            lock (_lock)
            {
                if (s != _session) return;
                _title = PageTitle.Clean(p.Title);
                _artist = Lyrics.Titles.Cap(p.Artist);
                _album = Lyrics.Titles.Cap(p.AlbumTitle);
                _cover = cover;
            }
            Changed?.Invoke();
        }
        catch { /* 播放器拒絕或剛關掉 */ }
    }

    private static async Task<BitmapImage?> LoadAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return Images.Decode(memory.ToArray());
        }
        catch { return null; }
    }

    public async void PlayPause() { try { if (_session is { } s) await s.TryTogglePlayPauseAsync(); } catch { } }
    public async void Play() { try { if (_session is { } s) await s.TryPlayAsync(); } catch { } }
    public async void Pause() { try { if (_session is { } s) await s.TryPauseAsync(); } catch { } }
    /// <summary>
    /// 要暫停哪些：所有正在播、而且不被 Skip 排除的工作階段（回傳在 sessions 裡的位置）。
    /// 不看有沒有標題，剛開始播、還沒讀到標題的也要暫停。
    /// </summary>
    public static List<int> PausePicks(IReadOnlyList<(string AppId, bool Playing)> sessions, Func<string, bool>? skip) =>
        Enumerable.Range(0, sessions.Count).Where(i => sessions[i].Playing && skip?.Invoke(sessions[i].AppId) != true).ToList();

    /// <summary>
    /// 同上，但每個工作階段的狀態各自讀：讀狀態是跨程序呼叫，剛關掉的分頁會丟例外，
    /// 讀不到的那個當成沒在播，不能讓它害其他正在播的一個都沒暫停。
    /// </summary>
    public static List<int> PausePicks<T>(IReadOnlyList<T> sessions, Func<T, (string AppId, bool Playing)> read, Func<string, bool>? skip)
    {
        var states = new (string AppId, bool Playing)[sessions.Count];
        for (int i = 0; i < sessions.Count; i++)
        {
            try { states[i] = read(sessions[i]); }
            catch { states[i] = ("", false); }
        }
        return PausePicks(states, skip);
    }

    private static (string AppId, bool Playing) ReadState(Session s) =>
        (s.SourceAppUserModelId ?? "", s.GetPlaybackInfo()?.PlaybackStatus == Status.Playing);

    private List<Session> SessionsToPause()
    {
        try
        {
            if (_manager?.GetSessions() is not { } all) return [];
            var list = all.ToList();
            return [.. PausePicks(list, ReadState, Skip).Select(i => list[i])];
        }
        catch { return []; /* 清單本身拿不到 */ }
    }

    /// <summary>所有正在播的都暫停（睡眠定時用），不只是島上跟著的那一個。</summary>
    public async void PauseAll()
    {
        foreach (var s in SessionsToPause()) { try { await s.TryPauseAsync(); } catch { /* 播放器剛關掉 */ } }
    }

    public async void Next() { try { if (_session is { } s) await s.TrySkipNextAsync(); } catch { } }
    public async void Previous() { try { if (_session is { } s) await s.TrySkipPreviousAsync(); } catch { } }
    public async void Seek(TimeSpan position) { try { if (_session is { } s) await s.TryChangePlaybackPositionAsync(position.Ticks); } catch { } }

    /// <summary>"Spotify.exe"、"Chrome"、"308046B0AF4A39CB"（Firefox）這類識別碼 → 給人看的名字。</summary>
    public static string AppName(string appId)
    {
        if (appId.Length == 0) return "";
        var name = appId;
        int bang = name.LastIndexOf('!');
        if (bang >= 0) name = name[(bang + 1)..];
        int crx = name.IndexOf("._crx_", StringComparison.OrdinalIgnoreCase);
        if (crx > 0) name = name[..crx];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length >= 12 && name.All(Uri.IsHexDigit)) return "Firefox";
        if (name.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        return name.Length == 0 ? "" : char.ToUpperInvariant(name[0]) + name[1..];
    }
}

public static class Images
{
    public static BitmapImage? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}

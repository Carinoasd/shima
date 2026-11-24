// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shima.Playback;

namespace Shima.Listening;

/// <summary>聽完（或聽夠）的一首歌：從什麼時候開始、實際聽了幾秒。</summary>
public sealed record Listen(
    [property: JsonPropertyName("start")] DateTimeOffset Start,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("album")] string Album,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("duration")] double Duration,
    [property: JsonPropertyName("listened")] double Listened);

/// <summary>
/// 判斷「一次聆聽」的純邏輯：不讀時鐘、不碰檔案，時間都由呼叫端傳進來。
/// 聽的秒數是用兩個 tick 之間實際經過的時間累加（只算前後都在播放的那段），
/// 不看播放位置，所以拖進度不會讓秒數暴增；一個 tick 最多算 MaxStep 秒，睡眠喚醒或畫面卡住也不會。
/// </summary>
public sealed class ListenTracker
{
    /// <summary>兩個 tick 之間最多算幾秒。</summary>
    public static readonly TimeSpan MaxStep = TimeSpan.FromSeconds(2);

    private Track? _track;
    private DateTime _start, _last;
    private bool _playing;
    private double _listened;
    private double _position = -1;

    /// <summary>目前這首累積聽了幾秒。</summary>
    public double Listened => _listened;
    /// <summary>目前在算的歌（還沒開始播就是 null）。</summary>
    public Track? Current => _track;

    /// <summary>算不算一次聆聽：至少 30 秒，或超過歌曲長度的一半（不知道長度就只看 30 秒）。</summary>
    public static bool Counts(double listened, double duration) =>
        listened >= 30 || (duration > 0 && listened > duration / 2);

    /// <summary>
    /// 每個 tick 呼叫一次。換歌、停止、或同一首從結尾跳回開頭（單曲循環）時，
    /// 上一首聽夠了就回傳它的紀錄，否則回傳 null。started 是這一刻開始算的新歌（給「正在播放」用）。
    /// </summary>
    public Listen? Observe(Track? track, DateTime now, out Track? started)
    {
        started = null;
        if (track != null && track.Title.Trim().Length == 0) track = null;

        if (track == null)
        {
            var done = Finish(now);
            Reset();
            return done;
        }

        Listen? result = null;
        double position = PositionAt(track, now);
        if (_track != null && !Same(_track, track))
        {
            result = Finish(now);
            Reset();
        }
        else if (_track != null && _playing && track.Playing && Restarted(_position, position, track.Duration.TotalSeconds) &&
                 Counts(_listened, Seconds(_track, track)))
        {
            // 單曲循環：同一首又從頭播了，前一遍算一次
            result = Finish(now);
            Reset();
        }

        if (_track == null)
        {
            if (!track.Playing) return result;   // 還沒真的播之前不開始算，開始時間才準
            _track = track;
            _start = now;
            _last = now;
            _playing = true;
            _position = position;
            started = track;
            return result;
        }

        var step = now - _last;
        if (_playing && track.Playing && step > TimeSpan.Zero) _listened += Math.Min(step.TotalSeconds, MaxStep.TotalSeconds);
        // 長度晚到：一開始回報 0、之後才補上，記最後知道的
        if (track.Duration > TimeSpan.Zero || _track.Duration <= TimeSpan.Zero) _track = track;
        _last = now;
        _playing = track.Playing;
        _position = position;
        return result;
    }

    /// <summary>程式結束時：目前這首聽夠了就交出紀錄。</summary>
    public Listen? Flush()
    {
        var done = Finish(_last);
        Reset();
        return done;
    }

    private Listen? Finish(DateTime now)
    {
        if (_track == null) return null;
        if (_playing && now > _last) _listened += Math.Min((now - _last).TotalSeconds, MaxStep.TotalSeconds);
        _last = now;
        double duration = Math.Round(Math.Max(0, _track.Duration.TotalSeconds), 3);
        double listened = Math.Round(duration > 0 ? Math.Min(_listened, duration) : _listened, 1);
        if (!Counts(listened, duration)) return null;
        return new Listen(new DateTimeOffset(_start), _track.Title, _track.Artist, _track.Album, _track.Source, duration, listened);
    }

    private void Reset()
    {
        _track = null;
        _listened = 0;
        _playing = false;
        _position = -1;
    }

    private static double Seconds(Track a, Track b) => Math.Max(a.Duration.TotalSeconds, b.Duration.TotalSeconds);

    /// <summary>
    /// 是不是同一首：歌名、藝人、專輯相同，長度一樣（四捨五入到秒）或其中一邊還不知道長度。
    /// 跟 Track.Key 一樣，只是長度晚到時不會被拆成兩首。
    /// </summary>
    public static bool Same(Track a, Track b)
    {
        if (a.Title != b.Title || a.Artist != b.Artist || a.Album != b.Album) return false;
        double x = a.Duration.TotalSeconds, y = b.Duration.TotalSeconds;
        return x <= 0 || y <= 0 || Math.Round(x) == Math.Round(y);
    }

    /// <summary>從後段跳回開頭幾秒：單曲循環或重播。</summary>
    public static bool Restarted(double before, double after, double duration) =>
        before >= 0 && after < 10 && before - after > Math.Max(20, duration > 0 ? duration / 2 : 0);

    /// <summary>某一刻的播放位置（不讀真的時鐘，跟 Track.Now 一樣的推算）。</summary>
    public static double PositionAt(Track track, DateTime now)
    {
        var pos = track.Position;
        if (track.Playing && track.At != default) pos += new DateTimeOffset(now) - track.At;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        if (track.Duration > TimeSpan.Zero && pos > track.Duration) pos = track.Duration;
        return pos.TotalSeconds;
    }
}

/// <summary>聆聽紀錄檔（JSON Lines，一行一首）的讀寫。</summary>
public static class HistoryFile
{
    public static string DefaultPath => Path.Combine(AppSettings.Folder, "history.jsonl");

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中日文直接寫，檔案打開看得懂
    };

    public static string ToLine(Listen listen) => JsonSerializer.Serialize(listen, Options);

    /// <summary>一行轉回紀錄；壞掉的行回傳 null。</summary>
    public static Listen? FromLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var listen = JsonSerializer.Deserialize<Listen>(line, Options);
            return listen is { Title: not null, Artist: not null, Album: not null, Source: not null } ? listen : null;
        }
        catch (JsonException) { return null; }
    }

    public static List<Listen> Parse(string text) =>
        text.Split('\n').Select(l => FromLine(l.Trim())).OfType<Listen>().ToList();

    /// <summary>
    /// 讀整個檔案的文字。允許別人同時寫入：背景讀的時候，UI 執行緒可能正好在附加新的一筆，
    /// 用預設的 FileShare.Read 開檔會讓後開的那一方因為共用衝突失敗（寫的一方失敗就丟了那筆紀錄）。
    /// 讀到的最後一行可能只寫了一半，Parse 會略過（壞掉的 JSON 行不算），下次檔案變大再讀就完整了。
    /// </summary>
    public static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>讀整個紀錄檔；沒有檔案或讀不到就是空的。</summary>
    public static List<Listen> Read(string? path = null, Action<string>? log = null)
    {
        path ??= DefaultPath;
        try { return File.Exists(path) ? Parse(ReadText(path)) : []; }
        catch (Exception e) { (log ?? Log.Write)("history read: " + e.Message); return []; }
    }
}

/// <summary>
/// 記住上一次讀進來的聆聽紀錄：檔案的大小與修改時間都沒變就直接給同一份清單，不再整檔重讀、重新解析。
/// 任何執行緒都可以呼叫（同一時間只讀一次）。讀失敗不記，下次再試；失敗時先給上一份。
/// </summary>
public sealed class HistoryCache
{
    private readonly object _gate = new();
    private string? _path;
    private long _size = -1;
    private long _stamp;
    private List<Listen> _listens = [];

    /// <summary>Changed 為 false 表示檔案沒變，Listens 就是上次給的同一個物件。</summary>
    public (List<Listen> Listens, bool Changed) Read(string? path = null, Action<string>? log = null)
    {
        path ??= HistoryFile.DefaultPath;
        lock (_gate)
        {
            long size = -1, stamp = 0;
            try
            {
                var info = new FileInfo(path);
                if (info.Exists) { size = info.Length; stamp = info.LastWriteTimeUtc.Ticks; }
            }
            catch (Exception e) { (log ?? Log.Write)("history stat: " + e.Message); return (_listens, false); }

            if (path == _path && size == _size && stamp == _stamp) return (_listens, false);
            try
            {
                // 先量再讀：讀的當中檔案又長了，下次比對會不同，就會再讀一次
                _listens = size < 0 ? [] : HistoryFile.Parse(HistoryFile.ReadText(path));
                _path = path; _size = size; _stamp = stamp;
                return (_listens, true);
            }
            catch (Exception e)
            {
                (log ?? Log.Write)("history read: " + e.Message);
                return (_listens, false);
            }
        }
    }
}

/// <summary>
/// 聆聽紀錄：整合者每個 tick 呼叫 Observe，聽夠的歌附加寫進 history.jsonl。寫檔失敗只記錄，不丟例外。
/// </summary>
public sealed class ListeningLog
{
    private readonly ListenTracker _tracker = new();
    private readonly object _gate = new();
    private readonly Action<string> _log;

    public string FilePath { get; }

    /// <summary>這個來源算不算（被排除的來源不寫檔、不通知）；預設全算。</summary>
    public Func<string, bool> Accepts { get; set; } = _ => true;

    /// <summary>寫進檔案的每一筆（在呼叫 Observe／Flush 的執行緒上觸發）。</summary>
    public event Action<Listen>? Listened;
    /// <summary>開始算一首新歌（真的開始播放的那一刻）。</summary>
    public event Action<Track>? Started;

    public ListeningLog(string? path = null, Action<string>? log = null)
    {
        FilePath = path ?? HistoryFile.DefaultPath;
        _log = log ?? Log.Write;
    }

    public void Observe(Track? track, DateTime now)
    {
        Listen? done;
        Track? started;
        lock (_gate) done = _tracker.Observe(track, now, out started);
        if (done != null) Write(done);
        if (started != null && Accepts(started.Source)) Raise(() => Started?.Invoke(started));
    }

    /// <summary>程式結束時呼叫：目前這首聽夠了就寫下來。</summary>
    public void Flush()
    {
        Listen? done;
        lock (_gate) done = _tracker.Flush();
        if (done != null) Write(done);
    }

    private void Write(Listen listen)
    {
        if (!Accepts(listen.Source)) return;
        var line = HistoryFile.ToLine(listen) + "\n";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
                    File.AppendAllText(FilePath, line, new UTF8Encoding(false));
                }
                break;
            }
            catch (IOException) when (attempt < 3) { Thread.Sleep(25); }   // 別人正好開著檔：等一下再試，不要丟了這一筆
            catch (Exception e) { _log("history write: " + e.Message); break; }
        }
        Raise(() => Listened?.Invoke(listen));
    }

    private void Raise(Action raise)
    {
        try { raise(); }
        catch (Exception e) { _log("history event: " + e.Message); }
    }
}

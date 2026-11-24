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

/// <summary>一筆回報的結果：送成功、暫時送不出去（留著以後再送）、被服務明確拒絕（不會因為再送而成功，丟掉）。</summary>
public enum SubmitResult { Sent, Retry, Rejected }

/// <summary>
/// 還沒回報出去的聆聽，存在 pending-scrobbles.jsonl（每行一筆：目的地＋那一筆聆聽，不含任何金鑰）。
/// 每個目的地（listenbrainz、lastfm）各自一份、互不影響：最多 <see cref="MaxPerDestination"/> 筆（超過丟最舊的）、
/// 同一首同一個開始時間只留一筆、超過 <see cref="MaxAge"/> 還沒送出去的丟掉。
/// 每次變動都整份寫成暫存檔再換上去，中途當掉不會留下半份；讀的時候壞掉的行略過。可以從任何執行緒呼叫。
/// </summary>
public sealed class PendingScrobbles
{
    public const int MaxPerDestination = 500;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    public static string DefaultPath => Path.Combine(AppSettings.Folder, "pending-scrobbles.jsonl");

    private sealed record Line(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("listen")] Listen Listen);

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;
    private readonly List<(string To, Listen Listen)> _items = [];

    public PendingScrobbles(string? path = null, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
    {
        _path = path ?? DefaultPath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? Log.Write;
        Load();
    }

    /// <summary>同一首同一個開始時間（兩個服務的時間戳記都是秒）算同一筆。</summary>
    public static string Key(Listen l) => $"{l.Start.ToUnixTimeSeconds()}|{l.Artist.Trim()}|{l.Title.Trim()}";

    /// <summary>某個目的地還沒送的，開始時間早的在前（補送要照原本的順序）。</summary>
    public List<Listen> Items(string to)
    {
        lock (_gate)
        {
            Prune();
            return _items.Where(i => i.To == to).Select(i => i.Listen).OrderBy(l => l.Start).ToList();
        }
    }

    public bool Contains(string to, Listen listen)
    {
        var key = Key(listen);
        lock (_gate) return _items.Any(i => i.To == to && Key(i.Listen) == key);
    }

    /// <summary>加進待送清單；已經有、或已經過期就不加（回傳 false）。</summary>
    public bool Add(string to, Listen listen)
    {
        lock (_gate)
        {
            if (listen.Start < _clock() - MaxAge) return false;
            if (_items.Any(i => i.To == to && Key(i.Listen) == Key(listen))) return false;
            _items.Add((to, listen));
            Prune();
            Save();
            return true;
        }
    }

    public void Remove(string to, Listen listen)
    {
        var key = Key(listen);
        lock (_gate)
        {
            if (_items.RemoveAll(i => i.To == to && Key(i.Listen) == key) > 0) Save();
        }
    }

    /// <summary>中斷連接：那個目的地的全部丟掉，別的不動。</summary>
    public void Clear(string to)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(i => i.To == to) > 0) Save();
        }
    }

    /// <summary>過期的丟掉、每個目的地超過上限的丟最舊的。呼叫的人要持有 _gate；有變動才寫檔。</summary>
    private void Prune()
    {
        var limit = _clock() - MaxAge;
        bool changed = _items.RemoveAll(i => i.Listen.Start < limit) > 0;
        foreach (var to in _items.Select(i => i.To).Distinct().ToList())
        {
            var mine = _items.Where(i => i.To == to).OrderBy(i => i.Listen.Start).ToList();
            foreach (var old in mine.Take(Math.Max(0, mine.Count - MaxPerDestination))) { _items.Remove(old); changed = true; }
        }
        if (changed) Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var text in File.ReadAllText(_path, Encoding.UTF8).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                try
                {
                    var line = JsonSerializer.Deserialize<Line>(text.Trim(), Options);
                    if (line is { To.Length: > 0, Listen: { Title: not null, Artist: not null, Album: not null, Source: not null } listen }
                        && !_items.Any(i => i.To == line.To && Key(i.Listen) == Key(listen)))
                        _items.Add((line.To, listen));
                }
                catch (JsonException) { }   // 壞掉的行略過
            }
        }
        catch (Exception e) { _log("pending read: " + e.Message); }
        lock (_gate) Prune();
    }

    /// <summary>先寫暫存檔再換上去：當掉的時候不是舊的整份就是新的整份。</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, string.Concat(_items.Select(i => JsonSerializer.Serialize(new Line(i.To, i.Listen), Options) + "\n")), new UTF8Encoding(false));
            File.Move(temp, _path, true);
        }
        catch (Exception e) { _log("pending write: " + e.Message); }
    }
}

/// <summary>
/// 在回報目的地外面包一層：要送的先寫進待送清單，送成功或確定不該再送才拿掉；
/// 暫時送不出去的留著，程式啟動時（<see cref="Drain"/>）與之後每次有新的一筆時，照時間順序補送。
/// 補送遇到暫時的失敗就停（離線時不要一首一首都試），兩筆之間的間隔由裡面那個服務自己控制。
/// 「正在播放」不進清單（過了就沒意義）。
/// </summary>
public sealed class QueuedSink : IListenSink
{
    private readonly string _name;
    private readonly IListenSink _inner;
    private readonly PendingScrobbles _pending;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, SubmitResult?> _awaiting = [];

    /// <param name="name">這個目的地在待送清單裡的名字（listenbrainz、lastfm）。</param>
    public QueuedSink(string name, IListenSink inner, PendingScrobbles pending, Action<string>? log = null)
    {
        _name = name;
        _inner = inner;
        _pending = pending;
        _log = log ?? Log.Write;
    }

    public bool Enabled => _inner.Enabled;
    public bool Wants(Listen listen) => _inner.Wants(listen);
    public Task NowPlaying(Track track) => _inner.NowPlaying(track);

    public async Task<bool> Submit(Listen listen) => await Deliver(listen).ConfigureAwait(false) == SubmitResult.Sent;

    public async Task<SubmitResult> Deliver(Listen listen)
    {
        if (!_inner.Wants(listen)) return SubmitResult.Rejected;
        var key = PendingScrobbles.Key(listen);
        lock (_awaiting) _awaiting[key] = null;
        _pending.Add(_name, listen);
        await Drain().ConfigureAwait(false);
        lock (_awaiting)
        {
            _awaiting.Remove(key, out var result);
            return result ?? SubmitResult.Retry;
        }
    }

    /// <summary>補送時連續幾筆都被明確拒絕，就當成是整個系統的問題（設定、帳號、請求格式），這一次停下來、一筆都不刪。</summary>
    public const int RejectedStreak = 3;

    /// <summary>
    /// 把清單裡還沒送的依序補送，直到送完或遇到暫時的失敗。被拒絕的先不刪：後面有一筆送成功，才證明它是那一筆自己的問題；
    /// 連續 <see cref="RejectedStreak"/> 筆都被拒，是服務對每一筆都說不行，全部留著、這一次不再往下送。
    /// </summary>
    public async Task Drain()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var rejected = new List<Listen>();   // 被拒絕、還沒刪的（照順序排在清單最前面）
            var skipped = new HashSet<string>();
            bool exhausted = false;
            while (_inner.Enabled)
            {
                var head = _pending.Items(_name).FirstOrDefault(i => !skipped.Contains(PendingScrobbles.Key(i)));
                if (head == null) { exhausted = true; break; }
                var key = PendingScrobbles.Key(head);
                SubmitResult result;
                try { result = _inner.Wants(head) ? await _inner.Deliver(head).ConfigureAwait(false) : SubmitResult.Rejected; }
                catch (Exception e) { _log($"pending {_name}: {e.GetType().Name} {e.Message}"); result = SubmitResult.Retry; }
                if (result == SubmitResult.Retry)
                {
                    lock (_awaiting) { if (_awaiting.ContainsKey(key)) _awaiting[key] = SubmitResult.Retry; }
                    break;
                }
                if (result == SubmitResult.Rejected)
                {
                    rejected.Add(head);
                    skipped.Add(key);
                    if (rejected.Count >= RejectedStreak)
                    {
                        _log($"pending {_name}: {rejected.Count} 筆連續被拒絕，全部留著，不再往下送");
                        lock (_awaiting) foreach (var r in rejected) { var k = PendingScrobbles.Key(r); if (_awaiting.ContainsKey(k)) _awaiting[k] = SubmitResult.Retry; }
                        return;
                    }
                    continue;
                }
                // 送成功了：前面被拒的確實是各自的問題，一起拿掉
                foreach (var r in rejected) Settle(r, SubmitResult.Rejected);
                rejected.Clear();
                skipped.Clear();
                Settle(head, result);
            }
            // 清單送到底了：不到連續被拒的上限，照原樣拿掉（單獨一筆新的被拒絕就是這個情形）；中途遇到暫時的失敗就先留著
            if (exhausted) foreach (var r in rejected) Settle(r, SubmitResult.Rejected);
        }
        finally { _gate.Release(); }
    }

    private void Settle(Listen listen, SubmitResult result)
    {
        _pending.Remove(_name, listen);
        lock (_awaiting) { var k = PendingScrobbles.Key(listen); if (_awaiting.ContainsKey(k)) _awaiting[k] = result; }
    }
}

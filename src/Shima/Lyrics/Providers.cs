// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shima.Lyrics;

public sealed record SongQuery(string Title, string Artist, string Album, double Duration);

/// <param name="Exact">確定是這首歌的這個版本；false 表示只找到同一首歌的原曲。</param>
public sealed record LyricsHit(LyricSheet Sheet, bool Exact, string Provider);

/// <summary>一個歌詞來源。要加新的來源就實作這個介面，再登記到 <see cref="LyricsFinder.Known"/>。</summary>
public interface ILyricsProvider
{
    string Name { get; }
    Task<LyricsHit?> FindAsync(SongQuery q);
}

/// <summary>
/// 自己的 .lrc 檔：%APPDATA%\Shima\lyrics 以及設定裡 LyricsFolders 的資料夾（含子資料夾，例如音樂庫）。
/// 檔名是「歌名」「藝人 - 歌名」或「01 歌名」都認得。
/// </summary>
public sealed partial class LocalProvider : ILyricsProvider
{
    public string Name => "local";
    private Dictionary<string, string>? _index;
    private DateTime _indexedAt;

    public static string Folder => Path.Combine(AppSettings.Folder, "lyrics");

    public Task<LyricsHit?> FindAsync(SongQuery q) => Task.Run(() =>
    {
        var index = Index();
        foreach (var key in Keys(q))
        {
            if (!index.TryGetValue(key, out var path)) continue;
            try
            {
                var sheet = Lrc.ParseSheet(File.ReadAllText(path));
                if (sheet.Lines.Count > 0) return new LyricsHit(sheet, true, Name);
            }
            catch (Exception e) { Log.Write("歌詞檔讀取失敗: " + e.Message); }
        }
        return (LyricsHit?)null;
    });

    /// <summary>一首歌可能的檔名（已正規化），最明確的在前面。</summary>
    public static IEnumerable<string> Keys(SongQuery q)
    {
        string title = Titles.Norm(q.Title), artist = Titles.Norm(Titles.MainArtist(q.Artist));
        if (title.Length == 0) yield break;
        if (artist.Length > 0) { yield return artist + title; yield return title + artist; }
        yield return title;
    }

    /// <summary>檔名 → 索引鍵：正規化後，開頭的曲目編號另外再存一份沒有編號的。</summary>
    public static IEnumerable<string> KeysOf(string fileName)
    {
        var key = Titles.Norm(Path.GetFileNameWithoutExtension(fileName));
        if (key.Length == 0) yield break;
        yield return key;
        var trimmed = key.TrimStart("0123456789".ToCharArray());
        if (trimmed.Length > 0 && trimmed.Length < key.Length) yield return trimmed;
    }

    /// <summary>
    /// 存歌詞用的檔名：「藝人 - 歌名.lrc」（沒有藝人就是「歌名.lrc」），<see cref="Keys"/> 一定認得。
    /// Windows 不允許的字元換成底線（比對時本來就不看標點）；認不出這首歌（歌名全是符號）就是 null。
    /// </summary>
    public static string? FileName(SongQuery q)
    {
        if (!Keys(q).Any()) return null;
        var artist = Titles.MainArtist(q.Artist);
        var name = Safe(artist.Length > 0 ? $"{artist} - {q.Title}" : q.Title);
        // 太長的話只用歌名（Windows 的路徑有長度上限）
        if (name.Length > 150) name = Safe(q.Title);
        if (name.Length > 150) name = name[..150].TrimEnd(' ', '.');
        if (name.Length == 0 || !KeysOf(name + ".lrc").Intersect(Keys(q)).Any()) return null;
        // CON、NUL、COM1 這些是裝置名稱，不能當檔名
        if (Reserved().IsMatch(name)) name = "_" + name;
        return name + ".lrc";
    }

    private static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var safe = new string(name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        return safe.Trim().TrimEnd('.', ' ');
    }

    /// <summary>
    /// 寫進 folder：已經有同名的檔案、而且還沒有 .bak 時先備份再覆寫。.bak 只寫一次：
    /// 留住的是最原本的那一份（可能是使用者自己放的），第二次存檔不會拿自己上一次寫的把它蓋掉。檔名帶路徑、會跑出資料夾的一律不寫。回傳寫好的完整路徑。
    /// </summary>
    public static string Save(string folder, string fileName, string lrc)
    {
        var root = Path.GetFullPath(folder);
        var path = Path.GetFullPath(Path.Combine(root, fileName));
        if (Path.GetFileName(fileName) != fileName || !string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("檔名不合法: " + fileName);
        Directory.CreateDirectory(root);
        if (File.Exists(path) && !File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
        // 先寫暫存檔再換上去：寫到一半當掉也不會留下半個檔案；加 BOM，其他播放器才認得是 UTF-8
        File.WriteAllText(path + ".tmp", lrc, new System.Text.UTF8Encoding(true));
        File.Move(path + ".tmp", path, overwrite: true);
        return path;
    }

    /// <summary>資料夾裡剛多了檔案：下次查的時候重新掃一次。</summary>
    public void Reindex()
    {
        lock (this) _index = null;
    }

    [GeneratedRegex(@"^(?:con|prn|aux|nul|com\d|lpt\d)(?:\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();

    private Dictionary<string, string> Index()
    {
        lock (this)
        {
            if (_index != null && DateTime.Now - _indexedAt < TimeSpan.FromMinutes(5)) return _index;
            var index = new Dictionary<string, string>();
            Directory.CreateDirectory(Folder);
            // 先複製一份：這裡在背景執行緒，使用者同時在設定頁加減資料夾的話，邊走邊改會出錯
            foreach (var root in AppSettings.Current.LyricsFolders.ToArray().Prepend(Folder))
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var file in Directory.EnumerateFiles(root, "*.lrc", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                        foreach (var key in KeysOf(file))
                            index.TryAdd(key, file);
                }
                catch (Exception e) { Log.Write("歌詞資料夾: " + e.Message); }
            }
            _indexedAt = DateTime.Now;
            return _index = index;
        }
    }
}

/// <summary>LRCLIB（lrclib.net）：免費、不用金鑰、社群維護的同步歌詞。</summary>
public sealed class LrclibProvider : ILyricsProvider
{
    public string Name => "lrclib";
    private readonly HttpClient Http;

    private static readonly Lazy<HttpClient> Shared = new(() => Client(null));

    /// <param name="handler">測試用：換掉真的網路。</param>
    public LrclibProvider(HttpMessageHandler? handler = null) => Http = handler == null ? Shared.Value : Client(handler);

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var http = HttpLimits.Create(TimeSpan.FromSeconds(12), handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Shima/0.1");
        return http;
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        static string E(string s) => Uri.EscapeDataString(s);
        var artist = Titles.MainArtist(q.Artist);

        // 歌名、藝人、長度完全對上
        if (q.Artist.Length > 0 && q.Duration > 0)
        {
            using var doc = await GetAsync($"get?track_name={E(q.Title)}&artist_name={E(q.Artist)}&duration={Math.Round(q.Duration)}" +
                                           (q.Album.Length > 0 ? $"&album_name={E(q.Album)}" : ""));
            if (doc != null && Sheet(doc.RootElement) is { } sheet) return new LyricsHit(sheet, true, Name);
        }

        // 搜尋：完整歌名，再來是去掉結尾括號的歌名（LRCLIB 的「104th Ver.」寫法常跟播放器的「104期Ver.」不同）
        LyricsHit? approximate = null;
        var bare = Titles.Bare(q.Title);
        foreach (var path in new[]
        {
            artist.Length > 0 ? $"search?track_name={E(q.Title)}&artist_name={E(artist)}" : null,
            artist.Length > 0 && bare != q.Title ? $"search?track_name={E(bare)}&artist_name={E(artist)}" : null,
            $"search?q={E((artist + " " + bare).Trim())}",
        })
        {
            if (path == null) continue;
            using var doc = await GetAsync(path);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array) continue;
            var items = doc.RootElement.EnumerateArray().ToList();
            var (index, exact) = SongPicker.Pick(items.Select(Candidate).ToList(), q.Title, artist, q.Duration);
            if (index < 0 || Sheet(items[index]) is not { } sheet) continue;
            if (exact) return new LyricsHit(sheet, true, Name);
            approximate ??= new LyricsHit(sheet, false, Name);
        }
        return approximate;
    }

    private async Task<JsonDocument?> GetAsync(string path)
    {
        using var response = await Http.GetAsync("https://lrclib.net/api/" + path, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return null;
        return await HttpLimits.ReadAsync(response) is { } body ? JsonDocument.Parse(body) : null;
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static SongCandidate Candidate(JsonElement e) => new(
        Text(e, "trackName"), Text(e, "artistName"),
        e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0,
        Text(e, "syncedLyrics").Length > 0 ? 3 : Text(e, "plainLyrics").Length > 0 ? 2 : 0);

    private static LyricSheet? Sheet(JsonElement e)
    {
        if (Text(e, "syncedLyrics").Length > HttpLimits.MaxText || Text(e, "plainLyrics").Length > HttpLimits.MaxText) return null;
        if (Lrc.Parse(Text(e, "syncedLyrics")) is { Count: > 0 } synced) return new LyricSheet(synced, true);
        if (Lrc.Plain(Text(e, "plainLyrics")) is { Count: > 0 } plain) return new LyricSheet(plain, false);
        return null;
    }
}

/// <summary>網易雲音樂：ACGN 的歌很齊，同一首歌的各個版本分開收錄。非官方介面，不用帳號。</summary>
public sealed class NetEaseProvider : ILyricsProvider
{
    public string Name => "netease";
    private readonly HttpClient Http;

    private static readonly Lazy<HttpClient> Shared = new(() => Client(null));

    /// <param name="handler">測試用：換掉真的網路。</param>
    public NetEaseProvider(HttpMessageHandler? handler = null) => Http = handler == null ? Shared.Value : Client(handler);

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var http = HttpLimits.Create(TimeSpan.FromSeconds(10), handler);
        // 這個站只回應來自它自己網頁的請求
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        return http;
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        var artist = Titles.MainArtist(q.Artist);
        if (await HttpLimits.GetStringAsync(Http, $"https://music.163.com/api/search/get?s={Uri.EscapeDataString((q.Title + " " + artist).Trim())}&type=1&limit=20") is not { } json) return null;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array) return null;

        var ids = new List<long>();
        var items = new List<SongCandidate>();
        foreach (var s in songs.EnumerateArray())
        {
            ids.Add(s.GetProperty("id").GetInt64());
            var artists = s.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                ? string.Join(" ", a.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : "")) : "";
            items.Add(new SongCandidate(
                s.TryGetProperty("name", out var t) ? t.GetString() ?? "" : "", artists,
                s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() / 1000 : 0));
        }

        var (index, exact) = SongPicker.Pick(items, q.Title, artist, q.Duration);
        if (index < 0) return null;

        // yv=1：有逐字歌詞（yrc）的話一起給
        if (await HttpLimits.GetStringAsync(Http, $"https://music.163.com/api/song/lyric?id={ids[index]}&lv=1&tv=-1&rv=-1&yv=1") is not { } lyric) return null;
        using var l = JsonDocument.Parse(lyric);
        if (!l.RootElement.TryGetProperty("lrc", out var lrc) || !lrc.TryGetProperty("lyric", out var text) || text.ValueKind != JsonValueKind.String) return null;
        if (text.GetString()!.Length > HttpLimits.MaxText) return null;
        var lines = Lrc.Parse(Lrc.StripCredits(text.GetString()!));
        if (lines.Count == 0) return null;

        string Extra(string name) =>
            l.RootElement.TryGetProperty(name, out var e) && e.TryGetProperty("lyric", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { } x && x.Length <= HttpLimits.MaxText ? x : "";
        // 句子還是照 LRC，翻譯才對得上；逐字時間只是掛上去
        if (Extra("yrc") is { Length: > 0 } yrc) lines = WordTiming.Attach(lines, WordTiming.ParseYrc(yrc));
        var sheet = new LyricSheet(lines, true)
        {
            Translation = Lrc.Align(lines, Lrc.Parse(Chinese.Localize(Lrc.StripCredits(Extra("tlyric"))))),
            Romaji = Lrc.Align(lines, Lrc.Parse(Lrc.StripCredits(Extra("romalrc")))),
        };
        return new LyricsHit(sheet, exact, Name);
    }
}

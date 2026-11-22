// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net.Http;
using System.Text.Json;

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
public sealed class LocalProvider : ILyricsProvider
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
                var lines = Lrc.Parse(File.ReadAllText(path));
                if (lines.Count > 0) return new LyricsHit(new LyricSheet(lines, true), true, Name);
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

    private Dictionary<string, string> Index()
    {
        lock (this)
        {
            if (_index != null && DateTime.Now - _indexedAt < TimeSpan.FromMinutes(5)) return _index;
            var index = new Dictionary<string, string>();
            Directory.CreateDirectory(Folder);
            foreach (var root in AppSettings.Current.LyricsFolders.Prepend(Folder))
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
    private static readonly HttpClient Http = Client();

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
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

    private static async Task<JsonDocument?> GetAsync(string path)
    {
        using var response = await Http.GetAsync("https://lrclib.net/api/" + path);
        if (!response.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static SongCandidate Candidate(JsonElement e) => new(
        Text(e, "trackName"), Text(e, "artistName"),
        e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0,
        Text(e, "syncedLyrics").Length > 0 ? 3 : Text(e, "plainLyrics").Length > 0 ? 2 : 0);

    private static LyricSheet? Sheet(JsonElement e)
    {
        if (Lrc.Parse(Text(e, "syncedLyrics")) is { Count: > 0 } synced) return new LyricSheet(synced, true);
        if (Lrc.Plain(Text(e, "plainLyrics")) is { Count: > 0 } plain) return new LyricSheet(plain, false);
        return null;
    }
}

/// <summary>網易雲音樂：ACGN 的歌很齊，同一首歌的各個版本分開收錄。非官方介面，不用帳號。</summary>
public sealed class NetEaseProvider : ILyricsProvider
{
    public string Name => "netease";
    private static readonly HttpClient Http = Client();

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // 這個站只回應來自它自己網頁的請求
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        http.DefaultRequestHeaders.Referrer = new Uri("https://music.163.com/");
        return http;
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        var artist = Titles.MainArtist(q.Artist);
        var json = await Http.GetStringAsync($"https://music.163.com/api/search/get?s={Uri.EscapeDataString((q.Title + " " + artist).Trim())}&type=1&limit=20");
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

        var lyric = await Http.GetStringAsync($"https://music.163.com/api/song/lyric?id={ids[index]}&lv=1&tv=-1&rv=-1");
        using var l = JsonDocument.Parse(lyric);
        if (!l.RootElement.TryGetProperty("lrc", out var lrc) || !lrc.TryGetProperty("lyric", out var text) || text.ValueKind != JsonValueKind.String) return null;
        var lines = Lrc.Parse(Lrc.StripCredits(text.GetString()!));
        if (lines.Count == 0) return null;

        string Extra(string name) =>
            l.RootElement.TryGetProperty(name, out var e) && e.TryGetProperty("lyric", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
        var sheet = new LyricSheet(lines, true)
        {
            Translation = Lrc.Align(lines, Lrc.Parse(Chinese.Localize(Lrc.StripCredits(Extra("tlyric"))))),
            Romaji = Lrc.Align(lines, Lrc.Parse(Lrc.StripCredits(Extra("romalrc")))),
        };
        return new LyricsHit(sheet, exact, Name);
    }
}

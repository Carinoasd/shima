// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Shima.Lyrics;

/// <summary>QQ 音樂：同一首歌的各個版本分開收錄，歌名帶版本。非官方介面，不用帳號。</summary>
public sealed class QQProvider : ILyricsProvider
{
    public string Name => "qq";
    private static readonly HttpClient Http = Client();

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        http.DefaultRequestHeaders.Referrer = new Uri("https://y.qq.com/");
        return http;
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        var artist = Titles.MainArtist(q.Artist);
        var body = JsonSerializer.Serialize(new
        {
            req = new
            {
                method = "DoSearchForQQMusicDesktop",
                module = "music.search.SearchCgiService",
                param = new { num_per_page = 20, page_num = 1, query = (q.Title + " " + artist).Trim(), search_type = 0 },
            },
        });
        using var response = await Http.PostAsync("https://u.y.qq.com/cgi-bin/musicu.fcg", new StringContent(body, Encoding.UTF8, "application/json"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("req", out var req) || !req.TryGetProperty("data", out var data) || !data.TryGetProperty("body", out var b) ||
            !b.TryGetProperty("song", out var song) || !song.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return null;

        var mids = new List<string>();
        var items = new List<SongCandidate>();
        foreach (var s in list.EnumerateArray())
        {
            string Get(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            var singers = s.TryGetProperty("singer", out var sg) && sg.ValueKind == JsonValueKind.Array
                ? string.Join(" ", sg.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() : "")) : "";
            mids.Add(Get("mid"));
            // title 帶版本（「Dream Believers (SAKURA Ver.)」），name 只有歌名
            items.Add(new SongCandidate(Get("title") is { Length: > 0 } t ? t : Get("name"), singers,
                s.TryGetProperty("interval", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0));
        }
        var (index, exact) = SongPicker.Pick(items, q.Title, artist, q.Duration);
        if (index < 0 || mids[index].Length == 0) return null;

        var json = await Http.GetStringAsync($"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={mids[index]}&format=json&nobase64=1&g_tk=5381");
        using var l = JsonDocument.Parse(json);
        string Lyric(string name) => l.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? WebUtility.HtmlDecode(v.GetString() ?? "") : "";
        var lines = Lrc.Parse(Lrc.StripCredits(Lyric("lyric"), items[index].Title));
        if (lines.Count == 0) return null;
        var sheet = new LyricSheet(lines, true) { Translation = Lrc.Align(lines, Lrc.Parse(Chinese.ToTraditional(Lrc.StripCredits(Lyric("trans"))))) };
        return new LyricsHit(sheet, exact, Name);
    }
}

/// <summary>酷狗音樂。它的歌名常常不帶版本，所以多半只能當原曲的備援。非官方介面，不用帳號。</summary>
public sealed class KugouProvider : ILyricsProvider
{
    public string Name => "kugou";
    private static readonly HttpClient Http = Client();

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        return http;
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        var artist = Titles.MainArtist(q.Artist);
        var found = await Http.GetStringAsync($"http://mobilecdn.kugou.com/api/v3/search/song?keyword={Uri.EscapeDataString((q.Title + " " + artist).Trim())}&page=1&pagesize=20&format=json");
        using var doc = JsonDocument.Parse(found);
        if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Array) return null;

        var hashes = new List<string>();
        var items = new List<SongCandidate>();
        foreach (var s in info.EnumerateArray())
        {
            string Get(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            hashes.Add(Get("hash"));
            items.Add(new SongCandidate(Get("songname"), Get("singername"), s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0));
        }
        var (index, exact) = SongPicker.Pick(items, q.Title, artist, q.Duration);
        if (index < 0 || hashes[index].Length == 0) return null;

        // 兩步：先用歌的雜湊查到歌詞的編號與金鑰，再下載
        var search = await Http.GetStringAsync($"http://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration={(long)(items[index].Duration * 1000)}&hash={hashes[index]}");
        using var s2 = JsonDocument.Parse(search);
        if (!s2.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return null;
        var first = candidates[0];
        string Field(string name) => first.TryGetProperty(name, out var v) ? v.ToString() : "";
        var download = await Http.GetStringAsync($"http://lyrics.kugou.com/download?ver=1&client=pc&id={Field("id")}&accesskey={Field("accesskey")}&fmt=lrc&charset=utf8");
        using var d2 = JsonDocument.Parse(download);
        if (!d2.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return null;
        var lrc = Encoding.UTF8.GetString(Convert.FromBase64String(content.GetString() ?? ""));
        var lines = Lrc.Parse(Lrc.StripCredits(lrc, items[index].Title));
        return lines.Count > 0 ? new LyricsHit(new LyricSheet(lines, true), exact, Name) : null;
    }
}

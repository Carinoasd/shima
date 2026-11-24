// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Shima.Lyrics;

/// <summary>QQ 音樂：同一首歌的各個版本分開收錄，歌名帶版本。非官方介面，不用帳號。</summary>
public sealed class QQProvider : ILyricsSearchProvider
{
    public string Name => "qq";
    private readonly HttpClient Http;

    private static readonly Lazy<HttpClient> Shared = new(() => Client(null));

    /// <param name="handler">測試用：換掉真的網路。</param>
    public QQProvider(HttpMessageHandler? handler = null) => Http = handler == null ? Shared.Value : Client(handler);

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var http = HttpLimits.Create(TimeSpan.FromSeconds(10), handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        http.DefaultRequestHeaders.Referrer = new Uri("https://y.qq.com/");
        return http;
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        var artist = Titles.MainArtist(q.Artist);
        var (mids, items) = await QuerySongsAsync((q.Title + " " + artist).Trim(), 20, CancellationToken.None);
        var (index, exact) = SongPicker.Pick(items, q.Title, artist, q.Duration, q.Tolerance);
        if (index < 0 || mids[index].Length == 0) return null;
        var sheet = await FetchLyricAsync(mids[index], items[index].Title, CancellationToken.None);
        return sheet == null ? null : new LyricsHit(sheet, exact, Name);
    }

    public async Task<List<LyricsChoice>> SearchAsync(string keyword, CancellationToken stop = default)
    {
        keyword = Titles.Cap(keyword).Trim();
        if (keyword.Length == 0) return [];
        var (mids, items) = await QuerySongsAsync(keyword, 10, stop);
        var list = new List<LyricsChoice>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var mid = mids[i];
            var item = items[i];
            if (mid.Length == 0 || item.Title.Length == 0) continue;
            list.Add(new LyricsChoice(item.Title, item.Artist, item.Duration, Name, true, ct => FetchLyricAsync(mid, item.Title, ct)));
        }
        return list;
    }

    private async Task<(List<string> Mids, List<SongCandidate> Items)> QuerySongsAsync(string keyword, int limit, CancellationToken stop)
    {
        var mids = new List<string>();
        var items = new List<SongCandidate>();
        var body = JsonSerializer.Serialize(new
        {
            req = new
            {
                method = "DoSearchForQQMusicDesktop",
                module = "music.search.SearchCgiService",
                param = new { num_per_page = limit, page_num = 1, query = keyword, search_type = 0 },
            },
        });
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (await HttpLimits.SendStringAsync(Http, reqMsg, stop) is not { } found) return (mids, items);
        using var doc = JsonDocument.Parse(found);
        if (!doc.RootElement.TryGetProperty("req", out var req) || !req.TryGetProperty("data", out var data) || !data.TryGetProperty("body", out var b) ||
            !b.TryGetProperty("song", out var song) || !song.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return (mids, items);

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
        return (mids, items);
    }

    private async Task<LyricSheet?> FetchLyricAsync(string mid, string title, CancellationToken stop)
    {
        if (await HttpLimits.GetStringAsync(Http, $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={Uri.EscapeDataString(mid)}&format=json&nobase64=1&g_tk=5381", stop) is not { } json) return null;
        using var l = JsonDocument.Parse(json);
        string Lyric(string name) => l.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } raw && raw.Length <= HttpLimits.MaxText ? WebUtility.HtmlDecode(raw) : "";
        var lines = Lrc.Parse(Lrc.StripCredits(Lyric("lyric"), title));
        if (lines.Count == 0) return null;
        return new LyricSheet(lines, true) { Translation = Lrc.Align(lines, Lrc.Parse(Chinese.Localize(Lrc.StripCredits(Lyric("trans"))))) };
    }
}

/// <summary>酷狗音樂。它的歌名常常不帶版本，所以多半只能當原曲的備援。非官方介面，不用帳號。</summary>
public sealed class KugouProvider : ILyricsSearchProvider
{
    public string Name => "kugou";
    private readonly HttpClient Http;

    private static readonly Lazy<HttpClient> Shared = new(() => Client(null));

    /// <param name="handler">測試用：換掉真的網路。</param>
    public KugouProvider(HttpMessageHandler? handler = null) => Http = handler == null ? Shared.Value : Client(handler);

    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var http = HttpLimits.Create(TimeSpan.FromSeconds(10), handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        return http;
    }

    /// <summary>搜尋結果標示關鍵字的 &lt;em&gt;、&lt;/em&gt; 拿掉。</summary>
    public static string StripEm(string s) => s.Replace("<em>", "", StringComparison.OrdinalIgnoreCase).Replace("</em>", "", StringComparison.OrdinalIgnoreCase);

    private static JsonDocument? TryParse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    public async Task<LyricsHit?> FindAsync(SongQuery q)
    {
        var artist = Titles.MainArtist(q.Artist);
        var (hashes, items) = await QuerySongsAsync((q.Title + " " + artist).Trim(), 20, CancellationToken.None);
        var (index, exact) = SongPicker.Pick(items, q.Title, artist, q.Duration, q.Tolerance);
        if (index < 0 || hashes[index].Length == 0) return null;
        var sheet = await FetchLyricAsync(hashes[index], items[index].Duration, items[index].Title, CancellationToken.None);
        return sheet == null ? null : new LyricsHit(sheet, exact, Name);
    }

    public async Task<List<LyricsChoice>> SearchAsync(string keyword, CancellationToken stop = default)
    {
        keyword = Titles.Cap(keyword).Trim();
        if (keyword.Length == 0) return [];
        var (hashes, items) = await QuerySongsAsync(keyword, 10, stop);
        var list = new List<LyricsChoice>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var hash = hashes[i];
            var item = items[i];
            if (hash.Length == 0 || item.Title.Length == 0) continue;
            list.Add(new LyricsChoice(item.Title, item.Artist, item.Duration, Name, true, ct => FetchLyricAsync(hash, item.Duration, item.Title, ct)));
        }
        return list;
    }

    private async Task<(List<string> Hashes, List<SongCandidate> Items)> QuerySongsAsync(string keyword, int limit, CancellationToken stop)
    {
        var hashes = new List<string>();
        var items = new List<SongCandidate>();
        // mobilecdn.kugou.com 沒有有效的 HTTPS 憑證，改用 songsearch；全部走 HTTPS，免得同網路的人看得到、改得到歌詞
        if (await HttpLimits.GetStringAsync(Http, $"https://songsearch.kugou.com/song_search_v2?keyword={Uri.EscapeDataString(keyword)}&page=1&pagesize={limit}&platform=WebFilter", stop) is not { } found)
            return (hashes, items);
        using var doc = TryParse(found);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("lists", out var list) || list.ValueKind != JsonValueKind.Array) return (hashes, items);

        foreach (var s in list.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            string Get(string name) => s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? StripEm(v.GetString() ?? "") : "";
            hashes.Add(Get("FileHash"));
            items.Add(new SongCandidate(Get("SongName"), Get("SingerName"), s.TryGetProperty("Duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0));
        }
        return (hashes, items);
    }

    private async Task<LyricSheet?> FetchLyricAsync(string hash, double duration, string title, CancellationToken stop)
    {
        // 兩步：先用歌的雜湊查到歌詞的編號與金鑰，再下載
        if (await HttpLimits.GetStringAsync(Http, $"https://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration={(long)(duration * 1000)}&hash={Uri.EscapeDataString(hash)}", stop) is not { } search) return null;
        using var s2 = TryParse(search);
        if (s2 == null || s2.RootElement.ValueKind != JsonValueKind.Object ||
            !s2.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0) return null;
        var first = candidates[0];
        if (first.ValueKind != JsonValueKind.Object) return null;
        string Field(string name) => first.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
        string id = Field("id"), key = Field("accesskey");
        if (id.Length == 0 || key.Length == 0) return null;
        if (await HttpLimits.GetStringAsync(Http, $"https://lyrics.kugou.com/download?ver=1&client=pc&id={Uri.EscapeDataString(id)}&accesskey={Uri.EscapeDataString(key)}&fmt=lrc&charset=utf8", stop) is not { } download) return null;
        using var d2 = TryParse(download);
        if (d2 == null || d2.RootElement.ValueKind != JsonValueKind.Object ||
            !d2.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return null;
        var base64 = content.GetString() ?? "";
        string lrc;
        try { lrc = Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
        catch (FormatException) { return null; }
        if (lrc.Length > HttpLimits.MaxText) return null;
        var lines = Lrc.Parse(Lrc.StripCredits(lrc, title));
        return lines.Count > 0 ? new LyricSheet(lines, true) : null;
    }
}

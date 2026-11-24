// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Shima.Lyrics;

namespace Shima.Playback;

/// <summary>一筆 YouTube 或 YouTube Music 的搜尋結果。</summary>
public sealed record YouTubeItem(string VideoId, string Title, string Channel, string Duration, string ThumbnailUrl, bool Music)
{
    public string WatchUrl => Music
        ? $"https://music.youtube.com/watch?v={VideoId}"
        : $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>
/// 透過 YouTube / YouTube Music 的網頁搜尋介面（InnerTube）查影片與歌曲，不需要 API 金鑰。
/// 回傳的影片編號與縮圖網址都經過白名單檢查，避免不正常回應帶入奇怪連結。
/// </summary>
public sealed partial class YouTubeSearch
{
    public static YouTubeSearch Shared { get; } = new();

    private const int MaxDepth = 24;
    private const int MaxThumbBytes = 1024 * 1024;
    private const string MusicSongsParam = "EgWKAQIIAWoMEAMQBBAJEAoQBRAV";

    private readonly HttpClient _http;
    private readonly Dictionary<string, Task<BitmapImage?>> _thumbs = new(StringComparer.Ordinal);

    /// <param name="handler">測試用：換掉真的網路。</param>
    public YouTubeSearch(HttpMessageHandler? handler = null)
    {
        _http = HttpLimits.Create(TimeSpan.FromSeconds(10), handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
    }

    /// <summary>在瀏覽器直接開啟的搜尋頁網址（InnerTube 失敗或想看完整清單時的備援）。</summary>
    public static string SearchUrl(string query, bool music)
    {
        query = Titles.Cap(query).Trim();
        return music
            ? $"https://music.youtube.com/search?q={Uri.EscapeDataString(query)}"
            : $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
    }

    /// <summary>YouTube 的影片編號固定為 11 碼英數與 - _，不合法的一律拒絕。</summary>
    public static bool ValidVideoId(string? id)
    {
        if (id is not { Length: 11 }) return false;
        foreach (char c in id)
        {
            bool ok = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>縮圖只從 YouTube / Google 自己的圖片主機透過 HTTPS 載入。</summary>
    public static bool AllowedThumb(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns) return false;
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host == "img.youtube.com"
            || host == "lh3.googleusercontent.com"
            || host == "yt3.googleusercontent.com"
            || host == "yt3.ggpht.com"
            || host.EndsWith(".ytimg.com", StringComparison.Ordinal);
    }

    public async Task<List<YouTubeItem>> SearchAsync(string query, bool music, string? lang = null, int limit = 8, CancellationToken stop = default)
    {
        query = Titles.Cap(query).Trim();
        if (query.Length == 0 || limit <= 0) return [];

        var (hl, gl) = Locale(lang);
        string endpoint;
        string body;
        if (music)
        {
            endpoint = "https://music.youtube.com/youtubei/v1/search?prettyPrint=false";
            body = JsonSerializer.Serialize(new
            {
                context = new { client = new { clientName = "WEB_REMIX", clientVersion = "1.20261110.01.00", hl, gl } },
                query,
                @params = MusicSongsParam,
            });
        }
        else
        {
            endpoint = "https://www.youtube.com/youtubei/v1/search?prettyPrint=false";
            body = JsonSerializer.Serialize(new
            {
                context = new { client = new { clientName = "WEB", clientVersion = "2.20261110.01.00", hl, gl } },
                query,
            });
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (music) req.Headers.TryAddWithoutValidation("Origin", "https://music.youtube.com");

        if (await HttpLimits.SendStringAsync(_http, req, stop).ConfigureAwait(false) is not { } json)
            return [];
        return music ? ParseMusic(json, limit) : ParseYouTube(json, limit);
    }

    /// <summary>下載並解碼縮圖（已凍結，可跨執行緒用）。不符白名單或失敗回 null。</summary>
    public Task<BitmapImage?> ThumbnailAsync(string url, CancellationToken stop = default)
    {
        if (!AllowedThumb(url)) return Task.FromResult<BitmapImage?>(null);
        lock (_thumbs)
        {
            if (_thumbs.TryGetValue(url, out var cached)) return cached;
            if (_thumbs.Count >= 120) _thumbs.Clear();
            var task = FetchThumbAsync(url, stop);
            _thumbs[url] = task;
            return task;
        }
    }

    private async Task<BitmapImage?> FetchThumbAsync(string url, CancellationToken stop)
    {
        try
        {
            var bytes = await HttpLimits.GetBytesAsync(_http, url, MaxThumbBytes, stop).ConfigureAwait(false);
            return bytes is { Length: > 0 } ? Images.Decode(bytes) : null;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- 解析回應

    public static List<YouTubeItem> ParseYouTube(string json, int limit = 8)
    {
        var items = new List<YouTubeItem>();
        if (limit <= 0) return items;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return items; }

        using (doc)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Walk(doc.RootElement, 0, "videoRenderer", vr =>
            {
                if (items.Count >= limit) return;
                var id = StringProp(vr, "videoId");
                if (!ValidVideoId(id) || !seen.Add(id)) return;
                var title = TextOf(vr, "title");
                if (title.Length == 0) return;
                var channel = TextOf(vr, "ownerText");
                if (channel.Length == 0) channel = TextOf(vr, "longBylineText");
                if (channel.Length == 0) channel = TextOf(vr, "shortBylineText");
                var duration = TextOf(vr, "lengthText");
                var thumb = vr.TryGetProperty("thumbnail", out var t) ? PickThumb(t) : "";
                items.Add(new YouTubeItem(id, title, channel, duration, thumb, Music: false));
            });
        }
        return items;
    }

    public static List<YouTubeItem> ParseMusic(string json, int limit = 8)
    {
        var items = new List<YouTubeItem>();
        if (limit <= 0) return items;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return items; }

        using (doc)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Walk(doc.RootElement, 0, "musicResponsiveListItemRenderer", mr =>
            {
                if (items.Count >= limit) return;
                var id = MusicVideoId(mr);
                if (!ValidVideoId(id) || !seen.Add(id)) return;

                if (!mr.TryGetProperty("flexColumns", out var cols) || cols.ValueKind != JsonValueKind.Array || cols.GetArrayLength() == 0)
                    return;
                var title = FlexText(cols[0]);
                if (title.Length == 0) return;

                var sub = cols.GetArrayLength() > 1 ? FlexText(cols[1]) : "";
                var (channel, duration) = SplitMusicSubtitle(sub);
                var thumb = mr.TryGetProperty("thumbnail", out var t) && t.TryGetProperty("musicThumbnailRenderer", out var mtr) &&
                            mtr.TryGetProperty("thumbnail", out var inner)
                    ? PickThumb(inner) : "";
                items.Add(new YouTubeItem(id, title, channel, duration, thumb, Music: true));
            });
        }
        return items;
    }

    private static void Walk(JsonElement node, int depth, string targetProperty, Action<JsonElement> onFound)
    {
        if (depth > MaxDepth) return;
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in node.EnumerateObject())
                {
                    if (prop.Name == targetProperty && prop.Value.ValueKind == JsonValueKind.Object)
                        onFound(prop.Value);
                    else
                        Walk(prop.Value, depth + 1, targetProperty, onFound);
                }
                break;
            case JsonValueKind.Array:
                foreach (var elem in node.EnumerateArray())
                    Walk(elem, depth + 1, targetProperty, onFound);
                break;
        }
    }

    private static string MusicVideoId(JsonElement mr)
    {
        if (mr.TryGetProperty("playlistItemData", out var pid))
        {
            var id = StringProp(pid, "videoId");
            if (ValidVideoId(id)) return id;
        }
        if (mr.TryGetProperty("overlay", out var ov) &&
            ov.TryGetProperty("musicItemThumbnailOverlayRenderer", out var mto) &&
            mto.TryGetProperty("content", out var ct) &&
            ct.TryGetProperty("musicPlayButtonRenderer", out var btn) &&
            btn.TryGetProperty("playNavigationEndpoint", out var nav) &&
            nav.TryGetProperty("watchEndpoint", out var we))
        {
            var id = StringProp(we, "videoId");
            if (ValidVideoId(id)) return id;
        }
        if (mr.TryGetProperty("flexColumns", out var cols) && cols.ValueKind == JsonValueKind.Array && cols.GetArrayLength() > 0 &&
            cols[0].TryGetProperty("musicResponsiveListItemFlexColumnRenderer", out var fr) &&
            fr.TryGetProperty("text", out var txt) && txt.TryGetProperty("runs", out var runs) &&
            runs.ValueKind == JsonValueKind.Array && runs.GetArrayLength() > 0 &&
            runs[0].TryGetProperty("navigationEndpoint", out var nav2) &&
            nav2.TryGetProperty("watchEndpoint", out var we2))
        {
            var id = StringProp(we2, "videoId");
            if (ValidVideoId(id)) return id;
        }
        return "";
    }

    private static string FlexText(JsonElement col)
    {
        if (!col.TryGetProperty("musicResponsiveListItemFlexColumnRenderer", out var fr)) return "";
        return TextOf(fr, "text");
    }

    private static (string Channel, string Duration) SplitMusicSubtitle(string subtitle)
    {
        if (subtitle.Length == 0) return ("", "");
        var parts = subtitle.Split([" • ", " · "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0) return ("", "");

        string duration = "";
        if (DurationPattern().IsMatch(parts[^1]))
        {
            duration = parts[^1];
            parts.RemoveAt(parts.Count - 1);
        }
        // 沒有帶歌曲篩選時第一段可能是「歌曲」「Song」「影片」「Video」之類的分類標籤
        if (parts.Count >= 2 && parts[0] is "歌曲" or "Song" or "影片" or "Video" or "曲" or "動画")
            parts.RemoveAt(0);
        return (parts.Count > 0 ? parts[0] : "", duration);
    }

    private static string StringProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    private static string TextOf(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object) return "";
        if (v.TryGetProperty("simpleText", out var s) && s.ValueKind == JsonValueKind.String)
            return (s.GetString() ?? "").Trim();
        if (v.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var r in runs.EnumerateArray())
                if (r.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
            return sb.ToString().Trim();
        }
        return "";
    }

    private static string PickThumb(JsonElement thumbnailObj)
    {
        if (!thumbnailObj.TryGetProperty("thumbnails", out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        string best = "";
        int bestWidth = 0;
        foreach (var t in arr.EnumerateArray())
        {
            var url = StringProp(t, "url");
            if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
            if (!AllowedThumb(url)) continue;
            int w = t.TryGetProperty("width", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetInt32() : 0;
            // 挑一張夠清楚但不用太大的（120～360px 最剛好）
            if (best.Length == 0 || (w >= 120 && w <= 360 && (bestWidth < 120 || bestWidth > 360 || w > bestWidth)))
            {
                best = url;
                bestWidth = w;
            }
        }
        return best;
    }

    private static (string Hl, string Gl) Locale(string? lang) => (lang ?? "").ToLowerInvariant() switch
    {
        "zh-tw" => ("zh-TW", "TW"),
        "zh-cn" => ("zh-CN", "CN"),
        "ja" => ("ja", "JP"),
        "ko" => ("ko", "KR"),
        "es" => ("es", "ES"),
        "fr" => ("fr", "FR"),
        "de" => ("de", "DE"),
        "pt" => ("pt", "BR"),
        "it" => ("it", "IT"),
        "ru" => ("ru", "RU"),
        _ => ("en", "US"),
    };

    [GeneratedRegex(@"^\d{1,2}:\d{2}(?::\d{2})?$", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex DurationPattern();
}

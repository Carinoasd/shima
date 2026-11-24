// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shima.Playback;

namespace Shima.Lyrics;

/// <summary>
/// 依設定的順序問每個歌詞來源。確定是同一版本的歌詞，先找到的先用；
/// 只找到原曲（版本不同）的先留著，全部來源都沒有同版本時才用它。
/// 結果存在 %APPDATA%\Shima\cache，同一首歌不會再問第二次；找不到的 3 天後再問。
/// </summary>
public sealed class LyricsFinder
{
    /// <summary>所有認得的來源。設定檔的 LyricsProviders 決定用哪些、什麼順序。</summary>
    public static readonly IReadOnlyDictionary<string, Func<ILyricsProvider>> Known = new Dictionary<string, Func<ILyricsProvider>>
    {
        ["local"] = () => new LocalProvider(),
        ["lrclib"] = () => new LrclibProvider(),
        ["netease"] = () => new NetEaseProvider(),
        ["qq"] = () => new QQProvider(),
        ["kugou"] = () => new KugouProvider(),
    };

    private readonly List<ILyricsProvider> _providers;
    private readonly Dictionary<string, LyricsHit?> _memory = [];

    private readonly string? _cacheFolder;

    public LyricsFinder() : this(
        AppSettings.Current.LyricsProviders.Where(Known.ContainsKey).Distinct().Select(n => Known[n]()).ToList(),
        Path.Combine(AppSettings.Folder, "cache")) { }

    /// <param name="cacheFolder">null：不存磁碟（測試用）。</param>
    public LyricsFinder(List<ILyricsProvider> providers, string? cacheFolder = null)
    {
        _providers = providers;
        _cacheFolder = cacheFolder;
    }

    private static SongQuery? Query(Track track)
    {
        var (artist, title) = Titles.Clean(track.Artist, track.Title, split: track.AppId != RoonSource.AppId);
        return title.Length == 0 ? null : new SongQuery(title, artist, track.Album, track.Duration.TotalSeconds);
    }

    /// <summary>整理過的歌名、藝人（跟查歌詞用的一樣）；認不出歌名是 null。</summary>
    public static SongQuery? QueryOf(Track track) => Query(track);

    /// <summary>
    /// 這首歌換了歌詞檔（剛存了 .lrc）：忘掉記住的結果、自己的歌詞資料夾重新掃一次，下次查才會用到新檔案。
    /// </summary>
    public void Forget(Track track)
    {
        var id = "|" + IdOf(track) + "|";
        lock (_memory)
            foreach (var key in _memory.Keys.Where(k => k.Contains(id, StringComparison.Ordinal)).ToList()) _memory.Remove(key);
        foreach (var local in _providers.OfType<LocalProvider>()) local.Reindex();
    }

    /// <summary>一首歌的身分（不含長度）：使用者為它選的歌詞來源、調的時間都記在這個名字下。</summary>
    public static string IdOf(Track track) =>
        Query(track) is { } q ? $"{Titles.Norm(q.Artist)}|{Titles.Norm(q.Title)}" : "";

    /// <summary>每個來源各問一次，把找到的全部列出來（同版本的排前面），給「換一份」用。</summary>
    public async Task<List<LyricsHit>> AllAsync(Track track)
    {
        if (Query(track) is not { } q) return [];
        var hits = await Task.WhenAll(_providers.Select(p => AskAsync(p, q)));
        return hits.OfType<LyricsHit>().OrderByDescending(h => h.Exact).ToList();
    }

    public async Task<LyricsHit?> FindAsync(Track track)
    {
        if (Query(track) is not { } q) return null;
        string artist = q.Artist, title = q.Title;

        // 使用者替這首歌選過來源：直接問它
        if (AppSettings.Current.LyricsPick.TryGetValue(IdOf(track), out var picked) &&
            _providers.FirstOrDefault(p => p.Name == picked) is { } chosen)
        {
            var pickKey = $"pick|{picked}|{IdOf(track)}|{Math.Round(q.Duration / 2)}";
            lock (_memory) if (_memory.TryGetValue(pickKey, out var again)) return again;
            if (await AskAsync(chosen, q) is { } hit) return Remember(pickKey, hit, save: false);
        }

        var key = $"3|{WantsSecondLine}|{Titles.Norm(artist)}|{Titles.Norm(title)}|{Titles.VersionTag(title)}|{Math.Round(q.Duration / 2)}";
        lock (_memory) if (_memory.TryGetValue(key, out var known)) return known;

        // 自己放的歌詞檔隨時可能新增，每次都先看
        var local = _providers.OfType<LocalProvider>().FirstOrDefault();
        if (local != null && await AskAsync(local, q) is { } mine) return Remember(key, mine, save: false);

        if (Read(key) is { } stored) return Remember(key, stored.Hit, save: false);

        LyricsHit? approximate = null;
        bool failed = false;
        foreach (var provider in _providers.Where(p => p is not LocalProvider))
        {
            LyricsHit? hit;
            try { hit = await provider.FindAsync(q); }
            catch (Exception e) { Log.Write($"歌詞 ({provider.Name}): {e.Message}"); failed = true; continue; }
            if (hit is { Exact: true }) return Remember(key, await WithSecondLineAsync(hit, q), save: true);
            approximate ??= hit;
        }
        // 有來源連不上時先不記「找不到」，下次再試
        return Remember(key, approximate, save: approximate != null || !failed);
    }

    /// <summary>第二行要翻譯或羅馬拼音（讀音是自己算的，不用另外找有翻譯的版本）。</summary>
    private static bool WantsSecondLine => AppSettings.Current.LyricsSecondLine is not ("none" or "furigana");

    /// <summary>
    /// 找到的歌詞沒有翻譯／羅馬拼音時，問問有這些資料的來源（網易雲）有沒有同一版本；
    /// 有就整份改用它的，時間軸才對得上。
    /// </summary>
    private async Task<LyricsHit> WithSecondLineAsync(LyricsHit hit, SongQuery q)
    {
        if (!WantsSecondLine || hit.Sheet.Translation != null || hit.Sheet.Romaji != null) return hit;
        if (_providers.OfType<NetEaseProvider>().FirstOrDefault() is not { } netEase || hit.Provider == netEase.Name) return hit;
        return await AskAsync(netEase, q) is { Exact: true } richer && (richer.Sheet.Translation != null || richer.Sheet.Romaji != null) ? richer : hit;
    }

    private static async Task<LyricsHit?> AskAsync(ILyricsProvider provider, SongQuery q)
    {
        try { return await provider.FindAsync(q); }
        catch (Exception e) { Log.Write($"歌詞 ({provider.Name}): {e.Message}"); return null; }
    }

    private LyricsHit? Remember(string key, LyricsHit? hit, bool save)
    {
        lock (_memory) _memory[key] = hit;
        if (save) Write(key, hit);
        return hit;
    }

    // ---------------------------------------------------------------- 磁碟快取

    // 新欄位一律放最後、給預設值：舊的快取檔沒有它們也讀得進來
    private sealed record Stored(bool Found, bool Synced, bool Exact, string Provider, List<double> Times, List<string> Texts, DateTime At,
        List<string>? Translation = null, List<string>? Romaji = null, List<List<StoredWord>?>? Words = null);

    /// <summary>一個字：開始與長度（毫秒）、字。</summary>
    private sealed record StoredWord(double S, double L, string T);

    /// <summary>找到的歌詞（或「找不到」）寫成快取檔的內容。</summary>
    public static string Pack(LyricsHit? hit, DateTime at)
    {
        var lines = hit?.Sheet.Lines;
        var words = lines?.Any(l => l.Words != null) == true
            ? lines.Select(l => l.Words?.Select(w => new StoredWord(w.Start.TotalMilliseconds, w.Length.TotalMilliseconds, w.Text)).ToList()).ToList()
            : null;
        return JsonSerializer.Serialize(new Stored(hit != null, hit?.Sheet.Synced ?? false, hit?.Exact ?? false, hit?.Provider ?? "",
            lines?.Select(l => l.Time.TotalMilliseconds).ToList() ?? [], lines?.Select(l => l.Text).ToList() ?? [], at,
            hit?.Sheet.Translation?.ToList(), hit?.Sheet.Romaji?.ToList(), words));
    }

    /// <summary>讀回快取檔：Hit 是 null 表示當時找不到。逐字資料跟字對不上的那句就當作沒有。</summary>
    public static (LyricsHit? Hit, DateTime At)? Unpack(string json)
    {
        var s = JsonSerializer.Deserialize<Stored>(json);
        if (s == null) return null;
        if (!s.Found) return (null, s.At);
        var lines = s.Times.Zip(s.Texts, (t, x) => new LyricLine(TimeSpan.FromMilliseconds(t), x)).ToList();
        if (s.Words != null)
            for (int i = 0; i < lines.Count && i < s.Words.Count; i++)
            {
                if (s.Words[i] is not { } stored) continue;
                var words = stored.Select(w => new LyricWord(TimeSpan.FromMilliseconds(w.S), TimeSpan.FromMilliseconds(w.L), w.T)).ToList();
                if (WordTiming.Fits(lines[i].Text, words)) lines[i] = lines[i] with { Words = words };
            }
        return (new LyricsHit(new LyricSheet(lines, s.Synced) { Translation = s.Translation, Romaji = s.Romaji }, s.Exact, s.Provider), s.At);
    }

    private string FileFor(string key) =>
        Path.Combine(_cacheFolder!, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");

    private (LyricsHit? Hit, bool _)? Read(string key)
    {
        if (_cacheFolder == null) return null;
        try
        {
            var path = FileFor(key);
            if (!File.Exists(path)) return null;
            if (Unpack(File.ReadAllText(path)) is not { } s) return null;
            // 找不到的 3 天後再問；只找到原曲的 14 天後再問（也許有人補上這個版本了）
            var age = DateTime.Now - s.At;
            if (s.Hit == null) return age < TimeSpan.FromDays(3) ? (null, true) : null;
            if (!s.Hit.Exact && age > TimeSpan.FromDays(14)) return null;
            return (s.Hit, true);
        }
        catch (Exception e) { Log.Write("歌詞快取: " + e.Message); return null; }
    }

    private void Write(string key, LyricsHit? hit)
    {
        if (_cacheFolder == null) return;
        try
        {
            Directory.CreateDirectory(_cacheFolder);
            var path = FileFor(key);
            File.WriteAllText(path + ".tmp", Pack(hit, DateTime.Now));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) { Log.Write("歌詞快取寫入: " + e.Message); }
    }
}

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

        var key = $"3|{AppSettings.Current.LyricsSecondLine != "none"}|{Titles.Norm(artist)}|{Titles.Norm(title)}|{Titles.VersionTag(title)}|{Math.Round(q.Duration / 2)}";
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

    /// <summary>
    /// 找到的歌詞沒有翻譯／羅馬拼音時，問問有這些資料的來源（網易雲）有沒有同一版本；
    /// 有就整份改用它的，時間軸才對得上。
    /// </summary>
    private async Task<LyricsHit> WithSecondLineAsync(LyricsHit hit, SongQuery q)
    {
        if (AppSettings.Current.LyricsSecondLine == "none" || hit.Sheet.Translation != null || hit.Sheet.Romaji != null) return hit;
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

    private sealed record Stored(bool Found, bool Synced, bool Exact, string Provider, List<double> Times, List<string> Texts, DateTime At,
        List<string>? Translation = null, List<string>? Romaji = null);

    private string FileFor(string key) =>
        Path.Combine(_cacheFolder!, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");

    private (LyricsHit? Hit, bool _)? Read(string key)
    {
        if (_cacheFolder == null) return null;
        try
        {
            var path = FileFor(key);
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            if (s == null) return null;
            // 找不到的 3 天後再問；只找到原曲的 14 天後再問（也許有人補上這個版本了）
            var age = DateTime.Now - s.At;
            if (!s.Found) return age < TimeSpan.FromDays(3) ? (null, true) : null;
            if (!s.Exact && age > TimeSpan.FromDays(14)) return null;
            var lines = s.Times.Zip(s.Texts, (t, x) => new LyricLine(TimeSpan.FromMilliseconds(t), x)).ToList();
            return (new LyricsHit(new LyricSheet(lines, s.Synced) { Translation = s.Translation, Romaji = s.Romaji }, s.Exact, s.Provider), true);
        }
        catch (Exception e) { Log.Write("歌詞快取: " + e.Message); return null; }
    }

    private void Write(string key, LyricsHit? hit)
    {
        if (_cacheFolder == null) return;
        try
        {
            Directory.CreateDirectory(_cacheFolder);
            var s = new Stored(hit != null, hit?.Sheet.Synced ?? false, hit?.Exact ?? false, hit?.Provider ?? "",
                hit?.Sheet.Lines.Select(l => l.Time.TotalMilliseconds).ToList() ?? [], hit?.Sheet.Lines.Select(l => l.Text).ToList() ?? [], DateTime.Now,
                hit?.Sheet.Translation?.ToList(), hit?.Sheet.Romaji?.ToList());
            var path = FileFor(key);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(s));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) { Log.Write("歌詞快取寫入: " + e.Message); }
    }
}

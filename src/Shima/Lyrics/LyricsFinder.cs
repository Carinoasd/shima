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
    private readonly Dictionary<string, LyricsHit> _manual = new(StringComparer.Ordinal);

    private readonly string? _cacheFolder;
    private readonly string? _manualFolder;

    public LyricsFinder() : this(
        AppSettings.Current.LyricsProviders.Where(Known.ContainsKey).Distinct().Select(n => Known[n]()).ToList(),
        Path.Combine(AppSettings.Folder, "cache"),
        Path.Combine(AppSettings.Folder, "lyrics-manual")) { }

    /// <param name="cacheFolder">null：不存磁碟快取（測試用）。</param>
    /// <param name="manualFolder">null：手動綁定只留記憶體（測試用）。</param>
    public LyricsFinder(List<ILyricsProvider> providers, string? cacheFolder = null, string? manualFolder = null)
    {
        _providers = providers;
        _cacheFolder = cacheFolder;
        _manualFolder = manualFolder;
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
        var id = IdOf(track);
        if (id.Length > 0)
        {
            var marker = "|" + id + "|";
            lock (_memory)
                foreach (var key in _memory.Keys.Where(k => k.Contains(marker, StringComparison.Ordinal)).ToList()) _memory.Remove(key);
        }
        foreach (var local in _providers.OfType<LocalProvider>()) local.Reindex();
    }

    /// <summary>一首歌的身分（不含長度）：使用者為它選的歌詞來源、調的時間都記在這個名字下。</summary>
    public static string IdOf(Track track) =>
        Query(track) is { } q ? $"{Titles.Norm(q.Artist)}|{Titles.Norm(q.Title)}" : "";

    /// <summary>未經標題拆解的原始身分：手動關鍵字綁定歌詞記在這個鍵下，就算標題解析規則改了或解析不到仍認得這支影片。</summary>
    public static string RawId(Track track) =>
        $"{Titles.Norm(track.Artist)}|{Titles.Norm(track.Title)}";

    /// <summary>把使用者手動搜尋挑中的歌詞綁到這首歌上，之後再播到同一首會直接套用。</summary>
    public void Bind(Track track, LyricsHit hit)
    {
        var raw = RawId(track);
        if (raw == "|") return;
        lock (_manual) _manual[raw] = hit;
        Forget(track);
        WriteManual(raw, hit);
    }

    /// <summary>移除這首歌的手動綁定歌詞（還原、存成 .lrc 或換一份時呼叫）。</summary>
    public void Unbind(Track track)
    {
        var raw = RawId(track);
        if (raw == "|") return;
        lock (_manual) _manual.Remove(raw);
        DeleteManual(raw);
        Forget(track);
    }

    /// <summary>以關鍵字同時問所有支援搜尋的歌詞來源，供使用者手動挑選。</summary>
    public async Task<List<LyricsChoice>> SearchAsync(string keyword, CancellationToken stop = default)
    {
        keyword = Titles.Cap(keyword).Trim();
        if (keyword.Length == 0) return [];
        var searchable = _providers.OfType<ILyricsSearchProvider>().ToList();
        if (searchable.Count == 0) return [];

        var lists = await Task.WhenAll(searchable.Select(async p =>
        {
            try { return await p.SearchAsync(keyword, stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
            catch (Exception e) { Log.Write($"歌詞搜尋 ({p.Name}): {e.Message}"); return new List<LyricsChoice>(); }
        }));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<LyricsChoice>();
        // 先排有時間軸的，再補純文字；同一來源同歌名同藝人同長度去重
        foreach (var synced in new[] { true, false })
            foreach (var list in lists)
                foreach (var c in list)
                {
                    if (c.Synced != synced) continue;
                    var dedup = $"{c.Provider}|{Titles.Norm(c.Title)}|{Titles.Norm(c.Artist)}|{Math.Round(c.Duration)}";
                    if (!seen.Add(dedup)) continue;
                    merged.Add(c);
                    if (merged.Count >= 24) return merged;
                }
        return merged;
    }

    /// <summary>每個來源各問一次，把找到的全部列出來（同版本的排前面），給「換一份」用。</summary>
    public async Task<List<LyricsHit>> AllAsync(Track track)
    {
        if (Query(track) is not { } q) return [];
        var hits = (await Task.WhenAll(_providers.Select(p => AskAsync(p, q))))
            .OfType<LyricsHit>().OrderByDescending(h => h.Exact).ToList();
        if (hits.Count == 0 && track.AppId != RoonSource.AppId && q.Duration > 0)
        {
            var relaxed = q with { Tolerance = 15 };
            hits = (await Task.WhenAll(_providers.Where(p => p is not LocalProvider).Select(p => AskAsync(p, relaxed))))
                .OfType<LyricsHit>().OrderByDescending(h => h.Exact).ToList();
        }
        return hits;
    }

    public async Task<LyricsHit?> FindAsync(Track track)
    {
        // 使用者曾經手動搜尋並綁定給這首歌的歌詞最優先（永遠不過期，也不受標題拆解結果影響）
        if (ReadManual(track) is { } manual) return manual;

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

        var online = _providers.Where(p => p is not LocalProvider).ToList();
        var (hit1, failed) = await QueryProvidersAsync(online, q);
        if (hit1 is { Exact: true }) return Remember(key, await WithSecondLineAsync(hit1, q), save: true);
        LyricsHit? approximate = hit1;

        // 瀏覽器 / YouTube 標題常夾雜多個藝人、聲優、作品名，或 MV 片頭片尾多幾秒：第一輪沒中時再放寬重試
        if (approximate == null && !failed && track.AppId != RoonSource.AppId)
        {
            foreach (var fq in FallbackQueries(track, q))
            {
                var (hit2, failed2) = await QueryProvidersAsync(online, fq);
                if (failed2) failed = true;
                if (hit2 is { Exact: true }) return Remember(key, await WithSecondLineAsync(hit2, fq), save: true);
                if (hit2 != null) { approximate = hit2; break; }
            }
        }

        // 有來源連不上時先不記「找不到」，下次再試
        return Remember(key, approximate, save: approximate != null || !failed);
    }

    private static async Task<(LyricsHit? Hit, bool Failed)> QueryProvidersAsync(List<ILyricsProvider> providers, SongQuery q)
    {
        LyricsHit? approximate = null;
        bool failed = false;
        foreach (var provider in providers)
        {
            LyricsHit? hit;
            try { hit = await provider.FindAsync(q); }
            catch (Exception e) { Log.Write($"歌詞 ({provider.Name}): {e.Message}"); failed = true; continue; }
            if (hit is { Exact: true }) return (hit, failed);
            approximate ??= hit;
        }
        return (approximate, failed);
    }

    private static List<SongQuery> FallbackQueries(Track track, SongQuery primary)
    {
        var list = new List<SongQuery>(4);
        // 先用原本整理出的藝人＋歌名放寬長度容許（MV 常見片頭片尾多幾秒）
        if (primary.Duration > 0) list.Add(primary with { Tolerance = 15 });
        foreach (var (altArtist, altTitle) in Titles.Alternates(track.Artist, track.Title, split: true))
        {
            if (list.Count >= 4) break;
            if (altArtist.Length == 0 && primary.Duration <= 0) continue;
            list.Add(new SongQuery(altTitle, altArtist, "", primary.Duration, altArtist.Length > 0 ? 15 : 10));
        }
        return list;
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

    private static string HashFile(string folder, string key) =>
        Path.Combine(folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");

    private string FileFor(string key) => HashFile(_cacheFolder!, key);

    private LyricsHit? ReadManual(Track track)
    {
        var raw = RawId(track);
        if (raw == "|") return null;
        lock (_manual) if (_manual.TryGetValue(raw, out var mem)) return mem;
        if (_manualFolder == null) return null;
        try
        {
            var path = HashFile(_manualFolder, raw);
            if (!File.Exists(path)) return null;
            if (Unpack(File.ReadAllText(path)) is { Hit: { } hit })
            {
                lock (_manual) _manual[raw] = hit;
                return hit;
            }
        }
        catch (Exception e) { Log.Write("手動歌詞快取: " + e.Message); }
        return null;
    }

    private void WriteManual(string raw, LyricsHit hit)
    {
        if (_manualFolder == null || AppSettings.Frozen) return;
        try
        {
            Directory.CreateDirectory(_manualFolder);
            var path = HashFile(_manualFolder, raw);
            File.WriteAllText(path + ".tmp", Pack(hit, DateTime.Now));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) { Log.Write("手動歌詞寫入: " + e.Message); }
    }

    private void DeleteManual(string raw)
    {
        if (_manualFolder == null || AppSettings.Frozen) return;
        try
        {
            var path = HashFile(_manualFolder, raw);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) { Log.Write("手動歌詞移除: " + e.Message); }
    }

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
        if (_cacheFolder == null || AppSettings.Frozen) return;
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

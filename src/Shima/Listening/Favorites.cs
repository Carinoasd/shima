// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Shima.Listening;

/// <summary>收藏的一句歌詞。At 是這句在歌裡的時間，Added 是收藏的時間。</summary>
public sealed record Favorite(
    string Id,
    string Line,
    string Second,
    string Title,
    string Artist,
    string Album,
    TimeSpan At,
    DateTimeOffset Added);

/// <summary>收藏的歌詞，存在 favorites.json。讀寫失敗只記錄，不丟例外。</summary>
public sealed class LyricFavorites
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly Action<string> _log;
    private List<Favorite> _items;

    public string FilePath { get; }

    public static string DefaultPath => Path.Combine(AppSettings.Folder, "favorites.json");

    /// <summary>加入、刪除之後觸發。</summary>
    public event Action? Changed;

    public LyricFavorites(string? path = null, Action<string>? log = null)
    {
        FilePath = path ?? DefaultPath;
        _log = log ?? Log.Write;
        _items = Load();
    }

    /// <summary>全部，新收藏的在前面。</summary>
    public IReadOnlyList<Favorite> All
    {
        get { lock (_gate) return _items.OrderByDescending(f => f.Added).ToList(); }
    }

    /// <summary>加入一句；同一首歌的同一句已經收藏過就回傳原本那筆，不重複。空白的句子不收，回傳 null。</summary>
    public Favorite? Add(string line, string second, string title, string artist, string album, TimeSpan at, DateTimeOffset added)
    {
        line = (line ?? "").Trim();
        if (line.Length == 0) return null;
        Favorite favorite;
        lock (_gate)
        {
            var existing = _items.FirstOrDefault(f => f.Line == line && f.Title == title && f.Artist == artist);
            if (existing != null) return existing;
            favorite = new Favorite(Guid.NewGuid().ToString("N"), line, (second ?? "").Trim(), title ?? "", artist ?? "", album ?? "", at, added);
            _items.Add(favorite);
            Save();
        }
        Changed?.Invoke();
        return favorite;
    }

    /// <summary>歌詞、第二行、歌名、藝人、專輯裡有這段字的（不分大小寫）。空字串：全部。</summary>
    public IReadOnlyList<Favorite> Search(string query) => Filter(All, query);

    public static List<Favorite> Filter(IEnumerable<Favorite> items, string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return items.ToList();
        bool Has(string s) => s.Contains(query, StringComparison.OrdinalIgnoreCase);
        return items.Where(f => Has(f.Line) || Has(f.Second) || Has(f.Title) || Has(f.Artist) || Has(f.Album)).ToList();
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(f => f.Id == id) == 0) return false;
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    private List<Favorite> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var items = JsonSerializer.Deserialize<List<Favorite>>(File.ReadAllText(FilePath, Encoding.UTF8), Options);
            return items?.Where(f => f is { Id.Length: > 0, Line.Length: > 0 })
                .Select(f => f with { Second = f.Second ?? "", Title = f.Title ?? "", Artist = f.Artist ?? "", Album = f.Album ?? "" })
                .ToList() ?? [];
        }
        catch (Exception e)
        {
            // 讀不懂的檔案先複製一份 .bad，下次存檔蓋掉時收藏才不會整個消失
            _log("favorites read: " + e.Message);
            try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch { }
            return [];
        }
    }

    /// <summary>先寫暫存檔再換過去，寫到一半當掉也不會留下半個檔案。</summary>
    private void Save()
    {
        // 還原備份換掉資料夾之後到重新啟動之前：記憶體裡是還原前的收藏，寫出去會蓋掉剛換上去的 favorites.json
        if (AppSettings.Frozen) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_items, Options), new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) { _log("favorites write: " + e.Message); }
    }
}

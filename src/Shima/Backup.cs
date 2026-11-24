// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shima.Lyrics;

namespace Shima;

public enum BackupStatus { Ok, Unreadable, Unsafe, TooBig, Empty, BadSettings, Failed }

/// <param name="Saved">還原前複製出來的資料夾名稱（原本沒有資料夾就是 null）。</param>
public sealed record BackupResult(BackupStatus Status, int Files = 0, string? Saved = null);

/// <summary>
/// 把 Shima 的資料夾打包成 zip，或從 zip 還原。不依賴畫面，給定資料夾路徑就能跑。
/// 匯出只帶值得帶走的資料；還原把 zip 當成不可信的輸入：路徑、數量、大小、設定檔都要先驗證，
/// 全部通過才換掉，中途失敗就維持原狀。
/// </summary>
public static class Backup
{
    /// <summary>解開後的總大小與檔案數上限；超過就整個拒絕（防壓縮炸彈）。</summary>
    public const long MaxBytes = 500L * 1024 * 1024;
    public const int MaxFiles = 5000;

    // 認得的項目。沒有列在這裡的（四個 *.token、agent-rules.json、cache、紀錄檔…）既不匯出，也不採用 zip 裡的。
    private static readonly string[] KnownFiles = ["settings.json", "history.jsonl", "favorites.json", "alarms.json"];
    private static readonly string[] KnownFolders = ["lyrics", "translations", "shelf"];
    private static readonly string[] SkipExtensions = [".tmp", ".bak", ".token", ".part", ".new", ".old"];

    /// <summary>
    /// 只在這台電腦上有意義、或是機密的設定欄位：DPAPI 加密的（AI 金鑰、Last.fm 的 secret 與 session）、
    /// Roon 的 token，以及明文存放的 ListenBrainz token。匯出時拿掉，還原時保留這台原本的。
    /// </summary>
    public static readonly string[] SecretFields = ["AiKeys", "AnthropicKey", "LastFmSecret", "LastFmSession", "RoonTokens", "ListenBrainzToken"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // ---------------------------------------------------------------- 匯出

    public static BackupResult Export(string folder, string zipPath)
    {
        var tmp = zipPath + ".tmp";
        try
        {
            var root = Path.GetFullPath(folder);
            int count = 0;
            long total = 0;
            using (var zip = new ZipArchive(new FileStream(tmp, FileMode.Create, FileAccess.Write), ZipArchiveMode.Create))
            {
                foreach (var name in KnownFiles)
                {
                    var path = Path.Combine(root, name);
                    if (!File.Exists(path)) continue;
                    if (name == "settings.json")
                    {
                        // 寫進 zip 的是去掉機密欄位的複本，原本的設定檔不動
                        var text = StripSecrets(ReadText(path));
                        if (text == null) return new BackupResult(BackupStatus.BadSettings);
                        var bytes = new UTF8Encoding(false).GetBytes(text);
                        total += bytes.Length;
                        using var to = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                        to.Write(bytes);
                    }
                    else total += AddFile(zip, path, name);
                    count++;
                }
                foreach (var dir in KnownFolders)
                {
                    var top = Path.Combine(root, dir);
                    if (!Directory.Exists(top)) continue;
                    foreach (var file in Directory.EnumerateFiles(top, "*", SearchOption.AllDirectories))
                    {
                        if (Skipped(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        if (++count > MaxFiles) return new BackupResult(BackupStatus.TooBig);
                        total += AddFile(zip, file, Path.GetRelativePath(root, file).Replace('\\', '/'));
                        if (total > MaxBytes) return new BackupResult(BackupStatus.TooBig);
                    }
                }
            }
            if (count == 0) return new BackupResult(BackupStatus.Empty);
            File.Move(tmp, zipPath, overwrite: true);
            return new BackupResult(BackupStatus.Ok, count);
        }
        catch (Exception e) { Log.Write("backup export: " + e.Message); return new BackupResult(BackupStatus.Failed); }
        finally { TryDelete(tmp); }
    }

    private static long AddFile(ZipArchive zip, string path, string entryName)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        // 聆聽紀錄隨時可能有人在寫：用共用讀取的方式開
        using var from = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTime(path));
        using var to = entry.Open();
        from.CopyTo(to);
        return from.Length;
    }

    private static bool Skipped(string path) => SkipExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string ReadText(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>拿掉設定檔裡的機密欄位；設定檔不是一個 JSON 物件就是 null。</summary>
    public static string? StripSecrets(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            foreach (var field in SecretFields) obj.Remove(field);
            return obj.ToJsonString(Indented);
        }
        catch (JsonException) { return null; }
    }

    // ---------------------------------------------------------------- 路徑檢查

    private static readonly Regex Reserved = new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|CLOCK\$|(COM|LPT)[0-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// zip 項目的名稱是不是一個安全的相對路徑：不能是絕對路徑、磁碟代號、UNC、含 ..、含 NTFS 資料流的冒號、
    /// 保留的裝置名稱，也不能有 Windows 不接受的字元。通過的話 <paramref name="relative"/> 是用 / 分隔的正規化路徑。
    /// </summary>
    public static bool SafeName(string? name, out string relative)
    {
        relative = "";
        if (string.IsNullOrEmpty(name) || name.Length > 260 || name.Any(c => c < 0x20)) return false;
        var path = name.Replace('\\', '/');
        if (path.StartsWith('/') || path.Contains(':')) return false;
        var parts = path.Split('/').ToList();
        if (parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);   // 資料夾項目結尾的 /
        if (parts.Count == 0) return false;
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Length > 200 || part is "." or "..") return false;
            if (part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(invalid) >= 0) return false;
            if (Reserved.IsMatch(part.Split('.')[0].TrimEnd(' '))) return false;
        }
        relative = string.Join('/', parts);
        return true;
    }

    /// <summary>認得的項目就回正規的名稱，不認得的是 null（略過）。</summary>
    public static string? Known(string relative)
    {
        var parts = relative.Split('/');
        if (parts.Length == 1 && KnownFiles.FirstOrDefault(f => f.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) is { } file) return file;
        if (parts.Length >= 2 && KnownFolders.FirstOrDefault(f => f.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) is { } dir && !Skipped(relative))
            return dir + "/" + string.Join('/', parts.Skip(1));
        return null;
    }

    // ---------------------------------------------------------------- 還原

    public static BackupResult Restore(string zipPath, string folder, DateTime now)
    {
        string? stage = null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (Path.GetDirectoryName(root) is not { } parent) return new BackupResult(BackupStatus.Failed);

            ZipArchive zip;
            try { zip = ZipFile.OpenRead(zipPath); }
            catch (InvalidDataException) { return new BackupResult(BackupStatus.Unreadable); }
            using var _ = zip;

            // 1. 先全部檢查過，不寫任何東西
            if (zip.Entries.Count > MaxFiles) return new BackupResult(BackupStatus.TooBig);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var picks = new List<(ZipArchiveEntry Entry, string Name)>();
            long declared = 0;
            foreach (var entry in zip.Entries)
            {
                if (!SafeName(entry.FullName, out var relative)) return new BackupResult(BackupStatus.Unsafe);
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                if (!seen.Add(relative)) return new BackupResult(BackupStatus.Unsafe);   // 同名兩個項目：不知道該用哪一個
                declared += entry.Length;
                if (declared > MaxBytes) return new BackupResult(BackupStatus.TooBig);
                if (Known(relative) is { } name) picks.Add((entry, name));
            }
            if (picks.Count == 0) return new BackupResult(BackupStatus.Empty);

            // 2. 解到旁邊的暫存資料夾（跟資料夾同一個磁碟，最後用改名換上去）
            stage = Path.Combine(parent, ".Shima-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var staged = stage + Path.DirectorySeparatorChar;
            long written = 0;
            foreach (var (entry, name) in picks)
            {
                var target = Path.GetFullPath(Path.Combine(stage, name));
                if (!target.StartsWith(staged, StringComparison.OrdinalIgnoreCase)) return new BackupResult(BackupStatus.Unsafe);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var from = entry.Open();
                using var to = new FileStream(target, FileMode.Create, FileAccess.Write);
                var buffer = new byte[81920];
                for (int n; (n = from.Read(buffer)) > 0;)
                {
                    // 項目自己申報的大小不可信：照實際解出來的算
                    if ((written += n) > MaxBytes) return new BackupResult(BackupStatus.TooBig);
                    to.Write(buffer, 0, n);
                }
            }

            // 3. 設定檔要能讀得懂才採用；機密欄位一律用這台原本的
            var stagedSettings = Path.Combine(stage, "settings.json");
            if (File.Exists(stagedSettings))
            {
                if (MergeSettings(File.ReadAllText(stagedSettings), Path.Combine(root, "settings.json"), root) is not { } merged)
                    return new BackupResult(BackupStatus.BadSettings);
                File.WriteAllText(stagedSettings, merged, new UTF8Encoding(false));
            }

            // 4. 還原錯了要救得回來：先把目前的整份複製一份
            string? saved = null;
            if (Directory.Exists(root))
            {
                saved = $"Shima-before-restore-{now:yyyyMMdd-HHmmss}";
                for (int i = 2; Directory.Exists(Path.Combine(parent, saved)); i++) saved = $"Shima-before-restore-{now:yyyyMMdd-HHmmss}-{i}";
                try { CopyFolder(root, Path.Combine(parent, saved)); }
                catch { TryDeleteFolder(Path.Combine(parent, saved)); throw; }
            }

            // 5. 換上去：每一項先把舊的移到暫存區，任何一步失敗就全部移回來
            Apply(stage, root, picks.Select(p => p.Name.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            return new BackupResult(BackupStatus.Ok, picks.Count, saved);
        }
        catch (Exception e) { Log.Write("backup restore: " + e.Message); return new BackupResult(BackupStatus.Failed); }
        finally { if (stage != null) TryDeleteFolder(stage); }
    }

    private static void Apply(string stage, string root, List<string> items)
    {
        var trash = Path.Combine(stage, ".old");
        Directory.CreateDirectory(trash);
        Directory.CreateDirectory(root);
        var done = new List<(string Target, string Old, bool HadOld)>();
        try
        {
            foreach (var item in items)
            {
                var target = Path.Combine(root, item);
                var old = Path.Combine(trash, item);
                bool had = File.Exists(target) || Directory.Exists(target);
                done.Add((target, old, had));
                if (had) Move(target, old);
                Move(Path.Combine(stage, item), target);
            }
        }
        catch
        {
            foreach (var (target, old, had) in Enumerable.Reverse(done))
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target); else if (Directory.Exists(target)) Directory.Delete(target, true);
                    if (had) Move(old, target);
                }
                catch (Exception e) { Log.Write("backup rollback: " + e.Message); }
            }
            throw;
        }
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
    }

    /// <summary>
    /// 把 zip 裡的設定檔和這台的設定合起來：機密欄位用這台的（沒有就空著）、可疑的值改回安全的範圍。
    /// 讀不懂（不是物件、型別不對、欄位是 null）就是 null。
    /// </summary>
    public static string? MergeSettings(string incoming, string localPath, string folder)
    {
        try
        {
            if (JsonNode.Parse(incoming) is not JsonObject obj) return null;
            JsonObject? local = null;
            try { if (File.Exists(localPath)) local = JsonNode.Parse(ReadText(localPath)) as JsonObject; } catch (JsonException) { }
            foreach (var field in SecretFields) { obj.Remove(field); if (local?[field] is { } mine) obj[field] = mine.DeepClone(); }
            // 自訂的 API 位址會收到這台的金鑰：這台已經存了「自訂」的金鑰就不讓別人換掉位址
            if (local?["AiCustomUrl"] is { } url && local["AiKeys"] is JsonObject { } keys && keys.ContainsKey(Agents.AiService.Custom)) obj["AiCustomUrl"] = url.DeepClone();

            var s = obj.Deserialize<AppSettings>();
            if (s == null) return null;
            foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanWrite && !property.PropertyType.IsValueType && property.GetValue(s) == null) return null;
            Sanitize(s, folder);
            return JsonSerializer.Serialize(s, Indented);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or ArgumentException) { return null; }
    }

    private static void Sanitize(AppSettings s, string folder)
    {
        s.Opacity = s.Opacity is >= 0.55 and <= 1 ? s.Opacity : 1;
        s.WaterMinutes = Math.Clamp(s.WaterMinutes, 1, 24 * 60);
        s.FocusMinutes = Math.Clamp(s.FocusMinutes, 1, 180);
        s.BreakMinutes = Math.Clamp(s.BreakMinutes, 1, 120);
        s.LyricsLines = s.LyricsLines <= 3 ? 3 : s.LyricsLines >= 7 ? 7 : 5;
        if (s.AiCustomUrl.Length > 0 && !(Uri.TryCreate(s.AiCustomUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)) s.AiCustomUrl = "";
        s.LyricsProviders = s.LyricsProviders.Where(LyricsFinder.Known.ContainsKey).Distinct().ToList();
        s.Todos.RemoveAll(t => t == null);
        s.LyricsFolders.RemoveAll(string.IsNullOrWhiteSpace);
        s.NotificationApps.RemoveAll(string.IsNullOrWhiteSpace);
        // 暫存架自己的複本記的是原本那台的路徑：改指到還原後的 shelf 資料夾
        s.Shelf = s.Shelf.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Rebase(p, folder)).ToList();
    }

    private static string Rebase(string path, string folder)
    {
        var parts = path.Split('\\', '/');
        return parts.Length >= 2 && parts[^2].Equals("shelf", StringComparison.OrdinalIgnoreCase) && parts[^1].Length > 0
            ? Path.Combine(folder, "shelf", parts[^1]) : path;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = new FileStream(Path.Combine(to, Path.GetFileName(file)), FileMode.CreateNew, FileAccess.Write);
            source.CopyTo(target);
        }
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private static void TryDeleteFolder(string path) { try { Directory.Delete(path, true); } catch { } }
}

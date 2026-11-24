// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
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
    /// 暫存架自己存的複本只有文字與圖片（Shelf.SaveText／SaveImage）。暫存架的項目點一下就用關聯的程式開啟，
    /// 所以 shelf 資料夾只收這兩種：備份裡塞的 .exe、.lnk、.bat… 一律不匯出、不還原。
    /// </summary>
    private static readonly string[] ShelfExtensions = [".txt", ".png"];

    // ---------------------------------------------------------------- 設定欄位的四種歸類
    // 備份可能是別人分享的「設定檔」，還原時要當成不可信的輸入。AppSettings 每加一個屬性，都要想它從 zip 進來能做什麼，放進其中一類：
    //   機密（SecretFields）    金鑰、token、session。不匯出，還原時用這台的。
    //   只用本機（LocalFields） 連到哪裡、用誰的帳號花錢或回報、開什麼權限（麥克風、通知、快捷鍵、Stream Dock 允許、檢查新版）、
    //                          以及只對這台有意義的狀態。不匯出，還原時一律用這台原本的（這台沒有設定檔就是預設值）。
    //   只能變嚴（Tighten）     關掉了就不能被備份打開的隱私開關與範圍：備份只能把它關掉、縮小。照常匯出。
    //   照備份（其他全部）       外觀、待辦、歌詞偏好這類資料：照備份的，可疑的值在 Sanitize 改回安全的範圍。
    // 拿不準的就放「只用本機」：還原之後使用者要自己重設幾個開關，比被別人的備份打開麥克風好。

    /// <summary>
    /// 機密：DPAPI 加密的（AI 金鑰、Last.fm 的 secret 與 session、ListenBrainz token）與 Roon 的 token。
    /// 匯出時拿掉，還原時保留這台原本的。
    /// </summary>
    public static readonly string[] SecretFields = ["AiKeys", "AnthropicKey", "LastFmSecret", "LastFmSession", "RoonTokens", "ListenBrainzToken"];

    /// <summary>
    /// 只用本機：跟上面的機密一樣不匯出、還原時一律用這台原本的。
    /// 連線目標（Roon、自訂 AI 位址）能被改的話，等於能把這台存著的 token、對 AI 說的話導到別人的伺服器；
    /// AI 服務與模型會用這台的金鑰花錢；Last.fm 的 API key 與帳號要跟這台的 secret、session 配成一對，不然之後每一首都送不出去；
    /// Discord 的 Application ID 會開始公開顯示在聽什麼；Roon 搜尋會改變向 Roon 要的權限。
    /// 麥克風（喚醒詞、連續對話、辨識引擎與模型）、通知內容（開關與哪些程式）、全域快捷鍵、Stream Dock 的允許、檢查新版：權限類的開關。
    /// 勿擾、上次執行的版本、設定檔版本：這台自己的狀態（舊備份的版本會讓重新啟動跳出假的「已更新」、重跑或跳過升級步驟）。
    /// </summary>
    public static readonly string[] LocalFields =
    [
        "RoonHost", "RoonPort", "RoonExtensionId", "RoonSearch", "AiCustomUrl", "AiService", "AiModels",
        "LastFmApiKey", "LastFmUser", "DiscordClientId",
        "WakeWord", "VoiceContinuous", "SpeechEngine", "WhisperModel", "Notifications", "NotificationApps",
        "HotkeyToggle", "HotkeyAsk", "DeckAllow", "CheckUpdates",
        "DoNotDisturbUntil", "LastRunVersion", "Version",
    ];

    /// <summary>只能變嚴的欄位（照常匯出，還原時見 <see cref="Tighten"/>）。</summary>
    public static readonly string[] StricterFields = ["History", "HistoryExcluded", "DiscordCover", "LyricsProviders"];

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
                    // 捷徑資料夾（junction、symlink）不跟進去：裡面是別的地方的檔案，不是 Shima 的資料
                    foreach (var file in Directory.EnumerateFiles(top, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    {
                        var entryName = Path.GetRelativePath(root, file).Replace('\\', '/');
                        if (Skipped(file)) continue;
                        // 還原時會被拒絕或略過的名稱（含格式字元、暫存架裡不是文字或圖片的）不匯出：匯出的一定還原得回來
                        if (!SafeName(entryName, out _) || Known(entryName) == null) { Log.Write("backup export skip: " + entryName); continue; }
                        if (++count > MaxFiles) return new BackupResult(BackupStatus.TooBig);
                        total += AddFile(zip, file, entryName);
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
            foreach (var field in SecretFields.Concat(LocalFields)) obj.Remove(field);
            return obj.ToJsonString(Indented);
        }
        catch (JsonException) { return null; }
    }

    // ---------------------------------------------------------------- 路徑檢查

    private static readonly Regex Reserved = new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|CLOCK\$|(COM|LPT)[0-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// zip 項目的名稱是不是一個安全的相對路徑：不能是絕對路徑、磁碟代號、UNC、含 ..、含 NTFS 資料流的冒號、
    /// 保留的裝置名稱，也不能有 Windows 不接受的字元，也不能有看不見的格式字元（U+202E 右至左覆寫能讓
    /// 「報告‮fdp.exe」顯示成「報告exe.pdf」）。通過的話 <paramref name="relative"/> 是用 / 分隔的正規化路徑。
    /// </summary>
    public static bool SafeName(string? name, out string relative)
    {
        relative = "";
        if (string.IsNullOrEmpty(name) || name.Length > 260 || name.Any(c => c < 0x20 || char.GetUnicodeCategory(c) == UnicodeCategory.Format)) return false;
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
        if (parts.Length >= 2 && KnownFolders.FirstOrDefault(f => f.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) is { } dir && !Skipped(relative)
            && (dir != "shelf" || ShelfExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)))
            return dir + "/" + string.Join('/', parts.Skip(1));
        return null;
    }

    /// <summary>是不是 shelf 資料夾最上層、卻因為副檔名被略過的檔案；是的話回檔名。</summary>
    private static string? SkippedShelfCopy(string relative) =>
        relative.Split('/') is [var dir, var name] && dir.Equals("shelf", StringComparison.OrdinalIgnoreCase) && Known(relative) == null ? name : null;

    // ---------------------------------------------------------------- 還原

    /// <param name="committing">
    /// 驗證全部通過、確定要換上去的那一刻呼叫（在還原前的複本與換檔之前）；被拒絕的 zip 不會走到。
    /// Restore 在背景執行緒跑，要碰畫面的話回呼自己切回畫面的執行緒（同步地做完再回來）。
    /// </param>
    public static BackupResult Restore(string zipPath, string folder, DateTime now, Action? committing = null)
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
            var skippedShelf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long declared = 0;
            foreach (var entry in zip.Entries)
            {
                if (!SafeName(entry.FullName, out var relative)) return new BackupResult(BackupStatus.Unsafe);
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                if (!seen.Add(relative)) return new BackupResult(BackupStatus.Unsafe);   // 同名兩個項目：不知道該用哪一個
                declared += entry.Length;
                if (declared > MaxBytes) return new BackupResult(BackupStatus.TooBig);
                if (Known(relative) is { } name) picks.Add((entry, name));
                else if (SkippedShelfCopy(relative) is { } skipped) skippedShelf.Add(skipped);
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

            // 3. 設定檔要能讀得懂才採用；機密欄位與連線目標一律用這台原本的
            var stagedSettings = Path.Combine(stage, "settings.json");
            if (File.Exists(stagedSettings))
            {
                var copies = picks.Select(p => p.Name.Split('/')).Where(p => p.Length == 2 && p[0] == "shelf").Select(p => p[1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (MergeSettings(File.ReadAllText(stagedSettings), Path.Combine(root, "settings.json"), root, copies, skippedShelf) is not { } merged)
                    return new BackupResult(BackupStatus.BadSettings);
                File.WriteAllText(stagedSettings, merged, new UTF8Encoding(false));
            }

            // 4. 確定要換了：讓呼叫端先把手上的東西結算掉（例如正在聽的這一首），這樣它也會在下面那份複本裡
            committing?.Invoke();

            // 還原錯了要救得回來：先把目前的整份複製一份
            string? saved = null;
            if (Directory.Exists(root))
            {
                saved = $"Shima-before-restore-{now:yyyyMMdd-HHmmss}";
                for (int i = 2; Directory.Exists(Path.Combine(parent, saved)); i++) saved = $"Shima-before-restore-{now:yyyyMMdd-HHmmss}-{i}";
                try { CopyFolder(root, Path.Combine(parent, saved)); }
                catch { TryDeleteFolder(Path.Combine(parent, saved)); throw; }
            }

            // 5. 換上去
            var undone = Apply(stage, root, picks.Select(p => p.Name.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            if (undone == null) return new BackupResult(BackupStatus.Ok, picks.Count, saved);
            // 換到一半失敗。全部換回來了：資料沒變，那份複本不用留。有東西沒能換回來：舊資料只剩那份複本，留著並回報在哪裡
            if (undone == true && saved != null) { TryDeleteFolder(Path.Combine(parent, saved)); saved = null; }
            return new BackupResult(BackupStatus.Failed, 0, saved);
        }
        catch (Exception e) { Log.Write("backup restore: " + e.Message); return new BackupResult(BackupStatus.Failed); }
        finally { if (stage != null) TryDeleteFolder(stage); }
    }

    /// <summary>換一項的進度：舊的移開了沒、新的放進來了沒。復原要照實際走到哪一步來做。</summary>
    private sealed class Swap(string target, string old)
    {
        public string Target { get; } = target;
        public string Old { get; } = old;
        public bool OldMoved, NewIn;
    }

    /// <summary>
    /// 把暫存區的每一項換上去：舊的先移到暫存區，再把新的移進來。成功回傳 null。
    /// 中途失敗（例如檔案正被別的程式開著）就照每一項實際走到的步驟換回來，回傳有沒有全部換回來。
    /// 不可以憑「這一項開始處理了」就動手復原：舊的還沒移開時，位置上的就是使用者原本的資料，刪不得。
    /// </summary>
    private static bool? Apply(string stage, string root, List<string> items)
    {
        var trash = Path.Combine(stage, ".old");
        Directory.CreateDirectory(trash);
        Directory.CreateDirectory(root);
        var swaps = new List<Swap>();
        try
        {
            foreach (var item in items)
            {
                var swap = new Swap(Path.Combine(root, item), Path.Combine(trash, item));
                swaps.Add(swap);
                if (File.Exists(swap.Target) || Directory.Exists(swap.Target)) { Move(swap.Target, swap.Old); swap.OldMoved = true; }
                Move(Path.Combine(stage, item), swap.Target);
                swap.NewIn = true;
            }
            return null;
        }
        catch (Exception e)
        {
            Log.Write("backup apply: " + e.Message);
            bool clean = true;
            foreach (var swap in Enumerable.Reverse(swaps))
            {
                try
                {
                    if (swap.NewIn) { if (File.Exists(swap.Target)) File.Delete(swap.Target); else if (Directory.Exists(swap.Target)) Directory.Delete(swap.Target, true); }
                    if (swap.OldMoved) Move(swap.Old, swap.Target);
                }
                catch (Exception again) { Log.Write("backup rollback: " + again.Message); clean = false; }
            }
            return clean;
        }
    }

    private static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
    }

    /// <summary>
    /// 把 zip 裡的設定檔和這台的設定合起來：機密欄位與連線目標用這台的（沒有就是預設值）、可疑的值改回安全的範圍。
    /// 讀不懂（不是物件、型別不對、欄位是 null）就是 null。
    /// </summary>
    /// <param name="shelfCopies">備份的 shelf 資料夾裡有哪些檔名；null：不知道（只在測試裡）。</param>
    /// <param name="shelfSkipped">備份的 shelf 資料夾裡因為副檔名被略過的檔名：暫存架上指向它們的項目拿掉。</param>
    public static string? MergeSettings(string incoming, string localPath, string folder, IReadOnlySet<string>? shelfCopies = null, IReadOnlySet<string>? shelfSkipped = null)
    {
        try
        {
            if (JsonNode.Parse(incoming) is not JsonObject obj) return null;
            JsonObject? local = null;
            try { if (File.Exists(localPath)) local = JsonNode.Parse(ReadText(localPath)) as JsonObject; } catch (JsonException) { }
            // zip 裡的一律不用（照理說匯出時就拿掉了，有的話是別人塞的）；這台有就用這台的
            foreach (var field in SecretFields.Concat(LocalFields)) { obj.Remove(field); if (local?[field] is { } mine) obj[field] = mine.DeepClone(); }

            var s = obj.Deserialize<AppSettings>();
            if (s == null) return null;
            foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.CanWrite && !property.PropertyType.IsValueType && property.GetValue(s) == null) return null;
            Tighten(s, Mine(local));
            Sanitize(s, folder, Paths(local), shelfCopies, shelfSkipped);
            return JsonSerializer.Serialize(s, Indented);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or ArgumentException) { return null; }
    }

    /// <summary>這台原本的設定；沒有或讀不懂就是預設值。</summary>
    private static AppSettings Mine(JsonObject? local)
    {
        try { if (local?.Deserialize<AppSettings>() is { } mine) return mine; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or ArgumentException) { }
        return new AppSettings();
    }

    /// <summary>
    /// 只能變嚴：這台關掉的不會因為還原又打開、排除的不會被拿掉。
    /// 聆聽紀錄（記下來還會用這台的帳號回報出去）、排除的來源取聯集、Discord 封面（歌名會送到 Apple）、
    /// 歌詞來源（歌名會送到那些網站）只留這台也開著的，順序照備份。
    /// </summary>
    private static void Tighten(AppSettings s, AppSettings mine)
    {
        s.History &= mine.History;
        s.HistoryExcluded = (mine.HistoryExcluded ?? []).Concat(s.HistoryExcluded).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        s.DiscordCover &= mine.DiscordCover;
        if (mine.LyricsProviders is { } allowed) s.LyricsProviders = s.LyricsProviders.Where(allowed.Contains).ToList();
    }

    /// <summary>這台原本就設定過的路徑（角色圖片、歌詞資料夾、暫存架）：使用者自己加的，還原時照舊信任。</summary>
    private static HashSet<string> Paths(JsonObject? local)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(JsonNode? node) { if (node is JsonValue value && value.TryGetValue(out string? path) && path != null) known.Add(path); }
        Add(local?["CharacterImage"]);
        foreach (var list in new[] { "LyricsFolders", "Shelf" })
            if (local?[list] is JsonArray array) foreach (var item in array) Add(item);
        return known;
    }

    /// <summary>
    /// 本機磁碟上的路徑（X:\…）。網路路徑（\\server\share）與裝置路徑（\\?\、\\.\）不算：
    /// Shima 一去讀，Windows 就會拿這個帳號向那台伺服器登入，來路不明的備份不該能讓這件事發生。
    /// </summary>
    public static bool OnThisComputer(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';

    private static void Sanitize(AppSettings s, string folder, HashSet<string> trusted, IReadOnlySet<string>? shelfCopies, IReadOnlySet<string>? shelfSkipped)
    {
        s.Opacity = s.Opacity is >= 0.55 and <= 1 ? s.Opacity : 1;
        s.WaterMinutes = Math.Clamp(s.WaterMinutes, 1, 24 * 60);
        s.FocusMinutes = Math.Clamp(s.FocusMinutes, 1, 180);
        s.BreakMinutes = Math.Clamp(s.BreakMinutes, 1, 120);
        s.LyricsLines = s.LyricsLines <= 3 ? 3 : s.LyricsLines >= 7 ? 7 : 5;
        s.LyricsProviders = s.LyricsProviders.Where(LyricsFinder.Known.ContainsKey).Distinct().ToList();
        s.Todos.RemoveAll(t => t == null);
        s.NotificationApps.RemoveAll(string.IsNullOrWhiteSpace);
        s.HistoryExcluded.RemoveAll(string.IsNullOrWhiteSpace);

        // 路徑：這台原本就有的照舊，其他的只收本機磁碟上的（或 Shima 自己的資料夾裡的）
        var inside = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        bool Ok(string path) => trusted.Contains(path) || OnThisComputer(path) || path.StartsWith(inside, StringComparison.OrdinalIgnoreCase);
        if (!Ok(s.CharacterImage)) s.CharacterImage = "";
        s.LyricsFolders.RemoveAll(p => string.IsNullOrWhiteSpace(p) || !Ok(p));
        s.Shelf = s.Shelf.Where(p => !string.IsNullOrWhiteSpace(p) && !SkippedCopy(p, shelfSkipped)).Select(p => Rebase(p, folder, shelfCopies)).Where(Ok).ToList();
    }

    /// <summary>
    /// 暫存架自己的複本記的是原本那台的完整路徑：備份裡有這個檔的，改指到還原後的 shelf 資料夾。
    /// 只是剛好放在某個叫 shelf 的資料夾裡的別的檔案不動。
    /// </summary>
    private static string Rebase(string path, string folder, IReadOnlySet<string>? shelfCopies)
    {
        var parts = path.Split('\\', '/');
        return parts.Length >= 2 && parts[^2].Equals("shelf", StringComparison.OrdinalIgnoreCase) && parts[^1].Length > 0 && parts[^1] is not ("." or "..")
               && (shelfCopies == null || shelfCopies.Contains(parts[^1]))
            ? Path.Combine(folder, "shelf", parts[^1]) : path;
    }

    /// <summary>暫存架的項目是不是指向備份裡被略過的那些複本（某個 shelf 資料夾裡、檔名在略過的清單上）。</summary>
    private static bool SkippedCopy(string path, IReadOnlySet<string>? skipped)
    {
        if (skipped is not { Count: > 0 }) return false;
        var parts = path.Split('\\', '/');
        return parts.Length >= 2 && parts[^2].Equals("shelf", StringComparison.OrdinalIgnoreCase) && skipped.Contains(parts[^1]);
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
        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            // 捷徑資料夾不跟進去（裡面不是 Shima 的資料，還可能繞回自己）
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private static void TryDeleteFolder(string path) { try { Directory.Delete(path, true); } catch { } }
}

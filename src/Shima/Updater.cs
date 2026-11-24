// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Shima;

/// <summary>到 GitHub Releases 看有沒有新版，有的話下載並換掉自己。</summary>
public static class Updater
{
    private const string Latest = "https://api.github.com/repos/Carinoasd/shima/releases/latest";
    private static readonly HttpClient Http = Client();

    private static HttpClient Client()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5), MaxResponseContentBufferSize = 2 * 1024 * 1024 };   // 只管 releases/latest 的 JSON；安裝檔是邊收邊寫，另外用 release.Size 擋
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Shima");
        return http;
    }

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    /// <summary>只有打包成單一執行檔的才能自己更新（開發時的 Shima.exe 旁邊有 Shima.dll，換掉會壞）。</summary>
    public static bool CanApply => Environment.ProcessPath is { } exe && !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "Shima.dll"));

    /// <summary>「v0.6.0」這種標籤是不是比 current 新。認不得的標籤一律當作不是。</summary>
    public static Version? Newer(string? tag, Version current)
    {
        if (!Version.TryParse((tag ?? "").TrimStart('v', 'V'), out var version)) return null;
        var now = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build)) > now ? version : null;
    }

    public enum VersionChange { First, Same, Upgraded, Downgraded }

    /// <summary>
    /// 現在的版本跟上次記下的比：沒記過（或認不得）是 First，一樣是 Same。
    /// 記下的可以帶 v 或「0.7.1+commit」「0.8.0-beta」這種後綴；比的方式跟 <see cref="Newer"/> 一樣只看前三段。
    /// </summary>
    public static VersionChange Compare(string? last, Version current)
    {
        var text = (last ?? "").Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0) text = text[..cut];
        if (!Version.TryParse(text, out var before)) return VersionChange.First;
        var then = new Version(before.Major, before.Minor, Math.Max(0, before.Build));
        var now = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        return now > then ? VersionChange.Upgraded : now < then ? VersionChange.Downgraded : VersionChange.Same;
    }

    /// <summary>這一版的更新說明頁。</summary>
    public static string NotesUrl(Version version) => $"https://github.com/Carinoasd/shima/releases/tag/v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    /// <summary>一個可以裝的新版：下載網址，以及 GitHub 記錄的檔案大小與 SHA-256。</summary>
    public sealed record Release(Version Version, string Url, long Size, string Sha256);

    /// <summary>只認這個專案發佈頁的下載連結：https、github.com、/Carinoasd/shima/releases/download/ 底下。</summary>
    public static bool ValidUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/Carinoasd/shima/releases/download/", StringComparison.Ordinal) && !uri.AbsolutePath.Contains("..");

    /// <summary>GitHub 給的是「sha256:十六進位」；取出後面那 64 個字，格式不對就是 null。</summary>
    public static string? Sha256Of(string? digest)
    {
        const string prefix = "sha256:";
        if (digest == null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    /// <summary>從 releases/latest 的回應挑出可以裝的新版；沒有新版、或資料不齊（沒有雜湊就不裝），就是 null。</summary>
    public static Release? Pick(JsonObject release, Version current)
    {
        if (Newer(release["tag_name"]?.ToString(), current) is not { } version) return null;
        var asset = (release["assets"] as JsonArray)?.FirstOrDefault(a => a?["name"]?.ToString() == "Shima.exe");
        var url = asset?["browser_download_url"]?.ToString();
        var sha = Sha256Of(asset?["digest"]?.ToString());
        long size = asset?["size"] is JsonValue v ? v.TryGetValue<long>(out var n) ? n : v.TryGetValue<int>(out var small) ? small : v.TryGetValue<double>(out var real) ? (long)real : 0 : 0;
        return ValidUrl(url) && sha != null && size > 0 ? new Release(version, url!, size, sha) : null;
    }

    public static async Task<Release?> CheckAsync()
    {
        try
        {
            return JsonNode.Parse(await Http.GetStringAsync(Latest)) is JsonObject release ? Pick(release, Current) : null;
        }
        catch (Exception e) { Log.Write("update: " + e.Message); return null; }
    }

    private static int _applying;

    /// <summary>
    /// 下載新版放到自己旁邊，確認大小與 SHA-256 都跟 GitHub 記錄的一樣，才把正在執行的自己改名成 .old
    /// （Windows 允許改名、不允許覆寫），新的放回原位。成功後呼叫端重新啟動就是新版；.old 在下次啟動時由 Cleanup 刪掉。
    /// </summary>
    public static async Task<bool> ApplyAsync(Release release)
    {
        if (!CanApply || Environment.ProcessPath is not { } exe || !ValidUrl(release.Url)) return false;
        if (Interlocked.Exchange(ref _applying, 1) == 1) return false;   // 已經有一次在下載了
        string fresh = exe + ".new", old = exe + ".old";
        try
        {
            if (!await DownloadAsync(Http, release, fresh)) { Log.Write("update: 下載的內容比發佈的大小還大，中止"); return false; }
            if (!Matches(fresh, release.Size, release.Sha256)) { TryDelete(fresh); Log.Write("update: 下載到的檔案跟發佈的不一樣（大小或雜湊不符），不安裝"); return false; }

            await RetryAsync(() => { if (File.Exists(old)) File.Delete(old); });
            await RetryAsync(() => File.Move(exe, old));
            try { await RetryAsync(() => File.Move(fresh, exe)); }
            catch
            {
                // 放不回去（多半是防毒軟體正在掃剛下載的檔案）：一定要把原本的還原，不然執行檔就不見了
                await RetryAsync(() => File.Move(old, exe), attempts: 20);
                throw;
            }
            return true;
        }
        catch (Exception e) { Log.Write("update: " + e.Message); TryDelete(fresh); return false; }
        finally { Interlocked.Exchange(ref _applying, 0); }
    }

    /// <summary>下載時多久沒有收到任何資料就放棄。下載本身可以很久（慢的網路），但不能停住不動。</summary>
    public static readonly TimeSpan Stalled = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 下載到 <paramref name="path"/>，邊收邊算：超過 GitHub 回報的大小（<see cref="Release.Size"/>）就中止、刪掉暫存檔，回 false。
    /// 不能等寫完才比，不然對方（或中間人）送多大就寫多大到磁碟。網路錯誤照舊丟例外（暫存檔也會刪）。
    /// 用 ResponseHeadersRead 時 HttpClient.Timeout 只管到收到標頭為止：之後 <paramref name="idle"/>（預設 <see cref="Stalled"/>）沒有收到任何資料，
    /// 丟 TimeoutException，不然「正在更新」的狀態永遠不會結束。
    /// </summary>
    public static async Task<bool> DownloadAsync(HttpClient http, Release release, string path, TimeSpan? idle = null)
    {
        var patience = idle ?? Stalled;
        using var quiet = new CancellationTokenSource();
        try
        {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > release.Size) return false;
            quiet.CancelAfter(patience);
            await using var from = await response.Content.ReadAsStreamAsync(quiet.Token).WaitAsync(quiet.Token);
            await using var to = File.Create(path);
            var chunk = new byte[80 * 1024];
            long total = 0;
            int n;
            // 每次讀都另外用 WaitAsync 等：底下的串流不理會 token 的話也不會卡住
            while ((n = await from.ReadAsync(chunk, quiet.Token).AsTask().WaitAsync(quiet.Token)) > 0)
            {
                quiet.CancelAfter(patience);   // 有收到資料就重新計時
                total += n;
                if (total > release.Size) { to.Close(); TryDelete(path); return false; }
                await to.WriteAsync(chunk.AsMemory(0, n));
            }
            return true;
        }
        catch (OperationCanceledException) when (quiet.IsCancellationRequested)
        {
            TryDelete(path);
            throw new TimeoutException($"下載停住超過 {patience.TotalSeconds:0.#} 秒");
        }
        catch { TryDelete(path); throw; }
    }

    /// <summary>檔案是不是發佈的那一個：大小一樣、SHA-256 一樣，而且是 Windows 執行檔。</summary>
    public static bool Matches(string path, long size, string sha256)
    {
        try
        {
            if (new FileInfo(path).Length != size || !LooksLikeProgram(path)) return false;
            using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
            return hash.Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>檔案剛寫完常被防毒軟體或索引服務短暫鎖住：隔一下再試。</summary>
    private static async Task RetryAsync(Action move, int attempts = 8)
    {
        for (int i = 1; ; i++)
        {
            try { move(); return; }
            catch (Exception e) when (i < attempts && e is IOException or UnauthorizedAccessException) { await Task.Delay(250 * i); }
        }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    /// <summary>Windows 執行檔以「MZ」開頭；Shima 的單一執行檔有幾十 MB，太小的一定是下載壞了。</summary>
    public static bool LooksLikeProgram(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return file.Length > 5 * 1024 * 1024 && file.ReadByte() == 'M' && file.ReadByte() == 'Z';
        }
        catch { return false; }
    }

    public static void Cleanup()
    {
        try { if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old")) File.Delete(exe + ".old"); }
        catch { /* 上一個還沒完全結束，下次再刪 */ }
    }
}

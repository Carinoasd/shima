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
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
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

    /// <summary>有新版就回傳版本與 Shima.exe 的下載網址；沒有、或查不到，就是 null。</summary>
    public static async Task<(Version Version, string Url)?> CheckAsync()
    {
        try
        {
            if (JsonNode.Parse(await Http.GetStringAsync(Latest)) is not JsonObject release) return null;
            if (Newer(release["tag_name"]?.ToString(), Current) is not { } version) return null;
            var url = (release["assets"] as JsonArray)?.FirstOrDefault(a => a?["name"]?.ToString() == "Shima.exe")?["browser_download_url"]?.ToString();
            return url is { Length: > 0 } && url.StartsWith("https://github.com/Carinoasd/shima/") ? (version, url) : null;
        }
        catch (Exception e) { Log.Write("update: " + e.Message); return null; }
    }

    /// <summary>
    /// 下載新版放到自己旁邊，把正在執行的自己改名成 .old（Windows 允許改名、不允許覆寫），新的放回原位。
    /// 成功後呼叫端重新啟動就是新版；.old 在下次啟動時由 Cleanup 刪掉。
    /// </summary>
    public static async Task<bool> ApplyAsync(string url)
    {
        if (!CanApply || Environment.ProcessPath is not { } exe) return false;
        string fresh = exe + ".new", old = exe + ".old";
        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var to = File.Create(fresh);
                await response.Content.CopyToAsync(to);
            }
            if (!LooksLikeProgram(fresh)) { File.Delete(fresh); Log.Write("update: 下載到的不是執行檔"); return false; }
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }   // 放不回去就把原本的還原
            return true;
        }
        catch (Exception e) { Log.Write("update: " + e.Message); try { File.Delete(fresh); } catch { } return false; }
    }

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

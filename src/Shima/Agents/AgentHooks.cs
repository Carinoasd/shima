// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>
/// 在 Claude Code 的 settings.json 加上／拿掉 Shima 的 hook。只碰帶有 X-Shima-Token 的那幾筆，其他一律不動。
/// Windows 和 WSL 各有一份設定，兩邊都處理；hook 本身只是呼叫 Windows 內建的 curl.exe。
/// </summary>
public static class AgentHooks
{
    private const string Mark = "X-Shima-Token";
    /// <summary>
    /// Shima 裝的 hook：事件、matcher、要不要等（async 的在背景跑，不拖慢 Claude Code）、curl 最多等幾秒、hook 的 timeout。
    /// 權限請求要等人按；Stop 照舊同步送。其餘只是讓島知道每個工作階段在做什麼，全部背景送。
    /// SessionEnd 不能用背景（Claude Code 結束時會被砍掉），所以 curl 只等一秒、timeout 兩秒：
    /// Claude Code 結束前最多等的就是這個 timeout，不能設長。
    /// </summary>
    private static readonly (string Event, string? Matcher, bool Async, int Wait, int Timeout)[] Hooks =
    [
        ("PermissionRequest", null, false, 590, 600),
        ("Notification", null, true, 3, 10),
        ("Stop", null, false, 3, 10),
        ("PostToolUse", PlanTools, false, 3, 10),
        ("PostToolUse", OtherTools, true, 3, 10),
        ("SessionStart", null, true, 3, 10),
        ("UserPromptSubmit", null, true, 3, 10),
        ("StopFailure", null, true, 3, 10),
        ("SessionEnd", null, false, 1, 2),
    ];
    /// <summary>
    /// 待辦清單工具的 PostToolUse 同步送，進度一定比 Stop 先到；其他工具的 PostToolUse 在背景送，
    /// 只是讓「在終端機按了允許之後」狀態回到工作中。
    /// </summary>
    private const string PlanTools = "TaskCreate|TaskUpdate|TodoWrite";
    /// <summary>PlanTools 以外的工具（matcher 有特殊字元時 Claude Code 當成 JavaScript 正規表示式），免得同一次呼叫送兩次。</summary>
    private const string OtherTools = "^(?!(TaskCreate|TaskUpdate|TodoWrite)$)";

    /// <summary>Shima 用到的事件（測試與升級檢查用）。</summary>
    public static IReadOnlyList<string> Events => Hooks.Select(h => h.Event).Distinct().ToList();

    public sealed record Target(string SettingsPath, string Curl);

    /// <summary>這台電腦上 Claude Code 的設定檔：Windows 的，以及預設 WSL 發行版的。</summary>
    public static List<Target> Targets()
    {
        var list = new List<Target>
        {
            new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"), "curl.exe"),
        };
        try
        {
            var info = new ProcessStartInfo("wsl.exe", "-e sh -c \"printf '%s|%s' \\\"$WSL_DISTRO_NAME\\\" \\\"$HOME\\\"\"")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(info);
            if (p != null && p.WaitForExit(6000) && p.ExitCode == 0)
            {
                var parts = p.StandardOutput.ReadToEnd().Trim().Split('|');
                if (parts.Length == 2 && parts[0].Length > 0 && parts[1].StartsWith('/'))
                {
                    var home = $@"\\wsl.localhost\{parts[0]}{parts[1].Replace('/', '\\')}";
                    if (Directory.Exists(Path.Combine(home, ".claude")))
                        list.Add(new(Path.Combine(home, ".claude", "settings.json"), "/mnt/c/Windows/System32/curl.exe"));
                }
            }
        }
        catch (Exception e) { Log.Write("wsl: " + e.Message); }
        return list;
    }

    /// <summary>
    /// 一次查完「裝了沒」與「是不是舊版」。要啟動 wsl.exe、讀 WSL 裡的檔案，可能花上一兩秒，不要在畫面的執行緒上呼叫。
    /// </summary>
    public static (bool Installed, bool Outdated) State()
    {
        var settings = Targets().Select(t => Read(t.SettingsPath)).ToList();
        return (settings.Any(s => Has(s)), settings.Any(s => s is { } found && Has(found) && !Complete(found)));
    }

    public static bool Installed => Targets().Any(t => Has(Read(t.SettingsPath)));

    /// <summary>連接過、但是是舊版 Shima 裝的（少了幾個 hook）：再按一次「連接」，或直接呼叫 Install() 就會升級。</summary>
    public static bool Outdated => Targets().Any(t => Read(t.SettingsPath) is { } s && Has(s) && !Complete(s));

    /// <summary>Claude Code 的設定資料夾（Windows 的 ~/.claude，以及預設 WSL 發行版的）。</summary>
    public static List<string> ClaudeFolders() => Targets().Select(t => Path.GetDirectoryName(t.SettingsPath)!).ToList();

    /// <summary>回傳錯誤訊息；null 表示成功。</summary>
    public static string? Install() => Each(t => With(Read(t.SettingsPath) ?? [], t.Curl, AgentBridge.Token()));

    public static string? Remove() => Each(t => Read(t.SettingsPath) is { } s && Has(s) ? Without(s) : null);

    private static string? Each(Func<Target, JsonObject?> change)
    {
        string? problem = null;
        foreach (var target in Targets())
        {
            try
            {
                if (File.Exists(target.SettingsPath) && Read(target.SettingsPath) == null)
                {
                    problem = L.F("{0} 不是有效的 JSON，沒有更動", target.SettingsPath);
                    continue;
                }
                if (change(target) is not { } updated) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target.SettingsPath)!);
                var backup = target.SettingsPath + ".before-shima.bak";
                if (File.Exists(target.SettingsPath) && !File.Exists(backup)) File.Copy(target.SettingsPath, backup);
                File.WriteAllText(target.SettingsPath, updated.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            }
            catch (Exception e) { problem = e.Message; Log.Write("hooks: " + e.Message); }
        }
        return problem;
    }

    private static JsonObject? Read(string path)
    {
        try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null; }
        catch { return null; }
    }

    public static bool Has(JsonObject? settings) =>
        settings?["hooks"] is JsonObject hooks && hooks.Any(e => (e.Value as JsonArray)?.Any(Ours) == true);

    /// <summary>Shima 的 hook 每一個都在（而且沒有多出重複的）。</summary>
    public static bool Complete(JsonObject? settings)
    {
        if (settings?["hooks"] is not JsonObject hooks) return false;
        foreach (var name in Events)
        {
            int want = Hooks.Count(h => h.Event == name);
            int have = (hooks[name] as JsonArray)?.Count(Ours) ?? 0;
            if (have != want) return false;
        }
        return true;
    }

    private static bool Ours(JsonNode? group) =>
        (group?["hooks"] as JsonArray)?.Any(h => h?["command"]?.ToString().Contains(Mark) == true) == true;

    /// <summary>設定加上 Shima 的 hook（先拿掉舊的，所以重複連接不會越加越多）。</summary>
    public static JsonObject With(JsonObject settings, string curl, string token)
    {
        settings = Without(settings);
        var hooks = settings["hooks"] as JsonObject ?? [];
        settings["hooks"] = hooks;
        foreach (var (name, matcher, background, wait, timeout) in Hooks)
        {
            bool permission = name == "PermissionRequest";
            // 權限請求要等人按；其他的送了就走。Shima 沒開時 curl 會失敗，Claude Code 就照常運作。
            // /notify 永遠不回內容：SessionStart、UserPromptSubmit 的輸出會被 Claude Code 當成給模型看的內容
            var command = $"{curl} -s -m {wait} -X POST -H \"{Mark}: {token}\" -H \"Content-Type: application/json\" --data-binary @- http://localhost:{AgentBridge.Port}/{(permission ? "permission" : "notify")}";
            var list = hooks[name] as JsonArray ?? [];
            hooks[name] = list;
            var group = new JsonObject();
            if (matcher != null) group["matcher"] = matcher;
            var hook = new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = timeout };
            if (background) hook["async"] = true;
            group["hooks"] = new JsonArray(hook);
            list.Add(group);
        }
        return settings;
    }

    /// <summary>拿掉 Shima 的 hook，別人的原封不動。</summary>
    public static JsonObject Without(JsonObject settings)
    {
        settings = (JsonObject)settings.DeepClone();
        if (settings["hooks"] is not JsonObject hooks) return settings;
        foreach (var name in hooks.Select(e => e.Key).ToList())
        {
            if (hooks[name] is not JsonArray groups) continue;
            foreach (var group in groups.Where(Ours).ToList()) groups.Remove(group);
            if (groups.Count == 0) hooks.Remove(name);
        }
        if (hooks.Count == 0) settings.Remove("hooks");
        return settings;
    }
}

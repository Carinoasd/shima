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
    private static readonly string[] Events = ["PermissionRequest", "Notification", "Stop", "PostToolUse"];
    /// <summary>PostToolUse 只看 Claude Code 的待辦清單工具，其他工具不必每次都呼叫。</summary>
    private const string PlanTools = "TaskCreate|TaskUpdate|TodoWrite";

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

    public static bool Installed => Targets().Any(t => Has(Read(t.SettingsPath)));

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
                    problem = $"{target.SettingsPath} 不是有效的 JSON，沒有更動";
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

    private static bool Ours(JsonNode? group) =>
        (group?["hooks"] as JsonArray)?.Any(h => h?["command"]?.ToString().Contains(Mark) == true) == true;

    /// <summary>設定加上 Shima 的 hook（先拿掉舊的，所以重複連接不會越加越多）。</summary>
    public static JsonObject With(JsonObject settings, string curl, string token)
    {
        settings = Without(settings);
        var hooks = settings["hooks"] as JsonObject ?? [];
        settings["hooks"] = hooks;
        foreach (var name in Events)
        {
            bool permission = name == "PermissionRequest";
            // 權限請求要等人按；其他的送了就走。Shima 沒開時 curl 會失敗，Claude Code 就照常運作
            var command = $"{curl} -s -m {(permission ? 590 : 3)} -X POST -H \"{Mark}: {token}\" -H \"Content-Type: application/json\" --data-binary @- http://localhost:{AgentBridge.Port}/{(permission ? "permission" : "notify")}";
            var list = hooks[name] as JsonArray ?? [];
            hooks[name] = list;
            var group = new JsonObject();
            if (name == "PostToolUse") group["matcher"] = PlanTools;
            group["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = permission ? 600 : 10 });
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

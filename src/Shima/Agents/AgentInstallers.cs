// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Target = Shima.Agents.AgentHooks.Target;

namespace Shima.Agents;

/// <summary>
/// 一個 agent 的「連接／中斷」：在它的設定檔加上／拿掉 Shima 的 hook。目標（設定檔的位置）都可以注入，測試只碰暫存資料夾。
/// </summary>
public abstract class AgentInstaller
{
    public abstract AgentKind Agent { get; }

    /// <summary>這台電腦上這個 agent 的設定檔。Claude Code 以外的只列設定資料夾已經存在的（沒裝的工具什麼都不顯示）。</summary>
    public abstract List<Target> Targets(AgentHooks.Home home);

    public abstract (bool Installed, bool Outdated) State(IEnumerable<Target> targets);
    public abstract string? Install(IEnumerable<Target> targets, string token);
    public abstract string? Remove(IEnumerable<Target> targets);

    public string? Install(AgentHooks.Home home) => Install(Targets(home), AgentBridge.Token(Agent));
    public string? Remove(AgentHooks.Home home) => Remove(Targets(home));

    public static readonly AgentInstaller Claude = new ClaudeInstaller();
    public static readonly AgentInstaller Codex = new CodexInstaller();
    public static readonly AgentInstaller Gemini = new GeminiInstaller();
    public static IReadOnlyList<AgentInstaller> All => [Claude, Codex, Gemini];

    public static AgentInstaller For(AgentKind agent) => All.First(i => i.Agent == agent);

    /// <summary>每個 agent 此刻的樣子（Detected：這台電腦找得到它的設定資料夾）。會啟動一次 wsl.exe，不要在畫面的執行緒上呼叫。</summary>
    public sealed record Status(AgentKind Agent, bool Detected, bool Installed, bool Outdated);

    public static List<Status> Scan() => Scan(AgentHooks.Homes());

    public static List<Status> Scan(AgentHooks.Home home) =>
        [.. All.Select(i =>
        {
            var targets = i.Targets(home);
            var (installed, outdated) = i.State(targets);
            return new Status(i.Agent, targets.Count > 0, installed, outdated);
        })];
}

/// <summary>Claude Code：原本的 AgentHooks，行為完全不變。</summary>
internal sealed class ClaudeInstaller : AgentInstaller
{
    public override AgentKind Agent => AgentKind.Claude;
    public override List<Target> Targets(AgentHooks.Home home) => AgentHooks.Targets(home);
    public override (bool Installed, bool Outdated) State(IEnumerable<Target> targets) => AgentHooks.State(targets);
    public override string? Install(IEnumerable<Target> targets, string token) => AgentHooks.Install(targets, token);
    public override string? Remove(IEnumerable<Target> targets) => AgentHooks.Remove(targets);
}

/// <summary>
/// 設定檔長得跟 Claude Code 一樣（"hooks" → 事件 → [{ matcher, hooks: [{ type, command, timeout }] }]）的 agent。
/// 認自己的 hook 一樣靠指令裡的 X-Shima-Token，別人的一律不動。
/// 跟 Claude Code 的不同：寫完會讀回來確認 hook 都在，好幾個設定檔（Windows 與 WSL）只要有一個失敗，已經改好的也全部還原，不留半套。
/// </summary>
public abstract class HookFileInstaller : AgentInstaller
{
    /// <summary>事件、matcher、背景執行、curl 最多等幾秒、hook 的 timeout（秒）。</summary>
    public sealed record Hook(string Event, string? Matcher, bool Async, int Wait, int Timeout);

    protected abstract IReadOnlyList<Hook> Hooks { get; }
    /// <summary>~ 底下的設定資料夾（.codex）。</summary>
    protected abstract string Folder { get; }
    protected abstract string FileName { get; }
    /// <summary>hook 物件的 timeout 欄位用毫秒（Gemini CLI）還是秒（Codex）。</summary>
    protected virtual bool Milliseconds => false;
    /// <summary>設定檔是 Shima 建的、拿掉 hook 之後變成空的就整個刪掉（hooks.json 這種專門放 hook 的檔才這樣做）。</summary>
    protected virtual bool OwnFile => false;
    /// <summary>每個 hook 物件額外的欄位（Gemini 的 name）。</summary>
    protected virtual void Decorate(JsonObject hook) { }

    /// <summary>
    /// 資料夾裡要有其中一個檔案才算這個工具有在用；空的表示資料夾在就算。
    /// ~/.gemini 不只 Gemini CLI 在用（Antigravity 也把東西放在那裡），光看資料夾會替沒裝的工具多顯示一列。
    /// </summary>
    protected virtual IReadOnlyList<string> Markers => [];

    private bool Present(string folder) =>
        Directory.Exists(folder) && (Markers.Count == 0 || Markers.Any(m => File.Exists(Path.Combine(folder, m))));

    public IReadOnlyList<string> Events => Hooks.Select(h => h.Event).Distinct().ToList();

    public override List<Target> Targets(AgentHooks.Home home)
    {
        var list = new List<Target>();
        if (Present(Path.Combine(home.Windows, Folder))) list.Add(new(Path.Combine(home.Windows, Folder, FileName), "curl.exe"));
        if (home.Wsl is { } wsl && Present(Path.Combine(wsl, Folder))) list.Add(new(Path.Combine(wsl, Folder, FileName), AgentHooks.WslCurl));
        return list;
    }

    public override (bool Installed, bool Outdated) State(IEnumerable<Target> targets)
    {
        var settings = targets.Select(t => AgentHooks.Read(t.SettingsPath)).ToList();
        return (settings.Any(AgentHooks.Has), settings.Any(s => AgentHooks.Has(s) && !Complete(s)));
    }

    public bool Complete(JsonObject? settings)
    {
        if (settings?["hooks"] is not JsonObject hooks) return false;
        foreach (var name in Events)
            if (((hooks[name] as JsonArray)?.Count(AgentHooks.Ours) ?? 0) != Hooks.Count(h => h.Event == name)) return false;
        return true;
    }

    /// <summary>這個 hook 該打的網址；Shima 只回答 permission，其餘都是 notify。</summary>
    private string Url(Hook hook) =>
        $"http://localhost:{AgentBridge.Port}{AgentKinds.Prefix(Agent)}{(hook.Event == "PermissionRequest" ? "permission" : "notify")}";

    public JsonObject With(JsonObject settings, string curl, string token)
    {
        settings = AgentHooks.Without(settings);
        var hooks = settings["hooks"] as JsonObject ?? [];
        settings["hooks"] = hooks;
        foreach (var h in Hooks)
        {
            // @- 加上引號：不知道這個 agent 在 Windows 上用哪個 shell 跑 hook，PowerShell 會把沒加引號的 @ 當成語法
            var command = $"{curl} -s -m {h.Wait} -X POST -H \"{AgentHooks.Mark}: {token}\" -H \"Content-Type: application/json\" --data-binary \"@-\" {Url(h)}";
            var list = hooks[h.Event] as JsonArray ?? [];
            hooks[h.Event] = list;
            var group = new JsonObject();
            if (h.Matcher != null) group["matcher"] = h.Matcher;
            var hook = new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = Milliseconds ? h.Timeout * 1000 : h.Timeout };
            if (h.Async) hook["async"] = true;
            Decorate(hook);
            group["hooks"] = new JsonArray(hook);
            list.Add(group);
        }
        return settings;
    }

    public override string? Install(IEnumerable<Target> targets, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return L.F("無法取得 Shima 的連線金鑰，沒有更動 {0} 的設定", AgentKinds.Name(Agent));
        // 改過的檔案與改之前的內容（null 表示原本沒有這個檔），出事時照這份還原
        var done = new List<(string Path, string? Before)>();
        string? problem = null;
        foreach (var target in targets)
        {
            try
            {
                var path = target.SettingsPath;
                string? before = File.Exists(path) ? File.ReadAllText(path) : null;
                JsonObject? current = before == null ? [] : AgentHooks.Read(path);
                if (current == null) { problem = L.F("{0} 不是有效的 JSON，沒有更動", path); break; }
                var updated = With(current, target.Curl, token);
                var backup = path + ".before-shima.bak";
                if (before != null && !File.Exists(backup)) File.Copy(path, backup);
                done.Add((path, before));
                AgentHooks.Save(path, Text(updated));
                // 讀回來確認：hook 都在、帶的是這把金鑰
                if (AgentHooks.Read(path) is not { } written || !Complete(written) || !written.ToJsonString().Contains(token, StringComparison.Ordinal))
                    throw new IOException(L.T("寫入的設定檔內容不完整，沒有更動"));
            }
            catch (Exception e) { problem = e.Message; Log.Write($"hooks {Agent}: {e.Message}"); break; }
        }
        if (problem != null) Undo(done);
        return problem;
    }

    private static void Undo(List<(string Path, string? Before)> done)
    {
        foreach (var (path, before) in done)
        {
            try
            {
                if (before == null) File.Delete(path);
                else AgentHooks.Save(path, before);
            }
            catch (Exception e) { Log.Write("hooks undo: " + e.Message); }
        }
    }

    public override string? Remove(IEnumerable<Target> targets)
    {
        string? problem = null;
        foreach (var target in targets)
        {
            try
            {
                var path = target.SettingsPath;
                if (!File.Exists(path)) continue;
                if (AgentHooks.Read(path) is not { } settings) { problem = L.F("{0} 不是有效的 JSON，沒有更動", path); continue; }
                if (!AgentHooks.Has(settings)) continue;
                var without = AgentHooks.Without(settings);
                if (OwnFile && without.Count == 0 && !File.Exists(path + ".before-shima.bak")) File.Delete(path);
                else AgentHooks.Save(path, Text(without));
            }
            catch (Exception e) { problem = e.Message; Log.Write($"hooks {Agent}: {e.Message}"); }
        }
        return problem;
    }

    private static string Text(JsonObject o) =>
        o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
}

/// <summary>
/// OpenAI Codex CLI：~/.codex/hooks.json（https://developers.openai.com/codex/hooks）。
/// PermissionRequest 的回答格式跟 Claude Code 一樣，所以可以在島上允許／拒絕。沒有 Notification、StopFailure 這兩個事件。
/// 注意：Codex 要使用者先在 /hooks 檢視並信任新的 hook 才會執行，Shima 不替人信任、也不用略過信任的旗標。
/// </summary>
internal sealed class CodexInstaller : HookFileInstaller
{
    public override AgentKind Agent => AgentKind.Codex;
    protected override string Folder => ".codex";
    protected override string FileName => "hooks.json";
    protected override bool OwnFile => true;

    // SessionEnd 永遠同步執行、timeout 最多 3 秒；PostToolUse 不加 matcher（Claude Code 那個排除待辦工具的正規表示式用到 lookahead，Codex 不一定支援）
    protected override IReadOnlyList<Hook> Hooks { get; } =
    [
        new("PermissionRequest", null, false, 590, 600),
        new("SessionStart", null, true, 3, 10),
        new("UserPromptSubmit", null, true, 3, 10),
        new("PostToolUse", null, true, 3, 10),
        new("Stop", null, false, 3, 10),
        new("SessionEnd", null, false, 1, 2),
    ];
}

/// <summary>
/// Gemini CLI：~/.gemini/settings.json 的 hooks（https://geminicli.com/docs/hooks/reference/）。
/// 文件沒說 BeforeTool 回 allow 能不能略過確認，Notification 也只能看、不能替人允許，所以只送狀態：
/// BeforeAgent 工作中、AfterAgent 做完了、AfterTool 回到工作中、Notification（ToolPermission）等你允許。timeout 是毫秒。
/// </summary>
internal sealed class GeminiInstaller : HookFileInstaller
{
    public override AgentKind Agent => AgentKind.Gemini;
    protected override string Folder => ".gemini";
    protected override string FileName => "settings.json";
    // installation_id、google_accounts.json 不能拿來認：Antigravity 也會在 ~/.gemini 建立這兩個
    protected override IReadOnlyList<string> Markers => ["settings.json", "oauth_creds.json"];
    protected override bool Milliseconds => true;
    /// <summary>名字讓使用者在 /hooks 認得出來，也能用 hooksConfig.disabled 單獨關掉。</summary>
    protected override void Decorate(JsonObject hook) => hook["name"] = "shima";

    // Gemini 沒有背景執行的 hook：curl 等很短，Shima 沒開時連線會馬上失敗
    protected override IReadOnlyList<Hook> Hooks { get; } =
    [
        new("SessionStart", null, false, 2, 5),
        new("BeforeAgent", null, false, 2, 5),
        new("AfterAgent", null, false, 3, 5),
        new("AfterTool", null, false, 2, 5),
        new("Notification", null, false, 2, 5),
        new("SessionEnd", null, false, 1, 2),
    ];
}

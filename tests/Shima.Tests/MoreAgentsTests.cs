// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;
using Door = Shima.Agents.AgentBridge.Door;

namespace Shima.Tests;

/// <summary>Codex 與 Gemini CLI 的金鑰各管各的門。</summary>
public class MoreAgentsGateTests
{
    private const string Claude = "claude-token-0123456789abcdef0123456789abcdef", Codex = "codex-token-0123456789abcdef0123456789abcdef",
        Gemini = "gemini-token-0123456789abcdef0123456789abcdef", Deck = "deck-token-0123456789abcdef0123456789abcdef";

    private static string TokenOf(AgentKind agent) => agent switch { AgentKind.Codex => Codex, AgentKind.Gemini => Gemini, _ => Claude };

    private static AgentBridge.Admission Admit(string method, string path, string? header, bool origin = false, string? query = null) =>
        AgentBridge.Admit(method, path, header, query, origin, TokenOf, Deck);

    [Fact]
    public void 每個_agent_只進得了自己的網址()
    {
        Assert.Equal(new AgentBridge.Admission(Door.Hook, AgentKind.Codex, true), Admit("POST", "/codex/permission", Codex));
        Assert.Equal(new AgentBridge.Admission(Door.Hook, AgentKind.Codex, false), Admit("POST", "/codex/notify", Codex));
        Assert.Equal(new AgentBridge.Admission(Door.Hook, AgentKind.Gemini, false), Admit("POST", "/gemini/notify", Gemini));
        Assert.Equal(new AgentBridge.Admission(Door.Hook, AgentKind.Claude, true), Admit("POST", "/permission", Claude));
        Assert.Equal(new AgentBridge.Admission(Door.Hook, AgentKind.Claude, false), Admit("POST", "/notify", Claude));

        // 拿別人的金鑰都不行
        foreach (var (path, wrong) in new[] { ("/codex/permission", Claude), ("/codex/permission", Gemini), ("/gemini/notify", Codex),
                     ("/gemini/notify", Claude), ("/permission", Codex), ("/notify", Gemini), ("/", Codex) })
            Assert.Equal(Door.Refuse, Admit("POST", path, wrong).Door);
        Assert.Equal(Door.Refuse, Admit("GET", "/deck/now", null, query: Codex).Door);
        Assert.Equal(Door.Refuse, Admit("POST", "/deck/press", Gemini, query: Gemini).Door);
    }

    [Fact]
    public void Gemini_沒有權限請求那扇門_也沒有別的網址()
    {
        Assert.Equal(Door.Refuse, Admit("POST", "/gemini/permission", Gemini).Door);
        Assert.Equal(Door.Refuse, Admit("POST", "/gemini/other", Gemini).Door);
        Assert.Equal(Door.Refuse, Admit("POST", "/codex/", Codex).Door);
    }

    [Fact]
    public void 一樣只收_POST_不能是網頁送的()
    {
        Assert.Equal(Door.Refuse, Admit("GET", "/codex/permission", Codex).Door);
        Assert.Equal(Door.Refuse, Admit("POST", "/codex/permission", Codex, origin: true).Door);
        Assert.Equal(Door.Refuse, Admit("GET", "/gemini/notify", Gemini).Door);
        Assert.Equal(Door.Refuse, Admit("POST", "/gemini/notify", Gemini, origin: true).Door);
        Assert.Equal(Door.Refuse, AgentBridge.Admit("POST", "/codex/permission", "", null, false, _ => "", Deck).Door);
    }

    [Fact]
    public void 原本的_Gate_碰不到新的網址()
    {
        Assert.Equal(Door.Refuse, AgentBridge.Gate("POST", "/codex/permission", Claude, null, false, Claude, Deck));
        Assert.Equal(Door.Refuse, AgentBridge.Gate("POST", "/gemini/notify", Claude, null, false, Claude, Deck));
        Assert.Equal(Door.Hook, AgentBridge.Gate("POST", "/permission", Claude, null, false, Claude, Deck));
    }
}

public class MoreAgentsBridgeTests
{
    private static string TempRules() => Path.Combine(Path.GetTempPath(), "shima-rules-" + Guid.NewGuid().ToString("N") + ".json");

    private static JsonObject Codex(string ev, string cwd = "/home/someone/alpha") => new()
    {
        ["session_id"] = "s1", ["hook_event_name"] = ev, ["cwd"] = cwd, ["turn_id"] = "t1", ["model"] = "gpt",
        ["tool_name"] = "Bash", ["tool_input"] = new JsonObject { ["command"] = "git status" },
    };

    [Fact]
    public async Task Codex_的請求不比對也不記_以後都允許_的規則()
    {
        var path = TempRules();
        try
        {
            var rules = new AgentRules(path);
            rules.Add(new AgentRule("Bash", "git status", "/home/someone/alpha"));
            var bridge = new AgentBridge(rules);
            var asked = new List<PermissionRequest>();
            bridge.Asked += ask => { asked.Add(ask); ask.Decision.TrySetResult(PermissionRequest.Always); };

            // Claude Code 的同一個請求會被規則直接允許；Codex 的照樣要問
            Assert.Equal("allow", await bridge.PermissionAsync(Codex("PermissionRequest"), AgentKind.Codex));
            var ask = Assert.Single(asked);
            Assert.Equal(AgentKind.Codex, ask.Agent);
            Assert.Null(ask.Remember);
            Assert.Null(ask.RememberText);
            // 就算有人送了「always」也不會多記一條
            Assert.Single(rules.List());
            Assert.Equal("allow", await bridge.PermissionAsync(Codex("PermissionRequest")));   // Claude Code 的：規則照常
            Assert.Single(asked);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Codex_允許拒絕回終端機與工作階段()
    {
        var bridge = new AgentBridge(new AgentRules(TempRules()));
        string? next = "deny";
        bridge.Asked += ask => ask.Decision.TrySetResult(next);
        Assert.Equal("deny", await bridge.PermissionAsync(Codex("PermissionRequest"), AgentKind.Codex));
        next = null;
        Assert.Null(await bridge.PermissionAsync(Codex("PermissionRequest"), AgentKind.Codex));
        var session = Assert.Single(bridge.Sessions);
        Assert.Equal(AgentKind.Codex, session.Agent);
        Assert.Equal("codex:s1", session.Id);
        Assert.Equal(AgentState.NeedsPermission, session.State);
    }

    [Fact]
    public void 同一個_session_id_不同_agent_是不同的工作階段_完成通知帶著是誰()
    {
        var bridge = new AgentBridge(new AgentRules(TempRules()));
        var claudeDone = new List<string>();
        var done = new List<(AgentKind, string, string)>();
        bridge.Finished += (p, _) => claudeDone.Add(p);
        bridge.AgentFinished += (a, p, m) => done.Add((a, p, m));

        bridge.Notify(new JsonObject { ["session_id"] = "x", ["hook_event_name"] = "UserPromptSubmit", ["cwd"] = "/p/claude" });
        bridge.Notify(new JsonObject { ["session_id"] = "x", ["hook_event_name"] = "UserPromptSubmit", ["cwd"] = "/p/codex" }, AgentKind.Codex);
        bridge.Notify(new JsonObject { ["session_id"] = "x", ["hook_event_name"] = "BeforeAgent", ["cwd"] = "/p/gemini", ["prompt"] = "hi" }, AgentKind.Gemini);
        Assert.Equal(3, bridge.Sessions.Length);
        Assert.All(bridge.Sessions, s => Assert.Equal(AgentState.Working, s.State));

        bridge.Notify(new JsonObject { ["session_id"] = "x", ["hook_event_name"] = "Stop", ["cwd"] = "/p/codex", ["last_assistant_message"] = "好了" }, AgentKind.Codex);
        bridge.Notify(new JsonObject { ["session_id"] = "x", ["hook_event_name"] = "AfterAgent", ["cwd"] = "/p/gemini", ["prompt_response"] = "完成" }, AgentKind.Gemini);
        Assert.Empty(claudeDone);
        Assert.Equal([(AgentKind.Codex, "codex", "好了"), (AgentKind.Gemini, "gemini", "完成")], done);
        Assert.Equal(AgentState.Working, bridge.Sessions.Single(s => s.Agent == AgentKind.Claude).State);
    }

    [Fact]
    public void Gemini_的事件換成同一套狀態()
    {
        var bridge = new AgentBridge(new AgentRules(TempRules()));
        JsonObject Hook(string ev) => new() { ["session_id"] = "g", ["hook_event_name"] = ev, ["cwd"] = "/p/app", ["timestamp"] = "2026-11-23T00:00:00Z" };
        AgentState State() => bridge.Sessions.Single().State;

        bridge.Notify(Hook("SessionStart").Also(o => o["source"] = "startup"), AgentKind.Gemini);
        Assert.Equal(AgentState.NeedsInput, State());
        bridge.Notify(Hook("BeforeAgent"), AgentKind.Gemini);
        Assert.Equal(AgentState.Working, State());
        bridge.Notify(Hook("Notification").Also(o => o["notification_type"] = "ToolPermission"), AgentKind.Gemini);
        Assert.Equal(AgentState.NeedsPermission, State());
        bridge.Notify(Hook("AfterTool").Also(o => { o["tool_name"] = "write_todos"; o["tool_input"] = new JsonObject(); }), AgentKind.Gemini);
        Assert.Equal(AgentState.Working, State());
        bridge.Notify(Hook("BeforeModel"), AgentKind.Gemini);   // 不管的事件
        Assert.Equal(AgentState.Working, State());
        bridge.Notify(Hook("AfterAgent"), AgentKind.Gemini);
        Assert.Equal(AgentState.Done, State());
        Assert.Equal(AgentKind.Gemini, bridge.Sessions.Single().Agent);
        bridge.Notify(Hook("SessionEnd").Also(o => o["reason"] = "exit"), AgentKind.Gemini);
        Assert.Empty(bridge.Sessions);
    }

    [Fact]
    public void 換事件名稱時不改到原本的物件()
    {
        var hook = new JsonObject { ["session_id"] = "g", ["hook_event_name"] = "AfterAgent", ["prompt_response"] = "ok" };
        var normalized = AgentKinds.Normalize(AgentKind.Gemini, hook)!;
        Assert.Equal("Stop", normalized["hook_event_name"]!.ToString());
        Assert.Equal("ok", normalized["last_assistant_message"]!.ToString());
        Assert.Equal("gemini:g", normalized["session_id"]!.ToString());
        Assert.Equal("AfterAgent", hook["hook_event_name"]!.ToString());
        Assert.Same(hook, AgentKinds.Normalize(AgentKind.Claude, hook));
        Assert.Null(AgentKinds.Normalize(AgentKind.Codex, new JsonObject { ["hook_event_name"] = "PreCompact" }));
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject o, Action<JsonObject> change) { change(o); return o; }
}

/// <summary>Codex 與 Gemini CLI 的安裝器：只碰暫存資料夾，絕不碰使用者的 ~/.codex、~/.gemini。</summary>
public sealed class MoreAgentsInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shima-agents-" + Guid.NewGuid().ToString("N"));
    private string Windows => Path.Combine(_root, "win");
    private string Wsl => Path.Combine(_root, "wsl");
    private AgentHooks.Home Home => new(Windows, Wsl);

    public MoreAgentsInstallTests()
    {
        Directory.CreateDirectory(Windows);
        Directory.CreateDirectory(Wsl);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static JsonObject Load(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    private static IEnumerable<string> Commands(JsonObject settings) =>
        ((JsonObject)settings["hooks"]!).SelectMany(e => ((JsonArray)e.Value!).SelectMany(g => (JsonArray)g!["hooks"]!).Select(h => h!["command"]!.ToString()));

    [Fact]
    public void 沒有設定資料夾就什麼都不列()
    {
        Assert.Empty(AgentInstaller.Codex.Targets(Home));
        Assert.Empty(AgentInstaller.Gemini.Targets(Home));
        Assert.Empty(AgentInstaller.Gemini.Targets(new AgentHooks.Home(Windows, null)));
        var status = AgentInstaller.Scan(Home);
        Assert.False(status.Single(s => s.Agent == AgentKind.Codex).Detected);
        Assert.False(status.Single(s => s.Agent == AgentKind.Gemini).Detected);

        Directory.CreateDirectory(Path.Combine(Wsl, ".codex"));
        var target = Assert.Single(AgentInstaller.Codex.Targets(Home));
        Assert.Equal(Path.Combine(Wsl, ".codex", "hooks.json"), target.SettingsPath);
        Assert.Equal(AgentHooks.WslCurl, target.Curl);
        Assert.True(AgentInstaller.Scan(Home).Single(s => s.Agent == AgentKind.Codex).Detected);
    }

    [Fact]
    public void Codex_裝進_hooks_json_權限請求打自己的網址_中斷後刪掉自己建的檔()
    {
        Directory.CreateDirectory(Path.Combine(Windows, ".codex"));
        var targets = AgentInstaller.Codex.Targets(Home);
        Assert.Null(AgentInstaller.Codex.Install(targets, "CODEXTOKEN"));
        var file = Path.Combine(Windows, ".codex", "hooks.json");
        var s = Load(file);
        var installer = (HookFileInstaller)AgentInstaller.Codex;
        Assert.True(installer.Complete(s));
        Assert.Equal((true, false), installer.State(targets));
        Assert.Equal(["PermissionRequest", "SessionStart", "UserPromptSubmit", "PostToolUse", "Stop", "SessionEnd"], ((JsonObject)s["hooks"]!).Select(e => e.Key));

        var permission = s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!;
        Assert.EndsWith($"http://localhost:{AgentBridge.Port}/codex/permission", permission["command"]!.ToString());
        Assert.Equal(600, permission["timeout"]!.GetValue<int>());
        Assert.Null(permission["async"]);
        Assert.True(s["hooks"]!["PostToolUse"]![0]!["hooks"]![0]!["async"]!.GetValue<bool>());
        Assert.Null(s["hooks"]!["PostToolUse"]![0]!["matcher"]);
        Assert.True(s["hooks"]!["SessionEnd"]![0]!["hooks"]![0]!["timeout"]!.GetValue<int>() <= 3);
        Assert.All(Commands(s), c => Assert.Contains("X-Shima-Token: CODEXTOKEN", c));
        Assert.All(Commands(s), c => Assert.Contains("--data-binary \"@-\"", c));
        Assert.All(Commands(s).Where(c => !c.EndsWith("/codex/permission")), c => Assert.EndsWith("/codex/notify", c));
        Assert.False(File.Exists(file + ".before-shima.bak"));   // 原本沒有檔案，就沒有備份

        Assert.Null(AgentInstaller.Codex.Remove(targets));
        Assert.False(File.Exists(file));
        Assert.Equal((false, false), installer.State(targets));
    }

    [Fact]
    public void Codex_原本的_hook_不動_重複連接不會越加越多_中斷後回到原樣()
    {
        Directory.CreateDirectory(Path.Combine(Windows, ".codex"));
        var file = Path.Combine(Windows, ".codex", "hooks.json");
        const string original = """{ "description": "mine", "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "guard.sh" } ] } ] } }""";
        File.WriteAllText(file, original);
        var targets = AgentInstaller.Codex.Targets(Home);

        Assert.Null(AgentInstaller.Codex.Install(targets, "A"));
        Assert.Null(AgentInstaller.Codex.Install(targets, "B"));
        var s = Load(file);
        Assert.Single((JsonArray)s["hooks"]!["PermissionRequest"]!);
        Assert.Contains("X-Shima-Token: B", s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["command"]!.ToString());
        Assert.Equal("guard.sh", s["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]!.ToString());
        Assert.Equal(original, File.ReadAllText(file + ".before-shima.bak"));

        Assert.Null(AgentInstaller.Codex.Remove(targets));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), Load(file)));   // 別人的東西還在，檔案也還在
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp"));
    }

    [Fact]
    public void Gemini_的資料夾裡只有別的工具的東西就不算有裝()
    {
        // Antigravity 也用 ~/.gemini：只有它的子資料夾時，不該出現 Gemini CLI 那一列
        Directory.CreateDirectory(Path.Combine(Wsl, ".gemini", "antigravity"));
        // 這兩個 Antigravity 也會建立，不能當成有裝 Gemini CLI
        File.WriteAllText(Path.Combine(Wsl, ".gemini", "installation_id"), "x");
        File.WriteAllText(Path.Combine(Wsl, ".gemini", "google_accounts.json"), "{}");
        Assert.Empty(AgentInstaller.Gemini.Targets(Home));
        Assert.False(AgentInstaller.Scan(Home).Single(s => s.Agent == AgentKind.Gemini).Detected);
        File.WriteAllText(Path.Combine(Wsl, ".gemini", "oauth_creds.json"), "{}");
        Assert.Single(AgentInstaller.Gemini.Targets(Home));
    }

    [Fact]
    public void Gemini_的_timeout_是毫秒_沒有權限請求_帶名字()
    {
        Directory.CreateDirectory(Path.Combine(Wsl, ".gemini"));
        var file = Path.Combine(Wsl, ".gemini", "settings.json");
        File.WriteAllText(file, """{ "theme": "Dracula", "hooksConfig": { "enabled": true } }""");
        var targets = AgentInstaller.Gemini.Targets(Home);
        Assert.Null(AgentInstaller.Gemini.Install(targets, "GEMINITOKEN"));
        var s = Load(file);
        Assert.Equal("Dracula", s["theme"]!.ToString());
        var hooks = (JsonObject)s["hooks"]!;
        Assert.False(hooks.ContainsKey("PermissionRequest"));
        Assert.False(hooks.ContainsKey("BeforeTool"));
        Assert.Equal(["SessionStart", "BeforeAgent", "AfterAgent", "AfterTool", "Notification", "SessionEnd"], hooks.Select(e => e.Key));
        foreach (var (_, groups) in hooks)
        {
            var hook = groups![0]!["hooks"]![0]!;
            Assert.True(hook["timeout"]!.GetValue<int>() >= 1000);
            Assert.Equal("shima", hook["name"]!.ToString());
            Assert.Null(hook["async"]);
            Assert.StartsWith(AgentHooks.WslCurl + " ", hook["command"]!.ToString());
            Assert.EndsWith($"http://localhost:{AgentBridge.Port}/gemini/notify", hook["command"]!.ToString());
        }

        Assert.Null(AgentInstaller.Gemini.Remove(targets));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "theme": "Dracula", "hooksConfig": { "enabled": true } }"""), Load(file)));
    }

    [Fact]
    public void 其中一份設定壞掉_已經改好的也還原_不留半套()
    {
        Directory.CreateDirectory(Path.Combine(Windows, ".codex"));
        Directory.CreateDirectory(Path.Combine(Wsl, ".codex"));
        var good = Path.Combine(Windows, ".codex", "hooks.json");
        var bad = Path.Combine(Wsl, ".codex", "hooks.json");
        File.WriteAllText(good, """{ "hooks": {} }""");
        File.WriteAllText(bad, "{ 壞掉");
        Assert.NotNull(AgentInstaller.Codex.Install(AgentInstaller.Codex.Targets(Home), "TOKEN"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "hooks": {} }"""), Load(good)));
        Assert.Equal("{ 壞掉", File.ReadAllText(bad));

        // 原本沒有檔案的那一份：還原就是刪掉
        File.Delete(good);
        Assert.NotNull(AgentInstaller.Codex.Install(AgentInstaller.Codex.Targets(Home), "TOKEN"));
        Assert.False(File.Exists(good));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void 拿不到金鑰就什麼都不寫(string token)
    {
        Directory.CreateDirectory(Path.Combine(Windows, ".gemini"));
        File.WriteAllText(Path.Combine(Windows, ".gemini", "oauth_creds.json"), "{}");
        Assert.NotNull(AgentInstaller.Gemini.Install(AgentInstaller.Gemini.Targets(Home), token));
        Assert.False(File.Exists(Path.Combine(Windows, ".gemini", "settings.json")));
    }

    [Fact]
    public void Claude_Code_的安裝器就是原本的_AgentHooks()
    {
        var claude = AgentInstaller.Claude.Targets(Home);
        Assert.Equal(Path.Combine(Windows, ".claude", "settings.json"), Assert.Single(claude).SettingsPath);   // Windows 那份一定列，WSL 沒有 .claude 不列
        Assert.Null(AgentInstaller.Claude.Install(claude, "TOKEN"));
        var s = Load(claude[0].SettingsPath);
        Assert.True(AgentHooks.Complete(s));
        Assert.True(JsonNode.DeepEquals(AgentHooks.With([], "curl.exe", "TOKEN"), s));
        Assert.Null(AgentInstaller.Claude.Remove(claude));
        Assert.False(AgentHooks.Has(Load(claude[0].SettingsPath)));
    }
}

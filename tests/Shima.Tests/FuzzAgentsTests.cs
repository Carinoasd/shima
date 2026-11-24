// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>別的程式（Claude Code、Codex、Gemini 的 hook）送來的資料：型別不對、欄位缺漏、亂序的事件。</summary>
public class FuzzAgentsTests
{
    /// <summary>隨機的 JSON 值：什麼型別都有，深度有限。</summary>
    private static JsonNode? AnyJson(Random r, int depth = 0)
    {
        switch (r.Next(depth > 2 ? 6 : 9))
        {
            case 0: return null;
            case 1: return JsonValue.Create(r.Next(-2, 100));
            case 2: return JsonValue.Create(r.Chance(0.5));
            case 3: return JsonValue.Create(r.NextDouble() * 1e6);
            case 4: return JsonValue.Create(r.Pick(["", "a", "x y", "/tmp/a", "C:\\x", "中文", "😀", "completed", "pending", "in_progress", "deleted", "1", "-1", "call_1", "\uD83D"]));
            case 5: return JsonValue.Create(r.Pick(["Stop", "SessionStart", "ok", "x"]));
            case 6: { var a = new JsonArray(); for (int n = r.Next(0, 4); n > 0; n--) a.Add(AnyJson(r, depth + 1)); return a; }
            default:
                var o = new JsonObject();
                for (int n = r.Next(0, 4); n > 0; n--) o[r.Pick(Keys)] = AnyJson(r, depth + 1);
                return o;
        }
    }

    private static readonly string[] Keys =
    [
        "session_id", "cwd", "transcript_path", "hook_event_name", "tool_name", "tool_input", "tool_response", "tool_use_id", "source", "notification_type", "message", "prompt", "prompt_response", "last_assistant_message",
        "todos", "content", "status", "activeForm", "active_form", "subject", "title", "description", "id", "taskId", "task_id", "task", "command", "file_path",
    ];

    private static readonly string[] Events = ["SessionStart", "SessionEnd", "UserPromptSubmit", "PermissionRequest", "PostToolUse", "PostToolUseFailure", "Notification", "Stop", "StopFailure", "BeforeAgent", "AfterAgent", "AfterTool", "Other", ""];
    private static readonly string[] Tools = ["TodoWrite", "TaskCreate", "TaskUpdate", "AskUserQuestion", "ExitPlanMode", "Bash", "Read", ""];

    private static JsonObject RandomHook(Random r)
    {
        var o = new JsonObject
        {
            ["hook_event_name"] = r.Chance(0.9) ? r.Pick(Events) : AnyJson(r),
            ["session_id"] = r.Chance(0.85) ? r.Pick(["s1", "s2", "s3", "", "😀"]) : AnyJson(r),
            ["cwd"] = r.Chance(0.8) ? r.Pick(["/home/u/proj", "C:\\Users\\u\\proj", "", "/", "C:\\", "\\\\srv\\share\\", "a/b/", "\0"]) : AnyJson(r),
        };
        if (r.Chance(0.6)) o["tool_name"] = r.Chance(0.9) ? r.Pick(Tools) : AnyJson(r);
        if (r.Chance(0.6)) o["tool_use_id"] = r.Chance(0.8) ? r.Pick(["u1", "u2", "u3", ""]) : AnyJson(r);
        if (r.Chance(0.5)) o["source"] = r.Chance(0.7) ? r.Pick(["clear", "compact", "startup", "resume"]) : AnyJson(r);
        if (r.Chance(0.5)) o["notification_type"] = r.Chance(0.7) ? r.Pick(["permission_prompt", "idle_prompt", "elicitation_dialog", "ToolPermission", "agent_needs_input"]) : AnyJson(r);
        if (r.Chance(0.7))
        {
            var input = new JsonObject();
            if (r.Chance(0.7)) input["todos"] = r.Chance(0.6) ? new JsonArray(Enumerable.Range(0, r.Next(0, 4)).Select(_ => (JsonNode?)(r.Chance(0.7) ? new JsonObject { ["content"] = r.Pick(["a", "b", ""]), ["status"] = AnyJson(r), ["activeForm"] = AnyJson(r) } : AnyJson(r))).ToArray()) : AnyJson(r);
            foreach (var key in new[] { "subject", "activeForm", "taskId", "id", "status", "content", "title", "description" }) if (r.Chance(0.3)) input[key] = r.Chance(0.6) ? r.Pick(["1", "2", "a", "completed", "deleted", "pending", "in_progress", ""]) : AnyJson(r);
            o["tool_input"] = r.Chance(0.9) ? input : AnyJson(r);
        }
        if (r.Chance(0.4)) o["tool_response"] = AnyJson(r);
        if (r.Chance(0.2)) o["prompt_response"] = AnyJson(r);
        if (r.Chance(0.1)) o[r.Pick(Keys)] = AnyJson(r);
        return o;
    }

    [Fact]
    public void AgentKinds_Normalize_NeverThrows_OnAnyHook()
    {
        Fuzz.Run("AgentKinds.Normalize", 6001, 4000, r => (RandomHook(r).ToJsonString(), r.Pick(Enum.GetValues<AgentKind>())), v =>
        {
            var (json, agent) = v;
            var hook = JsonNode.Parse(json)!.AsObject();
            var normalized = AgentKinds.Normalize(agent, hook);
            if (agent == AgentKind.Claude) Assert.Same(hook, normalized);
            Assert.Equal(json, hook.ToJsonString());   // 不能改到原本那一份
            if (normalized != null && agent != AgentKind.Claude && hook["session_id"] is JsonValue { } id && id.TryGetValue(out string? text) && text.Length > 0)
                Assert.Equal($"{agent.ToString().ToLowerInvariant()}:{text}", normalized["session_id"]!.ToString());
            AgentKinds.Name(agent);
        }, v => $"{v.Item2} {v.Item1}");
    }

    [Fact]
    public void AgentSessions_And_AgentPlan_SurviveAnyEventSequence()
    {
        Fuzz.Run("AgentSessions.Apply／AgentPlan.Apply", 6002, 1000, r => string.Join("\n", Enumerable.Range(0, r.Next(1, 25)).Select(_ => RandomHook(r).ToJsonString())), text =>
        {
            var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            var sessions = new AgentSessions(() => now);
            foreach (var line in text.Split('\n'))
            {
                var hook = JsonNode.Parse(line)!.AsObject();
                var before = sessions.Snapshot();
                sessions.Apply(hook, out _, AgentKind.Claude);
                if (line.Length % 5 == 0) now = now.AddMinutes(20);
                if (line.Length % 7 == 0) sessions.Prune();
                if (line.Length % 11 == 0) sessions.Resumed(AgentSessions.Key(hook));
                var snapshot = sessions.Snapshot();
                Assert.Equal(sessions.Count, snapshot.Length);
                Assert.Equal(snapshot.Length, snapshot.Select(s => s.Id).Distinct().Count());
                foreach (var s in snapshot)
                {
                    Assert.InRange(s.Done, 0, s.Total);
                    Assert.True(Enum.IsDefined(s.State));
                    Assert.NotNull(s.Project);
                    Assert.NotNull(s.Current);
                }
                Assert.Equal(snapshot.OrderByDescending(s => s.LastActive).Select(s => s.LastActive), snapshot.Select(s => s.LastActive));
            }
        }, Fuzz.Escape);
    }

    [Fact]
    public void AgentPlan_Apply_KeepsTheListConsistent()
    {
        Fuzz.Run("AgentPlan.Apply", 6003, 4000, r =>
        {
            var hooks = Enumerable.Range(0, r.Next(1, 10)).Select(_ => RandomHook(r)).ToList();
            foreach (var h in hooks) { h["hook_event_name"] = "PostToolUse"; if (r.Chance(0.9)) h["tool_name"] = r.Pick(["TodoWrite", "TaskCreate", "TaskUpdate"]); }
            return string.Join("\n", hooks.Select(h => h.ToJsonString()));
        }, text =>
        {
            var plan = new AgentPlan();
            foreach (var line in text.Split('\n'))
            {
                plan.Apply(JsonNode.Parse(line)!.AsObject());
                Assert.InRange(plan.Done, 0, plan.Steps.Count);
                Assert.NotNull(plan.Current);
                Assert.All(plan.Steps, s => { Assert.NotNull(s.Id); Assert.NotNull(s.Text); Assert.NotNull(s.Status); });
                Assert.Equal(plan.Steps.Count, plan.Steps.Select(s => s.Id).Distinct().Count());
                Assert.All(plan.Steps, s => Assert.NotEqual("", s.Id));
            }
        }, Fuzz.Escape);
    }

    // ---------------------------------------------------------------- Admit／Gate

    [Fact]
    public void Admit_OnlyTheRightKeyMethodAndNoOrigin_GetsThrough()
    {
        string[] methods = ["POST", "GET", "post", "Post", "PUT", "DELETE", "OPTIONS", "HEAD", "", "POST ", "PATCH"];
        string[] paths = ["/permission", "/notify", "/", "", "/codex/permission", "/codex/notify", "/gemini/permission", "/gemini/notify", "/codex/", "/gemini", "/codex/notify/", "//codex/notify", "/CODEX/notify", "/deck/state", "/deck/press", "/deck/turn", "/deck/", "/deck", "/DECK/press", "//deck/press", "/deck/press/", "/deck/../notify", "/a/b", "/permission/", "/notify?x=1", "/permission\0", "/codex/permission/x"];
        string[] tokens = ["", "a", "tok-hook", "tok-deck", "tok-codex", "tok-gemini", "TOK-HOOK", " tok-hook", "tok-hook ", "tok-hoo", "tok-hook1", "中文"];
        Fuzz.Run("AgentBridge.Admit／Gate", 6004, 20000, r => (r.Pick(methods), r.Pick(paths), r.Chance(0.1) ? null : r.Pick(tokens), r.Chance(0.1) ? null : r.Pick(tokens), r.Chance(0.2), r.Pick(["tok-hook", "", "x"]), r.Pick(["tok-deck", "", "x"]), r.Pick(["tok-codex", "", "x"]), r.Pick(["tok-gemini", "", "x"])), v =>
        {
            var (method, path, header, query, origin, hook, deck, codex, gemini) = v;
            string Token(AgentKind a) => a switch { AgentKind.Claude => hook, AgentKind.Codex => codex, _ => gemini };
            var admission = AgentBridge.Admit(method, path, header, query, origin, Token, deck);
            var gate = AgentBridge.Gate(method, path, header, query, origin, hook, deck);
            switch (admission.Door)
            {
                case AgentBridge.Door.Hook:
                    Assert.Equal("POST", method);
                    Assert.False(origin, "瀏覽器的請求（有 Origin）不能走 hook 那扇門");
                    Assert.False(path.StartsWith("/deck/", StringComparison.Ordinal));
                    Assert.NotEqual("", Token(admission.Agent));
                    Assert.Equal(Token(admission.Agent), header);   // 只認那個 agent 自己的金鑰
                    if (admission.Permission) Assert.True(path is "/permission" or "/codex/permission" or "/gemini/permission" && admission.Agent != AgentKind.Gemini);
                    if (path.StartsWith("/codex/", StringComparison.Ordinal)) Assert.Equal(AgentKind.Codex, admission.Agent);
                    if (path.StartsWith("/gemini/", StringComparison.Ordinal)) Assert.Equal(AgentKind.Gemini, admission.Agent);
                    break;
                case AgentBridge.Door.Deck:
                    Assert.StartsWith("/deck/", path);
                    Assert.NotEqual("", deck);
                    Assert.Equal(deck, query);
                    Assert.Equal(path is "/deck/press" or "/deck/turn" ? "POST" : "GET", method);   // 會改變狀態的只收 POST
                    break;
                default:
                    Assert.False(admission.Permission);
                    break;
            }
            // Gate 只有 Claude Code 的金鑰：別的 agent 的網址一律進不去
            if (path.StartsWith("/codex/", StringComparison.Ordinal) || path.StartsWith("/gemini/", StringComparison.Ordinal)) Assert.Equal(AgentBridge.Door.Refuse, gate);
            // 標頭金鑰、網址金鑰各管各的：deck 的金鑰放在標頭沒有用，hook 的金鑰放在網址也沒有用
            if (hook.Length > 0 && header != hook && query == hook && !path.StartsWith("/deck/", StringComparison.Ordinal)) Assert.Equal(AgentBridge.Door.Refuse, gate);
            // 正面：全部都對的時候一定放行
            if (method == "POST" && !origin && header == hook && hook.Length > 0 && path is "/permission" or "/notify" && !path.StartsWith("/deck/")) Assert.Equal(AgentBridge.Door.Hook, gate);
        }, v => $"method={v.Item1} path={v.Item2} header={v.Item3} query={v.Item4} origin={v.Item5} tokens(hook={v.Item6},deck={v.Item7},codex={v.Item8},gemini={v.Item9})");
    }

    [Fact]
    public void AgentBridge_PermissionAsync_And_Notify_SurviveAnyHook_AndOnlyAutoAllowWhatTheRuleCovers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shima-fuzz-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var rules = new AgentRules(Path.Combine(folder, "rules.json"));
            Assert.True(rules.Add(new AgentRule("Bash", "git status", "/home/u/proj")));
            Assert.True(rules.Add(new AgentRule("Read", null, "/home/u/proj")));
            var bridge = new AgentBridge(rules, () => new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc));
            var auto = new List<(string Tool, string Input, string Cwd)>();
            bridge.AutoAllowed += (ask, rule) => auto.Add((ask.Tool, ask.ToolInput, ask.Cwd));
            bridge.Asked += ask => ask.Decision.TrySetResult("deny");
            Fuzz.Run("AgentBridge.PermissionAsync／Notify", 6006, 1500, r =>
            {
                var hook = RandomHook(r);
                hook["hook_event_name"] = r.Chance(0.5) ? "PermissionRequest" : hook["hook_event_name"];
                if (r.Chance(0.6))
                {
                    hook["tool_name"] = r.Pick(["Bash", "Read", "Edit", "Foo"]);
                    hook["cwd"] = r.Pick(["/home/u/proj", "/home/u/proj/", "/home/u/proj/sub", "/home/u/other", ""]);
                    hook["tool_input"] = r.Chance(0.5) ? new JsonObject { ["command"] = r.Pick(["git status", "git status -s", "git status; rm -rf x", "git status .env", "git status $(x)", "ls"]) } : new JsonObject { ["file_path"] = r.Pick(["/home/u/proj/a.cs", "/home/u/proj/.git/config", "../x", "a.cs"]) };
                }
                return (hook.ToJsonString(), r.Pick(Enum.GetValues<AgentKind>()));
            }, v =>
            {
                var (json, agent) = v;
                var hook = JsonNode.Parse(json)!.AsObject();
                auto.Clear();
                var behavior = bridge.PermissionAsync(hook, agent).GetAwaiter().GetResult();
                Assert.True(behavior is null or "allow" or "deny");
                bridge.Notify(JsonNode.Parse(json)!.AsObject(), agent);
                _ = bridge.Sessions;
                foreach (var (tool, input, cwd) in auto)
                {
                    Assert.Equal(AgentKind.Claude, agent);   // 別的 agent 不比對規則
                    var node = JsonNode.Parse(input);
                    Assert.True(rules.List().Any(rule => AgentRules.Covers(rule, tool, node, cwd)), $"自動允許了沒有規則涵蓋的請求：{tool} {input} cwd={cwd}");
                    Assert.Equal("allow", behavior);
                    if (tool == "Bash") Assert.DoesNotContain(';', node!["command"]!.ToString());
                }
            }, v => $"{v.Item2} {v.Item1}", perCallMs: 500);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    /// <summary>每次只給幾個位元組的請求內容（分塊傳輸：沒有 Content-Length，一直送也不能撐爆）。</summary>
    private sealed class Trickle(int total, Random random) : Stream
    {
        private int _sent;
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, total - _sent), random.Next(1, 20000));
            for (int i = 0; i < n; i++) buffer[offset + i] = (byte)'a';
            _sent += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => total;
        public override long Position { get => _sent; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void AgentBridge_ReadLimitedAsync_NeverReturnsMoreThanTheLimit()
    {
        Fuzz.Run("AgentBridge.ReadLimitedAsync", 6007, 2000, r => (r.Pick([0, 1, 100, 8191, 8192, 8193, 50_000, 600_000, 2_000_000]), r.Pick([0, 1, 100, 8192, 512 * 1024]), r.Next()), v =>
        {
            var (total, limit, seed) = v;
            var text = AgentBridge.ReadLimitedAsync(new Trickle(total, new Random(seed)), limit).GetAwaiter().GetResult();
            if (total > limit) Assert.Null(text);
            else Assert.Equal(total, text!.Length);
        }, v => $"total={v.Item1} limit={v.Item2}", perCallMs: 300);
    }

    [Fact]
    public void AgentBridge_Answer_And_Describe_AreSafe()
    {
        Fuzz.Run("AgentBridge.Answer／Describe", 6005, 3000, r => (r.Chance(0.2) ? null : r.Pick(["allow", "deny", "always", "ALLOW", "", "x", "allow "]), r.Chance(0.2) ? null : r.Pick(Tools), AnyJson(r)?.ToJsonString()), v =>
        {
            var (behavior, tool, json) = v;
            var answer = AgentBridge.Answer(behavior);
            if (answer != null) Assert.Contains(JsonNode.Parse(answer)!["hookSpecificOutput"]!["decision"]!["behavior"]!.ToString(), new[] { "allow", "deny" });
            var text = AgentBridge.Describe(tool, json == null ? null : JsonNode.Parse(json));
            Assert.True(text.Length <= 401);
        }, v => $"{v.Item1} {v.Item2} {v.Item3}");
    }
}

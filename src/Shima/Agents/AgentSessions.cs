// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.IO;
using System.Text.Json.Nodes;

namespace Shima.Agents;

public enum AgentState
{
    /// <summary>工作中。</summary>
    Working,
    /// <summary>等你允許。</summary>
    NeedsPermission,
    /// <summary>等你輸入（問了問題、剛開起來還沒下指令、出錯停下來）。</summary>
    NeedsInput,
    /// <summary>做完了。</summary>
    Done,
}

/// <summary>一個 Claude Code（或別的 agent）工作階段此刻的樣子。不可變，可以放心交給 UI 執行緒。</summary>
public sealed record AgentSession(
    string Id, string Project, string Cwd, string TranscriptPath, AgentState State,
    int Done, int Total, string Current, DateTime LastActive, AgentKind Agent = AgentKind.Claude);

/// <summary>
/// 依 session_id 分開記每個 Claude Code 工作階段：狀態、待辦進度、最後活動時間。
/// 只是純邏輯，不鎖；AgentBridge 負責在鎖裡呼叫。
/// </summary>
public sealed class AgentSessions
{
    /// <summary>這麼久沒有任何事件就當成已經關掉了。</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    private sealed class Entry
    {
        public required string Id;
        public AgentKind Agent;
        public string Cwd = "", Transcript = "";
        public AgentState State = AgentState.Working;
        public AgentPlan Plan = new();
        public DateTime LastActive;
        public long Order;
        /// <summary>最近處理過的 tool_use_id：同一次工具呼叫的 PostToolUse 萬一送了兩次，只算一次。</summary>
        public readonly Queue<string> Seen = new();
    }

    private readonly Dictionary<string, Entry> _entries = [];
    /// <summary>剛結束的工作階段：背景送的 hook 可能比 SessionEnd 晚到，不要讓它又冒出來。</summary>
    private readonly Dictionary<string, DateTime> _ended = [];
    private static readonly TimeSpan Late = TimeSpan.FromMinutes(1);
    private readonly Func<DateTime> _clock;
    private long _order;

    public AgentSessions(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.UtcNow);

    public int Count => _entries.Count;

    /// <summary>目前的清單，最近有動靜的在前面。</summary>
    public ImmutableArray<AgentSession> Snapshot() =>
        [.. _entries.Values.OrderByDescending(e => e.LastActive).ThenByDescending(e => e.Order).Select(View)];

    public AgentSession? Find(string id) => _entries.TryGetValue(id, out var e) ? View(e) : null;

    /// <summary>沒有 session_id 的舊版 Claude Code：用資料夾當作工作階段。</summary>
    public static string Key(JsonObject hook) =>
        hook["session_id"]?.ToString() is { Length: > 0 } id ? id : "cwd:" + (hook["cwd"]?.ToString() ?? "");

    public static string ProjectOf(string? cwd) => Path.GetFileName((cwd ?? "").TrimEnd('\\', '/'));

    /// <summary>
    /// 一則 hook（任何事件都可以丟進來）。回傳 true 表示清單或某個工作階段的狀態、進度有變。
    /// planChanged：這則讓那個工作階段的待辦清單變了（給 Progressed 用）。
    /// 別的 agent 的 hook 要先用 AgentKinds.Normalize 換成 Claude Code 的事件名稱。
    /// </summary>
    public bool Apply(JsonObject hook, out bool planChanged, AgentKind agent = AgentKind.Claude)
    {
        planChanged = false;
        var name = hook["hook_event_name"]?.ToString() ?? "";
        var key = Key(hook);
        if (name == "SessionEnd")
        {
            _ended[key] = _clock();
            return _entries.Remove(key);
        }
        if (_ended.TryGetValue(key, out var ended))
        {
            if (name != "SessionStart" && _clock() - ended < Late) return false;
            _ended.Remove(key);
        }

        bool created = !_entries.TryGetValue(key, out var e);
        if (e == null) e = _entries[key] = new Entry { Id = key, Agent = agent };
        var before = View(e);
        e.LastActive = _clock();
        e.Order = ++_order;
        if (hook["cwd"]?.ToString() is { Length: > 0 } cwd) e.Cwd = cwd;
        if (hook["transcript_path"]?.ToString() is { Length: > 0 } transcript) e.Transcript = transcript;

        switch (name)
        {
            case "SessionStart":
                // compact 會在工作到一半時發生，不改狀態；其他（新開、/clear、resume、fork）都是在等使用者下指令
                var source = hook["source"]?.ToString();
                if (source == "clear") e.Plan = new();
                if (source != "compact" || created) e.State = source == "compact" ? AgentState.Working : AgentState.NeedsInput;
                break;
            case "UserPromptSubmit":
                e.State = AgentState.Working;
                break;
            case "PermissionRequest":
                e.State = hook["tool_name"]?.ToString() is "AskUserQuestion" or "ExitPlanMode" ? AgentState.NeedsInput : AgentState.NeedsPermission;
                break;
            case "PostToolUse":
            case "PostToolUseFailure":
                e.State = AgentState.Working;
                if (hook["tool_use_id"]?.ToString() is { Length: > 0 } use)
                {
                    if (e.Seen.Contains(use)) break;
                    e.Seen.Enqueue(use);
                    if (e.Seen.Count > 64) e.Seen.Dequeue();
                }
                planChanged = e.Plan.Apply(hook);
                break;
            case "Notification":
                switch (hook["notification_type"]?.ToString())
                {
                    case "permission_prompt": e.State = AgentState.NeedsPermission; break;
                    case "elicitation_dialog" or "elicitation_url_dialog" or "agent_needs_input": e.State = AgentState.NeedsInput; break;
                    case "idle_prompt": if (e.State != AgentState.Done) e.State = AgentState.NeedsInput; break;
                }
                break;
            case "Stop":
                e.State = AgentState.Done;
                break;
            case "StopFailure":
                e.State = AgentState.NeedsInput;
                break;
        }
        return created || planChanged || before with { LastActive = e.LastActive } != View(e);
    }

    public bool Apply(JsonObject hook) => Apply(hook, out _);

    /// <summary>權限請求在 Shima 上有了回答（允許或拒絕），Claude Code 會繼續動。</summary>
    public bool Resumed(string key)
    {
        if (!_entries.TryGetValue(key, out var e) || e.State is not (AgentState.NeedsPermission or AgentState.NeedsInput)) return false;
        e.State = AgentState.Working;
        e.LastActive = _clock();
        return true;
    }

    /// <summary>拿掉太久沒動靜的。回傳 true 表示有拿掉。</summary>
    public bool Prune()
    {
        var now = _clock();
        var stale = _entries.Where(p => now - p.Value.LastActive > Idle).Select(p => p.Key).ToList();
        foreach (var key in stale) _entries.Remove(key);
        foreach (var key in _ended.Where(p => now - p.Value > Late).Select(p => p.Key).ToList()) _ended.Remove(key);
        return stale.Count > 0;
    }

    private static AgentSession View(Entry e) =>
        new(e.Id, ProjectOf(e.Cwd), e.Cwd, e.Transcript, e.State, e.Plan.Done, e.Plan.Steps.Count, e.Plan.Current, e.LastActive, e.Agent);
}

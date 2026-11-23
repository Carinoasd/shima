// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>
/// 接到島上的 AI coding agent。OpenCode 不在這裡：它的外掛 API 在 1.x 與 2.x 之間整個換掉、兩版同時在發行，
/// 而 2.x 的文件沒寫外掛能等多久、單一檔案能不能不裝套件就載入，沒辦法照文件做出可靠的整合。
/// </summary>
public enum AgentKind
{
    Claude,
    /// <summary>OpenAI Codex CLI：hooks.json 的 PermissionRequest 可以回答允許／拒絕（格式跟 Claude Code 一樣）。</summary>
    Codex,
    /// <summary>Gemini CLI：hook 沒辦法替人允許工具（Notification 只能看），所以只顯示狀態與完成通知。</summary>
    Gemini,
}

public static class AgentKinds
{
    public static string Name(AgentKind agent) => agent switch
    {
        AgentKind.Codex => "Codex",
        AgentKind.Gemini => "Gemini CLI",
        _ => "Claude Code",
    };

    /// <summary>工作階段清單上的小標籤；Claude Code 不加（維持原本的樣子）。</summary>
    public static string? Tag(AgentKind agent) => agent switch
    {
        AgentKind.Codex => "Codex",
        AgentKind.Gemini => "Gemini",
        _ => null,
    };

    /// <summary>網址的前綴（Claude Code 沿用最早的 /permission、/notify，已經裝好的 hook 才不用重裝）。</summary>
    public static string Prefix(AgentKind agent) => agent switch
    {
        AgentKind.Codex => "/codex/",
        AgentKind.Gemini => "/gemini/",
        _ => "/",
    };

    /// <summary>這個 agent 的 hook 能不能攔下權限請求等人回答。</summary>
    public static bool Asks(AgentKind agent) => agent != AgentKind.Gemini;

    /// <summary>
    /// 一則 hook 換成 Claude Code 的事件名稱與欄位，讓 AgentSessions 用同一套邏輯記狀態；null 表示這則不用管。
    /// 別的 agent 的 session_id 加上前綴，免得跟 Claude Code 的撞在一起。
    /// </summary>
    public static JsonObject? Normalize(AgentKind agent, JsonObject hook)
    {
        if (agent == AgentKind.Claude) return hook;
        var o = (JsonObject)hook.DeepClone();
        if (o["session_id"]?.ToString() is { Length: > 0 } id) o["session_id"] = $"{agent.ToString().ToLowerInvariant()}:{id}";
        var name = o["hook_event_name"]?.ToString();
        if (agent == AgentKind.Codex)
            // Codex 的事件名稱與欄位本來就跟 Claude Code 一樣（https://developers.openai.com/codex/hooks）
            return name is "SessionStart" or "UserPromptSubmit" or "PermissionRequest" or "PostToolUse" or "Stop" or "SessionEnd" ? o : null;

        // Gemini CLI（https://geminicli.com/docs/hooks/reference/）
        switch (name)
        {
            case "SessionStart" or "SessionEnd":
                return o;
            case "BeforeAgent":
                o["hook_event_name"] = "UserPromptSubmit";
                return o;
            case "AfterAgent":
                o["hook_event_name"] = "Stop";
                o["last_assistant_message"] = o["prompt_response"]?.DeepClone();
                return o;
            case "AfterTool":
                o["hook_event_name"] = "PostToolUse";
                o.Remove("tool_name");   // 待辦清單是 Claude Code 的工具，Gemini 的不要拿去比
                return o;
            case "Notification" when o["notification_type"]?.ToString() == "ToolPermission":
                o["notification_type"] = "permission_prompt";
                return o;
            default:
                return null;
        }
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>
/// Claude Code 自己的待辦清單，從它的 PostToolUse hook 重建：新版一次建立或更新一項（TaskCreate / TaskUpdate），
/// 舊版一次給整份（TodoWrite）。欄位名稱各版本不太一樣，所以寬鬆地讀。每個工作階段一份（AgentSessions 各自持有）；換了 session_id 就從頭開始。
/// </summary>
public sealed class AgentPlan
{
    public sealed record Step(string Id, string Text, string Status);

    private readonly List<Step> _steps = [];
    private string _session = "";

    public IReadOnlyList<Step> Steps => _steps;
    public int Done => _steps.Count(s => s.Status == "completed");
    /// <summary>正在做的那一項；沒有就是下一個還沒做的。</summary>
    public string Current =>
        (_steps.FirstOrDefault(s => s.Status == "in_progress") ?? _steps.FirstOrDefault(s => s.Status != "completed"))?.Text ?? "";

    /// <summary>一則 PostToolUse。回傳 true 表示清單有變。</summary>
    public bool Apply(JsonObject hook)
    {
        var tool = hook["tool_name"]?.ToString() ?? "";
        if (hook["tool_input"] is not JsonObject input) return false;
        var session = hook["session_id"]?.ToString() ?? "";
        if (session != _session && tool is "TaskCreate" or "TaskUpdate" or "TodoWrite") { _session = session; _steps.Clear(); }

        switch (tool)
        {
            case "TodoWrite":
                if (input["todos"] is not JsonArray todos) return false;
                _steps.Clear();
                foreach (var t in todos)
                    if (Text(t) is { Length: > 0 } text)
                        _steps.Add(new Step((_steps.Count + 1).ToString(), text, t?["status"]?.ToString() ?? "pending"));
                return true;

            case "TaskCreate":
            {
                if (Text(input) is not { Length: > 0 } text) return false;
                var response = hook["tool_response"];
                var id = Id(response) ?? Id(response?["task"]) ?? Id(input) ?? (_steps.Count + 1).ToString();
                _steps.RemoveAll(s => s.Id == id);
                _steps.Add(new Step(id, text, input["status"]?.ToString() ?? "pending"));
                return true;
            }

            case "TaskUpdate":
            {
                var id = Id(input);
                int i = _steps.FindIndex(s => s.Id == id);
                if (i < 0) return false;
                var status = input["status"]?.ToString() ?? _steps[i].Status;
                if (status == "deleted") { _steps.RemoveAt(i); return true; }
                _steps[i] = _steps[i] with { Status = status, Text = Text(input) is { Length: > 0 } text ? text : _steps[i].Text };
                return true;
            }
        }
        return false;
    }

    private static string? Id(JsonNode? n) =>
        (n?["taskId"] ?? n?["task_id"] ?? n?["id"])?.ToString() is { Length: > 0 } id ? id : null;

    private static string? Text(JsonNode? n) =>
        (n?["activeForm"] ?? n?["active_form"] ?? n?["subject"] ?? n?["content"] ?? n?["title"] ?? n?["description"])?.ToString();
}

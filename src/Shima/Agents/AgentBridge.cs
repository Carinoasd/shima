// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>Claude Code 想用某個工具，等人回答。Decision：「allow」、「deny」，或 null（回終端機照常問）。</summary>
public sealed class PermissionRequest
{
    public required string Project { get; init; }
    public required string Tool { get; init; }
    public required string Details { get; init; }
    public TaskCompletionSource<string?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Claude Code 的 hook 會連到這個只開在本機的 HTTP 端點。權限請求會等使用者在島上按下去；
/// 通知和「做完了」送了就走。每次呼叫都要帶只有這個 Windows 使用者讀得到的 token。
/// </summary>
public sealed class AgentBridge
{
    public const int Port = 47813;
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(570);

    private HttpListener? _listener;

    public event Action<PermissionRequest>? Asked;
    /// <summary>專案名稱、最後一句話。</summary>
    public event Action<string, string>? Finished;
    /// <summary>Claude Code 的待辦清單變了：專案名稱、做完幾項、總共幾項、正在做的那一項。</summary>
    public event Action<string, int, int, string>? Progressed;

    private readonly AgentPlan _plan = new();

    public static string TokenFile => Path.Combine(AppSettings.Folder, "bridge.token");

    public static string Token()
    {
        try
        {
            if (File.Exists(TokenFile) && File.ReadAllText(TokenFile).Trim() is { Length: >= 32 } existing) return existing;
            Directory.CreateDirectory(AppSettings.Folder);
            var fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            File.WriteAllText(TokenFile, fresh);
            return fresh;
        }
        catch (Exception e) { Log.Write("bridge token: " + e.Message); return ""; }
    }

    public void Start()
    {
        if (_listener != null) return;
        var token = Token();
        if (token.Length == 0) return;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(() => LoopAsync(_listener, token));
        }
        catch (Exception e) { Log.Write("bridge: " + e.Message); _listener = null; }
    }

    private async Task LoopAsync(HttpListener listener, string token)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(context, token));
        }
    }

    private async Task HandleAsync(HttpListenerContext context, string token)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            // 只接受 hook：網頁（帶 Origin）、token 不對、內容太大的一律拒絕
            if (request.HttpMethod != "POST" || request.Headers["Origin"] != null || request.ContentLength64 > 512 * 1024 ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Headers["X-Shima-Token"] ?? ""), Encoding.UTF8.GetBytes(token)))
            {
                response.StatusCode = 403;
                return;
            }

            string text;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) text = await reader.ReadToEndAsync();
            if (JsonNode.Parse(text) is not JsonObject json) { response.StatusCode = 400; return; }
            var project = Path.GetFileName((json["cwd"]?.ToString() ?? "").TrimEnd('\\', '/'));

            if (request.Url?.AbsolutePath == "/permission")
            {
                var ask = new PermissionRequest
                {
                    Project = project,
                    Tool = json["tool_name"]?.ToString() ?? "?",
                    Details = Describe(json["tool_name"]?.ToString(), json["tool_input"]),
                };
                if (Asked == null) { response.StatusCode = 204; return; }
                Asked.Invoke(ask);
                var done = await Task.WhenAny(ask.Decision.Task, Task.Delay(Patience));
                var behavior = done == ask.Decision.Task ? ask.Decision.Task.Result : null;
                ask.Decision.TrySetResult(null);
                if (Answer(behavior) is { } body)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    response.ContentType = "application/json";
                    await response.OutputStream.WriteAsync(bytes);
                }
                else response.StatusCode = 204;   // 沒有決定：Claude Code 照常在終端機問
                return;
            }

            var name = json["hook_event_name"]?.ToString();
            if (name == "Stop") Finished?.Invoke(project, json["last_assistant_message"]?.ToString() ?? "");
            else if (name == "PostToolUse")
            {
                bool changed;
                int done, total;
                string current;
                lock (_plan) { changed = _plan.Apply(json); done = _plan.Done; total = _plan.Steps.Count; current = _plan.Current; }
                if (changed) Progressed?.Invoke(project, done, total, current);
            }
            response.StatusCode = 204;
        }
        catch (Exception e) { Log.Write("bridge request: " + e.Message); try { response.StatusCode = 500; } catch { } }
        finally { try { response.Close(); } catch { } }
    }

    /// <summary>給 Claude Code 的回答；null 表示不回答（它會照常自己問）。</summary>
    public static string? Answer(string? behavior)
    {
        if (behavior is not ("allow" or "deny")) return null;
        var decision = new JsonObject { ["behavior"] = behavior };
        if (behavior == "deny") decision["message"] = "使用者在 Shima 上拒絕了這個動作。";
        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = decision },
        }.ToJsonString();
    }

    /// <summary>工具要做什麼，用一兩行講完：指令、檔案路徑、網址。</summary>
    public static string Describe(string? tool, JsonNode? input)
    {
        if (input is not JsonObject o) return "";
        foreach (var key in (string[])["command", "file_path", "path", "url", "pattern", "query", "prompt", "description"])
            if (o[key]?.ToString() is { Length: > 0 } value)
                return value.Length > 400 ? value[..400] + "…" : value;
        var all = o.ToJsonString();
        return all.Length > 400 ? all[..400] + "…" : all;
    }
}

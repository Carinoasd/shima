// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>
/// 「以後都允許」的規則比對（安全相關）：隨機產生的請求，只要碰到規則宣稱會擋的東西就不可以自動允許。
/// 這裡的「該擋」判斷是另外照規則的說明寫的，不呼叫被測的函式。
/// </summary>
public class FuzzAgentRulesTests
{
    private static readonly string[] Cwds = ["/home/u/proj", @"C:\Users\u\proj", "/mnt/c/work/app"];

    private static AgentRule[] BashRules(string cwd) =>
    [
        new("Bash", "git status", cwd), new("Bash", "ls", cwd), new("Bash", "dotnet build", cwd), new("Bash", "pytest", cwd), new("Bash", "cat", cwd),
        new("Bash", "git status", null), new("Bash", "ls", null),
    ];

    // ---------------------------------------------------------------- 獨立的判斷

    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".claude", ".ssh", ".env", ".aws", ".gnupg", ".vscode", ".github", ".config", ".bashrc", ".npmrc", ".gitconfig", ".mcp.json", ".netrc", "id_rsa", ".githooks",
    };

    private static bool IsSensitive(string segment)
    {
        var name = segment.TrimEnd('.', ' ').ToLowerInvariant();
        if (name.Length == 0) return false;
        if (Sensitive.Contains(name) || name.StartsWith(".env.")) return true;
        return name is "git~1" or "claude~1" or "ssh~1" or "env~1" or "github~1" or "gnupg~1" || name.StartsWith("git~") && name[4..].All(char.IsAsciiDigit) && name.Length > 4;
    }

    private static readonly char[] Seps = ['/', '\\'];

    /// <summary>
    /// 這個路徑相對於 cwd，一定不能自動允許嗎？保守地把 / 與 \ 都當分隔符：
    /// 往上跳出 cwd、絕對路徑不在 cwd 裡、~、任何一段（解析之後）是設定／金鑰、磁碟機相對路徑。
    /// </summary>
    private static string? MustRefuse(string path, string cwd)
    {
        if (path.Length == 0) return "空的";
        if (path[0] == '~') return "~";
        bool windowsCwd = cwd.Length >= 2 && cwd[1] == ':';
        var cwdSegs = cwd.Split(Seps, StringSplitOptions.RemoveEmptyEntries).Skip(windowsCwd ? 1 : 0).ToList();
        var stack = new List<string>();
        bool relative = true;
        string rest = path;
        if (path[0] is '/' or '\\')
        {
            if (windowsCwd) return "Windows 專案裡的 / 開頭路徑";
            relative = false;
        }
        else if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        {
            if (!windowsCwd) return "POSIX 專案裡的磁碟機路徑";
            if (path.Length < 3 || path[2] is not ('/' or '\\')) return "磁碟機相對路徑";
            if (char.ToLowerInvariant(path[0]) != char.ToLowerInvariant(cwd[0])) return "別的磁碟機";
            relative = false;
            rest = path[3..];
        }
        if (relative) stack.AddRange(cwdSegs);
        foreach (var seg in rest.Split(Seps))
        {
            if (seg.Length == 0 || seg == "." || (seg.Length > 2 && seg.All(c => c == '.'))) continue;
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(seg);
        }
        var cmp = windowsCwd ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (stack.Count < cwdSegs.Count || !cwdSegs.Zip(stack).All(p => string.Equals(p.First, p.Second, cmp))) return "不在 cwd 裡";
        if (stack.Any(IsSensitive)) return "設定或金鑰";
        return null;
    }

    private static readonly string[] CoreDangerous = ["output", "upload-pack", "receive-pack", "exec-path", "pager", "config", "git-dir", "work-tree", "exec"];

    private static string? MustRefuseArg(string arg, string cwd)
    {
        if (arg.Contains('=')) return "有 =";
        if (arg.StartsWith('@')) return "@";
        if (arg.StartsWith("--") && arg.Length > 4)
        {
            var name = arg[2..];
            int cut = name.IndexOfAny(['=', ':']);
            if (cut >= 0) name = name[..cut];
            if (name.Length >= 3 && CoreDangerous.Any(d => d.StartsWith(name, StringComparison.OrdinalIgnoreCase))) return "危險旗標";
        }
        if (arg.Length > 1 && arg[0] == '-')
        {
            // 旗標帶冒號的值：冒號後面整段當路徑
            int colon = arg.IndexOf(':');
            if (colon < 0 || colon == arg.Length - 1) return null;
            return MustRefusePathLike(arg[(colon + 1)..], cwd);
        }
        return MustRefusePathLike(arg, cwd);
    }

    private static string? MustRefusePathLike(string arg, string cwd)
    {
        if (arg.IndexOfAny(['?', '[', ']']) >= 0) return "萬用字元";
        // 冒號後面每一段（HEAD:.env、a:b）也各自是路徑；整個字不是磁碟機路徑時才拆
        bool drive = arg.Length >= 2 && char.IsAsciiLetter(arg[0]) && arg[1] == ':';
        var pieces = drive || !arg.Contains(':') ? [arg] : arg.Split(':').Where(p => p.Length > 0).Append(arg).ToArray();
        foreach (var piece in pieces)
        {
            if (piece.Split(Seps).Any(s => s.StartsWith('.') && s.Contains('*'))) return "隱藏檔萬用字元";
            if (piece.Contains(':') && !(piece.Length >= 2 && char.IsAsciiLetter(piece[0]) && piece[1] == ':')) { if (MustRefusePath(piece.Split(':')[0], cwd) is { } c) return c; continue; }
            if (MustRefusePath(piece, cwd) is { } why) return why;
        }
        return null;
    }

    private static string? MustRefusePath(string path, string cwd) => path.Length == 0 ? null : MustRefuse(path, cwd);

    // ---------------------------------------------------------------- Bash

    private static readonly string[] Args =
    [
        "-s", "--short", "-b", "-rf", "-la", "-j4", "-v:normal", "--no-restore", "-c", "-C", "-x", "-o", "-p:A=B", "-exec", "--exec", "--output", "--outp", "--out", "--upload", "--upload-pack", "--pager", "--config", "--git-dir", "--work-tree", "--ext", "--",
        "src", "src/a.cs", "a.txt", "./a", "../x", "../../etc/passwd", "..", ".", "...", "/etc/passwd", "/", "~", "~/x", "~root", "-", "foo=bar", "--output=x", "@resp", "$HOME", "*.cs", ".*", "*", "**", "?", "[a]", "{a,b}",
        ".git", ".git/config", ".GIT/config", ".git.", ".git ", ".git/", "src/.git/hooks/x", "GIT~1", "GIT~1/config", ".env", ".env.local", ".ENV", ".ssh/id_rsa", ".claude/settings.json", ".config/x", ".vscode/tasks.json", ".npmrc", ".mcp.json", ".gitconfig",
        "src/../.git", "src/../../x", "src/./.git", "./.env", "a/../.env", "a//.git", "a\\.git", "a\\..\\..\\x", "..\\x", "C:\\x", "C:/x", "D:secret.txt", "D:\\x", "a:b", "a.txt:stream", "HEAD:.env", "HEAD:src/a.cs", "--foo:D:secret.txt", "-p:.git", "-v:D:x",
        "C:\\Users\\u\\proj\\src", "c:/users/U/PROJ/a", "/home/u/proj/a", "/home/u/proj", "/home/u/proj/.git", "/home/u/proj-secret/x", "/home/u/proj/../x", "/mnt/c/work/app/a", "/mnt/c/work/app/../other", "//server/share/x", "\\\\server\\share\\x", "\\\\?\\C:\\x",
        "\u200B.git", "\uFF0Egit", ".g\u0131t", "\u212A", "a\tb", "x\u00A0y", "\u3000x", "中文", "😀", "\uD83D",
    ];

    private static readonly string[] Seps2 = [" ", " ", " ", "  ", "\t", " \t "];

    private static (string Command, string Cwd, string RuleCwd) BashInput(Random r)
    {
        var cwd = r.Pick(Cwds);
        string[] programs = ["git status", "git  status", "ls", "LS", "ls.exe", "Git.EXE status", "git", "git statu", "git status-x", "dotnet build", "dotnet  build", "pytest", "cat", "sudo ls", "bash", "python3", "rm", "git config", "env ls", "lsx", "./ls", "git\tstatus"];
        var sb = new System.Text.StringBuilder(r.Pick(programs));
        for (int n = r.Next(0, 5); n > 0; n--) sb.Append(r.Pick(Seps2)).Append(r.Chance(0.15) ? r.Mangle(r.Pick(Args)) : r.Pick(Args));
        var cmd = sb.ToString();
        if (r.Chance(0.25)) cmd = cmd.Insert(r.Next(cmd.Length + 1), r.Pick([";", "&&", "||", "|", "&", "`x`", "$(x)", "${x}", "<x", ">x", ">>x", "2>&1", "\n", "\r", "'", "\"", "\\", "(", ")", "{", "}", "!", "#", "\u0000", "\u001B", "\u200B", "\u2028", "\u00A0", "\u3000", "\uFEFF", "$", "%PATH%", "^"]));
        return (cmd, cwd, r.Chance(0.9) ? cwd : r.Pick(Cwds));
    }

    private static readonly char[] ShellChars = [';', '&', '|', '`', '$', '<', '>', '(', ')', '{', '}', '\\', '"', '\'', '!', '#', '\n', '\r'];

    [Fact]
    public void Bash_Rules_NeverAllow_WhatTheyPromiseToRefuse()
    {
        int allowed = 0;
        Fuzz.Run("AgentRules.Covers（Bash）", 5001, 8000, BashInput, v =>
        {
            var (command, cwd, ruleCwd) = v;
            var input = new JsonObject { ["command"] = command };
            foreach (var rule in BashRules(ruleCwd))
            {
                bool covered = AgentRules.Covers(rule, "Bash", input, cwd);
                // Match 與 Covers 用同一套：被 Suggest 出來的規則一定也放行
                if (!covered) continue;
                Interlocked.Increment(ref allowed);

                Assert.True(rule.Cwd == null || AgentRules.SameFolder(rule.Cwd, cwd), "規則的專案跟請求的不是同一個資料夾");
                Assert.True(command.IndexOfAny(ShellChars) < 0, "有串接／替換／重新導向／引號");
                Assert.DoesNotContain(command, c => char.IsControl(c) && c != '\t');
                Assert.DoesNotContain(command, c => char.IsWhiteSpace(c) && c is not (' ' or '\t'));
                Assert.DoesNotContain(command, c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
                var words = command.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                Assert.True(words.Length >= 1);
                var program = AgentRules.ProgramName(words[0]);
                Assert.Equal(rule.Command!.Split(' ')[0], program);
                int skip = rule.Command.Contains(' ') ? 2 : 1;
                var folder = rule.Cwd ?? cwd;
                foreach (var arg in words.Skip(skip))
                    Assert.True(MustRefuseArg(arg, folder) is null, $"規則「{rule.Command}」放行了參數 {Fuzz.Escape(arg)}（該擋：{MustRefuseArg(arg, folder)}）");
                if (rule.Command == "git status") Assert.Equal("status", words[1]);
            }
            // Suggest 給出的規則一定放行
            if (AgentRules.Suggest("Bash", input, cwd) is { } suggested) Assert.True(AgentRules.Covers(suggested, "Bash", input, cwd));
        }, v => $"command={Fuzz.Escape(v.Item1)} cwd={v.Item2} ruleCwd={v.Item3}");
        Assert.True(allowed > 500, $"隨機輸入太少會被放行（{allowed}），測試等於沒測");
    }

    [Fact]
    public void Plain_RejectsEveryShellMetacharacter()
    {
        Fuzz.Run("AgentRules.Plain", 5002, 4000, r => r.Mangle(r.Build(Args.Concat(["ls", "git", "status"]).Concat(Gen.Noise).ToArray(), 0, 6, 0.1) + (r.Chance(0.5) ? r.Pick(ShellChars.Select(c => c.ToString()).ToArray()) : "")), s =>
        {
            bool bad = s.IndexOfAny(ShellChars) >= 0 || s.Any(c => (char.IsControl(c) && c != '\t') || (char.IsWhiteSpace(c) && c is not (' ' or '\t')) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
            Assert.Equal(!bad, AgentRules.Plain(s));
        }, Fuzz.Escape);
    }

    // ---------------------------------------------------------------- 檔案工具

    private static readonly string[] PathSegs =
    [
        "src", "a.cs", "docs", "Proj", "proj", "..", "..", ".", "", "...", ".git", ".GIT", ".Git", ".git.", ".git ", "GIT~1", ".env", ".env.local", ".ENV", ".ssh", ".claude", "settings.json", ".config", ".vscode", ".github", ".gitconfig", ".npmrc", ".mcp.json", "CLAUDE~1", "id_rsa",
        "hooks", "config", "x.txt", "中文", "😀", ".hidden", "a b", "con", "nul", "a:b", "a*", "a?", ".git::$INDEX_ALLOCATION", ".gıt", "\u200B", "\uFF0Egit",
    ];

    private static string RandomPath(Random r, string cwd)
    {
        var sep = r.Pick(new[] { "/", "/", "\\", "/\\" });
        string prefix = r.Next(14) switch
        {
            0 => "", 1 => "", 2 => "", 3 => "/", 4 => "\\", 5 => cwd + sep, 6 => cwd + sep, 7 => cwd.ToUpperInvariant() + sep, 8 => "C:" + sep, 9 => "c:/", 10 => "//srv/share/", 11 => "~/", 12 => "/mnt/c/work/app" + sep, _ => cwd.Replace('\\', '/') + "/",
        };
        var path = prefix + string.Join(sep, Enumerable.Range(0, r.Next(0, 6)).Select(_ => r.Pick(PathSegs)));
        return r.Chance(0.15) ? r.Mangle(path) : path;
    }

    private static readonly (string Tool, string Key)[] FileTools = [("Edit", "file_path"), ("Write", "file_path"), ("Read", "file_path"), ("MultiEdit", "file_path"), ("NotebookEdit", "notebook_path"), ("Glob", "path"), ("Grep", "path")];

    [Fact]
    public void FileTools_NeverAllow_PathsOutsideTheProjectOrIntoConfigAndKeys()
    {
        int allowed = 0;
        Fuzz.Run("AgentRules.Covers（檔案工具）", 5003, 12000, r =>
        {
            var cwd = r.Pick(Cwds);
            var (tool, key) = r.Pick(FileTools);
            var input = new JsonObject();
            if (!(tool is "Glob" or "Grep") || r.Chance(0.8)) input[key] = RandomPath(r, cwd);
            if (r.Chance(0.1)) input["extra_path"] = RandomPath(r, cwd);
            if (r.Chance(0.05)) input[key] = r.Pick<JsonNode?>([5, null, true, new JsonArray("a"), new JsonObject()]);
            if (tool == "Glob" && r.Chance(0.5)) input["pattern"] = r.Pick(["**/*.cs", "src/**", "*.txt", "{.env,a}", "../x", "/etc/*", ".*", "**/.git/**", "[.]env", "{a,b}/x"]);
            return (tool, input.ToJsonString(), cwd, r.Chance(0.9) ? cwd : r.Pick(Cwds));
        }, v =>
        {
            var (tool, json, cwd, requestCwd) = v;
            var input = JsonNode.Parse(json);
            var rule = new AgentRule(tool, null, cwd);
            bool covered = AgentRules.Covers(rule, tool, input, requestCwd);
            if (AgentRules.Suggest(tool, input, requestCwd) is { } suggested) Assert.True(AgentRules.Covers(suggested, tool, input, requestCwd));
            if (!covered) return;
            Interlocked.Increment(ref allowed);
            Assert.True(AgentRules.SameFolder(cwd, requestCwd), "規則的專案跟請求的不是同一個資料夾");
            foreach (var (key, value) in input!.AsObject())
            {
                if (key is "pattern") continue;
                var path = value!.GetValue<string>();
                Assert.True(MustRefuse(path, cwd) is null, $"{tool} 放行了 {key}={Fuzz.Escape(path)}（該擋：{MustRefuse(path, cwd)}）");
            }
        }, v => $"tool={v.Item1} input={v.Item2} ruleCwd={v.Item3} requestCwd={v.Item4}");
        Assert.True(allowed > 300, $"隨機輸入太少會被放行（{allowed}），測試等於沒測");
    }

    [Theory]
    [InlineData("../x", "/home/u/proj")]
    [InlineData("src/../../x", "/home/u/proj")]
    [InlineData("/etc/passwd", "/home/u/proj")]
    [InlineData("/home/u/proj-secret/x", "/home/u/proj")]
    [InlineData("src/.git/config", "/home/u/proj")]
    [InlineData(".GIT./config", "/home/u/proj")]
    [InlineData("GIT~1/config", "/home/u/proj")]
    [InlineData("C:\\x", "C:\\Users\\u\\proj")]
    [InlineData("D:secret.txt", "C:\\Users\\u\\proj")]
    [InlineData("\\\\srv\\share\\x", "C:\\Users\\u\\proj")]
    [InlineData("~/x", "/home/u/proj")]
    public void TheOracleItselfRefusesTheObviousBadPaths(string path, string cwd)
    {
        Assert.NotNull(MustRefuse(path, cwd));
        Assert.False(AgentRules.Covers(new AgentRule("Edit", null, cwd), "Edit", new JsonObject { ["file_path"] = path }, cwd));
    }

    [Theory]
    [InlineData("src/a.cs", "/home/u/proj")]
    [InlineData("./src/../a.cs", "/home/u/proj")]
    [InlineData("/home/u/proj/a.cs", "/home/u/proj")]
    [InlineData("c:/users/U/PROJ/a.cs", "C:\\Users\\u\\proj")]
    public void TheOracleAllowsTheObviousGoodPaths(string path, string cwd)
    {
        Assert.Null(MustRefuse(path, cwd));
        Assert.True(AgentRules.Covers(new AgentRule("Edit", null, cwd), "Edit", new JsonObject { ["file_path"] = path }, cwd));
    }

    private static readonly string[] PatternParts =
    [
        "**", "*", "*.cs", "src", "/", "\\", ".", "..", ".git", ".env", ".*", "?", "[", "]", "[.]", "[!a]", "[^a]", "[--0]", "{", "}", ",", "{a,b}", "{.env,a}", "{/etc,src}", "{~,a}", "{C:,a}", "{..,a}", "~", "C:", "x", "中文", ".ssh", ".claude", "!",
    ];

    [Fact]
    public void GlobPatterns_NeverAllow_Absolute_Parent_Or_HiddenTargets()
    {
        Fuzz.Run("AgentRules.PatternProblem（Glob／Grep）", 5004, 8000, r =>
        {
            var pattern = r.Build(PatternParts, 0, 8, 0.05);
            return (r.Pick(["Glob", "Grep"]), pattern);
        }, v =>
        {
            var (tool, pattern) = v;
            var cwd = "/home/u/proj";
            var input = new JsonObject { [tool == "Glob" ? "pattern" : "glob"] = pattern, ["path"] = cwd + "/src" };
            if (!AgentRules.Covers(new AgentRule(tool, null, cwd), tool, input, cwd)) return;
            var expanded = AgentRules.Braces(pattern);
            Assert.NotNull(expanded);
            foreach (var one in expanded!)
            {
                Assert.False(one.Length > 0 && (one[0] is '/' or '\\' or '~' || (one.Length >= 2 && one[1] == ':')), $"絕對路徑 {Fuzz.Escape(one)}");
                var parts = one.Split(Seps);
                Assert.DoesNotContain("..", parts);
                Assert.DoesNotContain(parts, IsSensitive);
                Assert.DoesNotContain(parts, p => p.StartsWith('.') && p.IndexOfAny(['*', '?', '[', ']']) >= 0);
            }
        }, v => $"{v.Item1} pattern={Fuzz.Escape(v.Item2)}");
    }

    // ---------------------------------------------------------------- 其他不認得的工具與規則檔

    [Fact]
    public void UnknownTools_WithPathLikeInput_AreNeverAllowed()
    {
        string[] values = ["a", "x y", "/etc/passwd", "../x", "~/x", "C:\\x", "C:", "http://x.y", "a/b", "a\\b", "..", ".", "~", "5", ""];
        Fuzz.Run("AgentRules.Covers（不認得的工具）", 5005, 5000, r =>
        {
            var input = new JsonObject();
            for (int n = r.Next(0, 4); n > 0; n--) input[r.Pick(["query", "url", "path", "file", "dir", "target", "text", "cwd", "uri", "notes", "command"])] = JsonValue.Create(r.Pick(values));
            return (r.Pick(["Foo", "Fetch", "WebFetch", "mcp__x__y", "Task", "Agent", "WebSearch", "TodoWrite", "Bash", "PowerShell", "AskUserQuestion", "ExitPlanMode", "Monitor", ""]), input.ToJsonString());
        }, v =>
        {
            var (tool, json) = v;
            var input = JsonNode.Parse(json)!.AsObject();
            var rule = new AgentRule(tool, tool == "Bash" ? "ls" : null, "/home/u/proj");
            bool covered = AgentRules.Covers(rule, tool, input, "/home/u/proj");
            if (tool is "PowerShell" or "Monitor" or "AskUserQuestion" or "ExitPlanMode" or "") Assert.False(covered, "不給記的工具被放行了");
            if (!covered) return;
            if (input.ContainsKey("command")) Assert.Equal("Bash", tool);
            if (tool is not ("WebSearch" or "TodoWrite" or "Bash"))
                foreach (var (key, value) in input)
                {
                    var s = value!.GetValue<string>();
                    bool keyLooksLikePath = new[] { "path", "file", "dir", "cwd", "url", "uri" }.Any(k => key.EndsWith(k, StringComparison.OrdinalIgnoreCase));
                    Assert.False(keyLooksLikePath || s.Contains('/') || s.Contains('\\') || s.StartsWith('~') || s is ".." or "." || (s.Length >= 2 && char.IsAsciiLetter(s[0]) && s[1] == ':'), $"不認得的工具 {tool} 帶著 {key}={s} 被放行");
                }
        }, v => $"tool={v.Item1} input={v.Item2}");
    }

    [Fact]
    public void Match_UsesTheSameAnswerAsCovers_AndSurvivesABrokenRulesFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "shima-fuzz-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string[] files =
            [
                """[{"tool":"Bash","command":"git status","cwd":"/home/u/proj"},{"tool":"Bash","command":"bash","cwd":"/home/u/proj"},{"tool":"Bash"},{"tool":"Edit","command":"x"},{"tool":"Edit","cwd":"/"},{"tool":"Read","cwd":"/home/u/proj"}]""",
                """[null,5,"x",{},{"tool":5},{"tool":"","command":"ls"},{"tool":"Bash","command":"Git.exe status","cwd":5},{"tool":"PowerShell"}]""",
                "not json", "[", "{}", "null", "", """[{"tool":"Bash","command":"sudo ls"}]""", """[{"tool":"Bash","command":"git status","cwd":"/home/u/proj"},{"tool":"Bash","command":"git status","cwd":"/home/u/proj"}]""",
            ];
            Fuzz.Run("AgentRules.Match（規則檔）", 5006, 300, r => (r.Pick(files), r.Pick(["git status", "git status -s", "ls", "sudo ls", "git status; rm x", "bash", "git status .git"]), r.Pick(["Bash", "Edit", "Read", "PowerShell"]), r.Pick(["/home/u/proj", "/", null])), v =>
            {
                var (file, command, tool, cwd) = v;
                var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(path, file);
                var rules = new AgentRules(path);
                var input = tool == "Bash" ? new JsonObject { ["command"] = command } : new JsonObject { ["file_path"] = "/home/u/proj/a.cs" };
                var matched = rules.Match(tool, input, cwd);
                foreach (var rule in rules.List())
                {
                    Assert.NotEqual("", rule.Tool);
                    Assert.False(rule.Tool is "PowerShell" or "Monitor" or "AskUserQuestion" or "ExitPlanMode");
                    if (rule.Tool == "Bash") Assert.NotNull(rule.Command);
                }
                if (matched != null)
                {
                    Assert.True(AgentRules.Covers(matched, tool, input, cwd));
                    Assert.Contains(matched, rules.List());
                    Assert.False(command.Contains(';') && tool == "Bash");
                }
                if (matched == null) Assert.DoesNotContain(rules.List(), rule => AgentRules.Covers(rule, tool, input, cwd));
            }, v => $"file={Fuzz.Escape(v.Item1)} command={Fuzz.Escape(v.Item2)} tool={v.Item3} cwd={v.Item4}");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}

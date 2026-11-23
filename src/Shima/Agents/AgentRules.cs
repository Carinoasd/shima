// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shima.Agents;

/// <summary>
/// 一條「以後都允許」：工具名稱，Bash 的話再加上指令的前一兩個字（「git status」），可以限定只在某個專案資料夾。
/// Command 是 null 表示整個工具；Cwd 是 null 表示每個專案都算。
/// </summary>
public sealed record AgentRule(string Tool, string? Command = null, string? Cwd = null)
{
    /// <summary>按鈕上顯示的樣子：「git status」、「Edit」。</summary>
    public string Label => Command ?? Tool;
}

/// <summary>
/// 使用者對權限請求選過的「以後都允許」。比對寧可保守：Bash 指令只要有一點能串接別的指令的寫法就不比對，照常問人。
/// 規則只能由使用者按按鈕建立；存在 agent-rules.json。
/// </summary>
public sealed class AgentRules
{
    private readonly string? _path;
    private readonly object _gate = new();
    private List<AgentRule>? _rules;

    /// <param name="path">規則檔；null 就用 Shima 設定資料夾裡的 agent-rules.json（用到時才去找，測試不會碰到真的設定資料夾）。</param>
    public AgentRules(string? path = null) => _path = path;

    public string FilePath => _path ?? Path.Combine(AppSettings.Folder, "agent-rules.json");

    public IReadOnlyList<AgentRule> List()
    {
        lock (_gate) return [.. Loaded()];
    }

    /// <summary>加一條規則（已經有一樣的就不重複）。回傳 true 表示真的加了。</summary>
    public bool Add(AgentRule rule)
    {
        rule = rule with { Cwd = rule.Cwd is { } c ? Folder(c) : null };
        if (!Rememberable(rule)) return false;
        lock (_gate)
        {
            var rules = Loaded();
            if (rules.Contains(rule)) return false;
            rules.Add(rule);
            Save(rules);
            return true;
        }
    }

    public bool Remove(AgentRule rule)
    {
        lock (_gate)
        {
            var rules = Loaded();
            if (!rules.Remove(rule)) return false;
            Save(rules);
            return true;
        }
    }

    /// <summary>這個請求有沒有被某一條規則允許；有的話回傳那一條。</summary>
    public AgentRule? Match(string tool, JsonNode? input, string? cwd)
    {
        List<AgentRule> rules;
        lock (_gate) rules = [.. Loaded()];
        return rules.FirstOrDefault(r => Covers(r, tool, input, cwd));
    }

    private List<AgentRule> Loaded()
    {
        if (_rules != null) return _rules;
        _rules = [];
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonArray array)
                foreach (var item in array)
                    if (item?["tool"]?.ToString() is { Length: > 0 } tool)
                    {
                        var rule = new AgentRule(tool, item["command"]?.ToString(), item["cwd"]?.ToString());
                        if (Rememberable(rule) && !_rules.Contains(rule)) _rules.Add(rule);
                    }
        }
        catch (Exception e) { Log.Write("agent rules: " + e.Message); }   // 壞掉的檔當成沒有規則：最多就是照常問
        return _rules;
    }

    private void Save(List<AgentRule> rules)
    {
        try
        {
            var array = new JsonArray();
            foreach (var r in rules)
            {
                var o = new JsonObject { ["tool"] = r.Tool };
                if (r.Command != null) o["command"] = r.Command;
                if (r.Cwd != null) o["cwd"] = r.Cwd;
                array.Add(o);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, array.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            File.Move(temp, FilePath, overwrite: true);   // 先寫暫存檔再換掉：寫到一半當掉也不會留下半個檔
        }
        catch (Exception e) { Log.Write("agent rules: " + e.Message); }
    }

    // ---------------------------------------------------------------- 純函式

    /// <summary>要使用者回答的工具：只回 allow 不夠，一定要人來。</summary>
    private static readonly HashSet<string> Interactive = ["AskUserQuestion", "ExitPlanMode"];

    /// <summary>會執行任意指令、但不是 Bash 的工具：指令的語法不一樣，不敢比對，乾脆不給記。</summary>
    private static readonly HashSet<string> OtherShells = ["PowerShell", "Monitor"];

    /// <summary>後面接什麼都能執行的指令：記住「sudo」或「bash」等於什麼都允許，所以不給記。</summary>
    private static readonly HashSet<string> Wrappers =
    [
        "sudo", "su", "doas", "env", "command", "builtin", "exec", "eval", "source", ".", "xargs", "nohup", "time", "timeout",
        "watch", "nice", "ionice", "stdbuf", "strace", "ltrace", "setsid", "chroot", "flock", "unbuffer", "parallel", "script",
        "busybox", "sh", "bash", "zsh", "dash", "ksh", "fish", "csh", "tcsh", "cmd", "cmd.exe", "powershell", "powershell.exe",
        "pwsh", "pwsh.exe", "wsl", "wsl.exe", "ssh", "start", "trap", "alias", "function",
        "python", "python3", "py", "node", "deno", "bun", "perl", "ruby", "php", "lua", "Rscript", "osascript",
        // 參數本身就能執行指令：sed 的 e、rg --pre、tar --to-command、find -exec、rsync -e、vim -c、man -P…
        "sed", "awk", "gawk", "mawk", "nawk", "tar", "rg", "find", "fd", "rsync", "scp", "zip", "vim", "vi", "nvim", "emacs",
        "nano", "less", "more", "man", "git-shell",
    ];

    /// <summary>只記第一個字太寬的指令（git、npm…後面的子指令差很多），一定要有第二個字。</summary>
    private static readonly HashSet<string> NeedSubcommand =
    [
        "git", "gh", "dotnet", "npm", "npx", "pnpm", "yarn", "bunx", "uv", "uvx", "pip", "pip3", "cargo", "go", "docker",
        "podman", "kubectl", "helm", "terraform", "az", "aws", "gcloud", "winget", "choco", "scoop", "brew", "apt", "apt-get",
        "dnf", "yum", "pacman", "systemctl", "make", "cmake", "mvn", "gradle", "composer", "rustup", "conda", "claude",
    ];

    /// <summary>
    /// 如果使用者對這個請求按「以後都允許」，規則會長什麼樣；null 表示這種請求不給記（照常每次問）。
    /// </summary>
    public static AgentRule? Suggest(string tool, JsonNode? input, string? cwd)
    {
        if (tool.Length == 0 || Interactive.Contains(tool) || OtherShells.Contains(tool)) return null;
        var folder = cwd is { Length: > 0 } ? Folder(cwd) : null;
        if (tool == "Bash")
        {
            if (BashWords(input) is not { } words) return null;
            int n = words.Length >= 2 && Subcommand(words[1]) ? 2 : 1;
            if (n == 1 && NeedSubcommand.Contains(words[0])) return null;
            return new AgentRule(tool, string.Join(' ', words[..n]), folder);
        }
        if (input is JsonObject o && o.ContainsKey("command")) return null;   // 別的會執行指令的工具：不認得，不記
        return new AgentRule(tool, null, folder);
    }

    /// <summary>規則允不允許這個請求。</summary>
    public static bool Covers(AgentRule rule, string tool, JsonNode? input, string? cwd)
    {
        if (!Rememberable(rule) || rule.Tool != tool) return false;
        if (rule.Cwd != null && (cwd is not { Length: > 0 } || !SameFolder(rule.Cwd, cwd))) return false;
        if (tool != "Bash") return !(input is JsonObject o && o.ContainsKey("command"));
        if (rule.Command == null || BashWords(input) is not { } words) return false;
        var want = rule.Command.Split(' ');
        return words.Length >= want.Length && want.Select((w, i) => w == words[i]).All(same => same);
    }

    /// <summary>規則本身合不合理（手改過的規則檔也一樣檢查）。</summary>
    private static bool Rememberable(AgentRule rule)
    {
        if (rule.Tool.Length == 0 || Interactive.Contains(rule.Tool) || OtherShells.Contains(rule.Tool)) return false;
        if (rule.Tool != "Bash") return rule.Command == null;
        if (rule.Command == null) return false;   // 整個 Bash 都允許：不行
        var words = rule.Command.Split(' ');
        if (words.Length is < 1 or > 2 || !Program(words[0]) || Wrappers.Contains(words[0])) return false;
        if (words.Length == 1 && NeedSubcommand.Contains(words[0])) return false;
        return words.Length == 1 || Subcommand(words[1]);
    }

    /// <summary>
    /// Bash 指令拆成字；只要有任何能串接、替換、重新導向或藏東西的寫法就回傳 null（不比對）。
    /// </summary>
    public static string[]? BashWords(JsonNode? input)
    {
        if (input is not JsonObject o || o["command"] is not JsonValue v || !v.TryGetValue(out string? command)) return null;
        // 要求跳出沙箱的指令一律問人
        if (o["dangerouslyDisableSandbox"] is JsonValue d && (!d.TryGetValue(out bool off) || off)) return null;
        if (!Plain(command)) return null;
        var words = command.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || !Program(words[0]) || Wrappers.Contains(words[0])) return null;
        return words;
    }

    /// <summary>沒有 ; &amp; | ` $ &lt; &gt; ( ) { } \ 引號、換行、控制字元或奇怪的空白。</summary>
    public static bool Plain(string command)
    {
        foreach (char c in command)
        {
            if (c is ';' or '&' or '|' or '`' or '$' or '<' or '>' or '(' or ')' or '{' or '}' or '\\' or '"' or '\'' or '!' or '#' or '\n' or '\r') return false;
            if (c == '\t') continue;
            if (char.IsControl(c) || (char.IsWhiteSpace(c) && c != ' ')) return false;
            if (char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format) return false;   // 零寬字元之類看不見的東西
        }
        return true;
    }

    /// <summary>指令名稱：只能是英數字和 . _ + -，不能是路徑、不能是 FOO=bar 這種環境變數前綴。</summary>
    private static bool Program(string word) =>
        word.Length is > 0 and <= 64 && char.IsAsciiLetterOrDigit(word[0]) && word.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '-');

    /// <summary>像子指令的字：小寫英文開頭，只有小寫英數字和 -（「status」、「run-tests」）；選項或路徑不算。</summary>
    private static bool Subcommand(string word) =>
        word.Length is > 0 and <= 40 && char.IsAsciiLetterLower(word[0]) && word.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    /// <summary>資料夾比較前的整理：去掉結尾的斜線。</summary>
    public static string Folder(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>同一個資料夾（只比完全一樣的，子資料夾不算）。Windows 的路徑不分大小寫。</summary>
    public static bool SameFolder(string a, string b)
    {
        a = Folder(a);
        b = Folder(b);
        bool windows = a.Contains('\\') || a.Contains(':') || b.Contains('\\') || b.Contains(':');
        return string.Equals(a, b, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

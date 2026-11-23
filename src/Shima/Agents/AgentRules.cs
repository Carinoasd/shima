// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Shima.Agents;

/// <summary>
/// 一條「以後都允許」：工具名稱，Bash 的話再加上指令的前一兩個字（「git status」），可以限定只在某個專案資料夾。
/// Command 是 null 表示整個工具；Cwd 是 null 表示每個專案都算（會碰檔案的工具一定要有 Cwd 才放行）。
/// </summary>
public sealed record AgentRule(string Tool, string? Command = null, string? Cwd = null)
{
    /// <summary>按鈕上顯示的樣子：「git status」、「Edit」。</summary>
    public string Label => Command ?? Tool;
}

/// <summary>
/// 使用者對權限請求選過的「以後都允許」。比對寧可保守：只要有一點看不懂、可能跑出專案、可能換掉之後會被執行的東西，就不比對，照常問人。
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
        rule = Normalized(rule) with { Cwd = rule.Cwd is { } c ? Folder(c) : null };
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
            if (!rules.Remove(rule) && !rules.Remove(Normalized(rule))) return false;
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
                        // 舊版存的「Git.exe status」之類先正規化；正規化之後變成不該存在的（例如落在 Wrappers）就丟掉
                        var rule = Normalized(new AgentRule(tool, item["command"]?.ToString(), item["cwd"]?.ToString()));
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

    // ---------------------------------------------------------------- 名單

    private static readonly StringComparer NoCase = StringComparer.OrdinalIgnoreCase;

    /// <summary>要使用者回答的工具：只回 allow 不夠，一定要人來。</summary>
    private static readonly HashSet<string> Interactive = ["AskUserQuestion", "ExitPlanMode"];

    /// <summary>會執行任意指令、但不是 Bash 的工具：指令的語法不一樣，不敢比對，乾脆不給記。</summary>
    private static readonly HashSet<string> OtherShells = ["PowerShell", "Monitor"];

    /// <summary>
    /// 後面接什麼都能執行的指令：記住「sudo」或「bash」等於什麼都允許，所以不給記。
    /// 比對前名稱會先正規化（小寫、去掉 .exe/.cmd/.bat/.com/.ps1、另外也比去掉結尾版本號的「python3.12 → python」），
    /// 不然 Python.exe、CMD.EXE、node18 就能繞過。
    /// </summary>
    private static readonly HashSet<string> Wrappers = new(NoCase)
    {
        // 提權、換身分、換環境之後執行後面的指令
        "sudo", "su", "doas", "pkexec", "runas", "gsudo", "runuser", "sudoedit", "sg", "newgrp", "chroot", "nsenter", "unshare",
        "firejail", "proot", "fakeroot", "faketime", "torsocks", "proxychains", "proxychains4", "systemd-run", "ssh-agent",
        "dbus-launch", "dbus-run-session",
        // 包一層之後執行後面的指令
        "env", "command", "builtin", "exec", "eval", "source", ".", "xargs", "nohup", "time", "timeout", "watch", "nice", "ionice",
        "stdbuf", "strace", "ltrace", "setsid", "flock", "unbuffer", "parallel", "script", "taskset", "chrt", "entr", "expect",
        "gdb", "lldb", "valgrind", "perf", "trap", "alias", "function", "busybox", "toybox", "tmux", "screen", "byobu", "winpty",
        // shell 與直譯器：後面接腳本或 -c 就是任意程式碼
        "sh", "bash", "zsh", "dash", "ksh", "mksh", "fish", "csh", "tcsh", "ash", "nu", "elvish", "xonsh",
        "cmd", "powershell", "powershell_ise", "pwsh", "wsl", "wslg", "bash.exe", "conhost", "wt", "start", "explorer",
        "python", "pythonw", "py", "pyw", "pypy", "ipython", "node", "nodejs", "deno", "bun", "tsx", "ts-node", "zx", "perl",
        "ruby", "irb", "php", "lua", "luajit", "tclsh", "wish", "rscript", "r", "julia", "java", "javaw", "jshell", "kotlin",
        "scala", "groovy", "elixir", "iex", "erl", "escript", "runghc", "runhaskell", "swift", "dotnet-script", "csi", "fsi",
        "osascript", "open", "xdg-open", "gio",
        // Windows 內建、能載入或執行任意東西的程式（LOLBins）
        "cscript", "wscript", "mshta", "rundll32", "regsvr32", "msiexec", "reg", "regedit", "schtasks", "at", "sc", "wmic",
        "certutil", "bitsadmin", "forfiles", "mmc", "control", "hh", "cmstp", "msdt", "installutil", "regasm", "regsvcs",
        "odbcconf", "pcalua", "msxsl", "msbuild", "netsh", "crontab",
        // 網路：抓任意東西、寫任意檔、連出去執行
        "curl", "wget", "ssh", "scp", "sftp", "ftp", "telnet", "nc", "ncat", "netcat", "socat",
        // 參數本身就能執行指令：sed 的 e、rg --pre、tar --to-command、find -exec、rsync -e、vim -c、man -P…
        "sed", "awk", "gawk", "mawk", "nawk", "tar", "rg", "find", "fd", "rsync", "zip", "vim", "vi", "nvim", "gvim", "view",
        "ex", "ed", "emacs", "nano", "less", "more", "man", "git-shell",
        // 另一個 AI 代理：claude -p 加上旗標等於不受限地做任何事
        "claude",
    };

    /// <summary>只記第一個字太寬的指令（git、npm…後面的子指令差很多），一定要有第二個字。</summary>
    private static readonly HashSet<string> NeedSubcommand = new(NoCase)
    {
        "git", "gh", "dotnet", "npm", "npx", "pnpm", "pnpx", "yarn", "bunx", "corepack", "uv", "uvx", "pip", "pipx", "cargo", "go",
        "docker", "podman", "kubectl", "helm", "terraform", "az", "aws", "gcloud", "winget", "choco", "scoop", "brew", "apt",
        "apt-get", "dnf", "yum", "pacman", "systemctl", "make", "cmake", "mvn", "gradle", "composer", "rustup", "conda", "mamba",
        "micromamba", "bundle", "poetry", "pipenv", "pdm", "hatch", "rye", "pixi", "mise", "asdf", "nuget", "vcpkg", "bazel",
        "nx", "turbo", "lerna", "volta", "fnm", "nvm",
    };

    /// <summary>
    /// 第二個字本身就會執行任意東西的子指令：記住「uv run」等於允許「uv run 任何程式」，「git config」能把 core.pager 換成任意程式。
    /// 鍵是「正規化後的指令 子指令」。
    /// </summary>
    private static readonly HashSet<string> ExecSubcommands = new(NoCase)
    {
        // git：改設定（core.pager、core.fsmonitor…之後會被執行）、對每個子模組或二分搜尋時執行指令、叫外部工具
        "git config", "git submodule", "git bisect", "git difftool", "git mergetool", "git filter-branch", "git filter-repo",
        "git daemon", "git instaweb", "git send-email", "git credential", "git web--browse", "git p4", "git svn",
        "git archimport", "git cvsimport", "git cvsserver", "git upload-pack", "git receive-pack",
        // gh：任意 API 呼叫、執行擴充套件、設定會變成 shell 的別名、印出 token
        "gh api", "gh extension", "gh ext", "gh alias", "gh config", "gh auth", "gh codespace", "gh cs",
        // 套件管理器：執行任意套件或指令、改設定（script-shell 之類）
        "npm exec", "npm x", "npm explore", "npm config", "npm set", "npm init", "npm create", "npm edit",
        "pnpm exec", "pnpm dlx", "pnpm create", "pnpm config", "pnpm env",
        "yarn exec", "yarn dlx", "yarn create", "yarn config", "yarn node", "yarn set", "yarn plugin",
        "uv run", "uv tool", "uvx run", "pipx run", "poetry run", "poetry shell", "pipenv run", "pipenv shell", "pdm run",
        "hatch run", "hatch shell", "rye run", "pixi run", "pixi exec", "conda run", "mamba run", "micromamba run",
        "bundle exec", "mise exec", "mise x", "asdf exec", "nvm exec", "nvm run", "fnm exec", "volta run", "nx exec",
        "lerna exec", "corepack enable",
        // dotnet：執行任意 dll、跑工具、改套件來源
        "dotnet exec", "dotnet tool", "dotnet fsi", "dotnet nuget", "nuget sources", "nuget config",
        // go：依檔案裡的 //go:generate 執行任意指令、改環境設定
        "go generate", "go env",
        // 容器：在容器裡執行任意指令，還能把主機的資料夾掛進去
        "docker run", "docker exec", "docker create", "docker compose", "docker container", "docker buildx",
        "podman run", "podman exec", "podman create", "podman compose", "podman container",
        "kubectl exec", "kubectl run", "kubectl debug", "kubectl attach", "kubectl cp",
    };

    /// <summary>
    /// 危險的旗標（不分大小寫、去掉開頭的 - 之後比，所以 -exec 和 --exec 都算）。
    /// 長旗標還比「縮寫」：git 與 GNU getopt 接受不會混淆的縮寫，--outp 等於 --output、--upload 等於 --upload-pack。
    /// 寧可擋掉一些其實無害的（只是多問一次）。
    /// </summary>
    private static readonly string[] DangerousFlags =
    [
        // 把輸出寫到任意檔：git diff --output、日誌檔、dotnet test --diag
        "output", "output-file", "output-directory", "out-file", "outfile", "log-file", "logfile", "diag",
        // 指定要執行的外部程式：git 傳輸的 upload/receive-pack、find -exec/-ok、pager、編輯器、diff 工具、
        // tar --to-command/--checkpoint-action/--use-compress-program、rsync --rsh/--rsync-path、go -exec/-toolexec、rg --pre、npm --script-shell
        "upload-pack", "receive-pack", "exec", "exec-path", "execdir", "ok", "okdir", "pager", "open-files-in-pager", "editor",
        "extcmd", "tool", "ext-diff", "textconv", "rsh", "rsync-path", "ssh", "ssh-command", "proxy-command", "shell",
        "script-shell", "to-command", "checkpoint-action", "info-script", "new-volume-script", "rmt-command", "rsh-command",
        "use-compress-program", "compress-program", "toolexec", "compiler", "pre", "pre-glob", "command", "eval", "call",
        // 換掉設定或改從別處載入設定、程式碼：git --config/--template/--git-dir、cargo --config、dotnet --settings/-property/--logger、
        // go -overlay/-modfile/-ldflags、node --require/--import、npm --prefix/--userconfig/--registry、make --file/--include-dir、
        // pip --index-url/--find-links、nuget --source
        "config", "config-env", "config-file", "configfile", "userconfig", "globalconfig", "settings", "template", "git-dir",
        "work-tree", "separate-git-dir", "super-prefix", "property", "test-adapter-path", "logger", "collect", "overlay",
        "modfile", "ldflags", "gcflags", "asmflags", "gccgoflags", "require", "import", "loader", "node-options", "prefix",
        "file", "makefile", "include-dir", "registry", "index", "index-url", "extra-index-url", "find-links", "source",
        "add-source",
    ];

    /// <summary>
    /// 各指令危險的單字母旗標（不分大小寫；-xvf 這種併在一起的也逐字檢查）。只對該指令有效，因為 -c、-e 在別的指令是無害的意思。
    /// </summary>
    private static readonly Dictionary<string, string> DangerousShortFlags = new(NoCase)
    {
        ["git"] = "coxu",      // -c 設定（core.pager…）、-C 換目錄、-o 輸出檔、-O 開檔用的 pager、rebase -x 執行指令、clone -u upload-pack
        ["make"] = "fcie",     // -f 別的 makefile、-C 換目錄、-I 從別處 include、-E 直接給 make 程式碼
        ["dotnet"] = "psl",    // -p:屬性（能改 MSBuild 行為）、-s 套件來源／測試設定檔、-l 載入 logger
        ["npm"] = "c", ["pnpm"] = "c", ["yarn"] = "c", ["npx"] = "cp", ["bunx"] = "cp",   // exec -c 執行指令、npx -p 裝任意套件
        ["pip"] = "if", ["uv"] = "if", ["uvx"] = "if", ["pipx"] = "if",                // -i 套件索引、-f 從別處找套件
        ["cargo"] = "z",       // -Z 不穩定功能（含改設定）
        ["cmake"] = "epc",     // -E 執行指令、-P 執行腳本、-C 先執行快取腳本
        ["rsync"] = "e", ["ssh"] = "oefj", ["scp"] = "osfj", ["tar"] = "i",           // 這幾個本來就在 Wrappers，這裡是多一層保險
    };

    /// <summary>
    /// 放設定、hook、金鑰、開機自動執行東西的資料夾：改了等於能換掉之後會被執行的程式，或讀到金鑰。路徑任何一段是這些就不自動允許。
    /// </summary>
    private static readonly HashSet<string> SensitiveFolders = new(NoCase)
    {
        // Claude Code 自己的設定與 hook（~/.claude/settings.json、專案的 .claude/settings.json）
        ".claude",
        // 版本控制：.git/hooks、.git/config 會被執行
        ".git", ".svn", ".hg", ".githooks", ".husky",
        // CI 與編輯器、開發容器的設定：裡面的指令會被自動執行
        ".github", ".gitlab", ".circleci", ".buildkite", ".vscode", ".vscode-server", ".idea", ".devcontainer", ".zed",
        // 金鑰與雲端憑證
        ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker", ".gcloud", ".terraform.d", ".password-store",
        // 套件管理器與工具鏈的設定、快取裡會被執行的東西（.yarn/releases、.gradle/init.d、.ipython/profile_*/startup…）
        ".cargo", ".npm", ".nuget", ".m2", ".gradle", ".pip", ".conda", ".ipython", ".jupyter", ".gem", ".bundle", ".yarn",
        ".pnpm-store", ".rustup", ".deno", ".bun",
        // 使用者層級的設定、自動啟動與服務（~/.config/autostart、systemd --user、fish、git…）
        ".config", ".local", "appdata", "start menu", "autostart", "launchagents", "launchdaemons",
    };

    /// <summary>會在別的時候被自動讀進去執行、或存著密碼的檔名。</summary>
    private static readonly HashSet<string> SensitiveFiles = new(NoCase)
    {
        // 環境變數與密碼
        ".env", ".envrc", ".netrc", "_netrc", ".git-credentials", ".pypirc", ".npmrc", ".yarnrc", ".yarnrc.yml", ".pnpmfile.cjs",
        "pip.conf", "pip.ini", "nuget.config", ".pgpass", ".my.cnf",
        // shell 啟動時執行
        ".bashrc", ".bash_profile", ".bash_login", ".bash_logout", ".bash_aliases", ".zshrc", ".zshenv", ".zprofile", ".zlogin",
        ".zlogout", ".profile", ".kshrc", ".cshrc", ".tcshrc", ".login", ".logout", ".inputrc",
        // git 設定與會叫外部程式的設定
        ".gitconfig", ".gitmodules", ".gitattributes",
        // 編輯器、工具在當下資料夾自動讀的設定
        ".vimrc", ".gvimrc", ".exrc", ".nvimrc", ".tmux.conf", ".wgetrc", ".curlrc", ".pdbrc", ".pythonrc",
        // Python 啟動時自動匯入
        "sitecustomize.py", "usercustomize.py",
        // MCP 伺服器設定（會被啟動）、commit 時執行的 hook 設定
        ".mcp.json", ".pre-commit-config.yaml", ".huskyrc", ".lintstagedrc",
        // MSBuild 會自動從上層資料夾匯入，裡面能放任意建置步驟
        "directory.build.props", "directory.build.targets",
        // PowerShell 啟動時執行的 profile
        "profile.ps1", "microsoft.powershell_profile.ps1", "microsoft.vscode_profile.ps1",
        // Windows 開資料夾時會被讀
        "desktop.ini", "autorun.inf",
        // SSH 金鑰（萬一不在 .ssh 底下）
        "authorized_keys", "id_rsa", "id_ed25519", "id_ecdsa", "id_dsa",
    };

    /// <summary>
    /// 知道怎麼檢查路徑的檔案工具：哪些欄位是目標路徑（第一個是必填）。欄位名稱依 Claude Code 官方文件
    /// （hooks 的 PreToolUse 與 Agent SDK 的工具輸入型別）：Edit/Write/Read 是 file_path、NotebookEdit 是 notebook_path、
    /// Glob/Grep 的 path 可省略（省略就是 cwd）。MultiEdit 現在的文件已經沒有，沿用舊版的 file_path。
    /// </summary>
    private static readonly Dictionary<string, (string[] Paths, bool Required, string[] Patterns)> FileTools = new()
    {
        ["Edit"] = (["file_path"], true, []),
        ["Write"] = (["file_path"], true, []),
        ["MultiEdit"] = (["file_path"], true, []),
        ["NotebookEdit"] = (["notebook_path", "file_path"], true, []),
        ["Read"] = (["file_path"], true, []),
        ["Glob"] = (["path"], false, ["pattern"]),
        ["Grep"] = (["path"], false, ["glob"]),
    };

    /// <summary>完全沒有路徑性質的工具：參數是查詢字串或待辦文字，裡面有斜線也不是路徑，維持「工具名稱＋cwd」。</summary>
    private static readonly HashSet<string> NoPathTools =
    [
        "WebSearch", "TodoWrite", "TaskCreate", "TaskGet", "TaskList", "TaskUpdate", "TaskStop", "ToolSearch",
        "ListMcpResourcesTool", "CronList", "CronDelete", "ListAgents", "EnterPlanMode",
    ];

    // ---------------------------------------------------------------- 純函式

    /// <summary>
    /// 如果使用者對這個請求按「以後都允許」，規則會長什麼樣；null 表示這種請求不給記（照常每次問）。
    /// 一定跟 Covers 一致：給出來的規則一定放行這個請求，Covers 不會放行的就不給記。
    /// </summary>
    public static AgentRule? Suggest(string tool, JsonNode? input, string? cwd)
    {
        if (tool.Length == 0 || Interactive.Contains(tool) || OtherShells.Contains(tool)) return null;
        var folder = cwd is { Length: > 0 } ? Folder(cwd) : null;
        AgentRule rule;
        if (tool == "Bash")
        {
            if (BashWords(input) is not { } words) return null;
            var program = ProgramName(words[0]);
            rule = new AgentRule(tool, words.Length >= 2 && Subcommand(words[1]) ? program + " " + words[1] : program, folder);
        }
        else rule = new AgentRule(tool, null, folder);
        return Covers(rule, tool, input, cwd) ? rule : null;
    }

    /// <summary>規則允不允許這個請求。</summary>
    public static bool Covers(AgentRule rule, string tool, JsonNode? input, string? cwd) => Refusal(rule, tool, input, cwd) == null;

    /// <summary>規則為什麼不放行這個請求；null 表示放行。</summary>
    public static string? Refusal(AgentRule rule, string tool, JsonNode? input, string? cwd)
    {
        rule = Normalized(rule);
        if (!Rememberable(rule)) return "規則本身不合理";
        if (rule.Tool != tool) return "工具不同";
        cwd = cwd is { Length: > 0 } ? cwd : null;
        if (rule.Cwd != null && (cwd == null || !SameFolder(rule.Cwd, cwd))) return "不在規則的專案資料夾";
        if (tool == "Bash")
        {
            if (BashWords(input) is not { } words) return "指令有串接、替換、重新導向或看不懂的寫法";
            var want = rule.Command!.Split(' ');
            if (words.Length < want.Length || ProgramName(words[0]) != want[0] || (want.Length == 2 && words[1] != want[1])) return "指令不同";
            return ArgumentProblem(want[0], words[want.Length..], rule.Cwd ?? cwd, cwd);
        }
        return InputProblem(tool, input, rule.Cwd, cwd);
    }

    /// <summary>
    /// 記住的指令後面其餘的參數是不是都看起來無害；null 表示無害，不然回傳原因。這是正向檢查：每個參數都要過關。
    /// </summary>
    /// <param name="program">正規化後的指令名稱（決定用哪一組單字母危險旗標）。</param>
    /// <param name="folder">參數裡的路徑必須在這個資料夾裡；null 表示沒限定專案，這時絕對路徑與跳出去的 .. 一律不行。</param>
    /// <param name="cwd">相對路徑的基準；null 就用 folder。</param>
    public static string? ArgumentProblem(string program, IReadOnlyList<string> args, string? folder, string? cwd = null)
    {
        foreach (var arg in args)
        {
            // 1. 有 = 一律不放行：--output=x、--upload-pack=x、SHELL=x（make 變數覆寫）、-exec=x、-c key=value 都長這樣
            if (arg.Contains('=')) return $"參數 {arg} 含有 =";
            // 回應檔：dotnet/msbuild/gcc 會把 @檔案 的內容當成更多參數，等於藏了看不到的旗標
            if (arg.StartsWith('@')) return $"參數 {arg} 是回應檔";
            // 2. 危險旗標名單
            if (FlagProblem(program, arg) is { } flag) return flag;
            // 單橫線的旗標可以把好幾個字母併在一起、最後一個直接黏著它的值（cp -rt/etc、grep -rf.env）。
            // 不知道每個程式哪個字母要吃值，就切不出哪一段是路徑，所以只放行純英數的（-rf、-xvf、-j4）；
            // 黏著 / . ~ : 之類的一律問人。
            if (arg.Length > 1 && arg[0] == '-' && !arg.StartsWith("--") && !arg[1..].All(char.IsAsciiLetterOrDigit)) return $"旗標 {arg} 黏著別的東西";
            // 3. 參數裡的路徑：不能跑出專案、不能指到設定／hook／金鑰
            foreach (var piece in PathPieces(arg))
            {
                if (piece.Contains('?') || piece.Contains('[') || piece.Contains(']')) return $"參數 {arg} 有萬用字元";
                // * 不會比對到 . 開頭的檔名，但 .* 會（.git、.env）
                if (piece.Split('/').Any(s => s.StartsWith('.') && s.Contains('*'))) return $"參數 {arg} 的萬用字元會比對到隱藏檔";
                if (PathProblem(folder, piece, cwd, needFolder: false) is { } path) return $"參數 {arg}：{path}";
            }
        }
        return null;
    }

    /// <summary>一個旗標是不是危險名單裡的（含縮寫與併在一起的單字母）。</summary>
    private static string? FlagProblem(string program, string arg)
    {
        if (arg.Length < 2 || arg[0] != '-' || arg == "--") return null;
        bool isLong = arg.StartsWith("--");
        var name = arg.TrimStart('-');
        int colon = name.IndexOf(':');   // dotnet 的 -p:Name、-logger:x
        if (colon >= 0) name = name[..colon];
        if (name.Length == 0) return null;
        foreach (var d in DangerousFlags)
            if (string.Equals(name, d, StringComparison.OrdinalIgnoreCase) || (isLong && d.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
                return $"旗標 {arg} 能指定外部程式、設定或輸出檔";
        // 只看 : 前面的旗標字母：dotnet 的 -v:normal 裡，normal 是值不是旗標
        if (!isLong && DangerousShortFlags.TryGetValue(Bare(program), out var letters))
            foreach (char c in name)
                if (char.IsAsciiLetter(c) && letters.Contains(char.ToLowerInvariant(c)))
                    return $"旗標 {arg} 對 {program} 是危險的 -{c}";
        return null;
    }

    /// <summary>
    /// 一個參數裡可能是路徑的部分。旗標本身不算（單橫線的已經確認是純英數，長旗標的值是下一個字）。
    /// 整個字一定要檢查一次，不能先用 : 切開：C:/work/a.cs 切開就不是 Windows 路徑了，
    /// 而 D:secret.txt（磁碟機相對路徑）與 a.txt:stream（NTFS 的替代資料流）切開後兩段看起來都很正常。
    /// 開頭不是磁碟機代號的，再另外檢查 : 後面的每一段（HEAD:.env、a:b 這種路徑清單、網址）。
    /// </summary>
    private static IEnumerable<string> PathPieces(string arg)
    {
        if (arg.Length == 0 || arg[0] == '-') 
        {
            // 長旗標帶著 : 的值（dotnet 的 --logger:x）
            foreach (var value in arg.Split(':').Skip(1)) if (value.Length > 0) yield return value;
            yield break;
        }
        yield return arg;
        bool drive = arg.Length >= 2 && char.IsAsciiLetter(arg[0]) && arg[1] == ':';
        if (drive || !arg.Contains(':')) yield break;
        foreach (var part in arg.Split(':')) if (part.Length > 0) yield return part;
    }

    /// <summary>
    /// 非 Bash 工具的參數檢查；null 表示放行。
    /// 檔案工具：目標路徑一定要在規則的專案資料夾裡、不能是設定／hook／金鑰；不認得的工具帶著像路徑或網址的參數就不放行。
    /// </summary>
    private static string? InputProblem(string tool, JsonNode? input, string? folder, string? cwd)
    {
        if (input is JsonObject withCommand && withCommand.ContainsKey("command")) return "會執行指令的工具";
        if (FileTools.TryGetValue(tool, out var spec))
        {
            if (folder == null) return "檔案工具的規則沒有限定專案資料夾";
            if (input is not JsonObject o) return "沒有目標路徑";
            bool found = false;
            foreach (var (key, value) in o)
            {
                if (spec.Paths.Contains(key) || PathKey(key))
                {
                    if (value is not JsonValue v || !v.TryGetValue(out string? path)) return $"{key} 不是字串";
                    if (PathProblem(folder, path, cwd, needFolder: true) is { } problem) return $"{key}：{problem}";
                    if (spec.Paths.Contains(key)) found = true;
                }
                else if (spec.Patterns.Contains(key))
                {
                    if (value is not JsonValue v || !v.TryGetValue(out string? pattern)) return $"{key} 不是字串";
                    if (PatternProblem(pattern) is { } problem) return $"{key}：{problem}";
                }
            }
            if (spec.Required && !found) return "沒有目標路徑";
            // 沒有 path 的 Glob/Grep 就是在 cwd 找：cwd 已經確定是規則的專案，但專案本身不能太大
            if (!found && ParseFolder(folder) is { } f && Broad(f)) return "專案資料夾是根目錄或家目錄";
            return null;
        }
        if (NoPathTools.Contains(tool)) return null;
        // 不認得的工具：不知道哪個欄位是路徑、怎麼驗證，只要有像路徑或網址的東西就問人
        return PathLike(input, null, 0) ? "不認得的工具帶著像路徑或網址的參數" : null;
    }

    /// <summary>Glob 的 pattern、Grep 的 glob：只能是專案裡的相對樣式。</summary>
    private static string? PatternProblem(string pattern)
    {
        if (pattern.Length == 0) return null;
        if (pattern[0] is '/' or '\\' or '~' || (pattern.Length >= 2 && pattern[1] == ':')) return "樣式是絕對路徑";
        var parts = pattern.Split('/', '\\');
        if (parts.Contains("..")) return "樣式往上跳出資料夾";
        if (Sensitive(parts)) return "樣式指到設定、hook 或金鑰";
        // { } 裡的每個選項也都是名字：{.env,.git}/**
        var names = pattern.Split('/', '\\', '{', '}', ',');
        if (Sensitive(names)) return "樣式指到設定、hook 或金鑰";
        // . 開頭又帶萬用字元的名字會比對到隱藏檔：**/.env*、.*、.?nv、.[e]nv
        if (names.Any(n => n.StartsWith('.') && n.IndexOfAny(['*', '?', '[', ']']) >= 0)) return "樣式會比對到隱藏檔";
        return null;
    }

    private static bool PathKey(string key)
    {
        var k = key.ToLowerInvariant();
        return k.EndsWith("path") || k.EndsWith("file") || k.EndsWith("dir") || k.EndsWith("directory") || k.EndsWith("folder")
            || k.EndsWith("cwd") || k.EndsWith("url") || k.EndsWith("uri");
    }

    /// <summary>參數裡有沒有看起來像路徑或網址的東西（遞迴看陣列與物件；太深的當成有）。</summary>
    private static bool PathLike(JsonNode? node, string? key, int depth)
    {
        if (depth > 16) return true;
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o) if (PathLike(v, k, depth + 1)) return true;
                return false;
            case JsonArray a:
                foreach (var v in a) if (PathLike(v, key, depth + 1)) return true;
                return false;
            case JsonValue v when v.TryGetValue(out string? s):
                if (key != null && PathKey(key)) return true;
                return s.Contains('/') || s.Contains('\\') || s.StartsWith('~') || s is ".." or "." || (s.Length >= 2 && char.IsAsciiLetter(s[0]) && s[1] == ':');
            default:
                return false;
        }
    }

    /// <summary>規則本身合不合理（手改過的規則檔也一樣檢查）。規則要先正規化。</summary>
    private static bool Rememberable(AgentRule rule)
    {
        if (rule.Tool.Length == 0 || Interactive.Contains(rule.Tool) || OtherShells.Contains(rule.Tool)) return false;
        if (rule.Tool != "Bash") return rule.Command == null;
        if (rule.Command == null) return false;   // 整個 Bash 都允許：不行
        var words = rule.Command.Split(' ');
        if (words.Length is < 1 or > 2 || !Program(words[0]) || IsWrapper(words[0])) return false;
        if (words.Length == 1) return !NeedSubcommand.Contains(words[0]) && !NeedSubcommand.Contains(Bare(words[0]));
        return Subcommand(words[1]) && !ExecSubcommands.Contains(words[0] + " " + words[1]) && !ExecSubcommands.Contains(Bare(words[0]) + " " + words[1]);
    }

    /// <summary>規則的指令名稱正規化（Git.exe status → git status），讓新舊規則、大小寫寫法不同的請求互通。</summary>
    private static AgentRule Normalized(AgentRule rule)
    {
        if (rule.Tool != "Bash" || rule.Command == null) return rule;
        var words = rule.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return rule;
        words[0] = ProgramName(words[0]);
        return rule with { Command = string.Join(' ', words) };
    }

    /// <summary>
    /// 指令名稱正規化：小寫、去掉 Windows 會自動補上的副檔名（.exe .com .cmd .bat，加上 .ps1）與結尾的點、空白
    /// （Windows 會忽略結尾的點：bash. 等於 bash）。重複去，所以 x.exe.exe 也一樣。
    /// </summary>
    public static string ProgramName(string word)
    {
        var name = word.ToLowerInvariant();
        while (true)
        {
            var before = name;
            name = name.TrimEnd('.', ' ');
            foreach (var ext in (string[])[".exe", ".com", ".cmd", ".bat", ".ps1"])
                if (name.Length > ext.Length && name.EndsWith(ext)) { name = name[..^ext.Length]; break; }
            if (name == before) break;
        }
        return name.Length == 0 ? word.ToLowerInvariant() : name;
    }

    /// <summary>再去掉結尾的版本號：python3.12、node18、pip3 → python、node、pip（只用來查名單）。</summary>
    private static string Bare(string name)
    {
        var bare = ProgramName(name).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '-', '_');
        return bare.Length == 0 ? ProgramName(name) : bare;
    }

    private static bool IsWrapper(string word)
    {
        var name = ProgramName(word);
        return Wrappers.Contains(name) || Wrappers.Contains(Bare(name));
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
        if (words.Length == 0 || !Program(words[0]) || IsWrapper(words[0])) return null;
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

    /// <summary>資料夾比較前的整理：去掉結尾的斜線（只是顯示與存檔用；比較請用 SameFolder／Inside）。</summary>
    public static string Folder(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>
    /// 同一個資料夾（只比完全一樣的，子資料夾不算）。Windows 路徑不分大小寫、/ 與 \ 混用都行；POSIX（WSL）路徑分大小寫；
    /// 一邊 Windows 一邊 POSIX、或看不懂的路徑一律不同。
    /// </summary>
    public static bool SameFolder(string a, string b) =>
        ParseFolder(a) is { } pa && ParseFolder(b) is { } pb && Contains(pa, pb) && pa.Segments.Count == pb.Segments.Count;

    /// <summary>
    /// path 是不是在 folder 裡面（folder 本身也算）。相對路徑以 cwd 為基準（沒給就用 folder）。
    /// 不碰檔案系統：先逐段處理 . 與 ..，再比資料夾邊界（C:\work\app 不含 C:\work\app-secret）。
    /// 已知限制：符號連結、junction 沒辦法在不讀檔案系統的情況下解析，專案裡指到外面的連結會被當成在裡面。
    /// </summary>
    public static bool Inside(string folder, string path, string? cwd = null)
    {
        if (ParseFolder(folder) is not { } f) return false;
        var basis = cwd is { Length: > 0 } ? ParseFolder(cwd) : f;
        return basis != null && Parse(path, basis) is { Root: not null, Escaped: false } p && Contains(f, p);
    }

    /// <summary>
    /// 一個目標路徑能不能自動放行；null 表示可以。要在 folder 裡、folder 不能是根目錄或家目錄、任何一段都不能是設定／hook／金鑰。
    /// </summary>
    /// <param name="needFolder">true（檔案工具）：沒有 folder 就不放行。false（Bash 參數）：沒有 folder 時只接受不往上跳出的相對路徑。</param>
    public static string? PathProblem(string? folder, string path, string? cwd = null, bool needFolder = true)
    {
        if (path.Length == 0) return "沒有路徑";
        if (path[0] == '~') return "~ 會展開成家目錄";
        PathParts? boundary = null;
        if (folder != null)
        {
            boundary = ParseFolder(folder);
            if (boundary == null) return "看不懂專案資料夾";
            // 專案資料夾就是根目錄或家目錄時，「在裡面」涵蓋開機啟動資料夾、Shima 自己的規則檔…等於沒有限制
            if (Broad(boundary)) return "專案資料夾是根目錄或家目錄";
        }
        else if (needFolder) return "規則沒有限定專案資料夾";
        PathParts? basis = boundary;
        if (cwd is { Length: > 0 } && (basis = ParseFolder(cwd)) == null) return "看不懂工作資料夾";
        if (Parse(path, basis) is not { } p) return "看不懂或可疑的路徑";
        if (p.Escaped) return "相對路徑往上跳出";
        if (boundary == null) { if (p.Root != null) return "沒有限定專案時不接受絕對路徑"; }
        else if (!Contains(boundary, p)) return "在專案資料夾外面";
        if (Sensitive(p.Segments)) return "指到設定、hook 或金鑰";
        return null;
    }

    // ---------------------------------------------------------------- 路徑

    /// <summary>
    /// 正規化後的路徑：Windows（C: 或 \\server\share）或 POSIX；Root 是 null 表示沒有基準的相對路徑；
    /// Escaped 表示相對路徑的 .. 跳到起點之外。
    /// </summary>
    private sealed record PathParts(bool Windows, string? Root, List<string> Segments, bool Escaped);

    private static PathParts? ParseFolder(string path) =>
        Parse(path, null) is { Root: not null, Escaped: false } p ? p : null;

    private static readonly Regex Drive = new(@"^[A-Za-z]:([\\/]|$)", RegexOptions.CultureInvariant);

    /// <summary>
    /// 不碰檔案系統的路徑正規化。Windows 路徑（C:\、C:/、\\server\share）與 POSIX 路徑（/home/…）分開處理：
    /// 絕對不能把 / 開頭的路徑交給 Path.GetFullPath，它會在 Windows 上被當成目前磁碟機的根、硬加上 C:\。
    /// 看起來可疑的寫法（NTFS 資料流 a:b、結尾的點或空白、\\?\ 裝置路徑、C:foo、\foo）直接回傳 null。
    /// </summary>
    private static PathParts? Parse(string path, PathParts? basis)
    {
        if (path.Length == 0) return null;
        foreach (char c in path)
            if (char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format) return null;

        bool windows;
        string? root;
        List<string> segments;
        string rest;
        if (path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/')
        {
            // UNC：\\server\share\…；\\?\ 與 \\.\ 是會跳過正規化的裝置路徑，不接受
            var parts = path[2..].Split('\\', '/');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0 || parts[0] is "?" or ".") return null;
            if (!WindowsName(parts[0]) || !WindowsName(parts[1])) return null;
            windows = true;
            root = (@"\\" + parts[0] + @"\" + parts[1]).ToLowerInvariant();
            segments = [];
            rest = string.Join('\\', parts[2..]);
        }
        else if (Drive.IsMatch(path))
        {
            windows = true;
            root = char.ToLowerInvariant(path[0]) + ":";
            segments = [];
            rest = path[2..];
        }
        else if (path[0] == '/')
        {
            if (path.Contains('\\')) return null;   // POSIX 檔名裡的 \ 太容易被誤會成分隔符，不接受
            windows = false;
            root = "/";
            segments = [];
            rest = path;
        }
        else if (path[0] == '\\' || (path.Length >= 2 && path[1] == ':')) return null;   // \foo（目前磁碟機的根）、C:foo（磁碟機的目前目錄）
        else if (basis != null)
        {
            if (!basis.Windows && path.Contains('\\')) return null;
            windows = basis.Windows;
            root = basis.Root;
            segments = [.. basis.Segments];
            rest = path;
        }
        else
        {
            windows = false;
            root = null;
            segments = [];
            rest = path;
        }

        bool escaped = false;
        foreach (var segment in windows || root == null ? rest.Split('\\', '/') : rest.Split('/'))
        {
            // 全是點的「...」：Windows 會把結尾的點去掉，等於「.」；POSIX 雖然是普通檔名，當成「.」只會更嚴（還是在同一層）
            if (segment.Length == 0 || segment == "." || (segment.Length > 2 && segment.All(c => c == '.'))) continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                else if (root == null) escaped = true;
                continue;
            }
            if (windows && !WindowsName(segment)) return null;
            segments.Add(segment);
        }
        return new PathParts(windows, root, segments, escaped);
    }

    /// <summary>
    /// Windows 的一段名稱：不能有 : （NTFS 資料流 .git::$INDEX_ALLOCATION）、萬用字元或不合法字元，
    /// 也不能以點或空白結尾（Windows 會把結尾的點去掉：.git. 就是 .git）。
    /// </summary>
    private static bool WindowsName(string segment) =>
        segment.IndexOfAny([':', '<', '>', '"', '|', '?', '*']) < 0 && !segment.EndsWith('.') && !segment.EndsWith(' ');

    /// <summary>inner 是不是在 outer 裡面（含相同）：同一種路徑、同一個根、逐段相同。Windows 不分大小寫、POSIX 分。</summary>
    private static bool Contains(PathParts outer, PathParts inner)
    {
        if (outer.Windows != inner.Windows || outer.Root == null || inner.Root == null) return false;
        var comparison = outer.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(outer.Root, inner.Root, comparison) || inner.Segments.Count < outer.Segments.Count) return false;
        for (int i = 0; i < outer.Segments.Count; i++)
            if (!string.Equals(outer.Segments[i], inner.Segments[i], comparison)) return false;
        return true;
    }

    /// <summary>
    /// 太大的資料夾：根目錄、磁碟機根目錄、/mnt/c、家目錄本身（/home/u、/root、/Users/u、C:\Users\u、/mnt/c/Users/u）、系統資料夾。
    /// 以這種資料夾當專案時，「在裡面」等於沒有限制，所以不放行任何路徑（專案的子資料夾照常可以）。
    /// </summary>
    private static bool Broad(PathParts folder)
    {
        var s = folder.Segments.Select(x => x.ToLowerInvariant()).ToList();
        if (s.Count == 0) return true;
        if (folder.Windows)
            return (s[0] == "users" && s.Count <= 2) || s[0] is "windows" or "program files" or "program files (x86)" or "programdata";
        if (s[0] == "mnt") return s.Count <= 2 || (s[2] == "users" && s.Count <= 4);
        if (s[0] is "home" or "users") return s.Count <= 2;
        return s.Count == 1 && s[0] is "root" or "etc" or "usr" or "bin" or "sbin" or "lib" or "var" or "opt" or "tmp" or "boot" or "srv";
    }

    private static readonly Regex ShortName = new(@"^([^~]{1,6})~\d+(\.[^.]*)?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 路徑裡有沒有任何一段是敏感的資料夾或檔名（不分大小寫：WSL 的 /mnt/c 也是不分大小寫的 NTFS）。
    /// 也擋 8.3 短檔名（GIT~1 就是 .git、CLAUDE~1 就是 .claude）與 POSIX 這邊結尾多了點的寫法。
    /// </summary>
    private static bool Sensitive(IEnumerable<string> segments)
    {
        foreach (var segment in segments)
        {
            var name = segment.TrimEnd('.', ' ').ToLowerInvariant();
            if (name.Length == 0) continue;
            if (SensitiveFolders.Contains(name) || SensitiveFiles.Contains(name) || name.StartsWith(".env.")) return true;
            if (ShortName.Match(name) is { Success: true } m)
            {
                var stem = Squash(m.Groups[1].Value);
                if (stem.Length > 0 && SensitiveFolders.Concat(SensitiveFiles).Any(s => Squash(s).StartsWith(stem))) return true;
            }
        }
        return false;
    }

    private static string Squash(string name) => name.Replace(".", "").Replace(" ", "").ToLowerInvariant();
}

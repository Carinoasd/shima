// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>「以後都允許」規則的安全邊界：寧可多問，不可放過。</summary>
public class AgentRulesSecurityTests
{
    private const string Win = @"C:\work\app";
    private const string Wsl = "/home/someone/proj";

    private static JsonNode Bash(string command) => new JsonObject { ["command"] = command };
    private static JsonNode File(string path, string key = "file_path") => new JsonObject { [key] = path, ["content"] = "x" };

    private static bool BashCovers(string rule, string command, string? cwd = Wsl) =>
        AgentRules.Covers(new AgentRule("Bash", rule, cwd), "Bash", Bash(command), cwd);

    // ---------------------------------------------------------------- A. 大小寫與副檔名

    [Theory]
    [InlineData("Python.exe x.py")]
    [InlineData("python.exe -c x")]
    [InlineData("bash.exe -c ls")]
    [InlineData("CMD.EXE /c dir")]
    [InlineData("PowerShell.exe -c x")]
    [InlineData("node.exe x.js")]
    [InlineData("tar.exe -xf a.tar")]
    [InlineData("find.exe . -name x")]
    [InlineData("python3.12 x.py")]
    [InlineData("node18 x.js")]
    [InlineData("bash. -c ls")]
    [InlineData("sh.exe.exe x")]
    [InlineData("pwsh x")]
    [InlineData("cscript x.vbs")]
    [InlineData("mshta x")]
    [InlineData("rundll32 x")]
    [InlineData("curl -o x http")]
    [InlineData("wsl ls")]
    [InlineData("claude -p x")]
    [InlineData("certutil x")]
    public void 換個大小寫或副檔名的直譯器也不給記(string command)
    {
        Assert.Null(AgentRules.Suggest("Bash", Bash(command), Wsl));
        var first = command.Split(' ')[0];
        Assert.False(BashCovers(first, command));
    }

    [Theory]
    [InlineData("git.exe status", "git status")]
    [InlineData("GIT status", "git status")]
    [InlineData("Git.Exe status -s", "git status")]
    [InlineData("npm.cmd run build", "npm run")]
    [InlineData("dotnet.exe test", "dotnet test")]
    public void 副檔名與大小寫不同的寫法跟原本的指令互通(string command, string label)
    {
        Assert.Equal(label, AgentRules.Suggest("Bash", Bash(command), Wsl)!.Label);
        Assert.True(BashCovers(label, command));
        Assert.True(BashCovers(label, label));
        Assert.True(BashCovers(command.Split(' ')[0] + " " + label.Split(' ')[1], label));   // 舊格式的規則也通
    }

    [Theory]
    [InlineData("git.exe")]
    [InlineData("GIT")]
    [InlineData("npm.cmd")]
    [InlineData("dotnet.exe")]
    [InlineData("pip3.11")]
    public void 需要子指令的指令換個寫法也不能只記一個字(string command)
    {
        Assert.Null(AgentRules.Suggest("Bash", Bash(command + " --version"), Wsl));
        Assert.False(BashCovers(command, command + " --version"));
    }

    [Theory]
    [InlineData("git config core.pager x", "git config")]
    [InlineData("git submodule foreach rm", "git submodule")]
    [InlineData("git bisect run x", "git bisect")]
    [InlineData("uv run sh", "uv run")]
    [InlineData("npm exec x", "npm exec")]
    [InlineData("docker run x", "docker run")]
    [InlineData("go generate", "go generate")]
    public void 子指令本身會執行任意東西的不給記(string command, string rule)
    {
        Assert.Null(AgentRules.Suggest("Bash", Bash(command), Wsl));
        Assert.False(BashCovers(rule, command));
    }

    [Fact]
    public void 舊規則讀進來先正規化_變危險的丟掉()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-rules-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            System.IO.File.WriteAllText(path, """
                [{"tool":"Bash","command":"Git.exe status"},{"tool":"Bash","command":"git status"},
                 {"tool":"Bash","command":"Python.exe"},{"tool":"Bash","command":"git.exe"},{"tool":"Bash","command":"CMD.EXE"},
                 {"tool":"Bash","command":"git config"},{"tool":"Edit","cwd":"C:\\work\\app"}]
                """);
            var rules = new AgentRules(path);
            Assert.Equal([new AgentRule("Bash", "git status"), new AgentRule("Edit", null, Win)], rules.List());
            Assert.True(rules.Add(new AgentRule("Bash", "NPM.cmd run")));
            Assert.Contains(new AgentRule("Bash", "npm run"), new AgentRules(path).List());
            Assert.True(rules.Remove(new AgentRule("Bash", "npm run")));
        }
        finally { System.IO.File.Delete(path); }
    }

    // ---------------------------------------------------------------- B. 記住的指令後面的參數

    [Theory]
    [InlineData("git diff")]
    [InlineData("git diff HEAD~1")]
    [InlineData("git diff --stat src/a.cs")]
    [InlineData("git diff main..feature")]
    [InlineData("git diff ./src/../src/a.cs")]
    [InlineData("git diff /home/someone/proj/src/a.cs")]
    public void 記住_git_diff_之後無害的參數放行(string command) => Assert.True(BashCovers("git diff", command));

    [Theory]
    [InlineData("git diff --output=x")]
    [InlineData("git diff --output x")]
    [InlineData("git diff --outp x")]
    [InlineData("git diff -o x")]
    [InlineData("git -c core.pager=x diff")]
    [InlineData("git diff --ext-diff")]
    [InlineData("git diff ../other/a.cs")]
    [InlineData("git diff /etc/passwd")]
    [InlineData("git diff ~/x")]
    [InlineData("git diff .env")]
    [InlineData("git diff .git/config")]
    [InlineData("git diff @args.rsp")]
    [InlineData("git diff .*")]
    [InlineData("git diff GIT~1/config")]
    public void 記住_git_diff_之後危險的參數不放行(string command) => Assert.False(BashCovers("git diff", command));

    [Theory]
    [InlineData("git fetch", "git fetch --upload-pack=x")]
    [InlineData("git fetch", "git fetch --upload-pack x")]
    [InlineData("git fetch", "git fetch --upload x")]
    [InlineData("git ls-remote", "git ls-remote --upload-pack x")]
    [InlineData("git clone", "git clone --config x y")]
    [InlineData("git clone", "git clone -u x y")]
    [InlineData("git rebase", "git rebase -x evil main")]
    [InlineData("git rebase", "git rebase --exec evil main")]
    [InlineData("go test", "go test -exec=x ./...")]
    [InlineData("go test", "go test -exec x")]
    [InlineData("go test", "go test --exec x")]
    [InlineData("go build", "go build -toolexec x")]
    [InlineData("go build", "go build -overlay o.json")]
    [InlineData("make test", "make test SHELL=/bin/evil")]
    [InlineData("make test", "make test -f evil.mk")]
    [InlineData("make test", "make test --eval x")]
    [InlineData("npm run", "npm run x --script-shell sh")]
    [InlineData("npm run", "npm run x --script-shell=sh")]
    [InlineData("dotnet test", "dotnet test --settings x.runsettings")]
    [InlineData("dotnet test", "dotnet test -p:VSTestCLIRunSettings")]
    [InlineData("dotnet test", "dotnet test --logger x")]
    [InlineData("dotnet build", "dotnet build @x.rsp")]
    [InlineData("cargo build", "cargo build --config x")]
    [InlineData("ls", "ls --ok x")]
    public void 記住的指令後面接能執行東西或寫檔的旗標不放行(string rule, string command)
    {
        Assert.False(BashCovers(rule, command));
        Assert.Null(AgentRules.Suggest("Bash", Bash(command), Wsl));
    }

    [Theory]
    [InlineData("go test", "go test ./...")]
    [InlineData("go test", "go test -run TestX ./...")]
    [InlineData("dotnet test", "dotnet test --configuration Release")]
    [InlineData("cargo build", "cargo build --release")]
    [InlineData("npm run", "npm run build")]
    [InlineData("git log", "git log --oneline -20")]
    public void 常見無害的參數照常放行(string rule, string command) => Assert.True(BashCovers(rule, command));

    [Theory]
    [InlineData("--output=x")]
    [InlineData("--upload-pack")]
    [InlineData("-exec")]
    [InlineData("SHELL=x")]
    [InlineData("../x")]
    [InlineData("/etc")]
    public void 不放行的參數會說出原因(string arg) => Assert.NotNull(AgentRules.ArgumentProblem("git", [arg], Wsl));

    [Fact]
    public void 沒有限定專案的規則_參數不能是絕對路徑或往上跳()
    {
        Assert.True(BashCovers("ls", "ls -la src", null));
        Assert.False(BashCovers("ls", "ls /etc", null));
        Assert.False(BashCovers("ls", "ls ../x", null));
        Assert.True(BashCovers("ls", "ls a/../b", null));
    }

    [Fact]
    public void 專案是家目錄時_參數裡的路徑不放行()
    {
        Assert.False(BashCovers("cat", "cat notes.txt", "/home/someone"));
        Assert.True(BashCovers("ls", "ls", "/home/someone"));
    }

    // ---------------------------------------------------------------- C. 檔案工具的路徑

    private static bool EditCovers(string path, string cwd = Win, string tool = "Edit", string key = "file_path") =>
        AgentRules.Covers(new AgentRule(tool, null, cwd), tool, File(path, key), cwd);

    [Theory]
    [InlineData(@"C:\work\app\src\a.cs")]
    [InlineData("c:/WORK/app/src/a.cs")]
    [InlineData(@"src\a.cs")]
    [InlineData("src/a.cs")]
    [InlineData(@"C:\work\app\src\..\src\.\a.cs")]
    [InlineData(@"C:/work\app/src\a.cs")]
    [InlineData(@"C:\work\app\.gitignore")]
    public void 記住_Edit_後專案裡的檔案放行(string path) => Assert.True(EditCovers(path));

    [Theory]
    [InlineData(@"C:\work\app-secret\a.cs")]
    [InlineData(@"C:\work\app\..\other\a.cs")]
    [InlineData(@"C:\work\app\..\..\Users\someone\.claude\settings.json")]
    [InlineData(@"C:\Users\someone\.claude\settings.json")]
    [InlineData(@"C:\work\app\.claude\settings.json")]
    [InlineData(@"C:\work\app\.git\hooks\pre-commit")]
    [InlineData(@"C:\work\app\.GIT\hooks\pre-commit")]
    [InlineData(@"C:\work\app\.git.\hooks\pre-commit")]
    [InlineData(@"C:\work\app\GIT~1\hooks\pre-commit")]
    [InlineData(@"C:\work\app\.git::$INDEX_ALLOCATION\hooks\x")]
    [InlineData(@"C:\work\app\a.cs:stream")]
    [InlineData(@"C:\work\app\.env")]
    [InlineData(@"C:\work\app\.env.local")]
    [InlineData(@"C:\work\app\.vscode\tasks.json")]
    [InlineData(@"C:\work\app\.github\workflows\ci.yml")]
    [InlineData(@"C:\work\app\.husky\pre-commit")]
    [InlineData(@"C:\work\app\.npmrc")]
    [InlineData(@"C:\work\app\.mcp.json")]
    [InlineData(@"C:\work\app\Directory.Build.props")]
    [InlineData(@"\\?\C:\work\app\a.cs")]
    [InlineData(@"\\.\C:\work\app\a.cs")]
    [InlineData(@"C:a.cs")]
    [InlineData(@"\work\app\a.cs")]
    [InlineData(@"..\other\a.cs")]
    [InlineData("~/a.cs")]
    [InlineData("/home/someone/proj/a.cs")]
    [InlineData(@"\\server\share\a.cs")]
    [InlineData("")]
    public void 記住_Edit_後專案外或設定檔不放行(string path) => Assert.False(EditCovers(path));

    [Fact]
    public void 沒有目標路徑或路徑不是字串的檔案工具請求不放行()
    {
        var rule = new AgentRule("Edit", null, Win);
        Assert.False(AgentRules.Covers(rule, "Edit", new JsonObject(), Win));
        Assert.False(AgentRules.Covers(rule, "Edit", new JsonObject { ["file_path"] = 3 }, Win));
        Assert.False(AgentRules.Covers(rule, "Edit", null, Win));
        Assert.False(AgentRules.Covers(new AgentRule("Edit"), "Edit", File(@"C:\work\app\a.cs"), Win));   // 規則沒限定專案
        Assert.Null(AgentRules.Suggest("Edit", new JsonObject(), Win));
        Assert.Null(AgentRules.Suggest("Edit", File(@"C:\work\app\a.cs"), null));
    }

    [Theory]
    [InlineData("/home/someone/proj/src/a.py", true)]
    [InlineData("/home/someone/proj/./src/../src/a.py", true)]
    [InlineData("/home/someone/proj//src///a.py", true)]
    [InlineData("src/a.py", true)]
    [InlineData("/home/someone/proj2/a.py", false)]
    [InlineData("/home/someone/proj/../.claude/settings.json", false)]
    [InlineData("/home/someone/.claude/settings.json", false)]
    [InlineData("/home/someone/PROJ/a.py", false)]
    [InlineData("/home/someone/proj/.Claude/settings.json", false)]
    [InlineData("/home/someone/proj/.git./config", false)]
    [InlineData(@"C:\work\app\a.py", false)]
    [InlineData(@"/home/someone/proj/a\..\..\x", false)]
    [InlineData("src/../../proj2/a.py", false)]
    public void WSL_的路徑自己正規化_分大小寫(string path, bool allowed) => Assert.Equal(allowed, EditCovers(path, Wsl));

    [Theory]
    [InlineData("Write", "file_path", "src/new.cs", true)]
    [InlineData("Write", "file_path", @"C:\Users\someone\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\x.bat", false)]
    [InlineData("MultiEdit", "file_path", @"C:\work\app\src\a.cs", true)]
    [InlineData("MultiEdit", "file_path", @"C:\work\app\.git\config", false)]
    [InlineData("NotebookEdit", "notebook_path", @"C:\work\app\nb\a.ipynb", true)]
    [InlineData("NotebookEdit", "notebook_path", @"C:\work\other\a.ipynb", false)]
    [InlineData("NotebookEdit", "file_path", @"C:\work\app\nb\a.ipynb", true)]
    [InlineData("Read", "file_path", @"C:\work\app\README.md", true)]
    [InlineData("Read", "file_path", @"C:\Users\someone\.ssh\id_ed25519", false)]
    [InlineData("Glob", "path", @"C:\work\app\src", true)]
    [InlineData("Glob", "path", @"C:\work", false)]
    [InlineData("Grep", "path", "src", true)]
    [InlineData("Grep", "path", "..", false)]
    public void 其他檔案工具也檢查路徑(string tool, string key, string path, bool allowed) =>
        Assert.Equal(allowed, EditCovers(path, Win, tool, key));

    [Fact]
    public void Glob_的樣式也不能跳出去()
    {
        var rule = new AgentRule("Glob", null, Win);
        Assert.True(AgentRules.Covers(rule, "Glob", new JsonObject { ["pattern"] = "**/*.cs" }, Win));
        Assert.False(AgentRules.Covers(rule, "Glob", new JsonObject { ["pattern"] = "../**/*.cs" }, Win));
        Assert.False(AgentRules.Covers(rule, "Glob", new JsonObject { ["pattern"] = "/etc/*" }, Win));
        Assert.False(AgentRules.Covers(rule, "Glob", new JsonObject { ["pattern"] = "**/.env" }, Win));
        var home = new AgentRule("Glob", null, @"C:\Users\someone");   // 沒給 path 就是在 cwd 找：家目錄太大
        Assert.False(AgentRules.Covers(home, "Glob", new JsonObject { ["pattern"] = "**/*.cs" }, @"C:\Users\someone"));
    }

    [Fact]
    public void NotebookEdit_的兩個路徑欄位都要在專案裡()
    {
        var input = new JsonObject { ["notebook_path"] = @"C:\work\app\a.ipynb", ["file_path"] = @"C:\other\x" };
        Assert.False(AgentRules.Covers(new AgentRule("NotebookEdit", null, Win), "NotebookEdit", input, Win));
    }

    [Fact]
    public void 專案是家目錄或根目錄時檔案工具不放行()
    {
        Assert.False(EditCovers(@"C:\Users\someone\a.txt", @"C:\Users\someone"));
        Assert.False(EditCovers(@"C:\a.txt", @"C:\"));
        Assert.False(EditCovers("/home/someone/a.txt", "/home/someone"));
        Assert.False(EditCovers("/mnt/c/a.txt", "/mnt/c"));
        Assert.False(EditCovers("/mnt/c/Users/someone/a.txt", "/mnt/c/Users/someone"));
        Assert.True(EditCovers(@"C:\Users\someone\proj\a.txt", @"C:\Users\someone\proj"));
    }

    [Fact]
    public void 不認得的工具帶著路徑或網址就不給記()
    {
        Assert.Null(AgentRules.Suggest("WebFetch", new JsonObject { ["url"] = "https://example.com", ["prompt"] = "x" }, Win));
        Assert.Null(AgentRules.Suggest("mcp__fs__write", new JsonObject { ["target"] = @"C:\x" }, Win));
        Assert.Null(AgentRules.Suggest("mcp__fs__write", new JsonObject { ["opts"] = new JsonObject { ["dest"] = "~/x" } }, Win));
        Assert.Null(AgentRules.Suggest("mcp__fs__write", new JsonObject { ["outputFile"] = "x" }, Win));
        Assert.False(AgentRules.Covers(new AgentRule("WebFetch", null, Win), "WebFetch", new JsonObject { ["url"] = "https://example.com" }, Win));
        // 沒有路徑性質的照舊
        Assert.Equal(new AgentRule("mcp__db__count", null, Win), AgentRules.Suggest("mcp__db__count", new JsonObject { ["table"] = "users" }, Win));
        Assert.NotNull(AgentRules.Suggest("WebSearch", new JsonObject { ["query"] = "site:example.com/a" }, Win));
    }

    [Theory]
    [InlineData("Edit", @"C:\work\app\src\a.cs")]
    [InlineData("Edit", @"C:\work\app\.env")]
    [InlineData("Write", @"C:\elsewhere\a.cs")]
    [InlineData("Read", @"src\a.cs")]
    public void 給出來的規則一定放行這個請求_不放行的就不給(string tool, string path)
    {
        var input = File(path);
        var rule = AgentRules.Suggest(tool, input, Win);
        if (rule != null) Assert.True(AgentRules.Covers(rule, tool, input, Win));
        Assert.Equal(rule != null, AgentRules.PathProblem(Win, path, Win) == null);
    }

    [Theory]
    [InlineData("git diff --output x")]
    [InlineData("git status")]
    [InlineData("ls ../x")]
    [InlineData("go test -exec x")]
    public void Bash_給出來的規則也跟_Covers_一致(string command)
    {
        var rule = AgentRules.Suggest("Bash", Bash(command), Wsl);
        Assert.Equal(rule != null, rule != null && AgentRules.Covers(rule, "Bash", Bash(command), Wsl));
        Assert.Equal(command == "git status", rule != null);
    }

    // ---------------------------------------------------------------- 路徑函式本身

    [Theory]
    [InlineData(@"C:\work\app", @"C:\work\app", true)]
    [InlineData(@"C:\work\app", @"C:\work\app\x", true)]
    [InlineData(@"C:\work\app", @"C:\work\app-secret\x", false)]
    [InlineData(@"C:\work\app\", @"c:/work/APP/x", true)]
    [InlineData(@"C:\work\app", @"D:\work\app\x", false)]
    [InlineData(@"C:\work\app", @"C:\work\app\..\..\.claude\settings.json", false)]
    [InlineData("/home/someone/proj", "/home/someone/proj2/x", false)]
    [InlineData("/home/someone/proj", "/home/someone/proj/x", true)]
    [InlineData("/home/someone/proj", "/home/someone/Proj/x", false)]
    [InlineData("/home/someone/proj", @"C:\home\someone\proj\x", false)]
    [InlineData(@"C:\home\someone\proj", "/home/someone/proj/x", false)]
    [InlineData(@"\\server\share\app", @"\\SERVER\share\app\x", true)]
    [InlineData(@"\\server\share\app", @"\\server\other\app\x", false)]
    [InlineData("relative", "relative/x", false)]
    public void Inside_比的是資料夾邊界(string folder, string path, bool inside) => Assert.Equal(inside, AgentRules.Inside(folder, path));

    [Fact]
    public void Inside_的相對路徑以_cwd_為基準()
    {
        Assert.True(AgentRules.Inside(Win, @"src\a.cs", Win));
        Assert.True(AgentRules.Inside(Win, "a.cs", @"C:\work\app\src"));
        Assert.False(AgentRules.Inside(Win, "a.cs", @"C:\work"));
        Assert.False(AgentRules.Inside(Wsl, "../x", Wsl));
    }

    [Theory]
    [InlineData("/home/someone/proj", "/home/someone/proj/./", true)]
    [InlineData("/home/someone/proj", "/home/someone/x/../proj", true)]
    [InlineData("/home/someone/a:b", "/home/someone/A:B", false)]   // 以前看到 : 就當成 Windows 路徑、不分大小寫
    [InlineData(@"C:\work\app", "C:/work/app", true)]
    [InlineData("/home/someone/proj", @"C:\home\someone\proj", false)]
    [InlineData("relative", "relative", false)]
    public void SameFolder_也用同一套正規化(string a, string b, bool same) => Assert.Equal(same, AgentRules.SameFolder(a, b));

    // ---------------------------------------------------------------- 第二輪審查找到的繞過

    [Theory]
    [InlineData("ls -la src")]
    [InlineData("ls -rt")]
    [InlineData("ls -1 src/a")]
    public void 純英數的合併短旗標照常放行(string command) => Assert.True(BashCovers("ls", command));

    [Theory]
    [InlineData("ls -rt/etc payload")]            // -r 加 -t /etc：第二個字母黏在路徑前面
    [InlineData("ls -rt../../etc payload")]
    [InlineData("ls -rf/etc/passwd .")]
    [InlineData("ls -rf.env .")]
    [InlineData("ls -o../x")]
    [InlineData("ls -I/x")]
    [InlineData("ls -a~/x")]
    public void 短旗標黏著路徑的一律問人(string command) => Assert.False(BashCovers("ls", command));

    [Theory]
    [InlineData(@"C:/work/app/src/a.cs", true)]              // 專案裡的 Windows 路徑：不能被冒號切壞
    [InlineData(@"c:\work\app\src\a.cs", true)]
    [InlineData(@"D:secret.txt", false)]                      // 磁碟機相對路徑
    [InlineData(@"C:secret.txt", false)]
    [InlineData(@"a.txt:stream", false)]                      // NTFS 替代資料流
    [InlineData(@"C:/work/app-secret/a.cs", false)]
    [InlineData(@"C:/work/app/.env", false)]
    public void Bash參數裡的Windows路徑整段檢查_不先用冒號切開(string argument, bool allowed)
    {
        // 測試不能含反斜線的指令（那會被當成跳脫），所以反斜線的那一筆直接測參數檢查
        Assert.Equal(allowed, AgentRules.ArgumentProblem("ls", [argument], Win, Win) == null);
    }

    [Theory]
    [InlineData("ls HEAD:.env")]
    [InlineData("ls a:../../x")]
    [InlineData("ls --logger:/etc/x")]
    public void 冒號後面的每一段也要檢查(string command) => Assert.False(BashCovers("ls", command));

    [Fact]
    public void 冒號前面的旗標字母才算旗標()
    {
        // dotnet 的 -l 是危險的（logger），但 -v:normal 裡的 l 是值的一部分。黏著東西的短旗標本來就會問，
        // 這裡確認的是原因不是「危險的 -l」
        var why = AgentRules.ArgumentProblem("dotnet", ["-v:normal"], Wsl, Wsl);
        Assert.NotNull(why);
        Assert.DoesNotContain("危險", why);
    }

    [Theory]
    [InlineData("**/.env*")]
    [InlineData(".*")]
    [InlineData(".?nv")]
    [InlineData(".[e]nv")]
    [InlineData("{.env,.git}/**")]
    [InlineData("src/{a,.ssh}/x")]
    [InlineData("**/.e*")]
    public void 會比對到隱藏檔或敏感名稱的樣式不放行(string pattern)
    {
        foreach (var (tool, key) in new[] { ("Glob", "pattern"), ("Grep", "glob") })
        {
            var input = new JsonObject { [key] = pattern, ["pattern"] = tool == "Grep" ? "x" : pattern };
            Assert.False(AgentRules.Covers(new AgentRule(tool, null, Wsl), tool, input, Wsl), $"{tool} {pattern}");
        }
    }

    [Theory]
    [InlineData("**/*.cs")]
    [InlineData("src/{a,b}/*.ts")]
    [InlineData("docs/**")]
    public void 一般的樣式照常放行(string pattern) =>
        Assert.True(AgentRules.Covers(new AgentRule("Glob", null, Wsl), "Glob", new JsonObject { ["pattern"] = pattern }, Wsl));
}

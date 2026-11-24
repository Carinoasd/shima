// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>橋接的安全性：deck 金鑰只在用 Stream Dock 時有效。</summary>
public class DeckDoorTests
{
    private const string Key = "deck-token-0123456789abcdef0123456789abcdef";

    [Fact]
    public void 沒在用_Stream_Dock_就沒有金鑰()
    {
        int asked = 0;
        var door = new DeckDoor(() => false, () => { asked++; return Key; }, () => 0);
        Assert.Equal("", door.Key);
        Assert.Equal(0, asked);   // 連金鑰檔都不碰
        Assert.Equal(AgentBridge.Door.Refuse, AgentBridge.Gate("GET", "/deck/now", null, "", false, "hook", door.Key));
        Assert.Equal(AgentBridge.Door.Refuse, AgentBridge.Gate("GET", "/deck/now", null, null, false, "hook", door.Key));
    }

    [Fact]
    public void 結果記一陣子_Forget_之後馬上重查()
    {
        long now = 0;
        bool inUse = false;
        int checks = 0;
        var door = new DeckDoor(() => { checks++; return inUse; }, () => Key, () => now);
        Assert.Equal("", door.Key);
        inUse = true;   // 之後才裝 Stream Dock
        now = DeckDoor.Remember - 1;
        Assert.Equal("", door.Key);
        Assert.Equal(1, checks);   // 記住的期間不再摸磁碟
        now = DeckDoor.Remember;
        Assert.Equal(Key, door.Key);
        Assert.Equal(AgentBridge.Door.Deck, AgentBridge.Gate("GET", "/deck/now", null, Key, true, "hook", door.Key));

        inUse = false;   // 關掉 Stream Dock 開關（移除外掛）
        door.Forget();
        Assert.Equal("", door.Key);
        Assert.Equal(3, checks);
    }
}

/// <summary>Stream Dock 的允許／拒絕只認外掛畫出來的那一則。</summary>
public class DeckAnswerTests
{
    private const string Current = "A1B2C3D4E5F60718", Other = "0000000000000000";
    private const long Fresh = DeckAnswer.Glance - 1, Seen = DeckAnswer.Glance;

    [Theory]
    // 識別碼對
    [InlineData("allow", Current, Seen, true, DeckVerdict.Apply)]
    [InlineData("allow", Current, Fresh, true, DeckVerdict.Ignore)]      // 剛出現：使用者不可能已經看完
    [InlineData("allow", Current, Seen, false, DeckVerdict.Ignore)]      // 設定關掉
    [InlineData("allow", Current, Fresh, false, DeckVerdict.Ignore)]
    [InlineData("deny", Current, Seen, true, DeckVerdict.Apply)]
    [InlineData("deny", Current, Fresh, true, DeckVerdict.Apply)]        // 拒絕不會造成傷害，不用等
    [InlineData("deny", Current, Seen, false, DeckVerdict.Apply)]
    [InlineData("deny", Current, Fresh, false, DeckVerdict.Apply)]
    // 識別碼錯（外掛畫的是上一則）
    [InlineData("allow", Other, Seen, true, DeckVerdict.Ignore)]
    [InlineData("allow", Other, Fresh, true, DeckVerdict.Ignore)]
    [InlineData("allow", Other, Seen, false, DeckVerdict.Ignore)]
    [InlineData("allow", Other, Fresh, false, DeckVerdict.Ignore)]
    [InlineData("deny", Other, Seen, true, DeckVerdict.Ignore)]
    [InlineData("deny", Other, Fresh, true, DeckVerdict.Ignore)]
    [InlineData("deny", Other, Seen, false, DeckVerdict.Ignore)]
    [InlineData("deny", Other, Fresh, false, DeckVerdict.Ignore)]
    [InlineData("allow", "-", Seen, true, DeckVerdict.Ignore)]           // 新版外掛還沒畫出任何一則
    // 沒帶（舊版外掛）
    [InlineData("allow", null, Seen, true, DeckVerdict.OldPlugin)]
    [InlineData("allow", null, Fresh, true, DeckVerdict.OldPlugin)]
    [InlineData("allow", "", Seen, true, DeckVerdict.OldPlugin)]
    [InlineData("allow", null, Seen, false, DeckVerdict.Ignore)]
    [InlineData("allow", null, Fresh, false, DeckVerdict.Ignore)]
    [InlineData("deny", null, Seen, true, DeckVerdict.Apply)]
    [InlineData("deny", null, Fresh, true, DeckVerdict.Apply)]
    [InlineData("deny", null, Seen, false, DeckVerdict.Apply)]
    [InlineData("deny", null, Fresh, false, DeckVerdict.Apply)]
    [InlineData("deny", "", Seen, true, DeckVerdict.Apply)]
    public void 允許要指名而且看過了_拒絕照舊(string action, string? id, long shownFor, bool allowOn, DeckVerdict expected) =>
        Assert.Equal(expected, DeckAnswer.Judge(action, id, Current, shownFor, allowOn));

    [Theory]
    [InlineData("allow", Current)]
    [InlineData("allow", null)]
    [InlineData("deny", Current)]
    [InlineData("deny", null)]
    public void 沒有在問的時候什麼都不做(string action, string? id) =>
        Assert.Equal(DeckVerdict.Ignore, DeckAnswer.Judge(action, id, null, 10_000, true));

    [Fact]
    public void 別的按鍵不會變成允許() =>
        Assert.Equal(DeckVerdict.Ignore, DeckAnswer.Judge("playpause", Current, Current, 10_000, true));

    [Fact]
    public void 每一則請求的識別碼都不一樣()
    {
        PermissionRequest Ask() => new() { Project = "p", Tool = "Bash", Details = "ls" };
        var a = Ask();
        var b = Ask();
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(16, a.Id.Length);
        Assert.Equal(a.Id, a.Id);
    }

    [Fact]
    public void 通知帶著識別碼()
    {
        var notice = new DeckNotice("ask", "Claude Code", "Bash  ls", "", "", false, Current);
        Assert.Equal(Current, notice.Id);
        Assert.Equal("", DeckNotice.None.Id);
    }
}

/// <summary>hook 送來的 transcript_path 只收 Claude Code 自己的對話紀錄。</summary>
public class TranscriptPathTests
{
    [Theory]
    [InlineData(@"C:\Users\someone\.claude\projects\C--src-alpha\0b1c.jsonl")]
    [InlineData(@"c:/Users/someone/.claude/projects/C--src-alpha/0b1c.jsonl")]
    [InlineData(@"D:\home\.claude\projects\x\y\z.JSONL")]
    [InlineData("/home/someone/.claude/projects/-home-someone-alpha/0b1c.jsonl")]
    [InlineData("/mnt/c/Users/someone/.claude/projects/x/0b1c.jsonl")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\someone\.claude\projects\x\0b1c.jsonl")]
    [InlineData(@"\\wsl$\Ubuntu-24.04\home\someone\.claude\projects\x\0b1c.jsonl")]
    public void 收(string path) => Assert.True(AgentSessions.TranscriptOk(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\attacker.example\s\x.jsonl")]                                   // 外面的 SMB
    [InlineData(@"\\attacker.example\s\.claude\projects\x\y.jsonl")]
    [InlineData("//attacker.example/s/.claude/projects/x/y.jsonl")]               // 斜線寫的 UNC
    [InlineData(@"\\?\UNC\attacker.example\s\.claude\projects\x\y.jsonl")]
    [InlineData(@"\\?\C:\Users\someone\.claude\projects\x\y.jsonl")]
    [InlineData(@"\\.\C:\Users\someone\.claude\projects\x\y.jsonl")]                // 裝置路徑
    [InlineData(@"\\.\pipe\.claude\projects\x\y.jsonl")]
    [InlineData(@"C:\Users\someone\.claude\projects\x\CON.jsonl")]
    [InlineData(@"C:\Users\someone\.claude\projects\x\..\..\..\secret.jsonl")]       // 繞出去
    [InlineData(@"C:\Users\someone\.claude\projects\..\..\Documents\notes.jsonl")]
    [InlineData(@"C:\Users\someone\.claude\projects\x\.. \y.jsonl")]
    [InlineData("/home/someone/.claude/projects/../../../etc/shadow.jsonl")]
    [InlineData("/home/someone/.claude/projects/./x/./y.jsonl")]
    [InlineData(@"\\wsl.localhost\..\C$\.claude\projects\x\y.jsonl")]
    [InlineData(@"C:\Users\someone\.claude\projects\x\y.txt")]                    // 不是 .jsonl
    [InlineData(@"C:\Users\someone\.ssh\id_ed25519")]
    [InlineData(@"C:\Users\someone\.claude\projects\x\y.jsonl:secret")]            // 替代資料流
    [InlineData(@"C:\Users\someone\.claude\projects\x\y.jsonl.")]
    [InlineData(@"C:\Users\someone\.claude\x\y.jsonl")]                           // 不在 projects 底下
    [InlineData(@"C:\Users\someone\.claude\projects\y.jsonl")]                    // 至少要有一層專案資料夾
    [InlineData(@"C:\Users\someone\Documents\y.jsonl")]
    [InlineData(@"Users\someone\.claude\projects\x\y.jsonl")]                     // 相對路徑
    [InlineData(@"C:Users\someone\.claude\projects\x\y.jsonl")]
    [InlineData("home/someone/.claude/projects/x/y.jsonl")]
    [InlineData("/home/someone/.claude/projects/x\\..\\y.jsonl")]
    [InlineData("C:\\Users\\someone\\.claude\\projects\\x\\y\n.jsonl")]
    public void 不收(string? path) => Assert.False(AgentSessions.TranscriptOk(path));

    [Fact]
    public void 不收的路徑當成沒有_之前收過的也不會被蓋掉()
    {
        var s = new AgentSessions();
        JsonObject Hook(string transcript) => new() { ["session_id"] = "a", ["hook_event_name"] = "UserPromptSubmit", ["cwd"] = @"C:\src\alpha", ["transcript_path"] = transcript };
        s.Apply(Hook(@"\\attacker.example\s\x.jsonl"), out _);
        Assert.Equal("", s.Find("a")!.TranscriptPath);
        s.Apply(Hook(@"C:\Users\someone\.claude\projects\x\a.jsonl"), out _);
        s.Apply(Hook(@"\\attacker.example\s\x.jsonl"), out _);
        Assert.Equal(@"C:\Users\someone\.claude\projects\x\a.jsonl", s.Find("a")!.TranscriptPath);
    }

    [Fact]
    public void Codex_送來的也一樣擋()
    {
        var bridge = new AgentBridge(new AgentRules(Path.Combine(Path.GetTempPath(), "shima-sec-" + Guid.NewGuid().ToString("N") + ".json")));
        bridge.Notify(new JsonObject { ["session_id"] = "s1", ["hook_event_name"] = "SessionStart", ["cwd"] = @"C:\src\alpha", ["transcript_path"] = @"\\attacker.example\s\x.jsonl" }, AgentKind.Codex);
        Assert.Equal("", Assert.Single(bridge.Sessions).TranscriptPath);
    }
}

/// <summary>權限請求與連線都有上限。</summary>
public class BridgeLimitTests
{
    private static JsonObject Permission(int n) => new()
    {
        ["session_id"] = "s" + n, ["hook_event_name"] = "PermissionRequest", ["cwd"] = @"C:\src\alpha",
        ["tool_name"] = "Bash", ["tool_input"] = new JsonObject { ["command"] = "echo " + n },
    };

    [Fact]
    public async Task 每個_agent_最多八則在等_再來的回終端機()
    {
        var bridge = new AgentBridge(new AgentRules(Path.Combine(Path.GetTempPath(), "shima-sec-" + Guid.NewGuid().ToString("N") + ".json")));
        var asked = new List<PermissionRequest>();
        bridge.Asked += ask => { lock (asked) asked.Add(ask); };

        var pending = Enumerable.Range(0, AgentBridge.MaxWaiting).Select(i => bridge.PermissionAsync(Permission(i))).ToList();
        Assert.Equal(AgentBridge.MaxWaiting, bridge.Waiting(AgentKind.Claude));
        Assert.All(pending, t => Assert.False(t.IsCompleted));

        // 第九則：不排上島，馬上回「不處理」（hook 拿到 204，Claude Code 在終端機問）
        var extra = bridge.PermissionAsync(Permission(99));
        Assert.True(extra.IsCompleted);
        Assert.Null(await extra);
        Assert.Equal(AgentBridge.MaxWaiting, asked.Count);

        // 別的 agent 各算各的
        var codex = bridge.PermissionAsync(Permission(100), AgentKind.Codex);
        Assert.Equal(AgentBridge.MaxWaiting + 1, asked.Count);
        asked[^1].Decision.TrySetResult("deny");
        Assert.Equal("deny", await codex);

        // 回答掉一則就空出一個位子
        asked[0].Decision.TrySetResult("allow");
        Assert.Equal("allow", await pending[0]);
        Assert.Equal(AgentBridge.MaxWaiting - 1, bridge.Waiting(AgentKind.Claude));
        var again = bridge.PermissionAsync(Permission(200));
        Assert.False(again.IsCompleted);
        asked[^1].Decision.TrySetResult("allow");
        Assert.Equal("allow", await again);

        foreach (var ask in asked) ask.Decision.TrySetResult(null);
        await Task.WhenAll(pending);
        Assert.Equal(0, bridge.Waiting(AgentKind.Claude));
        Assert.Equal(0, bridge.Waiting(AgentKind.Codex));
    }

    [Fact]
    public void 位子滿了就不收_還了才有()
    {
        var slots = new Slots(3);
        Assert.True(slots.TryTake());
        Assert.True(slots.TryTake());
        Assert.True(slots.TryTake());
        Assert.False(slots.TryTake());
        Assert.Equal(3, slots.Taken);   // 沒拿到的不佔位子
        slots.Give();
        Assert.True(slots.TryTake());
        Assert.False(slots.TryTake());
    }

    [Fact]
    public void 同時搶也不會超過()
    {
        var slots = new Slots(AgentBridge.MaxOpen);
        int got = 0;
        Parallel.For(0, 1000, _ => { if (slots.TryTake()) Interlocked.Increment(ref got); });
        Assert.Equal(AgentBridge.MaxOpen, got);
        Assert.Equal(AgentBridge.MaxOpen, slots.Taken);
    }
}

/// <summary>/deck/log 寫不出偽造的紀錄行、也洗不掉紀錄檔。</summary>
public class DeckLogTests
{
    [Fact]
    public void 換行與控制字元變成空白()
    {
        Assert.Equal("connected 2026-11-24 bridge: forged", DeckLog.Clean("connected\n2026-11-24\rbridge:\u0085forged"));
        Assert.Equal("a b c d e", DeckLog.Clean("a\u2028b\u2029c\td\0e"));
        Assert.Equal("", DeckLog.Clean(null));
        Assert.Equal("keyUp lyrics {\"x\":1} 歌詞", DeckLog.Clean("keyUp lyrics {\"x\":1} 歌詞"));
        Assert.Equal(DeckLog.MaxLength, DeckLog.Clean(new string('x', 5000)).Length);
        Assert.DoesNotContain('\n', DeckLog.Clean(new string('\n', 5000)));
    }

    [Fact]
    public void 十秒內最多二十行_過了再收()
    {
        long now = 1000;
        var log = new DeckLog(() => now);
        for (int i = 0; i < DeckLog.Burst; i++) Assert.Equal("line", log.Admit("line"));
        Assert.Null(log.Admit("line"));
        now += DeckLog.Window - 1;
        Assert.Null(log.Admit("line"));
        now += 1;   // 一開始那二十行過期了：再收二十行
        for (int i = 0; i < DeckLog.Burst; i++) Assert.Equal("line", log.Admit("line"));
        Assert.Null(log.Admit("line"));
    }

    [Fact]
    public void 陸續送的也照算()
    {
        long now = 0;
        var log = new DeckLog(() => now);
        int kept = 0;
        for (int i = 0; i < 1000; i++, now += 100) if (log.Admit("x") != null) kept++;   // 一百秒、每 0.1 秒一行
        Assert.InRange(kept, 1, DeckLog.Burst * 10);
    }
}

/// <summary>只收本機、衝著 localhost 來的請求。</summary>
public class BridgeLocalTests
{
    [Theory]
    [InlineData("127.0.0.1", "localhost:47813")]
    [InlineData("::1", "localhost:47813")]
    [InlineData("::ffff:127.0.0.1", "localhost:47813")]
    [InlineData("127.0.0.1", "localhost")]
    [InlineData("127.0.0.1", "LOCALHOST:47813")]
    [InlineData("127.0.0.1", "127.0.0.1:47813")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("127.5.6.7", "localhost:47813")]
    public void 本機(string remote, string host) => Assert.True(AgentBridge.Local(System.Net.IPAddress.Parse(remote), host));

    [Theory]
    [InlineData("192.168.1.20", "localhost:47813")]        // 區域網路上別台
    [InlineData("172.28.0.5", "localhost:47813")]
    [InlineData("::ffff:192.168.1.20", "localhost:47813")]
    [InlineData("fe80::1", "localhost:47813")]
    [InlineData("127.0.0.1", "attacker.example:47813")]    // DNS rebinding
    [InlineData("127.0.0.1", "localhost.attacker.example")]
    [InlineData("127.0.0.1", "localhost.:47813")]
    [InlineData("127.0.0.1", "localhost:80")]
    [InlineData("127.0.0.1", "127.0.0.1:8080")]
    [InlineData("127.0.0.1", "[::1]:47813")]
    [InlineData("127.0.0.1", "")]
    [InlineData("127.0.0.1", null)]
    public void 不收(string remote, string? host) => Assert.False(AgentBridge.Local(System.Net.IPAddress.Parse(remote), host));

    [Fact]
    public void 不知道對方是誰就不收() => Assert.False(AgentBridge.Local(null, "localhost:47813"));
}

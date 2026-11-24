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

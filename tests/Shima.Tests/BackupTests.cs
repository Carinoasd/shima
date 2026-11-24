// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Shima.Tests;

public sealed class BackupTests : IDisposable
{
    // 全部在暫存資料夾裡做，不碰 %APPDATA%\Shima
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "Shima-backup-test-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_temp, "data");
    private string Zip => Path.Combine(_temp, "backup.zip");

    public BackupTests() { Directory.CreateDirectory(Data); }
    public void Dispose() { try { Directory.Delete(_temp, true); } catch { } }

    private void Write(string rel, string text)
    {
        var path = Path.Combine(Data, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private const string Settings = """
        { "Language": "ja", "Version": 2, "Opacity": 0.8, "AiKeys": { "claude": "SECRET-AI" }, "LastFmSecret": "SECRET-LFM",
          "LastFmSession": "SECRET-SESSION", "AnthropicKey": "SECRET-OLD", "RoonTokens": { "core": "SECRET-ROON" }, "ListenBrainzToken": "SECRET-LB", "LastFmUser": "me" }
        """;

    private void Fill()
    {
        Write("settings.json", Settings);
        Write("history.jsonl", "{\"a\":1}\n{\"a\":2}\n");
        Write("favorites.json", "[1,2]");
        Write("alarms.json", "[3]");
        Write("lyrics/one.lrc", "[00:01.00]你好");
        Write("lyrics/sub/two.lrc", "[00:02.00]世界");
        Write("translations/t.json", "{}");
        Write("shelf/shelf-1.txt", "暫存");
        Write("bridge.token", "BRIDGE"); Write("deck.token", "DECK"); Write("codex.token", "CODEX"); Write("gemini.token", "GEMINI");
        Write("agent-rules.json", "[\"rule\"]");
        Write("cache/x.json", "cache");
        Write("lyrics/old.lrc.tmp", "tmp"); Write("lyrics/old.lrc.bak", "bak");
    }

    private static List<string> Names(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
    }

    private string Make(params (string Name, string Text)[] entries)
    {
        using (var z = ZipFile.Open(Zip, ZipArchiveMode.Create))
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
        return Zip;
    }

    private void Snapshot(out Dictionary<string, string> files)
    {
        files = Directory.Exists(Data)
            ? Directory.EnumerateFiles(Data, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(Data, f), File.ReadAllText)
            : [];
    }

    [Fact]
    public void 匯出再還原回來內容一致()
    {
        Fill();
        Assert.Equal(BackupStatus.Ok, Backup.Export(Data, Zip).Status);
        var other = Path.Combine(_temp, "other");
        Directory.CreateDirectory(other);
        var result = Backup.Restore(Zip, other, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.Equal(BackupStatus.Ok, result.Status);
        foreach (var rel in new[] { "history.jsonl", "favorites.json", "alarms.json", "lyrics/one.lrc", "lyrics/sub/two.lrc", "translations/t.json", "shelf/shelf-1.txt" })
            Assert.Equal(File.ReadAllText(Path.Combine(Data, rel)), File.ReadAllText(Path.Combine(other, rel)));
        var restored = JsonNode.Parse(File.ReadAllText(Path.Combine(other, "settings.json")))!;
        Assert.Equal("ja", (string?)restored["Language"]);
        Assert.Equal("me", (string?)restored["LastFmUser"]);
    }

    [Fact]
    public void 機密欄位與token不在zip裡_原本的設定檔不動()
    {
        Fill();
        Backup.Export(Data, Zip);
        var names = Names(Zip);
        Assert.DoesNotContain(names, n => n.EndsWith(".token") || n.StartsWith("cache") || n == "agent-rules.json" || n.EndsWith(".tmp") || n.EndsWith(".bak"));
        using var z = ZipFile.OpenRead(Zip);
        using var r = new StreamReader(z.GetEntry("settings.json")!.Open());
        var text = r.ReadToEnd();
        Assert.DoesNotContain("SECRET", text);
        Assert.Contains("\"Language\"", text);
        Assert.Contains("SECRET-AI", File.ReadAllText(Path.Combine(Data, "settings.json")));
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("lyrics/../../evil.lrc")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x.lrc")]
    [InlineData("C:/Windows/x.lrc")]
    [InlineData("C:\\x.lrc")]
    [InlineData("lyrics\\..\\..\\x.lrc")]
    [InlineData("lyrics/CON")]
    [InlineData("lyrics/nul.lrc")]
    [InlineData("lyrics/aux.txt.lrc")]
    [InlineData("lyrics/a.lrc:stream")]
    [InlineData("lyrics/bad.")]
    [InlineData("lyrics//x.lrc")]
    [InlineData("lyrics/a*b.lrc")]
    public void 惡意路徑整個zip被拒絕_原本的資料沒變(string name)
    {
        Fill();
        Snapshot(out var before);
        Make(("settings.json", "{}"), (name, "x"));
        Assert.Equal(BackupStatus.Unsafe, Backup.Restore(Zip, Data, DateTime.Now).Status);
        Snapshot(out var after);
        Assert.Equal(before, after);
        Assert.False(File.Exists(Path.Combine(_temp, "evil.json")));
        Assert.DoesNotContain(Directory.GetDirectories(_temp), d => Path.GetFileName(d).StartsWith(".Shima-restore") || Path.GetFileName(d).StartsWith("Shima-before"));
    }

    [Fact]
    public void 同名項目被拒絕()
    {
        Fill();
        Make(("lyrics/a.lrc", "1"), ("LYRICS/A.lrc", "2"));
        Assert.Equal(BackupStatus.Unsafe, Backup.Restore(Zip, Data, DateTime.Now).Status);
    }

    [Fact]
    public void 不認得的項目被略過_token與agent_rules不會被採用()
    {
        Fill();
        Make(("lyrics/new.lrc", "新的"), ("bridge.token", "HACK"), ("agent-rules.json", "[\"allow all\"]"), ("cache/x.json", "bad"), ("random.txt", "?"), ("lyrics/x.token", "t"), ("lyrics/x.tmp", "t"));
        var result = Backup.Restore(Zip, Data, DateTime.Now);
        Assert.Equal(BackupStatus.Ok, result.Status);
        Assert.Equal(1, result.Files);
        Assert.Equal("BRIDGE", File.ReadAllText(Path.Combine(Data, "bridge.token")));
        Assert.Equal("[\"rule\"]", File.ReadAllText(Path.Combine(Data, "agent-rules.json")));
        Assert.Equal("cache", File.ReadAllText(Path.Combine(Data, "cache/x.json")));
        Assert.False(File.Exists(Path.Combine(Data, "random.txt")));
        Assert.False(File.Exists(Path.Combine(Data, "lyrics/x.token")));
        Assert.Equal("新的", File.ReadAllText(Path.Combine(Data, "lyrics/new.lrc")));
        Assert.False(File.Exists(Path.Combine(Data, "lyrics/one.lrc")));   // 資料夾整個換成備份裡的
    }

    [Fact]
    public void 沒有認得的內容被拒絕()
    {
        Fill();
        Make(("random.txt", "?"));
        Assert.Equal(BackupStatus.Empty, Backup.Restore(Zip, Data, DateTime.Now).Status);
        File.WriteAllText(Zip, "not a zip");
        Assert.Equal(BackupStatus.Unreadable, Backup.Restore(Zip, Data, DateTime.Now).Status);
    }

    [Fact]
    public void 超過檔案數量上限被拒絕()
    {
        Fill();
        Snapshot(out var before);
        using (var z = ZipFile.Open(Zip, ZipArchiveMode.Create))
            for (int i = 0; i <= Backup.MaxFiles; i++) z.CreateEntry($"lyrics/{i}.lrc");
        Assert.Equal(BackupStatus.TooBig, Backup.Restore(Zip, Data, DateTime.Now).Status);
        Snapshot(out var after);
        Assert.Equal(before, after);
    }

    [Fact]
    public void 解開後超過大小上限被拒絕()
    {
        Fill();
        Snapshot(out var before);
        // 全是零，壓縮後很小，解開卻超過上限
        using (var z = ZipFile.Open(Zip, ZipArchiveMode.Create))
        {
            var buffer = new byte[1024 * 1024];
            for (int i = 0; i < 3; i++)
            {
                using var s = z.CreateEntry($"lyrics/big{i}.lrc").Open();
                for (int n = 0; n < 200; n++) s.Write(buffer);
            }
        }
        Assert.True(new FileInfo(Zip).Length < 10 * 1024 * 1024);
        Assert.Equal(BackupStatus.TooBig, Backup.Restore(Zip, Data, DateTime.Now).Status);
        Snapshot(out var after);
        Assert.Equal(before, after);
        Assert.DoesNotContain(Directory.GetDirectories(_temp), d => Path.GetFileName(d).StartsWith(".Shima-restore"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("{ \"Language\": 5 }")]
    [InlineData("{ \"LyricsProviders\": null }")]
    public void 壞掉的設定檔被拒絕_原本的資料沒變(string settings)
    {
        Fill();
        Snapshot(out var before);
        Make(("settings.json", settings), ("lyrics/new.lrc", "x"));
        Assert.Equal(BackupStatus.BadSettings, Backup.Restore(Zip, Data, DateTime.Now).Status);
        Snapshot(out var after);
        Assert.Equal(before, after);
        Assert.DoesNotContain(Directory.GetDirectories(_temp), d => Path.GetFileName(d).StartsWith("Shima-before"));
    }

    [Fact]
    public void 還原保留這台的機密欄位_zip裡塞的不採用()
    {
        Fill();
        Make(("settings.json", """
            { "Language": "en", "AiKeys": { "claude": "EVIL" }, "LastFmSecret": "EVIL", "LastFmSession": "EVIL", "RoonTokens": { "x": "EVIL" },
              "ListenBrainzToken": "EVIL", "AnthropicKey": "EVIL" }
            """));
        var result = Backup.Restore(Zip, Data, DateTime.Now);
        Assert.Equal(BackupStatus.Ok, result.Status);
        var text = File.ReadAllText(Path.Combine(Data, "settings.json"));
        Assert.DoesNotContain("EVIL", text);
        foreach (var keep in new[] { "SECRET-AI", "SECRET-LFM", "SECRET-SESSION", "SECRET-OLD", "SECRET-ROON", "SECRET-LB" }) Assert.Contains(keep, text);
        Assert.Contains("\"en\"", text);
    }

    [Fact]
    public void 這台沒有設定檔時機密欄位是空的()
    {
        Make(("settings.json", "{ \"Language\": \"en\", \"AiKeys\": { \"claude\": \"EVIL\" } }"));
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, Data, DateTime.Now).Status);
        Assert.DoesNotContain("EVIL", File.ReadAllText(Path.Combine(Data, "settings.json")));
    }

    [Fact]
    public void 可疑的值被改回安全的範圍_自訂位址不被換掉()
    {
        Write("settings.json", """{ "AiKeys": { "custom": "k" }, "AiCustomUrl": "https://mine.example/v1" }""");
        Make(("settings.json", """{ "Opacity": 0.01, "WaterMinutes": -5, "AiCustomUrl": "https://evil.example/v1", "LyricsProviders": ["lrclib", "../x"] }"""));
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, Data, DateTime.Now).Status);
        var s = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!;
        Assert.Equal(1.0, (double)s["Opacity"]!);
        Assert.Equal(1, (int)s["WaterMinutes"]!);
        Assert.Equal("https://mine.example/v1", (string?)s["AiCustomUrl"]);
        Assert.Equal(["lrclib"], s["LyricsProviders"]!.AsArray().Select(n => (string)n!).ToArray());
    }

    [Fact]
    public void 還原前先把整份複製一份_暫存架的路徑改指到這台()
    {
        Fill();
        Make(("settings.json", """{ "Shelf": ["D:\\old\\Shima\\shelf\\shelf-1.txt", "D:\\docs\\a.pdf"] }"""), ("shelf/shelf-1.txt", "x"));
        var now = new DateTime(2026, 5, 6, 7, 8, 9);
        var result = Backup.Restore(Zip, Data, now);
        Assert.Equal("Shima-before-restore-20260506-070809", result.Saved);
        var copy = Path.Combine(_temp, result.Saved!);
        Assert.Equal("BRIDGE", File.ReadAllText(Path.Combine(copy, "bridge.token")));
        Assert.True(File.Exists(Path.Combine(copy, "lyrics/one.lrc")));
        var shelf = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!["Shelf"]!.AsArray().Select(n => (string?)n).ToArray();
        Assert.Equal(Path.Combine(Data, "shelf", "shelf-1.txt"), shelf[0]);
        Assert.Equal("D:\\docs\\a.pdf", shelf[1]);
    }

    [Fact]
    public void 連線目標不匯出_還原時用這台的()
    {
        Write("settings.json", """{ "RoonHost": "192.168.1.5", "RoonPort": 9100, "RoonExtensionId": "mine", "AiCustomUrl": "http://localhost:11434/v1", "Language": "ja" }""");
        Assert.Equal(BackupStatus.Ok, Backup.Export(Data, Zip).Status);
        using (var z = ZipFile.OpenRead(Zip))
        {
            using var r = new StreamReader(z.GetEntry("settings.json")!.Open());
            var text = r.ReadToEnd();
            foreach (var field in Backup.LocalFields) Assert.DoesNotContain(field, text);
            Assert.Contains("\"ja\"", text);
        }
        File.Delete(Zip);
        Make(("settings.json", """{ "RoonHost": "evil.example", "RoonPort": 1, "RoonExtensionId": "x", "AiCustomUrl": "https://evil.example/v1", "Language": "en" }"""));
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, Data, DateTime.Now).Status);
        var s = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!;
        Assert.Equal("192.168.1.5", (string?)s["RoonHost"]);
        Assert.Equal(9100, (int)s["RoonPort"]!);
        Assert.Equal("mine", (string?)s["RoonExtensionId"]);
        Assert.Equal("http://localhost:11434/v1", (string?)s["AiCustomUrl"]);   // 這台自己填的 http 位址（本機的模型）照舊
        Assert.Equal("en", (string?)s["Language"]);
    }

    [Fact]
    public void 這台沒設定過連線目標時_備份裡的也不採用()
    {
        Make(("settings.json", """{ "RoonHost": "evil.example", "AiCustomUrl": "https://evil.example/v1", "AiService": "custom" }"""));
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, Data, DateTime.Now).Status);
        var s = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!;
        Assert.Equal("127.0.0.1", (string?)s["RoonHost"]);
        Assert.Equal("", (string?)s["AiCustomUrl"]);
    }

    [Fact]
    public void 備份裡的網路路徑不採用_這台原本就有的照舊()
    {
        Write("settings.json", """{ "LyricsFolders": ["\\\\nas\\music\\lyrics"] }""");
        Make(("settings.json", """
            { "CharacterImage": "\\\\evil.example\\share\\a.png",
              "LyricsFolders": ["\\\\nas\\music\\lyrics", "\\\\evil.example\\share", "D:\\lyrics", "//evil.example/x", "\\\\?\\UNC\\evil.example\\x", "relative\\path"],
              "Shelf": ["\\\\evil.example\\share\\b.txt", "D:\\docs\\a.pdf"] }
            """));
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, Data, DateTime.Now).Status);
        var s = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!;
        Assert.Equal("", (string?)s["CharacterImage"]);
        Assert.Equal(["\\\\nas\\music\\lyrics", "D:\\lyrics"], s["LyricsFolders"]!.AsArray().Select(n => (string)n!).ToArray());
        Assert.Equal(["D:\\docs\\a.pdf"], s["Shelf"]!.AsArray().Select(n => (string)n!).ToArray());
    }

    [Theory]
    [InlineData("C:\\a\\b.png", true)]
    [InlineData("d:/a", true)]
    [InlineData("\\\\server\\share\\a", false)]
    [InlineData("//server/share", false)]
    [InlineData("\\\\?\\C:\\a", false)]
    [InlineData("\\\\.\\pipe\\x", false)]
    [InlineData("C:", false)]
    [InlineData("C:relative", false)]
    [InlineData("a\\b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 本機磁碟上的路徑(string? path, bool expected) => Assert.Equal(expected, Backup.OnThisComputer(path));

    [Fact]
    public void 剛好放在叫shelf的資料夾裡的別的檔案不改路徑()
    {
        Make(("settings.json", """{ "Shelf": ["D:\\work\\shelf\\plan.txt", "D:\\old\\Shima\\shelf\\shelf-9.txt"] }"""), ("shelf/shelf-9.txt", "x"));
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, Data, DateTime.Now).Status);
        var shelf = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!["Shelf"]!.AsArray().Select(n => (string)n!).ToArray();
        Assert.Equal(["D:\\work\\shelf\\plan.txt", Path.Combine(Data, "shelf", "shelf-9.txt")], shelf);
    }

    [Fact]
    public void 舊資料移不開時_原本的一個都不能少()
    {
        Fill();
        Snapshot(out var before);
        Make(("settings.json", "{ \"Language\": \"en\" }"), ("history.jsonl", "{\"a\":9}\n"), ("lyrics/new.lrc", "[00:01.00]new"));
        // 別的程式正開著歌詞資料夾裡的檔案：設定與紀錄已經換上去了，輪到歌詞資料夾時移不走
        using (new FileStream(Path.Combine(Data, "lyrics", "one.lrc"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = Backup.Restore(Zip, Data, new DateTime(2026, 5, 6, 7, 8, 9));
            Assert.Equal(BackupStatus.Failed, result.Status);
            Assert.Null(result.Saved);   // 全部換回來了，不用留那份複本
        }
        Snapshot(out var after);
        Assert.Equal(before, after);
        Assert.Empty(Directory.GetDirectories(_temp, "Shima-before-restore-*"));
        Assert.Empty(Directory.GetDirectories(_temp, ".Shima-restore-*"));
    }

    [Fact]
    public void 確定要換的回呼_在複本與換檔之前呼叫一次()
    {
        Fill();
        Make(("settings.json", "{ \"Language\": \"en\" }"), ("history.jsonl", "{\"a\":9}\n"));
        int calls = 0;
        var result = Backup.Restore(Zip, Data, new DateTime(2026, 5, 6, 7, 8, 9), () =>
        {
            calls++;
            // 還沒換：資料夾裡還是原本的，也還沒有還原前的複本
            Assert.Equal("{\"a\":1}\n{\"a\":2}\n", File.ReadAllText(Path.Combine(Data, "history.jsonl")));
            Assert.Contains("\"ja\"", File.ReadAllText(Path.Combine(Data, "settings.json")));
            Assert.Empty(Directory.GetDirectories(_temp, "Shima-before-restore-*"));
            // 呼叫端在這時寫的（正在聽的這首）要進到還原前的複本裡
            File.AppendAllText(Path.Combine(Data, "history.jsonl"), "{\"a\":3}\n");
        });
        Assert.Equal(BackupStatus.Ok, result.Status);
        Assert.Equal(1, calls);
        Assert.Equal("{\"a\":9}\n", File.ReadAllText(Path.Combine(Data, "history.jsonl")));
        Assert.EndsWith("{\"a\":3}\n", File.ReadAllText(Path.Combine(_temp, result.Saved!, "history.jsonl")));
    }

    public static TheoryData<string, BackupStatus> Rejected => new()
    {
        { "unreadable", BackupStatus.Unreadable },
        { "unsafe", BackupStatus.Unsafe },
        { "empty", BackupStatus.Empty },
        { "bad-settings", BackupStatus.BadSettings },
        { "too-many", BackupStatus.TooBig },
        { "too-big", BackupStatus.TooBig },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void 被拒絕的zip不會呼叫確定要換的回呼(string kind, BackupStatus expected)
    {
        Fill();
        switch (kind)
        {
            case "unreadable": File.WriteAllText(Zip, "not a zip"); break;
            case "unsafe": Make(("settings.json", "{}"), ("../evil.json", "x")); break;
            case "empty": Make(("random.txt", "?")); break;
            case "bad-settings": Make(("settings.json", "{ not json"), ("lyrics/a.lrc", "x")); break;
            case "too-many":
                using (var z = ZipFile.Open(Zip, ZipArchiveMode.Create))
                    for (int i = 0; i <= Backup.MaxFiles; i++) z.CreateEntry($"lyrics/{i}.lrc");
                break;
            case "too-big":
                using (var z = ZipFile.Open(Zip, ZipArchiveMode.Create))
                {
                    var buffer = new byte[1024 * 1024];
                    for (int i = 0; i < 3; i++)
                    {
                        using var w = z.CreateEntry($"lyrics/big{i}.lrc").Open();
                        for (int n = 0; n < 200; n++) w.Write(buffer);
                    }
                }
                break;
        }
        int calls = 0;
        Assert.Equal(expected, Backup.Restore(Zip, Data, DateTime.Now, () => calls++).Status);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("shelf/報告\u202Efdp.exe")]
    [InlineData("lyrics/a\u200Db.lrc")]
    [InlineData("lyrics\u2066/a.lrc")]
    [InlineData("\uFEFFsettings.json")]
    public void 檔名含格式字元整個zip被拒絕(string name)
    {
        Fill();
        Snapshot(out var before);
        Assert.False(Backup.SafeName(name, out _));
        Make(("settings.json", "{}"), (name, "x"));
        Assert.Equal(BackupStatus.Unsafe, Backup.Restore(Zip, Data, DateTime.Now).Status);
        Snapshot(out var after);
        Assert.Equal(before, after);
    }

    [Fact]
    public void 暫存架只還原文字與圖片_指向被略過的檔案的項目拿掉()
    {
        Make(("settings.json", """
            { "Shelf": ["D:\\old\\Shima\\shelf\\a.txt", "D:\\old\\Shima\\shelf\\evil.exe", "D:\\old\\Shima\\shelf\\run.LNK", "D:\\old\\Shima\\shelf\\b.PNG", "D:\\docs\\a.pdf"] }
            """), ("shelf/a.txt", "t"), ("shelf/b.PNG", "p"), ("shelf/evil.exe", "MZ"), ("shelf/run.LNK", "l"), ("shelf/x.bat", "b"));
        var result = Backup.Restore(Zip, Data, DateTime.Now);
        Assert.Equal(BackupStatus.Ok, result.Status);
        Assert.Equal(3, result.Files);   // 設定檔、a.txt、b.PNG
        Assert.Equal(["a.txt", "b.PNG"], Directory.GetFiles(Path.Combine(Data, "shelf")).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        var shelf = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "settings.json")))!["Shelf"]!.AsArray().Select(n => (string)n!).ToArray();
        Assert.Equal([Path.Combine(Data, "shelf", "a.txt"), Path.Combine(Data, "shelf", "b.PNG"), "D:\\docs\\a.pdf"], shelf);
    }

    [Fact]
    public void 匯出時暫存架只帶文字與圖片_還原不了的名稱不匯出()
    {
        Write("settings.json", "{}");
        Write("shelf/a.txt", "t");
        Write("shelf/b.png", "p");
        Write("shelf/evil.exe", "MZ");
        Write("lyrics/ok.lrc", "[00:01.00]ok");
        Write("lyrics/emoji\u200D.lrc", "[00:01.00]zwj");
        Assert.Equal(BackupStatus.Ok, Backup.Export(Data, Zip).Status);
        Assert.Equal(["lyrics/ok.lrc", "settings.json", "shelf/a.txt", "shelf/b.png"], Names(Zip));
        // 自己匯出的一定還原得回來
        var other = Path.Combine(_temp, "other");
        Assert.Equal(BackupStatus.Ok, Backup.Restore(Zip, other, DateTime.Now).Status);
    }

    [Fact]
    public void 路徑檢查()
    {
        Assert.True(Backup.SafeName("lyrics/一首歌 (live).lrc", out var rel));
        Assert.Equal("lyrics/一首歌 (live).lrc", rel);
        Assert.True(Backup.SafeName("lyrics\\a.lrc", out rel));
        Assert.Equal("lyrics/a.lrc", rel);
        Assert.True(Backup.SafeName("lyrics/", out rel));
        Assert.False(Backup.SafeName("", out _));
        Assert.False(Backup.SafeName(null, out _));
        Assert.Null(Backup.Known("agent-rules.json"));
        Assert.Null(Backup.Known("lyrics"));
        Assert.Equal("settings.json", Backup.Known("Settings.JSON"));
    }
}

/// <summary>
/// AppSettings.Frozen 是整個程式共用的：開著的時候別的測試存的設定、提醒、收藏都會被略過。
/// 所以會動到它的測試放在不平行的集合裡，跟其他測試錯開跑。
/// </summary>
[CollectionDefinition("Frozen", DisableParallelization = true)]
public sealed class FrozenCollection;

[Collection("Frozen")]
public sealed class BackupFrozenTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "Shima-frozen-test-" + Guid.NewGuid().ToString("N"));

    public BackupFrozenTests() { Directory.CreateDirectory(_temp); }
    public void Dispose() { AppSettings.Frozen = false; try { Directory.Delete(_temp, true); } catch { } }

    [Fact]
    public void 還原換上去之後_提醒與收藏的存檔不會蓋掉還原的檔案()
    {
        var alarmsPath = Path.Combine(_temp, "alarms.json");
        var favoritesPath = Path.Combine(_temp, "favorites.json");
        var alarms = new Shima.Agents.Alarms(alarmsPath);
        var favorites = new Shima.Listening.LyricFavorites(favoritesPath, _ => { });
        alarms.Add(new DateTime(2030, 1, 1, 8, 0, 0), "還原前");
        favorites.Add("還原前", "", "歌", "人", "", TimeSpan.Zero, DateTimeOffset.Now);

        // 備份換上去了
        File.WriteAllText(alarmsPath, "[\"restored\"]");
        File.WriteAllText(favoritesPath, "[\"restored\"]");
        AppSettings.Frozen = true;
        try
        {
            alarms.Add(new DateTime(2030, 1, 2, 8, 0, 0), "還原後");
            alarms.Cancel("還原前");
            alarms.Check(new DateTime(2031, 1, 1));
            var added = favorites.Add("又一句", "", "歌", "人", "", TimeSpan.Zero, DateTimeOffset.Now);
            favorites.Remove(added!.Id);
        }
        finally { AppSettings.Frozen = false; }
        Assert.Equal("[\"restored\"]", File.ReadAllText(alarmsPath));
        Assert.Equal("[\"restored\"]", File.ReadAllText(favoritesPath));
        Assert.False(File.Exists(alarmsPath + ".part"));
        Assert.False(File.Exists(favoritesPath + ".tmp"));

        // 沒有凍結時照常存
        alarms.Add(new DateTime(2030, 1, 3, 8, 0, 0), "平常");
        favorites.Add("平常", "", "歌", "人", "", TimeSpan.Zero, DateTimeOffset.Now);
        Assert.Contains("平常", File.ReadAllText(alarmsPath));
        Assert.Contains("平常", File.ReadAllText(favoritesPath));
    }
}

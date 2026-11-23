// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class QuickAlarmTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 14, 0, 0);

    [Theory]
    [InlineData("15:30 開會", 15, 30, "開會")]
    [InlineData("15:30開會", 15, 30, "開會")]
    [InlineData("１５：３０ 開會", 15, 30, "開會")]
    [InlineData("下午3點 吃藥", 15, 0, "吃藥")]
    [InlineData("下午 3點半 吃藥", 15, 30, "吃藥")]
    [InlineData("3pm call mom", 15, 0, "call mom")]
    [InlineData("7點半開會", 7, 30, "開會")]
    public void 時刻加內容(string line, int hour, int minute, string text)
    {
        var result = Alarms.ParseLine(line, Now);
        Assert.NotNull(result);
        Assert.Equal((hour, minute), (result.Value.At.Hour, result.Value.At.Minute));
        Assert.Equal(text, result.Value.Text);
    }

    [Fact]
    public void 今天已經過的時刻算明天_還沒到的算今天()
    {
        Assert.Equal(new DateTime(2026, 11, 24, 8, 0, 0), Alarms.ParseLine("08:00 起床", Now)!.Value.At);
        Assert.Equal(new DateTime(2026, 11, 23, 20, 0, 0), Alarms.ParseLine("20:00 起床", Now)!.Value.At);
        Assert.Equal(new DateTime(2026, 11, 24, 8, 30, 0), Alarms.ParseLine("明天 8點半 交報告", Now)!.Value.At);
        Assert.Equal(new DateTime(2026, 11, 25, 7, 30, 0), Alarms.ParseLine("2026-11-25 07:30 出門", Now)!.Value.At);
    }

    [Theory]
    [InlineData("20 分鐘 喝水", 20, "喝水")]
    [InlineData("20分鐘喝水", 20, "喝水")]
    [InlineData("20m 喝水", 20, "喝水")]
    [InlineData("20 min 喝水", 20, "喝水")]
    [InlineData("1h 休息", 60, "休息")]
    [InlineData("2 hours stretch", 120, "stretch")]
    [InlineData("1小時 休息", 60, "休息")]
    [InlineData("1.5h 休息", 90, "休息")]
    public void 幾分鐘後(string line, int minutes, string text)
    {
        var result = Alarms.ParseLine(line, Now);
        Assert.NotNull(result);
        Assert.Equal(Now.AddMinutes(minutes), result.Value.At);
        Assert.Equal(text, result.Value.Text);
    }

    [Fact]
    public void 只有時間沒有內容也收()
    {
        Assert.Equal("", Alarms.ParseLine("10m", Now)!.Value.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("喝水")]
    [InlineData("20 喝水")]
    [InlineData("5 meeting")]
    [InlineData("25:00 開會")]
    [InlineData("0m 喝水")]
    [InlineData("9999h 休息")]
    [InlineData("2026-01-01 07:30 太久以前")]
    public void 看不懂就是null(string line)
    {
        Assert.Null(Alarms.ParseLine(line, Now));
    }

    [Fact]
    public void 畫面上的時間寫法()
    {
        Assert.Equal("15:30", Alarms.Short(new DateTime(2026, 11, 23, 15, 30, 0), Now, "明天"));
        Assert.Equal("明天 08:00", Alarms.Short(new DateTime(2026, 11, 24, 8, 0, 0), Now, "明天"));
        Assert.Equal("11/26 08:00", Alarms.Short(new DateTime(2026, 11, 26, 8, 0, 0), Now, "明天"));
    }

    [Fact]
    public void 新增與取消都會通知畫面()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var alarms = new Alarms(path);
            var changed = 0;
            alarms.Changed += () => changed++;
            var a = alarms.Add(Now.AddMinutes(5), "x");
            alarms.Cancel("#" + a.Id);
            alarms.Add(Now.AddMinutes(-1), "past");
            alarms.Check(Now);
            Assert.Equal(4, changed);
        }
        finally { File.Delete(path); }
    }
}

public class ShelfTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shima-shelf-" + Guid.NewGuid().ToString("N"));
    private string Folder => Path.Combine(_root, "shelf");
    public ShelfTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void 只有直接放在資料夾裡的才是自己的複本()
    {
        Assert.True(Shelf.IsOwned(Path.Combine(Folder, "a.txt"), Folder));
        Assert.True(Shelf.IsOwned(Path.Combine(Folder, "A.PNG"), Folder + Path.DirectorySeparatorChar));
        Assert.False(Shelf.IsOwned(Path.Combine(_root, "a.txt"), Folder));
        Assert.False(Shelf.IsOwned(Path.Combine(Folder, "sub", "a.txt"), Folder));
        Assert.False(Shelf.IsOwned(Folder, Folder));
        Assert.False(Shelf.IsOwned(Path.Combine(Folder, "..", "a.txt"), Folder));
        Assert.False(Shelf.IsOwned(Path.Combine(Folder, "..", "shelf2", "a.txt"), Folder));
        Assert.False(Shelf.IsOwned(Folder + "-other" + Path.DirectorySeparatorChar + "a.txt", Folder));
        Assert.False(Shelf.IsOwned("", Folder));
        Assert.False(Shelf.IsOwned(null, Folder));
    }

    [Fact]
    public void 種類看資料夾與副檔名()
    {
        Assert.Equal(ShelfKind.Text, Shelf.Classify(Path.Combine(Folder, "a.txt"), Folder));
        Assert.Equal(ShelfKind.Image, Shelf.Classify(Path.Combine(Folder, "a.PNG"), Folder));
        Assert.Equal(ShelfKind.File, Shelf.Classify(Path.Combine(Folder, "a.docx"), Folder));
        // 使用者自己的 .txt／.png 在別的地方：還是檔案
        Assert.Equal(ShelfKind.File, Shelf.Classify(Path.Combine(_root, "a.txt"), Folder));
        Assert.Equal(ShelfKind.File, Shelf.Classify(Path.Combine(_root, "a.png"), Folder));
    }

    [Fact]
    public void 標籤取第一行有字的地方_太長截斷()
    {
        Assert.Equal("hello world", Shelf.TextLabel("\n  hello \t world  \nsecond"));
        Assert.Equal("", Shelf.TextLabel("   \n  "));
        Assert.Equal("", Shelf.TextLabel(null));
        var label = Shelf.TextLabel(new string('字', 100));
        Assert.Equal(Shelf.LabelLength + 1, label.Length);
        Assert.EndsWith("…", label);
        Assert.Equal("a.docx", Shelf.Label(@"C:\x\a.docx\", ShelfKind.File));
    }

    [Fact]
    public void 文字存成檔案_空白不收_太長截斷()
    {
        var now = new DateTime(2026, 11, 23, 14, 0, 0);
        Assert.Null(Shelf.SaveText("  \n ", Folder, now));
        var path = Shelf.SaveText("內容", Folder, now);
        Assert.NotNull(path);
        Assert.True(Shelf.IsOwned(path, Folder));
        Assert.Equal(ShelfKind.Text, Shelf.Classify(path, Folder));
        Assert.Equal("內容", File.ReadAllText(path));
        // 同一秒存兩次不會蓋掉
        var second = Shelf.SaveText("另一個", Folder, now);
        Assert.NotEqual(path, second);
        var big = Shelf.SaveText(new string('a', Shelf.MaxText + 500), Folder, now);
        Assert.Equal(Shelf.MaxText, File.ReadAllText(big!).Length);
    }

    [Fact]
    public void 移除時只刪自己的複本_使用者的檔案絕對不刪()
    {
        var copy = Shelf.SaveText("x", Folder, DateTime.Now)!;
        var mine = Path.Combine(_root, "mine.txt");
        File.WriteAllText(mine, "重要");
        var traversal = Path.Combine(Folder, "..", "mine.txt");

        Assert.False(Shelf.DeleteCopy(mine, Folder));
        Assert.False(Shelf.DeleteCopy(traversal, Folder));
        Assert.True(File.Exists(mine));
        Assert.True(Shelf.DeleteCopy(copy, Folder));
        Assert.False(File.Exists(copy));
        Assert.False(Shelf.DeleteCopy(copy, Folder));
    }
}

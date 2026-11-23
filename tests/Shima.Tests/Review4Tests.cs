// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class Review4Tests
{
    [Theory]
    [InlineData("2026-11-8 07:30 開會")]
    [InlineData("2026/11/8 07:30 開會")]
    [InlineData("2026-11-08 7:30 開會")]
    public void 日期沒補零又帶時間也收(string line)
    {
        var parsed = Alarms.ParseLine(line, new DateTime(2026, 11, 5, 10, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 8, 7, 30, 0), parsed?.At);
        Assert.Equal("開會", parsed?.Text);
    }

    [Fact]
    public void 下午點今天沒寫時間_排在下一個整點()
    {
        var now = new DateTime(2026, 11, 24, 14, 20, 0);
        var parsed = Alarms.ParseLine("2026-11-24 交報告", now);
        Assert.Equal(new DateTime(2026, 11, 24, 15, 0, 0), parsed?.At);
        Assert.Equal("交報告", parsed?.Text);
    }

    [Fact]
    public void 今天早上九點前沒寫時間_還是九點()
    {
        var parsed = Alarms.ParseLine("2026-11-24 交報告", new DateTime(2026, 11, 24, 7, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 24, 9, 0, 0), parsed?.At);
    }

    [Fact]
    public void 今天有寫的過去時間還是不收_二十三點後沒有下一個整點()
    {
        Assert.Null(Alarms.ParseLine("2026-11-24 8:00 交報告", new DateTime(2026, 11, 24, 14, 0, 0)));
        Assert.Null(Alarms.ParseLine("2026-11-24 交報告", new DateTime(2026, 11, 24, 23, 30, 0)));
    }

    [Fact]
    public void 語音助手排遠的提醒_等待時間用天()
    {
        var now = new DateTime(2026, 11, 24, 10, 0, 0);
        var file = Path.Combine(Path.GetTempPath(), "shima-r4-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var alarms = new Alarms(file);
            var far = Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "2027-01-22 12:00", ["text"] = "遠的" }, alarms, now);
            Assert.Contains("59 天 2 小時後", far);
            var near = Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "2026-11-24 12:30", ["text"] = "近的" }, alarms, now);
            Assert.Contains("2 小時 30 分後", near);
        }
        finally { try { File.Delete(file); File.Delete(file + ".tmp"); } catch { } }   // 不要每跑一次測試就在暫存資料夾留一個檔
    }
}

public class Review5Tests
{
    private static Track Song(string title, DateTime at) =>
        new("Spotify", "Spotify.exe", title, "蓮ノ空", "Dream Believers", null, true, TimeSpan.Zero, new DateTimeOffset(at), TimeSpan.FromSeconds(200));

    private static Listen Entry(int i) =>
        new(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero).AddMinutes(i), "歌" + i, "B", "C", "Roon", 100, 60);

    [Fact]
    public void 一邊讀一邊附加寫入_兩邊都不失敗_紀錄一筆不少()
    {
        using var temp = new ListeningTempFolder();
        var path = temp.File("history.jsonl");
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var log = new ListeningLog(path, errors.Enqueue);
        var cache = new HistoryCache();
        using var stop = new CancellationTokenSource();
        int reads = 0;
        var reader = new Thread(() =>
        {
            while (!stop.IsCancellationRequested) { cache.Read(path, errors.Enqueue); Interlocked.Increment(ref reads); }
        });
        reader.Start();
        var t0 = new DateTime(2026, 11, 23, 8, 0, 0);
        const int Count = 150;
        for (int n = 0; n < Count; n++)
        {
            var start = t0.AddMinutes(n * 10);
            for (int i = 0; i <= 400; i++) log.Observe(Song("歌" + n, start), start.AddSeconds(i * 0.1));
        }
        log.Flush();
        stop.Cancel();
        reader.Join();

        Assert.Empty(errors);
        Assert.True(reads > 0);
        Assert.Equal(Count, cache.Read(path).Listens.Count);
        Assert.Equal(Count, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void 檔案開著別人也能寫_也能刪()
    {
        using var temp = new ListeningTempFolder();
        var path = temp.File("history.jsonl");
        File.WriteAllText(path, HistoryFile.ToLine(Entry(0)) + "\n");
        using var open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        File.AppendAllText(path, HistoryFile.ToLine(Entry(1)) + "\n");   // 讀的一方不擋寫
        Assert.Equal(2, HistoryFile.Read(path).Count);
    }

    [Fact]
    public void 開著寫的時候也讀得到_最後一行寫一半的略過_之後補完就讀得到()
    {
        using var temp = new ListeningTempFolder();
        var path = temp.File("history.jsonl");
        var whole = HistoryFile.ToLine(Entry(1));
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        var first = System.Text.Encoding.UTF8.GetBytes(HistoryFile.ToLine(Entry(0)) + "\n" + whole[..(whole.Length / 2)]);
        writer.Write(first); writer.Flush();

        var cache = new HistoryCache();
        var (half, changed) = cache.Read(path);
        Assert.True(changed);
        Assert.Single(half);   // 半行不算、也不丟例外

        var rest = System.Text.Encoding.UTF8.GetBytes(whole[(whole.Length / 2)..] + "\n");
        writer.Write(rest); writer.Flush();
        var (full, changedAgain) = cache.Read(path);
        Assert.True(changedAgain);
        Assert.Equal(2, full.Count);
        Assert.Equal("歌1", full[1].Title);
    }

    [Fact]
    public void 關掉之後的檢查不會把提醒吃掉()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-r5-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var now = new DateTime(2026, 11, 24, 10, 0, 0);
            var alarms = new Alarms(path);
            alarms.Add(now.AddSeconds(-1), "剛好到點");
            var fired = 0;
            alarms.Due += (_, _) => fired++;
            alarms.Dispose();

            Assert.Empty(alarms.Check(now));   // 計時器回呼晚了一步
            Assert.Equal(0, fired);
            Assert.Single(alarms.List);

            using var again = new Alarms(path);   // 下次開程式還在，可以補成錯過的提醒
            Assert.Single(again.List);
            Assert.Single(again.Check(now));
        }
        finally { File.Delete(path); }
    }
}

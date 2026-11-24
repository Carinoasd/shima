// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using Shima.Listening;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class ListenSourcesTests
{
    private static Listen From(string source, string title = "t") =>
        new(new DateTimeOffset(2026, 11, 23, 21, 0, 0, TimeSpan.Zero), title, "a", "", source, 200, 200);

    [Fact]
    public void 預設不排除任何來源() => Assert.True(ListenSources.Counts("Chrome", []));

    [Theory]
    [InlineData("Chrome", "Chrome", false)]
    [InlineData("chrome", "Chrome", false)]       // 大小寫
    [InlineData("  Chrome ", "Chrome", false)]    // 前後空白
    [InlineData("Chrome", " CHROME", false)]
    [InlineData("Spotify", "Chrome", true)]
    [InlineData("Chromium", "Chrome", true)]      // 只比整個名稱
    [InlineData("", "Chrome", true)]              // 沒有來源名稱：沒有東西可以排除它
    [InlineData("  ", "Chrome", true)]
    [InlineData("", "", true)]                    // 排除清單裡的空字串不會擋掉沒有名稱的紀錄
    public void 這一筆算不算(string source, string excluded, bool counts) => Assert.Equal(counts, ListenSources.Counts(source, [excluded]));

    [Fact]
    public void Roon_兩種來源名稱一致()
    {
        // 直接連 Roon 的 Track.Source 是 "Roon"；經由 SMTC 出現時 "Roon.exe" 轉成同一個名字
        Assert.Equal(RoonSource.AppId, SmtcSource.AppName("Roon.exe"));
        Assert.False(ListenSources.Counts(SmtcSource.AppName("Roon.exe"), ["Roon"]));
        Assert.True(ListenSources.Counts(SmtcSource.AppName("chrome.exe"), ["Roon"]));
        Assert.False(ListenSources.Counts(SmtcSource.AppName("Chrome"), ["chrome"]));
    }

    [Fact]
    public void 沒有排除就是同一份清單_有排除只留下算的()
    {
        var all = new List<Listen> { From("Roon"), From("Chrome"), From("chrome "), From("") };
        Assert.Same(all, ListenSources.Filter(all, []));
        Assert.Equal(["Roon", ""], ListenSources.Filter(all, ["Chrome"]).Select(l => l.Source));
    }

    [Fact]
    public void 統計不算被排除的來源()
    {
        var now = new DateTime(2026, 11, 23, 22, 0, 0);
        var all = new List<Listen> { From("Roon", "歌"), From("Chrome", "影片"), From("Chrome", "影片") };
        var summary = Stats.Compute(ListenSources.Filter(all, ["Chrome"]), StatsPeriod.All, now, DayOfWeek.Monday);
        Assert.Equal(TimeSpan.FromSeconds(200), summary.Total);
        Assert.Equal("歌", Assert.Single(summary.TopTracks).Name);
    }

    [Fact]
    public void 列出來源_次數多的在前_大小寫合併()
    {
        var listens = new List<Listen> { From("Roon"), From("Roon"), From("Roon"), From("Roon"), From("Chrome"), From("chrome"), From("Chrome"), From("Spotify"), From("") };
        var seen = ListenSources.Seen(listens, []);
        Assert.Equal(["Roon", "Chrome", "Spotify"], seen.Select(x => x.Name));
        Assert.Equal([4, 3, 1], seen.Select(x => x.Count));
        Assert.All(seen, x => Assert.True(x.Counted));
        Assert.Empty(ListenSources.Seen([], []));
        Assert.Empty(ListenSources.Seen([From("")], []));
    }

    [Fact]
    public void 列出來源_最多八個_被排除的一定在內()
    {
        var listens = new List<Listen>();
        for (int i = 0; i < 10; i++)
            for (int n = 0; n < 20 - i; n++) listens.Add(From("P" + i));
        listens.Add(From("Rare"));
        Assert.Equal(8, ListenSources.Seen(listens, []).Count);
        Assert.DoesNotContain(ListenSources.Seen(listens, []), x => x.Name == "Rare");

        var withRare = ListenSources.Seen(listens, ["rare"]);
        Assert.Equal(8, withRare.Count);
        Assert.False(withRare.Single(x => x.Name == "Rare").Counted);
    }

    [Fact]
    public void 點一下切換()
    {
        var once = ListenSources.Toggle([], "Chrome");
        Assert.Equal(["Chrome"], once);
        Assert.Empty(ListenSources.Toggle(once, " chrome"));
        Assert.Equal(["Chrome", "Edge"], ListenSources.Toggle(once, "Edge"));
        Assert.Empty(ListenSources.Toggle([], ""));
    }

    // ---------------------------------------------------------------- 不記錄

    private static Track Playing(string source) =>
        new(source, source, "歌", "人", "專輯", null, true, TimeSpan.Zero, default, TimeSpan.FromSeconds(200));

    [Fact]
    public void 被排除的來源_不寫檔也不通知()
    {
        var path = Path.Combine(Path.GetTempPath(), "shima-sources-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var log = new ListeningLog(path, _ => { }) { Accepts = s => ListenSources.Counts(s, ["Chrome"]) };
            var started = new List<string>();
            var done = new List<string>();
            log.Started += t => started.Add(t.Source);
            log.Listened += l => done.Add(l.Source);
            var t0 = new DateTime(2026, 11, 23, 21, 0, 0);
            foreach (var source in new[] { "Chrome", "Roon" })
            {
                for (int i = 0; i <= 40; i++) log.Observe(Playing(source), t0.AddSeconds(i));
                log.Observe(null, t0.AddSeconds(41));
                t0 = t0.AddMinutes(5);
            }
            Assert.Equal(["Roon"], started);
            Assert.Equal(["Roon"], done);
            Assert.Equal(["Roon"], HistoryFile.Read(path).Select(l => l.Source));
        }
        finally { File.Delete(path); }
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

public class RecapCardTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 20, 0, 0);
    private static readonly CultureInfo Tw = CultureInfo.GetCultureInfo("zh-TW"), En = CultureInfo.GetCultureInfo("en-US"), Ja = CultureInfo.GetCultureInfo("ja-JP");

    private static Ranked R(string name, string detail, int minutes, int plays = 3) => new(name, detail, TimeSpan.FromMinutes(minutes), plays);

    private static StatsSummary Summary(StatsPeriod period = StatsPeriod.Month, int tracks = 5, int artists = 5, int totalMinutes = 750, int plays = 40, int streak = 6)
    {
        var (from, to) = Stats.Range(period, Now, DayOfWeek.Monday, new DateTime(2025, 3, 2));
        var daily = new List<(DateTime, TimeSpan)>();
        for (var d = from; d < to; d = d.AddDays(1)) daily.Add((d, TimeSpan.FromMinutes((d.Day * 37 % 90) + (d.Day % 4 == 0 ? 0 : 10))));
        return new StatsSummary(from, to, TimeSpan.FromMinutes(totalMinutes), plays,
            Enumerable.Range(0, tracks).Select(i => R($"歌{i}", $"藝人{i}", 200 - i * 20)).ToList(),
            Enumerable.Range(0, artists).Select(i => R($"藝人{i}", "", 300 - i * 30)).ToList(),
            [], daily, new TimeSpan[24], streak);
    }

    private static RecapContent Content(StatsSummary s, StatsPeriod p, CultureInfo c) => RecapCard.Content(s, p, Now, c, x => x)!;

    [Fact]
    public void Empty_period_gives_no_card()
    {
        Assert.Null(RecapCard.Content(Summary(plays: 0), StatsPeriod.Week, Now, Tw, x => x));
        Assert.Null(RecapCard.Render(Summary(plays: 0), StatsPeriod.Week, Now, null));
    }

    [Fact]
    public void Titles_follow_the_culture()
    {
        Assert.Equal("2026年11月", Content(Summary(StatsPeriod.Month), StatsPeriod.Month, Tw).Period);
        Assert.Equal("November 2026", Content(Summary(StatsPeriod.Month), StatsPeriod.Month, En).Period);
        Assert.Equal("2026年11月", Content(Summary(StatsPeriod.Month), StatsPeriod.Month, Ja).Period);
        Assert.Equal("2026", Content(Summary(StatsPeriod.Year), StatsPeriod.Year, En).Period);
        Assert.Equal("全部", Content(Summary(StatsPeriod.All), StatsPeriod.All, En).Period);
        // 週：從週一到週日
        Assert.Equal("2026 · November 23 – November 29", Content(Summary(StatsPeriod.Week), StatsPeriod.Week, En).Period);
        Assert.Equal("2026 · 11月23日 – 11月29日", Content(Summary(StatsPeriod.Week), StatsPeriod.Week, Ja).Period);
    }

    [Fact]
    public void Time_is_minutes_under_an_hour_and_hours_with_minutes_above()
    {
        Assert.Equal("38 分", Content(Summary(totalMinutes: 38), StatsPeriod.Month, Tw).Total);
        Assert.Equal("12 小時 30 分", Content(Summary(totalMinutes: 750), StatsPeriod.Month, Tw).Total);
        Assert.Equal("1 分", Content(Summary(totalMinutes: 0), StatsPeriod.Month, Tw).Total);
        var rows = Content(Summary(), StatsPeriod.Month, Tw);
        Assert.Equal("3.3 小時", rows.Tracks[0].Time);
        Assert.Equal("3 小時", rows.Tracks[1].Time);
        Assert.Equal("40 次", rows.Plays);
        Assert.Equal("連續 6 天", rows.Streak);
        Assert.Equal("", Content(Summary(streak: 0), StatsPeriod.Month, Tw).Streak);
    }

    [Fact]
    public void Ranks_are_cut_to_five_tracks_and_three_artists()
    {
        var content = Content(Summary(tracks: 5, artists: 5), StatsPeriod.Month, Tw);
        Assert.Equal(5, content.Tracks.Count);
        Assert.Equal(3, content.Artists.Count);
        Assert.Equal("藝人0", content.Tracks[0].Detail);
        Assert.Equal(2, Content(Summary(tracks: 2, artists: 1), StatsPeriod.Month, Tw).Tracks.Count);
    }

    [Fact]
    public void Render_draws_a_1200_by_630_card()
    {
        Sta(() =>
        {
            var s = Summary(StatsPeriod.Month) with
            {
                TopTracks =
                [
                    R("START:DASH!!", "μ's", 190), R("ユメノトビラ", "μ's", 160), R("This is a very long song title that should be cut off before it reaches the edge of the card", "Some Artist With A Very Long Name Too", 120),
                    R("夜に駆ける", "YOASOBI", 90), R("愛にできることはまだあるかい", "RADWIMPS", 50),
                ],
                TopArtists = [R("μ's", "", 350), R("Aqours", "", 200), R("YOASOBI と 米津玄師 と その他たくさんの名前が続く長い名前", "", 90)],
            };
            var px = new byte[64 * 64 * 4];
            for (int i = 0; i < px.Length; i += 4) { px[i] = 200; px[i + 1] = (byte)(40 + i / 4 % 64 * 2); px[i + 2] = (byte)(60 + i / 4 / 64 * 3); px[i + 3] = 255; }
            var cover = BitmapSource.Create(64, 64, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, 64 * 4);
            cover.Freeze();
            foreach (var (period, art) in new[] { (StatsPeriod.Week, (ImageSource?)null), (StatsPeriod.Month, cover), (StatsPeriod.Year, null), (StatsPeriod.All, cover) })
            {
                var card = RecapCard.Render(Summary(period) with { TopTracks = s.TopTracks, TopArtists = s.TopArtists }, period, Now, art);
                Assert.NotNull(card);
                Assert.Equal(1200, card!.PixelWidth);
                Assert.Equal(630, card.PixelHeight);
                if (Environment.GetEnvironmentVariable("SHIMA_RECAP_PNG") is { Length: > 0 } dir)
                {
                    Directory.CreateDirectory(dir);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(card));
                    using var file = File.Create(Path.Combine(dir, $"recap-{period}.png"));
                    encoder.Save(file);
                }
            }
        });
    }

    [Fact]
    public void Render_draws_a_past_month_without_today_or_streak()
    {
        Sta(() =>
        {
            var (from, to) = Stats.Range(StatsPeriod.Month, Now, DayOfWeek.Monday, 1);
            var daily = Stats.Daily([], from, to).Select(d => (d.Day, TimeSpan.FromMinutes(d.Day.Day * 37 % 90 + 10))).ToList();
            var s = Summary(StatsPeriod.Month, streak: 0) with { From = from, To = to, Streak = 0, Daily = daily };
            Assert.Equal("2026年10月", RecapCard.Content(s, StatsPeriod.Month, Now, Tw, x => x)!.Period);
            Assert.Equal("", RecapCard.Content(s, StatsPeriod.Month, Now, Tw, x => x)!.Streak);
            var card = RecapCard.Render(s, StatsPeriod.Month, Now, null);
            Assert.NotNull(card);
            if (Environment.GetEnvironmentVariable("SHIMA_RECAP_PNG") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(card!));
                using var file = File.Create(Path.Combine(dir, "recap-PrevMonth.png"));
                encoder.Save(file);
            }
        });
    }

    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception x) { error = x; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

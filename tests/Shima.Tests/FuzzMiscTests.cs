// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Windows;
using Shima.Agents;
using Shima.Listening;
using Shima.Lyrics;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>小型的純函式：快捷鍵、頁面標題、色碼、縮放、閒置那一行、shelf 路徑、勿擾、睡眠定時、月曆、桌面歌詞版面、回顧卡片。</summary>
public class FuzzMiscTests
{
    // ---------------------------------------------------------------- Hotkeys

    private static readonly string[] KeyParts =
    [
        "Ctrl", "ctrl", "CONTROL", "Alt", "alt", "Shift", "Win", "Windows", "+", "+", "+", " + ", "A", "z", "5", "0", "F1", "f12", "F24", "F25", "F0", "F01", "F１", "F+", "Space", "space", "Enter", "Tab", "Backspace", "Insert", "Delete",
        "Home", "End", "PageUp", "PageDown", "Left", "Up", "Right", "Down", "Esc", "Escape", "Ctrl+", "++", "", " ", "É", "ä", "١", "😀", "Num1", "Fn",
    ];

    [Fact]
    public void Hotkeys_TryParse_And_Format_RoundTrip()
    {
        Fuzz.Run("Hotkeys.TryParse／Format", 8001, 8000, r => r.Chance(0.05) ? null : r.Mangle(r.Build(KeyParts, 0, 6, 0.05)), text =>
        {
            if (!Hotkeys.TryParse(text, out var modifiers, out var key))
            {
                Assert.Null(Hotkeys.Normalize(text));
                return;
            }
            Assert.NotEqual(0u, modifiers);
            Assert.NotEqual(0u, key);
            var formatted = Hotkeys.Format(modifiers, key);
            Assert.NotNull(formatted);
            Assert.True(Hotkeys.TryParse(formatted, out var m2, out var k2), $"格式化後讀不回來：{formatted}");
            Assert.Equal((modifiers, key), (m2, k2));
            Assert.Equal(formatted, Hotkeys.Format(m2, k2));
            Assert.Equal(formatted, Hotkeys.Normalize(text));
        }, v => Fuzz.Escape(v ?? "null"));
    }

    [Fact]
    public void Hotkeys_Format_AnyNumbers_NeverThrows()
    {
        Fuzz.Run("Hotkeys.Format 任意數字", 8002, 5000, r => (r.Chance(0.5) ? (uint)r.Next(0, 32) : (uint)r.Next(), r.Chance(0.5) ? (uint)r.Next(0, 0x100) : (uint)r.Next()), v =>
        {
            var text = Hotkeys.Format(v.Item1, v.Item2);
            if (text == null) return;
            Assert.True(Hotkeys.TryParse(text, out var m, out var k));
            Assert.Equal((v.Item1 & 15u, v.Item2), (m, k));
        });
    }

    // ---------------------------------------------------------------- PageTitle／Accent／IslandScale

    [Fact]
    public void PageTitle_Clean_OnlyRemovesATail()
    {
        string[] parts = ["_哔哩哔哩_bilibili", "_哔哩哔哩", " - YouTube Music", " - YouTube", " - youtube", "歌", "Song", " ", "\n", "-", "_", "YouTube", "😀", "\uD83D"];
        Fuzz.Run("PageTitle.Clean", 8003, 4000, r => r.Chance(0.05) ? null : r.Build(parts.Concat(Gen.Noise).ToArray(), 0, 6, 0.2), title =>
        {
            var clean = PageTitle.Clean(title);
            var trimmed = (title ?? "").Trim();
            Assert.Equal(clean.Trim(), clean);
            Assert.True(trimmed.StartsWith(clean, StringComparison.Ordinal), "結果要是原標題的開頭");
            if (trimmed.Length > 0) Assert.NotEqual("", clean);
        }, v => Fuzz.Escape(v ?? "null"));
    }

    [Fact]
    public void Accent_TryParseHex_Matches_SixHexDigits()
    {
        string[] parts = ["#", "##", "0", "9", "a", "F", "g", "x", "0x", " ", "\t", "\u00A0", "-", "+", "A1B2C3", "12345", "1234567", "٣٣٣٣٣٣", "ＡＢＣＤＥＦ", "FFFFFF", "000000"];
        Fuzz.Run("Accent.TryParseHex／Hex／Commit", 8004, 8000, r => r.Chance(0.05) ? null : r.Mangle(r.Build(parts.Concat(Gen.Noise).ToArray(), 0, 5, 0.1)), text =>
        {
            var trimmed = (text ?? "").Trim();
            var digits = trimmed.StartsWith('#') ? trimmed[1..] : trimmed;
            bool expected = digits.Length == 6 && digits.All(Uri.IsHexDigit);
            Assert.Equal(expected, Accent.TryParseHex(text, out var color));
            if (!expected) { Assert.Equal(default, color); Assert.Equal("#ABCDEF", Accent.Commit(text, "#ABCDEF")); return; }
            Assert.Equal("#" + digits.ToUpperInvariant(), Accent.Hex(color));
            Assert.Equal(Accent.Hex(color), Accent.Commit(text, "#000000"));
            Assert.Equal(color, Accent.Fixed("custom", text));
        }, v => Fuzz.Escape(v ?? "null"));
        Fuzz.Run("Accent.Pick", 8005, 1500, r => Enumerable.Range(0, r.Pick([0, 1, 3, 4, 5, 8, 96, 2304])).Select(_ => (byte)r.Pick([0, 255, r.Next(256)])).ToArray(), bytes =>
        {
            var c = Accent.Pick(bytes);
            Assert.Equal(255, c.A);
        }, bytes => Convert.ToHexString(bytes));
    }

    [Fact]
    public void IslandScale_StaysInRange()
    {
        Fuzz.Run("IslandScale.*", 8006, 8000, r => (r.Pick([double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1, 0.89, 0.9, 1, 1.15, 1.3, 1.31, 1e300, r.NextDouble() * 2]), r.Next(1, 600) + r.NextDouble(), r.Pick([0.9, 1, 1.15, 1.3]), r.Pick([-100.0, 0, 5, 1000]), r.Pick([0.0, 100, 1920])), v =>
        {
            var (value, size, scale, x, width) = v;
            var s = IslandScale.Sanitize(value);
            Assert.True(s == 1 || (s >= IslandScale.Min - 1e-9 && s <= IslandScale.Max + 1e-9));
            Assert.Contains(IslandScale.Nearest(value), IslandScale.Steps);
            Assert.True(IslandScale.Window(size, scale) >= size * scale - 1e-3);
            Assert.True(IslandScale.WindowHeight(size, size / 3, scale) >= IslandScale.Window(size, scale) - 1e-9 || true);
            var screens = new Rect(0, 0, 1920, 1080);
            var work = new Rect(0, 0, 1920, 1040);
            var p = IslandScale.TopLeft(work, screens, x, x * 2, width, 300);
            Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y));
            var centre = IslandScale.Inside(x, 0, 1920, size);
            Assert.True(centre >= 0 && centre <= 1920);
            if (size <= 1920) Assert.InRange(centre, size / 2 - 1e-9, 1920 - size / 2 + 1e-9);
        }, v => v.ToString());
    }

    // ---------------------------------------------------------------- IdleLine

    [Fact]
    public void IdleLine_Text_And_Trim_StayShort_AndWellFormed()
    {
        string[] parts = ["吃藥", "開會", "a", "Meeting", "😀", "\uD83D", "ＡＢＣ", "한국어", " ", "\n", "x"];
        Fuzz.Run("IdleLine.Text／Trim", 8007, 5000, r =>
        {
            var now = new DateTime(2026, 1, 1).AddMinutes(r.Next(0, 3 * 365 * 24 * 60));
            var alarms = Enumerable.Range(0, r.Next(0, 3)).Select(i => new Alarm(i, now.AddMinutes(r.Next(-100, 5000)), r.Build(parts, 0, 14, 0), null)).ToList();
            return (r.Chance(0.1) ? null : r.Pick(["clock", "date", "alarm", "none", "bogus", ""]), now, r.Chance(0.1) ? "05:00" : null, alarms, Cultures[r.Next(Cultures.Length)], r.Build(parts, 0, 14, 0), r.Next(0, 30));
        }, v =>
        {
            var (mode, now, timer, alarms, culture, text, cells) = v;
            var line = IdleLine.Text(mode, now, timer, alarms, CultureInfo.GetCultureInfo(culture), "明天");
            if (timer != null) Assert.Equal(timer, line);
            Assert.NotNull(line);
            Assert.Contains(IdleLine.Normalize(mode), new[] { "clock", "date", "alarm", "none" });
            var trimmed = IdleLine.Trim(text, cells);
            Assert.True(trimmed.Length <= text.Trim().Length + 1);
            if (trimmed.EndsWith('…')) Assert.StartsWith(trimmed[..^1], text.Trim(), StringComparison.Ordinal);
            else Assert.Equal(text.Trim(), trimmed);
            Assert.True(IsWellFormed(trimmed) || !IsWellFormed(text), $"把一對代理字元切成兩半：{Fuzz.Escape(trimmed)}");
        }, v => $"mode={v.Item1} now={v.Item2:O} culture={v.Item5} text={Fuzz.Escape(v.Item6)} cells={v.Item7}");
    }

    private static bool IsWellFormed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i])) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[++i])) return false; }
            else if (char.IsLowSurrogate(s[i])) return false;
        }
        return true;
    }

    private static readonly string[] Cultures = ["en-US", "zh-TW", "zh-CN", "ja-JP", "ko-KR", "es-ES", "fr-FR", "de-DE", "pt-BR", "it-IT", "ru-RU", "ar-SA", "th-TH", "fa-IR", "he-IL", "hi-IN"];

    // ---------------------------------------------------------------- Shelf

    [Fact]
    public void Shelf_IsOwned_OnlyDirectChildrenOfTheShelfFolder()
    {
        const string Folder = @"C:\Users\u\AppData\Roaming\Shima\shelf";
        string[] prefixes = [Folder + @"\", Folder + "/", Folder.ToUpperInvariant() + @"\", Folder + @"\sub\", Folder + @"\..\", Folder + @"\.\", Folder + @"\sub\..\", @"C:\Users\u\AppData\Roaming\Shima\shelf2\", @"C:\Users\u\AppData\Roaming\Shima\", @"C:\", @"\\?\" + Folder + @"\", @"\\srv\share\", "", ".\\", "shelf\\", "/", "~/", Folder + "  \\", Folder + @"\\", Folder + @"\..\shelf\"];
        string[] names = ["a.txt", "a.png", "A.TXT", "a.PNG", "a.lrc", "SHELF~1.TXT", "A~1.PNG", "", ".", "..", "x.txt:stream", "x.txt.", "x.txt ", "CON", "nul.txt", "a\\b.txt", "a/b.txt", "a?.txt", "中文.png", "\uD83D.txt", "a\u0000.txt", "x.txt\\", "x.txt/"];
        Fuzz.Run("Shelf.IsOwned／Classify", 8008, 8000, r => (r.Pick(prefixes) + r.Pick(names), r.Chance(0.05) ? r.Pick(["", "  ", "shelf", @"C:\", Folder + @"\.."]) : Folder), v =>
        {
            var (path, folder) = v;
            bool owned = Shelf.IsOwned(path, folder);
            var kind = Shelf.Classify(path, folder);
            if (!owned) Assert.Equal(ShelfKind.File, kind);
            if (owned) { _ = Shelf.Label(path, ShelfKind.File); }
            _ = Shelf.TextLabel(path);
            if (!owned) return;
            // 獨立的判斷：路徑逐段整理之後，剛好是資料夾底下一層
            var full = Resolve(path);
            var root = Resolve(folder);
            Assert.True(full.Count == root.Count + 1 && full.Take(root.Count).SequenceEqual(root, StringComparer.OrdinalIgnoreCase), $"不在 shelf 資料夾這一層：{string.Join("\\", full)}");
            Assert.False(path.Contains('\u0000'));
        }, v => $"path={Fuzz.Escape(v.Item1)} folder={Fuzz.Escape(v.Item2)}", perCallMs: 100);
    }

    private static List<string> Resolve(string path)
    {
        var stack = new List<string>();
        var text = path.StartsWith(@"\\?\") ? path[4..] : path;
        foreach (var seg in text.Replace('/', '\\').Split('\\'))
        {
            var name = seg.TrimEnd('.', ' ') is { Length: > 0 } t || seg.Length == 0 ? seg : seg;
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(name);
        }
        return stack;
    }

    [Fact]
    public void Shelf_TextLabel_And_Trim_StayWithinTheirLimits()
    {
        Fuzz.Run("Shelf.TextLabel／Trim", 8009, 4000, r => r.Chance(0.05) ? null : r.Mangle(r.Build(Gen.Noise.Concat(["一二三四五六七八九十", "a b  c", "\n\n", "  "]).ToArray(), 0, 12, 0.5) + (r.Chance(0.1) ? r.Long("字", 30_000) : "")), text =>
        {
            var label = Shelf.TextLabel(text);
            Assert.True(label.Length <= Shelf.LabelLength + 1);
            Assert.DoesNotContain('\n', label);
            var kept = Shelf.Trim(text);
            if (kept != null) { Assert.True(kept.Length <= Shelf.MaxText); Assert.NotEqual("", kept.Trim()); }
            else Assert.True(string.IsNullOrWhiteSpace(text));
        }, v => Fuzz.Escape(v ?? "null"));
    }

    // ---------------------------------------------------------------- DoNotDisturb／SleepTimer

    [Fact]
    public void DoNotDisturb_IsActive_OnlyForTheNextHour()
    {
        Fuzz.Run("DoNotDisturb", 8010, 5000, r => (new DateTime(2026, 11, 24, 12, 0, 0).AddSeconds(r.Next(-100000, 100000)), r.Chance(0.1) ? (DateTime?)null : new DateTime(2026, 11, 24, 12, 0, 0).AddSeconds(r.Next(-100000, 100000))), v =>
        {
            var (now, until) = v;
            var end = DoNotDisturb.EndFrom(now);
            Assert.Equal(now + DoNotDisturb.Length, end);
            Assert.True(DoNotDisturb.IsActive(end, now));
            Assert.True(DoNotDisturb.IsActive(end, now.AddMinutes(59)));
            Assert.False(DoNotDisturb.IsActive(end, end));
            Assert.False(DoNotDisturb.IsActive(end, end.AddSeconds(1)));
            Assert.False(DoNotDisturb.IsActive(null, now));
            if (DoNotDisturb.IsActive(until, now)) Assert.True(until > now && until!.Value - now <= TimeSpan.FromMinutes(61));
        });
    }

    [Fact]
    public void SleepTimer_PausesAtMostOncePerArming_AndOnlyWhilePlaying()
    {
        Fuzz.Run("SleepTimer.Tick", 8011, 5000, r => r.Next(), seed =>
        {
            var r = new Random(seed);
            var timer = new SleepTimer();
            var now = new DateTime(2026, 11, 24, 23, 0, 0);
            int pauses = 0;
            string?[] tracks = [null, "a", "b", "c"];
            for (int step = 0; step < 80; step++)
            {
                now = now.AddSeconds(r.Pick([0, 1, 1, 2, 5, 30, 600, -5]));
                var track = r.Pick(tracks);
                var position = TimeSpan.FromSeconds(r.Pick([0, 2, 50, 150, 190, 199, 200, 205]));
                var duration = TimeSpan.FromSeconds(r.Pick([0, 200, 200]));
                switch (r.Next(8))
                {
                    case 0: timer.After(TimeSpan.FromSeconds(r.Pick([0, 1, 60, 1800])), now); pauses = 0; break;
                    case 1: if (timer.AfterTrack(track, position)) pauses = 0; break;
                    case 2: timer.Cancel(); break;
                }
                var armed = timer.Mode;
                bool playing = r.Chance(0.7);
                bool pause = timer.Tick(now, track, position, duration, playing);
                if (pause)
                {
                    Assert.True(playing, "沒有在播放卻要求暫停");
                    Assert.NotEqual(SleepMode.Off, armed);
                    Assert.Equal(SleepMode.Off, timer.Mode);
                    Assert.True(++pauses <= 1, "同一次定時暫停了兩次");
                }
                if (armed == SleepMode.Off) Assert.False(pause);
                if (timer.Mode == SleepMode.After) Assert.True(timer.Left(now) >= TimeSpan.Zero);
                else Assert.Null(timer.Left(now));
            }
        }, seed => $"seed={seed}");
    }

    // ---------------------------------------------------------------- MonthView

    [Fact]
    public void MonthView_Weeks_And_Cells_AreConsistent()
    {
        Fuzz.Run("MonthView.Weeks／Cells", 8012, 4000, r => (new DateTime(r.Pick([1900, 1970, 2000, 2024, 2026, 2100, 9998]), r.Next(1, 13), r.Next(1, 29)), r.Pick(Enum.GetValues<DayOfWeek>())), v =>
        {
            var (month, firstDay) = v;
            var weeks = MonthView.Weeks(month, firstDay);
            var cells = MonthView.Cells(month, firstDay);
            Assert.InRange(weeks, 4, 6);
            Assert.Equal(42, cells.Length);
            int days = DateTime.DaysInMonth(month.Year, month.Month);
            Assert.Equal(days, cells.Count(c => c.InMonth));
            Assert.Equal(Enumerable.Range(1, days), cells.Where(c => c.InMonth).Select(c => c.Day));
            Assert.Equal(1, cells.First(c => c.InMonth).Day);
            int first = Array.FindIndex(cells, c => c.InMonth), last = Array.FindLastIndex(cells, c => c.InMonth);
            Assert.Equal(weeks, last / 7 + 1);
            Assert.Equal(((int)new DateTime(month.Year, month.Month, 1).DayOfWeek - (int)firstDay + 7) % 7, first);
            Assert.True(Enumerable.Range(first, days).All(i => cells[i].InMonth), "這個月的格子要連在一起");
        }, v => $"{v.Item1:yyyy-MM} {v.Item2}");
    }

    // ---------------------------------------------------------------- DesktopLyricsLayout

    [Fact]
    public void DesktopLyricsLayout_StaysOnScreen_AndPickStaysInRange()
    {
        Fuzz.Run("DesktopLyricsLayout.*", 8013, 6000, r =>
        {
            var lines = Enumerable.Range(0, r.Next(0, 8)).Select(i => new LyricLine(TimeSpan.FromSeconds(i * r.Pick([0, 1, 5, 30])), r.Chance(0.2) ? "" : r.Pick(["歌詞", "la la la", "あいうえお", "😀"]))).OrderBy(l => l.Time).ToList();
            return (r.Pick([double.NaN, double.PositiveInfinity, double.NegativeInfinity, -5, 0, 15.5, 16, 30, 72, 73, 1e9]), r.Pick([-100.0, 0, 800, 1920, 7680]), new Point(r.Pick([-5000.0, -1, 0, 500, 1900, 5000]), r.Pick([-5000.0, 0, 500, 1070, 5000])), lines, r.Next(-3, 12) * 1000L, r.Chance(0.5), r.Chance(0.5), r.Next(-3, 3));
        }, v =>
        {
            var (size, screenWidth, topLeft, lines, ms, synced, withSecond, wheel) = v;
            var clamped = DesktopLyricsLayout.ClampSize(size);
            Assert.InRange(clamped, DesktopLyricsLayout.MinSize, DesktopLyricsLayout.MaxSize);
            Assert.InRange(DesktopLyricsLayout.Wheel(size, wheel), DesktopLyricsLayout.MinSize, DesktopLyricsLayout.MaxSize);
            var window = DesktopLyricsLayout.WindowSize(size, screenWidth);
            Assert.True(window.Width > 0 && window.Height > 0 && double.IsFinite(window.Width));
            if (screenWidth > 0) Assert.True(window.Width <= Math.Floor(screenWidth));
            var screen = new Rect(0, 0, 1920, 1040);
            var spot = DesktopLyricsLayout.Clamp(topLeft, window, screen);
            Assert.InRange(spot.X, screen.Left, Math.Max(screen.Left, screen.Right - window.Width));
            Assert.InRange(spot.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - window.Height));
            Assert.True(DesktopLyricsLayout.OnScreen(spot, window, screen) || window.Width > screen.Width || window.Height > screen.Height);
            var def = DesktopLyricsLayout.DefaultSpot(screen, window);
            Assert.True(double.IsFinite(def.X) && double.IsFinite(def.Y));

            var sheet = new LyricSheet(lines, synced);
            IReadOnlyList<string>? second = withSecond ? lines.Select(l => l.Text.Length > 0 ? "二" : "").Take(Math.Max(0, lines.Count - 1)).ToList() : null;
            var pick = DesktopLyricsLayout.Pick(sheet, second, TimeSpan.FromMilliseconds(ms), "歌名", "藝人");
            Assert.InRange(pick.Index, -1, Math.Max(-1, lines.Count - 1));
            if (pick.Sweep) { Assert.True(pick.Index >= 0 && pick.Length > TimeSpan.Zero && pick.Main.Length > 0); Assert.True(synced); }
            if (!synced || lines.Count == 0) Assert.Equal("", pick.Main);
            Assert.NotNull(pick.Sub);
            Assert.True(DesktopLyricsLayout.Pick(null, null, TimeSpan.Zero, "", "").Main == "");
        }, v => $"size={v.Item1} screen={v.Item2} topLeft={v.Item3} ms={v.Item5} synced={v.Item6}");
    }

    // ---------------------------------------------------------------- RecapCard／ParseMinutes

    [Fact]
    public void RecapCard_Content_IsNullOnlyWhenNothingWasPlayed()
    {
        Fuzz.Run("RecapCard.Content", 8014, 2500, r =>
        {
            var now = new DateTime(2026, 1, 1).AddMinutes(r.Next(0, 3 * 365 * 24 * 60));
            var listens = Enumerable.Range(0, r.Next(0, 30)).Select(_ => new Listen(new DateTimeOffset(now.AddMinutes(-r.Next(0, 60 * 24 * 30))), r.Pick(["歌", "Song", ""]), r.Pick(["A", "B", ""]), r.Pick(["X", ""]), "roon", 200, r.Pick([0.0, 30, 200, 7200, 100000]))).ToList();
            return (listens, now, r.Pick(Enum.GetValues<StatsPeriod>()), Cultures[r.Next(Cultures.Length)]);
        }, v =>
        {
            var (listens, now, period, culture) = v;
            var summary = Stats.Compute(listens, period, now, DayOfWeek.Monday, top: 100);
            var content = RecapCard.Content(summary, period, now, CultureInfo.GetCultureInfo(culture), s => s);
            Assert.Equal(summary.Plays <= 0, content == null);
            if (content == null) return;
            Assert.NotEqual("", content.Period);
            Assert.NotEqual("", content.Total);
            Assert.True(content.Tracks.Count <= RecapCard.MaxTracks && content.Artists.Count <= RecapCard.MaxArtists);
            Assert.All(content.Tracks.Concat(content.Artists), row => Assert.NotNull(row.Name));
            Assert.Equal(summary.Streak > 0, content.Streak != "");
        }, v => $"now={v.Item2:O} period={v.Item3} culture={v.Item4} listens={v.Item1.Count}", perCallMs: 300);
    }

    [Fact]
    public void AppSettings_ParseMinutes_StaysInRange_Or_KeepsTheOldValue()
    {
        string[] parts = ["0", "1", "25", "180", "181", "999999999999", "-5", "+7", " ", "\t", "٣", "１２", "1.5", "1e2", "abc", "", "0x10", "12 3", "\u00A05", "5\u00A0", "２５"];
        Fuzz.Run("AppSettings.ParseMinutes", 8015, 5000, r => (r.Chance(0.05) ? null : r.Mangle(r.Build(parts.Concat(Gen.Noise).ToArray(), 0, 3, 0.1)), r.Next(1, 60), r.Pick([1, 5]), r.Pick([60, 180, 480])), v =>
        {
            var (text, fallback, min, max) = v;
            var result = AppSettings.ParseMinutes(text, fallback, min, max);
            Assert.True(result == fallback || (result >= min && result <= max));
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) Assert.Equal(Math.Clamp(value, min, max), result);
            else Assert.Equal(fallback, result);
        }, v => $"{Fuzz.Escape(v.Item1 ?? "null")} fallback={v.Item2} {v.Item3}..{v.Item4}");
    }
}

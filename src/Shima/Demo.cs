// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shima.Agents;
using Shima.Listening;
using Shima.Lyrics;
using Shima.Playback;

namespace Shima;

/// <summary>
/// 示範模式（--demo）：歌、歌詞、聆聽紀錄、待辦、提醒全部是編出來的，設定放在暫存資料夾。
/// 給 README 的截圖用：畫面上不會有自己正在聽的東西與個人設定，也不連 Roon、不讀系統的媒體資訊，
/// 所以可以跟平常在用的那一份同時開著。
/// </summary>
public static class Demo
{
    public static bool On { get; private set; }

    private static readonly DateTimeOffset Started = DateTimeOffset.Now;

    /// <summary>要在讀設定之前呼叫：把資料夾換成暫存的，放進編好的內容。</summary>
    public static void Prepare()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Shima-demo");
        try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { /* 上一次的還開著：照用 */ }
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable(AppSettings.FolderVariable, folder);
        On = true;

        var now = DateTime.Now;
        var s = AppSettings.Current;   // 資料夾是空的，這裡拿到的是全新的設定
        s.RoonHost = "";
        s.CheckUpdates = false;
        s.Welcomed = true;
        s.Todos =
        [
            new TodoItem { Text = "把新歌加進歌單", Done = true },
            new TodoItem { Text = "回覆設計稿的意見" },
            new TodoItem { Text = "訂週六的電影票" },
        ];

        Directory.CreateDirectory(Shelf.Folder);
        var notes = Path.Combine(folder, "會議記錄.md");
        File.WriteAllText(notes, "# 會議記錄\n");
        s.Shelf.Add(notes);
        if (Shelf.SaveText("週五前把簡報的第三頁換成新的數字", Shelf.Folder, now) is { } text) s.Shelf.Add(text);
        s.Save();

        File.WriteAllLines(HistoryFile.DefaultPath, Listens(now).Select(HistoryFile.ToLine));
        File.WriteAllText(Path.Combine(folder, "alarms.json"), Alarms.Serialize(
        [
            new Alarm(1, now.Date.AddDays(now.Hour >= 15 ? 1 : 0).AddHours(15).AddMinutes(30), "跟設計開會"),
            new Alarm(2, now.AddMinutes(20), "喝水"),
        ]));
        var favorites = new LyricFavorites();
        favorites.Add("ひかりは水面でほどけていく", "光在水面上慢慢散開", Title, Artist, Album, TimeSpan.FromSeconds(52), now.AddDays(-2));
        favorites.Add("窓をあけたら 夏のにおい", "打開窗，是夏天的味道", "午後四時の窓辺", "Minato Loop", "Slow Window", TimeSpan.FromSeconds(31), now.AddDays(-6));
        favorites.Add("Keep the porch light on for me", "", "Porch Light", "Harbor & Pine", "Small Hours", TimeSpan.FromSeconds(74), now.AddDays(-9));
    }

    private const string Title = "ひかりの汽水域", Artist = "潮見ノート", Album = "汽水域";

    /// <summary>正在播的那首：一分多鐘的地方，照實際時間往後走。</summary>
    public static Track Track() =>
        new("Roon", "roon", Title, Artist, Album, Cover(), true, TimeSpan.FromSeconds(48), Started, TimeSpan.FromSeconds(252));

    /// <summary>歌詞是為了示範寫的，不是任何一首真的歌。</summary>
    public static LyricsHit Lyrics()
    {
        (int At, string Line, string Meaning)[] lines =
        [
            (6, "朝のバスは まだ眠そうで", "早晨的公車 還一臉睡意"),
            (12, "イヤホンの片方を きみに渡した", "把耳機的一邊 遞給了你"),
            (19, "川と海が まざるあたりで", "在河與海 交會的那一帶"),
            (25, "言葉はすこし しょっぱくなる", "話語變得 有一點鹹"),
            (33, "", ""),
            (38, "ひかりは水面でほどけていく", "光在水面上慢慢散開"),
            (45, "わたしたちの影も ゆれている", "我們的影子 也跟著搖晃"),
            (52, "ここが どちらでもない場所なら", "如果這裡 是哪一邊都不算的地方"),
            (59, "どちらへでも 行ける気がした", "就覺得 往哪裡去都可以"),
            (67, "", ""),
            (72, "帰り道は 遠回りして", "回家的路 特地繞遠"),
            (79, "知らない橋を ひとつ渡った", "走過一座 沒走過的橋"),
            (86, "潮の匂いが 髪にのこる", "潮水的味道 留在髮梢"),
            (93, "あしたもきっと 思い出すだろう", "明天一定 還會想起來"),
        ];
        var sheet = new LyricSheet(lines.Select(l => new LyricLine(TimeSpan.FromSeconds(l.At), l.Line)).ToList(), true)
        {
            Translation = lines.Select(l => l.Meaning).ToList(),
        };
        return new LyricsHit(sheet, true, "local");
    }

    /// <summary>畫一張封面：河海交界的顏色，幾道光。</summary>
    private static BitmapSource Cover()
    {
        const int size = 480;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var sky = new LinearGradientBrush(Color.FromRgb(0x1E, 0x3A, 0x5F), Color.FromRgb(0xE8, 0x9A, 0x7B), 90);
            sky.GradientStops.Insert(1, new GradientStop(Color.FromRgb(0x4F, 0x86, 0xA6), 0.55));
            dc.DrawRectangle(sky, null, new Rect(0, 0, size, size));
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xE0, 0xFF, 0xE3, 0xB8), Color.FromArgb(0, 0xFF, 0xE3, 0xB8)), null, new Point(size * 0.68, size * 0.60), 170, 170);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xD6)), null, new Point(size * 0.68, size * 0.60), 46, 46);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0xD0, 0x16, 0x2C, 0x4A), Color.FromArgb(0xFF, 0x0E, 0x1B, 0x30), 90), null, new Rect(0, size * 0.66, size, size * 0.34));
            for (int i = 0; i < 7; i++)
            {
                double y = size * 0.70 + i * 20, w = 150 - i * 16;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(0xB0 - i * 0x14), 0xFF, 0xE3, 0xB8)), null, new Rect(size * 0.68 - w / 2, y, w, 4), 2, 2);
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>假的頻譜：幾個慢慢動的波疊起來，低音高、高音低，每次截圖看起來都像在播。</summary>
    public static float[] Bands(int count)
    {
        double t = (DateTimeOffset.Now - Started).TotalSeconds;
        var bands = new float[count];
        for (int i = 0; i < count; i++)
        {
            double x = i / (double)Math.Max(1, count - 1);
            double wave = 0.5 + 0.28 * Math.Sin(t * 2.1 + x * 9) + 0.16 * Math.Sin(t * 3.7 + x * 23) + 0.08 * Math.Sin(t * 5.3 + x * 41);
            bands[i] = (float)Math.Clamp((1 - x * 0.62) * wave + 0.16, 0.05, 1);
        }
        return bands;
    }

    public static float Level => 0.45f + 0.2f * (float)Math.Sin((DateTimeOffset.Now - Started).TotalSeconds * 2.1);

    /// <summary>過去五個星期的聆聽紀錄：固定的亂數種子，每次產生的都一樣。</summary>
    private static IEnumerable<Listen> Listens(DateTime now)
    {
        (string Title, string Artist, string Album, int Seconds, int Weight)[] songs =
        [
            (Title, Artist, Album, 252, 9),
            ("午後四時の窓辺", "Minato Loop", "Slow Window", 214, 7),
            ("Porch Light", "Harbor & Pine", "Small Hours", 198, 6),
            ("雨宿りのワルツ", "潮見ノート", "汽水域", 231, 5),
            ("紙飛機與晚霞", "林間電台", "放學後", 243, 5),
            ("Night Bus Home", "Minato Loop", "Slow Window", 187, 4),
            ("薄荷色的星期天", "林間電台", "放學後", 205, 3),
            ("Tidewater", "Harbor & Pine", "Small Hours", 266, 3),
            ("カーテンコール", "Aoi Kessel", "幕間", 221, 2),
        ];
        var random = new Random(20261123);
        int total = songs.Sum(s => s.Weight);
        for (int day = 34; day >= 0; day--)
        {
            // 週末聽得多，有幾天沒聽；最近七天每天都有，「連續幾天」才有數字
            int plays = day < 7 ? 3 + random.Next(5) : random.Next(10) < 3 ? 0 : 1 + random.Next(now.AddDays(-day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 9 : 5);
            var at = now.Date.AddDays(-day).AddHours(8 + random.Next(4));
            for (int i = 0; i < plays; i++)
            {
                int pick = random.Next(total);
                var song = songs.First(s => (pick -= s.Weight) < 0);
                at = at.AddSeconds(song.Seconds + random.Next(600));
                if (at >= now) yield break;
                yield return new Listen(new DateTimeOffset(at), song.Title, song.Artist, song.Album, "Roon", song.Seconds, song.Seconds * (0.7 + random.NextDouble() * 0.3));
            }
        }
    }
}

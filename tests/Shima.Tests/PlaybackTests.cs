// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json.Nodes;
using Shima.Motion;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class MooTests
{
    [Fact]
    public void 帶內容的請求來回不變()
    {
        var bytes = Moo.Build("REQUEST", "com.roonlabs.transport:2/control", "7", new JsonObject { ["control"] = "next", ["title"] = "夜に駆ける" });
        var msg = Moo.Parse(bytes)!;
        Assert.Equal(("REQUEST", "com.roonlabs.transport:2/control", "7"), (msg.Verb, msg.Name, msg.RequestId));
        Assert.Equal("夜に駆ける", msg.Body!["title"]!.ToString());
    }

    [Fact]
    public void 沒有內容就只有標頭()
    {
        Assert.Equal("MOO/1 COMPLETE Success\nRequest-Id: 3\n\n", Encoding.UTF8.GetString(Moo.Build("COMPLETE", "Success", "3")));
        var msg = Moo.Parse("MOO/1 REQUEST com.roonlabs.ping:1/ping\nLogging: quiet\nRequest-Id: 12\n\n"u8)!;
        Assert.Equal(("com.roonlabs.ping:1/ping", "12"), (msg.Name, msg.RequestId));
        Assert.Null(msg.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HTTP/1.1 200 OK\n\n")]
    [InlineData("MOO/1 COMPLETE Success\n\n")]
    [InlineData("MOO/1 CONTINUE Changed\nRequest-Id: 1\nContent-Length: 50\nContent-Type: application/json\n\n{}")]
    public void 壞掉的訊息丟掉(string text) => Assert.Null(Moo.Parse(Encoding.UTF8.GetBytes(text)));
}

public class RoonZoneTests
{
    private static JsonObject Zone(string id, string state, bool track = true)
    {
        var z = new JsonObject { ["zone_id"] = id, ["state"] = state };
        if (track) z["now_playing"] = new JsonObject();
        return z;
    }

    [Fact]
    public void 顯示正在播的_而且不亂換()
    {
        JsonObject[] zones = [Zone("a", "paused"), Zone("b", "playing"), Zone("c", "playing")];
        Assert.Equal("b", RoonSource.Pick(zones, "")!["zone_id"]!.ToString());
        Assert.Equal("c", RoonSource.Pick(zones, "c")!["zone_id"]!.ToString());
        Assert.Equal("b", RoonSource.Pick(zones, "a")!["zone_id"]!.ToString());
    }

    [Fact]
    public void 暫停的留著_直到別的開始播()
    {
        JsonObject[] zones = [Zone("a", "paused"), Zone("b", "paused"), Zone("c", "stopped", track: false)];
        Assert.Equal("b", RoonSource.Pick(zones, "b")!["zone_id"]!.ToString());
        Assert.Null(RoonSource.Pick([Zone("c", "stopped", track: false)], "c"));
    }

    [Fact]
    public void 都暫停時_控制最後在播的那個()
    {
        var idle = Zone("a", "paused"); idle["now_playing"]!["seek_position"] = 0.0;
        var listening = Zone("b", "paused"); listening["now_playing"]!["seek_position"] = 88.0;
        JsonObject[] zones = [idle, listening];
        Assert.Equal("b", RoonSource.Pick(zones, "")!["zone_id"]!.ToString());
        Assert.Equal("b", RoonSource.Pick(zones, "a", lastPlayedId: "b")!["zone_id"]!.ToString());
        Assert.Equal("a", RoonSource.Pick(zones, "b", lastPlayedId: "b", pinnedId: "a")!["zone_id"]!.ToString());
    }

    [Fact]
    public void 指定的區域沒在播時_跟著正在播的()
    {
        JsonObject[] zones = [Zone("a", "paused"), Zone("b", "playing")];
        Assert.Equal("b", RoonSource.Pick(zones, "a", pinnedId: "a")!["zone_id"]!.ToString());
        JsonObject[] both = [Zone("a", "playing"), Zone("b", "playing")];
        Assert.Equal("a", RoonSource.Pick(both, "b", pinnedId: "a")!["zone_id"]!.ToString());
    }
}

public class TrackTests
{
    private static Track T(bool playing, double position, double secondsAgo, double duration = 200) =>
        new("x", "x", "t", "a", "", null, playing, TimeSpan.FromSeconds(position), DateTimeOffset.Now.AddSeconds(-secondsAgo), TimeSpan.FromSeconds(duration));

    [Fact]
    public void 播放中往後推算_暫停就停住_不超過長度()
    {
        Assert.InRange(T(true, 10, 5).Now.TotalSeconds, 14.9, 15.5);
        Assert.Equal(10, T(false, 10, 5).Now.TotalSeconds);
        Assert.Equal(200, T(true, 199, 30).Now.TotalSeconds);
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    public void 播放器識別碼轉成名字(string id, string name) => Assert.Equal(name, SmtcSource.AppName(id));

    [Theory]
    [InlineData("Roon.exe", true)]
    [InlineData("com.roon.Roon", true)]
    [InlineData("Chrome", false)]
    public void 認得_Roon(string id, bool roon) => Assert.Equal(roon, MediaHub.IsRoon(id));
}

public class SpringTests
{
    [Fact]
    public void 會停在目標上_而且不會爆開()
    {
        var s = new Spring(36) { Target = 260 };
        double peak = 0;
        for (int i = 0; i < 240 && !s.Settled; i++) { s.Step(1 / 60.0); peak = Math.Max(peak, s.Value); }
        Assert.True(s.Settled);
        Assert.Equal(260, s.Value);
        Assert.InRange(peak, 260, 300);
    }

    [Fact]
    public void 卡頓的一大步也穩定()
    {
        var s = new Spring(0) { Target = 100 };
        s.Step(0.5);
        Assert.InRange(s.Value, 50, 150);
    }
}

public class SingingTests
{
    private static double L(string text, double start, double? next) =>
        Singing.Length(text, TimeSpan.FromSeconds(start), next is { } n ? TimeSpan.FromSeconds(n) : null).TotalSeconds;

    [Fact]
    public void 光填滿到下一句之前的時間_留一口氣() => Assert.Equal(4.6, L("これからを咲き誇れと サクラの香りが呼ぶ", 10, 15), 3);

    [Fact]
    public void 長間奏不讓光慢慢爬() => Assert.InRange(L("Lalala..", 0, 60), 2.5, 6);

    [Fact]
    public void 最後一句照字數() => Assert.InRange(L("夢を見ようよ whoa", 300, null), 2.5, 12);
}

public class AudioMeterTests
{
    [Fact]
    public void 浮點與整數取樣的最大振幅()
    {
        var floats = new byte[12];
        BitConverter.GetBytes(0.1f).CopyTo(floats, 0);
        BitConverter.GetBytes(-0.8f).CopyTo(floats, 4);
        BitConverter.GetBytes(0.3f).CopyTo(floats, 8);
        Assert.Equal(0.8f, AudioMeter.Peak(floats, 12, 32), 3);

        var ints = new byte[4];
        BitConverter.GetBytes((short)-16384).CopyTo(ints, 0);
        BitConverter.GetBytes((short)8192).CopyTo(ints, 2);
        Assert.Equal(0.5f, AudioMeter.Peak(ints, 4, 16), 3);
        Assert.Equal(0, AudioMeter.Peak([], 0, 32));
    }
}

public class AccentTests
{
    private static byte[] Fill(int n, params (byte R, byte G, byte B)[] colors)
    {
        var px = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            var (r, g, b) = colors[i % colors.Length];
            px[i * 4] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r; px[i * 4 + 3] = 255;
        }
        return px;
    }

    [Fact]
    public void 鮮豔的顏色勝過大片暗色()
    {
        var c = Shima.Accent.Pick(Fill(100, (8, 8, 10), (8, 8, 10), (8, 8, 10), (230, 40, 40)));
        Assert.True(c.R > c.G * 1.5 && c.R > c.B * 1.5, c.ToString());
        Assert.Equal(255, c.R);
    }

    [Fact]
    public void 灰階封面用白色() => Assert.Equal(Shima.Accent.Default, Shima.Accent.Pick(Fill(100, (40, 40, 40), (200, 200, 200))));
}

public class DiscordPresenceTests
{
    [Theory]
    [InlineData("a", "a​")]
    [InlineData("  歌名  ", "歌名")]
    [InlineData("", "")]
    public void 文字補到至少兩個字元(string text, string fitted) => Assert.Equal(fitted, DiscordPresence.Fit(text));

    [Fact]
    public void 太長的文字截短_而且不切斷_emoji()
    {
        Assert.Equal(128, DiscordPresence.Fit(new string('あ', 300)).Length);
        var fitted = DiscordPresence.Fit(new string('x', 125) + "🎵🎵🎵");
        Assert.True(fitted.Length <= 128);
        Assert.False(char.IsHighSurrogate(fitted[^2]));
    }

    [Fact]
    public void Activity_的內容()
    {
        var a = DiscordPresence.Activity("Dream Believers", "蓮ノ空", "104th Collection", 1000, TimeSpan.FromSeconds(317), "https://example.com/c.jpg");
        Assert.Equal(2, a["type"]!.GetValue<int>());
        Assert.Equal("Dream Believers", a["details"]!.ToString());
        Assert.Equal("蓮ノ空", a["state"]!.ToString());
        Assert.Equal(318000, a["timestamps"]!["end"]!.GetValue<long>());
        Assert.Equal("104th Collection", a["assets"]!["large_text"]!.ToString());

        var bare = DiscordPresence.Activity("歌", "", "", 1000, TimeSpan.Zero, "http://not-https/c.jpg");
        Assert.Null(bare["state"]);
        Assert.Null(bare["assets"]);
        Assert.Null(bare["timestamps"]!["end"]);
    }

    [Theory]
    [InlineData("1234567890123456789", true)]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("12345678901234567x9", false)]
    public void 認得_Application_ID(string id, bool valid) => Assert.Equal(valid, DiscordPresence.ValidId(id));

    /// <summary>每次寫入只寫一半就讓出執行緒，模擬管線寫到一半時另一邊也來寫。</summary>
    private sealed class SlowStream : System.IO.MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
        {
            int half = buffer.Length / 2;
            base.Write(buffer.Span[..half]);
            await Task.Delay(5, cancel);
            base.Write(buffer.Span[half..]);
        }
    }

    [Fact]
    public async Task 同時送狀態和回_Pong_封包不會交錯()
    {
        using var pipe = new SlowStream();
        var gate = new SemaphoreSlim(1, 1);
        var writes = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            DiscordPresence.WriteAsync(pipe, gate, i % 2 == 0 ? 1 : 4, new JsonObject { ["n"] = i, ["pad"] = new string('x', 50 + i) }))).ToArray();
        await Task.WhenAll(writes);

        // 從頭一個一個封包讀回來：每個都要是完整、看得懂的 JSON，而且剛好 20 個
        var bytes = pipe.ToArray();
        var seen = new HashSet<int>();
        int at = 0;
        while (at < bytes.Length)
        {
            int length = BitConverter.ToInt32(bytes, at + 4);
            var json = JsonNode.Parse(Encoding.UTF8.GetString(bytes, at + 8, length))!;
            Assert.True(seen.Add((int)json["n"]!));
            at += 8 + length;
        }
        Assert.Equal(20, seen.Count);
    }

    [Fact]
    public void 沒開始過也可以停_停兩次也沒事()
    {
        var presence = new DiscordPresence("1234567890123456789");
        presence.Stop();
        presence.Dispose();
    }
}

public class PomodoroTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 10, 0, 0);

    [Fact]
    public void 開始_暫停_繼續()
    {
        var p = new Shima.Pomodoro();
        Assert.True(p.Fresh);
        p.Toggle(T0);
        Assert.Equal(TimeSpan.FromMinutes(20), p.Left(T0.AddMinutes(5)));
        p.Toggle(T0.AddMinutes(5));
        Assert.False(p.Running);
        Assert.Equal(TimeSpan.FromMinutes(20), p.Left(T0.AddHours(3)));
        p.Toggle(T0.AddHours(3));
        Assert.Equal(TimeSpan.FromMinutes(19), p.Left(T0.AddHours(3).AddMinutes(1)));
    }

    [Fact]
    public void 時間到就換成休息_再換回專注()
    {
        var p = new Shima.Pomodoro();
        p.Toggle(T0);
        Assert.False(p.Tick(T0.AddMinutes(24)));
        Assert.True(p.Tick(T0.AddMinutes(25)));
        Assert.True(p.Resting);
        Assert.Equal(TimeSpan.FromMinutes(5), p.Left(T0.AddMinutes(25)));
        Assert.True(p.Tick(T0.AddMinutes(30)));
        Assert.False(p.Resting);
    }

    [Fact]
    public void 重來回到完整的一段()
    {
        var p = new Shima.Pomodoro();
        p.Toggle(T0);
        p.Tick(T0.AddMinutes(26));
        p.Reset();
        Assert.True(p.Fresh);
        Assert.False(p.Resting);
        Assert.Equal(TimeSpan.FromMinutes(25), p.Left(T0.AddHours(1)));
    }
}

public class SweepTests
{
    [Fact]
    public void 漢字比假名久_英文字母很快_空白是換氣()
    {
        Assert.True(Singing.Weight('夢') > Singing.Weight('ゆ'));
        Assert.True(Singing.Weight('ゆ') > Singing.Weight('a'));
        Assert.True(Singing.Weight(' ') > Singing.Weight('ゆ'));
        Assert.True(Singing.Weight('っ') < Singing.Weight('つ'));
    }

    [Fact]
    public void 唱到第幾個字()
    {
        Assert.Equal(0, Singing.CharsAt("ああああ", 0));
        Assert.Equal(2, Singing.CharsAt("ああああ", 0.5), 6);
        Assert.Equal(4, Singing.CharsAt("ああああ", 1), 6);
        // 漢字佔的時間比較多：一半的時間還沒唱完第一個字後面的假名
        Assert.True(Singing.CharsAt("夢ああ", 0.5) < 1.5);
        Assert.Equal(0, Singing.CharsAt("", 0.5));
    }

    [Fact]
    public void 換成寬度的比例()
    {
        double[] prefixes = [0, 10, 20, 30, 40];
        Assert.Equal(0.5, Singing.WidthShare("ああああ", 0.5, prefixes), 6);
        Assert.Equal(1, Singing.WidthShare("ああああ", 1, prefixes), 6);
    }
}

public class NotificationTests
{
    [Theory]
    [InlineData("Discord", true)]
    [InlineData("LINE", true)]
    [InlineData("Telegram Desktop", true)]
    [InlineData("Microsoft Edge", false)]
    [InlineData("", false)]
    public void 只看清單上的通訊軟體(string app, bool wanted) =>
        Assert.Equal(wanted, Shima.NotificationMirror.Wanted(app, ["Discord", "LINE", "Telegram"]));

    [Fact]
    public void 膠囊上的一行()
    {
        Assert.Equal("Discord · 小明：晚上打 LoL 嗎", Shima.NotificationMirror.Line("Discord", "小明", "晚上打 LoL 嗎"));
        Assert.Equal("LINE · 新訊息", Shima.NotificationMirror.Line("LINE", "新訊息", ""));
        Assert.Equal(60, Shima.NotificationMirror.Line("Discord", "小明", new string('哈', 200)).Length);
    }
}

public class LocTests
{
    private static string Table()
    {
        using var stream = typeof(Shima.Loc).Assembly.GetManifestResourceStream("Shima.Assets.strings.tsv")!;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void 每一句在每種語言都有翻譯_參數也一樣()
    {
        var lines = Table().Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var codes = lines[0].Split('\t');
        Assert.Equal(Shima.Loc.Languages.Select(l => l.Code), codes);
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split('\t');
            Assert.Equal(codes.Length, cells.Length);
            var marks = System.Text.RegularExpressions.Regex.Matches(cells[0], @"\{\d+\}").Select(m => m.Value).Order().ToList();
            foreach (var cell in cells)
            {
                Assert.False(string.IsNullOrWhiteSpace(cell), cells[0]);
                Assert.Equal(marks, System.Text.RegularExpressions.Regex.Matches(cell, @"\{\d+\}").Select(m => m.Value).Order().ToList());
            }
        }
    }

    [Fact]
    public void 讀出指定語言那一欄()
    {
        var en = Shima.Loc.Parse(Table(), "en");
        Assert.Equal("Settings", en["設定"]);
        Assert.Equal("設定", Shima.Loc.Parse(Table(), "ja")["設定"]);
        Assert.Empty(Shima.Loc.Parse(Table(), "xx"));
    }

    [Theory]
    [InlineData("", "zh-TW", "zh-TW")]
    [InlineData("", "zh-HK", "zh-TW")]
    [InlineData("", "zh-CN", "zh-CN")]
    [InlineData("", "zh-Hans-SG", "zh-CN")]
    [InlineData("", "ja-JP", "ja")]
    [InlineData("", "ko-KR", "ko")]
    [InlineData("", "pt-BR", "pt")]
    [InlineData("", "th-TH", "en")]
    [InlineData("ja", "en-US", "ja")]
    [InlineData("nonsense", "fr-FR", "fr")]
    public void 跟著_Windows_的語言_沒有的用英文(string setting, string windows, string expected) =>
        Assert.Equal(expected, Shima.Loc.Resolve(setting, System.Globalization.CultureInfo.GetCultureInfo(windows)));
}

public class MonthViewTests
{
    [Fact]
    public void 月曆的四十二格()
    {
        var cells = Shima.MonthView.Cells(new DateTime(2026, 11, 23), DayOfWeek.Sunday);
        Assert.Equal(42, cells.Length);
        Assert.Equal((1, true), cells[0]);            // 2026-11-01 是星期日
        Assert.Equal((30, true), cells[29]);
        Assert.Equal((1, false), cells[30]);
        var monday = Shima.MonthView.Cells(new DateTime(2026, 11, 23), DayOfWeek.Monday);
        Assert.Equal((26, false), monday[0]);         // 一週從星期一開始：前面補上個月的 26–31
        Assert.Equal((1, true), monday[6]);
    }
}

public class SystemWatchTests
{
    [Fact]
    public void 各種狀態的一行字()
    {
        Assert.Equal("音量 42", Shima.SystemWatch.Line("volume", "42"));
        Assert.Equal("充電中 80%", Shima.SystemWatch.Line("charging", "80"));
        Assert.Equal("已連上 Home", Shima.SystemWatch.Line("online", "Home"));
        Assert.Equal("網路已連線", Shima.SystemWatch.Line("online", ""));
        Assert.Equal("輸出：耳機", Shima.SystemWatch.Line("device", "耳機"));
    }
}

public class SpectrumTests
{
    private static float[] Tone(double hz, double gain = 0.8) =>
        Enumerable.Range(0, Spectrum.Size).Select(i => (float)(gain * Math.Sin(2 * Math.PI * hz * i / 48000))).ToArray();

    [Fact]
    public void 安靜就是零() => Assert.All(Spectrum.Bands(new float[Spectrum.Size], 48000, 24), b => Assert.Equal(0, b));

    [Fact]
    public void 低音在左邊_高音在右邊()
    {
        var low = Spectrum.Bands(Tone(100), 48000, 24);
        var high = Spectrum.Bands(Tone(8000), 48000, 24);
        Assert.True(Array.IndexOf(low, low.Max()) < 6);
        Assert.True(Array.IndexOf(high, high.Max()) > 17);
        Assert.True(low.Max() > 0.8f && high.Max() > 0.8f);
    }

    [Fact]
    public void 小聲的比大聲的矮() =>
        Assert.True(Spectrum.Bands(Tone(1000, 0.05), 48000, 24).Max() < Spectrum.Bands(Tone(1000, 0.8), 48000, 24).Max() - 0.2f);

    /// <summary>改成查表與快取之前的寫法，原封不動抄過來：重構後的結果要跟它每一個位元都一樣。</summary>
    private static float[] Original(float[] samples, int sampleRate, int count)
    {
        const int Size = Spectrum.Size;
        var re = new double[Size];
        var im = new double[Size];
        for (int i = 0; i < Size; i++) re[i] = samples[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1)));
        int n = Size;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len, wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
        var bands = new float[count];
        double low = 50, high = Math.Min(14000, sampleRate / 2.0 - 1), perBin = (double)sampleRate / Size;
        for (int b = 0; b < count; b++)
        {
            double from = low * Math.Pow(high / low, (double)b / count), to = low * Math.Pow(high / low, (double)(b + 1) / count);
            int first = Math.Clamp((int)Math.Floor(from / perBin), 1, Size / 2 - 1), last = Math.Clamp((int)Math.Ceiling(to / perBin), first + 1, Size / 2);
            double peak = 0;
            for (int k = first; k < last; k++) peak = Math.Max(peak, Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
            double db = 20 * Math.Log10(peak / (Size / 4.0) + 1e-9);
            db += 9.0 * b / count;
            bands[b] = (float)Math.Clamp((db + 60) / 60, 0, 1);
        }
        return bands;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    [InlineData(96000)]
    [InlineData(16000)]
    public void 查表與快取不改變結果(int rate)
    {
        var random = new Random(7);
        var noise = Enumerable.Range(0, Spectrum.Size).Select(_ => (float)(random.NextDouble() * 2 - 1) * 0.3f).ToArray();
        var mixed = Tone(220).Zip(Tone(5000, 0.2), (a, b) => a + b).ToArray();
        double[] re = new double[Spectrum.Size], im = new double[Spectrum.Size];
        foreach (var samples in new[] { noise, mixed, Tone(60), new float[Spectrum.Size] })
            foreach (int count in new[] { 1, 4, 24, 56, 64, 96 })
            {
                var expected = Original(samples, rate, count);
                Assert.Equal(expected, Spectrum.Bands(samples, rate, count));
                Assert.Equal(expected, Spectrum.Bands(samples, rate, count, re, im));   // 第二次走快取的頻段邊界、重複用工作區
            }
    }

    [Fact]
    public void 每次回傳新的陣列()
    {
        var a = Spectrum.Bands(Tone(1000), 48000, 24);
        var b = Spectrum.Bands(Tone(1000), 48000, 24);
        Assert.NotSame(a, b);
    }
}

public class UpdaterTests
{
    [Theory]
    [InlineData("v0.6.0", "0.6.0")]
    [InlineData("0.5.1", "0.5.1")]
    [InlineData("v1.0.0", "1.0.0")]
    public void 比較新的版本(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), Shima.Updater.Newer(tag, new Version(0, 5, 0, 0)));

    [Theory]
    [InlineData("v0.5.0")]
    [InlineData("v0.4.9")]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void 一樣_比較舊_或看不懂的都不算(string? tag) => Assert.Null(Shima.Updater.Newer(tag, new Version(0, 5, 0, 0)));

    [Theory]
    [InlineData("https://github.com/Carinoasd/shima/releases/download/v0.6.1/Shima.exe", true)]
    [InlineData("http://github.com/Carinoasd/shima/releases/download/v0.6.1/Shima.exe", false)]
    [InlineData("https://github.com/Carinoasd/shima/archive/main.zip", false)]
    [InlineData("https://github.com/Someone/shima/releases/download/v9/Shima.exe", false)]
    [InlineData("https://github.com.evil.example/Carinoasd/shima/releases/download/v9/Shima.exe", false)]
    [InlineData("https://user@github.com/Carinoasd/shima/releases/download/v9/Shima.exe", false)]
    [InlineData("https://github.com:8443/Carinoasd/shima/releases/download/v9/Shima.exe", false)]
    [InlineData("https://github.com/Carinoasd/shima/releases/download/../../../evil/x.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void 只認這個專案發佈頁的下載連結(string? url, bool ok) => Assert.Equal(ok, Shima.Updater.ValidUrl(url));

    [Fact]
    public void 雜湊的格式()
    {
        var hex = new string('a', 64);
        Assert.Equal(hex, Shima.Updater.Sha256Of("sha256:" + hex.ToUpperInvariant()));
        Assert.Null(Shima.Updater.Sha256Of("sha1:" + hex));
        Assert.Null(Shima.Updater.Sha256Of("sha256:abc"));
        Assert.Null(Shima.Updater.Sha256Of(null));
    }

    private static JsonObject Latest(string tag, string? digest, string url = "https://github.com/Carinoasd/shima/releases/download/v9.0.0/Shima.exe") => new()
    {
        ["tag_name"] = tag,
        ["assets"] = new JsonArray(new JsonObject { ["name"] = "Shima.exe", ["browser_download_url"] = url, ["size"] = 88000000, ["digest"] = digest }),
    };

    [Fact]
    public void 沒有雜湊或網址不對的新版不裝()
    {
        var sha = "sha256:" + new string('b', 64);
        var now = new Version(0, 6, 0, 0);
        Assert.Equal(88000000, Shima.Updater.Pick(Latest("v9.0.0", sha), now)!.Size);
        Assert.Null(Shima.Updater.Pick(Latest("v9.0.0", null), now));
        Assert.Null(Shima.Updater.Pick(Latest("v9.0.0", sha, "https://example.com/Shima.exe"), now));
        Assert.Null(Shima.Updater.Pick(Latest("v0.6.0", sha), now));
    }

    [Fact]
    public void 下載的檔案要跟發佈的大小與雜湊都一樣()
    {
        var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "update-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            var path = System.IO.Path.Combine(folder, "Shima.exe.new");
            var bytes = new byte[6 * 1024 * 1024];
            bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; bytes[100] = 7;
            System.IO.File.WriteAllBytes(path, bytes);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            Assert.True(Shima.Updater.Matches(path, bytes.Length, sha));
            Assert.False(Shima.Updater.Matches(path, bytes.Length + 1, sha));                       // 大小不對（下載斷掉）
            Assert.False(Shima.Updater.Matches(path, bytes.Length, new string('0', 64)));           // 內容不對
            bytes[100] = 8; System.IO.File.WriteAllBytes(path, bytes);
            Assert.False(Shima.Updater.Matches(path, bytes.Length, sha));                           // 被改過一個位元組
        }
        finally { System.IO.Directory.Delete(folder, true); }
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>歌詞檔、逐字時間、歌名整理、AI 翻譯：隨機輸入測試。</summary>
public class FuzzLyricsTests
{
    private static readonly string[] LrcParts =
    [
        "[00:12.34]", "[00:00.00]", "[1:2]", "[01:02.5]", "[00:12:34]", "[99:59.999]", "[12:34]", "[00:12.34][00:30.00]", "[00:00.000]",
        "[offset:+250]", "[offset:-300]", "[offset: 0 ]", "[ar:歌手]", "[ti:歌名]", "[al:專輯]", "[by:誰]", "[shima:translation,romaji]", "[shima:translation]",
        "<00:12.00>", "<00:12.40>", "<1:2.5>", "<00:13:00>", "<99:99.99>",
        "作词 : 某某", "词：某某", "歌名 - 藝人", "[00:00.00]歌名 - 藝人", "[00:01.00]作曲 : 誰",
        "[", "]", "<", ">", "[]", "[::]", "[00:", "[a:b]", "[00:12.34", "00:12.34]",
        "[99999999999:00]", "[00:99999999999999999999]", "[00:12.99999999999999999999]", "<99999999999:00>", "[offset:99999999999999999999]", "[offset:-1e9]",
        "[00:12.34]<00:12.34>字<00:12.80>字<00:13.20>", "[٣:٠٠]", "[０１:０２]", "<٣:٠٠>", "[٠,٣]", "(٣,٤,٠)", "[00:15.00]<00:15.00>あ<00:15.50>い ", "[00:20.00]翻譯", "[00:20.00]romaji",
    ];

    private static string LrcInput(Random r)
    {
        var text = r.Build(LrcParts.Concat(Gen.Noise).Concat(Gen.Noise).ToArray(), 0, 40, 0.3);
        if (r.Chance(0.05)) text += r.Long("[00:01.00]la\n", 3000);
        if (r.Chance(0.03)) text += r.Long("[", 5000) + r.Long("<", 5000);
        return r.Chance(0.6) ? r.Mangle(text) : text;
    }

    // ---------------------------------------------------------------- Lrc

    [Fact]
    public void Lrc_Parse_NeverThrows_SortedNonNegative_WordsFit()
    {
        Fuzz.Run("Lrc.Parse／ParseSheet／PlainText／StripCredits／Write", 1001, 800, LrcInput, text =>
        {
            var lines = Lrc.Parse(text);
            for (int i = 0; i < lines.Count; i++)
            {
                Assert.True(lines[i].Time >= TimeSpan.Zero, "時間是負的");
                if (i > 0) Assert.True(lines[i - 1].Time <= lines[i].Time, "時間沒有遞增");
                if (lines[i].Words is { } words)
                {
                    Assert.True(WordTiming.Fits(lines[i].Text, words), "逐字資料接起來不等於整句");
                    Assert.All(words, w => Assert.True(w.Start >= TimeSpan.Zero && w.Length >= TimeSpan.Zero));
                }
            }

            var sheet = Lrc.ParseSheet(text);
            Assert.True(sheet.Lines.Count <= Lrc.Parse(text.Replace("\r", "")).Count);   // ParseSheet 先拿掉 \r，「[0\r0:20]」才會變成時間
            if (sheet.Translation != null) Assert.Equal(sheet.Lines.Count, sheet.Translation.Count);
            if (sheet.Romaji != null) Assert.Equal(sheet.Lines.Count, sheet.Romaji.Count);
            Lrc.PlainText(sheet);
            Lrc.StripCredits(text, "歌名");
            Lrc.StripCredits(text);

            // 寫出去再讀回來，句數不能變（同一個時間的兩句寫的時候會挪開，不會被當成翻譯）。
            // 行中間的 \r 不算：ParseSheet 一律先拿掉，「[0\r0:20]」寫出去再讀就變成時間了，檔案裡不會有這種東西
            var plain = Lrc.ParseSheet(text.Replace("\r", ""));
            // 時間標記拿掉之後剛好湊成新的標記（「[[00:01]00:02]」）：那是病態輸入，寫出去再讀自然會多出句子
            if (plain.Lines.Any(l => l.Text.Contains('[') || l.Text.Contains('<'))) return;
            var written = Lrc.Write(plain, TimeSpan.FromMilliseconds(r(text)), "t[]\n", "a\r");
            Assert.Equal(plain.Lines.Count, Lrc.ParseSheet(written).Lines.Count);

            static int r(string s) => s.Length % 7 * 100;
        }, perCallMs: 300);
    }

    [Fact]
    public void Lrc_LineAt_And_Align_StayInRange()
    {
        Fuzz.Run("Lrc.LineAt／Align", 1002, 3000, r =>
        {
            var lines = Enumerable.Range(0, r.Next(0, 12)).Select(_ => new LyricLine(TimeSpan.FromMilliseconds(r.Next(0, 20000)), r.Chance(0.2) ? "" : r.CleanText(1, 3))).OrderBy(l => l.Time).ToList();
            var other = Enumerable.Range(0, r.Next(0, 12)).Select(_ => new LyricLine(TimeSpan.FromMilliseconds(r.Next(0, 20000)), r.Chance(0.2) ? "" : r.CleanText(1, 3))).OrderBy(l => l.Time).ToList();
            if (r.Chance(0.3) && lines.Count > 0) other.Add(lines[r.Next(lines.Count)] with { Text = "同時" });
            other = other.OrderBy(l => l.Time).ToList();
            return (lines, other, TimeSpan.FromMilliseconds(r.Next(-100, 21000)));
        }, v =>
        {
            var (lines, other, t) = v;
            int i = Lrc.LineAt(lines, t);
            Assert.InRange(i, -1, lines.Count - 1);
            if (i >= 0) Assert.True(lines[i].Time <= t);
            if (i + 1 < lines.Count) Assert.True(lines[i + 1].Time > t);
            if (i < 0 && lines.Count > 0) Assert.True(lines[0].Time > t);

            var aligned = Lrc.Align(lines, other);
            if (aligned != null)
            {
                Assert.Equal(lines.Count, aligned.Count);
                Assert.Contains(aligned, s => s.Length > 0);
            }
        }, v => $"lines={string.Join("|", v.lines.Select(l => $"{l.Time.TotalMilliseconds}:{l.Text}"))} other={string.Join("|", v.other.Select(l => $"{l.Time.TotalMilliseconds}:{l.Text}"))} t={v.Item3.TotalMilliseconds}");
    }

    private static LyricSheet RandomSheet(Random r)
    {
        var lines = new List<LyricLine>();
        long at = r.Next(0, 3000);
        for (int n = r.Next(0, 9); n > 0; n--)
        {
            at += r.Next(1, 6000);
            string text = r.Chance(0.1) ? "" : r.CleanText(1, 4);
            IReadOnlyList<LyricWord>? words = null;
            if (text.Length > 0 && r.Chance(0.4))
            {
                // 在字元邊界切成幾段，每段一個字：空白跟著前一個字
                var cuts = Enumerable.Range(1, text.Length - 1).Where(_ => r.Chance(0.3)).ToList();
                var parts = new List<string>();
                int from = 0;
                foreach (int cut in cuts.Append(text.Length)) { if (cut > from) parts.Add(text[from..cut]); from = cut; }
                if (parts.All(p => p.Trim().Length > 0) && parts[0].TrimStart() == parts[0] && parts.Skip(1).All(p => p.TrimStart() == p || true))
                {
                    var made = new List<LyricWord>();
                    long start = at;
                    foreach (var p in parts)
                    {
                        long length = r.Next(10, 900);
                        made.Add(new LyricWord(TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(length), p));
                        start += length + (r.Chance(0.3) ? r.Next(1, 300) : 0);
                    }
                    if (WordTiming.Fit(text, made) is { } fitted) words = fitted;
                }
            }
            lines.Add(new LyricLine(TimeSpan.FromMilliseconds(at), text) { Words = words });
        }
        IReadOnlyList<string>? Layer() => r.Chance(0.4) ? lines.Select(_ => r.Chance(0.3) ? "" : r.CleanText(1, 3)).ToList() : null;
        return new LyricSheet(lines, true) { Translation = Layer(), Romaji = Layer() };
    }

    [Fact]
    public void Lrc_Write_ThenParseSheet_RoundTrips()
    {
        Fuzz.Run("Lrc.Write→ParseSheet 往返", 1003, 2500, RandomSheet, sheet =>
        {
            var written = Lrc.Write(sheet, TimeSpan.Zero, "歌名", "藝人");
            var back = Lrc.ParseSheet(written);
            Assert.Equal(sheet.Lines.Count, back.Lines.Count);
            for (int i = 0; i < sheet.Lines.Count; i++)
            {
                Assert.Equal(sheet.Lines[i].Text, back.Lines[i].Text);
                Assert.Equal(Math.Round(sheet.Lines[i].Time.TotalMilliseconds), Math.Round(back.Lines[i].Time.TotalMilliseconds));
                if (sheet.Lines[i].Words is { } ws)
                {
                    var got = back.Lines[i].Words;
                    Assert.NotNull(got);
                    Assert.Equal(ws.Select(w => (w.Text, Math.Round(w.Start.TotalMilliseconds))), got!.Select(w => (w.Text, Math.Round(w.Start.TotalMilliseconds))));
                }
            }
            static string At(IReadOnlyList<string>? l, int i) => l != null && i < l.Count ? l[i] : "";
            foreach (var (mine, theirs) in new[] { (sheet.Translation, back.Translation), (sheet.Romaji, back.Romaji) })
                for (int i = 0; i < sheet.Lines.Count; i++)
                    Assert.Equal(sheet.Lines[i].Text.Length == 0 ? "" : At(mine, i), At(theirs, i));
            Lrc.PlainText(back);
        }, sheet => Fuzz.Escape(Lrc.Write(sheet, TimeSpan.Zero)));
    }

    // ---------------------------------------------------------------- Words

    private static readonly string[] YrcParts =
    [
        "[1234,567]", "[0,0]", "[99999999999,1]", "[99999999999999999999,5]", "[12,34]", "(1234,200,0)", "(0,0,0)", "(12,34,-1)", "(99999999999999999999,2,0)",
        "字", "あ", "い ", " ", "{\"t\":0,\"c\":[{\"tx\":\"作词: \"}]}", "[", "(", ")", "]", "\n", "\r\n", "(1,2)", "(,,)",
    ];

    [Fact]
    public void Words_ParseYrc_ParseEnhanced_Attach_NeverThrow()
    {
        Fuzz.Run("WordTiming.ParseYrc／ParseEnhanced／Attach／Fit", 1004, 3000, r =>
            r.Build(YrcParts.Concat(LrcParts).Concat(Gen.Noise).ToArray(), 0, 30, 0.3), text =>
        {
            var yrc = WordTiming.ParseYrc(text);
            for (int i = 0; i < yrc.Count; i++)
            {
                if (i > 0) Assert.True(yrc[i - 1].Time <= yrc[i].Time);
                Assert.True(yrc[i].Text.Length > 0);
                if (yrc[i].Words is { } w) Assert.True(WordTiming.Fits(yrc[i].Text, w));
            }
            var (line, words) = WordTiming.ParseEnhanced(text, TimeSpan.FromSeconds(1));
            if (words != null) Assert.True(WordTiming.Fits(line, words));

            var lrc = Lrc.Parse(text);
            var attached = WordTiming.Attach(lrc, yrc);
            Assert.Equal(lrc.Count, attached.Count);
            Assert.Equal(lrc.Select(l => l.Text), attached.Select(l => l.Text));
            foreach (var l in attached) if (l.Words != null) Assert.True(WordTiming.Fits(l.Text, l.Words));
        }, perCallMs: 300);
    }

    [Fact]
    public void Words_At_CharsAt_Progress_StayInRange_And_Json_RoundTrips()
    {
        Fuzz.Run("WordTiming.At／CharsAt／Progress／ToJson／FromJson", 1005, 3000, r =>
        {
            var words = Enumerable.Range(0, r.Next(0, 8)).Select(_ => new LyricWord(TimeSpan.FromMilliseconds(r.Next(0, 5000)), TimeSpan.FromMilliseconds(r.Next(0, 800)), r.CleanText(1, 2))).ToList();
            if (r.Chance(0.8)) words = words.OrderBy(w => w.Start).ToList();
            return (words, TimeSpan.FromMilliseconds(r.Next(-500, 7000)));
        }, v =>
        {
            var (words, t) = v;
            var (index, fraction) = WordTiming.At(words, t);
            if (words.Count == 0) Assert.Equal((0, 0.0), (index, fraction));
            else Assert.InRange(index, 0, words.Count - 1);
            Assert.InRange(fraction, 0.0, 1.0);
            var progress = WordTiming.Progress(words, t);
            Assert.InRange(progress, 0.0, 1.0);
            Assert.True(WordTiming.CharsAt(words, t) >= 0);

            var back = WordTiming.FromJson(WordTiming.ToJson(words));
            Assert.Equal(words.Count, back.Count);
            Assert.Equal(words.Select(w => w.Text), back.Select(w => w.Text));
        }, v => $"{string.Join("|", v.words.Select(w => $"{w.Start.TotalMilliseconds}+{w.Length.TotalMilliseconds}:{w.Text}"))} t={v.Item2.TotalMilliseconds}");
    }

    [Fact]
    public void Words_FromJson_SurvivesBrokenJson()
    {
        string[] pieces = ["[]", "[1,2,\"a\"]", "[1,2]", "[\"x\",2,\"a\"]", "[1,2,3]", "[1e999,2,\"a\"]", "[1e300,1,\"a\"]", "[-1,-2,\"a\"]", "null", "5", "\"s\"", "{}", "[[1,2,\"a\"]]", "[null,null,null]", "[1,2,null]", "[99999999999999999999,2,\"a\"]"];
        Fuzz.Run("WordTiming.FromJson", 1006, 2000, r =>
        {
            var array = new JsonArray();
            for (int n = r.Next(0, 6); n > 0; n--) array.Add(JsonNode.Parse(r.Pick(pieces)));
            return array.ToJsonString();
        }, json => WordTiming.FromJson((JsonArray)JsonNode.Parse(json)!));
    }

    // ---------------------------------------------------------------- Titles

    private static readonly string[] TitleParts =
    [
        "Song", "歌名", "Dream Believers", "(Official Video)", "【MV】", "[4K]", " - Topic", "VEVO", " Official", " (104期Ver.)", "（SAKURA Ver.）", " - Remastered 2011",
        " - ", " – ", " — ", " / ", " ／ ", " feat. ", " ft ", " & ", " x ", ", ", "、", "(", ")", "[", "]", "（", "）", "【", "】", "「", "」", "〈", "＜", "＞", "Ver.", "version", "104", "０４", "①",
    ];

    [Fact]
    public void Titles_NeverThrow_And_KeepTheirPromises()
    {
        Fuzz.Run("Titles.*", 1007, 4000, r => (r.Mangle(r.Build(TitleParts.Concat(Gen.Noise).ToArray(), 0, 14, 0.3)), r.Mangle(r.Build(TitleParts.Concat(Gen.Noise).ToArray(), 0, 6, 0.3)), r.Chance(0.5)), v =>
        {
            var (title, artist, split) = v;
            var norm = Titles.Norm(title);
            Assert.All(norm, c => Assert.True(char.IsLetterOrDigit(c)));
            Assert.Equal(norm, norm.ToLowerInvariant().Length == norm.Length ? norm : norm);
            var bare = Titles.Bare(title);
            Assert.True(bare.Length <= title.Length);
            if (title.Trim().Length > 0) Assert.True(bare.Length > 0, "去掉括號後不能變成空的");
            Titles.VersionTag(title);
            Titles.MainArtist(artist);
            Titles.SameArtist(title, artist);
            var (a, t) = Titles.Clean(artist, title, split);
            Assert.Equal(a.Trim(), a);
            Assert.Equal(t.Trim(), t);
            Assert.False(t.Contains("  ", StringComparison.Ordinal));
            Assert.False(a.Contains("  ", StringComparison.Ordinal));

            var q = new SongQuery(title, artist, "", 200);
            foreach (var k in LocalProvider.Keys(q)) Assert.True(k.Length > 0);
            SongPicker.Pick([new SongCandidate(title, artist, 200), new SongCandidate(t, a, 199, 2), new SongCandidate("", "", 0, 0)], title, artist, 200);
        }, v => $"title={Fuzz.Escape(v.Item1)} artist={Fuzz.Escape(v.Item2)} split={v.Item3}", perCallMs: 200);
    }

    [Fact]
    public void Titles_PathologicalLengths_FinishQuickly()
    {
        string[] units = ["(", "[", "（", "【a】(", "(a)", " ", "-", " - ", "/", " / ", "a-", ", ", "(Official ", "[video] "];
        Fuzz.Run("Titles 病態長輸入", 1008, 60, r =>
        {
            var unit = r.Pick(units);
            return string.Concat(Enumerable.Repeat(unit, r.Next(20, 400))) + (r.Chance(0.5) ? "x" : "");
        }, s =>
        {
            Titles.Bare(s);
            Titles.VersionTag(s);
            Titles.MainArtist(s);
            Titles.Clean(s, s, true);
            Titles.Norm(s);
        }, perCallMs: 500);
    }

    [Fact]
    public void LocalProvider_FileName_IsAlwaysASafeWindowsName()
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM9", "LPT1", "LPT9", "COM0", "LPT0", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³", "con.", "Nul.txt", "aux ", "CONIN$", "CONOUT$", "CLOCK$"];
        string[] parts = ["Song", "歌", " - ", "/", "\\", ":", "*", "?", "\"", "<", ">", "|", ".", " ", "..", "...", "\u0001", "\t", "\uD83D", "😀", "a", "x".PadRight(80, 'x')];
        Fuzz.Run("LocalProvider.FileName", 1009, 6000, r =>
        {
            string title = r.Chance(0.3) ? r.Pick(reserved) + (r.Chance(0.5) ? r.Build(parts, 0, 3) : "") : r.Build(parts.Concat(Gen.Noise).ToArray(), 0, 8, 0.2);
            string artist = r.Chance(0.3) ? r.Pick(reserved) : r.Build(parts.Concat(Gen.Noise).ToArray(), 0, 5, 0.2);
            if (r.Chance(0.1)) title = r.Long("あ", 120);
            if (r.Chance(0.1)) artist = r.Long("😀", 120);
            return new SongQuery(title, artist, "", 0);
        }, q =>
        {
            var name = LocalProvider.FileName(q);
            if (name == null) return;
            Assert.EndsWith(".lrc", name);
            var stem = name[..^4];
            Assert.True(stem.Length > 0 && stem.Trim().Length > 0);
            Assert.True(stem.Length <= 151, "檔名太長");
            Assert.DoesNotContain(stem, c => bad.Contains(c) || char.IsControl(c));
            Assert.False(stem.EndsWith('.') || stem.EndsWith(' '), "結尾的點或空白 Windows 會吃掉");
            Assert.Equal(name, Path.GetFileName(name));
            // 裝置名稱：第一個點之前的部分（去掉結尾空白）不可以是保留字。微軟的檔名文件把 COM¹²³、LPT¹²³ 與 COM0／LPT0 也列進去，
            // 而且 Windows 10 上「NUL .txt」仍然是裝置；這台 Windows 11 實測它們是普通檔案，但最低支援版本是 Windows 10，所以照文件嚴格擋
            var device = stem.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            Assert.False(device is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(device, "^(COM|LPT)[0-9¹²³]$"), $"保留的裝置名稱：{device}");
            Assert.False(stem.Any(char.IsSurrogate) && !IsWellFormed(stem), "檔名裡有孤立的代理字元");
            Assert.NotEmpty(LocalProvider.KeysOf(name).Intersect(LocalProvider.Keys(q)));
        }, q => $"title={Fuzz.Escape(q.Title)} artist={Fuzz.Escape(q.Artist)}");
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

    [Fact]
    public void LyricsFinder_PackUnpack_RoundTrips()
    {
        Fuzz.Run("LyricsFinder.Pack／Unpack", 1010, 1500, r => (RandomSheet(r), r.Chance(0.5), r.Chance(0.2)), v =>
        {
            var (sheet, exact, none) = v;
            var json = LyricsFinder.Pack(none ? null : new LyricsHit(sheet, exact, "lrclib"), DateTime.Now);
            var (hit, _) = LyricsFinder.Unpack(json)!.Value;
            if (none) { Assert.Null(hit); return; }
            Assert.Equal(exact, hit!.Exact);
            Assert.Equal(sheet.Lines.Select(l => l.Text), hit.Sheet.Lines.Select(l => l.Text));
            Assert.Equal(sheet.Lines.Select(l => Math.Round(l.Time.TotalMilliseconds)), hit.Sheet.Lines.Select(l => Math.Round(l.Time.TotalMilliseconds)));
            foreach (var l in hit.Sheet.Lines) if (l.Words != null) Assert.True(WordTiming.Fits(l.Text, l.Words));
        }, v => Fuzz.Escape(Lrc.Write(v.Item1, TimeSpan.Zero)));
    }

    [Fact]
    public void SongPicker_Pick_ReturnsAValidIndexOfTheSameSong()
    {
        Fuzz.Run("SongPicker.Pick", 1011, 4000, r =>
        {
            string song = r.Pick(new[] { "Song", "歌名", "Dream Believers", "(Ver.)", "" });
            string Variant() => song + r.Pick(new[] { "", " (104期Ver.)", " (104th Ver.)", " (SAKURA Ver.)", " [Live]", "（Remix）", "!" });
            var items = Enumerable.Range(0, r.Next(0, 8)).Select(_ => new SongCandidate(Variant(), r.Pick(new[] { "", "A", "B", "a b", "結束バンド" }), r.Pick(new[] { 0.0, 100, 101, 101.9, 103.5, 200 }), r.Pick(new[] { 0, 2, 3, -1 }))).ToList();
            return (items, Variant(), r.Pick(new[] { "", "A", "kessoku" }), r.Pick(new[] { 0.0, 100, 101 }));
        }, v =>
        {
            var (items, title, artist, duration) = v;
            var (index, exact) = SongPicker.Pick(items, title, artist, duration);
            Assert.InRange(index, -1, items.Count - 1);
            if (index < 0) { Assert.False(exact); return; }
            Assert.True(items[index].Quality > 0);
            Assert.Equal(Titles.Norm(Titles.Bare(title)), Titles.Norm(Titles.Bare(items[index].Title)));
            if (duration > 0 && items[index].Duration > 0) Assert.True(Math.Abs(items[index].Duration - duration) <= 2);
            Assert.Equal(exact, Titles.VersionTag(items[index].Title) == Titles.VersionTag(title));
        }, v => $"title={Fuzz.Escape(v.Item2)} artist={v.Item3} d={v.Item4} items={string.Join("|", v.items.Select(i => $"{i.Title}/{i.Artist}/{i.Duration}/{i.Quality}"))}");
    }

    // ---------------------------------------------------------------- AI 翻譯

    private static readonly string[] ReplyParts =
    [
        "1. ", "2. ", "3) ", "4、", "5：", "(6) ", "[7] ", "#8 ", "【9】", "10. ", "0. ", "1999年", "99999. ", "１. ", "-1. ", "1.1 ",
        "翻譯", "你好", "「引用」", "\"quoted\"", "“引”", "以下是翻譯：", "Here is the translation:", "```", "```text", "<think>", "</think>", "思考", "\n", "\n", "\n", "\r\n", "\n\n",
    ];

    [Fact]
    public void AiTranslation_Parse_And_Align_AreNullOrExactlyAligned()
    {
        Fuzz.Run("AiTranslation.Parse／Align", 1012, 5000, r =>
        {
            var lines = Enumerable.Range(0, r.Next(0, 9)).Select(i => new LyricLine(TimeSpan.FromSeconds(i), r.Chance(0.15) ? "" : r.Pick(new[] { "la", "歌 詞", "你好", "1999年", "Hello" }))).ToList();
            var reply = r.Chance(0.5)
                ? string.Concat(Enumerable.Range(1, Math.Max(0, lines.Count(l => l.Text.Length > 0) + r.Next(-1, 2))).Select(i => $"{i}{r.Pick(new[] { ". ", ") ", "、", "：" })}{r.Pick(new[] { "翻", "你好", "「引」" })}{i}\n"))
                : r.Build(ReplyParts.Concat(Gen.Noise).ToArray(), 0, 25, 0.2);
            return (lines, r.Mangle(reply));
        }, v =>
        {
            var (lines, reply) = v;
            var sheet = new LyricSheet(lines, true);
            int count = lines.Count(l => l.Text.Length > 0);
            var parsed = AiTranslation.Parse(reply, count);
            if (parsed != null) Assert.Equal(count, parsed.Count);
            var aligned = AiTranslation.Align(sheet, reply);
            if (aligned != null)
            {
                Assert.Equal(lines.Count, aligned.Count);
                for (int i = 0; i < lines.Count; i++) if (lines[i].Text.Length == 0) Assert.Equal("", aligned[i]);
                Assert.Contains(aligned, s => s.Length > 0);
            }
            AiTranslation.Request(sheet, "t", "a", "English");
        }, v => $"lines={string.Join("|", v.lines.Select(l => l.Text))} reply={Fuzz.Escape(v.Item2)}");
    }
}

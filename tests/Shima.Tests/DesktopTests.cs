// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

public class MascotImageTests
{
    private static readonly byte[] Red = [0, 0, 255, 255], Green = [0, 255, 0, 255], Blue = [255, 0, 0, 255], None = [0, 0, 0, 0];

    private static byte[] Px(params byte[][] pixels) => pixels.SelectMany(p => p).ToArray();
    private static byte[] At(byte[] canvas, int width, int x, int y) => canvas.Skip((y * width + x) * 4).Take(4).ToArray();

    [Fact]
    public void GIF_延遲是百分之一秒_0和1當成十分之一秒()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), MascotImage.GifDelay(null));
        Assert.Equal(TimeSpan.FromMilliseconds(100), MascotImage.GifDelay(0));
        Assert.Equal(TimeSpan.FromMilliseconds(100), MascotImage.GifDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(50), MascotImage.GifDelay(5));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), MascotImage.GifDelay(200));
    }

    [Fact]
    public void 寬是高的整數倍才算精靈圖()
    {
        Assert.Equal(4, MascotImage.SpriteFrames(64, 16));
        Assert.Equal(1, MascotImage.SpriteFrames(16, 16));
        Assert.Equal(1, MascotImage.SpriteFrames(50, 16));
        Assert.Equal(1, MascotImage.SpriteFrames(16, 64));
        Assert.Equal(1, MascotImage.SpriteFrames(0, 0));
    }

    [Fact]
    public void 精靈圖切成水平排列的正方形()
    {
        Assert.Equal(new Int32Rect(32, 0, 16, 16), MascotImage.SpriteCell(2, 64, 16));
        Assert.Equal(new Int32Rect(48, 0, 16, 16), MascotImage.SpriteCell(9, 64, 16));
        Assert.Equal(new Int32Rect(0, 0, 50, 16), MascotImage.SpriteCell(1, 50, 16));
    }

    [Fact]
    public void 照每格的延遲挑格而且會循環()
    {
        TimeSpan[] delays = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100)];
        int At(int ms) => MascotImage.FrameAt(delays, TimeSpan.FromMilliseconds(ms));
        Assert.Equal([0, 0, 1, 1, 2, 0, 1], new[] { 0, 99, 100, 299, 300, 400, 550 }.Select(At));
        Assert.Equal(0, MascotImage.FrameAt([TimeSpan.FromSeconds(1)], TimeSpan.FromSeconds(5)));
        Assert.Equal(0, MascotImage.FrameAt(delays, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void 差分格疊在前一格上_透明的地方不蓋()
    {
        var frames = MascotImage.Compose(2, 2,
        [
            new GifPiece(Px(Red, Red, Red, Red), 0, 0, 2, 2, 1),
            new GifPiece(Px(None, Green), 0, 1, 2, 1, 1),
        ]).ToList();
        Assert.Equal(2, frames.Count);
        Assert.Equal(Red, At(frames[1], 2, 0, 1));
        Assert.Equal(Green, At(frames[1], 2, 1, 1));
        Assert.Equal(Red, At(frames[1], 2, 0, 0));
    }

    [Fact]
    public void 處理方式2清成透明_3回到畫之前()
    {
        var frames = MascotImage.Compose(2, 1,
        [
            new GifPiece(Px(Red, Red), 0, 0, 2, 1, 0),
            new GifPiece(Px(Green), 1, 0, 1, 1, 3),
            new GifPiece(Px(Blue), 0, 0, 1, 1, 2),
            new GifPiece(Px(None), 0, 0, 1, 1, 0),
        ]).ToList();
        Assert.Equal(Px(Red, Green), frames[1]);
        Assert.Equal(Px(Blue, Red), frames[2]);   // 綠色那格畫完就還原
        Assert.Equal(Px(None, Red), frames[3]);   // 藍色那格畫完清成透明
    }

    [Fact]
    public void 超出畫布的部分不會寫壞()
    {
        var frames = MascotImage.Compose(1, 1, [new GifPiece(Px(Red, Green, Blue, Red), -1, -1, 2, 2, 2)]).ToList();
        Assert.Equal(Red, frames[0]);
    }

    [Fact]
    public void 放進格子保持比例_小圖取整數倍()
    {
        Assert.Equal(new Rect(1, 0, 24, 24), MascotImage.Fit(26, 24, 8, 8));
        Assert.Equal(new Rect(1, 0, 24, 24), MascotImage.Fit(26, 24, 16, 16));   // 1.5 倍不取整，填滿格子
        var wide = MascotImage.Fit(26, 24, 100, 50);
        Assert.Equal(26, wide.Width, 3);
        Assert.Equal(13, wide.Height, 3);
        Assert.True(MascotImage.Fit(26, 24, 0, 5).IsEmpty);
    }

    [Fact]
    public void 真的GIF_讀出每格延遲並合成差分格() => Sta(() =>
    {
        // 2×2：第一格整張紅、停 0.05 秒；第二格只有右下角一點綠、延遲 0
        var gif = TinyGif.Make(2, 2,
        [
            (0, 0, 2, 2, 5, 1, new byte[] { 1, 1, 1, 1 }),
            (1, 1, 1, 1, 0, 1, new byte[] { 2 }),
        ]);
        var image = MascotImage.Decode(gif);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)], image.Delays);
        var second = image.Frames[1];
        Assert.Equal((2, 2), (second.PixelWidth, second.PixelHeight));
        Assert.True(second.IsFrozen);
        var px = new byte[16];
        second.CopyPixels(px, 8, 0);
        Assert.Equal(Red, At(px, 2, 0, 0));
        Assert.Equal(Green, At(px, 2, 1, 1));
    });

    [Fact]
    public void 真的GIF_透明色不蓋掉前一格() => Sta(() =>
    {
        // 第二格整張，但只有右上角不是透明色（索引 3 是透明）
        var gif = TinyGif.Make(2, 1,
        [
            (0, 0, 2, 1, 10, 1, new byte[] { 1, 1 }),
            (0, 0, 2, 1, 10, 1, new byte[] { 3, 2 }),
        ], transparent: 3);
        var px = new byte[8];
        MascotImage.Decode(gif).Frames[1].CopyPixels(px, 8, 0);
        Assert.Equal(Px(Red, Green), px);
    });

    [Fact]
    public void PNG_精靈圖每秒8格_單張是靜態_大圖會縮小() => Sta(() =>
    {
        var strip = MascotImage.Decode(Png(48, 16));
        Assert.Equal(3, strip.Frames.Count);
        Assert.All(strip.Delays, d => Assert.Equal(TimeSpan.FromMilliseconds(125), d));
        Assert.Equal((16, 16), (strip.Frames[2].PixelWidth, strip.Frames[2].PixelHeight));

        var still = MascotImage.Decode(Png(20, 16));
        Assert.Single(still.Frames);

        var big = MascotImage.Decode(Png(400, 200)).Frames[0];
        Assert.Equal(MascotImage.MaxSide, Math.Max(big.PixelWidth, big.PixelHeight));
    });

    [Fact]
    public void 壞掉的檔案丟例外() => Sta(() =>
    {
        Assert.ThrowsAny<Exception>(() => MascotImage.Decode([1, 2, 3, 4, 5]));
        Assert.ThrowsAny<Exception>(() => MascotImage.Load(Path.Combine(AppContext.BaseDirectory, "沒有這個檔.gif")));
    });

    [Fact]
    public void 像素圖放大才用最近鄰()
    {
        Assert.True(MascotImage.Pixelated(16, 24));
        Assert.True(MascotImage.Pixelated(48, 24));
        Assert.False(MascotImage.Pixelated(96, 24));
    }

    private static byte[] Png(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(i / 4 % 251); px[i + 3] = 255; }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>WPF 的影像解碼放在 STA 執行緒上跑，跟程式裡一樣。</summary>
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

/// <summary>測試用的極小 GIF 產生器：四色調色盤（黑、紅、綠、藍），LZW 每兩個碼清一次表，碼長永遠是 3 位元。</summary>
internal static class TinyGif
{
    public static byte[] Make(int width, int height, (int Left, int Top, int W, int H, int Delay, int Disposal, byte[] Indexes)[] frames, int transparent = -1)
    {
        var o = new List<byte>();
        o.AddRange("GIF89a"u8.ToArray());
        o.AddRange(U16(width)); o.AddRange(U16(height));
        o.Add(0xF1);   // 有全域調色盤，4 色
        o.Add(0); o.Add(0);
        o.AddRange([0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255]);
        foreach (var f in frames)
        {
            o.AddRange([0x21, 0xF9, 4, (byte)((f.Disposal << 2) | (transparent >= 0 ? 1 : 0))]);
            o.AddRange(U16(f.Delay));
            o.Add((byte)Math.Max(0, transparent));
            o.Add(0);
            o.Add(0x2C);
            o.AddRange(U16(f.Left)); o.AddRange(U16(f.Top)); o.AddRange(U16(f.W)); o.AddRange(U16(f.H));
            o.Add(0);
            o.Add(2);   // LZW 最小碼長
            var data = Lzw(f.Indexes);
            for (int i = 0; i < data.Count; i += 255)
            {
                int n = Math.Min(255, data.Count - i);
                o.Add((byte)n);
                o.AddRange(data.Skip(i).Take(n));
            }
            o.Add(0);
        }
        o.Add(0x3B);
        return o.ToArray();
    }

    private static byte[] U16(int v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static List<byte> Lzw(byte[] indexes)
    {
        const int clear = 4, end = 5;
        var codes = new List<int>();
        for (int i = 0; i < indexes.Length; i++)
        {
            if (i % 2 == 0) codes.Add(clear);
            codes.Add(indexes[i]);
        }
        codes.Add(end);
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in codes)
        {
            buffer |= c << bits;
            bits += 3;
            while (bits >= 8) { bytes.Add((byte)(buffer & 0xFF)); buffer >>= 8; bits -= 8; }
        }
        if (bits > 0) bytes.Add((byte)buffer);
        return bytes;
    }
}

public class DesktopLyricsLayoutTests
{
    private static readonly LyricSheet Sheet = new(Lrc.Parse("[00:05.00]第一句\n[00:10.00]第二句\n[00:15.00]\n[00:20.00]最後一句"), true);

    [Fact]
    public void 字級夾在範圍內_滾輪一格兩級()
    {
        Assert.Equal(DesktopLyricsLayout.MinSize, DesktopLyricsLayout.ClampSize(5));
        Assert.Equal(DesktopLyricsLayout.MaxSize, DesktopLyricsLayout.ClampSize(500));
        Assert.Equal(DesktopLyricsLayout.DefaultSize, DesktopLyricsLayout.ClampSize(double.NaN));
        Assert.Equal(32, DesktopLyricsLayout.Wheel(30, 120));
        Assert.Equal(28, DesktopLyricsLayout.Wheel(30, -240));
        Assert.Equal(DesktopLyricsLayout.MaxSize, DesktopLyricsLayout.Wheel(DesktopLyricsLayout.MaxSize, 120));
        Assert.Equal(DesktopLyricsLayout.MinSize, DesktopLyricsLayout.Wheel(DesktopLyricsLayout.MinSize, -120));
    }

    [Fact]
    public void 視窗不比螢幕寬_字越大越高()
    {
        Assert.Equal(1000, DesktopLyricsLayout.WindowSize(72, 1000).Width);
        Assert.True(DesktopLyricsLayout.WindowSize(40, 3840).Height > DesktopLyricsLayout.WindowSize(20, 3840).Height);
    }

    [Fact]
    public void 預設在主螢幕下方中央()
    {
        var work = new Rect(0, 0, 1920, 1032);
        var at = DesktopLyricsLayout.DefaultSpot(work, new Size(780, 90));
        Assert.Equal(570, at.X);
        Assert.True(at.Y + 90 < work.Bottom && at.Y > work.Height / 2);
    }

    [Fact]
    public void 位置夾在螢幕內()
    {
        var screen = new Rect(-1920, 0, 3840, 1080);
        var window = new Size(800, 100);
        Assert.Equal(new Point(-1920, 0), DesktopLyricsLayout.Clamp(new Point(-5000, -40), window, screen));
        Assert.Equal(new Point(1120, 980), DesktopLyricsLayout.Clamp(new Point(1900, 1070), window, screen));
        Assert.Equal(new Point(100, 300), DesktopLyricsLayout.Clamp(new Point(100, 300), window, screen));
        Assert.True(DesktopLyricsLayout.OnScreen(new Point(-1500, 400), window, screen));
        Assert.False(DesktopLyricsLayout.OnScreen(new Point(2500, 400), window, screen));   // 那個螢幕拔掉了
    }

    [Fact]
    public void 正在唱的那句_第二行是下一句()
    {
        var line = DesktopLyricsLayout.Pick(Sheet, null, TimeSpan.FromSeconds(6), "歌名", "藝人");
        Assert.Equal((0, "第一句", "第二句", true), (line.Index, line.Main, line.Sub, line.Sweep));
        Assert.Equal(TimeSpan.FromSeconds(5), line.Start);
        Assert.True(line.Length > TimeSpan.Zero && line.Length <= TimeSpan.FromSeconds(5));
        Assert.Equal("", DesktopLyricsLayout.Pick(Sheet, null, TimeSpan.FromSeconds(30), "歌名", "藝人").Sub);
    }

    [Fact]
    public void 有翻譯就用翻譯當第二行_那句沒翻譯就是下一句()
    {
        string[] second = ["first", "", "", "last"];
        Assert.Equal("first", DesktopLyricsLayout.Pick(Sheet, second, TimeSpan.FromSeconds(6), "歌名", "").Sub);
        Assert.Equal("", DesktopLyricsLayout.Pick(Sheet, second, TimeSpan.FromSeconds(11), "歌名", "").Sub);   // 下一句是空白行
        Assert.Equal("第二句", DesktopLyricsLayout.Pick(Sheet, ["", "x"], TimeSpan.FromSeconds(6), "歌名", "").Sub);
        Assert.Equal("", DesktopLyricsLayout.Pick(Sheet, ["a"], TimeSpan.FromSeconds(21), "歌名", "").Sub);   // 翻譯比歌詞短也不會出錯
    }

    [Fact]
    public void 前奏_間奏_沒有歌詞時顯示歌名()
    {
        var intro = DesktopLyricsLayout.Pick(Sheet, null, TimeSpan.FromSeconds(1), "歌名", "藝人");
        Assert.Equal((-1, "歌名", "藝人", false), (intro.Index, intro.Main, intro.Sub, intro.Sweep));
        Assert.Equal("歌名", DesktopLyricsLayout.Pick(Sheet, null, TimeSpan.FromSeconds(16), "歌名", "藝人").Main);
        Assert.Equal("歌名", DesktopLyricsLayout.Pick(null, null, TimeSpan.FromSeconds(6), "歌名", "藝人").Main);
        var plain = new LyricSheet(Lrc.Plain("純文字歌詞"), false);
        Assert.Equal("歌名", DesktopLyricsLayout.Pick(plain, null, TimeSpan.FromSeconds(6), "歌名", "藝人").Main);
        Assert.Equal(("藝人", ""), DesktopLyricsLayout.Pick(null, null, TimeSpan.Zero, "", "藝人") is var a ? (a.Main, a.Sub) : default);
    }

    [Fact]
    public void 迷你提示放在範圍上緣正中間()
    {
        Assert.Equal(new Point(760, 0), MiniToast.Place(new Rect(0, 0, 1920, 1080), 400));
        Assert.Equal(new Point(-1160, -200), MiniToast.Place(new Rect(-1920, -200, 1920, 1080), 400));
    }
}

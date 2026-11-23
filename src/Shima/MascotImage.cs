// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shima;

/// <summary>GIF 裡的一格原始資料：只是畫面的一小塊，要疊到前面的結果上才是完整的一格。</summary>
/// <param name="Bgra">這一塊的像素（BGRA，每列 Width×4 個位元組）。</param>
/// <param name="Disposal">畫完之後怎麼處理：0、1 留著；2 清成透明；3 回到畫這格之前的樣子。</param>
public sealed record GifPiece(byte[] Bgra, int Left, int Top, int Width, int Height, int Disposal);

/// <summary>
/// 使用者自己的圖當膠囊裡的角色：GIF 動畫，或水平排列的正方形格子拼成的 PNG（每秒 8 格），單張圖就是靜態。
/// 讀檔時就把每一格合成好、縮到夠用的大小並 Freeze，之後畫的時候只是挑一張。
/// </summary>
public sealed class MascotImage
{
    /// <summary>精靈圖每格的時間（每秒 8 格）。</summary>
    public static readonly TimeSpan SpriteDelay = TimeSpan.FromMilliseconds(125);
    /// <summary>格子比這大就先縮小，膠囊裡的角色只有 26×24，留四倍給高 DPI 就夠了。</summary>
    public const int MaxSide = 96;
    public const int MaxFrames = 300;
    private const long MaxFileBytes = 32L * 1024 * 1024, MaxCanvasPixels = 4096L * 4096;

    public IReadOnlyList<BitmapSource> Frames { get; }
    public IReadOnlyList<TimeSpan> Delays { get; }

    private MascotImage(IReadOnlyList<BitmapSource> frames, IReadOnlyList<TimeSpan> delays)
    {
        Frames = frames;
        Delays = delays;
    }

    /// <summary>從開始播放過了 elapsed 時該顯示的那一格。</summary>
    public BitmapSource At(TimeSpan elapsed) => Frames[FrameAt(Delays, elapsed)];

    // ---------------------------------------------------------------- 純邏輯（可測）

    /// <summary>GIF 的延遲是百分之一秒；0 或 1 照瀏覽器的慣例當成 0.1 秒，不然會快到看不清楚。</summary>
    public static TimeSpan GifDelay(int? centiseconds) =>
        centiseconds is not { } cs || cs <= 1 ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(cs * 10);

    /// <summary>PNG 有幾格：寬是高的整數倍（而且比高寬）就是水平排的正方形格子，不然就是一張。</summary>
    public static int SpriteFrames(int width, int height) => height > 0 && width > height && width % height == 0 ? width / height : 1;

    /// <summary>精靈圖第 index 格在原圖裡的位置（像素）。</summary>
    public static Int32Rect SpriteCell(int index, int width, int height)
    {
        int count = SpriteFrames(width, height);
        if (count == 1) return new Int32Rect(0, 0, width, height);
        int i = Math.Clamp(index, 0, count - 1);
        return new Int32Rect(i * height, 0, height, height);
    }

    /// <summary>每格各停 delays[i]，一直循環；時間 elapsed 該是第幾格。</summary>
    public static int FrameAt(IReadOnlyList<TimeSpan> delays, TimeSpan elapsed)
    {
        if (delays.Count <= 1) return 0;
        long total = 0;
        foreach (var d in delays) total += Math.Max(1, d.Ticks);
        long t = Math.Max(0, elapsed.Ticks) % total;
        for (int i = 0; i < delays.Count; i++)
        {
            t -= Math.Max(1, delays[i].Ticks);
            if (t < 0) return i;
        }
        return delays.Count - 1;
    }

    /// <summary>
    /// 把 GIF 一塊一塊疊成完整的格子：每格先疊到畫布上（透明的像素不蓋），輸出整張畫布，
    /// 再照這格的處理方式清掉或還原，給下一格當底。
    /// </summary>
    public static IEnumerable<byte[]> Compose(int width, int height, IEnumerable<GifPiece> pieces)
    {
        var canvas = new byte[width * height * 4];
        foreach (var p in pieces)
        {
            byte[]? before = p.Disposal == 3 ? (byte[])canvas.Clone() : null;
            Blit(canvas, width, height, p);
            yield return (byte[])canvas.Clone();
            if (p.Disposal == 2) Clear(canvas, width, height, p.Left, p.Top, p.Width, p.Height);
            else if (before != null) canvas = before;
        }
    }

    private static void Blit(byte[] canvas, int width, int height, GifPiece p)
    {
        for (int y = 0; y < p.Height; y++)
        {
            int cy = p.Top + y;
            if (cy < 0 || cy >= height) continue;
            for (int x = 0; x < p.Width; x++)
            {
                int cx = p.Left + x;
                if (cx < 0 || cx >= width) continue;
                int from = (y * p.Width + x) * 4, to = (cy * width + cx) * 4;
                if (from + 3 >= p.Bgra.Length || p.Bgra[from + 3] == 0) continue;
                Buffer.BlockCopy(p.Bgra, from, canvas, to, 4);
            }
        }
    }

    private static void Clear(byte[] canvas, int width, int height, int left, int top, int w, int h)
    {
        int x0 = Math.Max(0, left), x1 = Math.Min(width, left + w);
        if (x1 <= x0) return;
        for (int y = Math.Max(0, top); y < Math.Min(height, top + h); y++)
            Array.Clear(canvas, (y * width + x0) * 4, (x1 - x0) * 4);
    }

    /// <summary>
    /// 圖放進角色的格子裡：保持比例、置中、對齊整數。小圖放大兩倍以上時取整數倍，像素才不會一格寬一格窄。
    /// </summary>
    public static Rect Fit(double boxWidth, double boxHeight, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || boxWidth <= 0 || boxHeight <= 0) return Rect.Empty;
        double scale = Math.Min(boxWidth / pixelWidth, boxHeight / pixelHeight);
        if (scale >= 2) scale = Math.Floor(scale);
        double w = pixelWidth * scale, h = pixelHeight * scale;
        return new Rect(Math.Round((boxWidth - w) / 2), Math.Round((boxHeight - h) / 2), w, h);
    }

    /// <summary>圖夠小（像素圖）時放大要用最近鄰，不然會糊成一團。</summary>
    public static bool Pixelated(int pixelHeight, double boxHeight) => pixelHeight <= boxHeight * 2;

    // ---------------------------------------------------------------- 讀檔

    /// <summary>讀一張圖；讀不到、格式不對、太大都會丟例外，呼叫的人自己退回預設角色。</summary>
    public static MascotImage Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("找不到圖片", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException($"圖片太大（{info.Length / 1024 / 1024} MB）");
        // 整個讀進記憶體再解碼：檔案不會被鎖住，使用者可以繼續改它
        return Decode(File.ReadAllBytes(path));
    }

    /// <summary>圖檔的內容 → 每一格。</summary>
    public static MascotImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("圖片裡沒有畫面");
        return decoder is GifBitmapDecoder && decoder.Frames.Count > 1 ? Gif(decoder) : Still(decoder.Frames[0]);
    }

    private static MascotImage Still(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        Guard(w, h);
        var all = Pixels(source);
        int count = Math.Min(SpriteFrames(w, h), MaxFrames);
        var frames = new List<BitmapSource>(count);
        for (int i = 0; i < count; i++)
        {
            var cell = SpriteCell(i, w, h);
            var px = new byte[cell.Width * cell.Height * 4];
            for (int y = 0; y < cell.Height; y++)
                Buffer.BlockCopy(all, ((cell.Y + y) * w + cell.X) * 4, px, y * cell.Width * 4, cell.Width * 4);
            frames.Add(Make(px, cell.Width, cell.Height));
        }
        return new MascotImage(frames, Enumerable.Repeat(count > 1 ? SpriteDelay : TimeSpan.FromSeconds(1), count).ToArray());
    }

    private static MascotImage Gif(BitmapDecoder decoder)
    {
        var first = decoder.Frames[0];
        int w = Number(decoder.Metadata as BitmapMetadata, "/logscrdesc/Width") is > 0 and var lw ? lw : first.PixelWidth;
        int h = Number(decoder.Metadata as BitmapMetadata, "/logscrdesc/Height") is > 0 and var lh ? lh : first.PixelHeight;
        Guard(w, h);
        var used = decoder.Frames.Take(MaxFrames).ToList();
        var delays = used.Select(f => GifDelay(Number(f.Metadata as BitmapMetadata, "/grctlext/Delay"))).ToArray();
        var pieces = used.Select(f =>
        {
            var meta = f.Metadata as BitmapMetadata;
            return new GifPiece(Pixels(f), Number(meta, "/imgdesc/Left") ?? 0, Number(meta, "/imgdesc/Top") ?? 0, f.PixelWidth, f.PixelHeight, Number(meta, "/grctlext/Disposal") ?? 0);
        });
        var frames = Compose(w, h, pieces).Select(px => Make(px, w, h)).ToArray();
        return new MascotImage(frames, delays);
    }

    private static void Guard(int w, int h)
    {
        if (w <= 0 || h <= 0 || (long)w * h > MaxCanvasPixels) throw new InvalidDataException($"圖片尺寸不支援（{w}×{h}）");
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return px;
    }

    /// <summary>像素 → 凍結好的圖；太大的先縮小，免得一個 GIF 吃掉幾百 MB。</summary>
    private static BitmapSource Make(byte[] px, int w, int h)
    {
        BitmapSource image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        if (Math.Max(w, h) > MaxSide)
        {
            double k = (double)MaxSide / Math.Max(w, h);
            var small = new TransformedBitmap(image, new ScaleTransform(k, k));
            int sw = small.PixelWidth, sh = small.PixelHeight;
            var copy = new byte[sw * sh * 4];
            small.CopyPixels(copy, sw * 4, 0);
            image = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, copy, sw * 4);
        }
        image.Freeze();
        return image;
    }

    private static int? Number(BitmapMetadata? meta, string query)
    {
        try { return meta?.GetQuery(query) is { } v ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : null; }
        catch { return null; }
    }
}

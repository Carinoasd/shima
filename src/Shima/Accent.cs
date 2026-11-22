// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shima;

public static class Accent
{
    public static readonly Color Default = Colors.White;

    /// <summary>封面的代表色：把圖縮小後取最鮮豔的那群顏色，再調亮到在黑底上看得清楚。</summary>
    public static Color From(ImageSource? cover)
    {
        if (cover is not BitmapSource source) return Default;
        try
        {
            var small = new FormatConvertedBitmap(new TransformedBitmap(source, new ScaleTransform(24.0 / source.PixelWidth, 24.0 / source.PixelHeight)), PixelFormats.Bgra32, null, 0);
            int w = small.PixelWidth, h = small.PixelHeight;
            var pixels = new byte[w * h * 4];
            small.CopyPixels(pixels, w * 4, 0);
            return Pick(pixels);
        }
        catch { return Default; }
    }

    /// <summary>封面縮成 12×12 的小圖：放大回去就是一片柔和的色塊，當面板背景用，不必即時模糊。</summary>
    public static ImageSource? Blurred(ImageSource? cover)
    {
        if (cover is not BitmapSource source) return null;
        try
        {
            // 只取中間七成：很多封面有白邊，放大後會在面板邊緣變成一條亮帶
            int w = source.PixelWidth, h = source.PixelHeight;
            var centre = new CroppedBitmap(source, new System.Windows.Int32Rect(w * 15 / 100, h * 15 / 100, Math.Max(1, w * 70 / 100), Math.Max(1, h * 70 / 100)));
            var tiny = new TransformedBitmap(centre, new ScaleTransform(12.0 / centre.PixelWidth, 12.0 / centre.PixelHeight));
            tiny.Freeze();
            return tiny;
        }
        catch { return null; }
    }

    /// <summary>BGRA 像素 → 代表色。越鮮豔的像素權重越高；整張都是灰的就回白色。</summary>
    public static Color Pick(byte[] bgra)
    {
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            double pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
            double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
            double saturation = max == 0 ? 0 : (max - min) / max;
            double weight = saturation * saturation * (max / 255);
            r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
        }
        if (total < bgra.Length / 4 * 0.02) return Default;
        r /= total; g /= total; b /= total;
        // 調亮：最亮的色頻拉到 255，再往白色靠一點
        double peak = Math.Max(r, Math.Max(g, b));
        if (peak < 1) return Default;
        double k = 255 / peak;
        static byte Lift(double v) => (byte)Math.Clamp(v + (255 - v) * 0.25, 0, 255);
        return Color.FromRgb(Lift(r * k), Lift(g * k), Lift(b * k));
    }
}

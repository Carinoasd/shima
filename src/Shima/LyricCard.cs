// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shima.Playback;

namespace Shima;

/// <summary>歌詞卡片：正在唱的那句配上封面，畫成一張可以貼給別人的圖。</summary>
public static class LyricCard
{
    public const int Width = 1200, Height = 630;

    /// <param name="light">短線的顏色；不給就取自封面（島上設定的強調色由呼叫的人傳進來）。</param>
    public static BitmapSource Render(Track track, string line, string second, Color? light = null)
    {
        bool japanese = Fonts.HasKana(line + track.Title);
        var family = Fonts.For(japanese);
        var culture = Fonts.Culture(japanese);
        var accent = light ?? Accent.From(track.Cover);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var all = new Rect(0, 0, Width, Height);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 14, 16)), null, all);
            if (Accent.Blurred(track.Cover) is { } blur)
            {
                // 12×12 的小圖放大成柔和的色塊，再壓暗
                var group = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.Fant);
                using (var inner = group.Open()) inner.DrawImage(blur, new Rect(-60, -345, Width + 120, Width + 120));
                dc.PushClip(new RectangleGeometry(all));
                dc.DrawDrawing(group);
                dc.Pop();
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, all);
            }

            // 封面
            double size = 340, left = 80, top = (Height - size) / 2;
            var frame = new Rect(left, top, size, size);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new Rect(left + 6, top + 14, size, size), 30, 30);
            dc.PushClip(new RectangleGeometry(frame, 26, 26));
            if (track.Cover != null) dc.DrawImage(track.Cover, frame); else dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, frame);
            dc.Pop();

            // 文字：歌詞、第二行、一條主色短線、歌名與藝人，整組在封面右邊垂直置中
            double x = left + size + 64, room = Width - x - 80;
            FormattedText Text(string text, double points, FontWeight weight, byte alpha, int maxLines)
            {
                var formatted = new FormattedText(text, culture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
                    points, new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255)), 1.0)
                { MaxTextWidth = room, MaxLineCount = maxLines, Trimming = TextTrimming.CharacterEllipsis };
                return formatted;
            }
            string main = line.Length > 0 ? line : track.Title;
            var big = Text(main, main.Length > 26 ? 46 : 58, FontWeights.SemiBold, 255, 4);
            big.LineHeight = big.Height / Math.Max(1, Math.Ceiling(big.Height / (main.Length > 26 ? 46 * 1.32 : 58 * 1.32))) * 1.04;
            var sub = second.Length > 0 ? Text(second, 28, FontWeights.Normal, 190, 3) : null;
            string credit = line.Length == 0 ? track.Artist : track.Artist.Length > 0 ? $"{track.Title} — {track.Artist}" : track.Title;
            var name = Text(credit, 24, FontWeights.Normal, 160, 2);

            double total = big.Height + (sub != null ? 18 + sub.Height : 0) + 34 + 4 + 22 + name.Height;
            double y = (Height - total) / 2;
            dc.DrawText(big, new Point(x, y)); y += big.Height;
            if (sub != null) { y += 18; dc.DrawText(sub, new Point(x, y)); y += sub.Height; }
            y += 34;
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, new Rect(x, y, 56, 4), 2, 2);
            y += 4 + 22;
            dc.DrawText(name, new Point(x, y));
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>存成 PNG，回傳檔案路徑。檔名用歌名，去掉不能當檔名的字。</summary>
    /// <summary>卡片存在哪裡：「圖片\Shima」；示範模式放在它自己的暫存資料夾。</summary>
    public static string Folder => Demo.On ? Path.Combine(AppSettings.Folder, "pictures")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shima");

    public static string Save(BitmapSource card, string folder, string title)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{FileName(title)} {DateTime.Now:yyyyMMdd-HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(card));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    public static string FileName(string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(title.Select(c => bad.Contains(c) ? ' ' : c).ToArray()).Trim().TrimEnd('.');
        if (clean.Length > 60) clean = clean[..60].Trim();
        return clean.Length > 0 ? clean : "Shima";
    }
}

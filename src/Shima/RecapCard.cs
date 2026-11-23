// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shima.Listening;

namespace Shima;

public sealed record RecapRow(string Name, string Detail, string Time);

/// <summary>卡片上要顯示的全部文字；Streak 沒有連續天數時是空字串。</summary>
public sealed record RecapContent(string Period, string Total, string Plays, string Streak, IReadOnlyList<RecapRow> Tracks, IReadOnlyList<RecapRow> Artists);

/// <summary>聆聽回顧卡片：目前期間的統計畫成一張 1200×630 的圖，可以直接貼給別人。</summary>
public static class RecapCard
{
    public const int Width = 1200, Height = 630;
    public const int MaxTracks = 5, MaxArtists = 3;

    /// <summary>
    /// 卡片上的文字。這個期間一首都沒聽就回 null。translate 預設是目前語言的翻譯，
    /// 測試時換成原樣回傳，結果就不受語言設定影響。
    /// </summary>
    public static RecapContent? Content(StatsSummary summary, StatsPeriod period, DateTime now, CultureInfo culture, Func<string, string>? translate = null)
    {
        if (summary.Plays <= 0) return null;
        var t = translate ?? Loc.T;
        string F(string zh, params object[] args) => string.Format(CultureInfo.InvariantCulture, t(zh), args);
        var format = culture.DateTimeFormat;

        string title = period switch
        {
            StatsPeriod.Week when summary.From.Year != summary.From.AddDays(6).Year => Stats.Label(period, summary.From, culture),   // 跨年的那一週兩端都寫年份
            StatsPeriod.Week => $"{summary.From.Year} · {summary.From.ToString(format.MonthDayPattern, culture)} – {summary.From.AddDays(6).ToString(format.MonthDayPattern, culture)}",
            StatsPeriod.Month => summary.From.ToString(format.YearMonthPattern, culture),
            StatsPeriod.Year => summary.From.Year.ToString(CultureInfo.InvariantCulture),
            _ => t("全部"),
        };

        long minutes = (long)Math.Max(1, summary.Total.TotalMinutes);
        string total = minutes >= 60 ? F("{0} 小時 {1} 分", minutes / 60, minutes % 60) : F("{0} 分", minutes);

        string Short(TimeSpan time) => time.TotalMinutes >= 60
            ? F("{0} 小時", Math.Round(time.TotalHours, 1).ToString("0.#", culture))
            : F("{0} 分", (long)Math.Max(1, Math.Round(time.TotalMinutes)));

        return new RecapContent(
            title, total, F("{0} 次", summary.Plays),
            summary.Streak > 0 ? F("連續 {0} 天", summary.Streak) : "",
            summary.TopTracks.Take(MaxTracks).Select(r => new RecapRow(r.Name, r.Detail, Short(r.Time))).ToList(),
            summary.TopArtists.Take(MaxArtists).Select(r => new RecapRow(r.Name, "", Short(r.Time))).ToList());
    }

    /// <summary>畫卡片；沒有資料回 null。cover 是目前播放的封面，當模糊的背景並取強調色。</summary>
    /// <param name="light">標題、長條與名次的顏色；不給就取自封面（島上設定的強調色由呼叫的人傳進來）。</param>
    public static BitmapSource? Render(StatsSummary summary, StatsPeriod period, DateTime now, ImageSource? cover, Color? light = null)
    {
        var culture = Loc.Culture;
        if (Content(summary, period, now, culture) is not { } content) return null;
        var accent = light ?? Accent.From(cover);
        var bars = Stats.Bars(summary.Daily, period);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var all = new Rect(0, 0, Width, Height);
            if (Accent.Blurred(cover) is { } blur)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 14, 16)), null, all);
                var group = new DrawingGroup();
                RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.Fant);
                using (var inner = group.Open()) inner.DrawImage(blur, new Rect(-60, -345, Width + 120, Width + 120));
                dc.PushClip(new RectangleGeometry(all));
                dc.DrawDrawing(group);
                dc.Pop();
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(165, 0, 0, 0)), null, all);
            }
            else
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(30, 32, 44), Color.FromRgb(10, 10, 13), 55), null, all);
            }

            Brush White(byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));

            FormattedText Text(string text, double size, FontWeight weight, Brush brush, double room = 0)
            {
                bool kana = Fonts.HasKana(text);
                var typeface = new Typeface(kana ? Fonts.Japanese : Loc.Font, FontStyles.Normal, weight, FontStretches.Normal);
                var f = new FormattedText(text, kana ? Fonts.Culture(true) : culture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);
                if (room > 0) { f.MaxTextWidth = room; f.MaxLineCount = 1; f.Trimming = TextTrimming.CharacterEllipsis; }
                return f;
            }

            // 左半：期間、總時間（大字）、次數與連續天數、長條圖、Shima
            const double Pad = 64, LeftW = 560, PanelX = 700, PanelW = Width - PanelX - Pad;
            var title = Text(content.Period, 30, FontWeights.SemiBold, new SolidColorBrush(accent), LeftW);
            dc.DrawText(title, new Point(Pad, Pad - 4));

            double size = 104;
            FormattedText total;
            while ((total = Text(content.Total, size, FontWeights.SemiBold, Brushes.White)).Width > LeftW && size > 40) size -= 4;
            double totalY = Pad + title.Height + 8;
            dc.DrawText(total, new Point(Pad - 4, totalY));

            var plays = Text(content.Plays, 30, FontWeights.Normal, White(200));
            double lineY = totalY + total.Height + 4;
            dc.DrawText(plays, new Point(Pad, lineY));
            if (content.Streak.Length > 0)
            {
                var streak = Text(content.Streak, 30, FontWeights.Normal, White(200), Math.Max(60, LeftW - plays.Width - 40));
                dc.DrawText(streak, new Point(Pad + plays.Width + 36, lineY + plays.Baseline - streak.Baseline));
            }

            var brand = Text("Shima", 24, FontWeights.SemiBold, White(130));
            double brandY = Height - Pad - brand.Height + 4;
            dc.DrawText(brand, new Point(Pad, brandY));

            // 長條圖：夾在文字與 Shima 之間，貼著下方
            double chartBottom = brandY - 26, chartTop = Math.Max(lineY + plays.Height + 28, chartBottom - 150);
            double chartH = chartBottom - chartTop;
            if (period == StatsPeriod.Year && chartH > 60)
            {
                // 年：熱力圖，格子一律正方形，貼著下方；月份數字在上面
                var heat = Stats.Heat(summary.Daily, summary.From.Year, culture.DateTimeFormat.FirstDayOfWeek, now);
                const double Gap = 3;
                double cell = Math.Min((LeftW - (heat.Weeks - 1) * Gap) / heat.Weeks, (chartH - 28 - 6 * Gap) / 7);
                double gridH = 7 * cell + 6 * Gap, gridTop = chartBottom - gridH;
                var levels = new Brush[] { White(30), WithAlpha(90), WithAlpha(145), WithAlpha(200), WithAlpha(255) };
                Brush WithAlpha(byte a) => new SolidColorBrush(Color.FromArgb(a, accent.R, accent.G, accent.B));
                for (int w = 0; w < heat.Weeks; w++)
                    for (int r = 0; r < 7; r++)
                    {
                        if (heat.Cells[w, r] is not { } c) continue;
                        var rect = new Rect(Pad + w * (cell + Gap), gridTop + r * (cell + Gap), cell, cell);
                        dc.DrawRoundedRectangle(c.Future ? White(12) : levels[c.Level], c.Day == now.Date ? new Pen(Brushes.White, 2) : null, rect, 3, 3);
                    }
                foreach (var (month, week) in heat.MonthWeeks)
                {
                    var m = Text(month.ToString(CultureInfo.InvariantCulture), 18, FontWeights.Normal, White(150));
                    dc.DrawText(m, new Point(Math.Min(Pad + week * (cell + Gap), Pad + LeftW - m.Width), gridTop - m.Height - 6));
                }
            }
            else if (bars.Count > 0 && chartH > 20)
            {
                double slot = LeftW / (period == StatsPeriod.Week ? Math.Max(7, bars.Count) : bars.Count), bar = Math.Clamp(slot * 0.62, 2, 26);
                double max = Math.Max(1, bars.Max(b => b.Time.TotalSeconds));
                int current = summary.To > now.Date ? bars.FindLastIndex(b => b.Start <= now.Date) : -1;   // 過去的期間沒有「今天」
                for (int i = 0; i < bars.Count; i++)
                {
                    double cx = Pad + i * slot + slot / 2;
                    double h = bars[i].Time.TotalSeconds / max * chartH;
                    if (h < 4) { dc.DrawRoundedRectangle(White(36), null, new Rect(cx - bar / 2, chartBottom - 4, bar, 4), 2, 2); continue; }
                    var fill = i == current ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromArgb(150, accent.R, accent.G, accent.B));
                    dc.DrawRoundedRectangle(fill, null, new Rect(cx - bar / 2, chartBottom - h, bar, h), Math.Min(5, bar / 2), Math.Min(5, bar / 2));
                }
            }

            // 右半：半透明的面板，歌曲前五名、細線、藝人前三名
            var panel = new Rect(PanelX, Pad, PanelW, Height - 2 * Pad);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), new Pen(White(28), 1), panel, 28, 28);
            const double Inner = 32;
            double rx = PanelX + Inner, rw = PanelW - 2 * Inner, y = Pad + 26;

            void Row(RecapRow row, int rank, double height, double nameSize, bool detail)
            {
                var time = Text(row.Time, 19, FontWeights.Normal, White(150));
                double numW = rank > 0 ? 34 : 0, room = rw - numW - time.Width - 16;
                if (rank > 0)
                {
                    var n = Text(rank.ToString(CultureInfo.InvariantCulture), 22, FontWeights.SemiBold, new SolidColorBrush(accent));
                    dc.DrawText(n, new Point(rx, y + (height - n.Height) / 2));
                }
                var name = Text(row.Name, nameSize, FontWeights.SemiBold, Brushes.White, room);
                var sub = detail && row.Detail.Length > 0 ? Text(row.Detail, 18, FontWeights.Normal, White(140), room) : null;
                double block = name.Height + (sub?.Height ?? 0);
                double top = y + (height - block) / 2;
                dc.DrawText(name, new Point(rx + numW, top));
                if (sub != null) dc.DrawText(sub, new Point(rx + numW, top + name.Height));
                dc.DrawText(time, new Point(rx + rw - time.Width, y + (height - time.Height) / 2));
                y += height;
            }

            for (int i = 0; i < content.Tracks.Count; i++) Row(content.Tracks[i], i + 1, 58, 24, true);
            if (content.Artists.Count > 0)
            {
                y += 10;
                dc.DrawRectangle(White(34), null, new Rect(rx, y, rw, 1));
                y += 12;
                foreach (var artist in content.Artists) Row(artist, 0, 44, 22, false);
            }
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

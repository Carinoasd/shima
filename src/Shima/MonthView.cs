// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Shima;

/// <summary>這個月的月曆：星期的縮寫一列，日期六列，今天用強調色的圓圈標出來。</summary>
public sealed class MonthView : FrameworkElement
{
    private const double Row = 26, Head = 22;
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), Clay = Frozen(Color.FromRgb(0xD9, 0x77, 0x57));

    public DateTime Today { get; set; } = DateTime.Today;

    public MonthView() => Height = Head + Row * 6;

    /// <summary>這個月要畫的 42 格：每格是幾號，以及是不是這個月的。一週從 firstDay 開始。</summary>
    public static (int Day, bool InMonth)[] Cells(DateTime month, DayOfWeek firstDay)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        int lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        var start = first.AddDays(-lead);
        return Enumerable.Range(0, 42).Select(i => start.AddDays(i)).Select(d => (d.Day, d.Month == month.Month)).ToArray();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth / 7;
        var culture = Loc.Culture;
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(Loc.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Text(string s, double size, Brush brush) => new(s, culture, FlowDirection.LeftToRight, typeface, size, brush, dpi);

        for (int i = 0; i < 7; i++)
        {
            var name = Text(culture.DateTimeFormat.ShortestDayNames[((int)firstDay + i) % 7], 11, Dim);
            dc.DrawText(name, new Point(i * w + (w - name.Width) / 2, (Head - name.Height) / 2));
        }

        var cells = Cells(Today, firstDay);
        for (int i = 0; i < cells.Length; i++)
        {
            var (day, inMonth) = cells[i];
            double x = i % 7 * w, y = Head + i / 7 * Row;
            bool today = inMonth && day == Today.Day;
            if (today) dc.DrawEllipse(Clay, null, new Point(x + w / 2, y + Row / 2), 11, 11);
            var text = Text(day.ToString(CultureInfo.InvariantCulture), 12.5, today || inMonth ? Brushes.White : Dim);
            if (!inMonth) continue;   // 別的月份的日子不畫，版面比較乾淨
            dc.DrawText(text, new Point(x + (w - text.Width) / 2, y + (Row - text.Height) / 2));
        }
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Agents;

namespace Shima;

/// <summary>這個月的月曆：星期的縮寫一列，日期六列，今天用強調色的圓圈標出來，有提醒的日子底下一個小圓點。</summary>
public sealed class MonthView : FrameworkElement
{
    private const double Row = 26, Head = 22;
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), Clay = Frozen(Color.FromRgb(0xD9, 0x77, 0x57));

    private const double TipPad = 6;
    private static readonly Brush TipBack = Frozen(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x22));
    private static readonly Pen TipPen = FramePen(Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));

    private DateTime _today = DateTime.Today;
    private IReadOnlyDictionary<int, List<Alarm>> _alarms = new Dictionary<int, List<Alarm>>();
    private int _hover;      // 滑鼠下面是這個月的幾號（0：沒有）
    private Point _hoverAt;

    /// <summary>這個月每一天的提醒（<see cref="Alarms.InMonth"/>）：有的日子畫圓點，滑鼠移上去顯示內容。</summary>
    public IReadOnlyDictionary<int, List<Alarm>> AlarmDays
    {
        get => _alarms;
        set { _alarms = value; InvalidateVisual(); }
    }

    /// <summary>截圖用：假裝滑鼠停在這個月的某一天上。</summary>
    internal void Peek(int day)
    {
        var firstDay = Loc.Culture.DateTimeFormat.FirstDayOfWeek;
        int i = Array.FindIndex(Cells(Today, firstDay), c => c.InMonth && c.Day == day);
        _hover = day;
        _hoverAt = new Point(i % 7 * ActualWidth / 7 + ActualWidth / 14, Head + i / 7 * Row + Row / 2);
        InvalidateVisual();
    }

    /// <summary>點了今天或之後的某一天。過去的日子沒有反應。</summary>
    public event Action<DateTime>? DayPicked;

    /// <summary>今天。高度跟著這個月佔幾週走：大部分的月份只要五列，不用永遠留著第六列的空白。</summary>
    public DateTime Today
    {
        get => _today;
        set { _today = value; Height = Head + Row * Weeks(value, Loc.Culture.DateTimeFormat.FirstDayOfWeek); }
    }

    public MonthView() => Today = DateTime.Today;

    /// <summary>這個月要畫幾列（四到六）。</summary>
    public static int Weeks(DateTime month, DayOfWeek firstDay)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        int lead = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        return (lead + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
    }

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

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // 空白處也要接得到滑鼠
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
            // 今天那一格底下是實心圓，圓點改用白色才看得到
            if (_alarms.ContainsKey(day)) dc.DrawEllipse(today ? Brushes.White : Clay, null, new Point(x + w / 2, y + Row - 4.5), 1.75, 1.75);
        }

        // 滑鼠下面那一天的提醒：時間加內容，最多三筆
        if (_hover > 0 && _alarms.TryGetValue(_hover, out var list))
        {
            var lines = list.Take(3).Select(a => a.At.ToString("HH:mm", CultureInfo.InvariantCulture) + (a.Text.Length > 0 ? "  " + IdleLine.Trim(a.Text, 24) : ""));
            if (list.Count > 3) lines = lines.Append("…");
            var tip = Text(string.Join("\n", lines), 11.5, Brushes.White);
            double tw = tip.Width + TipPad * 2, th = tip.Height + TipPad;
            double tx = Math.Clamp(_hoverAt.X - tw / 2, 0, Math.Max(0, ActualWidth - tw));
            double ty = Math.Clamp(_hoverAt.Y - th - 10 >= 0 ? _hoverAt.Y - th - 10 : _hoverAt.Y + 16, 0, Math.Max(0, ActualHeight - th));
            dc.DrawRoundedRectangle(TipBack, TipPen, new Rect(tx, ty, tw, th), 5, 5);
            dc.DrawText(tip, new Point(tx + TipPad, ty + TipPad / 2));
        }
    }

    /// <summary>滑鼠下面是這個月的幾號；日期格以外、別的月份的格子是 0。</summary>
    private int DayAt(Point p)
    {
        if (ActualWidth <= 0 || p.X < 0 || p.X >= ActualWidth || p.Y < Head) return 0;
        int row = (int)((p.Y - Head) / Row), col = (int)(p.X / (ActualWidth / 7));
        if (row >= 6 || col > 6) return 0;
        var (day, inMonth) = Cells(Today, Loc.Culture.DateTimeFormat.FirstDayOfWeek)[row * 7 + col];
        return inMonth ? day : 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int day = DayAt(p);
        Cursor = day >= Today.Day ? Cursors.Hand : null;
        int shown = day > 0 && _alarms.ContainsKey(day) ? day : 0;
        _hoverAt = p;
        if (shown != _hover || shown > 0) { _hover = shown; InvalidateVisual(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover != 0) { _hover = 0; InvalidateVisual(); }
        Cursor = null;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        int day = DayAt(e.GetPosition(this));
        if (day >= Today.Day) { DayPicked?.Invoke(new DateTime(Today.Year, Today.Month, day)); e.Handled = true; return; }
        base.OnMouseLeftButtonUp(e);
    }

    private static Pen FramePen(Brush brush) { var p = new Pen(brush, 1); p.Freeze(); return p; }
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

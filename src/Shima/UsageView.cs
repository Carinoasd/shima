// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Agents;

namespace Shima;

/// <summary>
/// Claude Code 最近 14 天的用量：最上面一行合計，接著一天一根長條（高度是花費；花費不明的日子改用 token 比例、畫成淡色），
/// 今天那根是強調色，下面是這段期間用得最多的三個模型。滑鼠移到長條上顯示那天的日期、花費與 token。
/// </summary>
public sealed class UsageView : FrameworkElement
{
    private const double HeadH = 16, ChartY = 20, ChartH = 56, LabelY = ChartY + ChartH + 3, ListY = 98, RowH = 17, TipPad = 6;
    public const double ViewHeight = ListY + 3 * RowH + 2;

    private static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                                  Faint = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                                  BarBrush = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                                  UnknownBrush = Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                                  Accent = Frozen(Color.FromRgb(0xD9, 0x77, 0x57)),
                                  Hit = Frozen(Color.FromArgb(0x01, 0, 0, 0)),
                                  TipBack = Frozen(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x22));
    private static readonly Pen TipPen = FrozenPen(Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);

    private UsageTrend _trend = UsageTrend.Empty;
    private bool _loading = true;
    private int _hover = -1;
    private Point _tipAt;

    public UsageView()
    {
        Width = 400;
        Height = ViewHeight;
    }

    /// <summary>還在讀紀錄：第一次展開時畫「讀取中」。</summary>
    public bool Loading
    {
        get => _loading;
        set { _loading = value; InvalidateVisual(); }
    }

    public UsageTrend Trend
    {
        get => _trend;
        set { _trend = value; _loading = false; _hover = -1; InvalidateVisual(); }
    }

    /// <summary>「1.2M」「456K」。</summary>
    public static string Amount(long tokens) =>
        tokens >= 1_000_000 ? (tokens / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M" : (tokens / 1000.0).ToString("0", CultureInfo.InvariantCulture) + "K";

    /// <summary>「$1.23」；花費只算了一部分（有不認得的模型）就加「+」。</summary>
    public static string Money(decimal cost, bool known) => "$" + cost.ToString("0.00", CultureInfo.InvariantCulture) + (known ? "" : "+");

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        dc.DrawRectangle(Hit, null, new Rect(0, 0, width, ActualHeight));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var culture = Loc.Culture;

        FormattedText Text(string text, double size, Brush brush, double max = 0)
        {
            var t = new FormattedText(text, culture, FlowDirection.LeftToRight, new Typeface(Loc.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, dpi);
            if (max > 0) { t.MaxTextWidth = max; t.MaxLineCount = 1; t.Trimming = TextTrimming.CharacterEllipsis; }
            return t;
        }

        if (_loading || _trend.Days.Count == 0)
        {
            var wait = Text(L.T("讀取中…"), 12, Dim);
            dc.DrawText(wait, new Point((width - wait.Width) / 2, (ViewHeight - wait.Height) / 2));
            return;
        }

        string amount = Amount(_trend.Tokens.Total) + " tokens";
        dc.DrawText(Text(L.F("近 {0} 天 {1}", _trend.Days.Count, (_trend.Cost > 0 ? Money(_trend.Cost, _trend.CostKnown) + " · " : "") + amount), 11.5, Dim), new Point(0, 0));

        var days = _trend.Days;
        double slot = width / days.Count, bar = Math.Clamp(slot * 0.62, 1, 18);
        decimal maxCost = days.Where(d => d.CostKnown).Select(d => d.Cost).DefaultIfEmpty(0).Max();
        long maxTokens = Math.Max(1, days.Max(d => d.Tokens.Total));
        double bottom = ChartY + ChartH;
        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            double h = d.CostKnown ? (maxCost > 0 ? (double)(d.Cost / maxCost) * ChartH : 0) : (double)d.Tokens.Total / maxTokens * ChartH;
            bool today = i == days.Count - 1;
            var brush = !d.CostKnown ? UnknownBrush : today ? Accent : BarBrush;
            if (d.Tokens.Total > 0) h = Math.Max(h, 3);   // 用得很少也要看得到
            double cx = i * slot + slot / 2;
            if (h < 2) dc.DrawRectangle(Faint, null, new Rect(cx - bar / 2, bottom - 2, bar, 2));
            else dc.DrawRoundedRectangle(i == _hover && brush != Accent ? Brushes.White : brush, null, new Rect(cx - bar / 2, bottom - h, bar, h), Math.Min(2, bar / 2), Math.Min(2, bar / 2));
            // 今天一定標；其他每隔四天標一次，且離今天的標籤留一點空
            if (today || (i % 4 == 0 && days.Count - 1 - i >= 3))
            {
                var t = Text(d.Day.ToString("M/d", CultureInfo.InvariantCulture), 10.5, today ? Brushes.White : Dim);
                dc.DrawText(t, new Point(Math.Clamp(cx - t.Width / 2, 0, width - t.Width), LabelY));
            }
        }

        // 前三個模型：名稱在左，花費與 token 靠右對齊成兩欄
        const double CostCol = 288;
        for (int i = 0; i < Math.Min(3, _trend.Models.Count); i++)
        {
            var m = _trend.Models[i];
            double y = ListY + i * RowH;
            var tokens = Text(Amount(m.Tokens.Total), 11, Dim);
            dc.DrawText(tokens, new Point(width - tokens.Width, y + (RowH - tokens.Height) / 2));
            if (m.Cost is { } cost)
            {
                var money = Text(Money(cost, true), 11.5, Brushes.White);
                dc.DrawText(money, new Point(width - 62 - money.Width, y + (RowH - money.Height) / 2));
            }
            var name = Text(Pretty(m.Model), 12, Brushes.White, CostCol - 100);
            dc.DrawText(name, new Point(0, y + (RowH - name.Height) / 2));
        }

        if (_hover >= 0 && _hover < days.Count)
        {
            var d = days[_hover];
            string tip = d.Day.ToString(culture.DateTimeFormat.MonthDayPattern, culture) + "  " + (d.Cost > 0 ? Money(d.Cost, d.CostKnown) + " · " : "") + Amount(d.Tokens.Total) + " tokens";
            var t = Text(tip, 11.5, Brushes.White);
            double w = t.Width + TipPad * 2, h = t.Height + TipPad;
            double tx = Math.Clamp(_tipAt.X + 12, 0, Math.Max(0, width - w));
            double ty = Math.Clamp(_tipAt.Y - h - 8, 0, Math.Max(0, ActualHeight - h));
            dc.DrawRoundedRectangle(TipBack, TipPen, new Rect(tx, ty, w, h), 5, 5);
            dc.DrawText(t, new Point(tx + TipPad, ty + TipPad / 2));
        }
    }

    /// <summary>顯示用的模型名：拿掉前面的 claude-。</summary>
    private static string Pretty(string model) => model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? model[7..] : model;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        int hover = -1;
        if (!_loading && _trend.Days.Count > 0 && p.Y >= ChartY && p.Y <= ChartY + ChartH + 14 && ActualWidth > 0)
            hover = Math.Clamp((int)(p.X / (ActualWidth / _trend.Days.Count)), 0, _trend.Days.Count - 1);
        if (hover != _hover || (hover >= 0 && p != _tipAt)) { _hover = hover; _tipAt = p; InvalidateVisual(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover >= 0) { _hover = -1; InvalidateVisual(); }
        base.OnMouseLeave(e);
    }

    private static Pen FrozenPen(Brush brush, double thickness) { var p = new Pen(brush, thickness); p.Freeze(); return p; }
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

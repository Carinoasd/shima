// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Listening;

namespace Shima;

/// <summary>
/// 聆聽統計：上面切換期間（週／月／年／全部）與連續天數，接著是總時間與次數、
/// 最常聽的五首歌與五位藝人，最下面是長條圖（週、月一天一根，年一個月一根，全部兩年內一個月一根、再長一年一根）。
/// </summary>
public sealed class StatsView : FrameworkElement
{
    private const double TabsH = 24, TotalY = 30, ListY = 84, RowH = 21, ChartY = 204, ChartH = 84, LabelH = 16;
    public const double ViewHeight = ChartY + ChartH + LabelH + 4;

    private static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                                  Faint = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                                  BarBrush = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                                  Hit = Frozen(Color.FromArgb(0x01, 0, 0, 0));

    private static readonly StatsPeriod[] Periods = [StatsPeriod.Week, StatsPeriod.Month, StatsPeriod.Year, StatsPeriod.All];

    private IReadOnlyList<Listen> _listens = [];
    private DateTime _now = DateTime.Now;
    private StatsPeriod _period = StatsPeriod.Week;
    private StatsSummary? _summary;
    private List<(DateTime Start, TimeSpan Time)> _bars = [];
    private bool _byYear;
    private HeatGrid? _heat;
    private bool _showHours;                       // 月與全部：下面的圖是每日長條還是一天當中幾點
    private string? _tip;                          // 滑鼠下面那一格的說明，畫在控制項裡
    private Point _tipAt;
    private readonly Rect[] _tabs = new Rect[4];
    private readonly Rect[] _modes = new Rect[2];
    private readonly Rect[] _arrows = new Rect[2];     // 0 往前、1 往後
    private int _back;                                 // 往前翻了幾段，0 是最新的這一段
    private DateTime? _earliest;
    private Rect _share;

    private const double ToggleH = 16, HeatGap = 1.5, TipPad = 6;
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Brush[] HeatBrushes =
    [
        Faint,
        Frozen(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)), Frozen(Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF)),
        Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), Frozen(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)),
    ];
    private static readonly Brush HeatFuture = Frozen(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
                                  TipBack = Frozen(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x22));
    private static readonly Pen TodayPen = FrozenPen(Frozen(Color.FromRgb(0xD9, 0x77, 0x57)), 1.25), TipPen = FrozenPen(Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);

    /// <summary>月與全部期間下面的圖：true 是一天當中幾點在聽。</summary>
    public bool ShowHours
    {
        get => _showHours;
        set { _showHours = value; InvalidateVisual(); }
    }

    private bool Toggleable => _period is StatsPeriod.Month or StatsPeriod.All;
    private bool Hours => Toggleable && _showHours;
    private bool Heat => _period == StatsPeriod.Year;

    /// <summary>按下分享鈕：呼叫端用 Summary、Period、Now 做回顧卡片。</summary>
    public event Action? ShareRequested;

    public StatsSummary? Summary => _summary;

    /// <summary>往前翻了幾段（0 是這一週／這個月／今年）；超出能翻的範圍會夾回去。</summary>
    public int Back
    {
        get => _back;
        set
        {
            int back = Math.Max(0, value);
            while (back > 0 && !Stats.CanGoBack(_period, _now, FirstDay, _earliest, back - 1)) back--;
            if (_period == StatsPeriod.All) back = 0;
            if (back == _back) return;
            _back = back;
            _tip = null;
            Recompute();
        }
    }

    private static DayOfWeek FirstDay => Loc.Culture.DateTimeFormat.FirstDayOfWeek;
    public DateTime Now => _now;

    /// <summary>切換期間之後觸發。</summary>
    public event Action<StatsPeriod>? PeriodChanged;

    public StatsView()
    {
        Width = 400;
        Height = ViewHeight;
    }

    public StatsPeriod Period
    {
        get => _period;
        set
        {
            if (_period == value) return;
            _period = value;
            _back = 0;
            _tip = null;
            Recompute();
            PeriodChanged?.Invoke(value);
        }
    }

    /// <summary>換一批紀錄（例如每次打開面板時重讀檔案）。now 決定「本週」「本月」從哪天算。</summary>
    public void SetListens(IReadOnlyList<Listen> listens, DateTime now)
    {
        _listens = listens;
        _now = now;
        _earliest = listens.Count > 0 ? listens.Min(l => l.Start.DateTime) : null;
        while (_back > 0 && !Stats.CanGoBack(_period, _now, FirstDay, _earliest, _back - 1)) _back--;
        Recompute();
    }

    /// <summary>讀紀錄檔（預設 %APPDATA%\Shima\history.jsonl）再顯示。</summary>
    public void Load(string? path = null) => SetListens(HistoryFile.Read(path), DateTime.Now);

    private void Recompute()
    {
        _summary = Stats.Compute(_listens, _period, _now, _back, FirstDay);
        _bars = Stats.Fill(Stats.Bars(_summary.Daily, _period), _period);
        _byYear = _period == StatsPeriod.All && Stats.ByYear(_summary.Daily);
        _heat = _period == StatsPeriod.Year ? Stats.Heat(_summary.Daily, _summary.From.Year, FirstDay, _now) : null;
        InvalidateVisual();
    }

    /// <summary>總時間：「12 小時 5 分」或「38 分」。</summary>
    public static string Long(TimeSpan t)
    {
        long minutes = (long)t.TotalMinutes;
        return minutes >= 60 ? Loc.F("{0} 小時 {1} 分", minutes / 60, minutes % 60) : Loc.F("{0} 分", minutes);
    }

    /// <summary>排行榜旁邊的短時間：「38 分」或「2.5 小時」。</summary>
    public static string Short(TimeSpan t) =>
        t.TotalMinutes >= 60 ? Loc.F("{0} 小時", Math.Round(t.TotalHours, 1).ToString("0.#", Loc.Culture))
                             : Loc.F("{0} 分", (long)Math.Max(1, Math.Round(t.TotalMinutes)));

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        dc.DrawRectangle(Hit, null, new Rect(0, 0, width, ActualHeight));   // 整塊都收得到滑鼠
        if (_summary == null) Recompute();
        var s = _summary!;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var culture = Loc.Culture;

        FormattedText Text(string text, double size, Brush brush, FontFamily? family = null, FontWeight? weight = null, double max = 0)
        {
            var t = new FormattedText(text, culture, FlowDirection.LeftToRight,
                new Typeface(family ?? Loc.Font, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, brush, dpi);
            if (max > 0) { t.MaxTextWidth = max; t.MaxLineCount = 1; t.Trimming = TextTrimming.CharacterEllipsis; }
            return t;
        }

        // 期間
        string[] names = [L.T("週"), L.T("月"), L.T("年"), L.T("全部")];
        double x = 0;
        for (int i = 0; i < names.Length; i++)
        {
            bool on = Periods[i] == _period;
            var t = Text(names[i], 12.5, on ? Brushes.White : Dim, weight: on ? FontWeights.SemiBold : FontWeights.Normal);
            dc.DrawText(t, new Point(x, (TabsH - t.Height) / 2));
            _tabs[i] = new Rect(x - 6, 0, t.Width + 12, TabsH);
            x += t.Width + 18;
        }
        // 翻看過去的期間：緊接在期間後面的左右箭頭，翻到過去時中間寫那一段
        Array.Clear(_arrows);
        double rightEdge = width - 22;
        if (_period != StatsPeriod.All)
        {
            bool canBack = Stats.CanGoBack(_period, _now, FirstDay, _earliest, _back), canForward = Stats.CanGoForward(_period, _back);
            double ax = x + 2;
            var left = Text("\uE76B", 11, canBack ? Brushes.White : Faint, Icons);
            dc.DrawText(left, new Point(ax, (TabsH - left.Height) / 2));
            _arrows[0] = new Rect(ax - 5, 0, left.Width + 10, TabsH);
            ax += left.Width + 10;
            if (_back > 0)
            {
                var label = Text(Stats.Label(_period, _summary!.From, culture), 12, Dim, max: Math.Max(10, rightEdge - ax - 28));
                dc.DrawText(label, new Point(ax, (TabsH - label.Height) / 2));
                ax += label.Width + 10;
            }
            var right = Text("\uE76C", 11, canForward ? Brushes.White : Faint, Icons);
            dc.DrawText(right, new Point(ax, (TabsH - right.Height) / 2));
            _arrows[1] = new Rect(ax - 5, 0, right.Width + 10, TabsH);
            if (!canBack) _arrows[0] = Rect.Empty;
            if (!canForward) _arrows[1] = Rect.Empty;
        }
        // 最右邊是分享鈕，連續天數在它左邊
        var share = Text("\uE72D", 13, Dim, Icons);
        dc.DrawText(share, new Point(width - share.Width, (TabsH - share.Height) / 2));
        _share = new Rect(width - share.Width - 8, 0, share.Width + 8, TabsH);
        if (s.Streak > 0)
        {
            var streak = Text(L.F("連續 {0} 天", s.Streak), 12, Dim);
            dc.DrawText(streak, new Point(width - share.Width - 14 - streak.Width, (TabsH - streak.Height) / 2));
        }

        // 總時間與次數，底線對齊
        var total = Text(Long(s.Total), 26, Brushes.White, weight: FontWeights.SemiBold);
        dc.DrawText(total, new Point(0, TotalY));
        var plays = Text(L.F("{0} 次", s.Plays), 13, Dim);
        dc.DrawText(plays, new Point(total.Width + 12, TotalY + total.Baseline - plays.Baseline));

        // 前五名：左邊歌、右邊藝人
        double split = Math.Round(width * 0.6);
        Column(s.TopTracks, 0, split - 16, withDetail: true);
        Column(s.TopArtists, split, width - split, withDetail: false);

        void Column(IReadOnlyList<Ranked> rows, double left, double w, bool withDetail)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                double y = ListY + i * RowH;
                var time = Text(Short(r.Time), 11, Dim);
                dc.DrawText(time, new Point(left + w - time.Width, y + (RowH - time.Height) / 2));
                string line = withDetail && r.Detail.Length > 0 ? r.Name + "  " + r.Detail : r.Name;
                var name = Text(line, 12.5, Brushes.White, Fonts.For(Fonts.HasKana(line)), max: Math.Max(10, w - time.Width - 8));
                if (withDetail && r.Detail.Length > 0) name.SetForegroundBrush(Dim, r.Name.Length, line.Length - r.Name.Length);
                dc.DrawText(name, new Point(left, y + (RowH - name.Height) / 2));
            }
        }

        // 下面的圖：年是熱力圖，月與全部右上角有切換，可以改看一天當中幾點在聽
        double bottom = ChartY + ChartH;
        if (_heat != null) DrawHeat(dc, width, (t, size, brush, family) => Text(t, size, brush, family));
        else
        {
            double top = ChartY;
            if (Toggleable)
            {
                double ix = width;
                for (int i = 1; i >= 0; i--)
                {
                    var icon = Text(i == 0 ? "\uE787" : "\uE121", 12, (i == 1) == _showHours ? Brushes.White : Dim, Icons);
                    ix -= icon.Width;
                    dc.DrawText(icon, new Point(ix, ChartY - 2 + (ToggleH - icon.Height) / 2));
                    _modes[i] = new Rect(ix - 5, ChartY - 2, icon.Width + 10, ToggleH);
                    ix -= 10;
                }
                top = ChartY + ToggleH;
            }
            else Array.Clear(_modes);
            double chartH = bottom - top;

            if (Hours)
            {
                var hours = s.Hours;
                double slot = width / 24, bar = Math.Clamp(slot * 0.62, 1, 18);
                double peak = hours.Max(h => h.TotalSeconds), hmax = Math.Max(1, peak);
                int top1 = Array.FindIndex(hours, h => h.TotalSeconds == peak);
                for (int h = 0; h < 24; h++) Bar(dc, h * slot + slot / 2, bar, hours[h].TotalSeconds / hmax * chartH, bottom, h == top1 && peak > 0 ? Brushes.White : BarBrush);
                foreach (int h in new[] { 0, 6, 12, 18 })
                {
                    var t = Text(h.ToString(CultureInfo.InvariantCulture), 10.5, Dim);
                    double cx = h * slot + slot / 2;
                    dc.DrawText(t, new Point(Math.Clamp(cx - t.Width / 2, 0, width - t.Width), bottom + 4));
                }
            }
            else if (_bars.Count > 0)
            {
                double slot = width / _bars.Count;
                double bar = Math.Clamp(slot * 0.62, 1, 18);
                double max = Math.Max(1, _bars.Max(b => b.Time.TotalSeconds));
                int current = s.To > _now.Date ? _bars.FindLastIndex(b => b.Start <= _now.Date) : -1;   // 過去的期間沒有「今天」
                for (int i = 0; i < _bars.Count; i++)
                    Bar(dc, i * slot + slot / 2, bar, _bars[i].Time.TotalSeconds / max * chartH, bottom, i == current ? Brushes.White : BarBrush);

                for (int i = 0; i < _bars.Count; i++)
                {
                    var label = BarLabel(i);
                    if (label == null) continue;
                    var t = Text(label, 10.5, i == current ? Brushes.White : Dim);
                    double cx = i * slot + slot / 2;
                    dc.DrawText(t, new Point(Math.Clamp(cx - t.Width / 2, 0, width - t.Width), bottom + 4));
                }
            }
        }

        // 滑鼠下面那一格的日期與時間，跟著滑鼠、不超出邊界
        if (_tip != null)
        {
            var tip = Text(_tip, 11.5, Brushes.White);
            double w = tip.Width + TipPad * 2, h = tip.Height + TipPad;
            double tx = Math.Clamp(_tipAt.X + 12, 0, Math.Max(0, width - w));
            double ty = Math.Clamp(_tipAt.Y - h - 8 >= 0 ? _tipAt.Y - h - 8 : _tipAt.Y + 18, 0, Math.Max(0, ActualHeight - h));
            dc.DrawRoundedRectangle(TipBack, TipPen, new Rect(tx, ty, w, h), 5, 5);
            dc.DrawText(tip, new Point(tx + TipPad, ty + TipPad / 2));
        }
    }

    private static void Bar(DrawingContext dc, double cx, double bar, double h, double bottom, Brush brush)
    {
        if (h < 2) { dc.DrawRectangle(Faint, null, new Rect(cx - bar / 2, bottom - 2, bar, 2)); return; }
        double radius = Math.Min(2, bar / 2);
        dc.DrawRoundedRectangle(brush, null, new Rect(cx - bar / 2, bottom - h, bar, h), radius, radius);
    }

    /// <summary>熱力圖的格子大小與位置：寬度平分給所有的週，高度平分給七列，月份數字在上面。</summary>
    private (double W, double H, double Top) HeatLayout(double width)
    {
        int weeks = _heat?.Weeks ?? 53;
        double top = ChartY + 16, area = ChartY + ChartH + LabelH - top;
        return ((width - (weeks - 1) * HeatGap) / weeks, (area - 6 * HeatGap) / 7, top);
    }

    private void DrawHeat(DrawingContext dc, double width, Func<string, double, Brush, FontFamily?, FormattedText> text)
    {
        var heat = _heat!;
        var (cw, ch, top) = HeatLayout(width);
        for (int w = 0; w < heat.Weeks; w++)
            for (int r = 0; r < 7; r++)
            {
                if (heat.Cells[w, r] is not { } cell) continue;
                var rect = new Rect(w * (cw + HeatGap), top + r * (ch + HeatGap), cw, ch);
                dc.DrawRoundedRectangle(cell.Future ? HeatFuture : HeatBrushes[cell.Level], cell.Day == _now.Date ? TodayPen : null, rect, 1.5, 1.5);
            }
        foreach (var (month, week) in heat.MonthWeeks)
        {
            var t = text(month.ToString(CultureInfo.InvariantCulture), 10.5, Dim, null);
            dc.DrawText(t, new Point(Math.Min(week * (cw + HeatGap), width - t.Width), ChartY + 1));
        }
    }

    /// <summary>長條圖下面的字：週是星期、月是 1、8、15…號、年是月份、全部是年份或月份。</summary>
    private string? BarLabel(int i)
    {
        var start = _bars[i].Start;
        var format = Loc.Culture.DateTimeFormat;
        return _period switch
        {
            StatsPeriod.Week => format.ShortestDayNames[(int)start.DayOfWeek],
            StatsPeriod.Month => start.Day % 7 == 1 ? start.Day.ToString(CultureInfo.InvariantCulture) : null,
            StatsPeriod.Year => start.Month.ToString(CultureInfo.InvariantCulture),
            _ when _byYear => _bars.Count <= 8 || i % ((_bars.Count + 7) / 8) == 0 ? start.Year.ToString(CultureInfo.InvariantCulture) : null,
            // 一個月一根：一月與第一根寫年份，其他每三個月寫月份
            _ => i == 0 || start.Month == 1 ? start.Year.ToString(CultureInfo.InvariantCulture)
                : _bars.Count <= 12 || start.Month % 3 == 1 ? start.Month.ToString(CultureInfo.InvariantCulture) : null,
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_share.Contains(p)) { ShareRequested?.Invoke(); e.Handled = true; return; }
        for (int i = 0; i < _arrows.Length; i++)
            if (_arrows[i].Contains(p)) { Back += i == 0 ? 1 : -1; e.Handled = true; return; }
        for (int i = 0; i < _tabs.Length; i++)
            if (_tabs[i].Contains(p)) { Period = Periods[i]; e.Handled = true; return; }
        if (Toggleable)
            for (int i = 0; i < _modes.Length; i++)
                if (_modes[i].Contains(p))
                {
                    _showHours = i == 1;
                    _tip = Tip(p);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        Cursor = _share.Contains(p) || _arrows.Any(a => a.Contains(p)) || _tabs.Any(t => t.Contains(p)) || (Toggleable && _modes.Any(m => m.Contains(p))) ? Cursors.Hand : null;
        SetTip(Tip(p), p);
        base.OnMouseMove(e);
    }

    /// <summary>滾輪往上是更早的期間，往下回到比較新的；全部沒有可翻的。</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_period == StatsPeriod.All || e.Delta == 0) { base.OnMouseWheel(e); return; }
        Back += e.Delta > 0 ? 1 : -1;
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        SetTip(null, _tipAt);
        base.OnMouseLeave(e);
    }

    private void SetTip(string? tip, Point p)
    {
        if (tip == _tip && (tip == null || p == _tipAt)) return;
        _tip = tip;
        _tipAt = p;
        InvalidateVisual();
    }

    private string? Tip(Point p)
    {
        if (_share.Contains(p)) return L.T("分享聆聽回顧");
        if (_summary == null || ActualWidth <= 0) return null;
        var culture = Loc.Culture;
        double bottom = ChartY + ChartH;

        if (_heat != null)
        {
            var (cw, ch, top) = HeatLayout(ActualWidth);
            int w = (int)Math.Floor(p.X / (cw + HeatGap)), r = (int)Math.Floor((p.Y - top) / (ch + HeatGap));
            if (w < 0 || w >= _heat.Weeks || r < 0 || r > 6 || _heat.Cells[w, r] is not { } cell || cell.Future) return null;
            return cell.Day.ToString(culture.DateTimeFormat.MonthDayPattern, culture) + "  " + Long(cell.Time);
        }

        double chartTop = Toggleable ? ChartY + ToggleH : ChartY;
        if (p.Y < chartTop || p.Y > bottom + LabelH) return null;
        if (Hours)
        {
            int h = Math.Clamp((int)(p.X / (ActualWidth / 24)), 0, 23);
            return h.ToString("00", CultureInfo.InvariantCulture) + ":00  " + Long(_summary.Hours[h]);
        }
        if (_bars.Count == 0) return null;
        int i = Math.Clamp((int)(p.X / (ActualWidth / _bars.Count)), 0, _bars.Count - 1);
        var (start, time) = _bars[i];
        if (start > _now.Date) return null;
        string when = _period switch
        {
            StatsPeriod.Week or StatsPeriod.Month => start.ToString(culture.DateTimeFormat.MonthDayPattern, culture),
            _ when _byYear => start.Year.ToString(CultureInfo.InvariantCulture),
            _ => start.ToString(culture.DateTimeFormat.YearMonthPattern, culture),
        };
        return when + "  " + Long(time);
    }

    private static Pen FrozenPen(Brush brush, double thickness) { var p = new Pen(brush, thickness); p.Freeze(); return p; }
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

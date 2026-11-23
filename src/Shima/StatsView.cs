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
    private readonly Rect[] _tabs = new Rect[4];

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
            Recompute();
            PeriodChanged?.Invoke(value);
        }
    }

    /// <summary>換一批紀錄（例如每次打開面板時重讀檔案）。now 決定「本週」「本月」從哪天算。</summary>
    public void SetListens(IReadOnlyList<Listen> listens, DateTime now)
    {
        _listens = listens;
        _now = now;
        Recompute();
    }

    /// <summary>讀紀錄檔（預設 %APPDATA%\Shima\history.jsonl）再顯示。</summary>
    public void Load(string? path = null) => SetListens(HistoryFile.Read(path), DateTime.Now);

    private void Recompute()
    {
        _summary = Stats.Compute(_listens, _period, _now, Loc.Culture.DateTimeFormat.FirstDayOfWeek);
        _bars = Stats.Bars(_summary.Daily, _period);
        _byYear = _period == StatsPeriod.All && Stats.ByYear(_summary.Daily);
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
        t.TotalMinutes >= 60 ? Loc.F("{0} 小時", Math.Round(t.TotalHours, 1).ToString("0.#", CultureInfo.InvariantCulture))
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
        if (s.Streak > 0)
        {
            var streak = Text(L.F("連續 {0} 天", s.Streak), 12, Dim);
            dc.DrawText(streak, new Point(width - streak.Width, (TabsH - streak.Height) / 2));
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

        // 長條圖
        if (_bars.Count == 0) return;
        double slot = width / _bars.Count;
        double bar = Math.Clamp(slot * 0.62, 1, 18);
        double max = Math.Max(1, _bars.Max(b => b.Time.TotalSeconds));
        int current = _bars.FindLastIndex(b => b.Start <= _now.Date);
        double bottom = ChartY + ChartH;
        for (int i = 0; i < _bars.Count; i++)
        {
            double cx = i * slot + slot / 2;
            double h = _bars[i].Time.TotalSeconds / max * ChartH;
            var brush = i == current ? Brushes.White : BarBrush;
            if (h < 2) { dc.DrawRectangle(Faint, null, new Rect(cx - bar / 2, bottom - 2, bar, 2)); continue; }
            double radius = Math.Min(2, bar / 2);
            dc.DrawRoundedRectangle(brush, null, new Rect(cx - bar / 2, bottom - h, bar, h), radius, radius);
        }

        for (int i = 0; i < _bars.Count; i++)
        {
            var label = BarLabel(i);
            if (label == null) continue;
            var t = Text(label, 10.5, i == current ? Brushes.White : Dim);
            double cx = i * slot + slot / 2;
            dc.DrawText(t, new Point(Math.Clamp(cx - t.Width / 2, 0, width - t.Width), bottom + 4));
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
        for (int i = 0; i < _tabs.Length; i++)
            if (_tabs[i].Contains(p)) { Period = Periods[i]; e.Handled = true; return; }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        Cursor = _tabs.Any(t => t.Contains(p)) ? Cursors.Hand : null;
        var tip = BarTip(p);
        if (ToolTip as string != tip) ToolTip = tip;
        base.OnMouseMove(e);
    }

    /// <summary>滑到長條上：那一天（或那個月、那一年）與聽的時間。</summary>
    private string? BarTip(Point p)
    {
        if (_bars.Count == 0 || p.Y < ChartY || p.Y > ChartY + ChartH + LabelH || ActualWidth <= 0) return null;
        int i = Math.Clamp((int)(p.X / (ActualWidth / _bars.Count)), 0, _bars.Count - 1);
        var (start, time) = _bars[i];
        var culture = Loc.Culture;
        string when = _period switch
        {
            StatsPeriod.Week or StatsPeriod.Month => start.ToString(culture.DateTimeFormat.MonthDayPattern, culture),
            StatsPeriod.Year => start.ToString(culture.DateTimeFormat.YearMonthPattern, culture),
            _ when _byYear => start.Year.ToString(CultureInfo.InvariantCulture),
            _ => start.ToString(culture.DateTimeFormat.YearMonthPattern, culture),
        };
        return when + "  " + Long(time);
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

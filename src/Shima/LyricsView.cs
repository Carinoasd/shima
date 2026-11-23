// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Lyrics;
using Shima.Motion;

namespace Shima;

/// <summary>
/// 整份歌詞。正在唱的那句比較大、比較亮，光跟著唱的進度掃過去，下面可以多一行翻譯；
/// 其他句子暗一點。整份平滑地往上捲，讓正在唱的那句保持在偏上的位置，上下邊緣淡出。點任何一句會跳到那裡。
/// </summary>
public sealed class LyricsView : FrameworkElement
{
    private const double Row = 25, NowRow = 30, SubRow = 19, Anchor = 0.4;
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), Sub = Frozen(Color.FromArgb(0xB4, 0xFF, 0xFF, 0xFF));
    private static readonly Color DimColor = Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF);

    private readonly Spring _scroll = new(0, 120, 20);
    private LyricSheet? _sheet;
    private IReadOnlyList<string>? _second;
    private FormattedText?[] _plain = [], _big = [], _sub = [];
    private double[]?[] _prefixes = [];
    private bool _japanese, _frames;
    private int _index = int.MinValue;
    private TimeSpan _lastFrame;
    /// <summary>上次畫的光頭在正在唱的那句的第幾個像素；NaN 是還沒畫過。</summary>
    private double _drawnHead = double.NaN;
    /// <summary>掃光的漸層只做一支，每次畫只改位置與停點。</summary>
    private readonly LinearGradientBrush _sweepBrush = new() { MappingMode = BrushMappingMode.Absolute };

    public Func<(TimeSpan Position, bool Playing)>? Clock { get; set; }
    public Color Light { get; set; } = Colors.White;
    /// <summary>
    /// 顯示 lines 行（3、5、7）要多高：一行的高度乘上行數再多一行，留給正在唱的那句放大與第二行。
    /// 5 行剛好是原本的 150；淡出與正在唱的那句的位置都是照高度的比例算，所以不用跟著改。
    /// </summary>
    public static double HeightFor(int lines) => Row * ((lines <= 3 ? 3 : lines >= 7 ? 7 : 5) + 1);

    /// <summary>使用者點了某一句：那句開始的時間。</summary>
    public event Action<TimeSpan>? Seek;

    public LyricsView()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.16));
        fade.GradientStops.Add(new GradientStop(Colors.Black, 0.8));
        fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        fade.Freeze();
        OpacityMask = fade;
        foreach (var c in (Color[])[Colors.White, Colors.White, Colors.White, DimColor, DimColor]) _sweepBrush.GradientStops.Add(new GradientStop(c, 0));
    }

    /// <param name="second">每一句下面的第二行（翻譯或羅馬拼音），沒有就傳 null。</param>
    public void Show(LyricSheet? sheet, IReadOnlyList<string>? second)
    {
        if (ReferenceEquals(sheet, _sheet) && ReferenceEquals(second, _second)) return;
        _sheet = sheet;
        _second = second;
        int n = sheet?.Lines.Count ?? 0;
        _plain = new FormattedText?[n];
        _big = new FormattedText?[n];
        _sub = new FormattedText?[n];
        _prefixes = new double[]?[n];
        _japanese = sheet?.Lines.Any(l => Fonts.HasKana(l.Text)) == true;
        _index = int.MinValue;
        Follow(jump: true);
        InvalidateVisual();
    }

    /// <summary>每個 tick 呼叫：換句了就捲過去，需要的話開始逐幀更新。</summary>
    public void Wake()
    {
        Follow(jump: false);
        if (_frames) return;
        if (!Moving)
        {
            // 沒在動但光頭的位置變了（暫停時拖了進度）：補畫一次
            if (Active && IsVisible && FrameGate.Moved(_drawnHead, Head())) InvalidateVisual();
            return;
        }
        _frames = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private int Current => _sheet == null || Clock == null ? -1 : Lrc.LineAt(_sheet.Lines, Clock().Position);

    private bool HasSub(int i) => _second != null && i >= 0 && i < _second.Count && _second[i].Length > 0;

    private double HeightOf(int i, int current) => i == current ? NowRow + (HasSub(i) ? SubRow : 0) : Row;

    private double TopOf(int i, int current)
    {
        double y = i * Row;
        if (current >= 0 && i > current) y += HeightOf(current, current) - Row;
        return y;
    }

    private void Follow(bool jump)
    {
        int now = Current;
        // 沒在看的時候直接跳到位置，打開時就已經在對的地方；位置沒變就什麼都不做，
        // 不然島收著的時候每 0.1 秒都在重畫一塊看不到的東西
        // 明確要求跳到位置的（大小變了、換歌詞了）不能省：收著的時候高度還是 0，展開後要重算一次
        if (!Active)
        {
            if (now == _index && !jump) return;
            jump = true;
        }
        if (now == _index && !jump) return;
        _index = now;
        int at = Math.Max(0, now);
        double target = TopOf(at, now) + HeightOf(at, now) / 2 - Math.Max(1, ActualHeight) * Anchor;
        if (jump) _scroll.Jump(target); else _scroll.Target = target;
        if (Active) InvalidateVisual();
    }

    /// <summary>面板收起來、或不在音樂分頁時不用畫。</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value) InvalidateVisual();   // 沒在看的期間位置一直有在跟，只是沒畫；現在看得到了，畫一次
        }
    }
    private bool _active;

    /// <summary>
    /// 要不要逐幀更新：還在捲，或正在唱的那句光還沒掃完。那句掃完了、間奏、前奏都不用動，
    /// 換句時每 0.1 秒的 <see cref="Wake"/> 會再叫醒它。
    /// </summary>
    private bool Moving => Active && IsVisible && _sheet != null && (!_scroll.Settled || Clock?.Invoke().Playing == true && Sweeping);

    private bool Sweeping => _sheet is { Synced: true } sheet && _index >= 0 && _index < sheet.Lines.Count && sheet.Lines[_index].Text.Length > 0 && Share(_index) < 1;

    /// <summary>第 i 句唱到的位置佔那句寬度的比例。</summary>
    private double Share(int i)
    {
        var line = _sheet!.Lines[i];
        _prefixes[i] ??= Fonts.Prefixes(line.Text, new Typeface(Fonts.For(_japanese), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 16, Fonts.Culture(_japanese), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var position = Clock!().Position;
        // 有逐字時間就照著每個字走；沒有的話整句依長度估
        if (WordTiming.Fits(line.Text, line.Words)) return Singing.WidthAt(WordTiming.CharsAt(line.Words!, position), _prefixes[i]!);
        var length = Singing.Length(line.Text, line.Time, i + 1 < _sheet.Lines.Count ? _sheet.Lines[i + 1].Time : null);
        double progress = length <= TimeSpan.Zero ? 1 : Math.Clamp((position - line.Time) / length, 0, 1);
        return Singing.WidthShare(line.Text, progress, _prefixes[i]!);
    }

    /// <summary>正在唱的那句的光頭現在應該在第幾個像素（還沒畫過那句就是 NaN）。</summary>
    private double Head() =>
        _sheet is { Synced: true } sheet && Clock != null && _index >= 0 && _index < sheet.Lines.Count && sheet.Lines[_index].Text.Length > 0 && _big[_index] is { } big
            ? Share(_index) * big.WidthIncludingTrailingWhitespace : double.NaN;

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;   // 同一幀可能被叫兩次
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        bool scrolling = !_scroll.Settled;
        _scroll.Step(dt);
        bool moving = Moving, beat = Pulse.Beat(now);   // 每一幀都要問，節拍才會一直由幀來打
        // 捲動每一幀都畫；掃光是慢動作，跟著共用的節拍（約每秒 30 次、跟頻譜同一幀）、光頭動了半個像素以上才畫；停下來之前補畫最後一次
        if (scrolling || !moving || beat && FrameGate.Moved(_drawnHead, Head())) InvalidateVisual();
        if (moving) return;
        CompositionTarget.Rendering -= OnFrame;
        _frames = false;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Follow(jump: true);
    }

    private FormattedText Text(string text, double size, FontWeight weight, bool japanese)
    {
        return new FormattedText(text.Length == 0 ? "♪" : text, Fonts.Culture(japanese), FlowDirection.LeftToRight,
            new Typeface(Fonts.For(japanese), FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, MaxTextWidth = Math.Max(1, ActualWidth) };
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        // 透明的底：整塊都要能點
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_sheet is not { } sheet || w <= 0) return;

        int current = _index;
        _drawnHead = double.NaN;
        for (int i = 0; i < sheet.Lines.Count; i++)
        {
            double top = TopOf(i, current) - _scroll.Value, height = HeightOf(i, current);
            if (top + height < 0) continue;
            if (top > h) break;
            var line = sheet.Lines[i];

            if (i != current)
            {
                var text = _plain[i] ??= Text(line.Text, 13, FontWeights.Normal, _japanese);
                text.SetForegroundBrush(Dim);
                dc.DrawText(text, new Point(Math.Max(0, (w - text.WidthIncludingTrailingWhitespace) / 2), top + (Row - text.Height) / 2));
                continue;
            }

            var big = _big[i] ??= Text(line.Text, 16, FontWeights.SemiBold, _japanese);
            double width = big.WidthIncludingTrailingWhitespace, x = Math.Max(0, (w - width) / 2);
            Brush brush = Brushes.White;
            if (sheet.Synced && line.Text.Length > 0 && Clock != null)
            {
                double p = Share(i);
                _drawnHead = p * width;
                double edge = Math.Min(0.12, 16 / Math.Max(1, width)), head = p * (1 + edge);
                var sweep = _sweepBrush;
                sweep.StartPoint = new Point(x, 0);
                sweep.EndPoint = new Point(x + width, 0);
                var stops = sweep.GradientStops;
                stops[1].Offset = Math.Clamp(head - edge * 2.2, 0, 1);
                stops[2].Color = Light;
                stops[2].Offset = Math.Clamp(head - edge, 0, 1);
                stops[3].Offset = Math.Clamp(head, 0, 1);
                stops[4].Offset = 1;
                brush = sweep;
            }
            big.SetForegroundBrush(brush);
            dc.DrawText(big, new Point(x, top + (NowRow - big.Height) / 2));

            if (HasSub(i))
            {
                var sub = _sub[i] ??= Text(_second![i], 12, FontWeights.Normal, Fonts.HasKana(_second[i]));
                sub.SetForegroundBrush(Sub);
                dc.DrawText(sub, new Point(Math.Max(0, (w - sub.WidthIncludingTrailingWhitespace) / 2), top + NowRow + (SubRow - sub.Height) / 2 - 2));
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_sheet is not { Synced: true } sheet) return;
        double y = e.GetPosition(this).Y + _scroll.Value;
        for (int i = 0; i < sheet.Lines.Count; i++)
        {
            double top = TopOf(i, _index);
            if (y >= top && y < top + HeightOf(i, _index)) { Seek?.Invoke(sheet.Lines[i].Time); e.Handled = true; return; }
        }
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

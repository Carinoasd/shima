// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Shima;

/// <summary>
/// 一行會動的字。換句時舊的往上滑走、新的從下面彈上來；有時間軸的歌詞，光會從左到右跟著唱的進度掃過去。
/// 全部自己畫，只有東西在動的時候才逐幀更新。
/// </summary>
public sealed class KaraokeLine : FrameworkElement
{
    private const double Leave = 0.22, Arrive = 0.5;
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF));

    private string _text = "", _old = "";
    private TimeSpan _start, _length;
    private bool _sweep, _frames;
    private long _changedAt = Stopwatch.GetTimestamp();
    private FormattedText? _layout, _oldLayout;
    private double[]? _prefixes;

    public double Size { get; set; } = 12.5;
    public FontWeight Weight { get; set; } = FontWeights.Normal;

    /// <summary>光的顏色（跟著封面）。</summary>
    public Color Light { get; set; } = Colors.White;

    /// <summary>歌現在唱到哪、是不是正在播。</summary>
    public Func<(TimeSpan Position, bool Playing)>? Clock { get; set; }

    /// <summary>這一行字實際的寬度（島用它決定自己要多寬）。</summary>
    public double TextWidth
    {
        get
        {
            if (Layout(ref _layout, _text) is not { } layout) return 0;
            // 量的是整行字本來的寬度，不是被目前的寬度截斷之後的
            layout.MaxTextWidth = 0;
            return layout.WidthIncludingTrailingWhitespace;
        }
    }

    public KaraokeLine() => ClipToBounds = true;

    /// <summary>一句歌詞：從 start 開始唱，length 之內光掃完整句。</summary>
    public void Sing(string text, TimeSpan start, TimeSpan length)
    {
        bool same = text == _text && _sweep && start == _start;
        _start = start;
        _length = length;
        _sweep = true;
        if (!same) Change(text);
        Wake();
    }

    /// <summary>不是歌詞的字（歌名、時鐘）：整行都亮。</summary>
    public void Say(string text)
    {
        if (text == _text && !_sweep) return;
        bool moved = text != _text;
        _sweep = false;
        if (moved) Change(text); else InvalidateVisual();
        Wake();
    }

    /// <summary>播放狀態可能變了（暫停後繼續）：需要的話重新開始逐幀更新。</summary>
    public void Wake()
    {
        if (_frames || !Moving) { InvalidateVisual(); return; }
        _frames = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Change(string text)
    {
        _old = _text;
        _oldLayout = _layout;
        _text = text;
        _layout = null;
        _prefixes = null;
        _changedAt = Stopwatch.GetTimestamp();
    }

    private double SinceChange => Stopwatch.GetElapsedTime(_changedAt).TotalSeconds;

    private double Progress
    {
        get
        {
            if (!_sweep || Clock == null) return 1;
            if (_length <= TimeSpan.Zero) return 1;
            return Math.Clamp((Clock().Position - _start) / _length, 0, 1);
        }
    }

    private bool Moving => IsVisible && (SinceChange < Arrive || (_sweep && Clock?.Invoke().Playing == true && Progress < 1));

    private void OnFrame(object? sender, EventArgs e)
    {
        InvalidateVisual();
        if (Moving) return;
        CompositionTarget.Rendering -= OnFrame;
        _frames = false;
    }

    private FormattedText? Layout(ref FormattedText? cache, string text)
    {
        if (text.Length == 0) return null;
        bool japanese = Fonts.HasKana(text);
        return cache ??= new FormattedText(text, Fonts.Culture(japanese), FlowDirection.LeftToRight,
            new Typeface(Fonts.For(japanese), FontStyles.Normal, Weight, FontStretches.Normal),
            Size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, t = SinceChange;
        if (w <= 0 || h <= 0) return;

        // 舊的那句：往上滑走、淡出
        if (t < Leave && Layout(ref _oldLayout, _old) is { } old)
        {
            double k = t / Leave;
            old.MaxTextWidth = Math.Max(1, w);
            old.SetForegroundBrush(Dim);
            dc.PushOpacity(1 - k);
            dc.DrawText(old, new Point(Math.Max(0, (w - old.WidthIncludingTrailingWhitespace) / 2), (h - old.Height) / 2 - 11 * k * k));
            dc.Pop();
        }

        if (Layout(ref _layout, _text) is not { } now) return;
        now.MaxTextWidth = Math.Max(1, w);
        double width = now.WidthIncludingTrailingWhitespace, x = Math.Max(0, (w - width) / 2);

        // 新的這句：從下面彈上來，稍微過頭再回到位置
        double rise = 12 * Math.Exp(-9 * t) * Math.Cos(11 * t);
        double opacity = Math.Clamp(t / 0.14, 0, 1);

        Brush brush = Brushes.White;
        if (_sweep)
        {
            // 光的前緣是一小段漸層，不是一刀切
            bool japanese = Fonts.HasKana(_text);
            _prefixes ??= Fonts.Prefixes(_text, new Typeface(Fonts.For(japanese), FontStyles.Normal, Weight, FontStretches.Normal), Size, Fonts.Culture(japanese), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double p = Singing.WidthShare(_text, Progress, _prefixes), edge = Math.Min(0.12, 14 / Math.Max(1, width));
            double head = p * (1 + edge);
            var sweep = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x, 0), EndPoint = new Point(x + width, 0) };
            sweep.GradientStops.Add(new GradientStop(Colors.White, 0));
            sweep.GradientStops.Add(new GradientStop(Colors.White, Math.Clamp(head - edge * 2.2, 0, 1)));
            sweep.GradientStops.Add(new GradientStop(Light, Math.Clamp(head - edge, 0, 1)));
            sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF), Math.Clamp(head, 0, 1)));
            sweep.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, 0xFF, 0xFF, 0xFF), 1));
            brush = sweep;
        }
        now.SetForegroundBrush(brush);
        dc.PushOpacity(opacity);
        dc.DrawText(now, new Point(x, (h - now.Height) / 2 + rise));
        dc.Pop();
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

public static class Singing
{
    /// <summary>
    /// 一句歌詞要用多久掃完：到下一句之前大部分時間都在唱，所以填滿它（留一口氣）；
    /// 間奏很長時不讓光慢慢爬，依字數給一個上限。
    /// </summary>
    public static TimeSpan Length(string text, TimeSpan start, TimeSpan? next)
    {
        double natural = Math.Clamp(text.Length * 0.42, 2.5, 12);
        if (next is not { } n || n <= start) return TimeSpan.FromSeconds(natural);
        return TimeSpan.FromSeconds(Math.Clamp((n - start).TotalSeconds * 0.92, 0.6, natural * 1.6));
    }

    /// <summary>唱一個字要多久（相對值）：漢字通常是兩個音節，假名一個，英文字母很短，空白和標點是換氣。</summary>
    public static double Weight(char c) => c switch
    {
        ' ' or '\u3000' => 1.6,
        >= '\u3040' and <= '\u30FF' => c is 'っ' or 'ッ' or 'ー' or 'ゃ' or 'ゅ' or 'ょ' or 'ャ' or 'ュ' or 'ョ' ? 0.6 : 1,
        >= '\u4E00' and <= '\u9FFF' => 1.8,
        _ when char.IsLetterOrDigit(c) => 0.42,
        _ => 0.9,
    };

    /// <summary>
    /// 一句的時間過了 progress（0…1）時唱到第幾個字（可以是小數）。沒有逐字資料，所以是依字的種類估的。
    /// </summary>
    public static double CharsAt(string text, double progress)
    {
        if (text.Length == 0) return 0;
        double total = 0;
        foreach (var c in text) total += Weight(c);
        double target = Math.Clamp(progress, 0, 1) * total, sum = 0;
        for (int i = 0; i < text.Length; i++)
        {
            double w = Weight(text[i]);
            if (sum + w >= target) return i + (w <= 0 ? 0 : (target - sum) / w);
            sum += w;
        }
        return text.Length;
    }

    /// <summary>唱到的位置佔整句寬度的比例。prefixes 來自 <see cref="Fonts.Prefixes"/>。</summary>
    public static double WidthShare(string text, double progress, double[] prefixes)
    {
        double total = prefixes[^1];
        if (total <= 0) return progress;
        double at = CharsAt(text, progress);
        int k = Math.Min((int)at, text.Length - 1);
        return Math.Clamp((prefixes[k] + (prefixes[k + 1] - prefixes[k]) * (at - k)) / total, 0, 1);
    }
}

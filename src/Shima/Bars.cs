// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using Shima.Motion;

namespace Shima;

/// <summary>膠囊上的四根小音量條，跟著實際的聲音跳。</summary>
public sealed class Bars : FrameworkElement
{
    /// <summary>緩動的係數是照每 40 毫秒一步調的。</summary>
    private const double StepSeconds = 0.04;
    private readonly double[] _heights = new double[4], _drawn = new double[4];
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private Brush _brush = Brushes.White;

    public Func<float>? Level { get; set; }
    /// <summary>四段頻譜（低音到高音，每段 0…1）；有的話四根條子各看各的頻段，沒有就用 Level 加一點抖動。</summary>
    public Func<float[]?>? Bands { get; set; }

    public Color Color
    {
        set { var b = new SolidColorBrush(value); b.Freeze(); _brush = b; InvalidateVisual(); }
    }

    public Bars()
    {
        Width = 15;
        Height = 14;
        // 跟角色、歌詞掃光用同一個節拍，重畫落在同一幀
        Action beat = Step;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _clock.Restart(); Pulse.Join(beat); }
            else Pulse.Leave(beat);
        };
    }

    private void Step()
    {
        double seconds = Math.Min(0.1, _clock.Elapsed.TotalSeconds);
        _clock.Restart();
        double rise = Pulse.Ease(0.6, seconds, StepSeconds), fall = Pulse.Ease(0.25, seconds, StepSeconds);
        float level = Level?.Invoke() ?? 0;
        var bands = Bands?.Invoke();
        for (int i = 0; i < _heights.Length; i++)
        {
            // 沒有頻譜時每根條子各自抖一點，才不會四根一樣高
            double target = bands != null && i < bands.Length ? SpectrumView.Scale(bands[i]) : level * (0.55 + _random.NextDouble() * 0.45);
            _heights[i] += (target - _heights[i]) * (target > _heights[i] ? rise : fall);
        }
        // 四根條子的高度都動不到 0.3 個像素就不重畫：整個透明視窗都要跟著重新呈現
        if (!Worth(_drawn, _heights, ActualHeight - 2.5)) return;
        _heights.CopyTo(_drawn, 0);
        InvalidateVisual();
    }

    /// <summary>值得重畫嗎：有任何一根條子跟上次畫的差了至少 0.3 個像素（高度是 0…1，乘上 span 才是像素）。</summary>
    public static bool Worth(double[] drawn, double[] now, double span)
    {
        for (int i = 0; i < now.Length; i++)
            if (Math.Abs(now[i] - drawn[i]) * Math.Max(0, span) >= 0.3) return true;
        return false;
    }

    protected override void OnRender(DrawingContext dc)
    {
        for (int i = 0; i < _heights.Length; i++)
        {
            double h = 2.5 + _heights[i] * (ActualHeight - 2.5);
            dc.DrawRoundedRectangle(_brush, null, new Rect(i * 4, (ActualHeight - h) / 2, 2.6, h), 1.3, 1.3);
        }
    }
}

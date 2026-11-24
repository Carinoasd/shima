// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using Shima.Motion;

namespace Shima;

/// <summary>展開時鋪在背景底部的一排頻譜：低音在左、高音在右，每根上面浮著一顆慢慢落下的頂點。</summary>
public sealed class SpectrumView : FrameworkElement
{
    public const int Count = 56;

    /// <summary>緩動的係數是照每 40 毫秒一步調的。</summary>
    private const double StepSeconds = 0.04;
    private readonly Action _beat;
    private readonly Stopwatch _clock = new();
    private bool _running;
    private readonly double[] _heights = new double[Count], _caps = new double[Count];
    private Brush _fill = Brushes.White, _cap = Brushes.White;
    private bool _active;

    /// <summary>此刻的頻譜，每段 0…1；回傳 null 表示沒有聲音可看（條子會慢慢沉下去）。</summary>
    public Func<float[]?>? Bands { get; set; }

    /// <summary>看得到的時候才動。</summary>
    public bool Active
    {
        get => _active;
        set { _active = value; if (value && IsVisible) Start(); }
    }

    public Color Color
    {
        set
        {
            // 條子畫在歌詞後面：太亮的話最下面兩句歌詞會被吃掉
            var fill = new LinearGradientBrush(Color.FromArgb(84, value.R, value.G, value.B), Color.FromArgb(14, value.R, value.G, value.B), 90);
            fill.Freeze();
            var cap = new SolidColorBrush(Color.FromArgb(120, value.R, value.G, value.B));
            cap.Freeze();
            _fill = fill; _cap = cap;
            InvalidateVisual();
        }
    }

    public SpectrumView()
    {
        IsHitTestVisible = false;
        // 跟著共用的節拍走，跟歌詞掃光落在同一幀
        _beat = () =>
        {
            double seconds = Math.Min(0.1, _clock.Elapsed.TotalSeconds);
            _clock.Restart();
            if (!Step(seconds) && !_active) Stop();   // 全部沉到底了才停
        };
        IsVisibleChanged += (_, _) => { if (IsVisible && _active) Start(); else if (!IsVisible) Stop(); };
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _clock.Restart();
        Pulse.Join(_beat);
    }

    private void Stop()
    {
        _running = false;
        Pulse.Leave(_beat);
    }

    /// <summary>往前走一格（平常由共用的節拍呼叫，seconds 是離上一格多久）。回傳還有沒有條子在動。</summary>
    public bool Step(double seconds = StepSeconds)
    {
        double rise = Pulse.Ease(0.7, seconds, StepSeconds), fall = Pulse.Ease(0.22, seconds, StepSeconds), drop = 0.018 * seconds / StepSeconds;
        var bands = _active ? Bands?.Invoke() : null;
        bool moving = false;
        for (int i = 0; i < Count; i++)
        {
            double target = bands != null && i < bands.Length ? Scale(bands[i]) : 0;
            _heights[i] += (target - _heights[i]) * (target > _heights[i] ? rise : fall);   // 上去快、下來慢
            _caps[i] = Math.Max(_heights[i], _caps[i] - drop);
            if (_caps[i] > 0.004) moving = true;
        }
        InvalidateVisual();
        return moving;
    }

    /// <summary>頻譜的 0…1 是 -60 dB 到 0 dB；底下那一截幾乎永遠有東西，切掉才看得出起伏。</summary>
    public static double Scale(float band) => Math.Clamp((band - 0.18) / 0.82, 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, step = w / Count;
        if (w <= 0 || h <= 0) return;
        for (int i = 0; i < Count; i++)
        {
            double tall = _heights[i] * h;
            if (tall > 0.5) dc.DrawRectangle(_fill, null, new Rect(i * step + step * 0.18, h - tall, step * 0.64, tall));
            double cap = _caps[i] * h;
            if (cap > 2) dc.DrawRectangle(_cap, null, new Rect(i * step + step * 0.18, h - cap - 1.5, step * 0.64, 1));
        }
    }
}

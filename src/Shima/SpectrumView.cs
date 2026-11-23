// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shima;

/// <summary>展開時鋪在背景底部的一排頻譜：低音在左、高音在右，每根上面浮著一顆慢慢落下的頂點。</summary>
public sealed class SpectrumView : FrameworkElement
{
    public const int Count = 56;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly double[] _heights = new double[Count], _caps = new double[Count];
    private Brush _fill = Brushes.White, _cap = Brushes.White;
    private bool _active;

    /// <summary>此刻的頻譜，每段 0…1；回傳 null 表示沒有聲音可看（條子會慢慢沉下去）。</summary>
    public Func<float[]?>? Bands { get; set; }

    /// <summary>看得到的時候才動。</summary>
    public bool Active
    {
        get => _active;
        set { _active = value; if (value && IsVisible) _timer.Start(); }
    }

    public Color Color
    {
        set
        {
            var fill = new LinearGradientBrush(Color.FromArgb(120, value.R, value.G, value.B), Color.FromArgb(18, value.R, value.G, value.B), 90);
            fill.Freeze();
            var cap = new SolidColorBrush(Color.FromArgb(150, value.R, value.G, value.B));
            cap.Freeze();
            _fill = fill; _cap = cap;
            InvalidateVisual();
        }
    }

    public SpectrumView()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) =>
        {
            var bands = _active ? Bands?.Invoke() : null;
            bool moving = false;
            for (int i = 0; i < Count; i++)
            {
                double target = bands != null && i < bands.Length ? Scale(bands[i]) : 0;
                _heights[i] += (target - _heights[i]) * (target > _heights[i] ? 0.7 : 0.22);   // 上去快、下來慢
                _caps[i] = Math.Max(_heights[i], _caps[i] - 0.018);
                if (_caps[i] > 0.004) moving = true;
            }
            InvalidateVisual();
            if (!moving && !_active) _timer.Stop();   // 全部沉到底了才停
        };
        IsVisibleChanged += (_, _) => { if (IsVisible && _active) _timer.Start(); else if (!IsVisible) _timer.Stop(); };
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

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shima;

/// <summary>膠囊上的四根小音量條，跟著實際的聲音跳。</summary>
public sealed class Bars : FrameworkElement
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly double[] _heights = new double[4];
    private readonly Random _random = new();
    private Brush _brush = Brushes.White;

    public Func<float>? Level { get; set; }

    public Color Color
    {
        set { var b = new SolidColorBrush(value); b.Freeze(); _brush = b; InvalidateVisual(); }
    }

    public Bars()
    {
        Width = 15;
        Height = 14;
        _timer.Tick += (_, _) =>
        {
            float level = Level?.Invoke() ?? 0;
            for (int i = 0; i < _heights.Length; i++)
            {
                // 每根條子各自抖一點，才不會四根一樣高
                double target = level * (0.55 + _random.NextDouble() * 0.45);
                _heights[i] += (target - _heights[i]) * (target > _heights[i] ? 0.6 : 0.25);
            }
            InvalidateVisual();
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
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

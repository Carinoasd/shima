// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shima;

/// <summary>
/// 住在膠囊裡的小角色，全部用程式畫。眼睛會跟著游標、會眨眼，放音樂時跟著點頭，換歌時開心一下。
/// </summary>
public sealed class Mascot : FrameworkElement
{
    /// <summary>可以選的角色：設定值 → 顯示名稱。</summary>
    public static readonly (string Id, string Name)[] All =
        [("claude", "Claude"), ("eyes", "眼睛"), ("cat", "貓"), ("ghost", "幽靈"), ("custom", "自己的圖片"), ("none", "不要角色")];

    private static readonly Brush White = Brushes.White, Ink = Brushes.Black;
    private static readonly Brush Fur = Frozen(Color.FromRgb(0x3A, 0x3A, 0x40)), Blush = Frozen(Color.FromArgb(0x90, 0xFF, 0x8F, 0xA8));
    private static readonly Brush Clay = Frozen(Color.FromRgb(0xD9, 0x77, 0x57));

    private static readonly Pen Smile = FrozenPen(Brushes.White, 1.6), InkSmile = FrozenPen(Brushes.Black, 1.4);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private string _kind = "claude";
    private double _gazeX, _gazeY, _nextBlink = 2, _blinkAt = -1, _happyUntil = -1;

    public bool Dancing { get; set; }

    /// <summary>現在的聲音有多大聲（0…1）；沒給就照固定節拍點頭。</summary>
    public Func<float>? Level { get; set; }
    /// <summary>低音有多強（0…1）。突然變大就算一拍，小 Claude 會跟著跳一下。</summary>
    public Func<float>? Bass { get; set; }
    private double _bass, _hopAt = -10;

    public string Kind
    {
        get => _kind;
        set
        {
            _kind = value == Custom || All.Any(a => a.Id == value) ? value : "claude";
            Visibility = _kind == "none" ? Visibility.Collapsed : Visibility.Visible;
            LoadImage();
            Look();
        }
    }

    /// <summary>使用者自己的圖（GIF 或 PNG）當角色時的設定值。</summary>
    public const string Custom = "custom";

    /// <summary>自訂角色的圖片路徑。只在改了的時候讀一次檔。</summary>
    public string ImagePath
    {
        get => _imagePath;
        set
        {
            _imagePath = value ?? "";
            LoadImage();
            Look();
        }
    }

    private string _imagePath = "", _loadedPath = "";
    private MascotImage? _image;

    /// <summary>自訂的圖讀不到就畫原本的小 Claude（設定值還是 custom，不蓋掉使用者的選擇）。</summary>
    private string Drawn => _kind == Custom && _image == null ? "claude" : _kind;

    private void LoadImage()
    {
        if (_kind != Custom) { _image = null; _loadedPath = ""; return; }   // 沒在用就放掉，省記憶體
        if (_imagePath == _loadedPath && (_image != null || _imagePath.Length == 0)) return;
        _loadedPath = _imagePath;
        _image = null;
        if (_imagePath.Length == 0) return;
        try
        {
            _image = MascotImage.Load(_imagePath);
            _imageStart = _clock.Elapsed;
        }
        catch (Exception x) { Log.Write($"自訂角色的圖讀不到，改用預設角色: {x.GetType().Name} {x.Message}"); }
    }

    private TimeSpan _imageStart;

    private void Look()
    {
        string kind = Drawn;
        // 像素角色不要柔邊，一格就是一格
        RenderOptions.SetEdgeMode(this, kind == "claude" ? EdgeMode.Aliased : EdgeMode.Unspecified);
        SnapsToDevicePixels = kind == "claude";
        if (kind == Custom && _image is { } image)
            RenderOptions.SetBitmapScalingMode(this, MascotImage.Pixelated(image.Frames[0].PixelHeight, Height) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        else ClearValue(RenderOptions.BitmapScalingModeProperty);
        InvalidateVisual();
    }

    public Mascot()
    {
        Width = 26;
        Height = 24;
        _timer.Tick += (_, _) => Step();
        IsVisibleChanged += (_, _) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
    }

    /// <summary>有好事發生（換歌了）：笑一下。</summary>
    public void Cheer() => _happyUntil = _clock.Elapsed.TotalSeconds + 1.2;

    private void Step()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now > _nextBlink) { _blinkAt = now; _nextBlink = now + 2.5 + _random.NextDouble() * 4; }

        // 看向游標：越遠偏得越多，但有上限
        if (PresentationSource.FromVisual(this) != null && GetCursorPos(out var p))
        {
            var centre = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            double dx = p.X - centre.X, dy = p.Y - centre.Y, far = Math.Sqrt(dx * dx + dy * dy);
            double pull = Math.Min(1, far / 260);
            double tx = far < 1 ? 0 : dx / far * pull, ty = far < 1 ? 0 : dy / far * pull;
            _gazeX += (tx - _gazeX) * 0.25;
            _gazeY += (ty - _gazeY) * 0.25;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double now = _clock.Elapsed.TotalSeconds, w = ActualWidth, h = ActualHeight;
        double blink = now - _blinkAt is >= 0 and < 0.14 ? Math.Abs(1 - (now - _blinkAt) / 0.07) : 1;   // 1 → 0 → 1
        bool happy = now < _happyUntil;
        // 點頭：一秒兩下，聲音越大點得越用力
        float level = Dancing ? Level?.Invoke() ?? 0.35f : 0;
        float bass = Dancing ? Bass?.Invoke() ?? 0 : 0;
        if (bass > _bass + 0.07 && bass > 0.42 && now - _hopAt > 0.22) _hopAt = now;
        _bass += (bass - _bass) * 0.5;
        bool hop = now - _hopAt < 0.17;
        double bob = Dancing ? Math.Sin(now * Math.PI * 4) * (0.5 + level * 2.4) : 0;
        double cx = w / 2, cy = h / 2 + bob;

        switch (Drawn)
        {
            case Custom when _image is { } image:
            {
                // 自己的圖：跟著音樂上下擺、低音重的時候和換歌時跳一下；位置對齊整數，像素圖才不會晃糊
                var frame = image.At(_clock.Elapsed - _imageStart);
                int lift = happy ? new[] { 3, 5, 5, 3, 1, 0 }[Math.Clamp((int)((1.2 - (_happyUntil - now)) * 8), 0, 5)] : hop ? 3 : 0;
                var box = MascotImage.Fit(w, h, frame.PixelWidth, frame.PixelHeight);
                if (box.IsEmpty) break;
                box.Offset(0, Math.Round(bob) - lift);
                dc.DrawImage(frame, box);
                break;
            }
            case "claude":
            {
                // 像素小 Claude，向 Claude Code 的吉祥物致敬。像 GIF 一樣一格一格動（每秒 8 格），位置都是整數像素。
                int frame = (int)(now * 8);
                double left = Math.Floor(w / 2) - 12, top = Math.Floor(h / 2) - 12;
                int lift = 0, arms = 0;
                bool stepA = false, stepB = false;
                if (happy)
                {
                    // 跳起來、舉手
                    lift = new[] { 3, 5, 5, 3, 1, 0 }[Math.Clamp((int)((1.2 - (_happyUntil - now)) * 8), 0, 5)];
                    arms = -2;
                }
                else if (Dancing)
                {
                    // 踏步：兩組腳輪流抬，身體跟著一上一下；聲音大就跳高一點
                    int beat = frame % 4;
                    lift = hop ? 3 : beat == 1 ? (level > 0.6 ? 2 : 1) : 0;
                    stepA = beat == 1;
                    stepB = beat == 3;
                    arms = beat == 1 ? -1 : 0;
                }
                else lift = frame % 16 < 2 ? 1 : 0;   // 沒事的時候偶爾吸一口氣

                void Box(Brush brush, double x, double y, double bw, double bh) => dc.DrawRectangle(brush, null, new Rect(left + x, top + y - lift, bw, bh));
                Box(Clay, 5, 6, 14, 10);
                Box(Clay, 3, 10 + arms, 2, 3);
                Box(Clay, 19, 10 + arms, 2, 3);
                // 腳踩在地上不跟著身體浮起來，抬腳的那組短一格
                void Leg(double x, bool up) => dc.DrawRectangle(Clay, null, new Rect(left + x, top + 16 - lift, 2, 3 + lift - (up ? 2 : 0)));
                Leg(6, stepA); Leg(13, stepA); Leg(9, stepB); Leg(16, stepB);

                int look = (int)Math.Round(_gazeX);
                int eyeTop = 9 + (int)Math.Round(_gazeY * 0.6);
                bool closed = blink < 0.5;
                if (happy)
                {
                    // 開心：眼睛變成兩個 ^
                    foreach (double ex in (double[])[8, 14])
                    {
                        Box(Ink, ex + look, 10, 1, 1); Box(Ink, ex + look + 1, 9, 1, 1); Box(Ink, ex + look + 2, 10, 1, 1);
                    }
                }
                else
                {
                    Box(Ink, 8 + look, closed ? 10 : eyeTop, 2, closed ? 1 : 3);
                    Box(Ink, 14 + look, closed ? 10 : eyeTop, 2, closed ? 1 : 3);
                }
                break;
            }
            case "cat":
            {
                var head = new Rect(cx - 10, cy - 6, 20, 15);
                dc.DrawGeometry(Fur, null, Triangle(new Point(cx - 10, cy - 3), new Point(cx - 8, cy - 11), new Point(cx - 2, cy - 6)));
                dc.DrawGeometry(Fur, null, Triangle(new Point(cx + 10, cy - 3), new Point(cx + 8, cy - 11), new Point(cx + 2, cy - 6)));
                dc.DrawRoundedRectangle(Fur, null, head, 7, 7);
                Eyes(dc, cx, cy + 0.5, 4.6, 2.6, 3.4, blink, happy, White, Ink, Smile, pupil: 1.5);
                dc.DrawEllipse(Blush, null, new Point(cx, cy + 4.6), 1.2, 0.9);
                break;
            }
            case "ghost":
            {
                double floaty = Math.Sin(now * 2.2) * 1.2;
                cy += floaty;
                var body = new StreamGeometry();
                using (var g = body.Open())
                {
                    g.BeginFigure(new Point(cx - 8, cy + 8), true, true);
                    g.LineTo(new Point(cx - 8, cy - 2), true, false);
                    g.ArcTo(new Point(cx + 8, cy - 2), new Size(8, 8), 0, false, SweepDirection.Clockwise, true, false);
                    g.LineTo(new Point(cx + 8, cy + 8), true, false);
                    g.LineTo(new Point(cx + 5.3, cy + 5.5), true, false);
                    g.LineTo(new Point(cx + 2.7, cy + 8), true, false);
                    g.LineTo(new Point(cx, cy + 5.5), true, false);
                    g.LineTo(new Point(cx - 2.7, cy + 8), true, false);
                    g.LineTo(new Point(cx - 5.3, cy + 5.5), true, false);
                }
                body.Freeze();
                dc.DrawGeometry(White, null, body);
                Eyes(dc, cx, cy - 1.5, 3.4, 1.9, 2.4, blink, happy, Ink, null, InkSmile, pupil: 0);
                break;
            }
            default:
                Eyes(dc, cx, cy, 5.2, 3.6, 4.6, blink, happy, White, Ink, Smile, pupil: 1.9);
                break;
        }
    }

    /// <summary>一對眼睛：眼白、會跟著看的瞳孔；眨眼時壓扁，開心時變成兩道彎。</summary>
    private void Eyes(DrawingContext dc, double cx, double cy, double apart, double rx, double ry, double blink, bool happy, Brush white, Brush? ink, Pen smile, double pupil)
    {
        foreach (int side in (int[])[-1, 1])
        {
            var c = new Point(cx + side * apart, cy);
            if (happy)
            {
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(c.X - rx, c.Y + 1), false, false);
                    g.QuadraticBezierTo(new Point(c.X, c.Y - ry), new Point(c.X + rx, c.Y + 1), true, true);
                }
                arc.Freeze();
                dc.DrawGeometry(null, smile, arc);
                continue;
            }
            dc.DrawEllipse(white, null, c, rx, Math.Max(0.5, ry * blink));
            if (ink != null && pupil > 0 && blink > 0.5)
                dc.DrawEllipse(ink, null, new Point(c.X + _gazeX * (rx - pupil), c.Y + _gazeY * (ry - pupil)), pupil, pupil);
        }
    }

    private static Geometry Triangle(Point a, Point b, Point c)
    {
        var t = new StreamGeometry();
        using (var g = t.Open()) { g.BeginFigure(a, true, true); g.LineTo(b, true, false); g.LineTo(c, true, false); }
        t.Freeze();
        return t;
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FrozenPen(Brush b, double width)
    {
        var p = new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
}

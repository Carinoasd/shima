// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shima.Lyrics;

namespace Shima;

/// <summary>桌面歌詞此刻要顯示的內容。</summary>
/// <param name="Index">正在唱第幾句；顯示歌名時是 −1。</param>
/// <param name="Sweep">要不要有光跟著唱的進度掃過去（只有真的歌詞才有）。</param>
public readonly record struct DesktopLine(int Index, string Main, string Sub, bool Sweep, TimeSpan Start, TimeSpan Length);

/// <summary>桌面歌詞的排版與位置計算，全部是純函式。</summary>
public static class DesktopLyricsLayout
{
    public const double MinSize = 16, MaxSize = 72, DefaultSize = 30;
    /// <summary>第二行的字是第一行的幾倍大。</summary>
    public const double SubScale = 0.58;

    public static double ClampSize(double size) => double.IsFinite(size) ? Math.Clamp(Math.Round(size), MinSize, MaxSize) : DefaultSize;

    /// <summary>滾輪一格調兩級。</summary>
    public static double Wheel(double size, int delta) => ClampSize(ClampSize(size) + Math.Sign(delta) * 2);

    public static double Pad(double size) => Math.Round(size * 0.35);
    public static double MainRow(double size) => Math.Ceiling(size * 1.45);
    public static double SubRow(double size) => Math.Ceiling(size * SubScale * 1.45);

    /// <summary>視窗大小：高度剛好放兩行，寬度跟著字級但不超過螢幕。</summary>
    public static Size WindowSize(double size, double screenWidth)
    {
        size = ClampSize(size);
        double width = Math.Round(size * 26);
        if (screenWidth > 0) width = Math.Min(width, Math.Floor(screenWidth));
        return new Size(width, Pad(size) * 2 + MainRow(size) + SubRow(size));
    }

    /// <summary>預設位置：主螢幕工作區的下方中央，離工作列一點距離。</summary>
    public static Point DefaultSpot(Rect work, Size window) =>
        new(Math.Round(work.Left + (work.Width - window.Width) / 2), Math.Round(work.Bottom - window.Height - Math.Max(24, work.Height * 0.06)));

    /// <summary>整個視窗夾在螢幕範圍內；比螢幕還大時靠左上。</summary>
    public static Point Clamp(Point topLeft, Size window, Rect screen) =>
        new(Math.Clamp(topLeft.X, screen.Left, Math.Max(screen.Left, screen.Right - window.Width)),
            Math.Clamp(topLeft.Y, screen.Top, Math.Max(screen.Top, screen.Bottom - window.Height)));

    /// <summary>存下來的位置還在螢幕上嗎（中心點在範圍內）；拔掉一個螢幕之後就不是了。</summary>
    public static bool OnScreen(Point topLeft, Size window, Rect screen) =>
        screen.Contains(new Point(topLeft.X + window.Width / 2, topLeft.Y + window.Height / 2));

    /// <summary>
    /// 現在要顯示什麼：有時間軸的歌詞就是正在唱的那句，第二行是翻譯（沒有就是下一句）；
    /// 沒有歌詞、前奏、間奏的空白行顯示歌名與藝人。
    /// </summary>
    public static DesktopLine Pick(LyricSheet? sheet, IReadOnlyList<string>? second, TimeSpan position, string title, string artist)
    {
        if (sheet is { Synced: true } s && s.Lines.Count > 0)
        {
            int i = Lrc.LineAt(s.Lines, position);
            if (i >= 0 && s.Lines[i].Text.Length > 0)
            {
                var line = s.Lines[i];
                TimeSpan? next = i + 1 < s.Lines.Count ? s.Lines[i + 1].Time : null;
                string sub = second != null && i < second.Count && second[i].Length > 0 ? second[i] : i + 1 < s.Lines.Count ? s.Lines[i + 1].Text : "";
                return new DesktopLine(i, line.Text, sub, true, line.Time, Singing.Length(line.Text, line.Time, next));
            }
        }
        return title.Length > 0 ? new DesktopLine(-1, title, artist, false, TimeSpan.Zero, TimeSpan.Zero)
                                : new DesktopLine(-1, artist, "", false, TimeSpan.Zero, TimeSpan.Zero);
    }
}

/// <summary>
/// 浮在桌面上的一條歌詞，給島因為全螢幕藏起來、或想把歌詞放在別處的人用。
/// 鎖定時滑鼠完全穿過去；解鎖時有一圈淡淡的框，可以拖、滾輪調字的大小。
/// 只有看得到而且在播放時才逐幀重畫。開關用 Window 本身的 Show()／Hide()／Close()。
/// </summary>
public sealed class DesktopLyrics : Window
{
    private readonly Stage _stage;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private IntPtr _hwnd;
    private bool _locked = true, _frames, _playing = true, _hasTrack;
    private int _polls;
    private double _size = DesktopLyricsLayout.DefaultSize;
    private Point? _spot;
    private LyricSheet? _sheet;
    private IReadOnlyList<string>? _second;
    private string _title = "", _artist = "";
    private Func<(TimeSpan Position, bool Playing)> _clock = () => (TimeSpan.Zero, false);

    /// <summary>使用者拖了位置或滾輪改了字級（停手 0.4 秒後才通知一次，可以直接存設定）。</summary>
    public event Action? PlacementChanged;

    /// <summary>歌現在唱到哪、是不是正在播；跟膠囊歌詞用同一個（已經含提早量與校正）。</summary>
    public Func<(TimeSpan Position, bool Playing)> Clock
    {
        get => _clock;
        set { _clock = value ?? (() => (TimeSpan.Zero, false)); Wake(); }
    }

    /// <summary>光的顏色（跟著封面）。</summary>
    public Color Light
    {
        get => _stage.Light;
        set { _stage.Light = value; _stage.InvalidateVisual(); }
    }

    /// <summary>鎖定（預設）：滑鼠穿透；解鎖：可以拖、滾輪調大小。</summary>
    public bool Locked
    {
        get => _locked;
        set
        {
            if (_locked == value) return;
            _locked = value;
            if (value) EndDrag();
            ApplyStyle();
            _stage.InvalidateVisual();
        }
    }

    /// <summary>第一行的字級（裝置無關像素），16…72。</summary>
    public double LyricSize
    {
        get => _size;
        set
        {
            double size = DesktopLyricsLayout.ClampSize(value);
            if (size == _size) return;
            _size = size;
            Resize();
            Place();
        }
    }

    /// <summary>視窗左上角（裝置無關像素）。null 表示沒被拖過，放在主螢幕下方中央；存的位置已經不在任何螢幕上也回到預設。</summary>
    public Point? TopLeft
    {
        get => _spot;
        set { _spot = value; Place(); }
    }

    public DesktopLyrics()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Title = "Shima";
        FontFamily = Loc.Font;
        Content = _stage = new Stage(this) { Visibility = Visibility.Hidden };
        Resize();
        Place();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyStyle();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { _poll.Start(); Overlay.Raise(_hwnd); } else _poll.Stop();
            Wake();
        };
        _poll.Tick += (_, _) =>
        {
            Wake();
            // 每秒排回最上層一次，無邊框全螢幕的遊戲才蓋不掉
            if (++_polls % 4 == 0 && _hasTrack) Overlay.Raise(_hwnd);
        };
        _settle.Tick += (_, _) => { _settle.Stop(); PlacementChanged?.Invoke(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _poll.Stop();
            StopFrames();
        };
    }

    /// <param name="second">每一句的第二行（翻譯或羅馬拼音），沒有就傳 null，第二行改顯示下一句。</param>
    public void Show(LyricSheet? sheet, IReadOnlyList<string>? second)
    {
        if (ReferenceEquals(sheet, _sheet) && ReferenceEquals(second, _second)) return;
        _sheet = sheet;
        _second = second;
        Wake();
    }

    /// <summary>現在這首歌。歌名與藝人都是空的表示沒有歌，整個藏起來。</summary>
    public void SetTrack(string title, string artist)
    {
        _title = title ?? "";
        _artist = artist ?? "";
        bool has = _title.Length > 0 || _artist.Length > 0;
        if (has != _hasTrack)
        {
            _hasTrack = has;
            _stage.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            ApplyStyle();
        }
        Wake();
    }

    /// <summary>回到主螢幕下方中央。</summary>
    public void ResetPlace()
    {
        _spot = null;
        Place();
        PlacementChanged?.Invoke();
    }

    /// <summary>播放狀態或位置可能變了：更新內容，需要的話開始或停止逐幀重畫。自己每 0.25 秒也會檢查一次。</summary>
    public void Wake()
    {
        if (!IsVisible || !_hasTrack) { StopFrames(); return; }
        var (position, playing) = _clock();
        if (playing != _playing)
        {
            _playing = playing;
            // 暫停時變淡
            _stage.BeginAnimation(OpacityProperty, new DoubleAnimation(playing ? 1 : 0.4, TimeSpan.FromMilliseconds(300)));
        }
        _stage.Set(DesktopLyricsLayout.Pick(_sheet, _second, position, _title, _artist));
        if (playing || _stage.Animating) StartFrames(); else StopFrames();
    }

    private void StartFrames()
    {
        if (_frames) return;
        _frames = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopFrames()
    {
        if (!_frames) return;
        _frames = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        _stage.InvalidateVisual();
        Wake();
    }

    private void ApplyStyle() => Overlay.Style(_hwnd, passThrough: _locked || !_hasTrack);

    private void Resize()
    {
        var size = DesktopLyricsLayout.WindowSize(_size, SystemParameters.VirtualScreenWidth);
        Width = size.Width;
        Height = size.Height;
        _stage.Restyle();
    }

    private void Place()
    {
        var window = new Size(Width, Height);
        var screen = Overlay.VirtualScreen;
        if (_spot is { } spot && !DesktopLyricsLayout.OnScreen(spot, window, screen)) _spot = null;
        var at = _spot is { } s ? DesktopLyricsLayout.Clamp(s, window, screen) : DesktopLyricsLayout.DefaultSpot(SystemParameters.WorkArea, window);
        Left = at.X;
        Top = at.Y;
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { Resize(); Place(); });

    // ---------------------------------------------------------------- 解鎖時：拖、滾輪

    private Point? _dragFrom;
    private bool _dragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_locked) return;
        _dragFrom = PointToScreen(e.GetPosition(this));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragFrom is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
        var now = PointToScreen(e.GetPosition(this));
        if (!_dragging && (now - from).Length < 4) return;
        _dragging = true;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left += (now.X - from.X) / dpi.DpiScaleX;
        Top += (now.Y - from.Y) / dpi.DpiScaleY;
        _dragFrom = now;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool moved = _dragging;
        EndDrag();
        if (!moved) return;
        var at = DesktopLyricsLayout.Clamp(new Point(Left, Top), new Size(Width, Height), Overlay.VirtualScreen);
        Left = at.X;
        Top = at.Y;
        _spot = at;
        Changed();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_locked || e.Delta == 0) return;
        e.Handled = true;
        double size = DesktopLyricsLayout.Wheel(_size, e.Delta);
        if (size == _size) return;
        // 變大變小時中心不動、上緣不動
        double centre = Left + Width / 2;
        _size = size;
        Resize();
        var at = DesktopLyricsLayout.Clamp(new Point(Math.Round(centre - Width / 2), Top), new Size(Width, Height), Overlay.VirtualScreen);
        Left = at.X;
        Top = at.Y;
        _spot = at;
        Changed();
    }

    private void EndDrag()
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        _dragFrom = null;
        _dragging = false;
    }

    private void Changed()
    {
        _settle.Stop();
        _settle.Start();
    }

    // ---------------------------------------------------------------- 畫

    /// <summary>兩行字：上面大字有掃光，下面小字淡一點。字先描一圈深色的邊再填色，在任何桌布上都看得清楚。</summary>
    private sealed class Stage(DesktopLyrics owner) : FrameworkElement
    {
        private const double Leave = 0.24, Arrive = 0.4;
        private static readonly Brush Frame = Frozen(Color.FromArgb(0x38, 0, 0, 0));
        private static readonly Pen FrameEdge = FrozenPen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), 1);
        private static readonly Brush SubFill = Frozen(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF));
        private static readonly Color Unsung = Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF);
        private static readonly Brush UnsungFill = Frozen(Unsung);

        private sealed record Shape(Geometry Geometry, double Width, string Text);

        private DesktopLine _now = new(int.MinValue, "", "", false, TimeSpan.Zero, TimeSpan.Zero);
        private Shape? _main, _sub, _oldMain, _oldSub;
        private string _oldMainText = "", _oldSubText = "";
        private double[]? _prefixes;
        private long _changedAt = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        private Pen? _halo, _edge, _subHalo, _subEdge;

        public Color Light { get; set; } = Colors.White;

        private double SinceChange => Stopwatch.GetElapsedTime(_changedAt).TotalSeconds;
        public bool Animating => SinceChange < Arrive;

        public void Set(DesktopLine line)
        {
            bool mainChanged = line.Index != _now.Index || line.Main != _now.Main;
            bool subChanged = line.Sub != _now.Sub;
            if (!mainChanged && !subChanged) { _now = line; return; }
            if (mainChanged)
            {
                // 舊的往上淡出、新的從下面滑上來
                _oldMain = _main; _oldSub = _sub;
                _oldMainText = _now.Main; _oldSubText = _now.Sub;
                _main = null;
                _prefixes = null;
                _changedAt = Stopwatch.GetTimestamp();
            }
            _sub = null;
            _now = line;
            InvalidateVisual();
        }

        /// <summary>字級或 DPI 變了：重新排字。</summary>
        public void Restyle()
        {
            _main = _sub = _oldMain = _oldSub = null;
            _prefixes = null;
            _halo = _edge = _subHalo = _subEdge = null;
            InvalidateVisual();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Restyle();
        }

        private Typeface Face(string text, bool main) =>
            new(Fonts.For(Fonts.HasKana(text)), FontStyles.Normal, main ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);

        private Shape? Build(string text, double size, bool main)
        {
            if (text.Length == 0) return null;
            double room = Math.Max(1, ActualWidth - DesktopLyricsLayout.Pad(owner._size) * 2);
            var formatted = new FormattedText(text, Fonts.Culture(Fonts.HasKana(text)), FlowDirection.LeftToRight, Face(text, main), size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, MaxTextWidth = room };
            var geometry = formatted.BuildGeometry(new Point(0, (size * 1.45 - formatted.Height) / 2));
            geometry.Freeze();
            return new Shape(geometry, Math.Min(room, formatted.WidthIncludingTrailingWhitespace), text);
        }

        private static Pen Outline(double width, byte alpha)
        {
            var pen = new Pen(Frozen(Color.FromArgb(alpha, 0, 0, 0)), width) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, size = owner._size, sub = size * DesktopLyricsLayout.SubScale;
            if (w <= 0 || h <= 0) return;
            if (!owner._locked)
                dc.DrawRoundedRectangle(Frame, FrameEdge, new Rect(0.5, 0.5, w - 1, h - 1), size * 0.4, size * 0.4);

            double pad = DesktopLyricsLayout.Pad(size), mainTop = pad, subTop = pad + DesktopLyricsLayout.MainRow(size);
            _halo ??= Outline(Math.Max(3, size * 0.3), 0x48);
            _edge ??= Outline(Math.Max(1.5, size * 0.14), 0xD8);
            _subHalo ??= Outline(Math.Max(2.5, sub * 0.3), 0x40);
            _subEdge ??= Outline(Math.Max(1.2, sub * 0.14), 0xC8);

            double t = SinceChange;
            if (t < Leave)
            {
                double k = t / Leave;
                if (_oldMainText.Length > 0 && (_oldMain ??= Build(_oldMainText, size, true)) is { } om)
                    Draw(dc, om, (w - om.Width) / 2, mainTop - size * 0.5 * k * k, 1 - k, _halo, _edge, UnsungFill);
                if (_oldSubText.Length > 0 && (_oldSub ??= Build(_oldSubText, sub, false)) is { } os)
                    Draw(dc, os, (w - os.Width) / 2, subTop - sub * 0.4 * k * k, (1 - k) * 0.85, _subHalo, _subEdge, SubFill);
            }

            double arrive = Math.Clamp(t / Arrive, 0, 1), ease = 1 - Math.Pow(1 - arrive, 3);
            if ((_main ??= Build(_now.Main, size, true)) is { } main)
            {
                double x = (w - main.Width) / 2;
                Brush fill = Brushes.White;
                if (_now.Sweep) fill = Sweep(main, size);
                Draw(dc, main, x, mainTop + size * 0.45 * (1 - ease), Math.Clamp(t / 0.18, 0, 1), _halo, _edge, fill);
            }
            if ((_sub ??= Build(_now.Sub, sub, false)) is { } second)
                Draw(dc, second, (w - second.Width) / 2, subTop + sub * 0.35 * (1 - ease), Math.Clamp((t - 0.06) / 0.25, 0, 1) * 0.85, _subHalo, _subEdge, SubFill);
        }

        private Brush Sweep(Shape main, double size)
        {
            var (position, _) = owner._clock();
            double progress = _now.Length <= TimeSpan.Zero ? 1 : Math.Clamp((position - _now.Start) / _now.Length, 0, 1);
            bool japanese = Fonts.HasKana(main.Text);
            _prefixes ??= Fonts.Prefixes(main.Text, Face(main.Text, true), size, Fonts.Culture(japanese), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double p = Singing.WidthShare(main.Text, progress, _prefixes), edge = Math.Min(0.12, size * 0.5 / Math.Max(1, main.Width)), head = p * (1 + edge);
            // 唱過的是主色，前緣一小段漸層，還沒唱的是半透明的白
            var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(main.Width, 0) };
            brush.GradientStops.Add(new GradientStop(Light, 0));
            brush.GradientStops.Add(new GradientStop(Light, Math.Clamp(head - edge, 0, 1)));
            brush.GradientStops.Add(new GradientStop(Unsung, Math.Clamp(head, 0, 1)));
            brush.GradientStops.Add(new GradientStop(Unsung, 1));
            brush.Freeze();
            return brush;
        }

        private static void Draw(DrawingContext dc, Shape shape, double x, double y, double opacity, Pen halo, Pen edge, Brush fill)
        {
            if (opacity <= 0.01) return;
            dc.PushTransform(new TranslateTransform(Math.Round(Math.Max(0, x)), y));
            dc.PushOpacity(opacity);
            dc.DrawGeometry(null, halo, shape.Geometry);
            dc.DrawGeometry(null, edge, shape.Geometry);
            dc.DrawGeometry(fill, null, shape.Geometry);
            dc.Pop();
            dc.Pop();
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double width) { var p = new Pen(Frozen(c), width); p.Freeze(); return p; }
    }
}

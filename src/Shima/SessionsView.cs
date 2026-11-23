// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Agents;

namespace Shima;

/// <summary>
/// Claude Code 的工作階段，一個一列：狀態的小圓點、專案名稱、待辦進度（3/5 · 正在做的那一項）。
/// 別的 agent（Codex、Gemini CLI）的工作階段在名稱前面多一個小標籤。點一列觸發 Picked。沒有工作階段時高度是 0。
/// </summary>
public sealed class SessionsView : FrameworkElement
{
    private const double Row = 30, Pad = 6, Dot = 3.5, Radius = 10;
    private static readonly Brush Back = Frozen(Color.FromArgb(0x99, 0x14, 0x14, 0x16));
    private static readonly Brush Hover = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));   // 次要文字 60%
    private static readonly Brush TagBack = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    private ImmutableArray<AgentSession> _sessions = [];
    private int _hover = -1;

    /// <summary>使用者點了哪一個工作階段。</summary>
    public event Action<AgentSession>? Picked;

    public ImmutableArray<AgentSession> Sessions
    {
        get => _sessions;
        set
        {
            _sessions = value.IsDefault ? [] : value;
            _hover = -1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public SessionsView()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    /// <summary>狀態的顏色：工作中藍、等你允許琥珀、等你輸入陶土色、做完了綠。</summary>
    public static Color ColorOf(AgentState state) => state switch
    {
        AgentState.NeedsPermission => Color.FromRgb(0xFF, 0xB8, 0x4D),
        AgentState.NeedsInput => Color.FromRgb(0xD9, 0x77, 0x57),
        AgentState.Done => Color.FromRgb(0x7C, 0xD3, 0x8A),
        _ => Color.FromRgb(0x5A, 0xB4, 0xFF),
    };

    /// <summary>右邊那一段：「3/5 · 正在寫測試」；沒有待辦清單就是空的。</summary>
    public static string Progress(AgentSession s) =>
        s.Total == 0 ? "" : $"{s.Done}/{s.Total}{(s.Current.Length > 0 ? " · " + s.Current : "")}";

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 280 : available.Width, _sessions.Length == 0 ? 0 : _sessions.Length * Row + Pad * 2);

    protected override void OnRender(DrawingContext dc)
    {
        if (_sessions.Length == 0) return;
        double w = ActualWidth;
        dc.DrawRoundedRectangle(Back, null, new Rect(0, 0, w, ActualHeight), Radius, Radius);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int i = 0; i < _sessions.Length; i++)
        {
            var s = _sessions[i];
            double y = Pad + i * Row;
            if (i == _hover) dc.DrawRoundedRectangle(Hover, null, new Rect(Pad / 2, y, w - Pad, Row), 7, 7);

            double x = 14;
            dc.DrawEllipse(Frozen(ColorOf(s.State)), null, new Point(x, y + Row / 2), Dot, Dot);
            x += Dot + 10;

            if (AgentKinds.Tag(s.Agent) is { } tag)
            {
                var label = Text(tag, 10.5, Dim, FontWeights.SemiBold, dpi);
                double tw = label.WidthIncludingTrailingWhitespace + 12, th = 17;
                dc.DrawRoundedRectangle(TagBack, null, new Rect(x, y + (Row - th) / 2, tw, th), th / 2, th / 2);
                dc.DrawText(label, new Point(x + 6, y + (Row - label.Height) / 2));
                x += tw + 8;
            }

            var name = Text(s.Project.Length > 0 ? s.Project : AgentKinds.Name(s.Agent), 13, Brushes.White, FontWeights.SemiBold, dpi);
            name.MaxTextWidth = Math.Max(1, (w - x - 12) * 0.5);
            dc.DrawText(name, new Point(x, y + (Row - name.Height) / 2));
            x += name.WidthIncludingTrailingWhitespace + 10;

            var progress = Progress(s);
            if (progress.Length == 0 || w - x - 12 < 16) continue;
            var line = Text(progress, 12, Dim, FontWeights.Normal, dpi);
            line.MaxTextWidth = w - x - 12;
            dc.DrawText(line, new Point(x, y + (Row - line.Height) / 2));
        }
    }

    private static FormattedText Text(string text, double size, Brush brush, FontWeight weight, double dpi)
    {
        bool japanese = Fonts.HasKana(text);
        var typeface = new Typeface(Fonts.For(japanese), FontStyles.Normal, weight, FontStretches.Normal);
        return new FormattedText(text, Fonts.Culture(japanese), FlowDirection.LeftToRight, typeface, size, brush, dpi)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }

    private int RowAt(Point p)
    {
        int i = (int)Math.Floor((p.Y - Pad) / Row);
        return p.Y < Pad || i < 0 || i >= _sessions.Length ? -1 : i;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = RowAt(e.GetPosition(this));
        if (i != _hover) { _hover = i; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover >= 0) { _hover = -1; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        int i = RowAt(e.GetPosition(this));
        if (i < 0) return;
        e.Handled = true;
        Picked?.Invoke(_sessions[i]);
    }

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}

/// <summary>
/// 盡力把某個工作階段的終端機視窗帶到前面：看視窗標題有沒有專案名稱或資料夾路徑。
/// 終端機（尤其 Windows Terminal 的分頁）不一定把這些放在標題上，找不到就算了。
/// </summary>
public static class TerminalFocus
{
    /// <summary>會跑 Claude Code 的程式（行程名稱，不含 .exe）。</summary>
    public static readonly HashSet<string> Terminals = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "cmd", "powershell", "pwsh", "conhost", "OpenConsole", "wezterm-gui", "alacritty", "mintty",
        "Hyper", "Tabby", "kitty", "Code", "Code - Insiders", "Cursor", "Windsurf", "idea64", "rider64", "devenv", "wsl", "ubuntu",
    };

    public sealed record Window(IntPtr Handle, string Title, string Process);

    /// <summary>
    /// 從候選視窗挑一個：標題有完整的資料夾路徑最好，其次是專案名稱（整個字，不分大小寫）。
    /// 都沒有、但只開著一個終端機時就用那一個。純函式。
    /// </summary>
    public static Window? Pick(IEnumerable<Window> windows, string project, string cwd)
    {
        var terminals = windows.Where(w => Terminals.Contains(w.Process)).ToList();
        if (cwd.Length > 0 && terminals.FirstOrDefault(w => w.Title.Contains(AgentRules.Folder(cwd), StringComparison.OrdinalIgnoreCase)) is { } byPath) return byPath;
        if (project.Length > 0 && terminals.FirstOrDefault(w => HasWord(w.Title, project)) is { } byName) return byName;
        return terminals.Count == 1 ? terminals[0] : null;
    }

    private static bool HasWord(string title, string word)
    {
        for (int i = title.IndexOf(word, StringComparison.OrdinalIgnoreCase); i >= 0; i = title.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            bool left = i == 0 || !char.IsLetterOrDigit(title[i - 1]);
            int end = i + word.Length;
            bool right = end >= title.Length || !char.IsLetterOrDigit(title[end]);
            if (left && right) return true;
        }
        return false;
    }

    /// <summary>找到就帶到前面並回傳 true；找不到回傳 false。要在使用者剛點過島的時候呼叫（Windows 才肯換前景）。</summary>
    public static bool Bring(AgentSession session)
    {
        try
        {
            if (Pick(TopWindows(), session.Project, session.Cwd) is not { } target) return false;
            if (IsIconic(target.Handle)) ShowWindow(target.Handle, 9);   // SW_RESTORE
            return SetForegroundWindow(target.Handle);
        }
        catch (Exception e) { Log.Write("terminal focus: " + e.Message); return false; }
    }

    private static List<Window> TopWindows()
    {
        var list = new List<Window>();
        var names = new Dictionary<uint, string>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            int length = GetWindowTextLength(h);
            if (length == 0) return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(h, title, title.Capacity);
            GetWindowThreadProcessId(h, out uint pid);
            if (!names.TryGetValue(pid, out var name))
            {
                try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
                catch { name = ""; }
                names[pid] = name;
            }
            list.Add(new Window(h, title.ToString(), name));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private delegate bool EnumProc(IntPtr h, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
}

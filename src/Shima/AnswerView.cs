// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shima.Agents;

namespace Shima;

/// <summary>
/// AI 的回答：簡單的 Markdown 排版，可以選取複製。高度跟著內容，超過 MaxHeight 就用滾輪捲，
/// 上下還有內容的那一邊淡出。串流中自動跟到最新的字；使用者往上捲了就不拉他回去，捲回底部再繼續跟。
/// </summary>
public sealed class AnswerView : Grid
{
    private const double Fade = 28, Indent = 18;
    private static readonly Brush Body = Frozen(0xD0), Strong = Brushes.White, CodeBack = Frozen(0x1C), BlockBack = Frozen(0x12), Bar = Frozen(0x40);
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    private readonly RichTextBox _box;
    private readonly FlowDocument _doc;
    private readonly Button _copy;
    private readonly TextBlock _glyph;
    private readonly DispatcherTimer _copied = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private IReadOnlyList<MdBlock> _blocks = [];
    private string _text = "";
    private bool? _japanese;
    // 跟著最新的字；_top：剛換成一段新的內容，排好版時停在開頭（整段一次來的回答要從頭讀）
    private bool _follow = true, _top;

    public AnswerView()
    {
        _doc = new FlowDocument
        {
            PagePadding = new Thickness(0), FontSize = 13, Foreground = Body, TextAlignment = TextAlignment.Left,
            LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        _box = new RichTextBox
        {
            Document = _doc, IsReadOnly = true, IsUndoEnabled = false, IsDocumentEnabled = false,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Foreground = Body, CaretBrush = Brushes.Transparent, SelectionBrush = Frozen(0x60), SelectionOpacity = 1,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FocusVisualStyle = null, MaxHeight = 220, Cursor = Cursors.IBeam,
        };
        // 內建的右鍵選單（剪下／複製／貼上）沒有翻譯；Ctrl+C 照樣能用
        _box.ContextMenuOpening += (_, e) => e.Handled = true;
        _box.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScroll));
        _box.SizeChanged += (_, _) => Shade();

        _glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, Foreground = Strong, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _copy = new Button
        {
            Content = _glyph, Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -3, -4, 0), Focusable = false, Cursor = Cursors.Hand, ToolTip = L.T("複製"), Visibility = Visibility.Collapsed,
            Template = CopyTemplate(),
        };
        _copy.Click += (_, _) => Copy();
        _copied.Tick += (_, _) => { _copied.Stop(); _glyph.Text = ""; _copy.ToolTip = L.T("複製"); };

        Children.Add(_box);
        Children.Add(_copy);
        MouseEnter += (_, _) => ShowCopy(true);
        MouseLeave += (_, _) => ShowCopy(false);
    }

    /// <summary>原始的文字（含 Markdown 標記）。設定時只重畫有變的區塊。</summary>
    public string Text
    {
        get => _text;
        set => Show(value ?? "");
    }

    /// <summary>滑鼠在回答上時顯示複製按鈕。</summary>
    public void ShowCopy(bool on) =>
        _copy.Visibility = on && _text.Trim().Length > 1 ? Visibility.Visible : Visibility.Collapsed;

    private void Show(string text)
    {
        if (text == _text) return;
        // 接著原本的字長下去就是串流中；不是的話是一段新的內容（新的提問、聽到的話），回到跟隨。
        // 最後整段的答案會去掉頭尾的空白，所以比對時也不看頭尾空白
        string was = _text.Trim(), now = text.Trim();
        bool fresh = was.Length == 0 || now.Length == 0 || !(now.StartsWith(was, StringComparison.Ordinal) || was.StartsWith(now, StringComparison.Ordinal));
        _text = text;

        // 串流可能停在半個表情符號上
        var shown = text.Length > 0 && char.IsHighSurrogate(text[^1]) ? text[..^1] : text;
        bool japanese = Fonts.HasKana(shown);
        if (japanese != _japanese) { _japanese = japanese; _doc.FontFamily = japanese ? Fonts.Japanese : Loc.Font; }

        var blocks = Markdown.Parse(shown);
        int same = 0;
        while (same < blocks.Count && same < _blocks.Count && blocks[same].Equals(_blocks[same])) same++;
        // 串流時通常只有最後一塊在變：前面的不動
        while (_doc.Blocks.Count > same) _doc.Blocks.Remove(_doc.Blocks.LastBlock);
        for (int i = same; i < blocks.Count; i++) _doc.Blocks.Add(Build(blocks[i], i > 0 ? blocks[i - 1] : null));
        _blocks = blocks;

        if (fresh)
        {
            _follow = true;
            _top = true;
            _box.ScrollToHome();
        }
        else _top = false;
        if (IsMouseOver) ShowCopy(true);
    }

    private void OnScroll(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            // 內容長高了：在跟隨就捲到最新的字
            if (_top) { _top = false; _box.ScrollToHome(); }
            else if (_follow) _box.ScrollToEnd();
        }
        // 使用者自己捲的：捲回底部才恢復跟隨
        else _follow = !IslandWindow.MoreBelow(e.VerticalOffset, e.ExtentHeight - e.ViewportHeight);
        Shade();
    }

    /// <summary>上下還有內容的那一邊淡出；固定 28 的高度，不會跟著內容長短變。</summary>
    private void Shade()
    {
        double h = _box.ViewportHeight, offset = _box.VerticalOffset;
        bool up = offset > 0.5, down = IslandWindow.MoreBelow(offset, _box.ExtentHeight - h);
        if (h <= Fade * 2 || !up && !down) { _box.OpacityMask = null; return; }
        var fade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, h) };
        fade.GradientStops.Add(new GradientStop(up ? Colors.Transparent : Colors.Black, 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, up ? Fade / h : 0));
        fade.GradientStops.Add(new GradientStop(Colors.Black, down ? 1 - Fade / h : 1));
        fade.GradientStops.Add(new GradientStop(down ? Colors.Transparent : Colors.Black, 1));
        fade.Freeze();
        _box.OpacityMask = fade;
    }

    private void Copy()
    {
        try { Clipboard.SetText(_text); }
        catch (Exception x) { Log.Write("copy: " + x.Message); return; }   // 剪貼簿被別的程式佔著
        _glyph.Text = "";
        _copy.ToolTip = L.T("已複製");
        _copied.Stop();
        _copied.Start();
    }

    // ---------------------------------------------------------------- 排版

    private static Block Build(MdBlock block, MdBlock? previous)
    {
        bool listy(MdBlock? b) => b?.Kind is MdKind.Bullet or MdKind.Numbered;
        // 第一塊貼齊上緣；清單項目之間比段落之間緊
        double top = previous == null ? 0 : block.Kind == MdKind.Heading ? 12 : listy(block) && listy(previous) ? 3 : 8;
        double left = block.Depth * Indent;

        switch (block.Kind)
        {
            case MdKind.Bullet:
            case MdKind.Numbered:
            {
                bool numbered = block.Kind == MdKind.Numbered;
                var item = new Paragraph { Margin = new Thickness(0) };
                Fill(item.Inlines, block.Spans);
                var list = new List(new ListItem(item))
                {
                    Margin = new Thickness(left, top, 0, 0), Padding = new Thickness(numbered ? Indent + 4 : Indent, 0, 0, 0), MarkerOffset = 6,
                    MarkerStyle = numbered ? TextMarkerStyle.Decimal : (block.Depth % 3) switch { 0 => TextMarkerStyle.Disc, 1 => TextMarkerStyle.Circle, _ => TextMarkerStyle.Square },
                };
                if (numbered && int.TryParse(block.Marker.TrimEnd('.', ')'), out var start) && start > 0) list.StartIndex = start;
                return list;
            }
            case MdKind.Code:
            {
                var code = new Paragraph
                {
                    Margin = new Thickness(left, top, 0, 0), Padding = new Thickness(10, 6, 10, 6), Background = BlockBack,
                    FontFamily = Mono, FontSize = 12, LineHeight = 18,
                };
                Lines(code.Inlines, block.Source, null);
                return code;
            }
            case MdKind.Heading:
            {
                var heading = new Paragraph
                {
                    Margin = new Thickness(left, top, 0, 0), FontWeight = FontWeights.SemiBold, Foreground = Strong,
                    FontSize = block.Level switch { 1 => 15.5, 2 => 14.5, _ => 13.5 }, LineHeight = block.Level == 1 ? 23 : 21,
                };
                Fill(heading.Inlines, block.Spans);
                return heading;
            }
            case MdKind.Quote:
            {
                var quote = new Paragraph { Margin = new Thickness(left, top, 0, 0), BorderBrush = Bar, BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0) };
                Fill(quote.Inlines, block.Spans);
                return quote;
            }
            default:
            {
                // 清單底下的段落對齊項目的文字
                var paragraph = new Paragraph { Margin = new Thickness(left, top, 0, 0) };
                Fill(paragraph.Inlines, block.Spans);
                return paragraph;
            }
        }
    }

    private static void Fill(InlineCollection inlines, IReadOnlyList<MdSpan> spans)
    {
        foreach (var span in spans) Lines(inlines, span.Text, span.Style);
    }

    /// <summary>一段文字照換行切開，中間放 LineBreak（Run 裡的 \n 在 FlowDocument 不一定會換行）。</summary>
    private static void Lines(InlineCollection inlines, string text, MdStyle? style)
    {
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            if (parts[i].Length == 0) continue;
            var run = new Run(parts[i]);
            if (style is { } st)
            {
                if (st.HasFlag(MdStyle.Bold)) { run.FontWeight = FontWeights.SemiBold; run.Foreground = Strong; }
                if (st.HasFlag(MdStyle.Italic)) run.FontStyle = FontStyles.Italic;
                if (st.HasFlag(MdStyle.Code)) { run.FontFamily = Mono; run.FontSize = 12; run.Background = CodeBack; }
            }
            inlines.Add(run);
        }
    }

    private static ControlTemplate CopyTemplate()
    {
        var back = new FrameworkElementFactory(typeof(Border), "Bg");
        back.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        back.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xF0, 0x2A, 0x2A, 0x2A)));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        back.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = back };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A)), "Bg"));
        template.Triggers.Add(hover);
        return template;
    }

    private static SolidColorBrush Frozen(byte alpha) { var b = new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)); b.Freeze(); return b; }
}

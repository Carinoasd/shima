// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.RegularExpressions;

namespace Shima.Agents;

public enum MdKind { Paragraph, Heading, Bullet, Numbered, Code, Quote }

[Flags]
public enum MdStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Link = 8 }

/// <summary>一段同樣樣式的文字。</summary>
public readonly record struct MdSpan(string Text, MdStyle Style);

/// <summary>
/// 一個區塊。Source 是去掉行首標記後的原文（程式碼區塊就是程式碼本身），兩個區塊相等只看種類、層級與 Source：
/// 行內的片段由 Source 決定，畫面拿這個比對就知道哪一塊要重畫。
/// </summary>
public sealed class MdBlock : IEquatable<MdBlock>
{
    public MdKind Kind { get; }
    /// <summary>標題的級數 1～3；其他是 0。</summary>
    public int Level { get; }
    /// <summary>縮排的層數：清單的巢狀、清單項目底下的段落與程式碼。</summary>
    public int Depth { get; }
    /// <summary>有序清單的編號（「1.」）。</summary>
    public string Marker { get; }
    public string Source { get; }
    public IReadOnlyList<MdSpan> Spans { get; }

    public MdBlock(MdKind kind, string source, int level = 0, int depth = 0, string marker = "")
    {
        Kind = kind;
        Source = source;
        Level = level;
        Depth = depth;
        Marker = marker;
        Spans = kind == MdKind.Code ? (source.Length > 0 ? [new MdSpan(source, MdStyle.Code)] : []) : Markdown.Inline(source);
    }

    /// <summary>畫面上看到的字（不含標記）。</summary>
    public string Plain => string.Concat(Spans.Select(s => s.Text));

    public bool Equals(MdBlock? other) =>
        other != null && Kind == other.Kind && Level == other.Level && Depth == other.Depth && Marker == other.Marker && Source == other.Source;
    public override bool Equals(object? obj) => Equals(obj as MdBlock);
    public override int GetHashCode() => HashCode.Combine(Kind, Level, Depth, Marker, Source);
    public override string ToString() => $"{Kind}{(Level > 0 ? Level : "")}/{Depth}{Marker}: {Source}";
}

/// <summary>
/// 模型回答裡常見的那一小部分 Markdown。回答是串流進來的，所以有兩個原則：
/// 一、區塊的邊界只看那一行自己的開頭（沒有 setext 標題、沒有「下一行是 --- 就把上一行變標題」），
///     後面再來多少字，前面已經結束的區塊都不會變；
/// 二、標記沒收尾就當普通文字，收尾了才套樣式，不會先把後面整段變粗體。
/// 中日文沒有空格分詞，<c>*</c>、<c>_</c> 的判斷刻意保守：<c>snake_case</c>、<c>2*3*4</c>、<c>*.txt</c> 都不算斜體。
/// 看不懂的語法（表格、圖片、HTML）原樣留著，不吃掉任何字。
/// </summary>
public static partial class Markdown
{
    [GeneratedRegex(@"^(#{1,6})[ \t]+(.*)$")] private static partial Regex HeadingLine();
    [GeneratedRegex(@"^([-*+])[ \t]+(.*)$")] private static partial Regex BulletLine();
    [GeneratedRegex(@"^(\d{1,9})([.)])[ \t]+(.*)$")] private static partial Regex NumberedLine();

    public static IReadOnlyList<MdBlock> Parse(string text)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // 正在組的區塊
        MdKind kind = MdKind.Paragraph;
        int level = 0, depth = 0;
        string marker = "";
        StringBuilder? body = null;
        // 程式碼區塊：開頭那排 ``` 的長度與縮排
        bool fenced = false, firstCode = false;
        int fence = 0, fenceIndent = 0;
        // 這一串清單每一層項目的縮排，用來決定巢狀的層數
        var indents = new List<int>();
        bool blank = false;

        void Flush()
        {
            if (body == null) return;
            blocks.Add(new MdBlock(kind, body.ToString(), level, depth, marker));
            body = null;
        }
        void Start(MdKind k, string first, int lv = 0, int dp = 0, string mk = "")
        {
            Flush();
            kind = k; level = lv; depth = dp; marker = mk;
            body = new StringBuilder(first);
        }
        int ListDepth(int indent)
        {
            while (indents.Count > 0 && indents[^1] > indent) indents.RemoveAt(indents.Count - 1);
            // 差一格的縮排多半是手誤，當同一層
            if (indents.Count == 0 || indent - indents[^1] >= 2) indents.Add(indent);
            return Math.Min(indents.Count - 1, 3);
        }
        // 清單項目底下、空一行再縮排的內容：屬於縮排比它小的最深那一項
        int Under(int indent) => indent >= 2 ? Math.Min(indents.Count(i => i < indent), 4) : 0;

        foreach (var raw in lines)
        {
            int indent = 0;
            while (indent < raw.Length && raw[indent] is ' ' or '\t') indent++;
            var rest = raw[indent..];

            if (fenced)
            {
                if (rest.TrimEnd() is var end && end.Length >= fence && end.All(c => c == '`'))
                {
                    fenced = false;
                    Flush();
                    continue;
                }
                // 第一行之後才接換行：程式碼前後不留空行
                if (!firstCode) body!.Append('\n');
                firstCode = false;
                body!.Append(raw[Math.Min(indent, fenceIndent)..]);
                continue;
            }

            if (rest.Trim().Length == 0)
            {
                Flush();
                blank = true;
                continue;
            }
            bool afterBlank = blank;
            blank = false;

            if (rest.StartsWith("```", StringComparison.Ordinal) && !rest.TrimStart('`').Contains('`'))
            {
                int dp = Under(indent);
                if (dp == 0) indents.Clear();
                Start(MdKind.Code, "", dp: dp);
                fenced = true;
                fence = rest.Length - rest.TrimStart('`').Length;
                fenceIndent = indent;
                firstCode = true;
                continue;
            }
            if (HeadingLine().Match(rest) is { Success: true } h)
            {
                indents.Clear();
                Start(MdKind.Heading, h.Groups[2].Value, lv: Math.Min(h.Groups[1].Length, 3));
                continue;
            }
            if (rest[0] == '>')
            {
                var quoted = rest.Length > 1 && rest[1] == ' ' ? rest[2..] : rest[1..];
                if (body != null && kind == MdKind.Quote && !afterBlank) body.Append('\n').Append(quoted);
                else { indents.Clear(); Start(MdKind.Quote, quoted); }
                continue;
            }
            if (BulletLine().Match(rest) is { Success: true } b)
            {
                Start(MdKind.Bullet, b.Groups[2].Value, dp: ListDepth(indent));
                continue;
            }
            if (NumberedLine().Match(rest) is { Success: true } n)
            {
                Start(MdKind.Numbered, n.Groups[3].Value, dp: ListDepth(indent), mk: n.Groups[1].Value + n.Groups[2].Value);
                continue;
            }

            // 普通的一行：接在上一塊後面（每個換行都保留，歌詞、中文不能被接成一行），空過一行就是新的段落
            if (body != null && kind is MdKind.Paragraph or MdKind.Bullet or MdKind.Numbered && !afterBlank)
            {
                body.Append('\n').Append(rest);
                continue;
            }
            int under = afterBlank ? Under(indent) : 0;
            if (under == 0) indents.Clear();
            Start(MdKind.Paragraph, rest, dp: under);
        }
        Flush();
        return blocks;
    }

    // ---------------------------------------------------------------- 行內

    private const string Escapable = "`*_[]()#!>-+.|~";

    /// <summary>一個區塊裡的文字拆成樣式片段。相鄰同樣式的片段會合併。</summary>
    public static IReadOnlyList<MdSpan> Inline(string text)
    {
        var spans = new List<MdSpan>();
        if (string.IsNullOrEmpty(text)) return spans;
        // 沒收尾的 [ 或 * 每一個都要往後掃到這一塊的結尾：單一段落長到不像話時就不排版了，
        // 不然串流時每次更新都在畫面的執行緒上花掉幾百毫秒
        if (text.Length > MaxInline) { spans.Add(new MdSpan(text, MdStyle.None)); return spans; }
        var code = CodeSpans(text);
        Walk(text, 0, text.Length, MdStyle.None, code, spans, 0, new Work());
        return spans;
    }

    /// <summary>
    /// 先找出所有收了尾的行內程式碼（反引號的長度要一樣），它們優先：裡面的星號、底線都不算標記。
    /// 回傳每個起點對應的（結束位置、內容）。
    /// </summary>
    private static Dictionary<int, (int End, string Content)> CodeSpans(string s)
    {
        var found = new Dictionary<int, (int, string)>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length && Escapable.Contains(s[i + 1])) { i += 2; continue; }
            if (s[i] != '`') { i++; continue; }
            int n = Run(s, i, s.Length);
            int j = i + n, close = -1;
            while (j < s.Length)
            {
                if (s[j] != '`') { j++; continue; }
                int m = Run(s, j, s.Length);
                if (m == n) { close = j; break; }
                j += m;
            }
            if (close < 0) { i += n; continue; }   // 沒收尾：這排反引號是普通的字
            var content = s[(i + n)..close].Replace('\n', ' ');
            // 跟 CommonMark 一樣，兩邊各有一個空格時拿掉（``` `` `x` `` ``` 這種寫法要的）
            if (content.Length >= 2 && content[0] == ' ' && content[^1] == ' ' && content.Trim().Length > 0) content = content[1..^1];
            found[i] = (close + n, content);
            i = close + n;
        }
        return found;
    }

    private static int Run(string s, int i, int end)
    {
        int j = i;
        while (j < end && s[j] == s[i]) j++;
        return j - i;
    }

    /// <summary>一個區塊超過這麼多字就不做行內排版。</summary>
    public const int MaxInline = 20_000;
    /// <summary>標記最多套幾層（連結裡的連結、粗體裡的斜體…）：再深就當普通文字，不讓遞迴沒有上限。</summary>
    private const int MaxDepth = 12;

    /// <summary>
    /// 找收尾要掃過的字數上限（整個區塊共用）。沒收尾的 [ 或 * 每一個都掃到區塊結尾，兩萬字全是這種標記要花上好幾秒；
    /// 正常的回答連這個數字的百分之一都用不到。用完之後剩下的標記都當普通文字。
    /// </summary>
    private sealed class Work { public long Left = 4_000_000; }

    private static void Walk(string s, int from, int to, MdStyle style, Dictionary<int, (int End, string Content)> code, List<MdSpan> spans, int depth, Work work)
    {
        var plain = new StringBuilder();
        void Add(string text, MdStyle st)
        {
            if (text.Length == 0) return;
            if (spans.Count > 0 && spans[^1].Style == st) spans[^1] = new MdSpan(spans[^1].Text + text, st);
            else spans.Add(new MdSpan(text, st));
        }
        void Flush() { Add(plain.ToString(), style); plain.Clear(); }

        int i = from;
        while (i < to)
        {
            char c = s[i];
            if (code.TryGetValue(i, out var span) && span.End <= to)
            {
                Flush();
                Add(span.Content, style | MdStyle.Code);
                i = span.End;
                continue;
            }
            if (c == '\\' && i + 1 < to && Escapable.Contains(s[i + 1])) { plain.Append(s[i + 1]); i += 2; continue; }
            if (c == '`') { int n = Run(s, i, to); plain.Append(s, i, n); i += n; continue; }   // 沒收尾的反引號
            if (depth < MaxDepth && c == '[' && !(i > 0 && s[i - 1] == '!') && Link(s, i, to, code, work) is { } link)
            {
                Flush();
                int before = spans.Count;
                Walk(s, i + 1, link.TextEnd, style | MdStyle.Link, code, spans, depth + 1, work);
                // 網址留在後面：只顯示文字的話，「參考某頁」看不出連到哪裡，也沒辦法從畫面上選取複製
                string url = s[(link.TextEnd + 2)..(link.End - 1)];
                string shown = string.Concat(spans.Skip(before).Select(x => x.Text));
                if ((url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) && !shown.Contains(url, StringComparison.OrdinalIgnoreCase))
                    spans.Add(new MdSpan($" ({url})", style));
                i = link.End;
                continue;
            }
            if (c is '*' or '_')
            {
                int n = Run(s, i, to);
                if (depth < MaxDepth && Closer(s, i, n, to, code, work) is { } close)
                {
                    Flush();
                    var add = c == '_' || n == 1 ? MdStyle.Italic : n == 2 ? MdStyle.Bold : MdStyle.Bold | MdStyle.Italic;
                    Walk(s, i + n, close, style | add, code, spans, depth + 1, work);
                    i = close + n;
                    continue;
                }
                plain.Append(s, i, n);
                i += n;
                continue;
            }
            plain.Append(c);
            i++;
        }
        Flush();
    }

    private static bool AsciiWord(char c) => c < 128 && char.IsLetterOrDigit(c);

    /// <summary>
    /// 從 i 開始、長度 n 的一排星號或底線：找跟它配對、長度一樣的收尾，找不到回傳 null。
    /// 長度一定要一樣，<c>**粗*</c> 在串流途中才不會先閃成斜體。
    /// </summary>
    private static int? Closer(string s, int i, int n, int to, Dictionary<int, (int End, string Content)> code, Work work)
    {
        char c = s[i];
        if (c == '_' ? n != 1 : n > 3) return null;   // __init__ 這種不是標記
        // 開頭：裡面那側要是字
        if (i + n >= to || char.IsWhiteSpace(s[i + n])) return null;
        char before = i > 0 ? s[i - 1] : ' ';
        // 外側：底線前面不能是字（snake_case、中文字中間的底線）；單一星號前面不能是英數（2*3*4、foo*bar）
        if (c == '_' && char.IsLetterOrDigit(before)) return null;
        if (c == '*' && n == 1 && AsciiWord(before)) return null;

        int j = i + n;
        while (j < to)
        {
            if ((work.Left -= 1) < 0) return null;
            if (code.TryGetValue(j, out var span) && span.End <= to) { j = span.End; continue; }
            if (s[j] == '\\' && j + 1 < to && Escapable.Contains(s[j + 1])) { j += 2; continue; }
            if (s[j] != c) { j++; continue; }
            int m = Run(s, j, to);
            if (m == n && !char.IsWhiteSpace(s[j - 1]))
            {
                char after = j + m < s.Length ? s[j + m] : ' ';
                bool ok = c == '_' ? !char.IsLetterOrDigit(after) : n != 1 || !AsciiWord(after);
                if (ok) return j;
            }
            j += m;
        }
        return null;
    }

    /// <summary>收了尾的 [文字](網址)：回傳文字的結束位置與整段的結束位置。網址不能有空白。</summary>
    private static (int TextEnd, int End)? Link(string s, int i, int to, Dictionary<int, (int End, string Content)> code, Work work)
    {
        int depth = 0;
        for (int j = i + 1; j < to; j++)
        {
            if ((work.Left -= 1) < 0) return null;
            if (code.TryGetValue(j, out var span) && span.End <= to) { j = span.End - 1; continue; }
            if (s[j] == '\\') { j++; continue; }
            if (s[j] == '[') depth++;
            else if (s[j] == ']')
            {
                if (depth-- > 0) continue;
                if (j == i + 1 || j + 1 >= to || s[j + 1] != '(') return null;
                for (int k = j + 2; k < to; k++)
                {
                    if ((work.Left -= 1) < 0 || char.IsWhiteSpace(s[k])) return null;
                    if (s[k] == ')') return k == j + 2 ? null : (j, k + 1);
                }
                return null;
            }
        }
        return null;
    }
}

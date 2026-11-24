// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace Shima.Lyrics;

/// <summary>一段原文與它的讀音（平假名）；只有漢字段才有讀音，其他是 null。</summary>
public readonly record struct Ruby(string Text, string? Reading);

/// <summary>整份歌詞的讀音，跟 Lines 一一對應（不標的那行是 null）。</summary>
public sealed class RubySheet
{
    /// <summary>讀音模式、但讀音還在算（或這首不標）：版面照讀音模式排，只是沒有字可以畫。</summary>
    public static RubySheet Pending { get; } = new([]);

    public IReadOnlyList<IReadOnlyList<Ruby>?> Lines { get; }

    /// <summary>每一行把漢字換成讀音的整句（給桌面歌詞的第二行）；沒有讀音的那行是空字串。</summary>
    public IReadOnlyList<string> Readings { get; }

    public RubySheet(IReadOnlyList<IReadOnlyList<Ruby>?> lines)
    {
        Lines = lines;
        Readings = lines.Select(l => l != null && l.Any(r => r.Reading != null) ? Furigana.Hiragana(l) : "").ToList();
    }
}

/// <summary>
/// 日文歌詞的漢字讀音：用 Windows 日文輸入法的 IFELanguage（ProgID MSIME.Japan，Excel 的 PHONETIC 用的同一個），離線、不用網路。
/// 沒裝日文輸入法的電腦上 <see cref="Available"/> 是 false，整個功能不出現。
/// <para>
/// 介面依據：Windows SDK 10.0.26100.0 的 um/msime.h（<c>#pragma pack(1)</c>、IID {019F7152-E6DB-11d0-83C3-00C04FDDB82E}、
/// vtable 是 IUnknown 之後 Open、Close、GetJMorphResult…）、Microsoft 的〈IFELanguage Functions〉（呼叫端用 CoTaskMemFree 釋放結果），
/// 與 windows-rs 的 MORRSLT（<c>repr(C, packed(1))</c>）互相比對；欄位位置在這台電腦上實際呼叫驗證過。
/// </para>
/// </summary>
public static class Furigana
{
    private const uint ReqRev = 0x00030000;                 // FELANG_REQ_REV：漢字 → 讀音
    private const uint ModeMonoRuby = 0x00000002;           // FELANG_CMODE_MONORUBY：填 paMonoRubyPos
    private const uint ModeNoInvisible = 0x40000000;        // FELANG_CMODE_NOINVISIBLECHAR；HIRAGANAOUT 是 0（預設）
    private const ushort Invalid = 0xFFFF;                  // FELANG_INVALD_PO：這個字沒有自己的讀音（熟字訓，跟前一個字一起唸）
    private const int MaxInput = 200;

    // ---------------------------------------------------------------- 純函式

    public static bool IsKanji(char c) => c is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '豈' and <= '﫿' or '々' or '〆' or '〇' or 'ヶ';

    public static bool HasKanji(string text) => text.Any(IsKanji);

    private static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゟ' or >= 'ァ' and <= 'ヺ' or 'ー';

    private static char ToHiragana(char c) => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c;

    /// <summary>漢字換成讀音的整句；其他照原文（英數、空白、標點不會變成全形）。</summary>
    public static string Hiragana(IReadOnlyList<Ruby> line) => string.Concat(line.Select(r => r.Reading ?? r.Text));

    /// <summary>
    /// 原文、整段讀音與 paMonoRubyPos（原文每個字的讀音從讀音的第幾個字開始，共 text.Length + 1 個，最後一個是讀音的長度）
    /// 算出分段：送氣假名各自分開，相鄰的漢字併成一段。對照表對不上的時候回傳 null（這一行不標），不丟例外。
    /// </summary>
    public static IReadOnlyList<Ruby>? Split(string text, string reading, IReadOnlyList<ushort>? mono)
    {
        int n = text.Length;
        if (n == 0 || mono == null || mono.Count != n + 1 || mono[0] == Invalid || mono[n] != reading.Length) return null;

        // 先照對照表切成「一個字（或熟字訓的幾個字）對一段讀音」
        var groups = new List<(int From, int To, int ReadFrom, int ReadTo)>();
        int last = 0;
        for (int i = 0; i < n; i++)
        {
            if (mono[i] == Invalid) continue;
            if (mono[i] < last || mono[i] > reading.Length) return null;
            if (groups.Count > 0) groups[^1] = groups[^1] with { To = i, ReadTo = mono[i] };
            groups.Add((i, n, mono[i], reading.Length));
            last = mono[i];
        }

        var pieces = new List<Ruby>();
        foreach (var (from, to, readFrom, readTo) in groups)
        {
            string piece = text[from..to], sound = reading[readFrom..readTo];
            if (!HasKanji(piece))
            {
                // 平假名讀出來一定是自己：不是的話對照表錯位了，整行不標
                if (piece.All(c => c is >= 'ぁ' and <= 'ゟ') && sound != piece) return null;
                pieces.Add(new Ruby(piece, null));
                continue;
            }
            // 輸入法把送氣假名併進同一段時，頭尾跟讀音一樣的假名拆出來
            int head = 0, tail = 0;
            while (head < piece.Length - 1 && head < sound.Length && IsKana(piece[head]) && ToHiragana(piece[head]) == sound[head]) head++;
            while (tail < piece.Length - head - 1 && tail < sound.Length - head && IsKana(piece[^(tail + 1)]) && ToHiragana(piece[^(tail + 1)]) == sound[^(tail + 1)]) tail++;
            if (head > 0) pieces.Add(new Ruby(piece[..head], null));
            string core = piece[head..^tail], coreSound = sound[head..^tail];
            // 讀音是空的、或不全是假名（輸入法不認得的字會原樣丟回來）：這段不標
            pieces.Add(new Ruby(core, coreSound.Length > 0 && coreSound.All(IsKana) ? coreSound : null));
            if (tail > 0) pieces.Add(new Ruby(piece[^tail..], null));
        }

        // 相鄰的沒讀音段併起來、相鄰的漢字段併起來（讀音接起來），畫的時候比較好排
        var merged = new List<Ruby>();
        foreach (var p in pieces)
        {
            if (merged.Count > 0 && (merged[^1].Reading == null) == (p.Reading == null))
                merged[^1] = new Ruby(merged[^1].Text + p.Text, p.Reading == null ? null : merged[^1].Reading + p.Reading);
            else merged.Add(p);
        }
        return merged;
    }

    /// <summary>這份歌詞要不要標讀音：整份有假名（是日文）才標，中文歌不會被標上日文讀音。</summary>
    public static bool Wanted(LyricSheet sheet) => sheet.Lines.Any(l => Fonts.HasKana(l.Text)) && sheet.Lines.Any(l => HasKanji(l.Text));

    // ---------------------------------------------------------------- 輸入法元件

    private static readonly Lazy<Type?> Component = new(() =>
    {
        try { return Type.GetTypeFromProgID("MSIME.Japan"); } catch { return null; }
    });
    private static volatile bool _broken;

    /// <summary>這台電腦有日文輸入法的元件（只查登錄，不建立物件）。建立失敗過之後也是 false。</summary>
    public static bool Available => !_broken && Component.Value != null;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Task<RubySheet?>> Cache = [];
    private static readonly Queue<string> Order = new();
    private static BlockingCollection<Action>? _jobs;

    /// <summary>
    /// 整份歌詞的讀音，在背景算（以歌詞內容為鍵快取，同一份歌詞換了物件也不重算）。
    /// 不是日文、沒有輸入法、或元件出錯時結果是 null；這個 Task 不會失敗。
    /// </summary>
    public static Task<RubySheet?> ForAsync(LyricSheet sheet)
    {
        if (!Available || !Wanted(sheet)) return Task.FromResult<RubySheet?>(null);
        var lines = sheet.Lines.Select(l => l.Text).ToArray();
        string key = string.Join("\n", lines);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var known)) return known;
            var done = new TaskCompletionSource<RubySheet?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Cache[key] = done.Task;
            Order.Enqueue(key);
            while (Order.Count > 8) Cache.Remove(Order.Dequeue());
            Jobs().Add(() => done.TrySetResult(Compute(lines)));
            return done.Task;
        }
    }

    /// <summary>
    /// 一條專用的 STA 執行緒：元件在這裡建立、所有呼叫都在這裡做（自訂的 IUnknown 介面沒有 proxy，不能跨 apartment 用）。
    /// 第一次需要時才開，之後一直留著給下一首用。
    /// </summary>
    private static BlockingCollection<Action> Jobs()
    {
        if (_jobs != null) return _jobs;
        var jobs = _jobs = new BlockingCollection<Action>();
        var thread = new Thread(() =>
        {
            foreach (var job in jobs.GetConsumingEnumerable())
                try { job(); } catch (Exception e) { Log.Write("讀音: " + e.Message); }
            Release();
        }) { IsBackground = true, Name = "Furigana" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return jobs;
    }

    [ThreadStatic] private static IFELanguage? _ime;

    private static IFELanguage? Ime()
    {
        if (_ime != null || _broken) return _ime;
        try
        {
            var ime = (IFELanguage)Activator.CreateInstance(Component.Value!)!;
            if (ime.Open() < 0) { Marshal.FinalReleaseComObject(ime); throw new COMException("IFELanguage.Open"); }
            return _ime = ime;
        }
        catch (Exception e)
        {
            _broken = true;
            Log.Write("讀音: 日文輸入法的元件不能用 " + e.Message);
            return null;
        }
    }

    private static void Release()
    {
        if (_ime is not { } ime) return;
        _ime = null;
        try { ime.Close(); Marshal.FinalReleaseComObject(ime); } catch { }
    }

    private static RubySheet? Compute(string[] lines)
    {
        if (Ime() is not { } ime) return null;
        var result = new IReadOnlyList<Ruby>?[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Length is > 0 and <= MaxInput && HasKanji(lines[i])) result[i] = Line(ime, lines[i]);
        return new RubySheet(result);
    }

    /// <summary>一行歌詞送進輸入法，讀出整段讀音與 paMonoRubyPos。任何不對勁都回傳 null。</summary>
    private static IReadOnlyList<Ruby>? Line(IFELanguage ime, string text)
    {
        IntPtr block = IntPtr.Zero;
        try
        {
            if (ime.GetJMorphResult(ReqRev, ModeMonoRuby | ModeNoInvisible, text.Length, text, IntPtr.Zero, out block) != 0 || block == IntPtr.Zero) return null;
            // MORRSLT（pack 1）：DWORD dwSize; WCHAR* pwchOutput; WORD cchOutput; WCHAR* pwchRead; WORD cchRead;
            //                   WORD* pchInputPos; WORD* pchOutputIdxWDD; WORD* pchReadIdxWDD; WORD* paMonoRubyPos; …
            // REV 的時候 pwchOutput 是讀音、pwchRead 是（轉成全形的）原文
            int p = IntPtr.Size, at = 4;
            IntPtr output = Marshal.ReadIntPtr(block, at); at += p;
            int outputLength = (ushort)Marshal.ReadInt16(block, at); at += 2;
            at += p;   // pwchRead
            int readLength = (ushort)Marshal.ReadInt16(block, at); at += 2;
            at += p * 3;   // pchInputPos、pchOutputIdxWDD、pchReadIdxWDD
            IntPtr monoPtr = Marshal.ReadIntPtr(block, at);
            if (output == IntPtr.Zero || monoPtr == IntPtr.Zero || readLength != text.Length) return null;
            // 兩個陣列都要整個落在這塊記憶體裡（dwSize 是整塊的大小）才讀：
            // 讀到外面去是存取違規，那種錯誤攔不住，整個程式會直接消失
            long size = (uint)Marshal.ReadInt32(block, 0);
            if (!Inside(block, size, output, 2L * outputLength) || !Inside(block, size, monoPtr, 2L * (text.Length + 1))) return null;
            var mono = new ushort[text.Length + 1];
            for (int k = 0; k < mono.Length; k++) mono[k] = (ushort)Marshal.ReadInt16(monoPtr, k * 2);
            return Split(text, Marshal.PtrToStringUni(output, outputLength), mono);
        }
        catch (Exception e) { Log.Write("讀音: " + e.Message); return null; }
        finally { if (block != IntPtr.Zero) Marshal.FreeCoTaskMem(block); }
    }

    private static bool Inside(IntPtr block, long size, IntPtr pointer, long bytes)
    {
        long offset = pointer.ToInt64() - block.ToInt64();
        return offset >= 0 && bytes >= 0 && offset + bytes <= size;
    }

    /// <summary>msime.h 的 IFELanguage；只宣告用得到的前三個方法（後面的 vtable 位置不受影響）。</summary>
    [ComImport, Guid("019F7152-E6DB-11d0-83C3-00C04FDDB82E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFELanguage
    {
        [PreserveSig] int Open();
        [PreserveSig] int Close();
        [PreserveSig] int GetJMorphResult(uint request, uint mode, int length, [MarshalAs(UnmanagedType.LPWStr)] string input, IntPtr columnInfo, out IntPtr result);
    }
}

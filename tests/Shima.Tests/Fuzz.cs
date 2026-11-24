// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit.Sdk;

namespace Shima.Tests;

/// <summary>
/// 隨機輸入測試的共用部分：固定種子、失敗時帶上輸入與迭代次數、卡住時也能報出是哪個輸入。
/// 每個目標自己寫產生器：從那種格式常見的片段拼起來，再隨機破壞一點，才測得到深處。
/// </summary>
internal static class Fuzz
{
    private static readonly object Gate = new();

    /// <summary>
    /// 跑 iterations 次：make 產生輸入、check 檢查（丟例外或斷言失敗都算失敗）。
    /// 失敗訊息含種子、第幾次與輸入。perCallMs &gt; 0：單次超過就算失敗（防病態輸入拖垮畫面）。
    /// 整個目標超過 limitSeconds 還沒結束（無窮迴圈）也失敗，並指出卡在哪個輸入。
    /// </summary>
    public static void Run<T>(string name, int seed, int iterations, Func<Random, T> make, Action<T> check,
        Func<T, string>? show = null, int perCallMs = 0, int limitSeconds = 8)
    {
        show ??= v => Show(v);
        var random = new Random(seed);
        T? current = default;
        int index = 0;
        Exception? failure = null;
        double slowest = 0;
        var total = Stopwatch.StartNew();

        var thread = new Thread(() =>
        {
            try
            {
                for (index = 0; index < iterations; index++)
                {
                    current = make(random);
                    var one = Stopwatch.StartNew();
                    check(current);
                    slowest = Math.Max(slowest, one.Elapsed.TotalMilliseconds);
                    if (perCallMs > 0 && one.ElapsedMilliseconds > perCallMs)
                        throw new XunitException($"單次花了 {one.ElapsedMilliseconds} 毫秒（上限 {perCallMs}）");
                }
            }
            catch (Exception e) { failure = e; }
        }, 1024 * 1024) { IsBackground = true };   // 跟畫面的主執行緒一樣的 1 MB 堆疊
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(limitSeconds)))
            throw new XunitException($"[{name}] seed={seed} 第 {index} 次卡住（超過 {limitSeconds} 秒）。輸入：{Safe(show, current)}");
        if (failure != null)
            throw new XunitException($"[{name}] seed={seed} 第 {index} 次失敗。輸入：{Safe(show, current)}\n{failure}");

        var line = $"{name}\t{iterations}\t{total.ElapsedMilliseconds} ms\t最慢一次 {slowest:0.0} ms";
        lock (Gate)
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "fuzz-report.txt"), line + "\n"); } catch (IOException) { }
    }

    private static string Safe<T>(Func<T, string> show, T? value)
    {
        try { return value == null ? "(null)" : show(value); } catch (Exception) { return "(無法顯示)"; }
    }

    /// <summary>把輸入印成看得見跳脫字元的樣子，好直接貼回測試。</summary>
    public static string Show(object? value) => value switch
    {
        null => "null",
        string s => Escape(s),
        _ => value.ToString() ?? "",
    };

    public static string Escape(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s.Length > 600 ? s[..600] + $"…(共 {s.Length} 字)" : s)
            sb.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                _ when c < 32 || c is >= '\uD800' and <= '\uDFFF' || c is '﻿' or '​' => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        return sb.Append('"').ToString();
    }
}

/// <summary>產生器的小工具。</summary>
internal static class Gen
{
    public static T Pick<T>(this Random r, IReadOnlyList<T> items) => items[r.Next(items.Count)];

    public static bool Chance(this Random r, double p) => r.NextDouble() < p;

    /// <summary>外來文字常見的雜訊片段：空字串、空白、換行、BOM、中日韓、emoji、代理對（含孤立的）、全形、控制字元。</summary>
    public static readonly string[] Noise =
    [
        "", " ", "  ", "\t", "\n", "\r\n", "\r", "﻿", " ", "　", "中文", "歌詞", "日本語のうた", "こんにちは", "한국어", "😀", "👨‍👩‍👧", "🎵",
        "\uD83D", "\uDE00", "ＡＢＣ１２３", "（全形）", "\u0000", "\u001B[0m", "​", "é", "é", "İ", "ı", "ß", "K", "ǅ", "COM¹", "٣", "０", "a", "Z", "0", "9", "-", "_", ".", ",", ":", "：",
    ];

    /// <summary>隨機破壞：刪掉一段、重複一段、截斷、插入一個怪字、前後加空白。</summary>
    public static string Mangle(this Random r, string s)
    {
        for (int rounds = r.Next(0, 3); rounds > 0 && s.Length > 0; rounds--)
        {
            int a = r.Next(s.Length), b = Math.Min(s.Length, a + r.Next(1, 12));
            switch (r.Next(5))
            {
                case 0: s = s.Remove(a, b - a); break;
                case 1: s = s.Insert(b, s[a..b]); break;
                case 2: s = s[..a]; break;
                case 3: s = s.Insert(a, r.Pick(Noise)); break;
                default: s = (r.Chance(0.5) ? " " : "\n") + s + (r.Chance(0.5) ? " " : "\r\n"); break;
            }
        }
        return s;
    }

    /// <summary>從片段表隨機拼 min..max 個，片段之間偶爾夾雜訊。</summary>
    public static string Build(this Random r, IReadOnlyList<string> parts, int min, int max, double noise = 0.15)
    {
        var sb = new StringBuilder();
        for (int n = r.Next(min, max + 1); n > 0; n--)
        {
            sb.Append(r.Pick(parts));
            if (r.Chance(noise)) sb.Append(r.Pick(Noise));
        }
        return sb.ToString();
    }

    /// <summary>很長的重複：unit 重複幾百到幾萬次。</summary>
    public static string Long(this Random r, string unit, int max) =>
        string.Concat(Enumerable.Repeat(unit, r.Next(1, max + 1)));

    /// <summary>不含代理對、控制字元的「乾淨」歌詞用字，給要求原樣往返的測試用。</summary>
    public static readonly string[] Clean = ["la", "歌", "うた", "한", "Hello", "world", "ＡＢ", "é", "你好", "…", "-", "1", "'", "?", "!"];

    public static string CleanText(this Random r, int minWords, int maxWords)
    {
        var sb = new StringBuilder();
        for (int n = r.Next(minWords, maxWords + 1); n > 0; n--)
        {
            if (sb.Length > 0 && r.Chance(0.4)) sb.Append(' ');
            sb.Append(r.Pick(Clean));
        }
        return sb.ToString().Trim();
    }
}

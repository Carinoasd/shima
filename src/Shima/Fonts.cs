// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Windows.Media;

namespace Shima;

/// <summary>
/// 同一個漢字在日文和臺灣的寫法不一樣（直、骨、海…），混用一組字型會一下日式一下臺式。
/// 所以日文內容用日文字型、中文內容用臺灣字型。
/// </summary>
public static class Fonts
{
    public static readonly FontFamily Japanese = new("Segoe UI Variable Text, Yu Gothic UI, Meiryo UI, Segoe UI");
    public static readonly FontFamily Chinese = new("Segoe UI Variable Text, Microsoft JhengHei UI, Segoe UI");
    private static readonly CultureInfo Ja = CultureInfo.GetCultureInfo("ja-JP"), Zh = CultureInfo.GetCultureInfo("zh-TW");

    /// <summary>有平假名或片假名就是日文。</summary>
    public static bool HasKana(string text) => text.Any(c => c is >= '぀' and <= 'ヿ');

    public static FontFamily For(bool japanese) => japanese ? Japanese : Chinese;
    public static CultureInfo Culture(bool japanese) => japanese ? Ja : Zh;

    /// <summary>text 每個前綴的寬度：[k] 是前 k 個字的寬度（[0] = 0）。用來把「唱到第幾個字」換成位置。</summary>
    public static double[] Prefixes(string text, Typeface typeface, double size, CultureInfo culture, double pixelsPerDip)
    {
        var widths = new double[text.Length + 1];
        for (int k = 1; k <= text.Length; k++)
            widths[k] = new FormattedText(text[..k], culture, System.Windows.FlowDirection.LeftToRight, typeface, size, Brushes.White, pixelsPerDip).WidthIncludingTrailingWhitespace;
        return widths;
    }
}

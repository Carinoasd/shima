// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

global using L = Shima.Loc;

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shima;

/// <summary>
/// 多國語言。程式裡的文字都寫繁體中文，其他語言是一張「繁體中文 → 譯文」的對照表
/// （Assets/strings.tsv，第一列是語言代碼）。表裡沒有的字就維持中文，所以翻譯不完整也不會壞。
/// </summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
    [
        ("zh-TW", "繁體中文"), ("zh-CN", "简体中文"), ("en", "English"), ("ja", "日本語"), ("ko", "한국어"),
        ("es", "Español"), ("fr", "Français"), ("de", "Deutsch"), ("pt", "Português"), ("it", "Italiano"), ("ru", "Русский"),
    ];

    private static Dictionary<string, string> _table = [];

    public static string Lang { get; private set; } = "zh-TW";

    /// <summary>設定值 → 實際用的語言。空字串：跟著 Windows；Windows 的語言沒有翻譯就用英文。</summary>
    public static string Resolve(string setting, CultureInfo? windows = null)
    {
        if (Languages.Any(l => l.Code == setting)) return setting;
        var ui = windows ?? CultureInfo.CurrentUICulture;
        var two = ui.TwoLetterISOLanguageName;
        if (two == "zh")
        {
            var name = ui.Name;
            bool traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                               name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
            return traditional ? "zh-TW" : "zh-CN";
        }
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    public static void Init(string setting)
    {
        Lang = Resolve(setting);
        _table = [];
        if (Lang == "zh-TW") return;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shima.Assets.strings.tsv");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            _table = Parse(reader.ReadToEnd(), Lang);
        }
        catch (Exception e) { Log.Write("lang: " + e.Message); }
    }

    /// <summary>對照表（TSV）裡某個語言那一欄：繁體中文 → 譯文。空的格子不收。</summary>
    public static Dictionary<string, string> Parse(string tsv, string lang)
    {
        var table = new Dictionary<string, string>();
        var lines = tsv.Replace("\r", "").Split('\n');
        int column = Array.IndexOf(lines[0].Split('\t'), lang);
        if (column < 1) return table;
        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split('\t');
            if (cells.Length > column && cells[0].Length > 0 && cells[column].Length > 0) table[cells[0]] = cells[column];
        }
        return table;
    }

    public static string T(string zh) => _table.TryGetValue(zh, out var t) ? t : zh;

    /// <summary>帶參數的句子：「音量 {0}」。</summary>
    public static string F(string zh, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(zh), args);

    /// <summary>介面用的字型：日文、韓文、簡體各用自己的，才不會字形混雜。</summary>
    public static FontFamily Font => Lang switch
    {
        "ja" => Fonts.Japanese,
        "ko" => new FontFamily("Segoe UI Variable Text, Malgun Gothic, Segoe UI"),
        "zh-CN" => new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
        _ => Fonts.Chinese,
    };

    /// <summary>日期、星期的寫法跟著介面語言。</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Lang is "zh-TW" or "zh-CN" ? Lang : Speech);

    /// <summary>語音助手聽與說的語言（Windows 的語音語言代碼）。</summary>
    public static string Speech => Lang switch
    {
        "zh-TW" => "zh-TW", "zh-CN" => "zh-CN", "ja" => "ja-JP", "ko" => "ko-KR", "es" => "es-ES", "fr" => "fr-FR",
        "de" => "de-DE", "pt" => "pt-BR", "it" => "it-IT", "ru" => "ru-RU", _ => "en-US",
    };

    /// <summary>把 XAML 裡寫的文字換成目前的語言：文字、按鈕、選單、提示。圖示（單一符號）查不到就不動。</summary>
    public static void Translate(DependencyObject root)
    {
        if (Lang == "zh-TW") return;
        var seen = new HashSet<DependencyObject>();
        void Walk(DependencyObject d)
        {
            if (!seen.Add(d)) return;
            switch (d)
            {
                case TextBlock t when t.Inlines.Count <= 1 && t.Text.Length > 1: t.Text = T(t.Text); break;
                case HeaderedItemsControl h when h.Header is string header: h.Header = T(header); break;
                case ContentControl c when c.Content is string content && content.Length > 1: c.Content = T(content); break;
            }
            if (d is FrameworkElement e)
            {
                if (e.ToolTip is string tip) e.ToolTip = T(tip);
                if (e.ContextMenu != null) Walk(e.ContextMenu);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Walk(child);
        }
        Walk(root);
    }
}

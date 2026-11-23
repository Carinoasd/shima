// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

namespace Shima;

/// <summary>
/// 沒有歌在播的時候，膠囊上寫什麼。不看畫面也不看真的時鐘，全部由外面給：好測試。
/// 番茄鐘在跑時永遠優先（提示與語音在更上一層就先處理掉了）。
/// </summary>
public static class IdleLine
{
    public const string Clock = "clock", Date = "date", Alarm = "alarm", None = "none";

    /// <summary>認得的設定值；其他都當作「時間」。</summary>
    public static string Normalize(string? mode) => mode is Date or Alarm or None ? mode : Clock;

    /// <summary>提醒的內容在膠囊上最多佔幾個字寬（中日韓的字算兩格）。</summary>
    private const int AlarmWidth = 20;

    /// <param name="timer">番茄鐘的文字；沒在跑是 null。</param>
    /// <param name="alarms">排好的提醒（有重複的，At 已經是下一次）。</param>
    /// <param name="tomorrow">「明天」的譯文，給不在今天的提醒用。</param>
    public static string Text(string? mode, DateTime now, string? timer, IEnumerable<Shima.Agents.Alarm> alarms, CultureInfo culture, string tomorrow)
    {
        if (timer != null) return timer;
        var time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        switch (Normalize(mode))
        {
            case None: return "";
            case Date: return $"{Day(now, culture)} {now.ToString("ddd", culture)} {time}";
            case Alarm:
                var next = alarms.OrderBy(a => a.At).FirstOrDefault();
                if (next == null) return time;
                var when = Shima.Agents.Alarms.Short(next.At, now, tomorrow);
                var text = Trim(next.Text, AlarmWidth);
                return text.Length == 0 ? when : $"{when} {text}";
            default: return time;
        }
    }

    /// <summary>不含年份的短日期，順序與分隔符號照各語言自己的短日期（11/24、24.11.、11월 24일…）。</summary>
    public static string Day(DateTime date, CultureInfo culture)
    {
        var pattern = Regex.Replace(culture.DateTimeFormat.ShortDatePattern, "y+", "").Trim(' ', '/', '.', '-', ',');
        if (pattern.Length == 0) pattern = "M/d";
        try { return date.ToString(pattern, culture); }
        catch (FormatException) { return date.ToString("M/d", CultureInfo.InvariantCulture); }
    }

    /// <summary>太長就截斷加「…」。中日韓的字寬是兩格。</summary>
    public static string Trim(string text, int cells)
    {
        text = text.Trim();
        int used = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int w = Wide(text[i]) ? 2 : 1;
            if (used + w > cells) return text[..i].TrimEnd() + "…";
            used += w;
        }
        return text;
    }

    private static bool Wide(char c)
    {
        int n = c;
        return n >= 0x1100 && (n <= 0x11FF || n is >= 0x2E80 and <= 0xA4CF || n is >= 0xAC00 and <= 0xD7A3 || n is >= 0xF900 and <= 0xFAFF || n is >= 0xFE30 and <= 0xFE6F || n is >= 0xFF00 and <= 0xFF60 || n is >= 0xFFE0 and <= 0xFFE6);
    }
}

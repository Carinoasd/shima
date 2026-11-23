// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shima.Agents;

/// <summary>
/// 一個提醒：幾點、要說什麼。Id 給人（和 AI）取消時指定用。
/// Repeat 是重複的方式：daily、weekdays（週一到週五）、或某一天（mon、tue…sun，每週）；null 是只響一次。
/// 舊的存檔沒有這個欄位，讀進來就是 null。
/// </summary>
public sealed record Alarm(int Id, DateTime At, string Text, string? Repeat = null);

/// <summary>
/// 「十分鐘後提醒我關火」「七點半叫我」：到點觸發 <see cref="Due"/>。
/// 存在 alarms.json，關掉程式再開還在；關著的時候到點的，下次開程式時馬上觸發，並標成錯過。
/// 判斷到期（<see cref="Check"/>）和解析時間（<see cref="When"/>）都吃外面給的「現在」，不看真的時鐘，方便測試。
/// </summary>
public sealed class Alarms : IDisposable
{
    /// <summary>最多排多遠以後：一星期。再遠的應該放行事曆。</summary>
    public static readonly TimeSpan Farthest = TimeSpan.FromDays(7);
    /// <summary>超過這麼久才發現到期（程式關著）就算錯過。</summary>
    public static readonly TimeSpan Late = TimeSpan.FromMinutes(2);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<Alarm> _list;
    private System.Threading.Timer? _timer;

    /// <summary>到點了。第二個參數 true：程式關著的時候就該響了，現在才補。在背景執行緒觸發。</summary>
    public event Action<Alarm, bool>? Due;

    /// <summary>清單變了（新增、取消、到點被拿掉）。在任何執行緒觸發；畫面要自己切回 UI 執行緒。</summary>
    public event Action? Changed;

    /// <param name="path">存檔位置；不給就是設定資料夾裡的 alarms.json。</param>
    public Alarms(string? path = null)
    {
        _path = path ?? Path.Combine(AppSettings.Folder, "alarms.json");
        try { _list = File.Exists(_path) ? Parse(File.ReadAllText(_path)) : []; }
        catch { _list = []; }
    }

    /// <summary>排好的提醒，照時間先後。</summary>
    public List<Alarm> List { get { lock (_gate) return _list.OrderBy(a => a.At).ThenBy(a => a.Id).ToList(); } }

    public Alarm Add(DateTime at, string text, string? repeat = null)
    {
        Alarm added;
        lock (_gate)
        {
            var alarm = new Alarm(_list.Count == 0 ? 1 : _list.Max(a => a.Id) + 1, at, text.Trim(), NormalizeRepeat(repeat));
            _list.Add(alarm);
            Save();
            added = alarm;
        }
        Changed?.Invoke();
        return added;
    }

    /// <summary>取消一個：what 是編號或內容的關鍵字。找不到是 null。</summary>
    public Alarm? Cancel(string what)
    {
        Alarm? found;
        lock (_gate)
        {
            var key = what.Trim().TrimStart('#');
            found = int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? _list.FirstOrDefault(a => a.Id == id)
                : _list.Where(a => key.Length > 0 && a.Text.Contains(key, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.At).FirstOrDefault();
            if (found == null) return null;
            _list.Remove(found);
            Save();
        }
        Changed?.Invoke();
        return found;
    }

    /// <summary>把到點的拿出來（從清單移除、存檔）並觸發 <see cref="Due"/>。回傳這次到點的。</summary>
    public List<Alarm> Check(DateTime now)
    {
        List<Alarm> due;
        lock (_gate)
        {
            due = _list.Where(a => a.At <= now).OrderBy(a => a.At).ToList();
            if (due.Count == 0) return due;
            // 重複的不移除，排到下一次（從現在往後算，關機好幾天也只響這一次）
            foreach (var a in due)
            {
                if (NormalizeRepeat(a.Repeat) is { } repeat) _list[_list.IndexOf(a)] = a with { At = NextAfter(a.At, repeat, now) };
                else _list.Remove(a);
            }
            Save();
        }
        foreach (var a in due) Due?.Invoke(a, now - a.At > Late);
        Changed?.Invoke();
        return due;
    }

    /// <summary>每秒用真的時鐘檢查一次。開程式時呼叫一次就好；重複呼叫沒關係。</summary>
    public void Start()
    {
        lock (_gate) _timer ??= new System.Threading.Timer(_ => { try { Check(DateTime.Now); } catch { } }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        lock (_gate) { _timer?.Dispose(); _timer = null; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var partial = _path + ".part";
            File.WriteAllText(partial, Serialize(_list));
            File.Move(partial, _path, overwrite: true);
        }
        catch { /* 存不進去就只留在記憶體裡，這次還是會響 */ }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Serialize(IEnumerable<Alarm> alarms) => JsonSerializer.Serialize(alarms.OrderBy(a => a.At).ToList(), Json);

    /// <summary>存檔的內容 → 提醒；壞掉的檔案當作沒有。</summary>
    public static List<Alarm> Parse(string json)
    {
        try { return (JsonSerializer.Deserialize<List<Alarm>>(json) ?? []).Where(a => a is { Text: not null }).ToList(); }
        catch { return []; }
    }

    // ---------------------------------------------------------------- 重複

    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>認得的重複方式（daily、weekdays、sun…sat）；其他一律是 null（不重複）。</summary>
    public static string? NormalizeRepeat(string? repeat)
    {
        var r = repeat?.Trim().ToLowerInvariant();
        return r is "daily" or "weekdays" || (r != null && Array.IndexOf(Days, r) >= 0) ? r : null;
    }

    /// <summary>某一天是週幾要響的重複方式，給畫面用：星期日是 0。</summary>
    public static int? WeeklyDay(string? repeat) => NormalizeRepeat(repeat) is { } r && Array.IndexOf(Days, r) is >= 0 and var i ? i : null;

    public static bool Matches(string repeat, DayOfWeek day) => repeat switch
    {
        "daily" => true,
        "weekdays" => day is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
        _ => Days[(int)day] == repeat,
    };

    /// <summary>
    /// 下一次什麼時候響：時刻沿用 at 的，日期是「現在之後」第一個符合重複方式的那天。
    /// 不管 at 落後現在多少，只會排到未來的下一次，所以關機錯過好幾次也只補響一次。
    /// </summary>
    public static DateTime NextAfter(DateTime at, string repeat, DateTime now)
    {
        var candidate = now.Date + at.TimeOfDay;
        for (var i = 0; i <= 7; i++, candidate = candidate.AddDays(1))
            if (candidate > now && Matches(repeat, candidate.DayOfWeek)) return candidate;
        return at;   // 不認得的重複方式：維持原樣（Check 前已經濾掉，不會走到）
    }

    /// <summary>給 AI 看的重複寫法：每天、平日、每週五。</summary>
    public static string RepeatName(string repeat) => repeat switch
    {
        "daily" => "每天",
        "weekdays" => "平日",
        _ => "每週" + "日一二三四五六"[Math.Max(0, Array.IndexOf(Days, repeat))],
    };

    // 「每天」「平日」「每週五」「every day」「weekdays」「every friday」開頭
    private static readonly Regex RepeatPrefix = new(
        @"^(?:(?<d>每天|每日|天天|every\s*day|daily)|(?<w>平日|工作日|週間|周间|每?(?:週|周|星期)一(?:到|至)(?:週|周|星期)?五|(?:every\s*)?weekdays?)|(?:每(?:週|周|星期|禮拜|礼拜)|every\s*)(?<n>[一二三四五六日天]|sun|mon|tues?|wed(?:nes)?|thu(?:rs?)?|fri|sat(?:ur)?)(?:day)?(?![a-z]))\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? RepeatOf(Match m)
    {
        if (m.Groups["d"].Success) return "daily";
        if (m.Groups["w"].Success) return "weekdays";
        var n = m.Groups["n"].Value.ToLowerInvariant();
        var index = "日一二三四五六".IndexOf(n, StringComparison.Ordinal);
        if (n == "天") index = 0;
        if (index >= 0) return Days[index];
        // tues、wednes、thurs、satur：只看前三個字母
        return n.Length >= 3 && Days.Contains(n[..3]) ? n[..3] : null;
    }

    // ---------------------------------------------------------------- 解析時間

    /// <summary>
    /// 什麼時候響：minutes 是幾分鐘後；at 是幾點幾分（「07:30」「19:05」「7點半」「明天 08:00」「2026-11-24 07:30」）。
    /// 只給幾點、而那個時間今天已經過了，就是明天。看不懂、在過去、或超過一星期是 null。
    /// </summary>
    public static DateTime? When(string? at, double? minutes, DateTime now)
    {
        // 有些模型會把沒用到的數字參數填 0：有給時間就當作沒給分鐘數
        if (minutes is double m && !(m <= 0 && !string.IsNullOrWhiteSpace(at)))
        {
            if (!double.IsFinite(m) || m <= 0 || m > Farthest.TotalMinutes) return null;
            return now.AddSeconds(Math.Round(m * 60));
        }
        if (string.IsNullOrWhiteSpace(at)) return null;
        var text = at.Normalize(System.Text.NormalizationForm.FormKC).Trim();

        DateTime? result = null;
        string[] formats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm", "yyyy/M/d H:mm"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)) result = full;
        else
        {
            text = text.ToLowerInvariant();
            int days = 0;
            foreach (var (word, add) in new[] { ("後天", 2), ("明天", 1), ("tomorrow", 1), ("今天", 0), ("today", 0) })
                if (text.StartsWith(word, StringComparison.Ordinal)) { days = add; text = text[word.Length..].Trim(); break; }
            if (Clock(text) is not { } time) return null;
            result = now.Date.AddDays(days) + time;
            if (days == 0 && result <= now) result = result.Value.AddDays(1);
        }
        return result > now && result - now <= Farthest ? result : null;
    }

    /// <summary>「07:30」「7:30」「7點」「7點半」「7點05分」「下午3點」「3pm」→ 一天裡的時間。</summary>
    private static TimeSpan? Clock(string text)
    {
        var t = text.Replace(" ", "");
        string half = "";   // 上午、下午或中午
        foreach (var (word, kind) in new[] { ("下午", "pm"), ("晚上", "pm"), ("傍晚", "pm"), ("pm", "pm"), ("中午", "noon"), ("上午", "am"), ("早上", "am"), ("凌晨", "am"), ("清晨", "am"), ("am", "am") })
            if (t.Contains(word, StringComparison.Ordinal)) { half = kind; t = t.Replace(word, ""); }
        var match = Regex.Match(t, @"^(\d{1,2})(?::(\d{2})(?::\d{2})?|[點点時时](?:(半)|(\d{1,2})分?)?)?$");
        if (!match.Success) return null;
        // 只寫一個數字（「7」）沒有冒號、沒有「點」、也沒說上午下午：太模糊
        if (!match.Groups[2].Success && !t.Any(c => c is '點' or '点' or '時' or '时') && half.Length == 0) return null;
        int hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
            : match.Groups[3].Success ? 30
            : match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        if (half == "pm" && hour is >= 1 and < 12) hour += 12;
        if (half == "noon" && hour is >= 1 and < 6) hour += 12;
        if (half == "am" && hour == 12) hour = 0;
        if (hour > 23 || minute > 59) return null;
        return new TimeSpan(hour, minute, 0);
    }

    private static readonly Regex Duration = new(@"^(\d+(?:\.\d+)?)\s*(分鐘|分钟|分|min(?:ute)?s?|m|小時|小时|h(?:ou)?rs?|h)(?![a-z])\s*(?:[後后](?!天))?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FullDate = new(@"^\d{4}[-/]\d{1,2}[-/]\d{1,2}[ T]\d{1,2}:\d{2}(?::\d{2})?", RegexOptions.CultureInvariant);
    private static readonly Regex ClockPrefix = new(@"^(?:(?:後天|明天|今天|tomorrow|today)\s*)?(?:(?:上午|下午|早上|晚上|中午|凌晨|清晨|傍晚)\s*)?\d{1,2}(?::\d{2}|[點点時时](?:半|\d{1,2}分?)?)?(?:\s*(?:am|pm)(?![a-z]))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 一行字拆成「什麼時候」與「說什麼」：「15:30 開會」「明天 8點 交報告」「20 分鐘 喝水」「20m 喝水」「1h 休息」。
    /// 時間的部分交給 <see cref="When"/>（所以今天已過的時刻算明天、超過一星期不收）；看不懂是 null。
    /// </summary>
    public static (DateTime At, string Text, string? Repeat)? ParseLine(string? line, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var text = line.Normalize(System.Text.NormalizationForm.FormKC).Trim();

        // 重複的：「每天 8:00 吃藥」「平日 9:30 站會」「每週五 18:00 倒垃圾」。只收幾點，不收日期；At 是下一次
        if (RepeatPrefix.Match(text) is { Success: true } rep && RepeatOf(rep) is { } repeat)
        {
            var rest = text[rep.Length..];
            var clock = ClockPrefix.Match(rest);
            if (!clock.Success || clock.Value.Length == 0 || Clock(clock.Value.ToLowerInvariant().Replace(" ", "")) is not { } time
                || Regex.IsMatch(clock.Value, @"^(?:後天|明天|今天|tomorrow|today)", RegexOptions.IgnoreCase)) return null;
            return (NextAfter(now.Date + time, repeat, now), rest[clock.Length..].Trim(), repeat);
        }

        if (Duration.Match(text) is { Success: true } rel
            && double.TryParse(rel.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            var unit = rel.Groups[2].Value.ToLowerInvariant();
            var minutes = unit is "小時" or "小时" || unit.StartsWith('h') ? amount * 60 : amount;
            return When(null, minutes, now) is { } soon ? (soon, rel.Groups[3].Value.Trim(), null) : null;
        }

        var head = (FullDate.Match(text) is { Success: true } full ? full : ClockPrefix.Match(text));
        if (!head.Success) return null;
        return When(head.Value, null, now) is { } at ? (at, text[head.Length..].Trim(), null) : null;
    }

    /// <summary>畫面上的時間：今天只寫幾點，明天前面加 <paramref name="tomorrow"/>，再遠的寫月／日。</summary>
    public static string Short(DateTime at, DateTime now, string tomorrow) =>
        at.Date == now.Date ? $"{at:HH:mm}" : at.Date == now.Date.AddDays(1) ? $"{tomorrow} {at:HH:mm}" : $"{at:M/d} {at:HH:mm}";

    /// <summary>提醒給 AI 看的完整寫法：單次是「明天 07:30」，重複的是「每天 08:00（下一次 明天 08:00）」。</summary>
    public static string Describe(Alarm alarm, DateTime now) =>
        NormalizeRepeat(alarm.Repeat) is { } r ? $"{RepeatName(r)} {alarm.At:HH:mm}（下一次 {Describe(alarm.At, now)}）" : Describe(alarm.At, now);

    /// <summary>給 AI 看的時間寫法：今天的只寫幾點，其他加上日期。</summary>
    public static string Describe(DateTime at, DateTime now) =>
        at.Date == now.Date ? $"今天 {at:HH:mm}" : at.Date == now.Date.AddDays(1) ? $"明天 {at:HH:mm}" : $"{at:M/d} {at:HH:mm}";
}

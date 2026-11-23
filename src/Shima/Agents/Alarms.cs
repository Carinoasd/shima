// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Shima.Agents;

/// <summary>一個提醒：幾點、要說什麼。Id 給人（和 AI）取消時指定用。</summary>
public sealed record Alarm(int Id, DateTime At, string Text);

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

    /// <param name="path">存檔位置；不給就是設定資料夾裡的 alarms.json。</param>
    public Alarms(string? path = null)
    {
        _path = path ?? Path.Combine(AppSettings.Folder, "alarms.json");
        try { _list = File.Exists(_path) ? Parse(File.ReadAllText(_path)) : []; }
        catch { _list = []; }
    }

    /// <summary>排好的提醒，照時間先後。</summary>
    public List<Alarm> List { get { lock (_gate) return _list.OrderBy(a => a.At).ThenBy(a => a.Id).ToList(); } }

    public Alarm Add(DateTime at, string text)
    {
        lock (_gate)
        {
            var alarm = new Alarm(_list.Count == 0 ? 1 : _list.Max(a => a.Id) + 1, at, text.Trim());
            _list.Add(alarm);
            Save();
            return alarm;
        }
    }

    /// <summary>取消一個：what 是編號或內容的關鍵字。找不到是 null。</summary>
    public Alarm? Cancel(string what)
    {
        lock (_gate)
        {
            var key = what.Trim().TrimStart('#');
            var found = int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? _list.FirstOrDefault(a => a.Id == id)
                : _list.Where(a => key.Length > 0 && a.Text.Contains(key, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.At).FirstOrDefault();
            if (found == null) return null;
            _list.Remove(found);
            Save();
            return found;
        }
    }

    /// <summary>把到點的拿出來（從清單移除、存檔）並觸發 <see cref="Due"/>。回傳這次到點的。</summary>
    public List<Alarm> Check(DateTime now)
    {
        List<Alarm> due;
        lock (_gate)
        {
            due = _list.Where(a => a.At <= now).OrderBy(a => a.At).ToList();
            if (due.Count == 0) return due;
            foreach (var a in due) _list.Remove(a);
            Save();
        }
        foreach (var a in due) Due?.Invoke(a, now - a.At > Late);
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

    /// <summary>給 AI 看的時間寫法：今天的只寫幾點，其他加上日期。</summary>
    public static string Describe(DateTime at, DateTime now) =>
        at.Date == now.Date ? $"今天 {at:HH:mm}" : at.Date == now.Date.AddDays(1) ? $"明天 {at:HH:mm}" : $"{at:M/d} {at:HH:mm}";
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shima.Lyrics;

namespace Shima.Agents;

/// <summary>
/// 島本身功能以外的助手技能：天氣、開網站或程式、提醒、現在幾點、計算、剪貼簿。
/// 回傳的是給 AI 看的一句事實（跟 IslandWindow 的 Tool 一樣不翻譯）；任何錯誤都變成一句話，不丟例外。
/// 這裡的 await 全部 ConfigureAwait(false)：呼叫端可能在 UI 執行緒上同步等結果，不能卡住。
/// </summary>
public static class Skills
{
    /// <summary>這裡處理的工具名稱。</summary>
    public static readonly string[] Names = ["weather", "open", "alarm", "datetime", "calc", "clipboard"];

    /// <summary>提醒的清單。由程式啟動時設定；沒設定時 alarm 工具會說還不能用。</summary>
    public static Alarms? Alarms { get; set; }

    /// <summary>認得的工具就執行並回傳結果；不是這裡的工具回傳 null。</summary>
    public static async Task<string?> RunAsync(string tool, JsonObject arguments)
    {
        if (!Names.Contains(tool)) return null;
        string Arg(string name) => arguments[name]?.ToString().Trim() ?? "";
        try
        {
            return tool switch
            {
                "weather" => await WeatherAsync(Arg("location"), Arg("country"), CancellationToken.None).ConfigureAwait(false),
                "open" => await OpenAsync(Arg("target")).ConfigureAwait(false),
                "alarm" => Alarms is { } alarms ? Alarm(arguments, alarms, DateTime.Now) : "提醒功能還沒準備好",
                "datetime" => Now(DateTimeOffset.Now),
                "calc" => Calculate(Arg("expression")),
                "clipboard" => Clipboard(),
                _ => null,
            };
        }
        catch (Exception e) { return $"{tool} 執行失敗：{e.Message}"; }
    }

    /// <summary>同步版：在背景執行緒跑完再回來，從哪個執行緒（包含 UI 執行緒）呼叫都不會卡死。</summary>
    public static string? Run(string tool, JsonObject arguments) =>
        Names.Contains(tool) ? Task.Run(() => RunAsync(tool, arguments)).GetAwaiter().GetResult() : null;

    private static double? Number(JsonObject arguments, string name) =>
        arguments[name] is { } node && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    // ---------------------------------------------------------------- 現在幾點

    private static readonly string[] Weekdays = ["日", "一", "二", "三", "四", "五", "六"];

    public static string Now(DateTimeOffset now)
    {
        var offset = now.Offset;
        var zone = offset.Minutes == 0 ? $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{Math.Abs(offset.Hours)}" : $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}";
        return $"現在是 {now:yyyy} 年 {now.Month} 月 {now.Day} 日 星期{Weekdays[(int)now.DayOfWeek]} {now:HH:mm}（{zone}）";
    }

    // ---------------------------------------------------------------- 計算

    public static string Calculate(string expression)
    {
        if (expression.Length == 0) return "沒有給算式";
        return Calculator.TryEvaluate(expression, out var value, out var problem)
            ? $"{expression} = {Calculator.Format(value)}"
            : $"算不出來：{problem}";
    }

    // ---------------------------------------------------------------- 剪貼簿

    public const int ClipboardLimit = 4000;

    /// <summary>剪貼簿的文字。WPF 的剪貼簿只能在 STA 執行緒上用，所以另開一條，等它讀完。</summary>
    private static string Clipboard()
    {
        string? text = null;
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            // 別的程式正開著剪貼簿時會丟 CLIPBRD_E_CANT_OPEN，等一下再試
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
                    failed = null;
                    return;
                }
                catch (Exception e) { failed = e; Thread.Sleep(60); }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(3))) return "剪貼簿暫時讀不到";
        if (failed != null) return "剪貼簿暫時讀不到";
        return ClipboardText(text ?? "");
    }

    /// <summary>剪貼簿的文字 → 給 AI 的結果：太長就截斷並說明。</summary>
    public static string ClipboardText(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return "剪貼簿裡沒有文字";
        if (t.Length <= ClipboardLimit) return "剪貼簿的內容：\n" + t;
        // 不要切在一個字的中間（代理對）
        int cut = char.IsHighSurrogate(t[ClipboardLimit - 1]) ? ClipboardLimit - 1 : ClipboardLimit;
        return $"剪貼簿的內容（共 {t.Length} 字，只取前 {cut} 字）：\n" + t[..cut];
    }

    // ---------------------------------------------------------------- 開網站或程式

    /// <summary>
    /// 不在常用網站清單裡的網址，開之前在島上問使用者：網址 → 開（true）、取消（false），或現在沒辦法問（null，
    /// 例如島因為全螢幕藏起來、正在問 Claude Code 的權限、已經有一個網址在等）。
    /// 由程式啟動時設定；沒設定就一律當作沒辦法問。逾時的時候 token 會取消，畫面要把確認收起來。
    /// </summary>
    public static Func<string, CancellationToken, Task<bool?>>? Confirm { get; set; }

    /// <summary>使用者多久沒按就當作取消。</summary>
    public static TimeSpan Patience { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>真的去開網址或程式。測試換成記下來的假動作，不會開瀏覽器。</summary>
    public static Action<string> Launcher { get; set; } = Launch;

    /// <summary>
    /// AI 讀得到外來的文字（網頁設的歌名、匿名上傳的歌詞、剪貼簿），被注入時可能想把剪貼簿之類的內容帶在網址上送出去。
    /// 常用網站（Opener.Trusted）照舊直接開：資料頂多到那個網站本身。其他的要使用者在島上看過完整網址、按了才開。
    /// </summary>
    public static async Task<string> OpenAsync(string target)
    {
        var what = Opener.Classify(target);
        switch (what.Kind)
        {
            case OpenKind.Url:
                if (!Opener.Trusted(what.Target))
                {
                    var allowed = await AskAsync(what.Target).ConfigureAwait(false);
                    if (allowed == null) return $"這個網址要使用者自己確認才能開，現在沒辦法在島上問他，所以沒有開。請把網址原樣告訴使用者，讓他自己決定要不要複製去開：{what.Target}";
                    if (allowed == false) return "使用者沒有同意開啟這個網址（或沒有在時間內回應），網址沒有開。不要再試著開它。";
                }
                Launcher(what.Target);
                return $"已用瀏覽器開啟 {what.Target}";
            case OpenKind.App:
                try { Launcher(what.Target); }
                catch { return $"打不開{what.Name}（這台電腦可能沒有安裝）"; }
                return $"已開啟{what.Name}";
            default:
                return what.Name.Length > 0 ? what.Name : "不認得這個程式";
        }
    }

    /// <summary>在島上問；逾時當作取消。問的那一端出錯就當作沒辦法問。</summary>
    private static async Task<bool?> AskAsync(string url)
    {
        if (Confirm is not { } confirm) return null;
        using var giveUp = new CancellationTokenSource(Patience);
        try
        {
            var asked = confirm(url, giveUp.Token);
            var done = await Task.WhenAny(asked, Task.Delay(Patience)).ConfigureAwait(false);
            if (done != asked) { giveUp.Cancel(); return false; }
            return await asked.ConfigureAwait(false);
        }
        catch (Exception e) { Log.Write("open confirm: " + e.GetType().Name); return null; }
    }

    /// <summary>只會拿到白名單的固定字串或檢查過的網址，不會有使用者給的參數。</summary>
    private static void Launch(string target)
    {
        if (target == Opener.Browser) target = DefaultBrowser() ?? throw new InvalidOperationException("找不到預設瀏覽器");
        // 只有檔名的話，Windows 會先在「目前的資料夾」找：Shima 若是從下載資料夾啟動，裡面一個同名的 notepad.exe 就會被執行。
        // 所以白名單裡的執行檔一律換成它該在的完整路徑，找不到就當作沒安裝。
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(target))
            target = Opener.Locate(target, Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) ?? throw new FileNotFoundException(target);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>預設瀏覽器的執行檔：https 協定的 ProgId → 它的開啟命令裡的第一段（只取執行檔，不帶原本的參數）。</summary>
    private static string? DefaultBrowser()
    {
        using var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
        if (choice?.GetValue("ProgId") is not string progId || progId.Length == 0) return null;
        using var command = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
        return Opener.Executable(command?.GetValue(null) as string ?? "");
    }

    // ---------------------------------------------------------------- 提醒

    public static string Alarm(JsonObject arguments, Alarms alarms, DateTime now)
    {
        string Arg(string name) => arguments[name]?.ToString().Trim() ?? "";
        switch (Arg("action"))
        {
            case "add":
            {
                var repeat = Alarms.NormalizeRepeat(Arg("repeat"));
                if (Arg("repeat") is { Length: > 0 } given && repeat == null && given.ToLowerInvariant() is not ("none" or "once" or "no"))
                    return "看不懂這個重複方式（只有 daily、weekdays、mon…sun）";
                // 重複的只看幾點：同時給了 minutes 的話不要讓它搶走（When 會先用 minutes）
                var at = Alarms.When(Arg("at"), repeat != null && Arg("at").Length > 0 ? null : Number(arguments, "minutes"), now);
                if (at == null) return Arg("at").Length == 0 && Number(arguments, "minutes") == null
                    ? "沒有給時間：要幾分鐘後還是幾點？"
                    : "看不懂這個時間（只能排六十天內，幾點請用 24 小時制 HH:mm）";
                var text = Arg("text") is { Length: > 0 } words ? words : "時間到了";
                // 重複的只看幾點：第一次是現在之後第一個符合的那天
                if (repeat != null)
                {
                    if (Arg("at").Length == 0) return "重複的提醒要用 at 指定幾點（HH:mm）";
                    at = Alarms.NextAfter(at.Value, repeat, now);
                }
                var alarm = alarms.Add(at.Value, text, repeat);
                var left = at.Value - now;
                var wait = left.TotalMinutes < 60 ? $"{Math.Max(1, (int)Math.Round(left.TotalMinutes))} 分鐘後" : left.TotalHours < 24 ? $"{(int)left.TotalHours} 小時 {left.Minutes} 分後" : $"{(int)left.TotalDays} 天 {left.Hours} 小時後";
                return repeat != null
                    ? $"已設定重複提醒 #{alarm.Id}：{Alarms.RepeatName(repeat)} {at:HH:mm}，下一次 {Alarms.Describe(at.Value, now)}（{wait}）「{alarm.Text}」"
                    : $"已設定提醒 #{alarm.Id}：{Alarms.Describe(at.Value, now)}（{wait}）「{alarm.Text}」";
            }
            case "cancel":
            {
                var what = Arg("text");
                if (what.Length == 0) return "沒有說要取消哪一個";
                return alarms.Cancel(what) is { } gone ? $"已取消提醒 #{gone.Id}：{Alarms.Describe(gone, now)}「{gone.Text}」" : $"找不到「{what}」的提醒";
            }
            default:
            {
                var list = alarms.List;
                return list.Count == 0 ? "沒有排定的提醒" : string.Join("\n", list.Select(a => $"#{a.Id} {Alarms.Describe(a, now)}「{a.Text}」"));
            }
        }
    }

    // ---------------------------------------------------------------- 天氣

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Shima");
        return http;
    }

    /// <summary>Open-Meteo（不用金鑰）：先把地名換成經緯度，再查目前的天氣和今明兩天。</summary>
    private static async Task<string> WeatherAsync(string location, string country, CancellationToken stop)
    {
        if (location.Length == 0) return "沒有給地點，請先問使用者要查哪裡";
        var lang = Loc.Lang.StartsWith("zh", StringComparison.Ordinal) ? "zh" : Loc.Lang.Split('-')[0];
        Place? place = null;
        foreach (var name in Weather.Candidates(location))
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=5&language={lang}&format=json";
            if (Regex.IsMatch(country, "^[A-Za-z]{2}$")) url += "&countryCode=" + country.ToUpperInvariant();
            string json;
            try { json = await Http.GetStringAsync(url, stop).ConfigureAwait(false); }
            catch { return "天氣服務連不上"; }
            if ((place = Weather.Place(json)) != null) break;
        }
        if (place == null) return $"找不到「{location}」這個地方；請改用英文地名再試一次（例如 Taipei、Osaka）";

        var forecast = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,precipitation&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=2");
        try { return Weather.Describe(await Http.GetStringAsync(forecast, stop).ConfigureAwait(false), place.Name); }
        catch { return "天氣服務連不上"; }
    }
}

/// <summary>地名查到的地點。</summary>
public sealed record Place(string Name, double Latitude, double Longitude);

/// <summary>Open-Meteo 回應的解讀（純函式，測試用存下來的回應）。</summary>
public static class Weather
{
    /// <summary>
    /// 要試哪些寫法。Open-Meteo 的中文地名很不齊（「臺北」「台中」查不到、「台北市」查得到），
    /// 所以中文地名再試加「市」「縣」、臺／台互換。英文地名最準，工具說明會請 AI 盡量給英文。
    /// </summary>
    public static List<string> Candidates(string location)
    {
        var name = location.Trim();
        var list = new List<string> { name };
        if (name.Any(c => c is >= '一' and <= '鿿'))
        {
            var swapped = name.Contains('臺') ? name.Replace('臺', '台') : name.Replace('台', '臺');
            foreach (var n in new[] { name, swapped })
            {
                list.Add(n);
                if (!n.EndsWith('市') && !n.EndsWith('縣') && !n.EndsWith('县')) { list.Add(n + "市"); list.Add(n + "縣"); }
            }
        }
        return list.Distinct().Take(6).ToList();
    }

    /// <summary>geocoding 的回應 → 最可能的那個地點（人口最多的；都沒有人口資料就第一個）。沒有結果是 null。</summary>
    public static Place? Place(string json)
    {
        if (JsonNode.Parse(json)?["results"] is not JsonArray { Count: > 0 } results) return null;
        var best = results.OfType<JsonObject>()
            .Select((r, i) => (r, i, people: r["population"]?.GetValue<double>() ?? 0))
            .OrderByDescending(x => x.people).ThenBy(x => x.i).First().r;
        if (best["latitude"] is not { } lat || best["longitude"] is not { } lon) return null;
        var name = best["name"]?.ToString() ?? "";
        var region = best["admin1"]?.ToString() ?? "";
        var country = best["country"]?.ToString() ?? "";
        // admin1 有時是「臺灣省 or 台灣省」這種，不好唸就不放
        var where = string.Join("，", new[] { region.Contains(" or ") || region == name ? "" : region, country }.Where(s => s.Length > 0));
        var label = where.Length > 0 ? $"{name}（{where}）" : name;
        return new Place(Loc.Lang == "zh-CN" ? label : Chinese.ToTraditional(label), lat.GetValue<double>(), lon.GetValue<double>());
    }

    /// <summary>forecast 的回應 → 一段給 AI 的天氣描述。</summary>
    public static string Describe(string json, string place)
    {
        var root = JsonNode.Parse(json);
        if (root?["error"]?.GetValue<bool>() == true) return "天氣服務回應錯誤：" + root["reason"];
        var now = root?["current"];
        var daily = root?["daily"];
        if (now == null) return "天氣服務沒有給資料";
        string N(JsonNode? n, string format = "0.#") => n == null ? "?" : n.GetValue<double>().ToString(format, CultureInfo.InvariantCulture);

        var text = new StringBuilder($"{place}現在{Code(now["weather_code"]?.GetValue<int>() ?? -1)}，{N(now["temperature_2m"])}°C");
        text.Append($"（體感 {N(now["apparent_temperature"])}°C），濕度 {N(now["relative_humidity_2m"], "0")}%，風速 {N(now["wind_speed_10m"])} km/h");
        if (now["precipitation"]?.GetValue<double>() is > 0 and var rain) text.Append($"，降水 {rain.ToString("0.#", CultureInfo.InvariantCulture)} mm");
        text.Append('。');
        string[] days = ["今天", "明天"];
        for (int i = 0; i < 2 && daily?["time"] is JsonArray times && i < times.Count; i++)
        {
            text.Append($"{days[i]}{Code(daily["weather_code"]?[i]?.GetValue<int>() ?? -1)}，{N(daily["temperature_2m_min"]?[i])}～{N(daily["temperature_2m_max"]?[i])}°C");
            if (daily["precipitation_probability_max"]?[i] is { } chance) text.Append($"，降雨機率 {N(chance, "0")}%");
            text.Append('。');
        }
        return text.ToString();
    }

    /// <summary>WMO 天氣代碼 → 中文。</summary>
    public static string Code(int code) => code switch
    {
        0 => "晴朗",
        1 => "大致晴朗",
        2 => "局部多雲",
        3 => "陰天",
        45 => "有霧",
        48 => "有霧並結霜",
        51 => "小毛毛雨",
        53 => "毛毛雨",
        55 => "濃密的毛毛雨",
        56 or 57 => "凍毛毛雨",
        61 => "小雨",
        63 => "中雨",
        65 => "大雨",
        66 or 67 => "凍雨",
        71 => "小雪",
        73 => "中雪",
        75 => "大雪",
        77 => "米雪",
        80 => "短暫陣雨",
        81 => "陣雨",
        82 => "強烈陣雨",
        85 => "短暫陣雪",
        86 => "強烈陣雪",
        95 => "雷雨",
        96 or 99 => "雷雨伴隨冰雹",
        _ => "天氣不明",
    };
}

public enum OpenKind { Url, App, Refused }

/// <param name="Target">要交給系統開的東西：檢查過的網址，或白名單裡固定的執行檔名／URI。</param>
/// <param name="Name">程式的中文名稱；拒絕時是給 AI 的理由。</param>
public sealed record OpenTarget(OpenKind Kind, string Target, string Name);

/// <summary>
/// 判斷要開的是網址、白名單裡的程式，還是不允許。保守：只有 http／https 的網址和固定的幾個程式，
/// 不接受路徑、命令列參數或其他協定（file:、javascript:、ms-settings: 直接寫等等）。
/// </summary>
public static class Opener
{
    private sealed record App(string Name, string Target, string[] Aliases);

    /// <summary>
    /// 白名單：名字 → 執行檔名或 URI。這些都是 Windows 內建或由安裝程式註冊的，不帶任何參數。
    /// Roon 沒有在 PATH 也沒有協定，找不到就會說打不開。
    /// </summary>
    private static readonly App[] Apps =
    [
        new("記事本", "notepad.exe", ["記事本", "notepad", "筆記本"]),
        new("小算盤", "calc.exe", ["小算盤", "計算機", "calculator", "calc", "计算器"]),
        new("檔案總管", "explorer.exe", ["檔案總管", "檔案管理員", "file explorer", "explorer", "文件資源管理器", "资源管理器"]),
        new("設定", "ms-settings:", ["設定", "windows 設定", "系統設定", "settings", "设置"]),
        new("音效設定", "ms-settings:sound", ["音效設定", "聲音設定", "sound settings"]),
        new("藍牙設定", "ms-settings:bluetooth", ["藍牙設定", "藍牙", "bluetooth"]),
        new("Wi-Fi 設定", "ms-settings:network-wifi", ["wi-fi 設定", "wifi 設定", "wifi", "wi-fi", "網路設定", "無線網路"]),
        new("顯示設定", "ms-settings:display", ["顯示設定", "螢幕設定", "display settings"]),
        new("工作管理員", "taskmgr.exe", ["工作管理員", "task manager", "taskmgr", "任务管理器"]),
        new("終端機", "wt.exe", ["終端機", "terminal", "windows terminal", "終端"]),
        new("小畫家", "mspaint.exe", ["小畫家", "paint", "mspaint", "画图"]),
        new("剪取工具", "ms-screenclip:", ["剪取工具", "截圖工具", "截圖", "snipping tool"]),
        new("控制台", "control.exe", ["控制台", "control panel"]),
        new("瀏覽器", Browser, ["瀏覽器", "browser", "網頁瀏覽器", "浏览器"]),
        new("Discord", "discord:", ["discord"]),
        new("Spotify", "spotify:", ["spotify"]),
        new("Roon", "roon.exe", ["roon"]),
        new("Steam", "steam:", ["steam"]),
        new("Microsoft Store", "ms-windows-store:", ["microsoft store", "store", "微軟商店", "市集"]),
    ];

    /// <summary>
    /// 白名單裡的執行檔該在哪裡：系統程式在 System32 或 Windows 資料夾，終端機與 Roon 在使用者的 LocalAppData 底下。
    /// 只在這幾個固定的位置找，不看目前的資料夾也不看 PATH；都沒有就是 null。
    /// </summary>
    public static string? Locate(string exe, string system, string windows, string local)
    {
        string[] places = exe.ToLowerInvariant() switch
        {
            "explorer.exe" => [windows],
            "wt.exe" => [Path.Combine(local, "Microsoft", "WindowsApps")],
            "roon.exe" => [Path.Combine(local, "Roon", "Application")],
            _ => [system],
        };
        return places.Select(p => Path.Combine(p, exe)).FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 本機或區域網路的位址。AI 讀得到剪貼簿與歌詞，裡面如果藏著「幫我開 http://localhost:…」這種話，
    /// 不該真的用使用者已經登入的瀏覽器去開本機的服務或路由器的管理頁。
    /// 只看字面：127.0.0.1.nip.io、localtest.me 這種會解析到本機的名稱擋不到，但它們不在常用網站裡（Trusted），
    /// 會落到「要使用者確認」，確認時看得到完整的網址。
    /// </summary>
    public static bool Local(Uri uri)
    {
        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (uri.IsLoopback || host == "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal") || !host.Contains('.') && !host.Contains(':')) return true;
        if (!System.Net.IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return ip.Equals(System.Net.IPAddress.IPv6Any);
        return b[0] is 0 or 10 or 127 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254 || b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    /// <summary>
    /// 常用網站：這些主機與它們的子網域直接開。挑的是「資料送到這裡也只會到那個網站本身」的大網站；
    /// 縮網址（t.co、goo.gl、b23.tv…；youtu.be 只會轉到 YouTube，算在內）、任何人都能架頁面的主機（github.io、blogspot、*.booth.pm）、
    /// 能收表單的（Google 表單在 docs.google.com）一律不放，落到「要使用者確認」。
    /// </summary>
    private static readonly string[] Domains =
    [
        // 搜尋、百科、字典、翻譯
        "bing.com", "duckduckgo.com", "baidu.com", "yahoo.co.jp", "yahoo.com", "wikipedia.org", "wiktionary.org",
        "weblio.jp", "jisho.org", "deepl.com",
        // 影音、串流
        "youtube.com", "youtu.be", "bilibili.com", "nicovideo.jp", "twitch.tv", "netflix.com", "abema.tv", "crunchyroll.com", "tver.jp",
        // 音樂
        "spotify.com", "apple.com", "music.163.com", "y.qq.com", "kugou.com", "kkbox.com", "last.fm", "roonlabs.com",
        "recochoku.jp", "mora.jp", "uta-net.com", "utaten.com", "j-lyric.net", "vgmdb.net",
        // 動畫、插畫、LoveLive
        "pixiv.net", "myanimelist.net", "anilist.co", "bangumi.tv", "bgm.tv", "lovelive-anime.jp",
        // 社群、購物、開發
        "x.com", "twitter.com", "amazon.com", "amazon.co.jp", "github.com",
        // 地圖
        "openstreetmap.org",
    ];

    /// <summary>
    /// 只有這幾個主機本身，不含子網域：google.com 底下有 docs（表單用網址就能送出）、sites、script 這種任何人都能放東西的主機。
    /// </summary>
    private static readonly string[] Hosts =
    [
        "google.com", "www.google.com", "maps.google.com", "translate.google.com", "news.google.com", "images.google.com",
        "google.co.jp", "www.google.co.jp", "google.com.tw", "www.google.com.tw", "animestore.docomo.ne.jp",
    ];

    /// <summary>常見的轉址路徑（第一段）：google.com/url?q=…、bing.com/ck/a?…、youtube.com/redirect?q=…、pixiv.net/jump.php?…</summary>
    private static readonly string[] Redirects = ["url", "amp", "redirect", "link", "l", "ck", "out", "away", "jump", "goto", "interstitial"];

    /// <summary>
    /// 這個網址可以不問使用者就開：主機（照 IDN 轉成 punycode、小寫、去掉結尾的點）是常用網站或它的子網域、
    /// 沒有帳號密碼、用預設的埠號、不是 IP 位址，也不像是轉址（路徑是常見的轉址、或網址裡又夾了一個網址）。
    /// 長得像的（google.com.evil.example、evil-google.com、gооgle.com 用西里爾字母）都不算。
    /// 其他的都要使用者在島上看過完整網址、按了才開。
    /// </summary>
    public static bool Trusted(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        if (uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns) return false;
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (!Hosts.Contains(host) && !Domains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal))) return false;
        var first = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (Redirects.Contains(first.Split('.')[0].ToLowerInvariant())) return false;
        // 解開跳脫（最多三層，%252F 這種）之後還有「//」或反斜線：網址裡夾了另一個網址
        var rest = uri.PathAndQuery + uri.Fragment;
        for (int i = 0; i < 3; i++)
        {
            string next;
            try { next = Uri.UnescapeDataString(rest); } catch { return false; }
            if (next == rest) break;
            rest = next;
        }
        return !rest.Contains("//", StringComparison.Ordinal) && !rest.Contains('\\');
    }

    /// <summary>代表「預設瀏覽器」：開的時候才從登錄檔查是哪一個。</summary>
    public const string Browser = "default-browser";

    /// <summary>白名單裡的程式名稱（給工具說明用）。</summary>
    public static IEnumerable<string> Names => Apps.Select(a => a.Name);

    private static readonly string[] Programs = ["exe", "bat", "cmd", "ps1", "vbs", "js", "msi", "lnk", "scr", "reg", "hta", "jar", "dll", "sys", "cpl"];

    /// <summary>登錄檔裡的開啟命令（"C:\...\chrome.exe" --single-argument %1）→ 只有執行檔的路徑。</summary>
    public static string? Executable(string command)
    {
        var c = command.Trim();
        var path = c.StartsWith('"') ? c[1..].Split('"')[0] : Regex.Match(c, @"^.*?\.exe", RegexOptions.IgnoreCase).Value;
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    public static OpenTarget Classify(string input)
    {
        var raw = input.Trim();
        if (raw.Length == 0) return new(OpenKind.Refused, "", "沒有說要開什麼");
        if (raw.Length > 2048) return new(OpenKind.Refused, "", "網址太長");
        var text = raw.Normalize(NormalizationForm.FormKC);

        // 先看是不是白名單的名字（整個字串就是名字，不能多帶東西）
        var key = Regex.Replace(text.ToLowerInvariant(), @"\s+", " ");
        if (key.EndsWith(".exe", StringComparison.Ordinal)) key = key[..^4];
        foreach (var app in Apps)
            if (app.Aliases.Any(a => a == key) || app.Name.ToLowerInvariant() == key)
                return new(OpenKind.App, app.Target, app.Name);

        // 空白、引號、反斜線、控制字元：可能是路徑或命令列，一律不接受
        if (text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\'' or '`' or '\\' or '|' or '<' or '>' or '^' or '{' or '}'))
            return new(OpenKind.Refused, "", "不認得這個程式");

        // 有協定（scheme:）的只收 http、https；沒有的要長得像網域才自動補 https://
        var scheme = Regex.Match(text, @"^([A-Za-z][A-Za-z0-9+.\-]*):");
        // 「example.com:8080/x」的冒號後面是連接埠，不是協定
        bool port = Regex.IsMatch(text, @"^[A-Za-z0-9.\-]+\.[A-Za-z]{2,24}:\d{1,5}(?:[/?#]|$)");
        string candidate;
        if (scheme.Success && !port)
        {
            var name = scheme.Groups[1].Value.ToLowerInvariant();
            if (name is not ("http" or "https")) return new(OpenKind.Refused, "", "只能開 http 或 https 的網址");
            candidate = text;
        }
        else
        {
            var host = text.Split('/', '?', '#')[0];
            var tld = host.Split(':')[0].Split('.')[^1].ToLowerInvariant();
            bool domain = Regex.IsMatch(host, @"^(?:[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}(?::\d{1,5})?$");
            if (!domain || Programs.Contains(tld)) return new(OpenKind.Refused, "", "不認得這個程式");
            candidate = "https://" + text;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.IsUnc || uri.IsFile)
            return new(OpenKind.Refused, "", "網址格式不對");
        if (Local(uri)) return new(OpenKind.Refused, "", "不開這台電腦或區域網路裡的網址");
        return new(OpenKind.Url, uri.AbsoluteUri, "");
    }
}

/// <summary>
/// 小小的計算機：加減乘除、括號、次方（^ 或 **，右結合）、百分比（後綴 %，等於除以 100）。
/// 自己寫的遞迴下降解析，不會執行任何程式碼。
/// </summary>
public static class Calculator
{
    private const int Deepest = 200;   // 每層括號大約用掉 6 層，約可以 30 層括號

    public static bool TryEvaluate(string expression, out double value, out string problem)
    {
        value = 0;
        problem = "";
        if (expression.Length > 300) { problem = "算式太長"; return false; }
        var text = expression.Normalize(NormalizationForm.FormKC)
            .Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace("**", "^")
            .Replace("（", "(").Replace("）", ")");
        // 千分位的逗號拿掉：1,000 → 1000
        text = Regex.Replace(text, @"(?<=\d),(?=\d{3}(?!\d))", "");
        var parser = new Parser(text);
        try
        {
            value = parser.Expression(0);
            parser.SkipSpace();
            if (!parser.End) throw new FormatException($"看不懂「{parser.Rest}」");
            if (!double.IsFinite(value)) throw new FormatException("結果太大或不是數字");
            return true;
        }
        catch (FormatException e) { problem = e.Message; return false; }
        catch (DivideByZeroException) { problem = "不能除以零"; return false; }
    }

    /// <summary>結果的寫法：最多十位小數、不用科學記號（太大或太小才用）。</summary>
    public static string Format(double value)
    {
        if (value == 0) return "0";
        double abs = Math.Abs(value);
        if (abs >= 1e15 || abs < 1e-10) return value.ToString("G10", CultureInfo.InvariantCulture);
        var rounded = Math.Round(value, 10);
        return rounded.ToString("#,0.##########", CultureInfo.InvariantCulture);
    }

    private sealed class Parser(string text)
    {
        private int _at;

        public bool End => _at >= text.Length;
        public string Rest => text[_at..];

        public void SkipSpace() { while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++; }

        private bool Take(char c)
        {
            SkipSpace();
            if (_at < text.Length && text[_at] == c) { _at++; return true; }
            return false;
        }

        private static void Guard(int depth) { if (depth > Deepest) throw new FormatException("括號太多層"); }

        // 加減
        public double Expression(int depth)
        {
            Guard(depth);
            double value = Term(depth + 1);
            while (true)
            {
                if (Take('+')) value += Term(depth + 1);
                else if (Take('-')) value -= Term(depth + 1);
                else return value;
            }
        }

        // 乘除
        private double Term(int depth)
        {
            double value = Unary(depth + 1);
            while (true)
            {
                if (Take('*')) value *= Unary(depth + 1);
                else if (Take('/'))
                {
                    double by = Unary(depth + 1);
                    if (by == 0) throw new DivideByZeroException();
                    value /= by;
                }
                else return value;
            }
        }

        // 正負號比次方鬆：-2^2 = -(2^2) = -4
        private double Unary(int depth)
        {
            Guard(depth);
            if (Take('-')) return -Unary(depth + 1);
            if (Take('+')) return Unary(depth + 1);
            return Power(depth + 1);
        }

        // 次方，右結合：2^3^2 = 2^9；指數可以帶正負號：2^-1
        private double Power(int depth)
        {
            double value = Postfix(depth + 1);
            if (Take('^')) value = Math.Pow(value, Unary(depth + 1));
            return value;
        }

        // 百分比：50% = 0.5
        private double Postfix(int depth)
        {
            double value = Primary(depth + 1);
            while (Take('%')) value /= 100;
            return value;
        }

        private double Primary(int depth)
        {
            Guard(depth);
            if (Take('('))
            {
                double value = Expression(depth + 1);
                if (!Take(')')) throw new FormatException("括號沒有成對");
                return value;
            }
            SkipSpace();
            int start = _at;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || text[_at] == '.')) _at++;
            // 科學記號：1e5、2.5E-3
            if (_at > start && _at < text.Length && text[_at] is 'e' or 'E')
            {
                int back = _at++;
                if (_at < text.Length && text[_at] is '+' or '-') _at++;
                int digits = _at;
                while (_at < text.Length && char.IsAsciiDigit(text[_at])) _at++;
                if (_at == digits) _at = back;
            }
            if (_at == start) throw new FormatException(End ? "算式不完整" : $"看不懂「{Rest}」");
            if (!double.TryParse(text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw new FormatException($"看不懂「{text[start.._at]}」");
            return number;
        }
    }
}

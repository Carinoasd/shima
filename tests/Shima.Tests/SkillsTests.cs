// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class SkillToolTests
{
    [Fact]
    public void 新工具都登記了_兩條路都會帶到()
    {
        var body = OpenAiBrain.Body("m", "s", [], tools: true);
        var names = ((JsonArray)body["tools"]!).Select(t => t!["function"]!["name"]!.ToString()).ToList();
        foreach (var name in Skills.Names) Assert.Contains(name, names);
        // Claude 的工具清單也是從 AssistantTool.All 產生的
        Assert.Equal(Skills.Names, AssistantTool.All.Select(t => t.Name).TakeLast(Skills.Names.Length));
    }

    [Fact]
    public void 每個工具的必填參數都有定義_名稱不重複()
    {
        foreach (var tool in AssistantTool.All.Where(t => Skills.Names.Contains(t.Name)))
        {
            Assert.True(tool.Description.Length > 20, tool.Name);
            foreach (var required in tool.Required) Assert.True(tool.Properties.ContainsKey(required), $"{tool.Name}.{required}");
            foreach (var (name, property) in tool.Properties) Assert.False(string.IsNullOrEmpty(property!["description"]?.ToString()), $"{tool.Name}.{name}");
        }
        Assert.Equal(AssistantTool.All.Length, AssistantTool.All.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public async Task 不認得的工具回傳null()
    {
        Assert.Null(await Skills.RunAsync("music", new JsonObject { ["action"] = "play" }));
        Assert.Null(Skills.Run("todo", []));
    }

    [Fact]
    public async Task 統一入口_計算與時間()
    {
        Assert.Equal("1+2*3 = 7", await Skills.RunAsync("calc", new JsonObject { ["expression"] = "1+2*3" }));
        Assert.StartsWith("現在是 ", await Skills.RunAsync("datetime", []));
        Assert.Equal("沒有給地點，請先問使用者要查哪裡", await Skills.RunAsync("weather", []));
    }

    [Fact]
    public void 現在時間的寫法()
    {
        var now = new DateTimeOffset(2026, 11, 23, 15, 5, 0, TimeSpan.FromHours(8));
        Assert.Equal("現在是 2026 年 11 月 23 日 星期一 15:05（UTC+8）", Skills.Now(now));
        Assert.EndsWith("（UTC+05:30）", Skills.Now(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.FromMinutes(330))));
        Assert.Contains("星期日", Skills.Now(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void 剪貼簿太長會截斷()
    {
        Assert.Equal("剪貼簿裡沒有文字", Skills.ClipboardText("  \n"));
        Assert.Equal("剪貼簿的內容：\nhello", Skills.ClipboardText(" hello "));
        var longText = new string('字', 5000);
        var result = Skills.ClipboardText(longText);
        Assert.StartsWith("剪貼簿的內容（共 5000 字，只取前 4000 字）", result);
        Assert.Equal(4000, result.Count(c => c == '字') - 2);   // 說明裡也有兩個「字」
    }

    [Fact]
    public void 剪貼簿截斷不會切開代理對()
    {
        var text = new string('a', Skills.ClipboardLimit - 1) + "😀" + "bbb";
        var result = Skills.ClipboardText(text);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }
}

public class OpenerTests
{
    [Theory]
    [InlineData("https://www.youtube.com", "https://www.youtube.com/")]
    [InlineData("http://example.com/a?b=1", "http://example.com/a?b=1")]
    [InlineData("youtube.com", "https://youtube.com/")]
    [InlineData("www.google.com/search?q=shima", "https://www.google.com/search?q=shima")]
    [InlineData("HTTPS://Example.COM", "https://example.com/")]
    [InlineData("example.com:8080/x", "https://example.com:8080/x")]
    [InlineData("example.com", "https://example.com/")]
    public void 網址會開_自動補https(string input, string expected)
    {
        var r = Opener.Classify(input);
        Assert.Equal(OpenKind.Url, r.Kind);
        Assert.Equal(expected, r.Target);
    }

    [Theory]
    [InlineData("記事本", "notepad.exe")]
    [InlineData("notepad", "notepad.exe")]
    [InlineData("Notepad.exe", "notepad.exe")]
    [InlineData("小算盤", "calc.exe")]
    [InlineData("計算機", "calc.exe")]
    [InlineData("檔案總管", "explorer.exe")]
    [InlineData("設定", "ms-settings:")]
    [InlineData("工作管理員", "taskmgr.exe")]
    [InlineData("終端機", "wt.exe")]
    [InlineData("Task Manager", "taskmgr.exe")]
    [InlineData("  Discord ", "discord:")]
    [InlineData("Spotify", "spotify:")]
    [InlineData("roon", "roon.exe")]
    [InlineData("瀏覽器", Opener.Browser)]
    public void 白名單的程式會開(string input, string target)
    {
        var r = Opener.Classify(input);
        Assert.Equal(OpenKind.App, r.Kind);
        Assert.Equal(target, r.Target);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox")]
    [InlineData("ms-settings:privacy")]
    [InlineData("spotify:track:123")]
    [InlineData("notepad C:\\secret.txt")]
    [InlineData("notepad.exe C:\\secret.txt")]
    [InlineData("記事本 /p a.txt")]
    [InlineData("cmd /c del *")]
    [InlineData("cmd")]
    [InlineData("cmd.exe")]
    [InlineData("powershell -enc AAAA")]
    [InlineData("powershell")]
    [InlineData("\\\\server\\share\\evil.exe")]
    [InlineData("//server/share/evil.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData("calc.exe&whoami")]
    [InlineData("evil.exe")]
    [InlineData("setup.msi")]
    [InlineData("run.bat")]
    [InlineData("https://")]
    [InlineData("http:///path")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://example.com\" --flag")]
    [InlineData("https://exa mple.com")]
    [InlineData("https://example.com/\n--x")]
    [InlineData("example")]
    [InlineData("localhost")]
    [InlineData("ftp://example.com")]
    [InlineData("shell:startup")]
    [InlineData("%windir%\\system32\\calc.exe")]
    [InlineData("..\\..\\x.exe")]
    [InlineData("calc|notepad")]
    public void 不允許的一律拒絕(string input)
    {
        Assert.Equal(OpenKind.Refused, Opener.Classify(input).Kind);
    }

    [Fact]
    public void 開的是檢查過的網址_不是原本的字串()
    {
        var r = Opener.Classify("https://example.com/a b");
        Assert.Equal(OpenKind.Refused, r.Kind);
        r = Opener.Classify("https://example.com/%22quoted%22");
        Assert.Equal(OpenKind.Url, r.Kind);
        Assert.DoesNotContain("\"", r.Target);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --single-argument %1", "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")]
    [InlineData("C:\\Browser\\firefox.exe -osint -url \"%1\"", "C:\\Browser\\firefox.exe")]
    [InlineData("rundll32 url.dll,FileProtocolHandler %1", null)]
    [InlineData("", null)]
    public void 預設瀏覽器只取執行檔(string command, string? expected)
    {
        Assert.Equal(expected, Opener.Executable(command));
    }
}

public class CalculatorTests
{
    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("10/4", 2.5)]
    [InlineData("2^10", 1024)]
    [InlineData("2**3", 8)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("(-2)^2", 4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("50%", 0.5)]
    [InlineData("200*15%", 30)]
    [InlineData("3 × 4 ÷ 2", 6)]
    [InlineData("（1＋2）×３", 9)]
    [InlineData("1,000+234", 1234)]
    [InlineData("--3", 3)]
    [InlineData("1.5e3", 1500)]
    [InlineData("  7  ", 7)]
    [InlineData("10-2-3", 5)]
    [InlineData("100/10/5", 2)]
    public void 算得對(string expression, double expected)
    {
        Assert.True(Calculator.TryEvaluate(expression, out var value, out var problem), problem);
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("1/0", "不能除以零")]
    [InlineData("1+", "算式不完整")]
    [InlineData("(1+2", "括號沒有成對")]
    [InlineData("1+2)", "看不懂")]
    [InlineData("2(3)", "看不懂")]
    [InlineData("abc", "看不懂")]
    [InlineData("Math.Pow(2,3)", "看不懂")]
    [InlineData("System.IO.File.Delete(\"x\")", "看不懂")]
    [InlineData("10^400", "結果太大")]
    [InlineData("1..2", "看不懂")]
    public void 算不出來會說原因(string expression, string problem)
    {
        Assert.False(Calculator.TryEvaluate(expression, out _, out var why));
        Assert.Contains(problem, why);
    }

    [Fact]
    public void 太多層括號不會把堆疊撐爆()
    {
        var deep = new string('(', 5000) + "1" + new string(')', 5000);
        Assert.False(Calculator.TryEvaluate(deep, out _, out _));
        var minus = new string('-', 290) + "1";
        Assert.False(Calculator.TryEvaluate(minus, out _, out var why));
        Assert.Equal("括號太多層", why);
        Assert.True(Calculator.TryEvaluate("((((((1+1))))))", out var ok, out _));
        Assert.Equal(2, ok);
    }

    [Theory]
    [InlineData(1234567.5, "1,234,567.5")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(1e20, "1E+20")]
    [InlineData(0, "0")]
    public void 結果的寫法(double value, string text)
    {
        Assert.Equal(text, Calculator.Format(value));
    }

    [Fact]
    public void 給AI的結果()
    {
        Assert.Equal("2^10 = 1,024", Skills.Calculate("2^10"));
        Assert.Equal("算不出來：不能除以零", Skills.Calculate("5/0"));
        Assert.Equal("沒有給算式", Skills.Calculate(""));
    }
}

public class WeatherTests
{
    // 2026-11-23 實際打 Open-Meteo 拿到的回應（台北）
    private const string Forecast = """
        {"latitude":25.06151,"longitude":121.5194,"generationtime_ms":0.24,"utc_offset_seconds":28800,"timezone":"Asia/Taipei","timezone_abbreviation":"GMT+8","elevation":6.0,
         "current_units":{"time":"iso8601","interval":"seconds","temperature_2m":"°C","apparent_temperature":"°C","relative_humidity_2m":"%","weather_code":"wmo code","wind_speed_10m":"km/h","precipitation":"mm"},
         "current":{"time":"2026-11-23T10:00","interval":900,"temperature_2m":24.9,"apparent_temperature":24.8,"relative_humidity_2m":54,"weather_code":1,"wind_speed_10m":12.4,"precipitation":0.00},
         "daily_units":{"time":"iso8601","weather_code":"wmo code","temperature_2m_max":"°C","temperature_2m_min":"°C","precipitation_probability_max":"%"},
         "daily":{"time":["2026-11-23","2026-11-24"],"weather_code":[3,53],"temperature_2m_max":[26.7,18.9],"temperature_2m_min":[16.8,16.0],"precipitation_probability_max":[6,86]}}
        """;

    private const string Geocoding = """
        {"results":[
          {"id":1,"name":"斯普林菲尔德","latitude":39.80,"longitude":-89.64,"country_code":"US","country":"美国","admin1":"伊利诺伊州","population":114394},
          {"id":2,"name":"斯普林菲尔德","latitude":37.21533,"longitude":-93.29824,"country_code":"US","country":"美国","admin1":"密苏里州","population":170188},
          {"id":3,"name":"Springfield","latitude":36.5,"longitude":-86.9,"country_code":"US","country":"美国","admin1":"田纳西州"}
        ],"generationtime_ms":0.2}
        """;

    [Fact]
    public void 天氣描述()
    {
        var text = Weather.Describe(Forecast, "台北市");
        Assert.Equal("台北市現在大致晴朗，24.9°C（體感 24.8°C），濕度 54%，風速 12.4 km/h。今天陰天，16.8～26.7°C，降雨機率 6%。明天毛毛雨，16～18.9°C，降雨機率 86%。", text);
    }

    [Fact]
    public void 服務回錯誤或沒有資料()
    {
        Assert.Equal("天氣服務回應錯誤：Latitude must be in range of -90 to 90°. Given: 999.0.",
            Weather.Describe("""{"error":true,"reason":"Latitude must be in range of -90 to 90°. Given: 999.0."}""", "x"));
        Assert.Equal("天氣服務沒有給資料", Weather.Describe("{}", "x"));
    }

    [Fact]
    public void 地名挑人口最多的_簡體轉繁體()
    {
        var place = Weather.Place(Geocoding)!;
        Assert.Equal(37.21533, place.Latitude);
        Assert.Equal(-93.29824, place.Longitude);
        Assert.Equal("斯普林菲爾德（密蘇里州，美國）", place.Name);
    }

    [Fact]
    public void 查不到地名是null()
    {
        Assert.Null(Weather.Place("""{"generationtime_ms":0.1}"""));
        Assert.Null(Weather.Place("""{"results":[]}"""));
    }

    [Fact]
    public void 怪怪的省名不放()
    {
        var place = Weather.Place("""{"results":[{"name":"台北市","latitude":25.05,"longitude":121.52,"country":"台湾","admin1":"臺灣省 or 台灣省","population":7871900}]}""")!;
        Assert.Equal("臺北市（臺灣）", place.Name);
    }

    [Fact]
    public void 中文地名多試幾種寫法()
    {
        var names = Weather.Candidates("臺北");
        Assert.Equal("臺北", names[0]);
        Assert.Contains("台北市", names);
        Assert.Contains("臺北市", names);
        Assert.Equal(new[] { "Taipei" }, Weather.Candidates(" Taipei "));
        Assert.Equal("高雄市", Weather.Candidates("高雄市")[0]);
        Assert.DoesNotContain("高雄市市", Weather.Candidates("高雄市"));
    }

    [Theory]
    [InlineData(0, "晴朗")]
    [InlineData(2, "局部多雲")]
    [InlineData(45, "有霧")]
    [InlineData(63, "中雨")]
    [InlineData(81, "陣雨")]
    [InlineData(95, "雷雨")]
    [InlineData(99, "雷雨伴隨冰雹")]
    [InlineData(1234, "天氣不明")]
    public void 天氣代碼(int code, string text)
    {
        Assert.Equal(text, Weather.Code(code));
    }
}

public class AlarmTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 15, 0, 0);

    /// <summary>測試用的存檔放在測試程式旁邊的資料夾，不碰使用者的設定資料夾。</summary>
    private static string NewFile() => Path.Combine(AppContext.BaseDirectory, "alarm-tests", Guid.NewGuid().ToString("N") + ".json");

    [Theory]
    [InlineData(null, 10.0, "2026-11-23 15:10")]
    [InlineData(null, 0.5, "2026-11-23 15:00:30")]
    [InlineData("16:30", null, "2026-11-23 16:30")]
    [InlineData("07:30", null, "2026-11-24 07:30")]   // 今天已經過了 → 明天
    [InlineData("15:00", null, "2026-11-24 15:00")]   // 就是現在 → 明天
    [InlineData("7:05", null, "2026-11-24 07:05")]
    [InlineData("明天 08:00", null, "2026-11-24 08:00")]
    [InlineData("明天 16:00", null, "2026-11-24 16:00")]
    [InlineData("tomorrow 9:00", null, "2026-11-24 09:00")]
    [InlineData("後天 9:00", null, "2026-11-25 09:00")]
    [InlineData("2026-11-25 07:30", null, "2026-11-25 07:30")]
    [InlineData("2026-11-25T07:30", null, "2026-11-25 07:30")]
    [InlineData("7點半", null, "2026-11-24 07:30")]
    [InlineData("下午3點20分", null, "2026-11-23 15:20")]
    [InlineData("下午2點", null, "2026-11-24 14:00")]
    [InlineData("下午4點", null, "2026-11-23 16:00")]
    [InlineData("晚上8點", null, "2026-11-23 20:00")]
    [InlineData("中午12點", null, "2026-11-24 12:00")]
    [InlineData("凌晨12點", null, "2026-11-24 00:00")]
    [InlineData("5pm", null, "2026-11-23 17:00")]
    [InlineData("１９：０５", null, "2026-11-23 19:05")]
    public void 解析提醒時間(string? at, double? minutes, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Alarms.When(at, minutes, Now));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("7", null)]
    [InlineData("25:00", null)]
    [InlineData("12:60", null)]
    [InlineData("等一下", null)]
    [InlineData("2026-11-22 07:30", null)]   // 過去
    [InlineData("2026-12-25 07:30", null)]   // 超過一星期
    [InlineData(null, 0.0)]
    [InlineData(null, -5.0)]
    [InlineData(null, 20000.0)]
    public void 看不懂或不合理的時間是null(string? at, double? minutes)
    {
        Assert.Null(Alarms.When(at, minutes, Now));
    }

    [Fact]
    public void 分鐘數優先於時間()
    {
        Assert.Equal(Now.AddMinutes(5), Alarms.When("23:00", 5, Now));
    }

    [Fact]
    public void 有給時間時分鐘數是0當作沒給()
    {
        // 有些模型會把沒用到的數字參數填 0
        Assert.Equal(new DateTime(2026, 11, 24, 7, 30, 0), Alarms.When("07:30", 0, Now));
        using var alarms = new Alarms(NewFile());
        Assert.StartsWith("已設定提醒 #1：明天 07:30", Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "07:30", ["minutes"] = 0, ["text"] = "起床" }, alarms, Now));
    }

    [Fact]
    public void 到點才觸發_觸發後移除()
    {
        using var alarms = new Alarms(NewFile());
        var fired = new List<(Alarm, bool)>();
        alarms.Due += (a, late) => fired.Add((a, late));
        alarms.Add(Now.AddMinutes(10), "關火");
        alarms.Add(Now.AddMinutes(5), "喝水");

        Assert.Empty(alarms.Check(Now.AddMinutes(4)));
        Assert.Equal("喝水", Assert.Single(alarms.Check(Now.AddMinutes(5))).Text);
        Assert.Single(fired);
        Assert.False(fired[0].Item2);
        Assert.Empty(alarms.Check(Now.AddMinutes(6)));
        Assert.Equal("關火", Assert.Single(alarms.List).Text);
        alarms.Check(Now.AddMinutes(30));
        Assert.True(fired[1].Item2);   // 晚了二十分鐘才發現：錯過的
        Assert.Empty(alarms.List);
    }

    [Fact]
    public void 重開程式後還在()
    {
        var file = NewFile();
        using (var alarms = new Alarms(file))
        {
            alarms.Add(Now.AddHours(1), "開會");
            alarms.Add(Now.AddHours(2), "倒垃圾");
        }
        using var again = new Alarms(file);
        Assert.Equal(new[] { "開會", "倒垃圾" }, again.List.Select(a => a.Text));
        Assert.Equal(Now.AddHours(1), again.List[0].At);
        // 編號接著用
        Assert.Equal(3, again.Add(Now.AddHours(3), "睡覺").Id);
    }

    [Fact]
    public void 壞掉的存檔當作沒有()
    {
        Assert.Empty(Alarms.Parse("not json"));
        Assert.Empty(Alarms.Parse(""));
        var json = Alarms.Serialize([new Alarm(1, Now, "關火")]);
        Assert.Contains("關火", json);
        Assert.Equal(new Alarm(1, Now, "關火"), Assert.Single(Alarms.Parse(json)));
    }

    [Fact]
    public void 取消用編號或關鍵字()
    {
        using var alarms = new Alarms(NewFile());
        alarms.Add(Now.AddMinutes(10), "關火");
        alarms.Add(Now.AddMinutes(20), "拿衣服");
        Assert.Equal("拿衣服", alarms.Cancel("#2")!.Text);
        Assert.Null(alarms.Cancel("倒垃圾"));
        Assert.Equal("關火", alarms.Cancel("火")!.Text);
        Assert.Empty(alarms.List);
    }

    [Fact]
    public void 給AI的提醒結果()
    {
        using var alarms = new Alarms(NewFile());
        Assert.Equal("沒有排定的提醒", Skills.Alarm(new JsonObject { ["action"] = "list" }, alarms, Now));
        Assert.Equal("已設定提醒 #1：今天 15:10（10 分鐘後）「關火」",
            Skills.Alarm(new JsonObject { ["action"] = "add", ["minutes"] = 10, ["text"] = "關火" }, alarms, Now));
        Assert.Equal("已設定提醒 #2：明天 07:30（16 小時 30 分後）「起床」",
            Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "07:30", ["text"] = "起床" }, alarms, Now));
        Assert.Equal("沒有給時間：要幾分鐘後還是幾點？", Skills.Alarm(new JsonObject { ["action"] = "add", ["text"] = "x" }, alarms, Now));
        Assert.StartsWith("看不懂這個時間", Skills.Alarm(new JsonObject { ["action"] = "add", ["at"] = "等等" }, alarms, Now));
        Assert.Equal("#1 今天 15:10「關火」\n#2 明天 07:30「起床」", Skills.Alarm(new JsonObject { ["action"] = "list" }, alarms, Now));
        Assert.Equal("已取消提醒 #1：今天 15:10「關火」", Skills.Alarm(new JsonObject { ["action"] = "cancel", ["text"] = "1" }, alarms, Now));
        Assert.Equal("找不到「洗碗」的提醒", Skills.Alarm(new JsonObject { ["action"] = "cancel", ["text"] = "洗碗" }, alarms, Now));
    }
}

public class WakeWordTests
{
    private static readonly string[] Words = WakeGate.DefaultWords;
    private static readonly DateTime T0 = new(2026, 11, 23, 12, 0, 0);

    [Theory]
    [InlineData("Shima")]
    [InlineData("shima.")]
    [InlineData("Shima!")]
    [InlineData("SHIMA")]
    [InlineData("Ｓｈｉｍａ")]
    [InlineData("シマ")]
    [InlineData("しま")]
    [InlineData("シマ。")]
    [InlineData("島")]
    [InlineData("岛")]
    [InlineData("希瑪")]
    [InlineData("希玛")]
    [InlineData("西瑪？")]
    [InlineData("Hey Shima")]
    [InlineData("hey, shima")]
    [InlineData("嘿，希瑪")]
    [InlineData("ねえ、シマ")]
    [InlineData("シマちゃん")]
    [InlineData("Shima醬")]
    public void 喚醒詞的各種寫法_整段就是喚醒詞(string heard)
    {
        Assert.True(WakeGate.Matches(heard, Words, whole: true));
        Assert.True(WakeGate.Matches(heard, Words, whole: false));
    }

    [Theory]
    [InlineData("Shima, play the next song")]
    [InlineData("OK Shima what time is it")]
    [InlineData("shima 下一首")]
    public void 沒放音樂時英文喚醒詞可以在句子裡(string heard)
    {
        Assert.True(WakeGate.Matches(heard, Words, whole: false));
        Assert.False(WakeGate.Matches(heard, Words, whole: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("します")]
    [InlineData("よろしくお願いします")]
    [InlineData("しました")]
    [InlineData("しまった")]
    [InlineData("島の歌")]
    [InlineData("我想去小島")]
    [InlineData("這個島很漂亮")]
    [InlineData("希瑪拉雅")]
    [InlineData("Hiroshima")]
    [InlineData("shimano")]
    [InlineData("hi")]
    [InlineData("嘿")]
    [InlineData("謝謝")]
    [InlineData("Thank you")]
    public void 不是喚醒詞(string heard)
    {
        Assert.False(WakeGate.Matches(heard, Words, whole: false));
        Assert.False(WakeGate.Matches(heard, Words, whole: true));
    }

    [Fact]
    public void 自訂喚醒詞()
    {
        var words = WakeGate.Parse("小島, Jarvis\n賈維斯");
        Assert.Equal(new[] { "小島", "Jarvis", "賈維斯" }, words);
        Assert.True(WakeGate.Matches("jarvis", words, whole: true));
        Assert.True(WakeGate.Matches("嘿，賈維斯。", words, whole: true));
        Assert.False(WakeGate.Matches("Shima", words, whole: true));
        Assert.Same(WakeGate.DefaultWords, WakeGate.Parse(" , ，"));
    }

    [Fact]
    public void 正規化()
    {
        Assert.Equal("hey shima", WakeGate.Normalize("  Hey,   SHIMA!! "));
        Assert.Equal("しま", WakeGate.Normalize("シマ"));
        Assert.Equal("島", WakeGate.Normalize("岛"));
    }

    [Fact]
    public void 長度不對的不拿去辨識()
    {
        var gate = new WakeGate();
        Assert.False(gate.Worth(TimeSpan.FromSeconds(0.1), music: false, T0));
        Assert.True(gate.Worth(TimeSpan.FromSeconds(0.6), music: false, T0));
        Assert.True(gate.Worth(TimeSpan.FromSeconds(2.4), music: false, T0));
        Assert.False(gate.Worth(TimeSpan.FromSeconds(3), music: false, T0));
        // 放音樂時只收短的
        Assert.True(gate.Worth(TimeSpan.FromSeconds(0.8), music: true, T0));
        Assert.False(gate.Worth(TimeSpan.FromSeconds(2), music: true, T0));
    }

    [Fact]
    public void 放音樂時要整段都是喚醒詞()
    {
        var gate = new WakeGate();
        Assert.False(gate.Accept("Shima, next song", music: true, T0, Words));
        Assert.True(gate.Accept("Shima", music: true, T0.AddSeconds(1), Words));
        Assert.True(new WakeGate().Accept("Shima, next song", music: false, T0, Words));
    }

    [Fact]
    public void 聽到之後有冷卻時間()
    {
        var gate = new WakeGate();
        Assert.True(gate.Accept("Shima", false, T0, Words));
        Assert.False(gate.Worth(TimeSpan.FromSeconds(1), false, T0.AddSeconds(2)));
        Assert.False(gate.Accept("Shima", false, T0.AddSeconds(2), Words));
        Assert.True(gate.Accept("Shima", false, T0 + WakeGate.AfterHeard, Words));
    }

    [Fact]
    public void 剛接回麥克風先不聽()
    {
        var gate = new WakeGate();
        gate.Resumed(T0);
        Assert.False(gate.Worth(TimeSpan.FromSeconds(1), false, T0.AddSeconds(1)));
        Assert.True(gate.Worth(TimeSpan.FromSeconds(1), false, T0 + WakeGate.AfterResume));
    }

    [Fact]
    public void 一直誤觸就休息一下()
    {
        var gate = new WakeGate();
        var t = T0;
        for (int i = 0; i < WakeGate.MissesBeforeRest; i++, t = t.AddSeconds(5)) Assert.False(gate.Accept("今天天氣不錯", false, t, Words));
        Assert.False(gate.Worth(TimeSpan.FromSeconds(1), false, t));
        Assert.False(gate.Accept("Shima", false, t, Words));
        Assert.True(gate.Worth(TimeSpan.FromSeconds(1), false, t + WakeGate.Rest));
    }

    [Fact]
    public void 誤觸分散在很久以前不算()
    {
        var gate = new WakeGate();
        var t = T0;
        for (int i = 0; i < WakeGate.MissesBeforeRest * 2; i++, t = t.AddSeconds(20)) gate.Accept("今天天氣不錯", false, t, Words);
        Assert.True(gate.Worth(TimeSpan.FromSeconds(1), false, t));
    }

    [Fact]
    public void 什麼都沒聽出來不算誤觸()
    {
        var gate = new WakeGate();
        for (int i = 0; i < WakeGate.MissesBeforeRest * 2; i++) gate.Accept("", false, T0.AddSeconds(i), Words);
        Assert.True(gate.Worth(TimeSpan.FromSeconds(1), false, T0.AddSeconds(30)));
    }

    [Fact]
    public void 語音長度不含前後的餘裕()
    {
        var detector = new SpeechDetector { Silence = TimeSpan.FromSeconds(0.45) };
        var quiet = new float[Ears.Rate / 2];
        var loud = Enumerable.Range(0, Ears.Rate).Select(i => 0.2f * MathF.Sin(i * 0.3f)).ToArray();
        Assert.Equal(TimeSpan.Zero, detector.Spoken);
        detector.Feed(quiet);
        detector.Feed(loud);
        Assert.True(detector.Feed(new float[Ears.Rate]));
        Assert.InRange(detector.Spoken.TotalSeconds, 0.95, 1.05);
        Assert.True(detector.Speech().Length / (double)Ears.Rate > 1.2);
    }

    [Fact]
    public void 門檻調高後小聲的不算有人說話()
    {
        var soft = Enumerable.Range(0, Ears.Rate).Select(i => 0.025f * MathF.Sin(i * 0.3f)).ToArray();
        var normal = new SpeechDetector();
        normal.Feed(new float[Ears.Rate / 2]);
        normal.Feed(soft);
        Assert.True(normal.Spoken > TimeSpan.Zero);
        var strict = new SpeechDetector { Floor = WakeGate.MusicFloor };
        strict.Feed(new float[Ears.Rate / 2]);
        strict.Feed(soft);
        Assert.Equal(TimeSpan.Zero, strict.Spoken);
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shima;

/// <summary>%APPDATA%\Shima\settings.json</summary>
public sealed class AppSettings
{
    // 要排在 Current 前面：static 欄位照寫的順序初始化，而第一次啟動（還沒有設定檔）時 Load() 就會存檔、用到這把鎖。
    // 排在後面的話那時它還是 null，整個類別初始化失敗，全新安裝的人一啟動就當掉（0.6.1 到 0.7.0）。
    private static readonly object Saving = new();

    public static AppSettings Current { get; } = Load();

    /// <summary>Roon Core 的位址；留空就不連 Roon。</summary>
    public string RoonHost { get; set; } = "127.0.0.1";
    public int RoonPort { get; set; } = 9330;
    /// <summary>Roon 用這個識別碼認擴充功能；token 跟著它走，換了就要在 Roon 重新啟用。</summary>
    public string RoonExtensionId { get; set; } = "com.carinoasd.shima";
    /// <summary>每個 Roon Core 在「設定 → 擴充功能」啟用 Shima 後發給的 token。</summary>
    public Dictionary<string, string> RoonTokens { get; set; } = [];
    /// <summary>最後在播的 Roon 播放區域。</summary>
    public string RoonZone { get; set; } = "";

    /// <summary>歌詞來源，依序查；同一版本的歌詞先找到的先用。</summary>
    public List<string> LyricsProviders { get; set; } = ["local", "lrclib", "netease", "qq", "kugou"];
    /// <summary>設定檔的版本；升級時用來補上新加的東西。</summary>
    public int Version { get; set; }
    /// <summary>放 .lrc 檔的資料夾（含子資料夾），例如音樂庫本身。</summary>
    public List<string> LyricsFolders { get; set; } = [];

    /// <summary>Discord 應用程式的 Application ID（名稱就是「正在聽 ___」顯示的字）；留空就不顯示狀態。</summary>
    public string DiscordClientId { get; set; } = "";
    /// <summary>到 iTunes Search 查封面給 Discord 用（歌名、藝人、專輯會送到 Apple）。</summary>
    public bool DiscordCover { get; set; } = true;

    /// <summary>暫存架上的檔案（只記位置）。</summary>
    public List<string> Shelf { get; set; } = [];
    public List<TodoItem> Todos { get; set; } = [];
    /// <summary>番茄鐘：專注與休息各幾分鐘。</summary>
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;

    /// <summary>手動選過的歌詞來源、調過的時間（毫秒，正的是提早），以歌為單位記住。</summary>
    public Dictionary<string, string> LyricsPick { get; set; } = [];
    public Dictionary<string, int> LyricsShift { get; set; } = [];

    /// <summary>拖過之後島的位置：中心的 X 與上緣的 Y（null：主螢幕頂端正中間）。</summary>
    public double? IslandX { get; set; }
    public double? IslandY { get; set; }

    /// <summary>把通訊軟體的通知顯示在島上（要在 Windows 允許通知存取）。</summary>
    public bool Notifications { get; set; }
    /// <summary>哪些程式的通知要顯示：程式名稱裡有這些字就算。</summary>
    public List<string> NotificationApps { get; set; } = ["Discord", "LINE", "Telegram", "Messenger", "WhatsApp", "Slack", "Teams", "Signal"];

    /// <summary>介面語言（Loc.Languages 的代碼）；空字串：跟著 Windows。</summary>
    public string Language { get; set; } = "";

    /// <summary>喝水提醒：每隔幾分鐘提醒一次。</summary>
    public bool Water { get; set; }
    public int WaterMinutes { get; set; } = 60;
    /// <summary>音量、亮度、電量、網路、播放裝置有變化時在島上顯示。</summary>
    public bool SystemStatus { get; set; } = true;
    /// <summary>啟動後與每天一次，到 GitHub 看有沒有新版。</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>全域快捷鍵（如 "Ctrl+Alt+Space"）；空字串：用預設（展開有備援順序）。</summary>
    public string HotkeyToggle { get; set; } = "";
    public string HotkeyAsk { get; set; } = "";
    /// <summary>勿擾模式的結束時間；null 或已過就是沒開。</summary>
    public DateTime? DoNotDisturbUntil { get; set; }

    /// <summary>歌詞下面多一行：translation（中文翻譯）、romaji（羅馬拼音）或 none。</summary>
    public string LyricsSecondLine { get; set; } = "translation";

    public bool HideInFullscreen { get; set; } = true;

    /// <summary>聊天分頁開哪個網站：claude、gemini、chatgpt。</summary>
    public string ChatSite { get; set; } = "claude";

    /// <summary>語音助手聽與說的語言。</summary>
    /// <remarks>空字串：跟著介面語言。</remarks>
    public string VoiceLanguage { get; set; } = "";
    /// <summary>語音辨識用哪一種：whisper（在這台電腦上辨識）或 windows（Windows 的聽寫，要開線上語音辨識）。</summary>
    public string SpeechEngine { get; set; } = "whisper";
    /// <summary>本機語音辨識的模型：tiny（75 MB）、base（150 MB）、small（470 MB）、medium（1.5 GB）。越大越準也越慢。</summary>
    public string WhisperModel { get; set; } = "base";

    /// <summary>
    /// 按一次麥克風就一直對話到再按一次為止。預設關閉：問一句答一句就結束，
    /// 因為喇叭在放音樂時一直開著麥克風，它會把歌聲當成你在講話而自己回答。
    /// </summary>
    public bool VoiceContinuous { get; set; }

    /// <summary>語音助手在聽的時候先把音樂暫停（不然麥克風會收到歌聲），問完再繼續播。</summary>
    public bool PauseWhileListening { get; set; } = true;

    /// <summary>問問題用哪一家的 AI：AiService.All 裡的代號。</summary>
    public string AiService { get; set; } = "claude";
    /// <summary>每一家的 API 金鑰，用 Windows 的 DPAPI 加密後存放（只有這個 Windows 帳號解得開）。</summary>
    public Dictionary<string, string> AiKeys { get; set; } = [];
    /// <summary>每一家選的模型。</summary>
    public Dictionary<string, string> AiModels { get; set; } = [];
    /// <summary>選「自訂」時的 API 位址（OpenAI 相容，結尾到 /v1）。</summary>
    public string AiCustomUrl { get; set; } = "";
    /// <remarks>舊版只有 Claude 的金鑰；讀進來後會搬到 AiKeys。</remarks>
    public string AnthropicKey { get; set; } = "";

    /// <summary>膠囊裡的小角色：claude、eyes、cat、ghost 或 none。</summary>
    public string Character { get; set; } = "claude";
    /// <summary>沒有歌在播時膠囊顯示什麼：clock（時間）、date（日期與時間）、alarm（下一個提醒）、none（不顯示）。</summary>
    public string IdleShows { get; set; } = "clock";
    /// <summary>`Character` 是 `custom` 時用的圖片（GIF 或水平排列的 PNG 精靈圖）。</summary>
    public string CharacterImage { get; set; } = "";
    /// <summary>島的底色有多不透明（0.55–1）。</summary>
    public double Opacity { get; set; } = 1;
    /// <summary>整座島的大小（0.9–1.3，1 是原本的大小）。讀進來不合理的值（NaN、太小、太大）當成 1。</summary>
    /// <remarks>設定檔裡被手改成 "NaN" 這種字也要讀得進來：不然整個設定檔讀不了，所有設定都會回到預設。</remarks>
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public double Scale { get => _scale; set => _scale = IslandScale.Sanitize(value); }
    private double _scale = 1;
    /// <summary>掃光與進度條的顏色：`cover`（取自封面）、`Accent.Presets` 裡的 id（`white`、`clay`…）、`custom`。</summary>
    public string AccentMode { get; set; } = "cover";
    /// <summary>`AccentMode` 是 `custom` 時用的色碼（`#RRGGBB`）。換回別的顏色時留著，下次點自訂還在。</summary>
    public string AccentCustom { get; set; } = "";
    /// <summary>膠囊收著時底部那條細細的進度線。</summary>
    public bool PillProgress { get; set; }
    /// <summary>音樂分頁的歌詞區顯示幾行：3、5、7。</summary>
    public int LyricsLines { get; set; } = 5;
    /// <summary>桌面歌詞：一條浮在桌面上的歌詞，全螢幕時也看得到。</summary>
    public bool DesktopLyrics { get; set; }
    public bool DesktopLyricsLocked { get; set; } = true;
    public double? DesktopLyricsX { get; set; }
    public double? DesktopLyricsY { get; set; }
    public double DesktopLyricsSize { get; set; } = 30;
    /// <summary>島因為全螢幕藏起來時，換歌從螢幕邊緣滑出一張小卡片。</summary>
    public bool FullscreenToast { get; set; } = true;
    /// <summary>Roon 的搜尋。打開後 Roon 會要求到「設定 → 擴充功能」重新啟用 Shima 一次。</summary>
    public bool RoonSearch { get; set; }
    /// <summary>記下聽了什麼（存在這台電腦的 history.jsonl）。</summary>
    public bool History { get; set; } = true;
    /// <summary>ListenBrainz 的 user token；留空就不回報。</summary>
    public string ListenBrainzToken { get; set; } = "";
    /// <summary>Last.fm：使用者自己申請的 API key（不算機密）。</summary>
    public string LastFmApiKey { get; set; } = "";
    /// <summary>Last.fm 的 shared secret，DPAPI 加密後的 base64；用 <see cref="LastFmSecretText"/> 讀、<see cref="SetLastFmSecret"/> 寫。</summary>
    public string LastFmSecret { get; set; } = "";
    /// <summary>授權後換到的 session key，同上加密；空的就是沒連接。</summary>
    public string LastFmSession { get; set; } = "";
    /// <summary>連接的 Last.fm 使用者名稱，只用來顯示。</summary>
    public string LastFmUser { get; set; } = "";
    /// <summary>喚醒詞：說「Shima」就開始聽。預設關閉（麥克風會一直開著）。</summary>
    public bool WakeWord { get; set; }
    /// <summary>第一次啟動的引導看過了沒。</summary>
    public bool Welcomed { get; set; }
    /// <summary>擷取電腦播出的聲音，讓角色和音量條跟著跳。</summary>
    public bool AudioReactive { get; set; } = true;

    /// <summary>解密後的金鑰；沒存的話用那一家慣用的環境變數（ANTHROPIC_API_KEY、OPENAI_API_KEY…）。</summary>
    public string ApiKey(string service)
    {
        try
        {
            if (AiKeys.GetValueOrDefault(service, "") is { Length: > 0 } stored)
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) { Log.Write("api key: " + e.Message); }
        var variable = Agents.AiService.Of(service).Id == service ? Agents.AiService.Of(service).Variable : "";
        return variable.Length > 0 ? Environment.GetEnvironmentVariable(variable) ?? "" : "";
    }

    /// <summary>有沒有存這一家的金鑰（環境變數的不算）。</summary>
    public bool HasKey(string service) => AiKeys.GetValueOrDefault(service, "").Length > 0;

    public void SetApiKey(string service, string key)
    {
        if (key.Length == 0) AiKeys.Remove(service);
        else AiKeys[service] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        Save();
    }

    public string LastFmSecretText() => Reveal(LastFmSecret);
    public string LastFmSessionText() => Reveal(LastFmSession);
    public void SetLastFmSecret(string secret) { LastFmSecret = Conceal(secret); Save(); }
    public void SetLastFmSession(string session, string user) { LastFmSession = Conceal(session); LastFmUser = session.Length > 0 ? user : ""; Save(); }

    private static string Conceal(string text) =>
        text.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));

    private static string Reveal(string stored)
    {
        try { return stored.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) { Log.Write("lastfm 金鑰解密: " + e.Message); return ""; }
    }

    /// <summary>設了這個環境變數就把設定與資料放在那個資料夾（測試用暫存資料夾，不碰平常的那一份）。</summary>
    public const string FolderVariable = "SHIMA_DATA";

    /// <summary>資料夾被 <see cref="FolderVariable"/> 換掉了：這一份用的金鑰不是平常那一把，不要拿去更新別的程式裡的設定。</summary>
    public static bool Redirected => Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 };

    public static string Folder => Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } custom ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shima");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                if (loaded.AnthropicKey.Length > 0)
                {
                    // 舊版的 Claude 金鑰搬到新的位置（已經是加密過的，直接搬）
                    loaded.AiKeys.TryAdd("claude", loaded.AnthropicKey);
                    loaded.AnthropicKey = "";
                    loaded.Save();
                }
                if (loaded.Version < 2)
                {
                    // 版本 2 新增 QQ 音樂與酷狗：舊設定檔沒有它們，排到最後面
                    foreach (var name in (string[])["qq", "kugou"]) if (!loaded.LyricsProviders.Contains(name)) loaded.LyricsProviders.Add(name);
                    loaded.Version = 2;
                    loaded.Save();
                }
                return loaded;
            }
        }
        catch (Exception e) { Log.Write("settings: " + e.Message); }
        var fresh = new AppSettings { Version = 2 };
        fresh.Save();
        return fresh;
    }

    /// <summary>還原備份換掉資料夾的那段時間不寫設定檔：記憶體裡是還原前的設定，寫出去會把剛換好的蓋掉。</summary>
    public static volatile bool Frozen;

    public void Save()
    {
        if (Frozen) return;
        // 一次只讓一個存檔在跑：兩邊同時寫同一個暫存檔會撞在一起
        lock (Saving)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception e) { Log.Write("settings save: " + e.Message); }
        }
    }

    /// <summary>
    /// 從背景執行緒改設定並存檔（例如 Roon 連線那條執行緒）。設定裡的清單與字典不是給多條執行緒同時用的，
    /// 存檔時又要把它們整個走過一遍，所以一律排回畫面的執行緒去做；沒有畫面（跑測試）就直接做。
    /// </summary>
    /// <param name="change">回傳 true 表示真的改了、要存檔；沒改就不寫磁碟（Roon 每次重連都會走到這裡）。</param>
    public static void Change(Func<AppSettings, bool> change)
    {
        void Run() { if (change(Current)) Current.Save(); }
        var ui = System.Windows.Application.Current?.Dispatcher;
        if (ui == null || ui.CheckAccess()) Run(); else ui.BeginInvoke(Run);
    }

    /// <summary>設定頁的分鐘數輸入框：看得懂就夾在 [min, max]，空白或不是整數就維持原本的值。</summary>
    public static int ParseMinutes(string? text, int fallback, int min, int max) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Clamp(value, min, max) : fallback;
}

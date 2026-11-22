// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shima;

/// <summary>%APPDATA%\Shima\settings.json</summary>
public sealed class AppSettings
{
    public static AppSettings Current { get; } = Load();

    /// <summary>Roon Core 的位址；留空就不連 Roon。</summary>
    public string RoonHost { get; set; } = "127.0.0.1";
    public int RoonPort { get; set; } = 9330;
    /// <summary>Roon 用這個識別碼認擴充功能；token 跟著它走，換了就要在 Roon 重新啟用。</summary>
    public string RoonExtensionId { get; set; } = "com.carinoasd.shima";
    /// <summary>每個 Roon Core 在「設定 → 擴充功能」啟用 Shima 後發給的 token。</summary>
    public Dictionary<string, string> RoonTokens { get; set; } = [];

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

    /// <summary>歌詞下面多一行：translation（中文翻譯）、romaji（羅馬拼音）或 none。</summary>
    public string LyricsSecondLine { get; set; } = "translation";

    public bool HideInFullscreen { get; set; } = true;

    /// <summary>聊天分頁開哪個網站：claude、gemini、chatgpt。</summary>
    public string ChatSite { get; set; } = "claude";

    /// <summary>語音助手聽與說的語言。</summary>
    public string VoiceLanguage { get; set; } = "zh-TW";
    /// <summary>Anthropic API 金鑰，用 Windows 的 DPAPI 加密後存放（只有這個 Windows 帳號解得開）。</summary>
    public string AnthropicKey { get; set; } = "";

    /// <summary>膠囊裡的小角色：claude、eyes、cat、ghost 或 none。</summary>
    public string Character { get; set; } = "claude";
    /// <summary>擷取電腦播出的聲音，讓角色和音量條跟著跳。</summary>
    public bool AudioReactive { get; set; } = true;

    /// <summary>解密後的金鑰；沒存的話用環境變數 ANTHROPIC_API_KEY。</summary>
    public string ApiKey()
    {
        try
        {
            if (AnthropicKey.Length > 0)
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(AnthropicKey), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) { Log.Write("api key: " + e.Message); }
        return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    }

    public void SetApiKey(string key)
    {
        AnthropicKey = key.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        Save();
    }

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shima");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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

    public void Save()
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

# Shima

**繁體中文** · [简体中文](docs/README.zh-CN.md) · [English](docs/README.en.md) · [日本語](docs/README.ja.md) · [한국어](docs/README.ko.md) · [Español](docs/README.es.md) · [Français](docs/README.fr.md) · [Deutsch](docs/README.de.md) · [Português](docs/README.pt.md) · [Italiano](docs/README.it.md) · [Русский](docs/README.ru.md)

Windows 螢幕頂端的一座小島：顯示正在播放的音樂、同步歌詞，滑鼠移上去展開成播放面板。

![膠囊](docs/screenshots/pill.png)

<img src="docs/screenshots/music.png" width="440" alt="音樂分頁：封面背景、滾動歌詞與翻譯">

## 功能

- **音樂來源**
  - Roon：走擴充功能 API，有進度、長度、封面，可以暫停、換歌、拖進度；在音樂分頁滾滾輪調音量，點來源那一行切換播放區域
  - 其他播放器：瀏覽器裡的 YouTube、Spotify、Apple Music 等，只要會出現在 Windows 音量面板就讀得到
- **歌詞**：依序問每個來源，認得同一首歌的不同版本（`104期Ver.`、`SAKURA Ver.`），同版本優先，沒有才用原曲

  | 來源 | |
  | --- | --- |
  | `local` | 自己的 `.lrc` 檔 |
  | `lrclib` | [LRCLIB](https://lrclib.net) |
  | `netease` | 網易雲音樂 |
  | `qq` | QQ 音樂 |
  | `kugou` | 酷狗音樂 |

- **動態歌詞**：整份歌詞平滑捲動，正在唱的那句放大，光跟著唱的進度掃過去；點任何一句可以跳過去
- **歌詞校正**：抓錯時換一份來源、提早或延後 0.1 秒，每首歌各自記住（右鍵 → 這首歌的歌詞）
- **歌詞第二行**：日文歌下方多一行中文翻譯或羅馬拼音（右鍵 → 歌詞第二行）
- **小角色**：住在膠囊裡，跟著實際的聲音跳（右鍵 → 角色）。預設是向 Claude Code 吉祥物致敬的像素小 Claude，像 GIF 一樣一格一格動
- **跟著聲音動**：音量條與角色依電腦實際播出的聲音起伏，掃光與進度條的顏色取自封面
- **Discord 狀態**：在個人狀態顯示正在聽的歌、封面與進度
- **工具**：番茄鐘、檔案暫存架（把檔案拖到島上暫放，再拖出去）、待辦清單、月曆、喝水提醒
- **系統狀態**：音量、亮度、電量、網路、播放裝置（接上或拔掉耳機）有變化時在島上顯示
- **11 種語言**：繁體中文、简体中文、English、日本語、한국어、Español、Français、Deutsch、Português、Italiano、Русский；預設跟著 Windows，設定頁可以改
- **聊天**：把 Claude、Gemini、ChatGPT 的網頁版放進島裡，登入一次就好
- **Claude Code**：權限請求顯示在島上，按「允許／拒絕／回終端機」；顯示它的待辦進度（3/5 · 正在做的事），做完會通知
- **問 Claude**：在 AI 分頁打字問，或對麥克風說話讓它回答並唸出來；可以幫忙切歌；按 Ctrl+Alt+V 隨時問一句
- **訊息通知**：Discord、LINE、Telegram 等通訊軟體的通知在島上閃一下（設定頁開啟）
- **可以拖**：按住膠囊拖到任何位置、任何螢幕，會記住；右鍵可以回到正中間
- 前景是全螢幕程式（遊戲、影片）時自動隱藏

## 需求

- Windows 10 2004 以上

## 安裝

到 [Releases](../../releases) 下載 `Shima.exe`，雙擊執行。沒有程式碼簽章，Windows 第一次會跳 SmartScreen 警告，按「其他資訊 → 仍要執行」。

第一次執行後，到 Roon「設定 → 擴充功能」啟用 **Shima**。

| 快速鍵 | |
| --- | --- |
| Ctrl+Alt+Space | 展開／收起 |
| Ctrl+Alt+V | 問語音助手一句 |

系統匣的圖示可以把島藏起來或結束。展開後最右邊的齒輪是設定。

## 自己建置

需要 .NET 10 SDK。在 WSL：

```bash
./install.sh
```

會打包成單一執行檔、安裝到 `%LOCALAPPDATA%\Shima\Shima.exe`、在桌面與開始功能表建立捷徑並啟動。開發時用 `./run.sh`（不打包，比較快）；`./run.sh --smoke` 不顯示畫面只檢查能不能啟動。

## AI

展開島 → 麥克風分頁。

**Claude Code**：按「連接」會在 Claude Code 的 `settings.json`（Windows 與預設的 WSL 發行版各一份）加上四個 hook，只是用 `curl.exe` 把事件送到 Shima 開在本機的 `localhost:47813`；按「中斷」會拿掉，其他設定不動。第一次更動前會留一份 `settings.json.before-shima.bak`。Shima 沒開、或島因為全螢幕藏起來時，Claude Code 照常在終端機詢問。

**語音助手**：貼上 Anthropic API 金鑰（用 Windows DPAPI 加密保存，只有這個 Windows 帳號解得開），按麥克風開始、再按一次停止。語音辨識與朗讀用 Windows 內建的，對話內容送到 Anthropic API，按用量計費。模型是 `claude-opus-5-5`。

## 設定

`%APPDATA%\Shima\settings.json`

| 欄位 | 預設 | |
| --- | --- | --- |
| `RoonHost` / `RoonPort` | `127.0.0.1` / `9330` | Roon Core 位址；`RoonHost` 留空就不連 |
| `RoonExtensionId` | `com.carinoasd.shima` | Roon 認擴充功能用的識別碼；換了要在 Roon 重新啟用 |
| `LyricsProviders` | `["local", "lrclib", "netease", "qq", "kugou"]` | 歌詞來源與順序 |
| `LyricsFolders` | `[]` | 另外要找 `.lrc` 的資料夾（含子資料夾），例如音樂庫 |
| `HideInFullscreen` | `true` | |
| `Language` | 空 | 介面語言：`zh-TW`、`zh-CN`、`en`、`ja`、`ko`、`es`、`fr`、`de`、`pt`、`it`、`ru`；留空跟著 Windows |
| `Water` / `WaterMinutes` | `false` / `60` | 喝水提醒 |
| `SystemStatus` | `true` | 顯示音量、電量、網路等變化 |
| `Character` | `claude` | `claude`、`eyes`、`cat`、`ghost`、`none` |
| `AudioReactive` | `true` | 擷取電腦播出的聲音讓角色與音量條跟著跳 |
| `LyricsSecondLine` | `translation` | `translation`、`romaji`、`none` |
| `DiscordClientId` | 空 | Discord 應用程式的 Application ID；留空就不顯示狀態 |
| `DiscordCover` | `true` | 到 iTunes Search 查封面給 Discord 用 |
| `FocusMinutes` / `BreakMinutes` | `25` / `5` | 番茄鐘 |
| `ChatSite` | `claude` | `claude`、`gemini`、`chatgpt` |
| `VoiceLanguage` | 空 | 語音助手聽與說的語言；留空跟著介面語言 |

自己的歌詞放在 `%APPDATA%\Shima\lyrics`，檔名用 `歌名.lrc`、`藝人 - 歌名.lrc` 或 `01 歌名.lrc`。

查歌詞時，歌名與藝人會送到 lrclib.net、music.163.com、y.qq.com 與 kugou.com（看開了哪些來源）；開啟 Discord 封面時，歌名、藝人、專輯會送到 itunes.apple.com。

Discord 狀態要先到 [Developer Portal](https://discord.com/developers/applications) 建立一個應用程式（名稱就是「正在聽 ___」顯示的字），把 Application ID 填進 `DiscordClientId`。

Claude 是 Anthropic 的商標；島上的 Claude 角色是致敬之作，本專案與 Anthropic 沒有關係。

## 翻譯

介面文字在 `tools/strings.py`：一句一列，欄位依序是繁體中文與各語言。改完執行

```bash
python3 tools/strings.py
```

會寫出 `src/Shima/Assets/strings.tsv`，並檢查程式用到的每一句都有翻譯。要加語言就多一欄，再到 `Loc.Languages` 登記。其他語言的精簡版介紹由 `tools/readme.py` 產生。

## 測試

```bash
cd /mnt/c && dotnet.exe test "$(wslpath -w ~/projects/shima/tests/Shima.Tests/Shima.Tests.csproj)"
```

## 授權

MIT © 2026 Carinoasd

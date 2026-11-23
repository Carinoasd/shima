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
- **跟著聲音動**：音量條與展開時背景的頻譜是電腦實際播出的聲音（低音在左、高音在右），角色會跟著低音的拍子跳；掃光與進度條的顏色取自封面
- **歌詞卡片**：把正在唱的那句配上封面做成一張圖，放進剪貼簿並存到「圖片\Shima」（右鍵 → 複製歌詞卡片）
- **Discord 狀態**：在個人狀態顯示正在聽的歌、封面與進度
- **工具**：番茄鐘、檔案暫存架（把檔案拖到島上暫放，再拖出去）、待辦清單、月曆、喝水提醒
- **系統狀態**：音量、亮度、電量、網路、播放裝置（接上或拔掉耳機）有變化時在島上顯示
- **11 種語言**：繁體中文、简体中文、English、日本語、한국어、Español、Français、Deutsch、Português、Italiano、Русский；預設跟著 Windows，設定頁可以改
- **聊天**：把 Claude、Gemini、ChatGPT 的網頁版放進島裡，登入一次就好
- **Claude Code**：權限請求顯示在島上，按「允許／拒絕／回終端機」；顯示它的待辦進度（3/5 · 正在做的事），做完會通知
- **問 AI**：在 AI 分頁打字問，或對麥克風說話讓它回答並唸出來；可以幫忙切歌；按 Ctrl+Alt+V 隨時問一句。可以接 Claude、OpenAI、Gemini、DeepSeek、xAI、Mistral、Groq、OpenRouter、本機的 Ollama，或任何 OpenAI 相容的服務
- **訊息通知**：Discord、LINE、Telegram 等通訊軟體的通知在島上閃一下（設定頁開啟）
- **Stream Dock**：把島搬到 Stream Dock 的觸控長條或按鍵上：封面、唱到哪一句、小 Claude、進度；按一下暫停或換歌（設定頁開啟）
- **可以拖**：按住膠囊拖到任何位置、任何螢幕，會記住；右鍵可以回到正中間
- 前景是全螢幕程式（遊戲、影片）時自動隱藏

## 需求

- Windows 10 2004 以上

## 安裝

到 [Releases](../../releases) 下載 `Shima.exe`，雙擊執行。沒有程式碼簽章，Windows 第一次會跳 SmartScreen 警告，按「其他資訊 → 仍要執行」。

第一次執行後，到 Roon「設定 → 擴充功能」啟用 **Shima**。

| 快速鍵 | |
| --- | --- |
| Ctrl+Alt+Space | 展開／收起（被別的程式佔用時依序改用 Ctrl+Alt+I、Ctrl+Shift+Space，啟動時會提示） |
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

**問 AI**：先選服務，貼上那一家的 API 金鑰（用 Windows DPAPI 加密保存，只有這個 Windows 帳號解得開），再按模型的按鈕從那一家實際提供的清單裡選一個。之後在輸入框打字，或按麥克風用說的：問一句，它回答完就結束；它反過來問你問題時會再聽一次。對話內容送到你選的那一家，費用由那一家計算。

語音辨識預設在你的電腦上用 [Whisper](https://github.com/ggerganov/whisper.cpp) 進行：不用開任何 Windows 設定、不用裝語言套件，聲音不會離開這台電腦，中文、日文、英文等都認得。第一次使用會下載模型（約 150 MB）到 `%LOCALAPPDATA%\Shima\models`，並在執行檔旁邊建立 `runtimes` 資料夾放辨識用的函式庫。想改用 Windows 的聽寫可以在設定頁切換，那需要先在 Windows 開啟「線上語音辨識」並安裝該語言的語音套件。朗讀用 Windows 內建的語音。

聆聽時會先暫停音樂（不然麥克風會收到歌聲），問完再繼續。

| 服務 | 接法 |
| --- | --- |
| Claude | 官方 Anthropic SDK，預設模型 `claude-haiku-5-5`（便宜、回應快；要更聰明可以在選單換成 Sonnet 或 Opus） |
| OpenAI、Gemini、DeepSeek、xAI、Mistral、Groq、OpenRouter | OpenAI 相容的 Chat Completions |
| Ollama | 同上，連本機的 `localhost:11434`，不用金鑰 |
| 自訂 | 同上，自己填 API 網址（結尾到 `/v1`） |

沒填金鑰時會讀那一家慣用的環境變數（`ANTHROPIC_API_KEY`、`OPENAI_API_KEY`、`GEMINI_API_KEY`…）。不支援工具呼叫的模型仍然可以聊天，只是沒辦法幫忙切歌。

## Stream Dock

給 HotSpot／Mirabox 的 Stream Dock（N4 Pro 等）。電腦上裝了 Stream Dock 軟體，Shima 的設定頁才會出現「Stream Dock」開關；打開後外掛會放進 `%APPDATA%\HotSpot\StreamDock\plugins\com.carinoasd.shima.sdPlugin`，重新開啟 Stream Dock 就能在動作清單找到 **Shima**。關掉開關會把外掛資料夾刪掉。

| 按鍵與長條 | 顯示 | 按下 |
| --- | --- | --- |
| Lyrics | 拖到觸控長條：封面、唱到哪一句與第二行、小 Claude、進度；沒有歌的時候是時鐘與番茄鐘。拖到按鍵：目前這句歌詞 | 播放／暫停 |
| Now Playing | 封面 | 播放／暫停 |
| Big Cover | 放在相鄰的幾顆按鍵上，拼成一張大封面 | 播放／暫停 |
| Previous／Next | | 上一首／下一首 |
| Ask AI | 麥克風，在聽的時候變紅 | 問語音助手一句 |
| Allow／Deny | 勾與叉，Claude Code 在等權限時才亮 | 允許／拒絕 |

| 旋鈕 | 轉 | 按 | 上方那一格 |
| --- | --- | --- | --- |
| Volume | 音量（Roon 在播就調 Roon，否則調電腦） | 靜音 | 一圈音量環 |
| Seek | 前後 5 秒 | 播放／暫停 | 會轉的黑膠唱片與進度環 |
| Lyrics Timing | 歌詞提早／延後 0.1 秒 | 播放／暫停 | 正在唱的那句與偏移量 |
| Roon Zone | 換播放區域 | 換播放區域 | 小 Claude 與區域名稱 |

四格的背景是同一張模糊封面，底下的頻譜是電腦實際播出的聲音（低音在左、高音在右），長條的背景也是。

長條會跟著島插播：Claude Code 要權限時顯示工具與內容，右邊兩顆按鈕是允許、拒絕；語音助手在聽與回答、Claude Code 的進度、訊息通知、喝水提醒也會顯示。

外掛只跟 Shima 開在本機的 `localhost:47813` 說話，用的 token 在安裝時寫進外掛資料夾。Shima 讀得到的播放器（Roon、Spotify、瀏覽器…）都會顯示。長條的版面只在 N4 Pro 上看過。

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
| `CheckUpdates` | `true` | 啟動後與每天一次到 GitHub 看有沒有新版；有的話在島上提示，系統匣選單多一項「更新到 x」，按下會下載並換成新版 |
| `Character` | `claude` | `claude`、`eyes`、`cat`、`ghost`、`none` |
| `AudioReactive` | `true` | 擷取電腦播出的聲音讓角色與音量條跟著跳 |
| `LyricsSecondLine` | `translation` | `translation`、`romaji`、`none` |
| `DiscordClientId` | 空 | Discord 應用程式的 Application ID；留空就不顯示狀態 |
| `DiscordCover` | `true` | 到 iTunes Search 查封面給 Discord 用 |
| `FocusMinutes` / `BreakMinutes` | `25` / `5` | 番茄鐘 |
| `ChatSite` | `claude` | `claude`、`gemini`、`chatgpt` |
| `SpeechEngine` | `whisper` | 語音辨識：`whisper`（在這台電腦上辨識）或 `windows`（Windows 的聽寫） |
| `WhisperModel` | `base` | 本機辨識的模型：`tiny`、`base`、`small`、`medium`；越大越準也越慢 |
| `PauseWhileListening` | `true` | 語音助手在聽的時候先暫停音樂，問完再繼續 |
| `VoiceContinuous` | `false` | 按一次麥克風就一直對話到再按一次。預設是問一句答一句；喇叭在放音樂時不建議開，麥克風會把歌聲當成有人講話 |
| `AiService` | `claude` | 問 AI 用哪一家：`claude`、`openai`、`gemini`、`deepseek`、`xai`、`mistral`、`groq`、`openrouter`、`ollama`、`custom` |
| `AiModels` | `{}` | 每一家選的模型 |
| `AiCustomUrl` | 空 | 選 `custom` 時的 API 網址 |
| `VoiceLanguage` | 空 | 語音助手聽與說的語言；留空跟著介面語言 |

自己的歌詞放在 `%APPDATA%\Shima\lyrics`，檔名用 `歌名.lrc`、`藝人 - 歌名.lrc` 或 `01 歌名.lrc`。

檢查新版時只會向 api.github.com 詢問這個專案最新的版本號，不帶任何資料。查歌詞時，歌名與藝人會送到 lrclib.net、music.163.com、y.qq.com 與 kugou.com（看開了哪些來源）；開啟 Discord 封面時，歌名、藝人、專輯會送到 itunes.apple.com。

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

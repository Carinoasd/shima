# Shima

[繁體中文](../README.md) · [简体中文](README.zh-CN.md) · **English** · [日本語](README.ja.md) · [한국어](README.ko.md) · [Español](README.es.md) · [Français](README.fr.md) · [Deutsch](README.de.md) · [Português](README.pt.md) · [Italiano](README.it.md) · [Русский](README.ru.md)

A small island at the top of your Windows screen: it shows what is playing with synced lyrics, and opens into a playback panel when you hover over it.

![The pill](screenshots/pill.png)

<img src="screenshots/music.png" width="440" alt="Music tab: cover-art backdrop, scrolling lyrics and a translation line">

## Features

- **Music sources**
  - Roon: through its extension API, with position, length and cover art; pause, skip and seek. Scroll the wheel on the music tab to change volume, click the source line to switch zones
  - Other players: YouTube in a browser, Spotify, Apple Music and anything else that appears in the Windows volume flyout
- **Lyrics**: each source is asked in turn. Versions of the same song (`104期Ver.`, `SAKURA Ver.`) are told apart: the same version comes first, the plain song only when there is nothing else

  | Source | |
  | --- | --- |
  | `local` | your own `.lrc` files |
  | `lrclib` | [LRCLIB](https://lrclib.net) |
  | `netease` | NetEase Cloud Music |
  | `qq` | QQ Music |
  | `kugou` | Kugou |

- **Live lyrics**: the whole text scrolls smoothly, the line being sung is larger and a light runs across it as it is sung; click any line to jump there
- **Fixing lyrics**: when the wrong text was found, switch to another source or shift it 0.1 s earlier or later; remembered per song (right-click → Lyrics for this song)
- **Second line**: a Chinese translation or romaji under Japanese songs (right-click → Second lyrics line)
- **Character**: lives in the pill and moves to the actual sound (right-click → Character). The default is a pixel Claude, a homage to the Claude Code mascot, animated frame by frame like a GIF
- **Reacts to sound**: the level bars and the character follow what your computer is really playing; the light and the progress bar take their colour from the cover
- **Discord status**: the song, cover and progress on your profile
- **Tools**: pomodoro timer, file shelf (drop files on the island, drag them out later), to-do list, calendar, water reminder
- **System status**: volume, brightness, battery, network and output device (headphones plugged in or out) shown on the island when they change
- **11 languages**: 繁體中文, 简体中文, English, 日本語, 한국어, Español, Français, Deutsch, Português, Italiano, Русский. Follows Windows by default; change it in Settings
- **Chat**: the web versions of Claude, Gemini and ChatGPT inside the island; sign in once
- **Claude Code**: permission requests appear on the island with Allow / Deny / In terminal; its task progress is shown (3/5 · what it is doing) and you are told when it finishes
- **Ask an AI**: type on the AI tab, or speak and have the answer read aloud; it can skip songs for you; Ctrl+Alt+V asks one question from anywhere. Works with Claude, OpenAI, Gemini, DeepSeek, xAI, Mistral, Groq, OpenRouter, a local Ollama, or any OpenAI-compatible service
- **Message notifications**: Discord, LINE, Telegram and other messengers flash on the island (turn it on in Settings)
- **Lyric card**: the line being sung, set against the cover, as an image on the clipboard and in `Pictures\Shima` (right-click → Copy lyric card)
- **Updates**: checks GitHub for a new release after startup and once a day; a tray menu item downloads it and swaps it in (`CheckUpdates`, on by default; only the latest version number of this project is requested from api.github.com)
- **Stream Dock**: puts the island on a Stream Dock touch strip or keys: cover, the line being sung, little Claude, progress; press to pause or skip (turn it on in Settings)
- **Draggable**: hold the pill and drag it anywhere, on any monitor; the place is remembered. Right-click to send it back to the centre
- Hides itself while a full-screen app (a game, a video) is in front

## Requirements

- Windows 10 2004 or later

## Install

Download `Shima.exe` from [Releases](../../../releases) and run it. It is not code-signed, so Windows shows a SmartScreen warning the first time: choose "More info → Run anyway".

After the first start, enable **Shima** in Roon under Settings → Extensions.

| Shortcut | |
| --- | --- |
| Ctrl+Alt+Space | Open / close (falls back to Ctrl+Alt+I, then Ctrl+Shift+Space, when another program owns it; a toast at startup tells you) |
| Ctrl+Alt+V | Ask the voice assistant one question |

The tray icon hides the island or quits. The gear at the right of the open panel is Settings.

## Building it yourself

You need the .NET 10 SDK. From WSL:

```bash
./install.sh
```

This publishes a single executable, installs it to `%LOCALAPPDATA%\Shima\Shima.exe`, creates shortcuts on the desktop and in the Start menu, and starts it. While developing use `./run.sh` (no packaging, faster); `./run.sh --smoke` starts without showing anything, just to check that it runs.

## AI

Open the island → the microphone tab.

**Claude Code**: Connect adds four hooks to Claude Code's `settings.json` (one for Windows, one for the default WSL distribution). They only use `curl.exe` to send events to `localhost:47813`, which Shima listens on. Disconnect removes them and leaves everything else alone. Before the first change a copy is kept as `settings.json.before-shima.bak`. When Shima is not running, or the island is hidden behind a full-screen app, Claude Code asks in the terminal as usual.

**Ask an AI**: pick a service, paste that provider's API key (stored encrypted with Windows DPAPI, readable only by your Windows account), then press the model button and choose from the list that provider actually offers. After that, type in the box, or press the microphone and speak: one question, and it stops after answering; it listens once more only when it asks you something back. The conversation goes to the provider you chose and is billed by them.

Speech is recognised on your own computer with [Whisper](https://github.com/ggerganov/whisper.cpp) by default: no Windows setting to turn on, no language pack to install, the audio never leaves the machine, and Chinese, Japanese, English and many more are understood. The first use downloads a model (about 150 MB) to `%LOCALAPPDATA%\Shima\models` and creates a `runtimes` folder next to the executable for the recognition libraries. Windows dictation can be chosen in Settings instead; it needs "Online speech recognition" turned on in Windows and the speech pack for your language. Answers are read aloud with the voices built into Windows.

The music is paused while it listens (otherwise the microphone hears the song) and resumes afterwards.

| Service | How it is reached |
| --- | --- |
| Claude | the official Anthropic SDK; default model `claude-haiku-5-5` (cheap and quick; pick Sonnet or Opus from the menu for more capability) |
| OpenAI, Gemini, DeepSeek, xAI, Mistral, Groq, OpenRouter | OpenAI-compatible Chat Completions |
| Ollama | the same, at `localhost:11434` on your machine; no key |
| Custom | the same, with an API URL you enter (ending in `/v1`) |

With no key entered, the provider's usual environment variable is read (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`…). Models without tool calling still chat; they just cannot skip songs for you.

## Stream Dock

For HotSpot / Mirabox Stream Dock devices (N4 Pro and others). The "Stream Dock" switch only shows up in Shima's settings when the Stream Dock software is installed; turning it on puts the plugin in `%APPDATA%\HotSpot\StreamDock\plugins\com.carinoasd.shima.sdPlugin`. Restart Stream Dock and **Shima** appears in the action list. Turning the switch off deletes the plugin folder.

| Keys and strip | Shows | Press |
| --- | --- | --- |
| Lyrics | On the touch strip: cover, the line being sung and its second line, little Claude, progress; a clock and the pomodoro when nothing is playing. On a key: the current line | Play / pause |
| Now Playing | Cover | Play / pause |
| Big Cover | Put it on several adjacent keys and they tile into one large cover | Play / pause |
| Previous / Next | | Previous / next track |
| Ask AI | A microphone, red while listening | Ask the voice assistant once |
| Allow / Deny | A tick and a cross, lit only while Claude Code waits for permission | Allow / deny |

| Knob | Turn | Press | The cell above it |
| --- | --- | --- | --- |
| Volume | Volume (Roon's when Roon is playing, the PC's otherwise) | Mute | A volume ring |
| Seek | 5 seconds back or forward | Play / pause | A spinning record with a progress ring |
| Lyrics Timing | Lyrics 0.1 s earlier or later | Play / pause | The line being sung and the offset |
| Roon Zone | Next zone | Next zone | Little Claude and the zone name |

The four cells share one blurred cover as their backdrop; the spectrum along the bottom is the sound the PC is actually playing (bass on the left, treble on the right). The strip has it too.

The strip follows the island: when Claude Code asks for permission it shows the tool and its input with allow and deny buttons on the right; the voice assistant, Claude Code progress, message notifications and the water reminder show up too.

The plugin only talks to Shima on `localhost:47813`, with a token written into the plugin folder at install time. Any player Shima can read (Roon, Spotify, browsers…) shows up. The strip layout has only been checked on an N4 Pro.

## Settings

`%APPDATA%\Shima\settings.json`

| Field | Default | |
| --- | --- | --- |
| `RoonHost` / `RoonPort` | `127.0.0.1` / `9330` | Address of the Roon Core; leave `RoonHost` empty not to connect |
| `RoonExtensionId` | `com.carinoasd.shima` | The id Roon knows the extension by; changing it means enabling it in Roon again |
| `LyricsProviders` | `["local", "lrclib", "netease", "qq", "kugou"]` | Lyrics sources and their order |
| `LyricsFolders` | `[]` | More folders to look for `.lrc` files in (with subfolders), such as your music library |
| `HideInFullscreen` | `true` | |
| `Language` | empty | `zh-TW`, `zh-CN`, `en`, `ja`, `ko`, `es`, `fr`, `de`, `pt`, `it`, `ru`; empty follows Windows |
| `Water` / `WaterMinutes` | `false` / `60` | Water reminder |
| `SystemStatus` | `true` | Show volume, battery, network changes |
| `Character` | `claude` | `claude`, `eyes`, `cat`, `ghost`, `none` |
| `AudioReactive` | `true` | Capture what the computer plays so the character and bars move with it |
| `LyricsSecondLine` | `translation` | `translation`, `romaji`, `none` |
| `DiscordClientId` | empty | Application ID of a Discord application; empty shows no status |
| `DiscordCover` | `true` | Look the cover up on iTunes Search for Discord |
| `FocusMinutes` / `BreakMinutes` | `25` / `5` | Pomodoro |
| `ChatSite` | `claude` | `claude`, `gemini`, `chatgpt` |
| `SpeechEngine` | `whisper` | Speech recognition: `whisper` (on this computer) or `windows` (Windows dictation) |
| `WhisperModel` | `base` | Local recognition model: `tiny`, `base`, `small`, `medium`; larger is more accurate and slower |
| `PauseWhileListening` | `true` | Pause the music while the voice assistant listens, resume afterwards |
| `VoiceContinuous` | `false` | Keep the conversation going after one press of the microphone until you press again. By default it is one question, one answer; not recommended while speakers are playing music, because the microphone takes the song for speech |
| `AiService` | `claude` | Which provider answers: `claude`, `openai`, `gemini`, `deepseek`, `xai`, `mistral`, `groq`, `openrouter`, `ollama`, `custom` |
| `AiModels` | `{}` | The model chosen for each provider |
| `AiCustomUrl` | empty | API URL when `custom` is chosen |
| `VoiceLanguage` | empty | Language the voice assistant listens and speaks in; empty follows the interface language |

Put your own lyrics in `%APPDATA%\Shima\lyrics`, named `Title.lrc`, `Artist - Title.lrc` or `01 Title.lrc`.

Looking up lyrics sends the title and artist to lrclib.net, music.163.com, y.qq.com and kugou.com (whichever sources are on); with Discord covers on, the title, artist and album go to itunes.apple.com.

For the Discord status, create an application in the [Developer Portal](https://discord.com/developers/applications) (its name is what "Listening to ___" shows) and put its Application ID in `DiscordClientId`.

Claude is a trademark of Anthropic; the Claude character is a homage and this project is not affiliated with Anthropic.

## Translating

The interface texts are in `tools/strings.py`: one line per sentence, Traditional Chinese first and then each language. After editing run

```bash
python3 tools/strings.py
```

It writes `src/Shima/Assets/strings.tsv` and checks that every sentence the app uses has a translation. To add a language, add a column and register it in `Loc.Languages`. The short introductions in other languages come from `tools/readme.py`.

## Tests

```bash
cd /mnt/c && dotnet.exe test "$(wslpath -w ~/projects/shima/tests/Shima.Tests/Shima.Tests.csproj)"
```

## License

MIT © 2026 Carinoasd

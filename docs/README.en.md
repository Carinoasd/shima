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
- **Second line**: a Chinese translation or romaji under Japanese songs (right-click → Second lyrics line). "Furigana" instead puts hiragana readings above the kanji of the line being sung, and makes the second line of the desktop lyrics the whole line in hiragana. Readings come from the Japanese IME that ships with Windows, computed on this PC with no network; the option only appears when the Japanese language is installed
- **Character**: lives in the pill and moves to the actual sound (right-click → Character). The default is a pixel Claude, a homage to the Claude Code mascot, animated frame by frame like a GIF
- **Reacts to sound**: the level bars and the character follow what your computer is really playing; the light and the progress bar take their colour from the cover
- **Discord status**: the song, cover and progress on your profile
- **Tools**: pomodoro timer (the dots beside it count today's finished focus sessions), shelf (drop files, text or screenshots on the island, or press Paste; drag them out later), to-do list (double-click to edit, drag to reorder), reminders (type `15:30 meeting`, `20m water`, `every day 8:00 pills` or `weekdays 9:30 stand-up` and press Enter), calendar, water reminder
- **System status**: volume, brightness, battery, network and output device (headphones plugged in or out) shown on the island when they change
- **Sleep timer**: the moon button on the music tab (or right-click the pill → Sleep timer) pauses playback after 15 / 30 / 45 / 60 minutes or when the current track ends; you can also tell the voice assistant "stop playing in 30 minutes". Cancelled when Shima restarts
- **Output device**: the speaker button on the music tab and the pill's right-click menu switch the Windows default playback device (headphones, speakers, DAC); the spectrum and the character follow the new output
- **11 languages**: 繁體中文, 简体中文, English, 日本語, 한국어, Español, Français, Deutsch, Português, Italiano, Русский. Follows Windows by default; change it in Settings
- **Chat**: the web versions of Claude, Gemini and ChatGPT inside the island; sign in once. When a page leaves the chat site's own domain (a sign-in page, say), the domain you are on is shown above it
- **Claude Code**: permission requests appear on the island with Allow / Deny / In terminal; frequent commands can be set to "Always allow", and the remembered rules can be removed one by one on the AI tab; its task progress is shown (3/5 · what it is doing) and you are told when it finishes
- **Codex CLI, Gemini CLI**: when their settings are found on this PC, each gets its own Connect row on the AI tab. Codex permission requests can be allowed or denied on the island; Gemini CLI only shows session status and completion
- **Ask an AI**: type on the AI tab, or speak and have the answer read aloud; answers appear as they are generated, with Markdown (bold, lists, code) laid out, scrolling when long and a copy button on hover; it can skip songs for you; Ctrl+Alt+V asks one question from anywhere. Works with Claude, OpenAI, Gemini, DeepSeek, xAI, Mistral, Groq, OpenRouter, a local Ollama, or any OpenAI-compatible service
- **Quick commands**: lines starting with `/` in the box on the AI tab run on this PC, without the AI and without an API key: `/play`, `/pause`, `/next`, `/prev`, `/vol 30` (or `+`, `-`), `/sleep 30` (or `track`, `off`), `/remind 15:30 meeting`, `/todo buy milk`, `/focus`, `/dnd`, `/lyrics`, `/earlier`, `/later`, `/settings`. Typing `/` shows a list of matches and `/help` lists everything. Roon's volume is usually the volume of a DAC or an amplifier, so `/vol` raises it by at most a tenth of the range at a time
- **Message notifications**: Discord, LINE, Telegram and other messengers flash on the island (turn it on in Settings)
- **Recent notifications**: messages, reminders that went off and "finished" notices from Claude Code and other agents are kept (the latest 30, in memory only); press the bell on the tools tab to look back, and a small dot appears on the pill while some are unread. The calendar marks days that have reminders, and clicking a day starts a reminder for it (up to 60 days ahead)
- **Roon queue**: the list button on the music tab shows what plays next; click a track to start from it, toggle shuffle and loop. Search is off by default (`RoonSearch`): turning it on makes Roon ask you to enable Shima again under Settings → Extensions
- **Word-by-word lyrics**: when a lyric file carries per-word timing (enhanced LRC), the sweep follows each word; the online sources do not provide it today
- **Desktop lyrics**: a line of lyrics floating on the desktop, visible over borderless fullscreen apps (right-click → Desktop lyrics); locked and click-through by default; hidden for tracks (or videos) without synced lyrics
- **Listening history**: kept only on this PC (`history.jsonl`), with weekly / monthly / yearly time, plays and top tracks on the listening tab; the year view is a heat map with one cell per day, and the month / all-time views can switch to "what time of day"; hover the chart for a day's figure, and step back through earlier weeks, months and years with the arrows or the mouse wheel. The share button turns the current period into a recap card (copied to the clipboard and saved to Pictures\Shima); optional scrobbling to ListenBrainz and Last.fm (both can be on at once), and plays that could not be sent (offline, service down) are kept on this PC and sent later (up to 500, within 14 days). On the Music settings page, click a source (a browser playing videos, say) to exclude it: it is no longer recorded or scrobbled, and what was already recorded stays but is left out of the statistics
- **Saved lines, explain a line**: save the line being sung, search it later, turn it into a card; or ask the AI to explain its reading, words and grammar
- **Claude Code sessions**: one row per session with its state and plan progress, today's token use and estimated cost, and "always allow" for commands you approve often
- **More assistant skills**: weather, reminders, opening sites and common apps, arithmetic, reading clipboard text; an optional wake word
- **Save as .lrc, copy all, AI translation**: right-click → Lyrics for this song. Saving writes the timing you adjusted, the translation and word-level timing into your own lyrics folder, and that file is used from then on; when no translation is found, the service set up on the AI tab can translate line by line into the interface language (remembered in the `translations` folder)
- **Gestures and looks**: scroll on the pill for volume (Shift for previous / next), middle-click to pause; island size (90%–130%, text and icons scale with it), background opacity, what the pill shows when nothing is playing (clock, date, next reminder or nothing), accent colour (a row of swatches or your own hex code), an optional progress line along the bottom of the pill, 3 / 5 / 7 lines of lyrics, your own GIF or PNG sprite as the character
- **Lyric card**: the line being sung, set against the cover, as an image on the clipboard and in `Pictures\Shima` (right-click → Copy lyric card)
- **Updates**: checks GitHub for a new release after startup and once a day; a tray menu item downloads it and swaps it in (`CheckUpdates`, on by default; only the latest version number of this project is requested from api.github.com). The first start after an update says "Updated to x" and adds "What's new in this version" to the tray menu
- **Stream Dock**: puts the island on a Stream Dock touch strip or keys: cover, the line being sung, little Claude, progress; press to pause or skip (turn it on in Settings)
- **Backup and restore**: Settings → General exports your listening history, saved lines, reminders, to-dos, your own lyrics, AI translations, the text and images on the shelf, and settings such as appearance and language to one zip, to restore after moving to another PC or reinstalling. A restore brings back data and appearance, not connections, keys or permission-like switches: a backup contains no keys, tokens or accounts, and the following always stay as they are on this PC — AI provider and models, the custom AI address, the Roon host, the Last.fm and ListenBrainz connections, the Discord Application ID, wake word and continuous conversation, the speech engine and model, message notifications, global shortcuts, "Allow from Stream Dock", and update checks. Listening history, Discord covers and lyrics sources can only be switched off by a backup, never on; excluded listening sources are merged; paths that point to network shares are dropped. Before restoring, the current folder is copied next to itself as `Shima-before-restore-…`
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

Both can be changed under Settings → General: click the button and press the new combination; Esc cancels, Backspace or Delete restores the default.

The tray icon hides the island or quits; "Do not disturb (1 hour)" in its menu pauses the water reminder, messenger notifications and system status (Claude Code permission requests, your own reminders and the pomodoro still show). The gear at the right of the open panel is Settings, split into General, Look, Music, and AI & tools.

## Building it yourself

You need the .NET 10 SDK. From WSL:

```bash
./install.sh
```

This publishes a single executable, installs it to `%LOCALAPPDATA%\Shima\Shima.exe`, creates shortcuts on the desktop and in the Start menu, and starts it. While developing use `./run.sh` (no packaging, faster); `./run.sh --smoke` starts without showing anything, just to check that it runs.

## AI

Open the island → the microphone tab.

**Claude Code**: Connect adds four hooks to Claude Code's `settings.json` (one for Windows, one for the default WSL distribution). They only use `curl.exe` to send events to `localhost:47813`, which Shima listens on. Disconnect removes them and leaves everything else alone. Before the first change a copy is kept as `settings.json.before-shima.bak`. When Shima is not running, or the island is hidden behind a full-screen app, Claude Code asks in the terminal as usual. The bridge only accepts requests that come from this computer and are addressed to `localhost`; if another program already holds port `47813` the island says so, and Claude Code's requests do not reach Shima. Each agent can have at most 8 permission requests waiting on the island; further ones go back to the terminal.

**Other agents**: Codex CLI uses `~/.codex/hooks.json`; after connecting, run `/hooks` in Codex and trust Shima's hook for it to take effect. Gemini CLI uses `~/.gemini/settings.json`; its hooks cannot approve on your behalf, so only status and completion are shown. Each agent has its own key that only opens its own URLs, and "Always allow" applies to Claude Code only. A `.before-shima.bak` copy is kept before the first change here too.

**Last.fm**: fill in your own API key and shared secret (from last.fm/api) under Settings → Music and press Connect to authorise in the browser. Once connected, the title, artist, album, duration and start time of what you play are sent to ws.audioscrobbler.com. The shared secret and session key are stored encrypted with Windows DPAPI.

**Ask an AI**: pick a service, paste that provider's API key (stored encrypted with Windows DPAPI, readable only by your Windows account), then press the model button and choose from the list that provider actually offers. After that, type in the box, or press the microphone and speak: one question, and it stops after answering; it listens once more only when it asks you something back. What you say, and what the assistant gets back from its tools (the current track and its full lyrics, to-dos and reminders, the place name for the weather, clipboard text you ask it to read), goes to the provider you chose and is billed by them. Titles, lyrics and other outside text are handed to the model marked as data, but a model can still be swayed by instructions hidden in them, so the assistant can only open the plain uses of common sites directly (watching a video, searching, looking up an article, playing a song); for anything else, including links on those sites that fetch or redirect to another address, the island shows the full URL and waits for you to press Open. "Explain this line" and "Translate with AI" run without any tools.

Speech is recognised on your own computer with [Whisper](https://github.com/ggerganov/whisper.cpp) by default: no Windows setting to turn on, no language pack to install, the audio never leaves the machine, and Chinese, Japanese, English and many more are understood. The first use downloads a model (about 150 MB) to `%LOCALAPPDATA%\Shima\models` and creates a `runtimes` folder next to the executable for the recognition libraries. Windows dictation can be chosen in Settings instead; it needs "Online speech recognition" turned on in Windows and the speech pack for your language. Answers are read aloud with the voices built into Windows.

The music is paused while it listens (otherwise the microphone hears the song) and resumes afterwards.

| Service | How it is reached |
| --- | --- |
| Claude | the official Anthropic SDK; default model `claude-haiku-5-5` (cheap and quick; pick Sonnet or Opus from the menu for more capability) |
| OpenAI, Gemini, DeepSeek, xAI, Mistral, Groq, OpenRouter | OpenAI-compatible Chat Completions |
| Qwen, Zhipu GLM, Kimi, Doubao, ERNIE, Hunyuan, MiniMax, StepFun, SiliconFlow | Same. Services with separate China and international sites are listed twice; keys are not interchangeable between the two. Doubao, ERNIE and Hunyuan need the model enabled in their own console first |
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

The plugin only talks to Shima on `localhost:47813`, with a key of its own: it exists only while the Stream Dock switch is on (turning the switch off deletes it) and is written into the plugin folder at install time. Allow on a key only applies to the request shown on that key or strip, and only once the request has been visible on the island for 0.6 s (not while the island is hidden); requests allowed from the Stream Dock are listed under recent notifications. After updating Shima, restart Stream Dock before using it to allow. "Allow from Stream Dock" in Settings can be switched off, which leaves only Deny working. Any player Shima can read (Roon, Spotify, browsers…) shows up. The strip layout has only been checked on an N4 Pro.

**Always allow**: this button on a permission request remembers "tool + the first two words of the command" (for example `git status`) and auto-allows the same request in the same project folder from then on. It is a convenience, **not a security boundary**: Shima refuses the forms it can recognise as dangerous (chained commands, `=`, dangerous flags, paths that leave the project or point into `.claude` / `.git` / `.env`; commands such as `bash`, `python` and `sed` cannot be remembered), but it cannot promise to catch everything, and it cannot see symlinks that point outside the project. Do not use it in projects you do not trust, and be especially careful about remembering both `Edit` and commands that run project files such as `npm run` or `make`. Rules live in `%APPDATA%\Shima\agent-rules.json`; delete the file to start over.

**What the keys protect against**: the hooks and the Stream Dock plugin each use a random key (`%APPDATA%\Shima\*.token`). They keep out other computers and web pages in a browser. They do not keep out three things: other programs running under the same Windows account, which can read the key files; other Linux accounts in a WSL distribution where the hook is installed, which can read the key in `~/.claude/settings.json`; and a program that takes port `47813` while Shima is not running, which then receives whatever the hooks send. Keep that in mind on a computer you share.

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
| `DeckAllow` | `true` | Whether the Stream Dock may press Allow; when off, only Deny works |
| `Character` | `claude` | `claude`, `eyes`, `cat`, `ghost`, `none` |
| `AudioReactive` | `true` | Capture what the computer plays so the character and bars move with it |
| `LyricsSecondLine` | `translation` | `translation`, `romaji`, `furigana` (needs the Japanese IME), `none` |
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
| `AiCustomUrl` | empty | API URL when `custom` is chosen; `http://` is only accepted for this computer or the local network (an IP, a host name without dots, names ending in `.local` and the like), anything else needs `https://` |
| `VoiceLanguage` | empty | Language the voice assistant listens and speaks in; empty follows the interface language |

Put your own lyrics in `%APPDATA%\Shima\lyrics`, named `Title.lrc`, `Artist - Title.lrc` or `01 Title.lrc`. If `settings.json` cannot be understood (a hand edit gone wrong, say) it is not simply overwritten: it is first renamed to `settings.json.broken-<date-time>` and kept, and Shima starts again from the defaults.

What leaves this computer:

- **Lyrics lookup**: title and artist go to lrclib.net, music.163.com, y.qq.com and kugou.com (whichever sources are on; Kugou also gets the track length), all over HTTPS
- **ListenBrainz** (only with a token): title, artist, album, length, the player's name and the time playback started go to listenbrainz.org; "now playing" is sent as soon as a track starts, the actual listen once enough of it was heard
- **Last.fm** (once connected): title, artist, album, length and start time go to ws.audioscrobbler.com
- **Discord status** (only with an Application ID): the title, artist, album and progress of whatever is playing appear on your Discord profile, video titles from a browser tab included; with Discord covers on, title, artist and album go to itunes.apple.com to find the artwork
- **Ask an AI**: see the AI section above. Weather questions send the place name to open-meteo.com
- **Speech recognition**: happens on this computer, and the model is downloaded from huggingface.co on first use; with Windows dictation instead, audio goes through Windows' online speech recognition
- **Chat tab**: it is claude.ai, gemini.google.com or chatgpt.com itself; what you do there is between you and them
- **Updates**: only the latest version number of this project is requested from api.github.com, with no data attached. The update is downloaded from this project's GitHub Releases and checked against the size and SHA-256 that GitHub reports: that catches a download altered on the way, not a compromised GitHub account; the executable is not code-signed

Listening history, to-dos, reminders, saved lines and recent notifications stay on this computer.

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

Every pull request is built, tested and checked for missing translations, and the app is packaged the way a release is and actually started once on a Windows machine with no settings at all (`tools/first-run.ps1`): the island has to appear and the log must hold no unhandled exception.

## License

MIT © 2026 Carinoasd

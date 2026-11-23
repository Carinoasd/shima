// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using Shima.Agents;

namespace Shima;

/// <summary>AI：Claude Code 的權限請求顯示在島上，以及語音助手。</summary>
public partial class IslandWindow
{
    private readonly AgentBridge _bridge = new();
    private readonly Queue<PermissionRequest> _asks = new();
    private PermissionRequest? _asking;
    private Tab _beforeAsk = Tab.Music;
    private bool _expandedBeforeAsk;
    private VoiceAssistant? _voice;
    private string _planLine = "";

    private void InitAgents()
    {
        _bridge.Asked += ask => Dispatcher.BeginInvoke(() => Enqueue(ask));
        _bridge.Finished += (project, _) => Dispatcher.BeginInvoke(() => Toast(project.Length > 0 ? L.F("Claude Code 完成了 · {0}", project) : L.T("Claude Code 完成了")));
        _bridge.Progressed += (project, done, total, current) => Dispatcher.BeginInvoke(() =>
        {
            _planLine = total == 0 ? "" : $"{done}/{total}{(current.Length > 0 ? " · " + current : "")}";
            if (total > 0) Toast($"Claude Code {_planLine}", 5);
            if (_tab == Tab.Ai) RefreshAi();
        });
        _bridge.DeckPressed += action => Dispatcher.BeginInvoke(() =>
        {
            switch (action)
            {
                case "next": _media.Next(); break;
                case "previous": _media.Previous(); break;
                case "allow": Decide("allow"); break;
                case "deny": Decide("deny"); break;
                case "ask": AskOnce(); break;
                case "zone": SwitchZone(); break;
                case "mute":
                    if ((_media.ToggleMute() ?? _system.ToggleMute()) is { } muted) Toast(L.T(muted ? "靜音" : "取消靜音"), 2, false);
                    break;
                default: _media.PlayPause(); break;
            }
        });
        _bridge.DeckTurned += (what, ticks) => Dispatcher.BeginInvoke(() =>
        {
            switch (what)
            {
                case "volume":
                    if ((_media.Nudge(ticks) ?? _system.Nudge(ticks * 2)) is { } volume) Toast(L.F("音量 {0}", volume), 2, false);
                    break;
                case "seek":
                    if (_track is { } t && t.Duration > TimeSpan.Zero)
                    {
                        var to = t.Now + TimeSpan.FromSeconds(ticks * 5);
                        _media.Seek(to < TimeSpan.Zero ? TimeSpan.Zero : to > t.Duration ? t.Duration : to);
                    }
                    break;
                case "shift": ShiftLyrics(ticks * 100); break;
                case "zone": SwitchZone(); break;
            }
        });
        InitExamples();
        Loaded += (_, _) =>
        {
            _bridge.Start();
            if (StreamDock.Installed) StreamDock.Install();   // 跟著 Shima 一起更新
        };
    }

    // ---------------------------------------------------------------- Claude Code

    private void Enqueue(PermissionRequest ask)
    {
        // 島因為全螢幕藏起來時看不到按鈕：讓 Claude Code 照常自己問，不要卡住
        if (_hiddenForFullscreen) { ask.Decision.TrySetResult(null); return; }
        _asks.Enqueue(ask);
        if (_asking == null) NextAsk();
    }

    private void NextAsk()
    {
        while (_asks.Count > 0 && _asks.Peek().Decision.Task.IsCompleted) _asks.Dequeue();   // 等太久，已經回終端機了
        if (_asks.Count == 0)
        {
            if (_asking == null) return;
            _asking = null;
            _tab = _beforeAsk;
            _expanded = _expandedBeforeAsk && Host.IsMouseOver;
            Morph();
            return;
        }

        if (_asking == null) { _beforeAsk = _tab == Tab.Ask ? Tab.Music : _tab; _expandedBeforeAsk = _expanded; }
        var ask = _asking = _asks.Dequeue();
        AskTitle.Text = ask.Project.Length > 0 ? $"Claude Code · {ask.Project}" : "Claude Code";
        AskTool.Text = ask.Tool;
        AskDetails.Text = ask.Details;
        _tab = Tab.Ask;
        _expanded = true;
        Buddy.Cheer();
        Morph();
        // 沒人按就自己收起來
        _ = ask.Decision.Task.ContinueWith(_ => Dispatcher.BeginInvoke(() => { if (_asking == ask) NextAsk(); }));
    }

    private void Decide(string? behavior)
    {
        _asking?.Decision.TrySetResult(behavior);
    }

    private void AskAllow_Click(object sender, RoutedEventArgs e) => Decide("allow");
    private void AskDeny_Click(object sender, RoutedEventArgs e) => Decide("deny");
    private void AskTerminal_Click(object sender, RoutedEventArgs e) => Decide(null);

    private void RefreshAi()
    {
        AgentButton.Content = AgentHooks.Installed ? L.T("中斷") : L.T("連接");
        PlanText.Text = _planLine;

        var s = AppSettings.Current;
        var service = AiService.Of(s.AiService);
        ServiceButton.Content = (service.Id == AiService.Custom ? L.T("自訂") : service.Name) + "  \u25BE";
        var model = s.AiModels.GetValueOrDefault(service.Id, service.Id == "claude" ? ClaudeBrain.DefaultModel : "");
        ModelButton.Content = (model.Length > 0 ? model : L.T("選擇模型")) + "  \u25BE";
        UrlBox.Visibility = service.Id == AiService.Custom ? Visibility.Visible : Visibility.Collapsed;
        KeyRow.Visibility = service.NeedsKey ? Visibility.Visible : Visibility.Collapsed;
        _loadingKey = true;
        if (!UrlBox.IsKeyboardFocusWithin) UrlBox.Text = s.AiCustomUrl;
        if (!ApiKeyBox.IsKeyboardFocusWithin) ApiKeyBox.Password = s.HasKey(service.Id) ? s.ApiKey(service.Id) : "";
        _loadingKey = false;
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    private bool _loadingKey;

    private void Service_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = ServiceButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var service in AiService.All)
        {
            var item = new System.Windows.Controls.MenuItem { Header = service.Id == AiService.Custom ? L.T("自訂") : service.Name, IsCheckable = true, IsChecked = service.Id == AppSettings.Current.AiService };
            item.Click += (_, _) =>
            {
                AppSettings.Current.AiService = service.Id;
                AppSettings.Current.Save();
                _asker = null;   // 換了服務，對話重新開始
                RefreshAi();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>向這一家問它有哪些模型，列成選單。</summary>
    private async void Model_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Current;
        var service = AiService.Of(s.AiService);
        // 還沒選模型也要能問清單，所以先用一個暫時的名字建立
        bool none = !s.AiModels.ContainsKey(service.Id) && service.Id != "claude";
        if (none) s.AiModels[service.Id] = "-";
        var brain = Brain.Create(Tool, Loc.Speech, out var problem);
        if (none) s.AiModels.Remove(service.Id);
        if (brain == null) { Say(problem); return; }

        ModelButton.IsEnabled = false;
        List<string> models;
        try { models = await brain.ModelsAsync(); }
        catch (Exception x) { Say(brain.Explain(x)); return; }
        finally { ModelButton.IsEnabled = true; }
        if (models.Count == 0) { Say(L.T("找不到可用的模型")); return; }

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = ModelButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
        var current = s.AiModels.GetValueOrDefault(service.Id, service.Id == "claude" ? ClaudeBrain.DefaultModel : "");
        foreach (var model in models)
        {
            var item = new System.Windows.Controls.MenuItem { Header = model.Replace("_", "__"), IsCheckable = true, IsChecked = model == current };
            item.Click += (_, _) =>
            {
                s.AiModels[service.Id] = model;
                s.Save();
                _asker = null;
                Say("");
                RefreshAi();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Url_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loadingKey || UrlBox.Text.Trim() == AppSettings.Current.AiCustomUrl) return;
        AppSettings.Current.AiCustomUrl = UrlBox.Text.Trim();
        AppSettings.Current.Save();
        _asker = null;
    }

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        AgentButton.IsEnabled = false;
        try
        {
            bool installed = await Task.Run(() => AgentHooks.Installed);
            var problem = await Task.Run(() => installed ? AgentHooks.Remove() : AgentHooks.Install());
            if (problem != null) Say(problem);
            AgentButton.Content = await Task.Run(() => AgentHooks.Installed) ? L.T("中斷") : L.T("連接");
        }
        finally { AgentButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- 語音助手

    private void ApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingKey) return;
        _asker = null;   // 換了金鑰，對話重新開始
        AppSettings.Current.SetApiKey(AppSettings.Current.AiService, ApiKeyBox.Password.Trim());
    }

    private void Voice_Click(object sender, RoutedEventArgs e) => StartVoice(once: false);

    private VoiceAssistant? _asker;
    private Ears? _ears;
    private string _voiceLine = "";
    private bool _micOpen, _resumeMusic;

    /// <summary>可以直接點的範例：一看就知道能問什麼，點了就問。</summary>
    private static readonly string[] Examples = ["現在播的是哪一首？", "這句歌詞是什麼意思？", "下一首", "提醒我買牛奶", "專注 25 分鐘"];

    private void InitExamples()
    {
        foreach (var example in Examples)
        {
            var chip = new System.Windows.Controls.Button
            {
                Content = L.T(example), Style = (Style)FindResource("Pill"), Height = 26, FontSize = 12,
                Padding = new Thickness(11, 0, 11, 0), Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Click += (_, _) => _ = AskAsync(L.T(example));
            ExamplesBox.Children.Add(chip);
        }
    }

    private async void AskBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && AskBox.Text.Trim() is { Length: > 0 } question) await AskAsync(question);
    }

    /// <summary>在 AI 分頁打字（或點範例）問 AI，答案顯示在下面。同一段對話會接著聊。</summary>
    private async Task AskAsync(string question)
    {
        if (!AskBox.IsEnabled) return;
        if (_asker == null)
        {
            var brain = Brain.Create((tool, arguments) => Dispatcher.Invoke(() => Tool(tool, arguments)), Loc.Speech, out var problem);
            if (brain == null) { Say(problem); return; }
            _asker = new VoiceAssistant(brain, Loc.Speech);
        }
        AskBox.IsEnabled = false;
        AskBox.Text = question;
        Say("…");
        var answer = await _asker.AskAsync(question);
        AskBox.Text = "";
        AskBox.IsEnabled = true;
        Say(answer);
    }

    /// <summary>AI 分頁下方那塊文字；面板的高度跟著它長。</summary>
    private void Say(string text)
    {
        VoiceText.Text = text;
        VoiceText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    /// <summary>快速鍵：問一句就好，問答顯示在膠囊上。</summary>
    private void AskOnce() => StartVoice(once: true);

    private void StartVoice(bool once)
    {
        if (_voice != null) { _voice.Stop(); return; }

        var language = AppSettings.Current.VoiceLanguage is { Length: > 0 } spoken ? spoken : Loc.Speech;
        var brain = Brain.Create((tool, arguments) => Dispatcher.Invoke(() => Tool(tool, arguments)), language, out var problem);
        if (brain == null) { Say(problem); if (once) Toast(L.T("先到 AI 分頁設定好 AI 服務"), 4); return; }

        _ears ??= new Ears();
        var voice = _voice = new VoiceAssistant(brain, language, _ears) { Once = once || !AppSettings.Current.VoiceContinuous };
        _resumeMusic = false;
        // 聽到的話、回答都顯示在膠囊上，島收著也看得到
        voice.Said += text => Dispatcher.BeginInvoke(() => { Say(text); _voiceLine = text; UpdateTexts(); });
        voice.Listening += on => Dispatcher.BeginInvoke(() =>
        {
            _micOpen = on;
            // 喇叭在放歌的話麥克風會一直收到歌聲，以為還有人在講話：先暫停，問完再繼續
            // 用當下的播放狀態判斷（剛切歌時畫面上那份可能還沒更新）
            if (on && AppSettings.Current.PauseWhileListening && _media.Current?.Playing == true) { _media.Pause(); _resumeMusic = true; }
        });
        voice.NeedsSetting += page => Dispatcher.BeginInvoke(() =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true }); }
            catch (Exception x) { Log.Write("settings: " + x.Message); }
        });
        voice.Stopped += () => Dispatcher.BeginInvoke(() =>
        {
            if (_voice == voice) _voice = null;
            _micOpen = false;
            // 最後那句回答在膠囊上多留一下再換回歌詞
            if (_voiceLine.Length > 0) Toast(_voiceLine, 8, cheer: false);
            _voiceLine = "";
            if (_resumeMusic) _media.Play();
            _resumeMusic = false;
            VoiceButton.Content = "";
        });
        VoiceButton.Content = "";   // 停止
        _ = Task.Run(voice.RunAsync);
    }

    /// <summary>助手要用某個工具：做完之後回一句話讓它知道結果。（給 AI 看的，所以不翻譯。）</summary>
    private string Tool(string tool, System.Text.Json.Nodes.JsonObject arguments)
    {
        Log.Write($"tool: {tool} {arguments.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}");
        string Arg(string name) => arguments[name]?.ToString() ?? "";
        double Number(string name, double fallback) => double.TryParse(Arg(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        var s = AppSettings.Current;
        try
        {
            switch (tool)
            {
                case "music": return Music(Arg("action"));

                case "lyrics":
                {
                    if (_track is not { } track) return "目前沒有在播放音樂";
                    if (_sheet is not { } sheet) return $"《{track.Title}》找不到歌詞";
                    int now = sheet.Synced ? Lyrics.Lrc.LineAt(sheet.Lines, track.Now + _lyricShift) : -1;
                    var text = new System.Text.StringBuilder($"《{track.Title}》— {track.Artist}\n");
                    for (int i = 0; i < sheet.Lines.Count && i < 160; i++)
                    {
                        if (sheet.Lines[i].Text.Length == 0) continue;
                        text.Append(i == now ? "【正在唱】" : "").Append(sheet.Lines[i].Text);
                        if (sheet.Translation is { } tr && i < tr.Count && tr[i].Length > 0) text.Append("（").Append(tr[i]).Append('）');
                        text.Append('\n');
                    }
                    return text.ToString();
                }

                case "todo":
                {
                    var words = Arg("text").Trim();
                    TodoItem? Find() => s.Todos.FirstOrDefault(t => words.Length > 0 && t.Text.Contains(words, StringComparison.OrdinalIgnoreCase));
                    switch (Arg("action"))
                    {
                        case "add":
                            if (words.Length == 0) return "沒有給待辦的內容";
                            s.Todos.Add(new TodoItem { Text = words }); s.Save(); RefreshTools();
                            return $"已加入待辦：{words}";
                        case "done":
                            if (Find() is not { } done) return $"找不到包含「{words}」的待辦";
                            done.Done = true; s.Save(); RefreshTools();
                            return $"已標成完成：{done.Text}";
                        case "remove":
                            if (Find() is not { } gone) return $"找不到包含「{words}」的待辦";
                            s.Todos.Remove(gone); s.Save(); RefreshTools();
                            return $"已刪除：{gone.Text}";
                        default:
                            return s.Todos.Count == 0 ? "待辦清單是空的" : string.Join("\n", s.Todos.Select(t => (t.Done ? "[已完成] " : "[未完成] ") + t.Text));
                    }
                }

                case "timer":
                {
                    string Left() { var left = _pomodoro.Left(DateTime.Now); return $"{(int)left.TotalMinutes} 分 {left.Seconds} 秒"; }
                    switch (Arg("action"))
                    {
                        case "start":
                            if (Number("minutes", 0) is > 0 and var minutes)
                                _pomodoro = new Pomodoro { Focus = TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 180)), Break = TimeSpan.FromMinutes(Math.Clamp(s.BreakMinutes, 1, 60)) };
                            if (!_pomodoro.Running) _pomodoro.Toggle(DateTime.Now);
                            UpdateTimer();
                            return $"番茄鐘開始，剩 {Left()}";
                        case "pause":
                            if (_pomodoro.Running) _pomodoro.Toggle(DateTime.Now);
                            UpdateTimer();
                            return $"番茄鐘已暫停，剩 {Left()}";
                        case "reset": _pomodoro.Reset(); UpdateTimer(); return "番茄鐘已歸零";
                        default: return _pomodoro.Running ? $"{(_pomodoro.Resting ? "休息中" : "專注中")}，剩 {Left()}" : $"番茄鐘沒有在跑，目前設定剩 {Left()}";
                    }
                }

                case "volume":
                {
                    int steps = (int)Math.Clamp(Number("steps", 2), 1, 10) * (Arg("direction") == "down" ? -1 : 1);
                    // Roon 有自己的音量；不是 Roon 在播就調整台電腦的音量
                    if (_media.Nudge(steps) is { } roon) return $"Roon 音量現在是 {roon}";
                    return _system.Nudge(steps * 4) is { } system ? $"電腦音量現在是 {system}%" : "沒有可以調音量的裝置";
                }

                case "lyrics_fix":
                {
                    if (_track == null) return "目前沒有在播放音樂";
                    int ms = (int)Math.Round(Math.Clamp(Number("seconds", 0.3), 0.1, 5) * 1000);
                    switch (Arg("action"))
                    {
                        case "other": LyricsOther_Click(this, new RoutedEventArgs()); return "正在換另一個來源的歌詞";
                        case "earlier": ShiftLyrics(ms); return $"歌詞提早了 {ms / 1000.0:0.0} 秒，目前總共{(_lyricShift.TotalMilliseconds >= 0 ? "提早" : "延後")} {Math.Abs(_lyricShift.TotalSeconds):0.0} 秒";
                        case "later": ShiftLyrics(-ms); return $"歌詞延後了 {ms / 1000.0:0.0} 秒，目前總共{(_lyricShift.TotalMilliseconds >= 0 ? "提早" : "延後")} {Math.Abs(_lyricShift.TotalSeconds):0.0} 秒";
                        default: LyricsReset_Click(this, new RoutedEventArgs()); return "歌詞已還原";
                    }
                }
            }
            return "沒有這個工具";
        }
        catch (Exception e) { Log.Write($"tool {tool}: {e.Message}"); return "執行時發生錯誤"; }
    }

    /// <summary>語音助手要求的音樂動作；回傳給 Claude 看的結果。</summary>
    private string Music(string action)
    {
        var track = _track;
        // 使用者用說的控制了播放：照他說的做，結束時不要再自動恢復，免得跟他的指令打架。
        // 一律用明確的「播放」「暫停」而不是「切換」：聆聽時我們可能已經先替他暫停了，
        // 而且有些播放器切歌後自己就開始播，這時再切換一次反而會把它停掉。
        if (action is "play" or "pause" or "next" or "previous") _resumeMusic = false;
        switch (action)
        {
            case "play": _media.Play(); return track == null ? "目前沒有播放器" : "已開始播放";
            case "pause": _media.Pause(); return track == null ? "目前沒有播放器" : "已暫停";
            case "next": _media.Next(); _media.Play(); return track == null ? "目前沒有播放器" : "已切到下一首並播放";
            case "previous": _media.Previous(); _media.Play(); return track == null ? "目前沒有播放器" : "已切到上一首並播放";
            case "now_playing":
                return track == null ? "目前沒有在播放音樂"
                    // 為了收音暫時替他暫停的不算暫停：對使用者來說這首歌是在播的
                    : $"正在{(track.Playing || _resumeMusic ? "播放" : "暫停")}：{track.Title}，演出者 {track.Artist}，專輯 {track.Album}，來源 {track.Source}";
            default: return "不認得的動作";
        }
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Media;
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
    /// <summary>目前這一則權限請求出現在島上的時刻（Environment.TickCount64）。</summary>
    private long _askShownAt;
    /// <summary>舊版外掛按允許的提醒，一次執行期間只說一次。</summary>
    private bool _deckOldTold;
    private VoiceAssistant? _voice;
    private string _planLine = "";

    private void InitAgents()
    {
        _bridge.Asked += ask => Dispatcher.BeginInvoke(() => Enqueue(ask));
        _bridge.AgentFinished += (agent, project, _) => Dispatcher.BeginInvoke(() => { Remember(RecentKind.Agent, AgentKinds.Name(agent), L.T("完成了"), project); Toast(Finished(agent, project)); });
        _bridge.Progressed += (project, done, total, current) => Dispatcher.BeginInvoke(() =>
        {
            _planLine = total == 0 ? "" : $"{done}/{total}{(current.Length > 0 ? " · " + current : "")}";
            if (total > 0) Toast($"Claude Code {_planLine}", 5);
            // 收著的時候只更新待辦那一行；展開 AI 分頁時才整頁更新（查 hook 要啟動 wsl.exe、金鑰要解密）
            if (_tab == Tab.Ai && _expanded) RefreshAi();
            else { PlanText.Text = _planLine; _aiStale |= _tab == Tab.Ai; }
        });
        // 收著時錯過的更新，在展開的那一刻補上（Morph 展開時會把 Panel 設成可點）
        Panel.IsHitTestVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true && _aiStale) Dispatcher.BeginInvoke(() => { if (_aiStale && _tab == Tab.Ai && _expanded) RefreshAi(); });
        };
        _bridge.DeckPressed += (action, id) => Dispatcher.BeginInvoke(() =>
        {
            switch (action)
            {
                case "next": _media.Next(); break;
                case "previous": _media.Previous(); break;
                case "allow" or "deny": DeckDecide(action, id); break;
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
        InitSessions();
        InitExamples();
        Loaded += (_, _) =>
        {
            // 示範模式的金鑰是暫存資料夾裡新產生的：寫進真正的 Stream Dock 外掛的話，平常那一份就不認它了
            if (Demo.On) return;
            if (!_bridge.Start()) Toast(L.F("連接埠 {0} 被別的程式佔用，Claude Code 的請求不會送到 Shima", AgentBridge.Port), 12);
            // 資料夾被換掉（SHIMA_DATA）時金鑰也不是平常那一把，同樣不更新外掛
            if (StreamDock.Installed && !AppSettings.Redirected) StreamDock.Install();   // 跟著 Shima 一起更新
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
            // 等回答的期間錯過的更新：島一直是展開的，不會再有「剛展開」的事件來補
            if (_aiStale && _tab == Tab.Ai && _expanded) RefreshAi();
            Morph();
            return;
        }

        if (_asking == null) { _beforeAsk = _tab == Tab.Ask ? Tab.Music : _tab; _expandedBeforeAsk = _expanded; }
        var ask = _asking = _asks.Dequeue();
        _askShownAt = Environment.TickCount64;
        var name = AgentKinds.Name(ask.Agent);
        AskTitle.Text = ask.Project.Length > 0 ? $"{name} · {ask.Project}" : name;
        AskTool.Text = ask.Tool;
        AskDetails.Text = ask.Details;
        AskAlways.Visibility = ask.RememberText != null ? Visibility.Visible : Visibility.Collapsed;
        AskAlways.Content = ask.RememberText ?? "";
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

    /// <summary>
    /// Stream Dock 按了允許／拒絕：在 UI 執行緒拿此刻正在問的那一則來比對（判斷在 DeckAnswer.Judge）。
    /// 由 Stream Dock 允許的記進最近的通知，事後查得到是哪個工具、什麼指令。
    /// </summary>
    private void DeckDecide(string action, string? id)
    {
        var ask = _asking is { Decision.Task.IsCompleted: false } open ? open : null;
        switch (DeckAnswer.Judge(action, id, ask?.Id, Environment.TickCount64 - _askShownAt, AppSettings.Current.DeckAllow))
        {
            case DeckVerdict.Apply:
                if (ask!.Decision.TrySetResult(action) && action == "allow")
                    Remember(RecentKind.Agent, AgentKinds.Name(ask.Agent), L.F("Stream Dock 允許了 {0}", ask.Tool), ask.Details);
                break;
            case DeckVerdict.OldPlugin when !_deckOldTold:
                // 外掛更新了，但 Stream Dock 還跑著舊的那份：它送來的允許沒有識別碼，不會生效
                _deckOldTold = true;
                Toast(L.T("重新啟動 Stream Dock，才能用它按允許"), 8);
                break;
        }
    }

    private void AskAllow_Click(object sender, RoutedEventArgs e) => Decide("allow");
    private void AskDeny_Click(object sender, RoutedEventArgs e) => Decide("deny");
    private void AskTerminal_Click(object sender, RoutedEventArgs e) => Decide(null);

    private (bool Installed, bool Outdated)? _hooks;

    /// <summary>AI 分頁停著、收著的時候有進度回報進來，整頁還沒更新。</summary>
    private bool _aiStale;

    private void ShowHookState() =>
        AgentButton.Content = _hooks is not { } state ? "…" : !state.Installed ? L.T("連接") : state.Outdated ? L.T("更新連接") : L.T("中斷");

    private void RefreshAi()
    {
        _aiStale = false;
        // 查 hook 的狀態要啟動 wsl.exe，放到背景做；畫面先用上次的結果，查完再更新。每個 agent 共用同一次查詢
        ShowHookState();
        _ = Task.Run(() => AgentInstaller.Scan()).ContinueWith(done =>
        {
            if (done.IsCompletedSuccessfully) Dispatcher.BeginInvoke(() => ShowAgents(done.Result));
        });
        RefreshUsage();
        PlanText.Text = _planLine;

        var s = AppSettings.Current;
        var service = AiService.Of(s.AiService);
        ServiceButton.Content = (service.Id == AiService.Custom ? L.T("自訂") : service.Name) + "  \u25BE";
        var model = service.ModelFor(s);
        ModelButton.Content = (model.Length > 0 ? model : L.T("選擇模型")) + "  \u25BE";
        // 金鑰與自訂網址設定好之後就收起來（那一長串圓點平常用不到），按鑰匙才打開；還沒設定好、或正在打字時一定開著
        bool custom = service.Id == AiService.Custom;
        // 自訂的服務（例如本機的）不一定要金鑰：有填網址就算設定好了
        bool unset = custom ? s.AiCustomUrl.Length == 0 : service.NeedsKey && s.ApiKey(service.Id).Length == 0;
        bool open = _showKey || unset || ApiKeyBox.IsKeyboardFocusWithin || UrlBox.IsKeyboardFocusWithin;
        UrlBox.Visibility = custom && open ? Visibility.Visible : Visibility.Collapsed;
        KeyRow.Visibility = service.NeedsKey && open ? Visibility.Visible : Visibility.Collapsed;
        KeyToggle.Visibility = service.NeedsKey || custom ? Visibility.Visible : Visibility.Collapsed;
        KeyToggle.Foreground = open ? Brushes.White : KeyDim;
        _loadingKey = true;
        if (!UrlBox.IsKeyboardFocusWithin) UrlBox.Text = s.AiCustomUrl;
        if (!ApiKeyBox.IsKeyboardFocusWithin) ApiKeyBox.Password = s.HasKey(service.Id) ? s.ApiKey(service.Id) : "";
        _loadingKey = false;
        RefreshRules();
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
        // 有幾家不給模型清單：抓不到就只列預設的那一款，至少能用
        try { models = await brain.ModelsAsync(); }
        catch (Exception x)
        {
            if (service.DefaultModel.Length == 0) { Say(brain.Explain(x)); return; }
            Log.Write($"ai: {service.Id} 的模型清單抓不到（{x.Message}）");
            models = [];
        }
        finally { ModelButton.IsEnabled = true; }
        if (service.DefaultModel.Length > 0 && !models.Contains(service.DefaultModel)) models.Insert(0, service.DefaultModel);
        if (models.Count == 0) { Say(L.T("找不到可用的模型")); return; }

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = ModelButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MaxHeight = 420 };
        var current = service.ModelFor(s);
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

    private void CommitUrl()
    {
        if (_loadingKey) return;
        var url = UrlBox.Text.Trim();
        if (url != UrlBox.Text) UrlBox.Text = url;
        if (url == AppSettings.Current.AiCustomUrl) return;
        AppSettings.Current.AiCustomUrl = url;
        AppSettings.Current.Save();
        _asker = null;
    }

    private void Url_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => CommitUrl();

    private void Url_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { CommitUrl(); System.Windows.Input.Keyboard.ClearFocus(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape) { UrlBox.Text = AppSettings.Current.AiCustomUrl; System.Windows.Input.Keyboard.ClearFocus(); e.Handled = true; }
    }

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        AgentButton.IsEnabled = false;
        try
        {
            await ToggleAgent(AgentKind.Claude);
        }
        finally { AgentButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- 語音助手

    private bool _showKey;
    private static readonly Brush KeyDim = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));

    private void KeyToggle_Click(object sender, RoutedEventArgs e)
    {
        _showKey = !_showKey;
        RefreshAi();
    }

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
    private static readonly string[] Examples = ["現在播的是哪一首？", "這句歌詞是什麼意思？", "下一首", "提醒我買牛奶", "專注 25 分鐘", "/sleep 30"];

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
        if (TryCommand(question)) return;   // /開頭的指令在本機做，不用金鑰也不花 token
        if (_asker == null)
        {
            var brain = Brain.Create((tool, arguments) => Skills.Run(tool, arguments) ?? Dispatcher.Invoke(() => Tool(tool, arguments)), Loc.Speech, out var problem);
            if (brain == null) { Say(problem); return; }
            _asker = new VoiceAssistant(brain, Loc.Speech);
        }
        AskBox.IsEnabled = false;
        AskBox.Text = question;
        Say("…");
        var trickle = Streamed();
        try
        {
            // 在背景問：工具可能要上網（查天氣），不能卡住畫面。回答邊產生邊顯示
            var asker = _asker;
            var reply = await Task.Run(() => asker.AskAsync(question, trickle.Put));
            trickle.Close();   // 晚到的片段不能蓋掉整段答案
            Say(reply);
        }
        catch (Exception x) { Log.Write("ask: " + x.Message); Say(x.Message); }
        finally
        {
            trickle.Close();
            // 不管成功失敗都要把輸入框還回去，不然之後就再也問不了
            AskBox.Text = "";
            AskBox.IsEnabled = true;
        }
    }

    /// <summary>AI 分頁下方那塊文字；面板的高度跟著它長。</summary>
    private void Say(string text)
    {
        VoiceText.Text = text;
        VoiceText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // 有回答的時候把「可以問什麼」的範例收起來：這時分頁最高，範例也用不到
        ExamplesBox.Visibility = text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_tab == Tab.Ai && _expanded) Morph();
        _voiceHeight = VoiceText.DesiredSize.Height;
    }

    private double _voiceHeight;

    /// <summary>串流中的回答，每一個片段都要更新：只有字多到換行、高度變了才讓島重新變形，不要每個字都重排整個面板。</summary>
    /// <remarks>
    /// 長過 AnswerView 的 MaxHeight 之後高度就不再變，島也不用再變形：回答在裡面捲動、跟著最新的字。
    /// 最後整段的答案（Say）跟串流的最後一份只差頭尾空白，AnswerView 會當成同一段，不會跳回開頭。
    /// </remarks>
    private void SayPartial(string text)
    {
        VoiceText.Text = text;
        VoiceText.Visibility = Visibility.Visible;
        ExamplesBox.Visibility = Visibility.Collapsed;
        if (_tab != Tab.Ai || !_expanded) return;
        VoiceText.Measure(new Size(AiPage.ActualWidth, double.PositiveInfinity));
        if (Math.Abs(VoiceText.DesiredSize.Height - _voiceHeight) < 0.5) return;
        Morph();
        _voiceHeight = VoiceText.DesiredSize.Height;
    }

    /// <summary>把串流的片段節流後顯示在 VoiceText：最多每 66 毫秒換一次。</summary>
    private Trickle Streamed() => new(TimeSpan.FromMilliseconds(66), work => Dispatcher.BeginInvoke(work), SayPartial);

    /// <summary>快速鍵：問一句就好，問答顯示在膠囊上。</summary>
    private void AskOnce() => StartVoice(once: true);

    private void StartVoice(bool once)
    {
        if (_voice != null) { _voice.Stop(); return; }

        var language = AppSettings.Current.VoiceLanguage is { Length: > 0 } spoken ? spoken : Loc.Speech;
        var brain = Brain.Create((tool, arguments) => Skills.Run(tool, arguments) ?? Dispatcher.Invoke(() => Tool(tool, arguments)), language, out var problem);
        if (brain == null) { Say(problem); if (once) Toast(L.T("先到 AI 分頁設定好 AI 服務"), 4); return; }

        _ears ??= new Ears();
        _wake?.Stop();   // 麥克風讓給語音助手
        var voice = _voice = new VoiceAssistant(brain, language, _ears) { Once = once || !AppSettings.Current.VoiceContinuous };
        _resumeMusic = false;
        // 聽到的話、回答都顯示在膠囊上，島收著也看得到。
        // 回答產生中的片段只串流到面板上的文字：膠囊換字會淡入淡出、寬度也跟著變，每幾十毫秒換一次會一直閃
        var trickle = Streamed();
        voice.Partial += trickle.Put;
        voice.Said += text => Dispatcher.BeginInvoke(() => { trickle.Drop(); Say(text); _voiceLine = text; UpdateTexts(); });
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
            trickle.Close();
            ApplyWakeWord();
            _micOpen = false;
            // 最後那句回答在膠囊上多留一下再換回歌詞
            if (_voiceLine.Length > 0) Toast(_voiceLine, 8, cheer: false);
            _voiceLine = "";
            if (_resumeMusic) _media.Play();
            _resumeMusic = false;
            VoiceButton.Content = "";
        });
        VoiceButton.Content = "";   // 停止
        // 等喚醒詞那一輪真的收尾（放掉麥克風、辨識結束）再開始聽，兩邊才不會同時開著麥克風
        var wake = _wake;
        _ = Task.Run(async () => { if (wake != null) await wake.StopAsync(); await voice.RunAsync(); });
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

                case "sleep": return SleepTool(Arg("action"), Number("minutes", 0));

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

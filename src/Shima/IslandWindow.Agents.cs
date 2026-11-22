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
        Loaded += (_, _) => _bridge.Start();
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
        var brain = Brain.Create(Music, Loc.Speech, out var problem);
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

    /// <summary>在 AI 分頁打字問 Claude，答案顯示在下面。同一段對話會接著聊。</summary>
    private async void AskBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || AskBox.Text.Trim() is not { Length: > 0 } question || !AskBox.IsEnabled) return;
        if (_asker == null)
        {
            var brain = Brain.Create(action => Dispatcher.Invoke(() => Music(action)), Loc.Speech, out var problem);
            if (brain == null) { Say(problem); return; }
            _asker = new VoiceAssistant(brain, Loc.Speech);
        }
        AskBox.IsEnabled = false;
        Say("…");
        var answer = await _asker.AskAsync(question);
        AskBox.Text = "";
        AskBox.IsEnabled = true;
        Say(answer);
        AskBox.Focus();
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
        var brain = Brain.Create(action => Dispatcher.Invoke(() => Music(action)), language, out var problem);
        if (brain == null) { Say(problem); if (once) Toast(L.T("先到 AI 分頁設定好 AI 服務"), 4); return; }

        var voice = _voice = new VoiceAssistant(brain, language) { Once = once };
        voice.Said += text => Dispatcher.BeginInvoke(() => { Say(text); if (once && text.Length > 0) Toast(text, 12); });
        voice.Stopped += () => Dispatcher.BeginInvoke(() =>
        {
            if (_voice == voice) _voice = null;
            VoiceButton.Content = "";
        });
        VoiceButton.Content = "";   // 停止
        _ = Task.Run(voice.RunAsync);
    }

    /// <summary>語音助手要求的音樂動作；回傳給 Claude 看的結果。</summary>
    private string Music(string action)
    {
        var track = _track;
        switch (action)
        {
            case "play_pause": _media.PlayPause(); return track == null ? "目前沒有播放器" : track.Playing ? "已暫停" : "已開始播放";
            case "next": _media.Next(); return track == null ? "目前沒有播放器" : "已切到下一首";
            case "previous": _media.Previous(); return track == null ? "目前沒有播放器" : "已切到上一首";
            case "now_playing":
                return track == null ? "目前沒有在播放音樂"
                    : $"正在{(track.Playing ? "播放" : "暫停")}：{track.Title}，演出者 {track.Artist}，專輯 {track.Album}，來源 {track.Source}";
            default: return "不認得的動作";
        }
    }
}

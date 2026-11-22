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
        _bridge.Finished += (project, _) => Dispatcher.BeginInvoke(() => Toast(project.Length > 0 ? $"Claude Code 完成了 · {project}" : "Claude Code 完成了"));
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
        AgentButton.Content = AgentHooks.Installed ? "中斷" : "連接";
        PlanText.Text = _planLine;
        if (!ApiKeyBox.IsKeyboardFocusWithin)
        {
            _loadingKey = true;
            ApiKeyBox.Password = AppSettings.Current.AnthropicKey.Length > 0 ? AppSettings.Current.ApiKey() : "";
            _loadingKey = false;
        }
    }

    private bool _loadingKey;

    private async void Agent_Click(object sender, RoutedEventArgs e)
    {
        AgentButton.IsEnabled = false;
        try
        {
            bool installed = await Task.Run(() => AgentHooks.Installed);
            var problem = await Task.Run(() => installed ? AgentHooks.Remove() : AgentHooks.Install());
            if (problem != null) VoiceText.Text = problem;
            AgentButton.Content = await Task.Run(() => AgentHooks.Installed) ? "中斷" : "連接";
        }
        finally { AgentButton.IsEnabled = true; }
    }

    // ---------------------------------------------------------------- 語音助手

    private void ApiKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingKey) return;
        AppSettings.Current.SetApiKey(ApiKeyBox.Password.Trim());
    }

    private void Voice_Click(object sender, RoutedEventArgs e) => StartVoice(once: false);

    /// <summary>快速鍵：問一句就好，問答顯示在膠囊上。</summary>
    private void AskOnce() => StartVoice(once: true);

    private void StartVoice(bool once)
    {
        if (_voice != null) { _voice.Stop(); return; }

        var key = AppSettings.Current.ApiKey();
        if (key.Length == 0) { VoiceText.Text = "先貼上 Anthropic API 金鑰"; if (once) Toast("先到 AI 分頁貼上 Anthropic API 金鑰", 4); return; }

        var voice = _voice = new VoiceAssistant(key, AppSettings.Current.VoiceLanguage, action => Dispatcher.Invoke(() => Music(action))) { Once = once };
        voice.Said += text => Dispatcher.BeginInvoke(() => { VoiceText.Text = text; if (once && text.Length > 0) Toast(text, 12); });
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

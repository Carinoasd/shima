// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using Shima.Agents;

namespace Shima;

/// <summary>
/// AI 分頁裡 Claude Code 以外的 agent（Codex、Gemini CLI）：這台電腦找得到它的設定資料夾才多一列，各自連接／中斷。
/// </summary>
public partial class IslandWindow
{
    private readonly Dictionary<AgentKind, Button> _agentButtons = [];

    private static string Finished(AgentKind agent, string project)
    {
        if (agent == AgentKind.Claude) return project.Length > 0 ? L.F("Claude Code 完成了 · {0}", project) : L.T("Claude Code 完成了");
        var name = AgentKinds.Name(agent);
        return project.Length > 0 ? L.F("{0} 完成了 · {1}", name, project) : L.F("{0} 完成了", name);
    }

    private static string HookText(bool installed, bool outdated) =>
        !installed ? L.T("連接") : outdated ? L.T("更新連接") : L.T("中斷");

    /// <summary>查完每個 agent 的狀態之後更新畫面：Claude Code 那顆按鈕，以及其他偵測得到的 agent 各一列。</summary>
    private void ShowAgents(IReadOnlyList<AgentInstaller.Status> states)
    {
        if (states.FirstOrDefault(s => s.Agent == AgentKind.Claude) is { } claude)
        {
            _hooks = (claude.Installed, claude.Outdated);
            ShowHookState();
        }

        var others = states.Where(s => s.Agent != AgentKind.Claude && s.Detected).ToList();
        if (!others.Select(s => s.Agent).SequenceEqual(_agentButtons.Keys))
        {
            OtherAgentsBox.Children.Clear();
            _agentButtons.Clear();
            foreach (var status in others)
            {
                var agent = status.Agent;
                var row = new Grid { Height = 36, Margin = new Thickness(0, 8, 0, 0) };
                row.Children.Add(new TextBlock { Text = AgentKinds.Name(agent), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 90, 0) });
                var button = new Button { Style = (Style)FindResource("Pill"), HorizontalAlignment = HorizontalAlignment.Right };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    try { await ToggleAgent(agent); }
                    finally { button.IsEnabled = true; }
                };
                row.Children.Add(button);
                OtherAgentsBox.Children.Add(row);
                _agentButtons[agent] = button;
            }
        }
        foreach (var status in others) _agentButtons[status.Agent].Content = HookText(status.Installed, status.Outdated);
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    /// <summary>連接或中斷一個 agent（裝好了就拿掉，沒裝或是舊版就裝上），再重新查一次每個 agent 的狀態。</summary>
    private async Task ToggleAgent(AgentKind agent)
    {
        var (problem, connected) = await Task.Run(() =>
        {
            var installer = AgentInstaller.For(agent);
            var targets = installer.Targets(AgentHooks.Homes());
            var (installed, outdated) = installer.State(targets);
            bool on = installed && !outdated;
            var problem = on ? installer.Remove(targets) : installer.Install(targets, AgentBridge.Token(agent));
            return (problem, !on && problem == null);
        });
        if (problem != null) Say(problem);
        // Codex 要使用者自己在 /hooks 信任新的 hook 才會執行（Shima 不替人信任），不講的話連接了也不會有反應
        else if (connected && agent == AgentKind.Codex) Toast(L.T("在 Codex 輸入 /hooks，信任 Shima 的 hook"), 10, false);
        ShowAgents(await Task.Run(() => AgentInstaller.Scan()));
    }
}

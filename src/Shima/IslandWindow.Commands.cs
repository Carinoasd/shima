// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shima.Agents;

namespace Shima;

/// <summary>AI 分頁輸入框的快速指令（/next、/sleep 30 …）：本機直接執行，與提示清單。</summary>
public partial class IslandWindow
{
    private IReadOnlyList<(CommandSpec Spec, string Shown)> _hints = [];
    private int _hintIndex;
    private bool _hintMoved, _commandSaid;

    private static readonly Brush HintRow = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HintDim = Frozen(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF));

    // 方向鍵、Enter、Tab 文字框自己先處理掉了，KeyDown 收不到：要在 Preview 階段接
    private void AskBox_PreviewKeyDown(object sender, KeyEventArgs e) => CommandKeyDown(e);

    private void AskBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshHints();

    // ---------------------------------------------------------------- 提示清單

    private void RefreshHints()
    {
        var found = AskBox.IsEnabled ? Commands.Suggest(AskBox.Text) : [];
        if (found.Count == 0) { HideHints(); return; }
        if (found.SequenceEqual(_hints) && CommandHints.Visibility == Visibility.Visible) return;

        _hints = found;
        _hintIndex = 0;
        _hintMoved = false;
        CommandList.Children.Clear();
        for (int i = 0; i < found.Count; i++)
        {
            int at = i;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(new TextBlock { Text = "/" + found[i].Shown, FontSize = 13, Foreground = Brushes.White });
            var help = new TextBlock { Text = found[i].Spec.Help(), FontSize = 13, Foreground = HintDim, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(help, 1);
            grid.Children.Add(help);
            var row = new Border { Child = grid, CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 5, 10, 5), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.MouseLeftButtonUp += (_, e) => { _hintIndex = at; AcceptHint(); AskBox.Focus(); e.Handled = true; };
            CommandList.Children.Add(row);
        }
        ShowSelectedHint();
        CommandHints.Visibility = Visibility.Visible;
        ExamplesBox.Visibility = Visibility.Collapsed;
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    private void ShowSelectedHint()
    {
        for (int i = 0; i < CommandList.Children.Count; i++)
            ((Border)CommandList.Children[i]).Background = i == _hintIndex ? HintRow : Brushes.Transparent;
    }

    private void HideHints()
    {
        _hints = [];
        if (CommandHints.Visibility != Visibility.Visible) return;
        CommandHints.Visibility = Visibility.Collapsed;
        CommandList.Children.Clear();
        // 有回答的時候範例本來就收著
        ExamplesBox.Visibility = VoiceText.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (_tab == Tab.Ai && _expanded) Morph();
    }

    /// <summary>清單開著時的按鍵。回傳 true 表示這個鍵已經處理掉，不要再往下走。</summary>
    private bool CommandKeyDown(KeyEventArgs e)
    {
        if (CommandHints.Visibility != Visibility.Visible || _hints.Count == 0) return false;
        switch (e.Key)
        {
            case Key.Down:
            case Key.Up:
                _hintIndex = (_hintIndex + (e.Key == Key.Down ? 1 : _hints.Count - 1)) % _hints.Count;
                _hintMoved = true;
                ShowSelectedHint();
                break;
            case Key.Tab:
                AcceptHint();
                break;
            case Key.Enter:
                // 只打了「/」就按 Enter：要的是指令清單（/help），不是第一個指令
                if (!_hintMoved && AskBox.Text.Trim() is "/" or "／") return false;
                AcceptHint();
                break;
            case Key.Escape:
                HideHints();
                break;
            default:
                return false;
        }
        e.Handled = true;
        return true;
    }

    /// <summary>選了清單裡的一個：要參數的補上名稱、游標停在後面；不用參數的直接執行。</summary>
    private void AcceptHint()
    {
        if (_hintIndex >= _hints.Count) return;
        var (spec, shown) = _hints[_hintIndex];
        if (spec.Arg == ArgNeed.Required)
        {
            AskBox.Text = "/" + shown + " ";
            AskBox.CaretIndex = AskBox.Text.Length;
        }
        else TryCommand("/" + shown);
    }

    // ---------------------------------------------------------------- 執行

    /// <summary>是指令就在本機做完並回 true；不是指令回 false，照舊交給 AI。</summary>
    private bool TryCommand(string line)
    {
        var command = Commands.Parse(line);
        switch (command.State)
        {
            case CommandState.NotCommand:
                return false;
            case CommandState.Unknown:
                _commandSaid = true;
                Say(L.F("沒有「{0}」這個指令，打 /help 看全部", command.Name));
                return true;
            case CommandState.BadArgs:
                _commandSaid = true;
                Say(L.F("用法：{0}", "`" + command.Spec!.Usage + "`"));
                return true;
        }

        HideHints();
        AskBox.Text = "";
        if (command.Spec!.Kind == CommandKind.Help)
        {
            _commandSaid = true;
            Say(Commands.HelpText(Loc.Lang is "zh-TW" or "zh-CN"));
            return true;
        }
        // 上一次的用法提示與這次無關，清掉；AI 的回答不動
        if (_commandSaid) { _commandSaid = false; Say(""); }
        try { RunCommand(command); }
        catch (Exception x) { Log.Write("command: " + x.Message); }
        return true;
    }

    private void RunCommand(Command command)
    {
        switch (command.Spec!.Kind)
        {
            case CommandKind.Play: case CommandKind.Pause: case CommandKind.Next: case CommandKind.Prev:
                var action = command.Spec.Kind switch { CommandKind.Play => "play", CommandKind.Pause => "pause", CommandKind.Next => "next", _ => "previous" };
                if (_track == null) Toast(L.T("沒有正在播放的音樂"), 2.5, false);
                else Music(action);   // 沿用語音助手的做法：明確的播放／暫停，並取消自動恢復
                break;

            case CommandKind.Volume:
            {
                int? volume = command.Action switch
                {
                    "set" => _media.SetVolume(command.Number) ?? _system.SetVolume(command.Number),
                    // 與助手同樣的幅度：Roon 兩格、電腦 8%
                    var dir => _media.Nudge(dir == "up" ? 2 : -2) ?? _system.Nudge(dir == "up" ? 8 : -8),
                };
                Toast(volume is { } v ? L.F("音量 {0}", v) : L.T("沒有可以調音量的裝置"), 2, false);
                break;
            }

            case CommandKind.Sleep:
                switch (command.Action)
                {
                    case "minutes":
                        var span = TimeSpan.FromMinutes(command.Number);
                        StartSleep(span);
                        Toast(L.F("睡眠定時：剩 {0}", Clock(span)), 3, false);
                        break;
                    case "track":
                        if (StartSleepAfterTrack()) Toast(L.T("睡眠定時：這首播完"), 3, false);
                        break;
                    default:
                        CancelSleep();
                        Toast(L.T("睡眠定時已取消"), 2.5, false);
                        break;
                }
                break;

            case CommandKind.Remind:
                if (_alarms is not { } alarms) break;
                if (Alarms.ParseLine(command.Text, DateTime.Now) is not { } parsed) { Toast(L.T("看不懂這個時間"), 4, false); break; }
                alarms.Add(parsed.At, parsed.Text, parsed.Repeat);
                Toast(L.F("已設定提醒：{0}", parsed.At.ToString("M/d HH:mm", System.Globalization.CultureInfo.InvariantCulture)), 3, false);
                break;

            case CommandKind.Todo:
                Tool("todo", new JsonObject { ["action"] = "add", ["text"] = command.Text });
                Toast(L.T("已加入待辦"), 2.5, false);
                break;

            case CommandKind.Focus:
                switch (command.Action)
                {
                    case "start":
                        Tool("timer", command.Number > 0 ? new JsonObject { ["action"] = "start", ["minutes"] = command.Number } : new JsonObject { ["action"] = "start" });
                        Toast(TimerLine() ?? L.T("番茄鐘"), 3, false);
                        break;
                    case "stop":
                        Tool("timer", new JsonObject { ["action"] = "pause" });
                        Toast(L.T("番茄鐘已暫停"), 2.5, false);
                        break;
                    default:
                        Tool("timer", new JsonObject { ["action"] = "reset" });
                        Toast(L.T("番茄鐘已歸零"), 2.5, false);
                        break;
                }
                break;

            case CommandKind.Dnd:
                // 已經在勿擾中再打一次 /dnd：從現在重新算一小時
                if (command.Action == "on") { AppSettings.Current.DoNotDisturbUntil = DoNotDisturb.EndFrom(DateTime.Now); AppSettings.Current.Save(); }
                else if (InDoNotDisturb) ToggleDoNotDisturb();
                Toast(command.Action == "on" ? L.T("勿擾（1 小時）") : L.T("勿擾已結束"), 2.5, false);
                break;

            case CommandKind.Lyrics:
                DesktopLyricsItem.IsChecked = !DesktopLyricsItem.IsChecked;
                DesktopLyrics_Click(this, new RoutedEventArgs());
                Toast(L.T(DesktopLyricsItem.IsChecked ? "桌面歌詞已開啟" : "桌面歌詞已關閉"), 2.5, false);
                break;

            case CommandKind.Earlier: case CommandKind.Later:
                if (_track == null) Toast(L.T("沒有正在播放的音樂"), 2.5, false);
                else ShiftLyrics(command.Spec.Kind == CommandKind.Earlier ? 100 : -100);
                break;

            case CommandKind.Settings:
                TabSettings.IsChecked = true;
                break;
        }
    }
}

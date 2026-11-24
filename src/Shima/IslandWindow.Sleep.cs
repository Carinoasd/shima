// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shima.Playback;

namespace Shima;

public partial class IslandWindow
{
    // 不存進設定檔：重開程式就取消
    private readonly SleepTimer _sleep = new();
    private string _sleepTip = "";

    private static readonly int[] SleepMinutes = [15, 30, 45, 60];
    private static readonly Brush SleepOn = Frozen(Color.FromRgb(0xD9, 0x77, 0x57));

    private void InitSleep()
    {
        // 跟「播放裝置」一樣，子選單每次打開前重建
        Host.ContextMenu!.Opened += (_, _) => FillSleepMenu(SleepMenu.Items);
        UpdateSleepButton();
    }

    private void Sleep_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SleepButton, Placement = PlacementMode.Top };
        FillSleepMenu(menu.Items);
        menu.IsOpen = true;
    }

    private void FillSleepMenu(ItemCollection items)
    {
        items.Clear();
        foreach (int minutes in SleepMinutes)
        {
            var span = TimeSpan.FromMinutes(minutes);
            var item = new MenuItem { Header = L.F("{0} 分鐘", minutes), IsCheckable = true, IsChecked = _sleep.Mode == SleepMode.After && _sleep.Total == span };
            item.Click += (_, _) => StartSleep(span);
            items.Add(item);
        }
        var end = new MenuItem { Header = L.T("這首播完"), IsCheckable = true, IsChecked = _sleep.Mode == SleepMode.EndOfTrack };
        end.Click += (_, _) => StartSleepAfterTrack();
        items.Add(end);
        if (_sleep.Mode != SleepMode.Off)
        {
            var off = new MenuItem { Header = L.T("關閉") };
            off.Click += (_, _) => CancelSleep();
            items.Add(off);
        }
    }

    private void StartSleep(TimeSpan span)
    {
        _sleep.After(span, DateTime.Now);
        UpdateSleepButton();
    }

    /// <summary>沒有歌在播就沒有「這首」可言：拒絕並說一聲。</summary>
    private bool StartSleepAfterTrack()
    {
        var track = _track;
        if (!_sleep.AfterTrack(track == null ? null : SleepTimer.KeyOf(track.Title, track.Artist), track?.Now ?? default))
        {
            Toast(L.T("沒有正在播放的音樂"), 2.5, cheer: false);
            return false;
        }
        UpdateSleepButton();
        return true;
    }

    private void CancelSleep()
    {
        _sleep.Cancel();
        UpdateSleepButton();
    }

    /// <summary>每個 tick 呼叫：問定時現在該不該暫停，順便更新按鈕上的剩餘時間。</summary>
    private void SleepTick()
    {
        if (_sleep.Mode == SleepMode.Off) return;
        var track = _track;
        bool playing = track?.Playing == true;
        var key = track == null ? null : SleepTimer.KeyOf(track.Title, track.Artist);
        if (_sleep.Tick(DateTime.Now, key, track?.Now ?? default, track?.Duration ?? default, playing))
        {
            _media.Pause();
            Toast(L.T("睡眠定時：已暫停播放"), 4, cheer: false);
        }
        UpdateSleepButton();
    }

    private void UpdateSleepButton()
    {
        bool on = _sleep.Mode != SleepMode.Off;
        SleepButton.Foreground = on ? SleepOn : Brushes.White;
        string tip = _sleep.Left(DateTime.Now) is { } left ? L.F("睡眠定時：剩 {0}", Clock(left))
            : on ? L.T("睡眠定時：這首播完") : L.T("睡眠定時");
        if (tip != _sleepTip) SleepButton.ToolTip = _sleepTip = tip;
    }

    /// <summary>助手工具：回傳給 AI 看的一句話。</summary>
    private string SleepTool(string action, double minutes)
    {
        switch (action)
        {
            case "start":
                if (!double.IsFinite(minutes) || minutes <= 0) return "沒有給分鐘數";
                StartSleep(TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 600)));
                return $"睡眠定時已設定，{Math.Round(Math.Clamp(minutes, 1, 600))} 分鐘後暫停播放";
            case "end_of_track":
                return StartSleepAfterTrack() ? "睡眠定時已設定，這首播完就暫停" : "目前沒有在播放音樂，沒辦法設定這首播完";
            case "cancel":
                bool was = _sleep.Mode != SleepMode.Off;
                CancelSleep();
                return was ? "睡眠定時已取消" : "原本就沒有設定睡眠定時";
            default:
                return _sleep.Left(DateTime.Now) is { } left ? $"睡眠定時還剩 {(int)left.TotalMinutes} 分 {left.Seconds} 秒"
                    : _sleep.Mode == SleepMode.EndOfTrack ? "睡眠定時設定為這首播完就暫停" : "沒有設定睡眠定時";
        }
    }
}

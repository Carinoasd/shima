// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Shima.Playback;

namespace Shima;

public partial class IslandWindow
{
    private int _audioCount = -1;
    private int _switching;

    private void InitAudioOutputs()
    {
        // 子選單每次打開前重新列出（裝置會增減，預設也可能被別的程式換掉）
        Host.ContextMenu!.Opened += (_, _) => FillAudioMenu(AudioMenu.Items);
        UpdateAudioButton();
    }

    /// <summary>只有一個（或沒有）裝置就沒得選，按鈕藏起來。</summary>
    private void UpdateAudioButton()
    {
        // SystemWatch 每兩秒本來就會數一次；它沒啟動時（截圖模式）自己數一次就好，不要每兩秒重新列舉
        if (_system.Outputs < 0 && _audioCount >= 0) return;
        int count = _system.Outputs >= 0 ? _system.Outputs : AudioOutputs.Count();
        if (count == _audioCount) return;
        _audioCount = count;
        AudioButton.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Audio_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AudioButton, Placement = PlacementMode.Top };
        FillAudioMenu(menu.Items);
        menu.IsOpen = true;
    }

    private void FillAudioMenu(ItemCollection items)
    {
        items.Clear();
        foreach (var output in AudioOutputs.List())
        {
            var item = new MenuItem { Header = output.Name.Replace("_", "__"), IsCheckable = true, IsChecked = output.IsDefault };
            var target = output;
            item.Click += (_, _) => SwitchAudio(target);
            items.Add(item);
        }
    }

    private void SwitchAudio(AudioOutput target)
    {
        if (target.IsDefault || Interlocked.Exchange(ref _switching, 1) != 0) return;
        _system.Expect(target.Id);
        // COM 呼叫可能卡一下，不佔著畫面的執行緒
        _ = Task.Run(() => AudioOutputs.SetDefault(target.Id)).ContinueWith(done =>
        {
            Volatile.Write(ref _switching, 0);
            Dispatcher.BeginInvoke(() =>
            {
                if (done.IsCompletedSuccessfully && done.Result) Toast(L.F("輸出：{0}", target.Name), 2.5, cheer: false);
                else { _system.Expect(""); Toast(L.T("無法切換播放裝置"), 3, cheer: false); }
                UpdateAudioButton();
            });
        });
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shima;

public partial class IslandWindow
{
    /// <summary>設定的「一般」子分頁：兩列快捷鍵。</summary>
    private IEnumerable<UIElement> HotkeySettingsRows()
    {
        yield return HotkeyRow("展開／收起", () => ToggleHotkey, v => AppSettings.Current.HotkeyToggle = v, () => AppSettings.Current.HotkeyToggle, RegisterToggle);
        yield return HotkeyRow("問 AI", () => AskHotkey, v => AppSettings.Current.HotkeyAsk = v, () => AppSettings.Current.HotkeyAsk, RegisterAsk);
    }

    /// <summary>
    /// 一列：左邊名稱，右邊顯示目前組合的膠囊。按一下進入錄製，按下組合就存起來並立刻重新註冊；
    /// Esc 取消，Backspace／Delete 還原預設。
    /// </summary>
    private DockPanel HotkeyRow(string name, Func<string> actual, Action<string> store, Func<string> saved, Func<bool> register)
    {
        var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, MinWidth = 120, Padding = new Thickness(12, 0, 12, 0), Focusable = true };
        bool recording = false;
        void Show() => button.Content = recording ? L.T("按下新的組合") : actual() is { Length: > 0 } text ? text : "—";

        void Stop()
        {
            if (!recording) return;
            recording = false;
            _recordingHotkey = false;
            // 錄製時兩組都拆掉了，結束後照設定註冊回去
            RegisterToggle();
            RegisterAsk();
            Show();
        }

        void Commit(string value)
        {
            var before = saved();
            recording = false;
            _recordingHotkey = false;
            store(value);
            bool ok = register();
            if (!ok)
            {
                // 被別的程式佔用：恢復原本的
                store(before);
                Toast(L.T("這組快速鍵被別的程式佔用"), 4, false);
            }
            // 另一組也要照設定註冊回去
            RegisterToggle();
            RegisterAsk();
            AppSettings.Current.Save();
            Show();
        }

        button.Click += (_, _) =>
        {
            if (recording) return;
            recording = true;
            _recordingHotkey = true;
            UnregisterHotKey(_hwnd, HotkeyToggle);
            UnregisterHotKey(_hwnd, HotkeyAsk);
            // 島平常不拿鍵盤焦點；設定頁會開放，這裡再確認一次並把焦點給這顆按鈕
            SetActivatable(true);
            Activate();
            button.Focus();
            Keyboard.Focus(button);
            Show();
        };
        button.LostKeyboardFocus += (_, _) => Stop();
        button.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;
            var held = Keyboard.Modifiers;
            if (held == ModifierKeys.None && key == Key.Escape) { Stop(); Keyboard.ClearFocus(); return; }
            if (held == ModifierKeys.None && key is Key.Back or Key.Delete) { Commit(""); Keyboard.ClearFocus(); return; }
            uint mods = (held.HasFlag(ModifierKeys.Control) ? Hotkeys.Ctrl : 0) | (held.HasFlag(ModifierKeys.Alt) ? Hotkeys.Alt : 0)
                | (held.HasFlag(ModifierKeys.Shift) ? Hotkeys.Shift : 0) | (held.HasFlag(ModifierKeys.Windows) ? Hotkeys.Win : 0);
            if (Hotkeys.Format(mods, (uint)KeyInterop.VirtualKeyFromKey(key)) is not { } combo) return; // 沒有修飾鍵或不支援的鍵：繼續等
            Commit(combo);
            Keyboard.ClearFocus();
        };
        Show();
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = L.T(name), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}

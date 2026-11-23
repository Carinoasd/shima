// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shima;

public partial class IslandWindow
{
    /// <summary>兩列各自更新按鈕文字的方法：改了一組，另一組實際註冊到的也可能跟著變（備援順序），兩列一起更新。</summary>
    private readonly List<Action> _hotkeyShows = [];

    /// <summary>設定的「一般」子分頁：兩列快捷鍵。</summary>
    private IEnumerable<UIElement> HotkeySettingsRows()
    {
        _hotkeyShows.Clear();   // 設定頁重建了，舊的那兩顆按鈕不在畫面上了
        yield return HotkeyRow("展開／收起", () => ToggleHotkey, () => AskHotkey, v => AppSettings.Current.HotkeyToggle = v, () => AppSettings.Current.HotkeyToggle, () => RegisterToggle(), RegisterAsk);
        yield return HotkeyRow("問 AI", () => AskHotkey, () => ToggleHotkey, v => AppSettings.Current.HotkeyAsk = v, () => AppSettings.Current.HotkeyAsk, RegisterAsk, () => RegisterToggle());
    }

    /// <summary>
    /// 一列：左邊名稱，右邊顯示目前組合的膠囊。按一下進入錄製，按下組合就存起來並立刻重新註冊；
    /// Esc 取消，Backspace／Delete 還原預設。
    /// </summary>
    /// <param name="other">另一組目前實際用的組合。錄製時兩組都拆掉了，但這個字串要到重新註冊才會變。</param>
    private DockPanel HotkeyRow(string name, Func<string> actual, Func<string> other, Action<string> store, Func<string> saved, Func<bool> register, Func<bool> registerOther)
    {
        var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, MinWidth = 120, Padding = new Thickness(12, 0, 12, 0), Focusable = true };
        bool recording = false;
        void Show() => button.Content = recording ? L.T("按下新的組合") : actual() is { Length: > 0 } text ? text : "—";
        _hotkeyShows.Add(Show);

        // 錄製時兩組都拆掉了：結束後照設定註冊回去，兩列都更新
        void Restore()
        {
            recording = false;
            _recordingHotkey = false;
            RegisterHotkeys();
            foreach (var show in _hotkeyShows) show();
        }

        void Stop() { if (recording) Restore(); }

        void Commit(string value)
        {
            if (!recording) return;
            // 跟另一組一樣：先註冊的會拿走它，另一組就悄悄失效了
            if (value.Length > 0 && value == other())
            {
                Restore();
                Toast(L.T("這組快速鍵已經給另一個功能用了"), 4, false);
                return;
            }
            var before = saved();
            store(value);
            // 另一組先註冊回去，再註冊這一組：這樣被佔用（不管是別的程式還是另一組的預設）一定看得出來
            UnregisterHotKey(_hwnd, HotkeyToggle);
            UnregisterHotKey(_hwnd, HotkeyAsk);
            registerOther();
            bool taken = !register();
            if (taken) store(before);
            AppSettings.Current.Save();
            Restore();
            if (taken) Toast(L.T("這組快速鍵被別的程式佔用"), 4, false);
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
            // 沒拿到焦點（Windows 不讓視窗搶到前景）：收不到按鍵，也不會有失焦事件來收尾，直接結束，不然兩組快速鍵會一直拆著
            if (!button.IsKeyboardFocused) { Restore(); return; }
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

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shima.Listening;

namespace Shima;

public partial class IslandWindow
{
    private CancellationTokenSource? _lastFmConnecting;

    /// <summary>設定頁「音樂」分頁，接在 ListenBrainz 那一列後面：API key、Shared secret、連接。</summary>
    private IEnumerable<UIElement> LastFmSettingsRows()
    {
        var s = AppSettings.Current;
        yield return Entry("Last.fm API key", s.LastFmApiKey, 190, v => { s.LastFmApiKey = v; LastFmDisconnect(); });
        yield return SecretEntry("Last.fm Shared secret", s.LastFmSecretText(), 190, v => { s.SetLastFmSecret(v); LastFmDisconnect(); });
        yield return LastFmConnectRow();
    }

    /// <summary>金鑰換了，原本的 session key 就不能用了。</summary>
    private void LastFmDisconnect()
    {
        _lastFmConnecting?.Cancel();
        AppSettings.Current.SetLastFmSession("", "");
    }

    /// <summary>和 <see cref="Entry"/> 一樣的一列，但輸入框不顯示內容（樣式和 AI 分頁的金鑰框相同）。</summary>
    private DockPanel SecretEntry(string text, string value, double width, Action<string> changed)
    {
        var saved = value;
        var box = new PasswordBox { Style = (Style)FindResource("Field"), Height = 26, Width = width, Password = value };
        void Commit()
        {
            var trimmed = box.Password.Trim();
            if (trimmed != box.Password) box.Password = trimmed;
            if (trimmed == saved) return;
            saved = trimmed;
            changed(trimmed);
        }
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { box.Password = saved; Keyboard.ClearFocus(); e.Handled = true; }
        };
        DockPanel.SetDock(box, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = L.T(text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>沒連接：「連接」；等授權：「等待授權…」（再按一次取消）；已連接：使用者名稱（再按一次中斷）。</summary>
    private DockPanel LastFmConnectRow()
    {
        var s = AppSettings.Current;
        var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0) };
        void Show() => button.Content = _lastFmConnecting != null ? L.T("等待授權…") : s.LastFmSession.Length > 0 ? s.LastFmUser : L.T("連接");
        button.Click += async (_, _) =>
        {
            if (_lastFmConnecting != null) { _lastFmConnecting.Cancel(); return; }
            if (s.LastFmSession.Length > 0) { LastFmDisconnect(); Show(); return; }
            if (s.LastFmApiKey.Length == 0 || s.LastFmSecretText().Length == 0) { Toast(L.T("先填 API key 與 Shared secret"), 4, false); return; }

            using var cancel = _lastFmConnecting = new CancellationTokenSource();
            Show();
            LastFmLogin? login;
            using (var api = new LastFm { ApiKey = s.LastFmApiKey, Secret = s.LastFmSecretText() })
                login = await api.Connect(url =>
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch (Exception x) { Log.Write("lastfm 開瀏覽器: " + x.Message); }
                }, cancel.Token);
            bool cancelled = cancel.IsCancellationRequested;
            _lastFmConnecting = null;
            if (login != null) s.SetLastFmSession(login.Key, login.Name);
            else if (!cancelled) Toast(L.T("Last.fm 連接失敗"), 5, false);
            Show();
        };
        Show();
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = L.T("Last.fm"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}

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
    /// <summary>
    /// 現在畫面上的那顆連接按鈕。設定頁每次打開、每次點歌詞來源都會整頁重建，
    /// 等授權等到一半回來時，要更新的是這一顆，不是當初按下去的那一顆。
    /// </summary>
    private Button? _lastFmButton;

    /// <summary>設定頁「音樂」分頁，接在 ListenBrainz 那一列後面：API key、Shared secret、連接。</summary>
    private IEnumerable<UIElement> LastFmSettingsRows()
    {
        var s = AppSettings.Current;
        yield return Entry("Last.fm API key", s.LastFmApiKey, 190, v => { s.LastFmApiKey = v; LastFmDisconnect(); });
        yield return SecretEntry("Last.fm Shared secret", s.LastFmSecretText(), 190, v => { s.SetLastFmSecret(v); LastFmDisconnect(); });
        yield return LastFmConnectRow();
    }

    /// <summary>沒連接：「連接」；等授權：「等待授權…」；已連接：使用者名稱。</summary>
    private void RefreshLastFmButton()
    {
        if (_lastFmButton is not { } button) return;
        var s = AppSettings.Current;
        button.Content = _lastFmConnecting != null ? L.T("等待授權…")
            : s.LastFmSession.Length == 0 ? L.T("連接")
            : s.LastFmUser.Length > 0 ? s.LastFmUser : "Last.fm";
    }

    /// <summary>中斷，或金鑰換了（原本的 session key 就不能用了）。</summary>
    private void LastFmDisconnect()
    {
        _lastFmConnecting?.Cancel();
        AppSettings.Current.SetLastFmSession("", "");
        _pending.Clear("lastfm");
        // 還在排隊或等著重試的舊歌，不要再拿舊的 session 送
        _lastFm?.Use("", "", "");
        RefreshLastFmButton();
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
        row.Children.Add(new TextBlock { Text = L.T(text), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0) });
        return row;
    }

    /// <summary>按一下：沒連接就開始連接，等授權時是取消，已連接是中斷。</summary>
    private DockPanel LastFmConnectRow()
    {
        var button = new Button { Style = (Style)FindResource("Pill"), Height = 26, Padding = new Thickness(12, 0, 12, 0) };
        button.Click += (_, _) => LastFmConnect();
        _lastFmButton = button;
        RefreshLastFmButton();
        DockPanel.SetDock(button, Dock.Right);
        var row = new DockPanel { Height = 32, Margin = new Thickness(2, 0, 0, 2) };
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = L.T("Last.fm"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private async void LastFmConnect()
    {
        var s = AppSettings.Current;
        if (_lastFmConnecting is { } waiting) { waiting.Cancel(); return; }
        if (s.LastFmSession.Length > 0) { LastFmDisconnect(); return; }
        string key = s.LastFmApiKey, secret = s.LastFmSecretText();
        if (key.Length == 0 || secret.Length == 0) { Toast(L.T("先填 API key 與 Shared secret"), 4, false); return; }

        var cancel = new CancellationTokenSource();
        _lastFmConnecting = cancel;
        RefreshLastFmButton();
        LastFmLogin? login = null;
        try
        {
            using var api = new LastFm { ApiKey = key, Secret = secret };
            login = await api.Connect(url =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception x) { Log.Write("lastfm 開瀏覽器: " + x.Message); }
            }, cancel.Token);
        }
        catch (Exception x) { Log.Write("lastfm 連接: " + x.Message); }

        // 等的時候按了取消、改了金鑰（LastFmDisconnect 會取消）或關了程式：就算剛好授權完也不存
        bool cancelled = cancel.IsCancellationRequested;
        if (_lastFmConnecting == cancel) _lastFmConnecting = null;
        cancel.Dispose();
        if (_closed) return;
        if (login != null && !cancelled) s.SetLastFmSession(login.Key, login.Name);
        else if (!cancelled) Toast(L.T("Last.fm 連接失敗"), 5, false);
        RefreshLastFmButton();
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Shima;

/// <summary>
/// 把通訊軟體（Discord、LINE、Telegram…）的 Windows 通知在島上閃一下。
/// 要使用者在 Windows 允許「通知存取」；只看設定裡列出的程式，其他通知不碰。
/// </summary>
public sealed class NotificationMirror
{
    private readonly HashSet<uint> _seen = [];
    private UserNotificationListener? _listener;
    private bool _primed, _busy;

    /// <summary>程式名稱、標題、內文。</summary>
    public event Action<string, string, string>? Arrived;

    /// <summary>向 Windows 要通知存取權。回傳有沒有拿到。</summary>
    public async Task<bool> StartAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed) return false;
            _listener = listener;
            return true;
        }
        catch (Exception e) { Log.Write("通知: " + e.Message); return false; }
    }

    public void Stop() => _listener = null;

    /// <summary>每隔一兩秒呼叫：有新的通知就回報。第一次只記下已經在通知中心裡的，不回報。</summary>
    public async Task PollAsync()
    {
        if (_listener is not { } listener || _busy) return;
        _busy = true;
        try
        {
            var all = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in all)
            {
                if (!_seen.Add(n.Id) || !_primed) continue;
                var app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                if (!Wanted(app, AppSettings.Current.NotificationApps)) continue;
                var texts = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()?.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
                if (texts.Count == 0) continue;
                Arrived?.Invoke(app, texts[0], string.Join(" ", texts.Skip(1)));
            }
            _primed = true;
            if (_seen.Count > 2000) { _seen.Clear(); foreach (var n in all) _seen.Add(n.Id); }
        }
        catch (Exception e) { Log.Write("通知: " + e.Message); }
        finally { _busy = false; }
    }

    /// <summary>這個程式的通知要不要顯示：名稱裡有清單上的任一個字就算。</summary>
    public static bool Wanted(string app, IEnumerable<string> names) =>
        app.Length > 0 && names.Any(n => n.Length > 0 && app.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>膠囊上的一行：「程式 · 標題：內文」，太長就截短。</summary>
    public static string Line(string app, string title, string body)
    {
        var text = body.Length > 0 ? $"{app} · {title}：{body}" : $"{app} · {title}";
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        return text.Length > 60 ? text[..59] + "…" : text;
    }
}

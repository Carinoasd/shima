// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Shima;

public enum RecentKind { Message, Reminder, Agent }

/// <summary>一筆值得回頭看的通知。</summary>
public sealed record RecentItem(DateTime At, RecentKind Kind, string Source, string Title, string Body);

/// <summary>
/// 最近的通知：島上閃過就沒了，人不在螢幕前就錯過；這裡把通訊軟體的訊息、到點的提醒、agent 做完的通知留下來讓人回頭看。
/// 只放在記憶體：訊息內容是私人的，不存檔、不寫紀錄，重開程式就清空。不碰畫面，所有呼叫都在 UI 執行緒。
/// </summary>
public sealed class RecentLog
{
    public const int Max = 30;

    private readonly List<RecentItem> _items = [];   // 最舊的在前面

    /// <summary>上次看過之後新增了幾筆（舊的被擠掉的不算）。</summary>
    public int Unread { get; private set; }

    public int Count => _items.Count;

    /// <summary>新增、已讀、清空任何一個發生時。</summary>
    public event Action? Changed;

    /// <summary>由新到舊。</summary>
    public IReadOnlyList<RecentItem> Items => _items.AsEnumerable().Reverse().ToList();

    public void Add(RecentItem item)
    {
        // 時間要照順序：系統時鐘被往回調時，新的那筆不該排到舊的前面
        if (_items.Count > 0 && item.At < _items[^1].At) item = item with { At = _items[^1].At };
        _items.Add(item);
        if (_items.Count > Max) _items.RemoveRange(0, _items.Count - Max);
        Unread = Math.Min(Unread + 1, _items.Count);
        Changed?.Invoke();
    }

    public void Add(DateTime at, RecentKind kind, string source, string title, string body) =>
        Add(new RecentItem(at, kind, source, title, body));

    public void MarkRead()
    {
        if (Unread == 0) return;
        Unread = 0;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_items.Count == 0 && Unread == 0) return;
        _items.Clear();
        Unread = 0;
        Changed?.Invoke();
    }

    /// <summary>清單右邊的時間：今天的寫幾點幾分，更早的寫日期。</summary>
    public static string When(DateTime at, DateTime now, CultureInfo? culture = null) =>
        at.Date == now.Date ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : at.ToString("M", culture ?? CultureInfo.CurrentCulture);
}

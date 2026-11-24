// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Agents;

/// <summary>
/// deck 金鑰此刻算不算數。deck 金鑰是唯一能替人按「允許」的金鑰：沒在用 Stream Dock 的電腦不該多這一個入口。
/// 有沒有在用要摸磁碟，結果記一陣子，不要每個請求（外掛每 0.1 秒就問一次）都去查。
/// </summary>
public sealed class DeckDoor(Func<bool> inUse, Func<string> token, Func<long>? ticks = null)
{
    public const long Remember = 30_000;

    private readonly Func<long> _ticks = ticks ?? (() => Environment.TickCount64);
    private readonly object _lock = new();
    private bool _known;
    private long _at;
    private string _key = "";

    /// <summary>現在認的金鑰；空字串表示 /deck/ 一律拒絕。</summary>
    public string Key
    {
        get
        {
            lock (_lock)
            {
                long now = _ticks();
                if (!_known || now - _at >= Remember)
                {
                    _key = inUse() ? token() : "";
                    _at = now;
                    _known = true;
                }
                return _key;
            }
        }
    }

    /// <summary>剛裝好或移除外掛：下一個請求重新查，不用等記住的那段時間過去。</summary>
    public void Forget()
    {
        lock (_lock) _known = false;
    }
}

/// <summary>Stream Dock 按下允許／拒絕之後怎麼辦：照做、不理，或不理但要提醒（舊版外掛沒帶識別碼）。</summary>
public enum DeckVerdict { Apply, Ignore, OldPlugin }

public static class DeckAnswer
{
    /// <summary>一則請求出現在島上不到這麼久（毫秒），使用者不可能已經看完：這時的「允許」不算。</summary>
    public const long Glance = 600;

    /// <summary>
    /// 外掛按下 allow／deny。id 是外掛畫出來的那一則的識別碼（舊版外掛不帶）、current 是此刻正在問的那一則（沒有就是 null）、
    /// shownFor 是它在島上出現了多久（毫秒）、allowOn 是設定裡的「Stream Dock 可以按允許」。
    /// visible 是島此刻看得見、而且停在這一則上：藏起來（系統匣、全螢幕）時只裝了按鍵、沒有顯示內容的人看不到要允許什麼，允許不算。
    /// 允許一定要指名是哪一則：兩個工作階段同時在問時，島上剛換成下一則的瞬間按下去，不能批准使用者還沒看到的那一則。
    /// 拒絕不會造成傷害，舊版外掛沒帶識別碼時照舊套用在目前這一則。
    /// </summary>
    public static DeckVerdict Judge(string action, string? id, string? current, long shownFor, bool allowOn, bool visible = true)
    {
        if (current == null) return DeckVerdict.Ignore;
        bool named = !string.IsNullOrEmpty(id);
        if (action == "deny") return !named || id == current ? DeckVerdict.Apply : DeckVerdict.Ignore;
        if (action != "allow" || !allowOn || !visible) return DeckVerdict.Ignore;
        if (!named) return DeckVerdict.OldPlugin;
        return id == current && shownFor >= Glance ? DeckVerdict.Apply : DeckVerdict.Ignore;
    }
}

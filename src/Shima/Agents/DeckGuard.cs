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

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Agents;

/// <summary>
/// 同時最多幾個：拿到 hook 金鑰的程式可以一秒送幾千個請求，每個權限請求都會掛著一條連線等九分多鐘。
/// 滿了就不收，別讓島被洗版、真正的請求被淹沒、記憶體裡掛著幾千條連線。
/// </summary>
public sealed class Slots(int max)
{
    private int _taken;

    public int Max => max;
    public int Taken => Volatile.Read(ref _taken);

    /// <summary>拿一個位子；滿了就是 false（不用還）。</summary>
    public bool TryTake()
    {
        if (Interlocked.Increment(ref _taken) <= max) return true;
        Interlocked.Decrement(ref _taken);
        return false;
    }

    /// <summary>還一個 TryTake 拿到的位子。</summary>
    public void Give() => Interlocked.Decrement(ref _taken);
}

/// <summary>
/// Stream Dock 外掛送來的紀錄（/deck/log）。內容是網址上的字，誰拿到 deck 金鑰都能寫：
/// 換行會變成偽造的紀錄行、一直送會讓紀錄檔超過上限被整份刪掉。所以控制字元換成空白、限制頻率。
/// </summary>
public sealed class DeckLog(Func<long>? ticks = null)
{
    public const int MaxLength = 600;
    /// <summary>每 Window 毫秒最多收 Burst 行，超過的丟掉。</summary>
    public const int Burst = 20;
    public const long Window = 10_000;

    private readonly Func<long> _ticks = ticks ?? (() => Environment.TickCount64);
    private readonly Queue<long> _recent = new();

    /// <summary>這一行要不要寫進紀錄檔；要的話回傳清理過的內容，丟掉就是 null。</summary>
    public string? Admit(string? message)
    {
        long now = _ticks();
        lock (_recent)
        {
            while (_recent.Count > 0 && now - _recent.Peek() >= Window) _recent.Dequeue();
            if (_recent.Count >= Burst) return null;
            _recent.Enqueue(now);
        }
        return Clean(message);
    }

    /// <summary>控制字元（含換行）與 Unicode 的行、段落分隔符號換成空白，最多 MaxLength 個字。</summary>
    public static string Clean(string? message)
    {
        var text = message ?? "";
        if (text.Length > MaxLength) text = text[..MaxLength];
        return string.Create(text.Length, text, (span, from) =>
        {
            for (int i = 0; i < from.Length; i++)
            {
                char c = from[i];
                span[i] = char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c;
            }
        });
    }
}

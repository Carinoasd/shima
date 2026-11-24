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

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;

namespace Shima;

// 沒有歌在播時膠囊上那一行：每 0.1 秒問一次，答案沒變就不重算。
public partial class IslandWindow
{
    private readonly IdleLineCache _idleLine = new();
    private Alarms? _idleAlarms;
    private int _alarmsVersion;

    private string IdleText()
    {
        // 提醒清單變了才要重算「下一個提醒」。Changed 在任何執行緒觸發，只加一個數字
        if (_alarms is { } alarms && !ReferenceEquals(alarms, _idleAlarms))
        {
            _idleAlarms = alarms;
            Interlocked.Increment(ref _alarmsVersion);
            alarms.Changed += () => Interlocked.Increment(ref _alarmsVersion);
        }
        return _idleLine.Text(AppSettings.Current.IdleShows, DateTime.Now, TimerLine(), Volatile.Read(ref _alarmsVersion), () => _alarms?.List ?? [], Loc.Culture, L.T("明天"));
    }
}

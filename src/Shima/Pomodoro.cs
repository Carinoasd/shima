// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima;

/// <summary>番茄鐘：專注一段、休息一段，輪流。只記時間，不管畫面。</summary>
public sealed class Pomodoro
{
    public TimeSpan Focus { get; init; } = TimeSpan.FromMinutes(25);
    public TimeSpan Break { get; init; } = TimeSpan.FromMinutes(5);

    private TimeSpan _left;
    private DateTime? _endsAt;

    public Pomodoro() => _left = Focus;

    public bool Resting { get; private set; }
    public bool Running => _endsAt != null;
    /// <summary>還沒開始過、也沒在跑（顯示的是完整的一段）。</summary>
    public bool Fresh => !Running && _left == (Resting ? Break : Focus);

    public TimeSpan Left(DateTime now) => _endsAt is { } end ? (end > now ? end - now : TimeSpan.Zero) : _left;

    public void Toggle(DateTime now)
    {
        if (_endsAt != null) { _left = Left(now); _endsAt = null; }
        else _endsAt = now + _left;
    }

    public void Reset()
    {
        _endsAt = null;
        Resting = false;
        _left = Focus;
    }

    /// <summary>時間到了就換下一段並自動開始。回傳 true 表示剛剛換段（要提醒）。</summary>
    public bool Tick(DateTime now)
    {
        if (_endsAt is not { } end || now < end) return false;
        Resting = !Resting;
        _left = Resting ? Break : Focus;
        _endsAt = now + _left;
        return true;
    }
}

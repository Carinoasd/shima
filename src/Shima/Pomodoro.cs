// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima;

/// <summary>番茄鐘：專注一段、休息一段，輪流。只記時間，不管畫面。</summary>
public sealed class Pomodoro
{
    public TimeSpan Focus { get; init; } = TimeSpan.FromMinutes(25);
    public TimeSpan Break { get; init; } = TimeSpan.FromMinutes(5);

    private int _completed;
    private DateTime _day;
    private TimeSpan _left;
    private DateTime? _endsAt;

    public Pomodoro() => _left = Focus;

    public bool Resting { get; private set; }
    public bool Running => _endsAt != null;
    /// <summary>還沒開始過、也沒在跑（顯示的是完整的一段）。</summary>
    public bool Fresh => !Running && _left == (Resting ? Break : Focus);

    /// <summary>今天完成了幾個專注段落。跨日歸零；不存檔，重開程式也從零算。</summary>
    public int CompletedToday(DateTime now) => _day == now.Date ? _completed : 0;

    /// <summary>用小圓點表示：四個一組（●●●○）；超過四個改成數字。</summary>
    public static string Dots(int count) => count switch
    {
        <= 0 => "○○○○",
        <= 4 => new string('●', count) + new string('○', 4 - count),
        _ => "● " + count,
    };

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
        if (!Resting)
        {
            // 專注的一段走完才算一個；休息完不算
            _completed = CompletedToday(now) + 1;
            _day = now.Date;
        }
        Resting = !Resting;
        _left = Resting ? Break : Focus;
        _endsAt = now + _left;
        return true;
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Playback;

public enum SleepMode { Off, After, EndOfTrack }

/// <summary>
/// 睡眠定時：過一段時間、或這首歌播完就該暫停。只做判斷，不碰畫面也不碰播放器。
/// 暫停著時倒數照樣走（使用者要的是「30 分鐘後不要有聲音」）。
/// </summary>
public sealed class SleepTimer
{
    private DateTime _endsAt;
    private string? _track;
    private TimeSpan _last;

    public SleepMode Mode { get; private set; }
    /// <summary>設定的總長（只有 After 有意義），選單用來判斷打勾的是哪一項。</summary>
    public TimeSpan Total { get; private set; }

    /// <summary>同一首歌的判斷依據：歌名加藝人。</summary>
    public static string? KeyOf(string? title, string? artist) =>
        string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist) ? null : $"{title}\n{artist}";

    public void After(TimeSpan span, DateTime now)
    {
        Mode = SleepMode.After;
        Total = span;
        _endsAt = now + span;
        _track = null;
    }

    /// <summary>這首播完就停。沒有歌在播時沒有意義，回傳 false。</summary>
    public bool AfterTrack(string? track, TimeSpan position)
    {
        if (track == null) return false;
        Mode = SleepMode.EndOfTrack;
        Total = TimeSpan.Zero;
        _track = track;
        _last = position;
        return true;
    }

    public void Cancel()
    {
        Mode = SleepMode.Off;
        _track = null;
    }

    /// <summary>還剩多久；「這首播完」或沒設定時是 null。</summary>
    public TimeSpan? Left(DateTime now) =>
        Mode == SleepMode.After ? (_endsAt > now ? _endsAt - now : TimeSpan.Zero) : null;

    /// <summary>
    /// 每次 tick 呼叫。回傳 true 表示現在該暫停（定時會同時取消）。
    /// 時間到了但沒在播就只是取消，不用暫停。
    /// </summary>
    public bool Tick(DateTime now, string? track, TimeSpan position, TimeSpan duration, bool playing)
    {
        bool due = false;
        switch (Mode)
        {
            case SleepMode.After:
                due = now >= _endsAt;
                break;
            case SleepMode.EndOfTrack:
                // 歌換了（或沒歌了）就是播完了；同一首從尾巴跳回開頭是重播
                due = track != _track || Replayed(position, duration);
                if (!due) _last = position;
                break;
        }
        if (!due) return false;
        Cancel();
        return playing;
    }

    private bool Replayed(TimeSpan position, TimeSpan duration)
    {
        if (position >= _last) return false;
        // 有長度：上次已經在最後幾秒；沒長度：上次還在中段，這次回到開頭附近。拖進度條往回不算
        return duration > TimeSpan.Zero
            ? _last >= duration - TimeSpan.FromSeconds(5) && position < _last - TimeSpan.FromSeconds(3)
            : _last > TimeSpan.FromSeconds(10) && position < TimeSpan.FromSeconds(3);
    }
}

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
    private DateTime? _goneSince;
    /// <summary>最後一次看到在播的時間：到點時沒在播，用它分辨「換歌的空檔」和「本來就停著」。</summary>
    private DateTime? _playedAt;
    /// <summary>到點時剛好在換歌的空檔：從這時起等一下，看下一首會不會開始播。</summary>
    private DateTime? _dueAt;
    private string? _track;
    private TimeSpan _last;
    /// <summary>上一次問「所有播放器有沒有在出聲」的時間與答案（見 <see cref="AskEvery"/>）。</summary>
    private DateTime? _askedAt;
    private bool _askedSounding;

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
        _playedAt = _dueAt = _askedAt = null;
    }

    /// <summary>
    /// 這首播完就停。沒有歌在播時沒有意義，回傳 false；已經停在這首的結尾（播完了）也一樣，
    /// 不然下一個 tick 就會被當成播完而默默取消，畫面上卻先顯示設定好了。
    /// </summary>
    public bool AfterTrack(string? track, TimeSpan position, TimeSpan duration = default, bool playing = true)
    {
        if (track == null || Finished(position, duration, playing)) return false;
        Mode = SleepMode.EndOfTrack;
        Total = TimeSpan.Zero;
        _track = track;
        _last = position;
        _goneSince = _playedAt = _dueAt = _askedAt = null;
        return true;
    }

    public void Cancel()
    {
        Mode = SleepMode.Off;
        _track = null;
        _goneSince = _playedAt = _dueAt = _askedAt = null;
    }

    /// <summary>還剩多久；「這首播完」或沒設定時是 null。</summary>
    public TimeSpan? Left(DateTime now) =>
        Mode == SleepMode.After ? (_endsAt > now ? _endsAt - now : TimeSpan.Zero) : null;

    /// <summary>
    /// 每次 tick 呼叫。回傳 true 表示現在該暫停（定時會同時取消）。
    /// 時間到了但沒在播：剛剛還在播的話是換歌的空檔（Roon 的 loading、SMTC 的 Changing、短暫的 Paused），
    /// 等 <see cref="Grace"/> 看下一首會不會開始播，開始了就暫停；早就停著的（使用者自己按了暫停）只是取消，
    /// 不然使用者過幾秒手動恢復播放，又會被補暫停一次。
    /// <paramref name="anyPlaying"/>：「幾分鐘後」到點時問的是所有播放器有沒有在出聲（沒有標題的串流、
    /// 島上沒顯示的那個播放器也算），不只看島上這一首；只在到點之後才問（要跨程序問每個播放器，不能每個 tick 都問）。
    /// 「這首播完」跟的是島上這一首，照樣看 <paramref name="playing"/>。
    /// </summary>
    public bool Tick(DateTime now, string? track, TimeSpan position, TimeSpan duration, bool playing, Func<bool>? anyPlaying = null)
    {
        if (Mode == SleepMode.Off) return false;
        // 「有沒有東西在出聲」：倒數模式問所有播放器，其他只看島上這一首。
        // 問所有播放器要跨程序一個一個問，而這裡每 0.1 秒就跑一次，所以答案留用 AskEvery 那麼久
        bool Sounding()
        {
            if (playing || Mode != SleepMode.After || anyPlaying == null) return playing;
            if (_askedAt is { } asked && now >= asked && now - asked < AskEvery) return _askedSounding;
            _askedAt = now;
            return _askedSounding = anyPlaying();
        }
        if (_dueAt is { } dueAt)
        {
            // 「這首播完」是因為同一首在結尾短暫沒在播（緩衝、拖進度條）才到點的，結果它又接著播了：
            // 還沒播完，繼續等真的播完，不要提早一兩秒把它暫停
            if (Mode == SleepMode.EndOfTrack && playing && track != null && track == _track && !Replayed(position, duration))
            {
                _dueAt = null;
                _playedAt = now;
                _last = position;
                return false;
            }
            // 已經到點了，只等有沒有開始播：曲目、進度都不用再看
            if (Sounding()) { Cancel(); return true; }
            if (now - dueAt >= Grace) Cancel();
            return false;
        }
        if (playing) _playedAt = now;
        // 倒數快到了而島上沒在播：先看別的播放器（沒有標題的串流、島上沒顯示的那一個）有沒有在出聲。
        // 不然它們剛好在換片的空檔到點時，分不出是「空檔」還是「本來就沒聲音」，定時會直接取消
        else if (Mode == SleepMode.After && anyPlaying != null && now >= _endsAt - Gone && Sounding()) _playedAt = now;
        bool due = false;
        switch (Mode)
        {
            case SleepMode.After:
                due = now >= _endsAt;
                break;
            case SleepMode.EndOfTrack:
                if (track == null)
                {
                    // 換歌的那一瞬間播放器可能短暫沒有曲目：等幾秒，真的沒歌了才算結束（那時也沒有東西要暫停）
                    _goneSince ??= now;
                    due = now - _goneSince.Value >= Gone;
                    break;
                }
                _goneSince = null;
                // 歌換了就是播完了；同一首從尾巴跳回開頭是重播。
                // 播到底自己停下來（清單的最後一首、沒有自動播下一部的影片）也算結束：
                // 不然定時會一直留著，幾個小時後再播任何東西都會被馬上暫停
                due = track != _track || Replayed(position, duration) || Finished(position, duration, playing);
                if (!due) _last = position;
                break;
        }
        if (!due) return false;
        if (Sounding()) { Cancel(); return true; }
        if (_playedAt is { } at && now - at <= Gone) _dueAt = now;
        else Cancel();
        return false;
    }

    private bool Replayed(TimeSpan position, TimeSpan duration)
    {
        if (position >= _last) return false;
        // 有長度：上次已經在最後幾秒；沒長度：上次還在中段，這次回到開頭附近。拖進度條往回不算
        // 都要回到開頭附近才算：在最後幾秒往回拖一小段不是重播
        return position < TimeSpan.FromSeconds(3) &&
            (duration > TimeSpan.Zero ? _last >= duration - TimeSpan.FromSeconds(5) : _last > TimeSpan.FromSeconds(10));
    }

    /// <summary>沒有曲目多久才當成真的沒歌了。</summary>
    public static readonly TimeSpan Gone = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 到點時在換歌的空檔，最多等多久下一首開始播。串流服務的下一首緩衝可能要好幾秒；
    /// 過了還沒播就是真的沒有下一首（或使用者自己停了），那時也沒有東西要暫停。
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(8);

    /// <summary>「所有播放器有沒有在出聲」最多多久問一次。</summary>
    public static readonly TimeSpan AskEvery = TimeSpan.FromSeconds(0.5);

    /// <summary>停在這首的結尾：播完了，後面沒有東西接著播。</summary>
    private static bool Finished(TimeSpan position, TimeSpan duration, bool playing) =>
        !playing && duration > TimeSpan.Zero && position >= duration - TimeSpan.FromSeconds(2);
}

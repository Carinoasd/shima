// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Windows.Threading;

namespace Shima.Motion;

/// <summary>
/// 膠囊與面板上所有慢動作共用的節拍（約每秒 30 次）。
/// 音量條、角色、頻譜、歌詞掃光各跑各的時鐘的話，它們的重畫會落在不同的幀，透明視窗每秒要多呈現好幾十次；
/// 都在同一拍改，一拍只呈現一次。
/// 歌詞在掃光時（本來就跟著螢幕的幀在走）節拍就是它問 <see cref="Beat"/> 時答「是」的那些幀；
/// 沒有人跟著幀走時改用一個計時器，不為了節拍去訂螢幕的每一幀（那本身就要一直喚醒繪圖）。
/// </summary>
public static class Pulse
{
    private static readonly List<Action> Steps = [];
    private static Action[] _copy = [];
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static TimeSpan _beatAt, _seen, _lastRun = TimeSpan.FromDays(-1);
    private static bool _beat;

    static Pulse() => Timer.Tick += (_, _) =>
    {
        // 最近有跟著幀的節拍，就不用計時器再打一次，不然兩邊各打各的又不同幀了
        if (FrameGate.Quiet(_lastRun, Clock.Elapsed)) Run();
    };

    /// <summary>這一幀是不是節拍（給本來就跟著幀在畫的東西問）。同一幀裡不管誰先問，答案都一樣；是的話順便讓加入的人走一步。</summary>
    public static bool Beat(TimeSpan now)
    {
        if (now == _seen) return _beat;
        _seen = now;
        _beat = FrameGate.Due(_beatAt, now);
        if (_beat) { _beatAt = now; Run(); }
        return _beat;
    }

    /// <summary>每拍呼叫一次 step，直到 <see cref="Leave"/>。重複加入不會呼叫兩次。</summary>
    public static void Join(Action step)
    {
        if (Steps.Contains(step)) return;
        Steps.Add(step);
        if (Steps.Count == 1) Timer.Start();
    }

    public static void Leave(Action step)
    {
        if (Steps.Remove(step) && Steps.Count == 0) Timer.Stop();
    }

    private static void Run()
    {
        _lastRun = Clock.Elapsed;
        if (Steps.Count == 0) return;
        // 呼叫途中可能有人離開（看不到了），先抄一份
        if (_copy.Length != Steps.Count) _copy = new Action[Steps.Count];
        Steps.CopyTo(_copy);
        foreach (var step in _copy) step();
    }

    /// <summary>
    /// 每一步往目標靠近一個比例的緩動，原本是照固定的間隔（reference 秒）調的；間隔變了，照實際經過的時間換算，速度才一樣。
    /// </summary>
    public static double Ease(double factor, double seconds, double reference) =>
        1 - Math.Pow(1 - Math.Clamp(factor, 0, 1), Math.Max(0, seconds) / reference);
}

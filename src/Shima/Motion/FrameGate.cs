// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Motion;

/// <summary>
/// 慢動作（歌詞掃光、音量條、角色、頻譜）要不要重畫的幾個判斷。島是透明的分層視窗，裡面任何東西重畫一次就要整塊重新呈現，
/// 跟著螢幕每一幀（60–165 Hz）畫的話大部分的格子使用者根本分不出來。
/// 換句、捲動、展開收起這種彈簧動畫不走這裡，照樣每一幀都畫。
/// </summary>
public static class FrameGate
{
    /// <summary>節拍大約的間隔（每秒 30 次）。</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(1 / 30.0);

    /// <summary>
    /// 計時器要不要自己打拍子：最近一次跟著幀打的節拍已經超過 1.5 個間隔，表示沒有人在跟著幀走。
    /// 跟著幀的節拍每 1/30 秒一次，計時器的間隔差不多，留一點餘裕才不會兩邊都打。
    /// </summary>
    public static bool Quiet(TimeSpan lastBeat, TimeSpan now) => now - lastBeat >= Gap * 1.5 || now < lastBeat;

    /// <summary>光的前緣（像素）跟上次畫的差了至少半個像素嗎。還沒畫過（NaN）就是要畫。</summary>
    public static bool Moved(double drawn, double now) =>
        double.IsNaN(drawn) || double.IsNaN(now) || Math.Abs(now - drawn) >= 0.5;

    /// <summary>
    /// 沒在逐幀更新時，每 0.1 秒問一次：畫面上的光頭是不是舊的（暫停時拖了進度）。
    /// 現在根本沒有光頭（前奏、間奏、空白的一句）就不用補畫，不然那段時間每 0.1 秒都整塊重畫一次。
    /// </summary>
    public static bool Stale(double drawn, double now) => !double.IsNaN(now) && Moved(drawn, now);
}

/// <summary>
/// 照螢幕的幀打一個約每秒 30 次的節拍：每 k 幀一拍，k 照量到的幀間隔算（60 Hz 隔 2 幀、120 Hz 隔 4 幀、144 Hz 隔 5 幀）。
/// 用固定的時間門檻的話，總有某種更新率的幀間隔剛好卡在門檻上，幀一抖就在「隔 k 幀」與「隔 k+1 幀」之間跳，看起來一頓一頓的；
/// 照幀數數、再留半幀的餘裕就不會。
/// </summary>
public sealed class BeatGate
{
    private const double Target = 1 / 30.0;
    private readonly double[] _deltas = new double[31], _sorted = new double[31];
    private int _count, _next, _every = 2;
    private TimeSpan _lastFrame, _lastBeat;
    private bool _seen, _beaten;

    /// <summary>量到的幀間隔（秒）：最近 31 幀的中位數，偶爾掉一幀不會把它拉長。</summary>
    public double Interval { get; private set; } = 1 / 60.0;

    /// <summary>每幾幀一拍。</summary>
    public int Every => _every;

    /// <summary>新的一幀（同一幀只能問一次）：是不是節拍。</summary>
    public bool Next(TimeSpan now)
    {
        if (_seen && now > _lastFrame)
        {
            double delta = (now - _lastFrame).TotalSeconds;
            if (delta < 0.1)   // 停了一陣子才又開始的那一下不算
            {
                _deltas[_next] = delta;
                _next = (_next + 1) % _deltas.Length;
                _count = Math.Min(_count + 1, _deltas.Length);
                Array.Copy(_deltas, _sorted, _count);
                Array.Sort(_sorted, 0, _count);
                Interval = _sorted[_count / 2];
                // 差得夠多才換 k：剛好卡在兩個 k 中間的更新率（例如 75 Hz）不會來回跳
                double raw = Target / Interval;
                if (Math.Abs(raw - _every) > 0.75) _every = Math.Max(1, (int)Math.Round(raw));
            }
        }
        _seen = true;
        _lastFrame = now;
        if (_beaten && now >= _lastBeat && (now - _lastBeat).TotalSeconds < (_every - 0.5) * Interval) return false;
        _beaten = true;
        _lastBeat = now;
        return true;
    }
}

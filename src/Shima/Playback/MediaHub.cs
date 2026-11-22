// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

namespace Shima.Playback;

/// <summary>
/// 所有音樂來源的總機。Roon API 連得上時，Roon 自己那個沒有進度的 Windows 工作階段就不看了；
/// Roon 和別的播放器之間，誰在播就顯示誰，兩邊都在播（或都沒播）就維持原樣。
/// </summary>
public sealed class MediaHub
{
    private readonly SmtcSource _smtc = new();
    private readonly RoonSource _roon = new();
    private bool _roonShown;

    public event Action? Changed;

    public static bool IsRoon(string appId) => appId.Contains("roon", StringComparison.OrdinalIgnoreCase);

    public async Task StartAsync()
    {
        _smtc.Skip = id => IsRoon(id) && _roon.Current != null;
        _smtc.Changed += () => Changed?.Invoke();
        _roon.Changed += () => { _smtc.Choose(); Changed?.Invoke(); };
        _roon.Start();
        try { await _smtc.StartAsync(); }
        catch (Exception e) { Log.Write("smtc 無法使用: " + e.Message); }
    }

    /// <summary>約每秒呼叫一次：看看有沒有別的播放器開始播了。</summary>
    public void Poll() => _smtc.Choose();

    private IMediaSource? Active(out Track? track)
    {
        var roon = _roon.Current;
        var other = _smtc.Current;
        if (roon == null) { _roonShown = false; track = other; return other == null ? null : _smtc; }
        if (other == null) _roonShown = true;
        else if (roon.Playing != other.Playing) _roonShown = roon.Playing;
        track = _roonShown ? roon : other;
        return _roonShown ? _roon : _smtc;
    }

    public Track? Current { get { Active(out var t); return t; } }

    /// <summary>Roon 的播放區域名稱；現在顯示的不是 Roon 就是空字串。</summary>
    public string ZoneName => ReferenceEquals(Active(out _), _roon) ? _roon.ZoneName : "";
    public string NextZone() => ReferenceEquals(Active(out _), _roon) ? _roon.NextZone() : "";
    /// <summary>音量加減（目前只有 Roon 支援）。</summary>
    public int? Nudge(int steps) => ReferenceEquals(Active(out _), _roon) ? _roon.Nudge(steps) : null;

    public void PlayPause() => Active(out _)?.PlayPause();
    public void Next() => Active(out _)?.Next();
    public void Previous() => Active(out _)?.Previous();
    public void Seek(TimeSpan position) => Active(out _)?.Seek(position);
}

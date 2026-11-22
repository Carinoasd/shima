// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Windows.Media;

namespace Shima.Playback;

/// <summary>正在播放的一首歌，以及某一刻的播放位置。</summary>
public sealed record Track(
    string Source,
    string AppId,
    string Title,
    string Artist,
    string Album,
    ImageSource? Cover,
    bool Playing,
    TimeSpan Position,
    DateTimeOffset At,
    TimeSpan Duration)
{
    /// <summary>現在的位置：播放中就從上次回報的位置往後推算。</summary>
    public TimeSpan Now
    {
        get
        {
            var pos = Position;
            if (Playing && At != default) pos += DateTimeOffset.Now - At;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
            return pos;
        }
    }

    public string Key => $"{Title}\n{Artist}\n{Album}\n{Math.Round(Duration.TotalSeconds)}";
}

public interface IMediaSource
{
    Track? Current { get; }
    event Action? Changed;
    void PlayPause();
    void Next();
    void Previous();
    void Seek(TimeSpan position);
}

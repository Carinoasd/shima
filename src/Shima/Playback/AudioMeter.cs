// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Shima.Playback;

/// <summary>電腦現在實際播出的聲音有多大聲（0…1），讓角色和音量條跟著跳。只在需要時擷取。</summary>
public sealed class AudioMeter : IDisposable
{
    // NAudio 3.3 把這個類別標成過時，但它仍是擷取整台電腦聲音最簡單的方式
#pragma warning disable CS0618
    private WasapiLoopbackCapture? _capture;
    private float _peak;
    private long _heardAt;

    /// <summary>最近的音量，沒有新聲音就很快掉回 0。</summary>
    public float Level
    {
        get
        {
            double age = (Environment.TickCount64 - Interlocked.Read(ref _heardAt)) / 1000.0;
            return (float)Math.Clamp(Volatile.Read(ref _peak) * Math.Exp(-age * 9), 0, 1);
        }
    }

    public bool Running => _capture != null;

    public void Start()
    {
        if (_capture != null) return;
        try
        {
            var capture = new WasapiLoopbackCapture();
            capture.DataAvailable += (_, e) => Hear(e.Buffer, e.BytesRecorded, capture.WaveFormat);
            capture.RecordingStopped += (_, _) => { if (_capture == capture) _capture = null; };
            capture.StartRecording();
            _capture = capture;
        }
        catch (Exception e) { Log.Write("audio meter: " + e.Message); _capture = null; }
    }

    public void Stop()
    {
        var capture = _capture;
        _capture = null;
        try { capture?.StopRecording(); capture?.Dispose(); } catch { }
    }

    private void Hear(byte[] buffer, int count, WaveFormat format)
    {
        float peak = Peak(buffer, count, format.BitsPerSample);
        // 小聲的音樂也要看得出在動：開根號把低音量拉高
        peak = MathF.Sqrt(peak);
        if (peak >= Level)
        {
            Volatile.Write(ref _peak, peak);
            Interlocked.Exchange(ref _heardAt, Environment.TickCount64);
        }
    }

    /// <summary>一段取樣裡最大的振幅（0…1）。32 位元當浮點數，16 位元當整數。</summary>
    public static float Peak(byte[] buffer, int count, int bits)
    {
        float peak = 0;
        if (bits == 32)
            for (int i = 0; i + 4 <= count; i += 4) peak = MathF.Max(peak, MathF.Abs(BitConverter.ToSingle(buffer, i)));
        else if (bits == 16)
            for (int i = 0; i + 2 <= count; i += 2) peak = MathF.Max(peak, MathF.Abs(BitConverter.ToInt16(buffer, i) / 32768f));
        return float.IsFinite(peak) ? Math.Clamp(peak, 0, 1) : 0;
    }

    public void Dispose() => Stop();
#pragma warning restore CS0618
}

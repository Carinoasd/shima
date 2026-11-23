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
    private readonly float[] _recent = new float[Spectrum.Size];   // 最近的單聲道取樣，環狀
    private int _write, _rate = 48000;

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

    /// <summary>此刻的頻譜：低音到高音分成 count 段，每段 0…1。沒有在擷取、或已經安靜一陣子就全是 0。</summary>
    public float[] Bands(int count)
    {
        if (_capture == null || Environment.TickCount64 - Interlocked.Read(ref _heardAt) > 1500) return Silence(count);
        lock (_recent)
        {
            // 膠囊、背景、角色、Stream Dock 各自來問，一秒加起來近百次；同一瞬間算過的直接給，不要每次都重跑一次 FFT
            long now = Environment.TickCount64;
            if (_cached.TryGetValue(count, out var hit) && now - hit.At < 16) return hit.Bands;
            for (int i = 0; i < _samples.Length; i++) _samples[i] = _recent[(_write + i) % _samples.Length];
            var bands = Spectrum.Bands(_samples, _rate, count, _re, _im);
            _cached[count] = (now, bands);
            return bands;
        }
    }

    private readonly float[] _samples = new float[Spectrum.Size];
    private readonly double[] _re = new double[Spectrum.Size], _im = new double[Spectrum.Size];
    private readonly Dictionary<int, (long At, float[] Bands)> _cached = [];
    private static readonly Dictionary<int, float[]> Quiet = [];

    private static float[] Silence(int count)
    {
        lock (Quiet) return Quiet.TryGetValue(count, out var zeros) ? zeros : Quiet[count] = new float[count];
    }

    private void Remember(byte[] buffer, int count, WaveFormat format)
    {
        int bytes = format.BitsPerSample / 8, channels = Math.Max(1, format.Channels), frame = bytes * channels;
        if (bytes != 4 && bytes != 2) return;
        lock (_recent)
        {
            _rate = format.SampleRate;
            // 只需要最後一小段
            for (int i = Math.Max(0, count - _recent.Length * frame) / frame * frame; i + frame <= count; i += frame)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += bytes == 4 ? BitConverter.ToSingle(buffer, i + c * 4) : BitConverter.ToInt16(buffer, i + c * 2) / 32768f;
                _recent[_write] = float.IsFinite(sum) ? sum / channels : 0;
                _write = (_write + 1) % _recent.Length;
            }
        }
    }

    private void Hear(byte[] buffer, int count, WaveFormat format)
    {
        Remember(buffer, count, format);
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

/// <summary>把一小段聲音分成幾段頻率的強弱，畫頻譜用。</summary>
public static class Spectrum
{
    public const int Size = 1024;

    /// <summary>samples 要剛好 Size 個。回傳 count 段，依對數間隔從 50 Hz 排到 14 kHz，每段 0…1（約 -60 dB 到 0 dB）。</summary>
    public static float[] Bands(float[] samples, int sampleRate, int count, double[]? re = null, double[]? im = null)
    {
        // 呼叫端可以給兩塊重複使用的工作區，省得每次配置
        re ??= new double[Size];
        im ??= new double[Size];
        Array.Clear(im);
        for (int i = 0; i < Size; i++) re[i] = samples[i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1)));   // Hann 窗
        Fft(re, im);

        var bands = new float[count];
        double low = 50, high = Math.Min(14000, sampleRate / 2.0 - 1), perBin = (double)sampleRate / Size;
        for (int b = 0; b < count; b++)
        {
            double from = low * Math.Pow(high / low, (double)b / count), to = low * Math.Pow(high / low, (double)(b + 1) / count);
            int first = Math.Clamp((int)Math.Floor(from / perBin), 1, Size / 2 - 1), last = Math.Clamp((int)Math.Ceiling(to / perBin), first + 1, Size / 2);
            double peak = 0;
            for (int k = first; k < last; k++) peak = Math.Max(peak, Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
            // 正弦波滿刻度時這個值大約是 Size / 4
            double db = 20 * Math.Log10(peak / (Size / 4.0) + 1e-9);
            // 高音的能量天生比較少，補一點讓整排看起來平均
            db += 9.0 * b / count;
            bands[b] = (float)Math.Clamp((db + 60) / 60, 0, 1);
        }
        return bands;
    }

    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len, wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
    }
}

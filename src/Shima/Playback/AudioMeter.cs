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
    // Start／Stop 在畫面的執行緒，RecordingStopped 可能在 NAudio 的錄音執行緒（或被 Post 回畫面的執行緒），
    // 誰把 _capture 換成 null 誰就負責釋放那一個，靠 _gate 保證只有一方拿到。Bands／Running 只讀，所以另外標 volatile。
    private readonly object _gate = new();
    private volatile WasapiLoopbackCapture? _capture;
    private float _peak;
    private long _heardAt;
    private readonly float[] _recent = new float[Spectrum.Size];   // 最近的單聲道取樣，環狀
    private int _write, _rate = 48000;
    private string _deviceId = "";

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
        WasapiLoopbackCapture? failed = null;
        lock (_gate)
        {
            if (_capture != null) return;
            WasapiLoopbackCapture? capture = null;
            try
            {
                capture = new WasapiLoopbackCapture();
                _deviceId = DefaultId();
                var mine = capture;
                mine.DataAvailable += (_, e) => Hear(e.Buffer, e.BytesRecorded, mine.WaveFormat);
                mine.RecordingStopped += (_, e) => Stopped(mine, e.Exception);
                // 先記下來再開始：就算 RecordingStopped 馬上就來，也認得出是現在這一個
                _capture = mine;
                mine.StartRecording();
            }
            catch (Exception e)
            {
                Log.Write("audio meter: " + e.Message);
                _capture = null;
                failed = capture;
            }
        }
        // 跟 Stop() 一樣：釋放會等錄音執行緒結束，那條執行緒的 RecordingStopped 要拿 _gate，所以出了鎖才釋放
        try { failed?.Dispose(); } catch { }
    }

    /// <summary>
    /// 迴路擷取綁在開始時的那個裝置，預設裝置換了它不會自己跟過去（舊裝置還在的話根本不會停），
    /// 所以由島定期告訴它現在預設的是哪一個：不是當初擷取的那個就重開。沒在擷取時什麼都不做。
    /// </summary>
    /// <param name="defaultId">現在預設的播放裝置（SystemWatch 本來就在盯，不用這裡每秒再開一次列舉器去問）；空字串表示不知道。</param>
    public void FollowDefault(string defaultId)
    {
        if (_capture == null || defaultId.Length == 0 || defaultId == _deviceId) return;
        Stop();
        Start();
    }

    private static string DefaultId()
    {
        try { using var enumerator = new MMDeviceEnumerator(); using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); return d.ID; }
        catch { return ""; }
    }

    public void Stop()
    {
        WasapiLoopbackCapture? capture;
        lock (_gate) { capture = _capture; _capture = null; }
        // 釋放時不拿著 _gate：Dispose 會等錄音執行緒結束，而那條執行緒的 RecordingStopped 也要拿 _gate
        try { capture?.StopRecording(); } catch { }
        try { capture?.Dispose(); } catch { }
    }

    /// <summary>
    /// WASAPI 自己停了（拔耳機、換預設輸出裝置）：島之後會再 Start() 一個新的，這個要釋放，不然 COM 物件就漏掉了。
    /// 如果是 Stop() 先拿走的，由 Stop() 釋放，這裡什麼都不做，免得釋放兩次。
    /// 這個回呼可能就在 NAudio 自己的錄音執行緒上，而 WasapiCapture.Dispose 會等那條執行緒結束（NAudio 2.x 的寫法；
    /// 3.3 沒有原始碼可以對照，不確定有沒有改），在這裡同步 Dispose 有自己等自己的風險，所以排到執行緒集區去做。
    /// </summary>
    private void Stopped(WasapiLoopbackCapture capture, Exception? error)
    {
        lock (_gate)
        {
            if (_capture != capture) return;
            _capture = null;
        }
        if (error != null) Log.Write("audio meter stopped: " + error.Message);
        _ = Task.Run(() =>
        {
            try { capture.Dispose(); }
            catch (Exception e) { Log.Write("audio meter dispose: " + e.Message); }
        });
    }

    /// <summary>此刻的頻譜：低音到高音分成 count 段，每段 0…1。沒有在擷取、或已經安靜一陣子就全是 0。</summary>
    public float[] Bands(int count)
    {
        if (_capture == null || Environment.TickCount64 - Interlocked.Read(ref _heardAt) > 1500) return Silence(count);
        lock (_recent)
        {
            // 膠囊、背景、角色、Stream Dock 各自來問（段數 4、56、64 都有），一秒加起來近百次。
            // 16 ms 內只跑一次 FFT，各頻率格的振幅留著，不同段數都從同一份歸納；同一個段數算過就直接給。
            // 給出去的陣列之後不再改（HTTP 橋接可能正在別的執行緒序列化它），重算時一律配新的。
            long now = Environment.TickCount64;
            if (!_hasMagnitudes || now - _magnitudesAt >= 16 || _magnitudesRate != _rate)
            {
                for (int i = 0; i < _samples.Length; i++) _samples[i] = _recent[(_write + i) % _samples.Length];
                Spectrum.Magnitudes(_samples, _re, _im, _magnitudes);
                _hasMagnitudes = true;
                _magnitudesAt = now;
                _magnitudesRate = _rate;
                _cached.Clear();
            }
            if (_cached.TryGetValue(count, out var hit)) return hit;
            return _cached[count] = Spectrum.FromMagnitudes(_magnitudes, _magnitudesRate, count);
        }
    }

    private readonly float[] _samples = new float[Spectrum.Size];
    private readonly double[] _re = new double[Spectrum.Size], _im = new double[Spectrum.Size];
    private readonly double[] _magnitudes = new double[Spectrum.Size / 2];
    private bool _hasMagnitudes;
    private long _magnitudesAt;
    private int _magnitudesRate;
    private readonly Dictionary<int, float[]> _cached = [];
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

    /// <summary>Hann 窗。每次都算 1024 次 Cos 太浪費，公式不變所以結果跟逐次計算的一模一樣。</summary>
    private static readonly double[] Hann = Enumerable.Range(0, Size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1))).ToArray();

    /// <summary>(取樣率, 段數) → 每段的頻率格範圍 [First, Last)。只跟這兩個數有關，算一次就好（裡面有 Math.Pow）。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int Rate, int Count), (int First, int Last)[]> Edges = new();

    /// <summary>samples 要剛好 Size 個。回傳 count 段，依對數間隔從 50 Hz 排到 14 kHz，每段 0…1（約 -60 dB 到 0 dB）。</summary>
    public static float[] Bands(float[] samples, int sampleRate, int count, double[]? re = null, double[]? im = null)
    {
        var magnitudes = new double[Size / 2];
        Magnitudes(samples, re, im, magnitudes);
        return FromMagnitudes(magnitudes, sampleRate, count);
    }

    /// <summary>加窗、FFT，把第 0 到 Size/2 - 1 格的振幅寫進 magnitudes。呼叫端可以給兩塊重複使用的工作區，省得每次配置。</summary>
    internal static void Magnitudes(float[] samples, double[]? re, double[]? im, double[] magnitudes)
    {
        re ??= new double[Size];
        im ??= new double[Size];
        Array.Clear(im);
        for (int i = 0; i < Size; i++) re[i] = samples[i] * Hann[i];
        Fft(re, im);
        for (int k = 0; k < Size / 2; k++) magnitudes[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
    }

    /// <summary>同一份振幅歸納成 count 段。每次回傳新的陣列。</summary>
    internal static float[] FromMagnitudes(double[] magnitudes, int sampleRate, int count)
    {
        var bands = new float[count];
        // 實際上只有幾種取樣率與段數；保險起見設個上限，免得有人一直換段數讓快取越長越大
        if (!Edges.TryGetValue((sampleRate, count), out var edges))
        {
            edges = BandEdges(sampleRate, count);
            if (Edges.Count < 256) Edges.TryAdd((sampleRate, count), edges);
        }
        for (int b = 0; b < count; b++)
        {
            var (first, last) = edges[b];
            double peak = 0;
            for (int k = first; k < last; k++) peak = Math.Max(peak, magnitudes[k]);
            // 正弦波滿刻度時這個值大約是 Size / 4
            double db = 20 * Math.Log10(peak / (Size / 4.0) + 1e-9);
            // 高音的能量天生比較少，補一點讓整排看起來平均
            db += 9.0 * b / count;
            bands[b] = (float)Math.Clamp((db + 60) / 60, 0, 1);
        }
        return bands;
    }

    private static (int First, int Last)[] BandEdges(int sampleRate, int count)
    {
        var edges = new (int, int)[Math.Max(0, count)];
        double low = 50, high = Math.Min(14000, sampleRate / 2.0 - 1), perBin = (double)sampleRate / Size;
        for (int b = 0; b < count; b++)
        {
            double from = low * Math.Pow(high / low, (double)b / count), to = low * Math.Pow(high / low, (double)(b + 1) / count);
            int first = Math.Clamp((int)Math.Floor(from / perBin), 1, Size / 2 - 1), last = Math.Clamp((int)Math.Ceiling(to / perBin), first + 1, Size / 2);
            edges[b] = (first, last);
        }
        return edges;
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

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Shima.Lyrics;
using Whisper.net;
using Whisper.net.Ggml;

namespace Shima.Agents;

/// <summary>
/// 在這台電腦上聽懂人說的話：麥克風錄音，偵測到說完了就交給 Whisper 辨識。
/// 不靠 Windows 的聽寫，所以不用開任何系統設定、不用裝語言套件，聲音也不會離開這台電腦。
/// 第一次用要下載模型（base 約 150 MB），放在 %LOCALAPPDATA%\Shima\models。
/// </summary>
public sealed class Ears : IDisposable
{
    public const int Rate = 16000;

    private WhisperFactory? _factory;
    /// <summary>_factory 是從哪個模型檔載入的：設定換了模型就知道要重載。</summary>
    private string _loaded = "";
    private string _language = "", _prompt = "";
    private volatile bool _disposed;

    /// <summary>
    /// 喚醒詞和語音助手共用同一個 Ears：喚醒詞停下來時不會等背景的辨識結束，語音助手就馬上開始準備。
    /// 準備模型（下載、載入、換模型）和辨識都排隊進這把鎖，才不會兩邊同時寫同一個 .part 檔、
    /// 或一邊在用 factory 辨識時另一邊把它換掉。錄音不在鎖裡，等麥克風的時候不擋人。
    /// </summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>不同的 Ears（例如語音助手自己 new 的）也可能下載同一個模型檔：下載另外跨實例排隊。</summary>
    private static readonly SemaphoreSlim Downloads = new(1, 1);

    /// <summary>錄完音、開始辨識的那一刻（讓畫面馬上有反應）。</summary>
    public event Action? Recognizing;
    /// <summary>上一句：講了幾秒、辨識花了幾秒。</summary>
    public (double Spoken, double Took) Last { get; private set; }

    private static float _level;
    /// <summary>麥克風現在收到的聲音有多大聲（0…1）；沒在錄音是 0。給膠囊上的音量條用。</summary>
    public static float Level => Volatile.Read(ref _level);

    /// <summary>下載模型的進度（MB）。</summary>
    public event Action<int>? Downloading;

    public static string ModelFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shima", "models");

    private readonly string? _model;

    /// <param name="model">指定模型大小（tiny、base、small、medium）；不給就照設定。喚醒詞只要聽一個詞，可以用最小的 tiny。</param>
    public Ears(string? model = null) => _model = model;

    private GgmlType Size => (_model ?? AppSettings.Current.WhisperModel) switch
    {
        "tiny" => GgmlType.Tiny, "small" => GgmlType.Small, "medium" => GgmlType.Medium, _ => GgmlType.Base,
    };

    private static string PathOf(GgmlType size) => Path.Combine(ModelFolder, $"ggml-{size.ToString().ToLowerInvariant()}.bin");

    /// <summary>
    /// 模型還沒有就下載。先寫到暫存檔，下載完整才改名，中斷了不會留下壞檔。
    /// 每次都照現在的設定挑模型：設定從 base 換成 small，下一次準備就換掉已經載入的那個，不用重開程式。
    /// 另一邊正在準備時會排隊等它做完，做完的結果直接拿來用（檔案已經在、模型已經載好就不會再做一次）。
    /// </summary>
    public async Task PrepareAsync(string language, CancellationToken stop)
    {
        await _lock.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // 設定可能在下載途中被改：這一次從頭到尾用同一個模型
            var size = Size;
            var path = PathOf(size);
            if (!File.Exists(path)) await DownloadAsync(size, path, stop).ConfigureAwait(false);

            UseBundledLibrary();
            if (_factory == null || _loaded != path)
            {
                // 辨識也在這把鎖裡，所以換掉的時候沒有人在用舊的
                _factory?.Dispose();
                _factory = null;
                _loaded = "";
                _factory = WhisperFactory.FromPath(path);
                _loaded = path;
            }
            _language = Code(language);
            // 提示它用繁體字寫（模型對中文預設偏簡體）
            _prompt = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && Loc.Lang != "zh-CN" ? "以下是繁體中文的句子。" : "";
        }
        finally
        {
            if (_disposed) ReleaseFactory();
            _lock.Release();
            Swept();
        }
    }

    private async Task DownloadAsync(GgmlType size, string path, CancellationToken stop)
    {
        await Downloads.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) return;   // 排隊的時候別人已經下載好了
            Directory.CreateDirectory(ModelFolder);
            var partial = path + ".part";
            using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(size, QuantizationType.NoQuantization, stop).ConfigureAwait(false))
            using (var file = File.Create(partial))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read, shown = -1;
                while ((read = await source.ReadAsync(buffer, stop).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), stop).ConfigureAwait(false);
                    total += read;
                    int mb = (int)(total >> 20);
                    if (mb != shown && mb % 5 == 0) { shown = mb; Downloading?.Invoke(mb); }
                }
            }
            File.Move(partial, path, overwrite: true);
        }
        finally { Downloads.Release(); }
    }

    /// <summary>
    /// 模型本來一次看 30 秒的聲音（1500 格），一句話通常只有幾秒，其餘都是白算。
    /// 依這句話的長度只看需要的部分（每秒 50 格，多留一點餘裕），短句的辨識時間可以少掉一大半。
    /// </summary>
    public static int AudioContext(double seconds) => Math.Clamp((int)Math.Ceiling(seconds * 50) + 200, 384, 1500);

    /// <summary>
    /// Whisper.net 只會到「執行檔旁邊的 runtimes\win-x64」找原生函式庫（whisper.dll 等四個檔）。
    /// 打包成單一執行檔時，這些檔案在啟動時被解到暫存資料夾，不在那裡，指定路徑也沒用（實測過），
    /// 所以第一次用語音時把它們複製到它會找的位置。版本更新後檔案大小不同會重新複製。
    /// </summary>
    private static void UseBundledLibrary()
    {
        string[] names = ["ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-whisper.dll", "whisper.dll"];
        var wanted = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64");
        var unpacked = ((AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, "runtimes", "win-x64"))
            .FirstOrDefault(folder => File.Exists(Path.Combine(folder, "whisper.dll")) && !Path.GetFullPath(folder).Equals(Path.GetFullPath(wanted), StringComparison.OrdinalIgnoreCase));
        if (unpacked == null) return;   // 不是單一執行檔（開發時）：檔案本來就在對的位置
        try
        {
            Directory.CreateDirectory(wanted);
            foreach (var name in names)
            {
                string from = Path.Combine(unpacked, name), to = Path.Combine(wanted, name);
                if (!File.Exists(to) || new FileInfo(to).Length != new FileInfo(from).Length) File.Copy(from, to, overwrite: true);
            }
        }
        catch (Exception e) { Log.Write("ears: 無法把語音辨識的函式庫放到執行檔旁邊（資料夾不能寫入？）" + e.Message); }
    }

    /// <summary>Windows 的語言代碼（zh-TW、ja-JP）→ Whisper 的（zh、ja）。</summary>
    public static string Code(string language) => language.Split('-')[0].ToLowerInvariant() is { Length: 2 } two ? two : "auto";

    /// <summary>聽一句話。等了一陣子都沒人說話就回空字串。</summary>
    public async Task<string> ListenAsync(CancellationToken stop)
    {
        var samples = await RecordAsync(stop);
        if (samples.Length == 0) return "";
        Recognizing?.Invoke();
        return await TranscribeAsync(samples, stop);
    }

    /// <summary>16 kHz 單聲道的取樣 → 文字。</summary>
    /// <param name="whole">true：照模型原本的方式看滿 30 秒（比較用）。</param>
    /// <remarks>
    /// 同一時間只辨識一段：同一個 factory 建出來的處理器共用模型，不保證能同時跑；
    /// 也要擋住 <see cref="PrepareAsync"/> 在辨識途中換掉 factory。
    /// </remarks>
    public async Task<string> TranscribeAsync(float[] samples, CancellationToken stop, bool whole = false)
    {
        await _lock.WaitAsync(stop).ConfigureAwait(false);
        try
        {
            if (_factory == null || _disposed) return "";
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double seconds = samples.Length / (double)Rate;
            var language = _language;
            var builder = _factory.CreateBuilder().WithLanguage(language).WithNoContext()
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
            if (!whole) builder = builder.WithAudioContextSize(AudioContext(seconds));
            if (_prompt.Length > 0) builder = builder.WithPrompt(_prompt);
            using var processor = builder.Build();

            var text = new System.Text.StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, stop).ConfigureAwait(false)) text.Append(segment.Text);
            var result = Tidy(text.ToString());
            Last = (seconds, clock.Elapsed.TotalSeconds);
            return language == "zh" && Loc.Lang != "zh-CN" ? Chinese.ToTraditional(result) : result;
        }
        finally
        {
            // 辨識途中被 Dispose：那時拿不到鎖、沒辦法釋放，由這裡補做
            if (_disposed) ReleaseFactory();
            _lock.Release();
            Swept();
        }
    }

    /// <summary>
    /// Whisper 在沒人說話的片段會自己編出「(音樂)」「[BLANK_AUDIO]」「謝謝觀看」這類字幕常見的句子：
    /// 整句都在括號裡、或是這些套話，就當作沒聽到。
    /// </summary>
    public static string Tidy(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return "";
        if ((t[0] is '(' or '[' or '（' or '【' or '*') && (t[^1] is ')' or ']' or '）' or '】' or '*')) return "";
        string[] filler = ["謝謝觀看", "谢谢观看", "請不吝點贊", "请不吝点赞", "字幕由", "字幕by", "ご視聴ありがとうございました", "Thanks for watching", "Thank you for watching"];
        return filler.Any(f => t.Contains(f, StringComparison.OrdinalIgnoreCase)) ? "" : t;
    }

    // ---------------------------------------------------------------- 錄音

    /// <summary>從預設的麥克風錄到說完為止。</summary>
    private static async Task<float[]> RecordAsync(CancellationToken stop)
    {
        // NAudio 3.3 把這個類別標成過時，但它仍是從預設麥克風錄音最簡單的方式
#pragma warning disable CS0618
        var capture = new WasapiCapture();
#pragma warning restore CS0618
        var detector = new SpeechDetector();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var format = capture.WaveFormat;
            var resampler = new Downsampler(format.SampleRate, Rate);
            capture.DataAvailable += (_, e) =>
            {
                // 說完之後到麥克風真的關掉之間還會再進來幾批：不再餵，取出來的那一段才不會變
                if (done.Task.IsCompleted) return;
                var mono = Mono(e.Buffer, e.BytesRecorded, format);
                float peak = 0;
                foreach (var v in mono) peak = MathF.Max(peak, MathF.Abs(v));
                Volatile.Write(ref _level, MathF.Min(1, MathF.Sqrt(peak) * 1.6f));
                if (detector.Feed(resampler.Push(mono))) done.TrySetResult();
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) done.TrySetException(e.Exception); else done.TrySetResult(); };
            // 沒有麥克風權限時會從這裡丟出來，也要走到下面把 capture 放掉
            capture.StartRecording();
            using (stop.Register(() => done.TrySetCanceled())) await done.Task;
        }
        finally
        {
            try { capture.StopRecording(); } catch { }
            // StopRecording 只是通知錄音執行緒停下；Dispose 會等那條執行緒結束，
            // 之後就確定沒有 DataAvailable 還在 Feed（Feed 和 Speech 本身也有鎖，這裡是多一層保險）
            try { capture.Dispose(); } catch { }
            Volatile.Write(ref _level, 0);
        }
        return detector.Speech();
    }

    /// <summary>麥克風給的位元組（32 位元浮點或 16 位元整數、多聲道）→ 單聲道浮點。</summary>
    public static float[] Mono(byte[] buffer, int count, WaveFormat format)
    {
        int channels = Math.Max(1, format.Channels), bytes = format.BitsPerSample / 8, frame = bytes * channels;
        if (frame == 0) return [];
        var mono = new float[count / frame];
        for (int i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int at = i * frame + c * bytes;
                sum += bytes switch
                {
                    4 => BitConverter.ToSingle(buffer, at),
                    2 => BitConverter.ToInt16(buffer, at) / 32768f,
                    3 => ((buffer[at + 2] << 24 | buffer[at + 1] << 16 | buffer[at] << 8) >> 8) / 8388608f,
                    _ => 0,
                };
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    /// <summary>
    /// 自我檢查（--hear-test）：用 Windows 的語音把一句話唸出來，直接交給 Whisper 辨識，結果寫進紀錄檔。
    /// 不需要麥克風，用來確認模型下載得到、原生函式庫載得進來、辨識要多久。
    /// </summary>
    public static async Task<string> SelfTestAsync(string language, string sentence)
    {
        using var ears = new Ears();
        ears.Downloading += mb => { if (mb % 25 == 0) Log.Write($"hear-test: 下載模型 {mb} MB"); };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await ears.PrepareAsync(language, CancellationToken.None);
        var prepared = clock.Elapsed;

        using var synthesizer = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        var voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        if (voice != null) synthesizer.Voice = voice;
        using var speech = await synthesizer.SynthesizeTextToStreamAsync(sentence);
        using var wave = new WaveFileReader(speech.AsStreamForRead());
        var bytes = new byte[wave.Length];
        int read = wave.Read(bytes, 0, bytes.Length);
        var samples = new Downsampler(wave.WaveFormat.SampleRate, Rate).Push(Mono(bytes, read, wave.WaveFormat));

        var full = await ears.TranscribeAsync(samples, CancellationToken.None, whole: true);
        double fullTook = ears.Last.Took;
        var heard = await ears.TranscribeAsync(samples, CancellationToken.None);
        return $"模型={AppSettings.Current.WhisperModel} 準備 {prepared.TotalSeconds:0.0}s｜唸「{sentence}」（{samples.Length / (double)Rate:0.0}s）｜看滿 30 秒：「{full}」{fullTook:0.00}s｜只看需要的：「{heard}」{ears.Last.Took:0.00}s";
    }

    /// <summary>
    /// 放掉模型。正在準備或辨識的話不等它（可能在 UI 執行緒上呼叫），
    /// 標記起來由那一邊做完時釋放，免得在處理器還在用的時候把 factory 拆掉。
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        if (!_lock.Wait(0)) return;
        try { ReleaseFactory(); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// 放掉鎖之後再看一次。Dispose 可能剛好卡在「這邊已經看過 _disposed 是 false」與「這邊放掉鎖」之間：
    /// 它拿不到鎖就走了，這邊也沒釋放，模型就漏掉了。放掉鎖之後雙方總有一邊會看到對方，補做一次。
    /// </summary>
    private void Swept()
    {
        if (!_disposed || !_lock.Wait(0)) return;
        try { ReleaseFactory(); }
        finally { _lock.Release(); }
    }

    private void ReleaseFactory()
    {
        _factory?.Dispose();
        _factory = null;
        _loaded = "";
    }
}

/// <summary>把取樣率降到 16 kHz：兩個取樣之間用直線補。講話的聲音不需要更講究的濾波。</summary>
public sealed class Downsampler(int from, int to)
{
    private readonly double _step = (double)from / to;
    private double _at;
    private float _last;

    public float[] Push(float[] input)
    {
        if (input.Length == 0) return [];
        var output = new List<float>((int)(input.Length / _step) + 2);
        // _at 是下一個要取的位置，以這一批的開頭為 0；-1 的那一點是上一批的最後一個取樣
        while (_at < input.Length)
        {
            int i = (int)Math.Floor(_at);
            float a = i < 0 ? _last : input[i], b = i + 1 < input.Length ? input[i + 1] : input[^1];
            output.Add(a + (b - a) * (float)(_at - i));
            _at += _step;
        }
        _at -= input.Length;
        _last = input[^1];
        return output.ToArray();
    }
}

/// <summary>
/// 判斷一句話從哪裡開始、到哪裡結束：聲音連續夠大聲算開始，之後安靜了一秒算說完。
/// 環境的底噪會慢慢學起來，門檻跟著調，所以安靜的房間和有風扇聲的房間都能用。
/// </summary>
public sealed class SpeechDetector
{
    private const int Frame = Ears.Rate * 30 / 1000;   // 30 毫秒一格
    private readonly List<float> _all = [];
    /// <summary>Feed 在麥克風的錄音執行緒上跑，Speech 可能在另一條執行緒讀：兩邊不能同時碰 _all。</summary>
    private readonly object _gate = new();
    private double _noise = 0.004;
    private int _loud, _quiet, _start = -1, _end, _first, _last;

    public TimeSpan Patience { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>安靜多久算講完。太短會在換氣時把人打斷，太長會讓人覺得反應慢。</summary>
    public TimeSpan Silence { get; init; } = TimeSpan.FromSeconds(0.65);
    public TimeSpan Longest { get; init; } = TimeSpan.FromSeconds(25);
    /// <summary>多大聲才可能是人聲（RMS）。喚醒詞在放音樂時會調高。</summary>
    public double Floor { get; init; } = 0.012;

    /// <summary>真正有聲音的長度：從第一格大聲到最後一格大聲（不含前後留的餘裕）。沒人說話是 0。</summary>
    public TimeSpan Spoken { get { lock (_gate) return _start < 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(0, Math.Max(_end, _last) - _first) / (double)Ears.Rate); } }

    /// <summary>餵一段取樣。回傳 true：說完了（或等太久都沒人說話）。</summary>
    public bool Feed(float[] samples)
    {
        lock (_gate) return Add(samples);
    }

    private bool Add(float[] samples)
    {
        int before = _all.Count / Frame;
        _all.AddRange(samples);
        for (int f = before; (f + 1) * Frame <= _all.Count; f++)
        {
            double sum = 0;
            for (int i = f * Frame; i < (f + 1) * Frame; i++) sum += _all[i] * _all[i];
            double level = Math.Sqrt(sum / Frame);
            bool loud = level > Math.Max(Floor, _noise * 3.5);

            if (_start < 0)
            {
                if (!loud) _noise = _noise * 0.95 + level * 0.05;
                _loud = loud ? _loud + 1 : 0;
                // 連續三格（90 毫秒）才算有人說話；往前留 300 毫秒，免得第一個字被切掉
                if (_loud >= 3) { _start = Math.Max(0, (f - 12) * Frame); _first = (f - 2) * Frame; _last = (f + 1) * Frame; _quiet = 0; }
                else if (f * Frame > Patience.TotalSeconds * Ears.Rate) return true;
                continue;
            }

            _quiet = loud ? 0 : _quiet + 1;
            if (loud) _end = (f + 1) * Frame;
            if (_quiet * 30 >= Silence.TotalMilliseconds || (_all.Count - _start) > Longest.TotalSeconds * Ears.Rate) return true;
        }
        return false;
    }

    /// <summary>說話的那一段（後面多留 200 毫秒）；沒人說話是空的。</summary>
    public float[] Speech()
    {
        lock (_gate)
        {
            if (_start < 0) return [];
            int end = Math.Min(_all.Count, Math.Max(_end, _start) + Ears.Rate / 5);
            return _all.GetRange(_start, end - _start).ToArray();
        }
    }
}

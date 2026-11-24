// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using Shima.Lyrics;

namespace Shima.Agents;

/// <summary>
/// 喚醒詞：一直開著麥克風，聽到「Shima」（或自訂的詞）就觸發 <see cref="Heard"/>，不用按鍵就能開始問。
/// 省電：只有偵測到一小段像人聲的聲音（0.3～2.5 秒）才交給 Whisper；沒聲音時只算音量。
/// 保守：喇叭在放歌時門檻調高、而且整段話必須「就是」喚醒詞；誤觸太多會自己休息一下。判斷規則都在 <see cref="WakeGate"/>。
///
/// 交接麥克風：聽到之後這裡會先把麥克風完全關掉、<see cref="Running"/> 變成 false，才觸發 <see cref="Heard"/>。
/// 呼叫端接著開 <see cref="VoiceAssistant"/> 去聽問題，它的 Stopped 之後再呼叫 <see cref="Start"/> 接回來。
/// </summary>
public sealed class WakeWord : IDisposable
{
    private readonly Ears _ears;
    private string _language;
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private Task _loop = Task.CompletedTask;

    /// <param name="ears">
    /// 辨識用的 Whisper。可以跟語音助手共用同一個（不用多下載一個模型，兩邊不會同時辨識），
    /// 也可以另外給一個 new Ears("tiny")（最小最快，但要多下載約 75 MB）。
    /// </param>
    /// <param name="language">跟語音助手一樣的語言（zh-TW 等）。共用 Ears 時一定要一樣，不然會改到它的辨識語言。</param>
    public WakeWord(Ears ears, string language)
    {
        _ears = ears;
        _language = language;
    }

    /// <summary>算是喚醒詞的寫法（Whisper 對同一個發音會寫出不同的字）。</summary>
    public IReadOnlyList<string> Words { get; init; } = WakeGate.DefaultWords;

    /// <summary>電腦現在有沒有在放音樂。會在錄音的執行緒上被呼叫，要能從任何執行緒讀。</summary>
    public Func<bool>? MusicPlaying { get; init; }

    /// <summary>聽到喚醒詞了。麥克風已經關掉，在背景執行緒觸發。</summary>
    public event Action? Heard;
    /// <summary>沒辦法聽（沒有麥克風權限、模型下載失敗…）：給人看的一句話。之後就停了。</summary>
    public event Action<string>? Problem;
    /// <summary>每辨識一段就回報：聽到什麼、有沒有算數（除錯和紀錄用）。</summary>
    public event Action<string, bool>? Recognized;

    public bool Running { get { lock (_gate) return _stop != null; } }

    /// <summary>
    /// 辨識的語言（zh-TW 等）。設定改了語言就改這裡：下一次 <see cref="Start"/> 才會套用
    /// （正在聽的這一輪已經照舊的語言準備好了）。共用 Ears 時要跟語音助手一樣。
    /// </summary>
    public string Language
    {
        get { lock (_gate) return _language; }
        set { lock (_gate) _language = value; }
    }

    /// <summary>判斷規則與冷卻；重開之後還會記得剛剛的狀態。</summary>
    public WakeGate Gate { get; } = new();

    /// <summary>開始聽。已經在聽就什麼都不做。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_stop != null) return;
            var stop = _stop = new CancellationTokenSource();
            var language = _language;
            Gate.Resumed(DateTime.Now);
            var previous = _loop;
            _loop = Task.Run(async () =>
            {
                // 上一輪的麥克風要先放掉
                try { await previous.ConfigureAwait(false); } catch { }
                // 這一輪用的 token 只有迴圈自己在讀，結束了才釋放（Stop 只取消、不釋放，迴圈可能還在用）。
                // 走到這裡時 _stop 一定已經不是它了：每條結束的路都先經過 Finish 或 Cancel。
                // 取消時的後續可能直接在 Cancel 裡面接著跑：先離開那個呼叫，再在鎖裡釋放，確定 Cancel 已經做完
                try { await LoopAsync(stop, language).ConfigureAwait(false); }
                finally
                {
                    await Task.Yield();
                    lock (_gate) stop.Dispose();
                }
            });
        }
    }

    /// <summary>不聽了。已經停了就什麼都不做。麥克風在背景關掉，不用等；要等它真的放掉就用 <see cref="StopAsync"/>。</summary>
    public void Stop() => Cancel();

    /// <summary>
    /// 不聽了，並等背景那一輪真的結束：麥克風關掉、辨識做完。之後才把同一個 Ears 交給語音助手最穩。
    /// 已經停了也可以呼叫（等的是最後那一輪）。迴圈不需要回到 UI 執行緒，在 UI 執行緒上 await 不會卡死；
    /// 但不要用 .Wait() 同步等，Heard／Problem 的處理常常要回 UI 執行緒。
    /// </summary>
    public async Task StopAsync()
    {
        var loop = Cancel();
        try { await loop.ConfigureAwait(false); } catch { }
    }

    private Task Cancel()
    {
        lock (_gate)
        {
            if (_stop is { } stop)
            {
                _stop = null;
                stop.Cancel();
            }
            return _loop;
        }
    }

    /// <summary>這一輪結束（不管是聽到、出錯還是被停掉）：只清掉自己那一輪的狀態。</summary>
    private bool Finish(CancellationTokenSource stop)
    {
        lock (_gate)
        {
            if (_stop != stop) return false;
            _stop = null;
            return true;
        }
    }

    private sealed record Segment(float[] Samples, TimeSpan Spoken, bool Music);

    private SpeechDetector Detector(bool music) => new()
    {
        Silence = TimeSpan.FromSeconds(0.45),
        Longest = WakeGate.Longest + TimeSpan.FromSeconds(0.5),
        Patience = TimeSpan.FromSeconds(8),
        Floor = music ? WakeGate.MusicFloor : 0.012,
    };

    private bool Music() { try { return MusicPlaying?.Invoke() == true; } catch { return false; } }

    private async Task LoopAsync(CancellationTokenSource stop, string language)
    {
        bool heard = false;
        try
        {
            await _ears.PrepareAsync(language, stop.Token).ConfigureAwait(false);

            // 辨識中最多再排一段（最新的蓋掉舊的），不會越積越多
            var segments = Channel.CreateBounded<Segment>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // 跟 Ears 用一樣的方式錄音，但不更新 Ears.Level（膠囊上的音量條只在真的在問問題時才動）
#pragma warning disable CS0618
            using var capture = new WasapiCapture();
#pragma warning restore CS0618
            var format = capture.WaveFormat;
            var resampler = new Downsampler(format.SampleRate, Ears.Rate);
            bool music = Music();
            var detector = Detector(music);
            capture.DataAvailable += (_, e) =>
            {
                if (stop.IsCancellationRequested) return;
                if (!detector.Feed(resampler.Push(Ears.Mono(e.Buffer, e.BytesRecorded, format)))) return;
                var spoken = detector.Spoken;
                if (Gate.Worth(spoken, music, DateTime.Now)) segments.Writer.TryWrite(new Segment(detector.Speech(), spoken, music));
                music = Music();
                detector = Detector(music);
            };
            capture.RecordingStopped += (_, e) => { if (e.Exception != null) failed.TrySetException(e.Exception); else failed.TrySetResult(); };
            capture.StartRecording();
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var next = segments.Reader.ReadAsync(stop.Token).AsTask();
                    if (await Task.WhenAny(next, failed.Task).ConfigureAwait(false) == failed.Task) { await failed.Task.ConfigureAwait(false); break; }
                    var segment = await next.ConfigureAwait(false);
                    var text = await _ears.TranscribeAsync(segment.Samples, stop.Token).ConfigureAwait(false);
                    bool ok = Gate.Accept(text, segment.Music, DateTime.Now, Words);
                    Recognized?.Invoke(text, ok);
                    if (ok) { heard = true; break; }
                }
            }
            finally
            {
                try { capture.StopRecording(); } catch { }
            }
        }
        catch (Exception) when (stop.IsCancellationRequested) { }
        catch (Exception e)
        {
            bool microphone = e.HResult == unchecked((int)0x80070005) || e is UnauthorizedAccessException;
            if (Finish(stop)) Problem?.Invoke(microphone ? L.T("沒有麥克風權限：請到 Windows 設定 → 隱私權 → 麥克風開啟") : L.T("喚醒詞無法啟動"));
            return;
        }
        // using 已經把麥克風放掉了，才通知呼叫端可以開始用
        if (!Finish(stop)) return;
        if (heard) Heard?.Invoke();
        else Problem?.Invoke(L.T("麥克風中斷了"));   // 錄音自己停了（例如拔掉麥克風）
    }

    public void Dispose() => Stop();
}

/// <summary>
/// 喚醒詞的判斷規則（純邏輯，不碰麥克風和時鐘，方便測試）：
/// 1. 這段聲音值不值得交給 Whisper（<see cref="Worth"/>）：長度、放音樂時更短、冷卻中、誤觸太多在休息。
/// 2. 辨識出來的字算不算喚醒詞（<see cref="Accept"/>，比對規則在 <see cref="Matches"/>）。
/// </summary>
public sealed class WakeGate
{
    public static readonly TimeSpan Shortest = TimeSpan.FromSeconds(0.3);
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(2.5);
    /// <summary>放音樂時只接受更短的一段：喚醒詞本身大約半秒到一秒，歌聲通常是連續的長句。</summary>
    public static readonly TimeSpan MusicLongest = TimeSpan.FromSeconds(1.5);
    /// <summary>放音樂時人聲要夠大聲才算（RMS；平常是 0.012）。要湊近麥克風講。</summary>
    public const double MusicFloor = 0.03;

    /// <summary>聽到一次之後多久內不再觸發。</summary>
    public static readonly TimeSpan AfterHeard = TimeSpan.FromSeconds(4);
    /// <summary>剛開始（或助手剛講完、把麥克風交回來）先不聽，免得收到它自己講話的尾巴。</summary>
    public static readonly TimeSpan AfterResume = TimeSpan.FromSeconds(1.5);
    /// <summary>一分鐘內辨識了這麼多次都不是喚醒詞（一直有人在講話或放影片）：休息一下，省電也少誤觸。</summary>
    public const int MissesBeforeRest = 8;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Rest = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 預設算數的寫法。Whisper 依語言會把「Shima」寫成英文、假名或漢字；簡體寫法比對前會轉成繁體，所以不用另外列。
    /// </summary>
    public static readonly string[] DefaultWords = ["Shima", "Shimma", "Sheema", "Schima", "シマ", "しま", "島", "嶋", "志摩", "希瑪", "西瑪", "詩瑪", "夕瑪", "喜瑪", "席瑪"];

    private DateTime _quietUntil = DateTime.MinValue;
    private readonly Queue<DateTime> _misses = new();
    private readonly object _lock = new();

    /// <summary>重新開始聽。</summary>
    public void Resumed(DateTime now)
    {
        lock (_lock) if (_quietUntil < now + AfterResume) _quietUntil = now + AfterResume;
    }

    /// <summary>這段聲音要不要拿去辨識。</summary>
    /// <param name="spoken">真正有聲音的長度。</param>
    public bool Worth(TimeSpan spoken, bool music, DateTime now)
    {
        lock (_lock)
        {
            if (now < _quietUntil) return false;
            return spoken >= Shortest && spoken <= (music ? MusicLongest : Longest);
        }
    }

    /// <summary>辨識結果算不算喚醒詞，並記下來（算數就進入冷卻；不算就累計誤觸）。</summary>
    public bool Accept(string text, bool music, DateTime now, IEnumerable<string> words)
    {
        lock (_lock)
        {
            if (now < _quietUntil) return false;
            if (Matches(text, words, whole: music))
            {
                _quietUntil = now + AfterHeard;
                _misses.Clear();
                return true;
            }
            if (Normalize(text).Length == 0) return false;   // 什麼都沒聽出來不算誤觸
            _misses.Enqueue(now);
            while (_misses.Count > 0 && now - _misses.Peek() > Window) _misses.Dequeue();
            if (_misses.Count >= MissesBeforeRest) { _quietUntil = now + Rest; _misses.Clear(); }
            return false;
        }
    }

    /// <summary>開頭、結尾可以多出來的招呼語（「嘿 Shima」「Shima 醬」）。比對前會先拿掉。</summary>
    private static readonly string[] Greetings = ["hey", "hi", "ok", "okay", "嘿", "嗨", "欸", "誒", "喂", "哈囉", "ねえ", "ねぇ", "おい"];
    private static readonly string[] Endings = ["醬", "ちゃん", "さん", "啊", "呀", "喔", "哦", "嗎", "吗"];

    /// <summary>
    /// 比對用的寫法：全形轉半形、轉小寫、片假名轉平假名、簡體轉繁體，只留字母和數字（英文單字之間留一個空白）。
    /// </summary>
    public static string Normalize(string text)
    {
        var t = Chinese.ToTraditional(Lyrics.Titles.Wellformed(text).Normalize(NormalizationForm.FormKC)).ToLowerInvariant();
        var output = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (c is >= 'ァ' and <= 'ヶ') output.Append((char)(c - 0x60));   // カタカナ → ひらがな
            else if (char.IsLetterOrDigit(c)) output.Append(c);
            else if (output.Length > 0 && output[^1] != ' ') output.Append(' ');
        }
        return output.ToString().Trim();
    }

    private static bool Latin(string word) => word.Length > 0 && word.All(c => c is >= 'a' and <= 'z' or ' ');

    /// <summary>
    /// 辨識出來的字是不是喚醒詞。
    /// whole：整段（拿掉招呼語後）必須就是喚醒詞——放音樂時用，歌詞裡剛好有這個音也不會觸發。
    /// 不是 whole 時，英文的寫法可以出現在句子裡（要是完整的一個單字，「Hiroshima」不算）；
    /// 假名和漢字的寫法一律要整段相符，因為「しま」藏在「します」「しました」裡、「島」也常出現在一般句子和歌詞中。
    /// </summary>
    public static bool Matches(string heard, IEnumerable<string> words, bool whole)
    {
        var said = Strip(Normalize(heard));
        if (said.Length == 0) return false;
        var compact = said.Replace(" ", "");
        foreach (var word in words)
        {
            var w = Normalize(word);
            if (w.Length == 0) continue;
            if (compact == w.Replace(" ", "")) return true;
            if (whole || !Latin(w)) continue;
            if ((" " + said + " ").Contains(" " + w + " ", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>拿掉前後的招呼語（可以重複：「hey hey shima」）。</summary>
    private static string Strip(string said)
    {
        var t = said;
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var g in Greetings)
                if (t.StartsWith(g, StringComparison.Ordinal) && t.Length > g.Length && (!Latin(g) || t[g.Length] == ' '))
                { t = t[g.Length..].TrimStart(); changed = true; }
            foreach (var e in Endings)
                if (t.EndsWith(e, StringComparison.Ordinal) && t.Length > e.Length)
                { t = t[..^e.Length].TrimEnd(); changed = true; }
        }
        return t;
    }

    /// <summary>設定裡的喚醒詞（一行一個或用逗號、頓號分開）→ 清單；空的就用預設。</summary>
    public static IReadOnlyList<string> Parse(string setting)
    {
        var list = setting.Split(['\n', '\r', ',', '，', '、', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => Normalize(w).Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count > 0 ? list : DefaultWords;
    }
}

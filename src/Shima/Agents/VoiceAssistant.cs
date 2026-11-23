// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Windows.Globalization;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;

namespace Shima.Agents;

/// <summary>
/// 語音助手。這些 AI 的 API 只收發文字，所以是三段接起來：
/// 語音辨識（聽）→ AI（想）→ Windows 語音合成（說），說完再繼續聽，直到按停止。
/// 聽的部分預設在這台電腦上用 Whisper 辨識，也可以改用 Windows 的聽寫。
/// 可以幫忙控制音樂：暫停、下一首、上一首、現在在播什麼。
/// </summary>
public sealed class VoiceAssistant
{
    private readonly IBrain _brain;
    private readonly string _language;
    private readonly CancellationTokenSource _stop = new();
    /// <summary>_stop 已經釋放了（RunAsync 結束時）。Stop 在那之後還可能被按到，要先看這個。</summary>
    private bool _released;
    private readonly object _gate = new();
    private MediaPlayer? _player;

    /// <summary>目前的狀態或剛才說的話，給畫面顯示。</summary>
    public event Action<string>? Said;
    /// <summary>
    /// 回答還在產生：這一輪目前為止的文字（在背景執行緒，來得很密，要自己節流）。
    /// 只給畫面顯示；唸出來的仍然是 Said 那份完整的回答。
    /// </summary>
    public event Action<string>? Partial;
    public event Action? Stopped;
    /// <summary>要使用者去改一項 Windows 設定：那一頁的網址（ms-settings:…）。</summary>
    public event Action<string>? NeedsSetting;

    /// <summary>
    /// 問一句、回答完就結束。只有助手反過來問你問題時（「要加到哪一天？」）才會再聽一次。
    /// false：一直聽到按停止為止——喇叭在放音樂時不適合，麥克風會把歌聲當成有人在講話。
    /// </summary>
    public bool Once { get; init; }

    /// <summary>助手在問問題、等人回答（最多連問三輪，免得沒完沒了）。</summary>
    public static bool AsksBack(string reply, int rounds) =>
        rounds < 3 && reply.TrimEnd().TrimEnd('」', '"', '”', ')', '）') is { Length: > 0 } text && text[^1] is '?' or '？';

    private readonly Ears? _ears;

    /// <param name="ears">本機語音辨識；交給外面保管，模型載一次就一直留著，不用每次按麥克風都重載。</param>
    public VoiceAssistant(IBrain brain, string language, Ears? ears = null)
    {
        _brain = brain;
        _language = language;
        _ears = ears;
    }

    /// <summary>麥克風打開（true）或關上（false）。</summary>
    public event Action<bool>? Listening;

    public void Stop()
    {
        lock (_gate)
        {
            // 視窗收到 Stopped 之前還可能再按一次停止：那時 _stop 已經釋放了
            if (_released || _stop.IsCancellationRequested) return;
            _stop.Cancel();
        }
        try { _player?.Pause(); } catch { }
    }

    /// <summary>這一輪做完了：沒有人會再用 _stop 的 token，釋放它。</summary>
    private void Release()
    {
        lock (_gate)
        {
            if (_released) return;
            _released = true;
            _stop.Dispose();
        }
    }

    /// <summary>給還沒結束的工作用的 token；已經釋放了就當作已經按了停止。</summary>
    private CancellationToken Token
    {
        get { lock (_gate) return _released ? new CancellationToken(canceled: true) : _stop.Token; }
    }

    public Task RunAsync() => AppSettings.Current.SpeechEngine == "windows" ? RunWindowsAsync() : RunLocalAsync();

    /// <summary>在這台電腦上辨識（Whisper）：不用開任何 Windows 設定。</summary>
    private async Task RunLocalAsync()
    {
        var ears = _ears ?? new Ears();
        void Progress(int mb) => Said?.Invoke(L.F("下載語音模型中… {0} MB", mb));
        void Working() => Said?.Invoke(L.T("辨識中…"));
        ears.Downloading += Progress;
        ears.Recognizing += Working;
        try
        {
            try { await ears.PrepareAsync(_language, _stop.Token); }
            catch (Exception e) when (!_stop.IsCancellationRequested)
            {
                Said?.Invoke(L.T("語音模型下載失敗"));
                Log.Write("ears: " + e.Message);
                return;
            }

            int rounds = 0;
            while (!_stop.IsCancellationRequested)
            {
                Said?.Invoke(L.T("聆聽中…"));
                Listening?.Invoke(true);
                string heard;
                try { heard = await ears.ListenAsync(_stop.Token); }
                finally { Listening?.Invoke(false); }
                if (_stop.IsCancellationRequested) break;
                if (heard.Length == 0)
                {
                    if (Once) { Said?.Invoke(""); break; }
                    continue;
                }

                Said?.Invoke(heard);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var reply = await _brain.ThinkAsync(heard, _stop.Token, Streaming);
                Log.Write($"voice: 講 {ears.Last.Spoken:0.0}s，辨識 {ears.Last.Took:0.00}s，AI 回答 {clock.Elapsed.TotalSeconds:0.0}s");
                if (_stop.IsCancellationRequested) break;
                Said?.Invoke(reply);
                await SpeakAsync(reply);
                if (Once && !AsksBack(reply, rounds++)) break;
            }
        }
        // 整個「是不是 API 的錯」要括起來：&& 比 || 先算，少了括號的話按了停止之後 HTTP 開頭的錯誤還是會進來、蓋掉畫面
        catch (Exception e) when (!_stop.IsCancellationRequested && (e is Anthropic.Exceptions.AnthropicApiException or System.Net.Http.HttpRequestException || e.Message.StartsWith("HTTP ", StringComparison.Ordinal)))
        {
            Said?.Invoke(_brain.Explain(e));
            Log.Write("voice api: " + e.Message);
        }
        catch (Exception e) when (!_stop.IsCancellationRequested)
        {
            bool microphone = e.HResult == unchecked((int)0x80070005) || e is UnauthorizedAccessException;
            Said?.Invoke(microphone ? L.T("沒有麥克風權限：請到 Windows 設定 → 隱私權 → 麥克風開啟") : L.T("語音助手無法啟動"));
            if (microphone) NeedsSetting?.Invoke("ms-settings:privacy-microphone");
            Log.Write($"voice: 0x{e.HResult:X8} {e.Message}");
        }
        catch { /* 使用者按了停止 */ }
        finally
        {
            ears.Downloading -= Progress;
            ears.Recognizing -= Working;
            if (_ears == null) ears.Dispose();
            try { _player?.Dispose(); } catch { }
            Release();
            Stopped?.Invoke();
        }
    }

    /// <summary>Windows 的聽寫：邊講邊辨識，但要先在隱私設定開啟線上語音辨識、裝好那個語言的語音套件。</summary>
    private async Task RunWindowsAsync()
    {
        try
        {
            var listening = Pick(_language);
            using var recognizer = new SpeechRecognizer(listening);
            // 想用的語言這台電腦沒裝語音辨識：先講清楚現在是用哪個語言在聽
            if (!listening.LanguageTag.StartsWith(_language.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            {
                Said?.Invoke(L.F("這台電腦沒有安裝 {0} 的語音辨識，改用 {1} 聆聽", _language, listening.LanguageTag));
                await Task.Delay(2500, _stop.Token);
            }
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
            {
                Said?.Invoke(L.T("語音辨識無法使用：請到 Windows 設定 → 隱私權 → 語音，開啟線上語音辨識"));
                return;
            }
            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(12);
            recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);
            int winRounds = 0;

            while (!_stop.IsCancellationRequested)
            {
                Said?.Invoke(L.T("聆聽中…"));
                var heard = await recognizer.RecognizeAsync();
                if (_stop.IsCancellationRequested) break;
                if (heard.Status != SpeechRecognitionResultStatus.Success || heard.Confidence == SpeechRecognitionConfidence.Rejected || string.IsNullOrWhiteSpace(heard.Text))
                {
                    if (Once) { Said?.Invoke(""); break; }
                    continue;
                }

                Said?.Invoke(heard.Text);
                var reply = await _brain.ThinkAsync(heard.Text, _stop.Token, Streaming);
                if (_stop.IsCancellationRequested) break;
                Said?.Invoke(reply);
                await SpeakAsync(reply);
                if (Once && !AsksBack(reply, winRounds++)) break;
            }
        }
        // 整個「是不是 API 的錯」要括起來：&& 比 || 先算，少了括號的話按了停止之後 HTTP 開頭的錯誤還是會進來、蓋掉畫面
        catch (Exception e) when (!_stop.IsCancellationRequested && (e is Anthropic.Exceptions.AnthropicApiException or System.Net.Http.HttpRequestException || e.Message.StartsWith("HTTP ", StringComparison.Ordinal)))
        {
            Said?.Invoke(_brain.Explain(e));
            Log.Write("voice api: " + e.Message);
        }
        catch (Exception e) when (!_stop.IsCancellationRequested)
        {
            // 0x80045509：Windows 的「線上語音辨識」沒有開，聽寫用不了；0x80070005：麥克風權限被關掉
            bool speech = e.HResult == unchecked((int)0x80045509), microphone = e.HResult == unchecked((int)0x80070005);
            Said?.Invoke(speech ? L.T("語音辨識無法使用：請到 Windows 設定 → 隱私權 → 語音，開啟線上語音辨識")
                : microphone ? L.T("沒有麥克風權限：請到 Windows 設定 → 隱私權 → 麥克風開啟") : L.T("語音助手無法啟動"));
            // 直接把那一頁設定打開，不用自己找
            if (speech || microphone) NeedsSetting?.Invoke(speech ? "ms-settings:privacy-speech" : "ms-settings:privacy-microphone");
            Log.Write($"voice: 0x{e.HResult:X8} {e.Message}");
        }
        catch { /* 使用者按了停止 */ }
        finally
        {
            try { _player?.Dispose(); } catch { }
            Release();
            Stopped?.Invoke();
        }
    }

    /// <summary>使用者要的語言有裝語音辨識就用它，沒有就用 Windows 目前的語音語言。</summary>
    private static Language Pick(string wanted)
    {
        var have = SpeechRecognizer.SupportedTopicLanguages;
        return have.FirstOrDefault(l => l.LanguageTag.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            ?? have.FirstOrDefault(l => l.LanguageTag.StartsWith(wanted.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            ?? SpeechRecognizer.SystemSpeechLanguage;
    }

    /// <summary>有人要看串流的片段才用串流。</summary>
    private Action<string>? Streaming => Partial == null ? null : text => Partial?.Invoke(text);

    /// <summary>打字問一句（不聽也不唸）。API 的錯誤轉成給人看的一句話。progress：回答邊產生邊收到目前為止的全文。</summary>
    public async Task<string> AskAsync(string question, Action<string>? progress = null)
    {
        try { return await _brain.ThinkAsync(question, Token, progress); }
        catch (Exception e) { Log.Write("ask: " + e.Message); return _brain.Explain(e); }
    }

    /// <summary>
    /// 這段文字該用哪個語言的聲音唸（兩個字母的代碼）：有假名是日文，有韓文字母是韓文，有漢字是中文，
    /// 幾乎全是英文字母而設定的語言不是用拉丁字母的就用英文，其餘照設定的語言。
    /// </summary>
    public static string SpokenLanguage(string text, string language)
    {
        var mine = language.Split('-')[0].ToLowerInvariant();
        if (text.Any(c => c is >= '\u3040' and <= '\u30FF')) return "ja";
        if (text.Any(c => c is >= '\uAC00' and <= '\uD7A3')) return "ko";
        if (text.Any(c => c is >= '\u4E00' and <= '\u9FFF')) return mine is "zh" or "ja" ? mine : "zh";
        int letters = text.Count(char.IsLetter), latin = text.Count(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return letters > 0 && latin >= letters * 0.9 && mine is "zh" or "ja" or "ko" ? "en" : mine;
    }

    private async Task SpeakAsync(string text)
    {
        using var synthesizer = new SpeechSynthesizer();
        // 依回答的文字挑語音：用日文回答就用日文的聲音唸，不要拿中文語音硬唸
        var prefix = SpokenLanguage(text, _language);
        var voice = (prefix == _language.Split('-')[0] ? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.Equals(_language, StringComparison.OrdinalIgnoreCase)) : null)
                 ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                 ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.Equals(_language, StringComparison.OrdinalIgnoreCase));
        if (voice != null) synthesizer.Voice = voice;

        using var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _player ??= new MediaPlayer();
        void Done(MediaPlayer p, object e) => ended.TrySetResult();
        void Failed(MediaPlayer p, MediaPlayerFailedEventArgs e) => ended.TrySetResult();
        _player.MediaEnded += Done;
        _player.MediaFailed += Failed;
        MediaSource? source = null;
        try
        {
            source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Source = source;
            _player.Play();
            using (_stop.Token.Register(() => ended.TrySetResult())) await ended.Task;
        }
        finally
        {
            _player.MediaEnded -= Done;
            _player.MediaFailed -= Failed;
            // 先讓播放器放開這段聲音，再釋放它；串流由上面的 using 在最後釋放
            try { _player.Source = null; } catch { }
            source?.Dispose();
        }
    }
}

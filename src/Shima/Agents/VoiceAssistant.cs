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
/// Windows 語音辨識（聽）→ AI（想）→ Windows 語音合成（說），說完再繼續聽，直到按停止。
/// 可以幫忙控制音樂：暫停、下一首、上一首、現在在播什麼。
/// </summary>
public sealed class VoiceAssistant
{
    private readonly IBrain _brain;
    private readonly string _language;
    private readonly CancellationTokenSource _stop = new();
    private MediaPlayer? _player;

    /// <summary>目前的狀態或剛才說的話，給畫面顯示。</summary>
    public event Action<string>? Said;
    public event Action? Stopped;

    /// <summary>只聽一句、回答完就結束（快速鍵叫出來的用法）。</summary>
    public bool Once { get; init; }

    public VoiceAssistant(IBrain brain, string language)
    {
        _brain = brain;
        _language = language;
    }

    public void Stop()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        try { _player?.Pause(); } catch { }
    }

    public async Task RunAsync()
    {
        try
        {
            using var recognizer = new SpeechRecognizer(Pick(_language));
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
            {
                Said?.Invoke(L.T("語音辨識無法使用：請到 Windows 設定 → 隱私權 → 語音，開啟線上語音辨識"));
                return;
            }
            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(12);
            recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.2);

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
                var reply = await _brain.ThinkAsync(heard.Text, _stop.Token);
                if (_stop.IsCancellationRequested) break;
                Said?.Invoke(reply);
                await SpeakAsync(reply);
                if (Once) break;
            }
        }
        catch (Exception e) when (!_stop.IsCancellationRequested && e is Anthropic.Exceptions.AnthropicApiException or System.Net.Http.HttpRequestException || e.Message.StartsWith("HTTP ", StringComparison.Ordinal))
        {
            Said?.Invoke(_brain.Explain(e));
            Log.Write("voice api: " + e.Message);
        }
        catch (Exception e) when (!_stop.IsCancellationRequested)
        {
            // 0x80045509：這個語言的語音辨識沒有安裝；0x80070005：麥克風權限被關掉
            Said?.Invoke(e.HResult == unchecked((int)0x80070005) ? L.T("沒有麥克風權限：請到 Windows 設定 → 隱私權 → 麥克風開啟") : L.T("語音助手無法啟動"));
            Log.Write($"voice: 0x{e.HResult:X8} {e.Message}");
        }
        catch { /* 使用者按了停止 */ }
        finally
        {
            try { _player?.Dispose(); } catch { }
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

    /// <summary>打字問一句（不聽也不唸）。API 的錯誤轉成給人看的一句話。</summary>
    public async Task<string> AskAsync(string question)
    {
        try { return await _brain.ThinkAsync(question, _stop.Token); }
        catch (Exception e) { Log.Write("ask: " + e.Message); return _brain.Explain(e); }
    }

    private async Task SpeakAsync(string text)
    {
        using var synthesizer = new SpeechSynthesizer();
        var prefix = _language.Split('-')[0];
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.Equals(_language, StringComparison.OrdinalIgnoreCase))
                 ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (voice != null) synthesizer.Voice = voice;

        using var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _player ??= new MediaPlayer();
        void Done(MediaPlayer p, object e) => ended.TrySetResult();
        void Failed(MediaPlayer p, MediaPlayerFailedEventArgs e) => ended.TrySetResult();
        _player.MediaEnded += Done;
        _player.MediaFailed += Failed;
        try
        {
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
            using (_stop.Token.Register(() => ended.TrySetResult())) await ended.Task;
        }
        finally
        {
            _player.MediaEnded -= Done;
            _player.MediaFailed -= Failed;
        }
    }
}

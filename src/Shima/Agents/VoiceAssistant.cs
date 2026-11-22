// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Windows.Globalization;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;

namespace Shima.Agents;

/// <summary>
/// 語音助手。Claude API 沒有語音，所以是三段接起來：
/// Windows 語音辨識（聽）→ Claude（想）→ Windows 語音合成（說），說完再繼續聽，直到按停止。
/// 可以幫忙控制音樂：暫停、下一首、上一首、現在在播什麼。
/// </summary>
public sealed class VoiceAssistant
{
    public const string Model = "claude-opus-5-5";

    private readonly AnthropicClient _client;
    private readonly string _language;
    private readonly Func<string, string> _music;
    private readonly List<MessageParam> _messages = [];
    private readonly CancellationTokenSource _stop = new();
    private MediaPlayer? _player;

    /// <summary>目前的狀態或剛才說的話，給畫面顯示。</summary>
    public event Action<string>? Said;
    public event Action? Stopped;

    /// <param name="music">執行音樂指令（play_pause、next、previous、now_playing），回傳結果的說明。</param>
    /// <summary>只聽一句、回答完就結束（快速鍵叫出來的用法）。</summary>
    public bool Once { get; init; }

    public VoiceAssistant(string apiKey, string language, Func<string, string> music)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _language = language;
        _music = music;
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
                var reply = await ThinkAsync(heard.Text);
                if (_stop.IsCancellationRequested) break;
                Said?.Invoke(reply);
                await SpeakAsync(reply);
                if (Once) break;
            }
        }
        catch (AnthropicUnauthorizedException) { Said?.Invoke(L.T("API 金鑰無效")); }
        catch (AnthropicRateLimitException) { Said?.Invoke(L.T("用量已達上限，請稍後再試")); }
        catch (AnthropicApiException e) { Said?.Invoke(L.T("Claude API 錯誤")); Log.Write("voice api: " + e.Message); }
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

    private static readonly Tool MusicTool = new()
    {
        Name = "music",
        Description = "控制使用者電腦上正在播放的音樂，或查詢現在播的是哪一首。使用者提到播放、暫停、切歌、上一首、下一首、這首歌是什麼時使用。",
        InputSchema = new()
        {
            Properties = new Dictionary<string, JsonElement>
            {
                ["action"] = JsonSerializer.SerializeToElement(new
                {
                    type = "string",
                    @enum = new[] { "play_pause", "next", "previous", "now_playing" },
                    description = "play_pause：播放或暫停；next：下一首；previous：上一首；now_playing：查詢現在播放的歌",
                }),
            },
            Required = ["action"],
        },
    };

    /// <summary>打字問一句（不聽也不唸）。API 的錯誤轉成給人看的一句話。</summary>
    public async Task<string> AskAsync(string question)
    {
        try { return await ThinkAsync(question); }
        catch (AnthropicUnauthorizedException) { return L.T("API 金鑰無效"); }
        catch (AnthropicRateLimitException) { return L.T("用量已達上限，請稍後再試"); }
        catch (Exception e) { Log.Write("ask: " + e.Message); return L.T("Claude API 錯誤"); }
    }

    private async Task<string> ThinkAsync(string heard)
    {
        _messages.Add(new UserMessageParam { Content = heard });
        for (int round = 0; round < 4; round++)
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 16000,
                System = "你是 Shima 的助手，住在使用者 Windows 桌面頂端的小島裡。你的回答會顯示在很小的面板上或被語音唸出來，所以用口語、簡短的句子，" +
                         "通常一到三句；不要用 Markdown、條列、表情符號或網址。使用者用什麼語言說話就用什麼語言回答" +
                         (Loc.Lang == "zh-TW" ? "，中文一律用臺灣的繁體中文用語。" : $"；聽不出語言時用 {_language}。") +
                         "使用者說的話來自語音辨識，可能有同音錯字，請依上下文理解。",
                OutputConfig = new OutputConfig { Effort = Effort.Low },
                Tools = [MusicTool],
                Messages = _messages,
            }, _stop.Token);

            if (response.StopReason == "refusal")
            {
                _messages.RemoveAt(_messages.Count - 1);
                return L.T("這個我沒辦法幫忙。");
            }

            // 回應原封不動放回對話（包含思考區塊），下一輪才接得上
            List<ContentBlockParam> echo = [], results = [];
            var spoken = new List<string>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text)) { echo.Add(new TextBlockParam { Text = text.Text }); spoken.Add(text.Text); }
                else if (block.TryPickThinking(out ThinkingBlock? thinking)) echo.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted)) echo.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                else if (block.TryPickToolUse(out ToolUseBlock? use))
                {
                    echo.Add(new ToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                    var action = use.Input.TryGetValue("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
                    results.Add(new ToolResultBlockParam { ToolUseID = use.ID, Content = _music(action) });
                }
            }
            _messages.Add(new AssistantMessageParam { Content = echo });
            if (results.Count == 0) return string.Join(" ", spoken).Trim() is { Length: > 0 } said ? said : L.T("嗯。");
            _messages.Add(new UserMessageParam { Content = results });
        }
        return L.T("我有點卡住了，再說一次好嗎？");
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

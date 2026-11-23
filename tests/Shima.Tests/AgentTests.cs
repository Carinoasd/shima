// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

public class AgentHooksTests
{
    private static JsonObject Existing() => (JsonObject)JsonNode.Parse("""
        {
          "model": "opus",
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "/home/me/notify.sh" } ] } ],
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "guard.sh" } ] } ]
          }
        }
        """)!;

    [Fact]
    public void 連接後三個事件各多一筆_別人的不動()
    {
        var s = AgentHooks.With(Existing(), "curl.exe", "TOKEN");
        Assert.True(AgentHooks.Has(s));
        Assert.Equal("opus", s["model"]!.ToString());
        Assert.Equal(2, ((JsonArray)s["hooks"]!["Stop"]!).Count);
        Assert.Single((JsonArray)s["hooks"]!["PermissionRequest"]!);
        Assert.Single((JsonArray)s["hooks"]!["Notification"]!);
        Assert.Single((JsonArray)s["hooks"]!["PreToolUse"]!);
        Assert.Equal("TaskCreate|TaskUpdate|TodoWrite", s["hooks"]!["PostToolUse"]![0]!["matcher"]!.ToString());
        Assert.Null(s["hooks"]!["Stop"]![1]!["matcher"]);
        var command = s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["command"]!.ToString();
        Assert.Contains("X-Shima-Token: TOKEN", command);
        Assert.EndsWith($"http://localhost:{AgentBridge.Port}/permission", command);
        Assert.Equal(600, s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["timeout"]!.GetValue<int>());
    }

    [Fact]
    public void 重複連接不會越加越多()
    {
        var s = AgentHooks.With(AgentHooks.With(Existing(), "curl.exe", "A"), "curl.exe", "B");
        Assert.Single((JsonArray)s["hooks"]!["PermissionRequest"]!);
        Assert.Contains("X-Shima-Token: B", s["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["command"]!.ToString());
    }

    [Fact]
    public void 中斷後回到原樣()
    {
        var back = AgentHooks.Without(AgentHooks.With(Existing(), "curl.exe", "TOKEN"));
        Assert.True(JsonNode.DeepEquals(Existing(), back));
        Assert.False(AgentHooks.Has(back));
    }

    [Fact]
    public void 原本沒有_hook_的設定_中斷後也不留空殼()
    {
        var plain = new JsonObject { ["theme"] = "dark" };
        Assert.True(JsonNode.DeepEquals(plain, AgentHooks.Without(AgentHooks.With(plain, "curl.exe", "T"))));
    }
}

public class AgentBridgeTests
{
    [Fact]
    public void 允許與拒絕的回答格式()
    {
        var allow = JsonNode.Parse(AgentBridge.Answer("allow")!)!["hookSpecificOutput"]!;
        Assert.Equal("PermissionRequest", allow["hookEventName"]!.ToString());
        Assert.Equal("allow", allow["decision"]!["behavior"]!.ToString());
        Assert.Null(allow["decision"]!["message"]);

        var deny = JsonNode.Parse(AgentBridge.Answer("deny")!)!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("deny", deny["behavior"]!.ToString());
        Assert.False(string.IsNullOrEmpty(deny["message"]!.ToString()));
    }

    [Fact]
    public void 回終端機就是不回答() => Assert.Null(AgentBridge.Answer(null));

    [Theory]
    [InlineData("""{"command":"rm -rf node_modules","description":"清理"}""", "rm -rf node_modules")]
    [InlineData("""{"file_path":"/home/me/a.cs","content":"…"}""", "/home/me/a.cs")]
    [InlineData("""{"url":"https://example.com"}""", "https://example.com")]
    public void 用一行說明工具要做什麼(string input, string shown) =>
        Assert.Equal(shown, AgentBridge.Describe("Bash", JsonNode.Parse(input)));
}

public class AgentPlanTests
{
    private static JsonObject Hook(string session, string tool, string input, string? response = null)
    {
        var o = new JsonObject { ["session_id"] = session, ["hook_event_name"] = "PostToolUse", ["tool_name"] = tool, ["tool_input"] = JsonNode.Parse(input) };
        if (response != null) o["tool_response"] = JsonNode.Parse(response);
        return o;
    }

    [Fact]
    public void 舊版一次給整份清單()
    {
        var plan = new AgentPlan();
        Assert.True(plan.Apply(Hook("s", "TodoWrite", """{"todos":[{"content":"讀程式","status":"completed"},{"content":"寫測試","status":"in_progress","activeForm":"正在寫測試"},{"content":"建置","status":"pending"}]}""")));
        Assert.Equal((1, 3, "正在寫測試"), (plan.Done, plan.Steps.Count, plan.Current));
    }

    [Fact]
    public void 新版一次一項_欄位名稱寬鬆地讀()
    {
        var plan = new AgentPlan();
        plan.Apply(Hook("s", "TaskCreate", """{"subject":"讀程式"}""", """{"task":{"id":"7"}}"""));
        plan.Apply(Hook("s", "TaskCreate", """{"subject":"寫測試","activeForm":"正在寫測試"}""", """{"taskId":"8"}"""));
        Assert.True(plan.Apply(Hook("s", "TaskUpdate", """{"taskId":"7","status":"completed"}""")));
        Assert.True(plan.Apply(Hook("s", "TaskUpdate", """{"task_id":"8","status":"in_progress"}""")));
        Assert.Equal((1, 2, "正在寫測試"), (plan.Done, plan.Steps.Count, plan.Current));
        Assert.False(plan.Apply(Hook("s", "TaskUpdate", """{"taskId":"99","status":"completed"}""")));
        plan.Apply(Hook("s", "TaskUpdate", """{"taskId":"8","status":"deleted"}"""));
        Assert.Single(plan.Steps);
    }

    [Fact]
    public void 新的工作階段從頭開始_別的工具不理()
    {
        var plan = new AgentPlan();
        plan.Apply(Hook("a", "TaskCreate", """{"subject":"舊的"}"""));
        Assert.False(plan.Apply(Hook("a", "Bash", """{"command":"ls"}""")));
        plan.Apply(Hook("b", "TaskCreate", """{"subject":"新的"}"""));
        Assert.Equal(["新的"], plan.Steps.Select(s => s.Text));
    }
}

public class OpenAiBrainTests
{
    [Fact]
    public void 請求的內容_系統說明在最前面_工具可有可無()
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "下一首" });
        var body = OpenAiBrain.Body("some-model", "你是助手", messages, tools: true);
        Assert.Equal("some-model", body["model"]!.ToString());
        Assert.Equal("system", body["messages"]![0]!["role"]!.ToString());
        Assert.Equal("下一首", body["messages"]![1]!["content"]!.ToString());
        Assert.Equal("music", body["tools"]![0]!["function"]!["name"]!.ToString());
        Assert.Equal(["play", "pause", "next", "previous", "now_playing"], ((JsonArray)body["tools"]![0]!["function"]!["parameters"]!["properties"]!["action"]!["enum"]!).Select(a => a!.ToString()));
        Assert.Equal(AssistantTool.All.Select(t => t.Name), ((JsonArray)body["tools"]!).Select(t => t!["function"]!["name"]!.ToString()));
        Assert.Null(OpenAiBrain.Body("m", "s", messages, tools: false)["tools"]);
        Assert.Single(messages);   // 原本的對話不被改到
    }

    [Fact]
    public void 模型清單_排序_去掉_Gemini_的前綴()
    {
        var json = """{"data":[{"id":"zeta"},{"id":"models/gemini-x"},{"id":"alpha"},{"id":"alpha"},{"id":""}]}""";
        Assert.Equal(["alpha", "gemini-x", "zeta"], OpenAiBrain.Models(json));
        Assert.Empty(OpenAiBrain.Models("""{"object":"list"}"""));
    }

    [Fact]
    public void 服務清單_Claude_走_SDK_其他都有網址()
    {
        Assert.Equal("claude", AiService.All[0].Id);
        Assert.All(AiService.All.Where(s => s.Id is not ("claude" or AiService.Custom)), s => Assert.StartsWith("http", s.BaseUrl));
        Assert.False(AiService.Of("ollama").NeedsKey);
        Assert.Equal("claude", AiService.Of("沒有這一家").Id);
    }
}

public class EarsTests
{
    private static float[] Tone(double seconds, float amplitude) =>
        Enumerable.Range(0, (int)(seconds * Ears.Rate)).Select(i => amplitude * MathF.Sin(i * 0.2f)).ToArray();

    [Fact]
    public void 說完之後安靜一秒就結束_只留說話的那一段()
    {
        var d = new SpeechDetector();
        Assert.False(d.Feed(Tone(0.8, 0.001f)));          // 還沒人說話
        Assert.False(d.Feed(Tone(1.5, 0.2f)));            // 說話中
        Assert.False(d.Feed(Tone(0.4, 0.001f)));          // 才停一下，可能只是換氣
        Assert.True(d.Feed(Tone(0.4, 0.001f)));           // 安靜夠久了：說完了
        double seconds = d.Speech().Length / (double)Ears.Rate;
        Assert.InRange(seconds, 1.5, 2.3);                // 前面留一點、後面留一點，不含整段安靜
    }

    [Fact]
    public void 一直沒人說話就放棄()
    {
        var d = new SpeechDetector { Patience = TimeSpan.FromSeconds(2) };
        Assert.False(d.Feed(Tone(1.5, 0.001f)));
        Assert.True(d.Feed(Tone(1, 0.001f)));
        Assert.Empty(d.Speech());
    }

    [Fact]
    public void 有底噪的房間也分得出說話()
    {
        var d = new SpeechDetector();
        Assert.False(d.Feed(Tone(2, 0.01f)));             // 風扇聲：學成底噪，不當成說話
        Assert.False(d.Feed(Tone(1, 0.25f)));
        Assert.True(d.Feed(Tone(1.2, 0.01f)));
        Assert.NotEmpty(d.Speech());
    }

    [Fact]
    public void 說太久會切斷()
    {
        var d = new SpeechDetector { Longest = TimeSpan.FromSeconds(3) };
        Assert.True(d.Feed(Tone(4, 0.2f)));
    }

    [Fact]
    public void 降到十六千赫_分批餵結果一樣長()
    {
        var input = Enumerable.Range(0, 48000).Select(i => (float)i).ToArray();
        var whole = new Downsampler(48000, 16000).Push(input);
        Assert.Equal(16000, whole.Length);
        Assert.Equal(3f, whole[1], 3);                    // 每三個取一個

        var parts = new Downsampler(48000, 16000);
        int total = 0;
        foreach (var chunk in input.Chunk(4410)) total += parts.Push(chunk).Length;
        Assert.InRange(total, 15999, 16001);

        // 44100 → 16000 不是整數倍，累加的小數誤差可能差一個取樣
        Assert.InRange(new Downsampler(44100, 16000).Push(new float[44100]).Length, 15999, 16001);
    }

    [Fact]
    public void 多聲道與各種位元深度轉成單聲道()
    {
        var stereo = new byte[16];
        BitConverter.GetBytes(0.5f).CopyTo(stereo, 0); BitConverter.GetBytes(-0.1f).CopyTo(stereo, 4);
        BitConverter.GetBytes(1f).CopyTo(stereo, 8); BitConverter.GetBytes(1f).CopyTo(stereo, 12);
        Assert.Equal([0.2f, 1f], Ears.Mono(stereo, 16, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).Select(v => MathF.Round(v, 3)));

        var pcm = new byte[4];
        BitConverter.GetBytes((short)16384).CopyTo(pcm, 0); BitConverter.GetBytes((short)-32768).CopyTo(pcm, 2);
        Assert.Equal([0.5f, -1f], Ears.Mono(pcm, 4, new NAudio.Wave.WaveFormat(16000, 16, 1)));
    }

    [Theory]
    [InlineData("  現在播的是哪一首？ ", "現在播的是哪一首？")]
    [InlineData("(音樂)", "")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData("謝謝觀看", "")]
    [InlineData("ご視聴ありがとうございました", "")]
    [InlineData("", "")]
    public void 沒人說話時模型自己編的句子不算(string heard, string kept) => Assert.Equal(kept, Ears.Tidy(heard));

    [Theory]
    [InlineData(2, 384)]       // 很短的句子也留一個下限，太少會聽錯
    [InlineData(6, 500)]
    [InlineData(20, 1200)]
    [InlineData(40, 1500)]     // 不超過模型原本的 30 秒
    public void 只讓模型看這句話需要的長度(double seconds, int context) => Assert.Equal(context, Ears.AudioContext(seconds));

    [Theory]
    [InlineData("zh-TW", "zh")]
    [InlineData("ja-JP", "ja")]
    [InlineData("en-US", "en")]
    [InlineData("", "auto")]
    public void 語言代碼(string windows, string whisper) => Assert.Equal(whisper, Ears.Code(windows));
}

public class SpokenLanguageTests
{
    [Theory]
    [InlineData("現在播放的是 Dream Believers。", "zh-TW", "zh")]
    [InlineData("次の曲にしました。", "zh-TW", "ja")]
    [InlineData("Sure, skipping to the next song.", "zh-TW", "en")]
    [InlineData("다음 곡으로 넘겼어요.", "zh-TW", "ko")]
    [InlineData("Bien sûr, voilà.", "fr-FR", "fr")]
    [InlineData("Sure, here you go.", "de-DE", "de")]
    [InlineData("東京", "ja-JP", "ja")]
    [InlineData("東京", "en-US", "zh")]
    public void 依回答的文字挑朗讀的語言(string text, string language, string spoken) =>
        Assert.Equal(spoken, VoiceAssistant.SpokenLanguage(text, language));
}

public class AssistantToolTests
{
    [Fact]
    public void 每個工具都有名稱_說明_而且必填的參數有定義()
    {
        Assert.Equal(["music", "lyrics", "todo", "timer", "volume", "lyrics_fix", "weather", "open", "alarm", "datetime", "calc", "clipboard"], AssistantTool.All.Select(t => t.Name));
        foreach (var tool in AssistantTool.All)
        {
            Assert.True(tool.Description.Length > 20, tool.Name);
            Assert.All(tool.Required, r => Assert.True(tool.Properties.ContainsKey(r), $"{tool.Name}.{r}"));
            foreach (var (name, property) in tool.Properties) Assert.False(string.IsNullOrEmpty(property!["description"]?.ToString()), $"{tool.Name}.{name}");
        }
    }
}

public class FollowUpTests
{
    [Theory]
    [InlineData("已切到下一首。", 0, false)]
    [InlineData("要加到哪一天的待辦？", 0, true)]
    [InlineData("Which one do you mean?", 1, true)]
    [InlineData("你是說「買牛奶」嗎？」", 0, true)]
    [InlineData("還要我做什麼嗎？", 3, false)]     // 連問太多輪就停，不然沒完沒了
    [InlineData("", 0, false)]
    public void 只有助手反問時才再聽一次(string reply, int rounds, bool again) =>
        Assert.Equal(again, VoiceAssistant.AsksBack(reply, rounds));
}

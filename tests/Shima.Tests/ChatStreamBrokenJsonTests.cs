// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>隨機輸入測試找到的問題：串流裡的 JSON 型別不對、代理字元被切成兩半，不能讓整則回答失敗。</summary>
public class ChatStreamBrokenJsonTests
{
    private static ChatStream Feed(params string[] lines)
    {
        var stream = new ChatStream();
        foreach (var line in lines) stream.Feed(line);
        stream.End();
        return stream;
    }

    [Theory]
    [InlineData("""data: {"choices":[1]}""")]
    [InlineData("""data: {"choices":[[]]}""")]
    [InlineData("""data: {"choices":["x"]}""")]
    [InlineData("""data: {"choices":[null]}""")]
    public void Choices_WithNonObjectItems_AreSkipped(string line)
    {
        var stream = Feed(line, "");
        Assert.Equal("", stream.Text);
    }

    [Fact]
    public void Choices_SkipsNonObjects_AndStillReadsTheRest()
    {
        var stream = Feed("""data: {"choices":[true,{"index":0,"delta":{"content":"b"}}]}""", "");
        Assert.Equal("b", stream.Text);
    }

    [Fact]
    public void LoneSurrogateEscape_BecomesReplacementCharacter()
    {
        Assert.Equal("a�", Feed("""data: {"choices":[{"delta":{"content":"a\ud83d"}}]}""", "").Text);
        Assert.Equal("�a", Feed("""data: {"choices":[{"delta":{"content":"\ude00a"}}]}""", "").Text);
        Assert.Equal("��", Feed("""data: {"choices":[{"delta":{"content":"\ude00\ud83d"}}]}""", "").Text);
    }

    [Fact]
    public void PairedSurrogates_AndEscapedBackslashes_AreLeftAlone()
    {
        Assert.Equal("😀", Feed("""data: {"choices":[{"delta":{"content":"😀"}}]}""", "").Text);
        Assert.Equal(@"\ud83d", Feed("""data: {"choices":[{"delta":{"content":"\\ud83d"}}]}""", "").Text);
        Assert.Equal("é\n\"", Feed("""data: {"choices":[{"delta":{"content":"é\n\""}}]}""", "").Text);
    }

    [Fact]
    public void LoneSurrogate_InToolCall_DoesNotThrow()
    {
        var stream = Feed("""data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"\ud83d","function":{"name":"Re\ude00ad","arguments":"{\"a\":\"\ud83d\"}"}}]}}]}""", "");
        var call = stream.Message()["tool_calls"]!.AsArray()[0]!;
        Assert.Equal("Re�ad", call["function"]!["name"]!.ToString());
    }
}

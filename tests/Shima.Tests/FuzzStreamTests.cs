// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>AI 服務回的 SSE：合法的、壞掉的、隨機切成小塊送進來的。</summary>
public class FuzzStreamTests
{
    /// <summary>每次 Read 只給隨機幾個位元組：模擬網路封包的切法（連 UTF-8 的一個字都可能被切開）。</summary>
    private sealed class Choppy(byte[] data, Random random, int max) : Stream
    {
        private int _at;
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, data.Length - _at), (max == int.MaxValue ? count : random.Next(1, max + 1)));
            Array.Copy(data, _at, buffer, offset, n);
            _at += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _at; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static readonly string[] Chunks =
    [
        """{"choices":[{"index":0,"delta":{"role":"assistant","content":""}}]}""",
        """{"choices":[{"index":0,"delta":{"content":"你好"}}]}""",
        """{"choices":[{"index":0,"delta":{"content":"😀 emoji \n 換行 \"引號\""}}]}""",
        """{"choices":[{"delta":{"content":[{"type":"text","text":"陣列"},{"type":"image","text":"x"},{"text":"無類型"}]}}]}""",
        """{"choices":[{"index":0,"delta":{"reasoning_content":"想"}}]}""",
        """{"choices":[{"index":0,"delta":{"refusal":"","reasoning_content":""}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"Read","arguments":""}}]}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"file"}}]}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"_path\":\"a\"}"}}]}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":1,"id":"call_2","function":{"name":"Bash","arguments":{"command":"ls"}}}]}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":"x","function":{"name":"A","arguments":"1"}},{"function":{"arguments":"2"}},{"function":{"name":"B"}}]}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":-1}, {"index":2147483648}, {"index":"x"}, {"index":1.5}, 5, null, "s", [], {"function":"str"}, {"function":null}, {"function":{"name":5,"arguments":null}}]}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":{"index":0}}}]}""",
        """{"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"extra_content":{"sig":"abc"},"type":"function"}]}}]}""",
        """{"choices":[{"index":0,"delta":{"content":null,"extra":{"a":[1,2]},"n":5,"b":true}}]}""",
        """{"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""",
        """{"choices":[{"index":0,"delta":{},"finish_reason":null}]}""",
        """{"choices":[{"index":0,"delta":{},"finish_reason":5}]}""",
        """{"choices":[{"index":0,"message":{"role":"assistant","content":"整段"}}]}""",
        """{"choices":[1]}""", """{"choices":[null]}""", """{"choices":["x"]}""", """{"choices":[[]]}""", """{"choices":[true,{"index":0,"delta":{"content":"b"}}]}""",
        """{"choices":{}}""", """{"choices":[]}""", """{"choices":5}""", """{"choices":[{"index":"0","delta":5}]}""", """{"choices":[{"index":1,"delta":{"content":"x"}},{"index":0,"delta":{"content":"y"}}]}""",
        """{"usage":{"prompt_tokens":1}}""", """{"error":{"message":"boom","code":429}}""", """{"error":"字串"}""", """{"error":null}""", """{}""", """[]""", """null""", """5""", "\"s\"",
        "{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"", "{\"choices\":[{\"index\":0,", "{\"choices\"", "{", "}", """{"a":}""", "[1,2", """{"choices":[{"delta":{"content":"\ud83d"}}]}""",
        "[DONE]", " [DONE] ", "[DONE", "DONE",
    ];

    private static string Stream(Random r)
    {
        var sb = new StringBuilder();
        string eol() => r.Pick(new[] { "\n", "\n", "\n", "\r\n", "\r" });
        if (r.Chance(0.05)) sb.Append('﻿');
        for (int n = r.Next(0, 14); n > 0; n--)
        {
            switch (r.Next(12))
            {
                case 0: sb.Append(": keep-alive").Append(eol()).Append(eol()); break;
                case 1: sb.Append("event: message").Append(eol()); break;
                case 2: sb.Append("id: 5").Append(eol()).Append("retry: 100").Append(eol()); break;
                case 3: sb.Append(r.Pick(Gen.Noise)).Append(eol()); break;
                case 4: sb.Append(r.Pick(Chunks)).Append(eol()); break;   // 不是 SSE 的行（整段 JSON 或半截）
                default:
                    var chunk = r.Pick(Chunks);
                    if (r.Chance(0.15) && chunk.Length > 4) { int cut = r.Next(1, chunk.Length); sb.Append("data: ").Append(chunk[..cut]).Append(eol()).Append("data: ").Append(chunk[cut..]).Append(eol()); }
                    else sb.Append(r.Pick(new[] { "data: ", "data:", "data:  ", "data: " })).Append(chunk).Append(eol());
                    if (r.Chance(0.8)) sb.Append(eol());
                    break;
            }
        }
        var text = sb.ToString();
        return r.Chance(0.4) ? r.Mangle(text) : text;
    }

    private static string Summary(ChatStream s) =>
        $"{s.Text}|{s.Finished}|{s.Done}|{s.Started}|{s.Error?.ToJsonString()}|{s.Message().ToJsonString()}";

    private static async Task<string> Read(string text, Random? chop, int max)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var reader = new StreamReader(new Choppy(bytes, chop ?? new Random(0), chop == null ? int.MaxValue : max), Encoding.UTF8);
        var seen = new List<string>();
        var stream = await ChatStream.ReadAsync(reader, seen.Add, CancellationToken.None);
        return Summary(stream);
    }

    [Fact]
    public void ChatStream_AnyChopping_GivesTheSameAnswer()
    {
        Fuzz.Run("ChatStream（隨機切塊）", 3001, 400, r => (Stream(r), r.Next()), v =>
        {
            var (text, seed) = v;
            var whole = Read(text, null, 0).GetAwaiter().GetResult();
            var oneByte = Read(text, new Random(seed), 1).GetAwaiter().GetResult();
            var small = Read(text, new Random(seed), 7).GetAwaiter().GetResult();
            Assert.Equal(whole, oneByte);
            Assert.Equal(whole, small);
        }, v => Fuzz.Escape(v.Item1), perCallMs: 300);
    }

    [Fact]
    public void ChatStream_FeedLineByLine_NeverThrows_AtAnyStep()
    {
        // 真的資料都是從 UTF-8 解碼來的，不會有孤立的代理字元：先繞一圈
        Fuzz.Run("ChatStream.Feed／End／Message", 3002, 2500, r => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(Stream(r))), text =>
        {
            var stream = new ChatStream();
            foreach (var line in text.Split('\n'))
            {
                bool grew = stream.Feed(line);
                var before = stream.Text;
                var message = stream.Message();
                Assert.Equal(before, stream.Text);
                if (grew) Assert.NotEqual("", stream.Text);
                Assert.Equal("assistant", message["role"]!.ToString());
                if (message["tool_calls"] is System.Text.Json.Nodes.JsonArray calls)
                    foreach (var call in calls)
                        Assert.False(string.IsNullOrEmpty(call!["id"]!.ToString()), "工具呼叫要有編號");
            }
            stream.End();
            stream.End();
            _ = stream.Message().ToJsonString();
        }, v => Fuzz.Escape(v));
    }
}

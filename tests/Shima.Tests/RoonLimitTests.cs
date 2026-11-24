// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net.WebSockets;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

/// <summary>Roon 的 WebSocket 單一訊息大小上限。</summary>
public class RoonLimitTests
{
    /// <summary>照順序吐出一段段資料，吐完就關閉。</summary>
    private sealed class Script(IEnumerable<(int Count, bool End)> parts, byte[]? first = null) : WebSocket
    {
        private readonly Queue<(int Count, bool End)> _parts = new(parts);
        private WebSocketState _state = WebSocketState.Open;
        private byte[]? _first = first;
        public int Received { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override WebSocketState State => _state;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_parts.Count == 0) { _state = WebSocketState.CloseReceived; return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true)); }
            var (count, end) = _parts.Dequeue();
            count = Math.Min(count, buffer.Count);
            if (_first != null) { _first.AsSpan(0, Math.Min(count, _first.Length)).CopyTo(buffer.AsSpan()); }
            Received += count;
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Binary, end));
        }
    }

    [Fact]
    public async Task 正常大小的訊息照常處理()
    {
        var data = Moo.Build("REQUEST", "com.roonlabs.ping:1/ping", "7");
        var got = new List<MooMessage>();
        await RoonSource.ReceiveAsync(new Script([(data.Length, true)], data), m => { got.Add(m); return Task.CompletedTask; });
        Assert.Equal("7", Assert.Single(got).RequestId);
    }

    [Fact]
    public async Task 單一訊息超過上限_丟例外中斷_不再收()
    {
        // 64 KB 一段、永遠不結束：累積超過 8 MB 就要停
        var endless = Enumerable.Repeat((64 * 1024, false), 1000).ToList();
        var ws = new Script(endless);
        await Assert.ThrowsAsync<InvalidDataException>(() => RoonSource.ReceiveAsync(ws, _ => Task.CompletedTask));
        Assert.True(ws.Received <= RoonSource.MaxMessageBytes + 64 * 1024);
        Assert.True(ws.Received > RoonSource.MaxMessageBytes);
    }

    [Fact]
    public async Task 剛好在上限內的訊息可以收()
    {
        var parts = Enumerable.Repeat((64 * 1024, false), RoonSource.MaxMessageBytes / (64 * 1024) - 1).Append((64 * 1024, true)).ToList();
        var got = new List<MooMessage>();
        await RoonSource.ReceiveAsync(new Script(parts), m => { got.Add(m); return Task.CompletedTask; });
        Assert.Empty(got);   // 內容不是合法的 MOO，被略過，但沒有丟例外
    }
}

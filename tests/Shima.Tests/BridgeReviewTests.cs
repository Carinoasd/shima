// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using Shima.Agents;
using Xunit;

namespace Shima.Tests;

/// <summary>慢慢送內容的連線不能一直佔著名額；每個來源的名額各算各的。</summary>
public class BridgeLanesTests
{
    /// <summary>永遠讀不到東西、也不會結束的串流（宣稱有內容、然後一直不送）。</summary>
    private class Silent : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>每隔一段時間給一個位元組，永遠送不完。</summary>
    private sealed class Trickle(int delayMs) : Silent
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delayMs, cancellationToken);
            buffer.Span[0] = (byte)'x';
            return 1;
        }
    }

    [Fact]
    public async Task 一直不送的內容到期限就放棄()
    {
        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() => AgentBridge.ReadLimitedAsync(new Silent(), AgentBridge.MaxBody, TimeSpan.FromMilliseconds(150)));
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task 一點一點送的內容也是整個算期限()
    {
        // 每 30 毫秒送一個位元組：每次讀都沒逾時，但整個內容送不完
        await Assert.ThrowsAsync<TimeoutException>(() => AgentBridge.ReadLimitedAsync(new Trickle(30), AgentBridge.MaxBody, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task 正常送完的不受影響_太大的還是_null()
    {
        Assert.Equal("{\"a\":1}", await AgentBridge.ReadLimitedAsync(new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}")), 100));
        Assert.Null(await AgentBridge.ReadLimitedAsync(new MemoryStream(new byte[200]), 100));
        Assert.Equal("", await AgentBridge.ReadLimitedAsync(new MemoryStream(), 100));
    }

    [Fact]
    public async Task 逾時之後名額還回去()
    {
        var lanes = new Lanes(10, 2);
        Task Slow() => AgentBridge.ReadLimitedAsync(new Silent(), 100, TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<TimeoutException>(() => lanes.RunAsync("Gemini", Slow));
        await Assert.ThrowsAsync<TimeoutException>(() => lanes.RunAsync("Gemini", Slow));
        Assert.Equal(0, lanes.InLane("Gemini"));
        Assert.True(await lanes.RunAsync("Gemini", () => Task.CompletedTask));   // 又進得來了
    }

    [Fact]
    public async Task 例外與同步丟出的例外都會還名額()
    {
        var lanes = new Lanes(10, 3);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lanes.RunAsync("deck", async () => { await Task.Yield(); throw new InvalidOperationException(); }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lanes.RunAsync("deck", () => throw new InvalidOperationException()));
        Assert.Equal(0, lanes.InLane("deck"));
    }

    [Fact]
    public async Task 一個來源用光自己的名額_別的來源與_Stream_Dock_照樣進得來()
    {
        var lanes = new Lanes(AgentBridge.MaxOpen, AgentBridge.MaxPerSource);
        var hold = new TaskCompletionSource();
        var gemini = new List<Task<bool>>();
        for (int i = 0; i < AgentBridge.MaxPerSource; i++)
        {
            Assert.True(lanes.TryOpen());   // 總名額照舊在驗證前佔
            gemini.Add(lanes.RunAsync("Gemini", () => hold.Task));
        }
        Assert.Equal(AgentBridge.MaxPerSource, lanes.InLane("Gemini"));

        // 第 25 條：自己那組滿了，被擋
        Assert.True(lanes.TryOpen());
        Assert.False(await lanes.RunAsync("Gemini", () => Task.CompletedTask));
        lanes.Close();

        // 別人不受影響
        foreach (var other in new[] { "Claude", "Codex", "deck" })
        {
            Assert.True(lanes.TryOpen());
            Assert.True(await lanes.RunAsync(other, () => Task.CompletedTask));
            lanes.Close();
        }

        hold.SetResult();
        Assert.All(await Task.WhenAll(gemini), ok => Assert.True(ok));
        Assert.Equal(0, lanes.InLane("Gemini"));
    }

    [Fact]
    public void 總名額夠每個來源都拿到自己的份()
    {
        int sources = Enum.GetValues<AgentKind>().Length + 1;   // 每個 agent 一組、再加 Stream Dock
        Assert.True(AgentBridge.MaxOpen >= sources * AgentBridge.MaxPerSource);
    }

    [Fact]
    public void 驗證前的總名額滿了就不收()
    {
        var lanes = new Lanes(2, 2);
        Assert.True(lanes.TryOpen());
        Assert.True(lanes.TryOpen());
        Assert.False(lanes.TryOpen());
        Assert.Equal(2, lanes.Total);
        lanes.Close();
        Assert.True(lanes.TryOpen());
    }
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using Xunit;

namespace Shima.Tests;

/// <summary>更新檔下載：邊收邊算大小，超過發佈的大小就中止並刪掉暫存檔。不對外發請求。</summary>
public class UpdaterDownloadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shima-upd").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Fake(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply());
    }

    private sealed class Unsized(long size) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[64 * 1024];
            for (long sent = 0; sent < size; sent += chunk.Length) await stream.WriteAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, size - sent)));
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private static Updater.Release Release(long size) =>
        new(new Version(9, 0, 0), "https://github.com/Carinoasd/shima/releases/download/v9.0.0/Shima.exe", size, new string('0', 64));

    private string Target => Path.Combine(_dir, "Shima.exe.new");

    [Fact]
    public async Task 大小剛好_寫入並保留()
    {
        var bytes = new byte[300_000];
        Random.Shared.NextBytes(bytes);
        using var http = new HttpClient(new Fake(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        Assert.True(await Updater.DownloadAsync(http, Release(bytes.Length), Target));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Target));
    }

    [Fact]
    public async Task 宣告的長度就超過_不寫任何東西()
    {
        using var http = new HttpClient(new Fake(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2000]) }));
        Assert.False(await Updater.DownloadAsync(http, Release(1000), Target));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task 沒宣告長度_收到超過就中止並刪掉暫存檔()
    {
        using var http = new HttpClient(new Fake(() => new(HttpStatusCode.OK) { Content = new Unsized(3_000_000) }));
        Assert.False(await Updater.DownloadAsync(http, Release(1_000_000), Target));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task 比預期小_留下檔案交給後面的大小與雜湊檢查擋()
    {
        using var http = new HttpClient(new Fake(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[500]) }));
        Assert.True(await Updater.DownloadAsync(http, Release(1000), Target));
        Assert.False(Updater.Matches(Target, 1000, new string('0', 64)));
    }

    [Fact]
    public async Task 伺服器錯誤_丟例外且不留檔案()
    {
        using var http = new HttpClient(new Fake(() => new(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Updater.DownloadAsync(http, Release(1000), Target));
        Assert.False(File.Exists(Target));
    }
}

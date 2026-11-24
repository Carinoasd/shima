// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>
/// 來源送完標頭就停住：讀內容也要有期限（歌詞是一個來源接一個來源問的，卡住一個後面的都等不到；更新卡住「正在更新」永遠不會結束）。
/// 假的 handler 不對外發請求。
/// </summary>
public class StalledDownloadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("shima-stall").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>先給 first 個位元組，之後每 gap 給一點、給到 total；gap 是 null 就永遠停住（而且不理會取消，跟壞掉的連線一樣）。</summary>
    private sealed class Trickle(int first, TimeSpan? gap = null, int total = 0) : Stream
    {
        private int _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _sent; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent < first) { int n = Math.Min(buffer.Length, first - _sent); buffer.Span[..n].Fill((byte)'a'); _sent += n; return n; }
            if (gap is not { } wait) return await new TaskCompletionSource<int>().Task;   // 永遠不回來
            if (_sent >= total) return 0;
            await Task.Delay(wait, CancellationToken.None);
            buffer.Span[0] = (byte)'a';
            _sent++;
            return 1;
        }
    }

    private sealed class Fake(Func<Stream> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) });
    }

    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300), Bound = TimeSpan.FromSeconds(10);

    /// <summary>一定要是自己放棄的（幾秒內），不是測試外面那一層 10 秒的保險先到。</summary>
    private static async Task GivesUp(Func<Task> run)
    {
        var started = Environment.TickCount64;
        await Assert.ThrowsAsync<TimeoutException>(() => run().WaitAsync(Bound));
        Assert.True(Environment.TickCount64 - started < 5000);
    }

    [Fact]
    public async Task 歌詞_送完標頭就停住_整個請求的期限到了就放棄()
    {
        using var http = HttpLimits.Create(Short, new Fake(() => new Trickle(100)));
        await GivesUp(() => HttpLimits.GetStringAsync(http, "https://example.invalid/lyrics"));
    }

    [Fact]
    public async Task 歌詞_只給回應的讀取也有期限()
    {
        using var http = new HttpClient(new Fake(() => new Trickle(100)));
        using var response = await http.GetAsync("https://example.invalid/", HttpCompletionOption.ResponseHeadersRead);
        await GivesUp(() => HttpLimits.ReadAsync(response, Short));
        Assert.Equal(TimeSpan.FromSeconds(10), HttpLimits.ReadLimit);
    }

    [Fact]
    public async Task 歌詞_正常的回應照常讀完()
    {
        using var http = HttpLimits.Create(TimeSpan.FromSeconds(5), new Fake(() => new Trickle(0, TimeSpan.FromMilliseconds(1), 20)));
        Assert.Equal(new string('a', 20), await HttpLimits.GetStringAsync(http, "https://example.invalid/").WaitAsync(Bound));
    }

    private static Updater.Release Release(long size) =>
        new(new Version(9, 0, 0), "https://github.com/Carinoasd/shima/releases/download/v9.0.0/Shima.exe", size, new string('0', 64));

    private string Target => Path.Combine(_dir, "Shima.exe.new");

    [Fact]
    public async Task 更新_收到一部分後停住_放棄並刪掉暫存檔()
    {
        using var http = new HttpClient(new Fake(() => new Trickle(200_000)));
        await GivesUp(() => Updater.DownloadAsync(http, Release(1_000_000), Target, Short));
        Assert.False(File.Exists(Target));
    }

    [Fact]
    public async Task 更新_慢但一直有資料_不算停住()
    {
        // 每 100 毫秒一個位元組、總共約兩秒半：比「多久沒有資料」的兩秒久，但一直有在收。
        // 兩次資料之間留二十倍的餘裕：CI 的機器慢，只留三倍的時候（100 對 300 毫秒）偶爾會被當成停住
        var idle = TimeSpan.FromSeconds(2);
        using var http = new HttpClient(new Fake(() => new Trickle(0, TimeSpan.FromMilliseconds(100), 25)));
        Assert.True(await Updater.DownloadAsync(http, Release(25), Target, idle).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(25, new FileInfo(Target).Length);
        Assert.Equal(TimeSpan.FromSeconds(60), Updater.Stalled);
    }
}

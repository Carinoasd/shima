// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Shima.Lyrics;
using Xunit;

namespace Shima.Tests;

/// <summary>歌詞來源的網路行為：酷狗走 HTTPS、回應大小上限。全部用假的 handler，不對外發請求。</summary>
public class LyricsNetTests
{
    private sealed class Fake(Func<Uri, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<Uri> Sent { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add(request.RequestUri!);
            return Task.FromResult(reply(request.RequestUri!));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>不宣告長度的內容（像 chunked）：只能邊讀邊數。</summary>
    private sealed class Unsized(long size) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'a');
            for (long sent = 0; sent < size; sent += chunk.Length) await stream.WriteAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, size - sent)));
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private static readonly SongQuery Query = new("晴天", "周杰伦", "叶惠美", 269);

    private const string Lrc = "[00:01.00]故事的小黄花\n[00:05.00]从出生那年就飘着\n";

    private static string KugouSearch(string name = "晴天", string singer = "周杰伦", string hash = "B3A52A7A958BF0AED0EBFBA2E9A818B7") =>
        $$$"""{"status":1,"error_code":0,"data":{"total":1,"lists":[{"SongName":"{{{name}}}","SingerName":"{{{singer}}}","Duration":269,"FileHash":"{{{hash}}}","AlbumName":"叶惠美"}]}}""";

    private static string KugouCandidates(string id = "100 200", string key = "A+B/C=") =>
        $$$"""{"status":200,"candidates":[{"id":"{{{id}}}","accesskey":"{{{key}}}"}]}""";

    private static string KugouDownload(string lrc) =>
        $$$"""{"status":200,"content":"{{{Convert.ToBase64String(Encoding.UTF8.GetBytes(lrc))}}}"}""";

    private static Fake KugouFake(string search, string candidates, string download) => new(u => u.Host switch
    {
        "songsearch.kugou.com" => Json(search),
        "krcs.kugou.com" => Json(candidates),
        "lyrics.kugou.com" => Json(download),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    });

    [Fact]
    public async Task 酷狗_全部走_HTTPS_並編碼參數()
    {
        var fake = KugouFake(KugouSearch(), KugouCandidates(), KugouDownload(Lrc));
        var hit = await new KugouProvider(fake).FindAsync(Query);

        Assert.NotNull(hit);
        Assert.True(hit!.Exact);
        Assert.Equal(2, hit.Sheet.Lines.Count);
        Assert.Equal(3, fake.Sent.Count);
        Assert.All(fake.Sent, u => Assert.Equal("https", u.Scheme));
        Assert.Equal(["songsearch.kugou.com", "krcs.kugou.com", "lyrics.kugou.com"], fake.Sent.Select(u => u.Host));
        Assert.Contains("platform=WebFilter", fake.Sent[0].Query);
        Assert.Contains("id=100%20200", fake.Sent[2].Query);
        Assert.Contains("accesskey=A%2BB%2FC%3D", fake.Sent[2].Query);
    }

    [Fact]
    public async Task 酷狗_搜尋結果的_em_標記被拿掉()
    {
        var fake = KugouFake(KugouSearch("<em>晴天</em>", "<em>周杰伦</em>"), KugouCandidates(), KugouDownload(Lrc));
        var hit = await new KugouProvider(fake).FindAsync(Query);
        Assert.NotNull(hit);
        Assert.True(hit!.Exact);
        Assert.Equal("晴天", KugouProvider.StripEm("<em>晴</em>天"));
    }

    [Theory]
    [InlineData("""[1,2]""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":{"lists":"x"}}""")]
    [InlineData("""{"data":{"lists":[1,"a",null]}}""")]
    [InlineData("""{"data":{"lists":[{"SongName":5,"SingerName":null,"Duration":"x","FileHash":7}]}}""")]
    [InlineData("""not json""")]
    public async Task 酷狗_搜尋回應欄位缺少或型別不對_回_null(string body)
    {
        Assert.Null(await new KugouProvider(KugouFake(body, KugouCandidates(), KugouDownload(Lrc))).FindAsync(Query));
    }

    [Theory]
    [InlineData("""{"candidates":{}}""")]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("""{"candidates":[3]}""")]
    [InlineData("""{"candidates":[{"id":null,"accesskey":[]}]}""")]
    [InlineData("""{"candidates":[{"id":"1"}]}""")]
    public async Task 酷狗_歌詞編號回應有問題_回_null(string candidates)
    {
        Assert.Null(await new KugouProvider(KugouFake(KugouSearch(), candidates, KugouDownload(Lrc))).FindAsync(Query));
    }

    [Theory]
    [InlineData("""{"content":5}""")]
    [InlineData("""{"content":"!!!不是base64"}""")]
    [InlineData("""{"status":200}""")]
    [InlineData("""[]""")]
    public async Task 酷狗_下載回應有問題_回_null(string download)
    {
        Assert.Null(await new KugouProvider(KugouFake(KugouSearch(), KugouCandidates(), download)).FindAsync(Query));
    }

    [Fact]
    public async Task 酷狗_歌詞文字超過_1MB_當成沒找到()
    {
        var big = string.Concat(Enumerable.Repeat("[00:01.00]" + new string('字', 100) + "\n", 6000));   // 約 0.6M 字元 → 還在上限內
        Assert.NotNull(await new KugouProvider(KugouFake(KugouSearch(), KugouCandidates(), KugouDownload(big))).FindAsync(Query));

        var tooBig = string.Concat(Enumerable.Repeat("[00:01.00]" + new string('字', 100) + "\n", 12000));
        Assert.True(tooBig.Length > HttpLimits.MaxText);
        Assert.Null(await new KugouProvider(KugouFake(KugouSearch(), KugouCandidates(), KugouDownload(tooBig))).FindAsync(Query));
    }

    [Fact]
    public async Task 回應超過_4MB_是沒找到_不是例外()
    {
        var huge = new string('a', HttpLimits.MaxBody + 1);
        Assert.Null(await new KugouProvider(new Fake(_ => Json(huge))).FindAsync(Query));
        Assert.Null(await new NetEaseProvider(new Fake(_ => Json(huge))).FindAsync(Query));
        Assert.Null(await new QQProvider(new Fake(_ => Json(huge))).FindAsync(Query));
        Assert.Null(await new LrclibProvider(new Fake(_ => Json(huge))).FindAsync(Query with { Album = "" }));
    }

    [Fact]
    public async Task 沒宣告長度的超大回應_邊讀邊數_也是沒找到()
    {
        HttpResponseMessage Chunked(Uri _) => new(HttpStatusCode.OK) { Content = new Unsized(HttpLimits.MaxBody + 1024) };
        Assert.Null(await new KugouProvider(new Fake(Chunked)).FindAsync(Query));
        Assert.Null(await new NetEaseProvider(new Fake(Chunked)).FindAsync(Query));
        Assert.Null(await new LrclibProvider(new Fake(Chunked)).FindAsync(Query));
    }

    [Fact]
    public async Task 沒超過上限的回應照常讀()
    {
        var fine = KugouFake(KugouSearch(), KugouCandidates(), KugouDownload(Lrc));
        Assert.NotNull(await new KugouProvider(fine).FindAsync(Query));
        var content = new Unsized(1000);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        Assert.Equal(1000, (await HttpLimits.ReadAsync(response))!.Length);
    }

    [Fact]
    public async Task LRCLIB_單一份歌詞超過_1MB_當成沒找到()
    {
        string Body(int chars) => "{\"trackName\":\"晴天\",\"artistName\":\"周杰伦\",\"duration\":269,\"syncedLyrics\":\"[00:01.00]" + new string('a', chars) + "\"}";
        Assert.NotNull(await new LrclibProvider(new Fake(_ => Json(Body(1000)))).FindAsync(Query));
        Assert.Null(await new LrclibProvider(new Fake(_ => Json(Body(HttpLimits.MaxText + 10)))).FindAsync(Query));
    }
}

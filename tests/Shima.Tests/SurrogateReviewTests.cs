// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shima.Listening;
using Xunit;

namespace Shima.Tests;

/// <summary>網頁能把標題設成被截在 emoji 中間的字串（孤立的代理字元）：System.Text.Json 不會因此丟例外（寫成 U+FFFD），寫檔與回報都不受影響。</summary>
public class SurrogateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shima-sur-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

    private const string Lone = "歌\uD83D";             // 高代理字元落單
    private const string LoneLow = "\uDE00歌";         // 低代理字元落單

    [Fact]
    public void System_Text_Json_序列化孤立的代理字元不會丟例外_而是寫成_U_FFFD()
    {
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        Assert.Equal("歌\uFFFD", JsonNode.Parse(JsonSerializer.Serialize(new { Title = Lone }, options))!["Title"]!.ToString());
        Assert.Equal("\uFFFD歌", JsonNode.Parse(new JsonObject { ["t"] = LoneLow }.ToJsonString())!["t"]!.ToString());
    }

    [Fact]
    public void 孤立代理字元的歌名_寫紀錄_進待送清單_組出回報內容_都不丟例外()
    {
        var listen = new Listen(Now.AddHours(-2), Lone, LoneLow, "\uD800\uD800", "Chrome", 240, 200);

        // 紀錄檔
        var line = HistoryFile.ToLine(listen);
        Assert.Equal("歌\uFFFD", HistoryFile.FromLine(line)!.Title);

        // 待送清單：存檔不失敗、重開之後讀得回來
        var path = Path.Combine(_folder, "pending-scrobbles.jsonl");
        var logs = new List<string>();
        var store = new PendingScrobbles(path, () => Now, logs.Add);
        Assert.True(store.Add("listenbrainz", listen));
        Assert.Empty(logs);
        Assert.Single(new PendingScrobbles(path, () => Now, _ => { }).Items("listenbrainz"));

        // ListenBrainz 的請求內容
        var body = ListenBrainzJson.Single(listen);
        Assert.Equal("歌\uFFFD", (string)JsonNode.Parse(body)!["payload"]![0]!["track_metadata"]!["track_name"]!);
    }

    [Fact]
    public async Task Last_fm_的請求也組得出來()
    {
        var bodies = new List<string>();
        var handler = new Capture(bodies);
        var fm = new LastFm(handler, _ => { }, TimeSpan.Zero, TimeSpan.Zero) { ApiKey = "KEY", Secret = "SECRET-xyz", Session = "SESSION-xyz" };
        var listen = new Listen(Now.AddHours(-2), Lone, LoneLow, "", "Chrome", 240, 200);
        Assert.True(await fm.Submit(listen));
        Assert.Single(bodies);
    }

    private sealed class Capture(List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""<lfm status="ok"><scrobbles accepted="1" ignored="0"></scrobbles></lfm>""") };
        }
    }
}

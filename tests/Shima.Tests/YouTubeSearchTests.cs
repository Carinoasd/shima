// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Shima.Agents;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class YouTubeSearchTests
{
    private sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private const string SampleWebJson = """
    {
      "contents": {
        "twoColumnSearchResultsRenderer": {
          "primaryContents": {
            "sectionListRenderer": {
              "contents": [
                {
                  "itemSectionRenderer": {
                    "contents": [
                      {
                        "videoRenderer": {
                          "videoId": "h5XQqKuek5A",
                          "title": { "runs": [ { "text": "【Official MV】星降る海 – Aqu3ra / 月見ヤチヨ(cv.早見沙織) from 超かぐや姫！" } ] },
                          "ownerText": { "runs": [ { "text": "『超かぐや姫 ! 』公式和Aqu3ra" } ] },
                          "lengthText": { "simpleText": "4:12" },
                          "thumbnail": {
                            "thumbnails": [
                              { "url": "https://i.ytimg.com/vi/h5XQqKuek5A/hqdefault.jpg", "width": 360, "height": 202 }
                            ]
                          }
                        }
                      },
                      {
                        "videoRenderer": {
                          "videoId": "h5XQqKuek5A",
                          "title": { "runs": [ { "text": "重複影片應略過" } ] }
                        }
                      },
                      {
                        "videoRenderer": {
                          "videoId": "../evil",
                          "title": { "runs": [ { "text": "不合法 ID 應略過" } ] }
                        }
                      },
                      {
                        "videoRenderer": {
                          "videoId": "dQw4w9WgXcQ",
                          "title": { "runs": [ { "text": "Second Video" } ] },
                          "longBylineText": { "runs": [ { "text": "Some Channel" } ] },
                          "lengthText": { "simpleText": "3:33" },
                          "thumbnail": {
                            "thumbnails": [
                              { "url": "https://evil.example/track.png", "width": 320, "height": 180 }
                            ]
                          }
                        }
                      }
                    ]
                  }
                }
              ]
            }
          }
        }
      }
    }
    """;

    private const string SampleMusicJson = """
    {
      "contents": {
        "tabbedSearchResultsRenderer": {
          "tabs": [
            {
              "tabRenderer": {
                "content": {
                  "sectionListRenderer": {
                    "contents": [
                      {
                        "musicShelfRenderer": {
                          "contents": [
                            {
                              "musicResponsiveListItemRenderer": {
                                "playlistItemData": { "videoId": "a1B2c3D4e5F" },
                                "flexColumns": [
                                  { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "星降る海 - Starry Sea" } ] } } },
                                  { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "歌曲" }, { "text": " • " }, { "text": "Aqu3ra" }, { "text": " • " }, { "text": "Cosmic Princess Kaguya!" }, { "text": " • " }, { "text": "4:14" } ] } } }
                                ],
                                "thumbnail": {
                                  "musicThumbnailRenderer": {
                                    "thumbnail": {
                                      "thumbnails": [
                                        { "url": "https://lh3.googleusercontent.com/cover123=w120-h120", "width": 120, "height": 120 }
                                      ]
                                    }
                                  }
                                }
                              }
                            }
                          ]
                        }
                      }
                    ]
                  }
                }
              }
            }
          ]
        }
      }
    }
    """;

    [Fact]
    public void 解析一般_YouTube_結果並過濾重複_不合法編號與不明縮圖()
    {
        var items = YouTubeSearch.ParseYouTube(SampleWebJson, limit: 8);
        Assert.Equal(2, items.Count);

        Assert.Equal("h5XQqKuek5A", items[0].VideoId);
        Assert.Equal("https://www.youtube.com/watch?v=h5XQqKuek5A", items[0].WatchUrl);
        Assert.Equal("『超かぐや姫 ! 』公式和Aqu3ra", items[0].Channel);
        Assert.Equal("4:12", items[0].Duration);
        Assert.Equal("https://i.ytimg.com/vi/h5XQqKuek5A/hqdefault.jpg", items[0].ThumbnailUrl);
        Assert.False(items[0].Music);

        // 第二筆縮圖來自不明主機，應被濾成空字串
        Assert.Equal("dQw4w9WgXcQ", items[1].VideoId);
        Assert.Equal("Some Channel", items[1].Channel);
        Assert.Equal("", items[1].ThumbnailUrl);
    }

    [Fact]
    public void 解析_YouTube_Music_結果並拆出藝人與長度()
    {
        var items = YouTubeSearch.ParseMusic(SampleMusicJson, limit: 8);
        Assert.Single(items);
        Assert.Equal("a1B2c3D4e5F", items[0].VideoId);
        Assert.Equal("https://music.youtube.com/watch?v=a1B2c3D4e5F", items[0].WatchUrl);
        Assert.Equal("星降る海 - Starry Sea", items[0].Title);
        Assert.Equal("Aqu3ra", items[0].Channel);
        Assert.Equal("4:14", items[0].Duration);
        Assert.Equal("https://lh3.googleusercontent.com/cover123=w120-h120", items[0].ThumbnailUrl);
        Assert.True(items[0].Music);
    }

    [Theory]
    [InlineData("h5XQqKuek5A", true)]
    [InlineData("a1B2c3D4_-0", true)]
    [InlineData("short", false)]
    [InlineData("tooLongVideoId123", false)]
    [InlineData("h5XQqKuek5/", false)]
    [InlineData("h5XQqKuek5?", false)]
    [InlineData("", false)]
    public void 影片編號驗證(string id, bool valid) => Assert.Equal(valid, YouTubeSearch.ValidVideoId(id));

    [Theory]
    [InlineData("https://i.ytimg.com/vi/h5XQqKuek5A/hqdefault.jpg", true)]
    [InlineData("https://i9.ytimg.com/vi/h5XQqKuek5A/mqdefault.jpg", true)]
    [InlineData("https://lh3.googleusercontent.com/abc", true)]
    [InlineData("https://yt3.ggpht.com/abc", true)]
    [InlineData("http://i.ytimg.com/vi/h5XQqKuek5A/hqdefault.jpg", false)]
    [InlineData("https://i.ytimg.com.evil.example/a.jpg", false)]
    [InlineData("https://127.0.0.1/a.jpg", false)]
    [InlineData("https://user@i.ytimg.com/a.jpg", false)]
    [InlineData("https://i.ytimg.com:8443/a.jpg", false)]
    public void 縮圖網址白名單(string url, bool allowed) => Assert.Equal(allowed, YouTubeSearch.AllowedThumb(url));

    [Fact]
    public void 壞掉或過深的_JSON_不丟例外()
    {
        Assert.Empty(YouTubeSearch.ParseYouTube("{not json"));
        Assert.Empty(YouTubeSearch.ParseMusic("{not json"));
        var deep = new string('[', 40) + "{}" + new string(']', 40);
        Assert.Empty(YouTubeSearch.ParseYouTube(deep));
        Assert.Empty(YouTubeSearch.ParseMusic(deep));
    }

    [Fact]
    public void 瀏覽器備援搜尋網址通過_Opener_信任檢查()
    {
        var yt = YouTubeSearch.SearchUrl("星降る海 Aqu3ra", music: false);
        var ytm = YouTubeSearch.SearchUrl("星降る海 Aqu3ra", music: true);
        Assert.StartsWith("https://www.youtube.com/results?search_query=", yt);
        Assert.StartsWith("https://music.youtube.com/search?q=", ytm);
        Assert.True(Opener.Trusted(yt));
        Assert.True(Opener.Trusted(ytm));
    }

    [Fact]
    public async Task SearchAsync_送往對應端點並帶_Music_Origin()
    {
        var stub = new StubHttp(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.Host.StartsWith("music.") ? SampleMusicJson : SampleWebJson, Encoding.UTF8, "application/json"),
        });
        var search = new YouTubeSearch(stub);

        var yt = await search.SearchAsync("星降る海", music: false, lang: "zh-TW");
        Assert.Equal("www.youtube.com", stub.LastRequest!.RequestUri!.Host);
        Assert.Equal(2, yt.Count);

        var ytm = await search.SearchAsync("星降る海", music: true, lang: "ja");
        Assert.Equal("music.youtube.com", stub.LastRequest!.RequestUri!.Host);
        Assert.True(stub.LastRequest.Headers.TryGetValues("Origin", out var origins) && origins.Contains("https://music.youtube.com"));
        Assert.Single(ytm);
    }
}

[Collection(nameof(OpenFlowTests))]
public class YouTubeSkillTests : IDisposable
{
    private readonly YouTubeSearch _origYouTube = Skills.YouTube;
    private readonly Action<string> _origLauncher = Skills.Launcher;
    private readonly Func<string, CancellationToken, Task<bool?>>? _origConfirm = Skills.Confirm;
    private readonly List<string> _launched = [];

    public YouTubeSkillTests()
    {
        Skills.Launcher = _launched.Add;
        Skills.Confirm = null;
    }

    public void Dispose()
    {
        Skills.YouTube = _origYouTube;
        Skills.Launcher = _origLauncher;
        Skills.Confirm = _origConfirm;
    }

    private sealed class StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public async Task AI_工具_play_會直接開啟第一筆影片()
    {
        const string json = """
        {"contents":{"videoRenderer":{"videoId":"h5XQqKuek5A","title":{"runs":[{"text":"星降る海"}]},"ownerText":{"runs":[{"text":"Aqu3ra"}]},"lengthText":{"simpleText":"4:12"}}}}
        """;
        Skills.YouTube = new YouTubeSearch(new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));

        var reply = await Skills.RunAsync("youtube", new JsonObject { ["query"] = "星降る海", ["mode"] = "youtube", ["action"] = "play" });
        Assert.Contains("星降る海", reply);
        Assert.Single(_launched);
        Assert.Equal("https://www.youtube.com/watch?v=h5XQqKuek5A", _launched[0]);
    }

    [Fact]
    public async Task AI_工具_search_只回傳清單不開瀏覽器()
    {
        const string json = """
        {"contents":{"musicResponsiveListItemRenderer":{"playlistItemData":{"videoId":"a1B2c3D4e5F"},"flexColumns":[{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"星降る海"}]}}},{"musicResponsiveListItemFlexColumnRenderer":{"text":{"runs":[{"text":"Aqu3ra • 4:14"}]}}}]}}}
        """;
        Skills.YouTube = new YouTubeSearch(new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));

        var reply = await Skills.RunAsync("youtube", new JsonObject { ["query"] = "星降る海", ["mode"] = "music", ["action"] = "search" });
        Assert.Contains("1. 星降る海 — Aqu3ra (4:14)", reply);
        Assert.Empty(_launched);
    }

    [Fact]
    public async Task AI_工具搜尋失敗時_play_自動退回開啟搜尋頁()
    {
        Skills.YouTube = new YouTubeSearch(new StubHttp(_ => throw new HttpRequestException("down")));

        var reply = await Skills.RunAsync("youtube", new JsonObject { ["query"] = "星降る海", ["mode"] = "music", ["action"] = "play" });
        Assert.Contains("搜尋頁", reply);
        Assert.Single(_launched);
        Assert.StartsWith("https://music.youtube.com/search?q=", _launched[0]);
    }

    [Fact]
    public async Task AI_工具拒絕過長關鍵字且退回搜尋頁仍受_Opener_保護()
    {
        Skills.YouTube = new YouTubeSearch(new StubHttp(_ => throw new HttpRequestException("down")));

        // 過長查詢直接攔下（避免把整段剪貼簿帶去搜尋）
        var tooLong = await Skills.RunAsync("youtube", new JsonObject { ["query"] = new string('a', 150), ["action"] = "play" });
        Assert.Contains("太長", tooLong);
        Assert.Empty(_launched);

        // 搜尋失敗退回搜尋頁時，如果夾帶主機名稱仍會被 Opener.Trusted 擋下要求確認
        var hostish = await Skills.RunAsync("youtube", new JsonObject { ["query"] = "evil.example", ["action"] = "play" });
        Assert.Contains("確認", hostish);
        Assert.Empty(_launched);
    }
}

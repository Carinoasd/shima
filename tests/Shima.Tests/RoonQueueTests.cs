// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using Shima.Playback;
using Xunit;

namespace Shima.Tests;

public class RoonQueueTests
{
    private static string Song(long id, string title, int length = 200) => $$"""
        {"queue_item_id": {{id}}, "length": {{length}}, "image_key": "img{{id}}",
         "one_line": {"line1": "{{title}}"},
         "two_line": {"line1": "{{title}}", "line2": "μ's"},
         "three_line": {"line1": "{{title}}", "line2": "μ's", "line3": "School idol diary"} }
        """;

    private static List<RoonQueueItem> Subscribed(params string[] songs)
    {
        var queue = new List<RoonQueueItem>();
        RoonQueue.Apply(queue, "Subscribed", JsonNode.Parse($$"""{"items": [{{string.Join(",", songs)}}]}"""));
        return queue;
    }

    [Fact]
    public void 訂閱時讀進整份佇列()
    {
        var queue = Subscribed(Song(11, "僕らは今のなかで", 263), Song(12, "Snow halation"));
        Assert.Equal([11L, 12L], queue.Select(q => q.Id));
        var first = queue[0];
        Assert.Equal(("僕らは今のなかで", "μ's", "School idol diary", "img11"), (first.Title, first.Artist, first.Album, first.ImageKey));
        Assert.Equal(TimeSpan.FromSeconds(263), first.Length);
    }

    [Fact]
    public void 一則訊息裡的多個變動依序套用()
    {
        var queue = Subscribed(Song(1, "A"), Song(2, "B"), Song(3, "C"));
        // 先拿掉正在播的那首（換歌了），再在最後面加兩首；第二個 index 以拿掉之後的清單為準
        var body = JsonNode.Parse($$"""
            {"changes": [
              {"operation": "remove", "index": 0, "count": 1},
              {"operation": "insert", "index": 2, "items": [{{Song(4, "D")}}, {{Song(5, "E")}}]}
            ]}
            """);
        Assert.True(RoonQueue.Apply(queue, "Changed", body));
        Assert.Equal(["B", "C", "D", "E"], queue.Select(q => q.Title));
    }

    [Fact]
    public void 超出範圍的變動不會壞掉()
    {
        var queue = Subscribed(Song(1, "A"));
        var body = JsonNode.Parse($$"""
            {"changes": [
              {"operation": "remove", "index": 5, "count": 3},
              {"operation": "insert", "index": 99, "items": [{{Song(2, "B")}}]},
              {"operation": "remove", "index": 0, "count": 99},
              {"operation": "shuffle", "index": 0}
            ]}
            """);
        RoonQueue.Apply(queue, "Changed", body);
        Assert.Empty(queue);
    }

    [Fact]
    public void 沒有編號的項目略過_其他回應不動清單()
    {
        var queue = Subscribed("""{"three_line": {"line1": "沒有編號"}}""", Song(7, "G"));
        Assert.Single(queue);
        Assert.False(RoonQueue.Apply(queue, "Unsubscribed", null));
        Assert.Single(queue);
    }

    [Fact]
    public void 佇列的請求內容()
    {
        Assert.Equal("""{"zone_or_output_id":"z1","max_item_count":100,"subscription_key":3}""", RoonQueue.Subscribe("z1", 100, 3).ToJsonString());
        Assert.Equal("""{"subscription_key":3}""", RoonQueue.Unsubscribe(3).ToJsonString());
        Assert.Equal("""{"zone_or_output_id":"z1","queue_item_id":42}""", RoonQueue.PlayFromHere("z1", 42).ToJsonString());
    }

    [Fact]
    public void 設定只帶要改的項目()
    {
        Assert.Equal("""{"zone_or_output_id":"z","shuffle":true}""", RoonQueue.ChangeSettings("z", shuffle: true).ToJsonString());
        Assert.Equal("""{"zone_or_output_id":"z","loop":"next"}""", RoonQueue.ChangeSettings("z", loop: "next").ToJsonString());
        Assert.Equal("""{"zone_or_output_id":"z","auto_radio":false}""", RoonQueue.ChangeSettings("z", autoRadio: false).ToJsonString());
    }

    [Fact]
    public void 讀區域的隨機循環設定()
    {
        var zone = JsonNode.Parse("""{"zone_id": "z", "settings": {"loop": "loop_one", "shuffle": true, "auto_radio": false}}""");
        Assert.Equal(new RoonZoneSettings(true, "loop_one", false), RoonQueue.Settings(zone));
        Assert.Null(RoonQueue.Settings(JsonNode.Parse("""{"zone_id": "z"}""")));
    }
}

public class RoonBrowseTests
{
    [Fact]
    public void 搜尋從最上層開始_不帶項目()
    {
        var o = RoonBrowse.Search("Aqours", "z1");
        Assert.Equal("search", o["hierarchy"]!.ToString());
        Assert.Equal(RoonBrowse.Session, o["multi_session_key"]!.ToString());
        Assert.Equal("z1", o["zone_or_output_id"]!.ToString());
        Assert.True(o["pop_all"]!.GetValue<bool>());
        Assert.Equal("Aqours", o["input"]!.ToString());
        Assert.Null(o["item_key"]);
    }

    [Fact]
    public void 往下走帶項目_不帶回最上層()
    {
        var o = RoonBrowse.Open("435:3", "z1", "search");
        Assert.Equal(("435:3", "search", "z1"), (o["item_key"]!.ToString(), o["hierarchy"]!.ToString(), o["zone_or_output_id"]!.ToString()));
        Assert.Null(o["pop_all"]);
        Assert.Null(o["input"]);
        Assert.Equal(1, RoonBrowse.Back(0, "z1", "search")["pop_levels"]!.GetValue<int>());
        // 沒有區域就不帶
        Assert.Null(RoonBrowse.Home("")["zone_or_output_id"]);
    }

    [Fact]
    public void 載入跟瀏覽用同一個階層與工作階段()
    {
        Assert.Equal("""{"hierarchy":"search","multi_session_key":"shima","offset":0,"count":100}""", RoonBrowse.Load("search").ToJsonString());
    }

    private const string SearchPage = """
        {"offset": 0,
         "list": {"title": "Search", "count": 4, "level": 0, "subtitle": "Aqours"},
         "items": [
           {"title": "Artists", "item_key": "10:0", "hint": "list"},
           {"title": "Top Result", "hint": "header"},
           {"title": "青空Jumping Heart", "subtitle": "Aqours", "image_key": "abc", "item_key": "10:2", "hint": "action_list"},
           {"title": "Something new", "item_key": "10:3", "hint": "carousel"}
         ]}
        """;

    [Fact]
    public void 解析一層清單_不認得的提示當作一般項目()
    {
        var page = RoonBrowse.Page(JsonNode.Parse(SearchPage));
        Assert.Equal(("Search", "Aqours", 4, 0, ""), (page.Title, page.Subtitle, page.Count, page.Level, page.Hint));
        Assert.Equal(4, page.Items.Count);
        Assert.True(page.Items[1].IsHeader);
        var track = page.Items[2];
        Assert.Equal(("青空Jumping Heart", "Aqours", "abc", "10:2", "action_list"), (track.Title, track.Subtitle, track.ImageKey, track.ItemKey, track.Hint));
        Assert.Equal("", page.Items[3].Hint);
    }

    [Fact]
    public void 解析瀏覽結果()
    {
        var r = RoonBrowse.Result(JsonNode.Parse("""{"action": "message", "message": "Nothing to play", "is_error": true}"""));
        Assert.Equal(("message", "Nothing to play", true), (r.Action, r.Message, r.IsError));
        Assert.Equal("list", RoonBrowse.Result(JsonNode.Parse("""{"action": "list", "list": {"title": "Albums", "count": 3, "level": 1}}""")).Action);
        Assert.Equal("none", RoonBrowse.Result(null).Action);
        var replaced = RoonBrowse.Result(JsonNode.Parse("""{"action": "replace_item", "item": {"title": "Unfavorite", "item_key": "9:1", "hint": "action"}}"""));
        Assert.True(replaced.Item!.IsAction);
    }

    private static RoonBrowsePage Page(string hint, params (string Title, string Hint)[] items) =>
        new("", "", items.Length, 2, hint, items.Select((i, n) => new RoonBrowseItem(i.Title, "", "", "1:" + n, i.Hint, "")).ToList());

    [Fact]
    public void 直接播放時一路往下走到預設動作()
    {
        // 動作清單：點第一個動作
        Assert.Equal("Play Now", RoonBrowse.NextStep(Page("action_list", ("Play Now", "action"), ("Add Next", "action"), ("Queue", "action")))!.Title);
        // 專輯頁：先點「播放專輯」那個動作清單，不是點曲目
        Assert.Equal("Play Album", RoonBrowse.NextStep(Page("", ("Play Album", "action_list"), ("Track 1", "action_list")))!.Title);
        // 只有一個版本的層：走進去
        Assert.Equal("v1", RoonBrowse.NextStep(Page("", ("v1", "list")))!.Title);
        // 好幾個子清單（例如搜尋最上層的分組）：沒辦法替使用者選
        Assert.Null(RoonBrowse.NextStep(Page("", ("Artists", "list"), ("Albums", "list"))));
        // 標題不能點
        Assert.Null(RoonBrowse.NextStep(Page("", ("Header", "header"))));
    }
}

public class RoonRegistrationTests
{
    [Fact]
    public void 瀏覽服務放在選用服務()
    {
        var reg = RoonSource.Registration("ext", "1.2.3", "tok");
        Assert.Equal(["com.roonlabs.transport:2"], (reg["required_services"] as JsonArray)!.Select(s => s!.ToString()));
        Assert.Equal(["com.roonlabs.browse:1"], (reg["optional_services"] as JsonArray)!.Select(s => s!.ToString()));
        Assert.Equal("tok", reg["token"]!.ToString());
        Assert.Null(RoonSource.Registration("ext", "1.2.3", null)["token"]);
    }

    [Fact]
    public void 從註冊回應看出有沒有瀏覽服務()
    {
        var yes = JsonNode.Parse("""{"core_id": "c", "token": "t", "provided_services": ["com.roonlabs.transport:2", "com.roonlabs.browse:1"]}""");
        var no = JsonNode.Parse("""{"core_id": "c", "token": "t", "provided_services": ["com.roonlabs.transport:2"]}""");
        Assert.True(RoonSource.Provides(yes, RoonBrowse.Service));
        Assert.False(RoonSource.Provides(no, RoonBrowse.Service));
        Assert.False(RoonSource.Provides(null, RoonBrowse.Service));
    }

    [Fact]
    public void 縮圖網址()
    {
        Assert.Equal("http://127.0.0.1:9330/api/image/a%2Fb?scale=fit&width=96&height=96&format=image/jpeg", RoonSource.ImageUrl("127.0.0.1", 9330, "a/b", 96));
    }
}

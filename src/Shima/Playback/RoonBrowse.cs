// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;

namespace Shima.Playback;

/// <summary>
/// 瀏覽清單裡的一項。Hint 是 Roon 的原字："action"（點了就執行）、"action_list"（下一層是動作清單）、
/// "list"（下一層是清單）、"header"（只是標題，不能點）；空字串或不認得的都當一般項目。
/// </summary>
public sealed record RoonBrowseItem(string Title, string Subtitle, string ImageKey, string ItemKey, string Hint, string InputPrompt)
{
    public bool IsHeader => Hint == "header" || ItemKey.Length == 0;
    public bool IsAction => Hint == "action";
}

/// <summary>一層瀏覽清單：標題、總數、第幾層（0 是最上層）、是不是動作清單，以及載入的項目。</summary>
public sealed record RoonBrowsePage(string Title, string Subtitle, int Count, int Level, string Hint, IReadOnlyList<RoonBrowseItem> Items)
{
    public bool IsActionList => Hint == "action_list";
}

/// <summary>
/// browse 的結果。Action 是 Roon 的原字："list"（要接著 load 新的一層）、"message"（顯示 Message）、
/// "none"（動作做完了）、"replace_item"、"remove_item"。
/// </summary>
public sealed record RoonBrowseResult(string Action, string Message, bool IsError, RoonBrowseItem? Item);

/// <summary>
/// browse 服務（com.roonlabs.browse:1）：組請求、解析回應。都是純函式。
/// 瀏覽的位置記在 Core 那邊，用 hierarchy 加 multi_session_key 區分；browse 之後要用同樣兩個值 load。
/// </summary>
public static class RoonBrowse
{
    public const string Service = "com.roonlabs.browse:1";
    public const string Session = "shima";

    /// <summary>搜尋：回到最上層並帶入關鍵字。pop_all 跟 item_key 不能一起給。</summary>
    public static JsonObject Search(string query, string zoneId, string session = Session) => Base("search", session, zoneId, o =>
    {
        o["pop_all"] = true;
        o["input"] = query;
    });

    /// <summary>從最上層開始一般瀏覽（音樂庫、播放清單…）。</summary>
    public static JsonObject Home(string zoneId, string hierarchy = "browse", string session = Session) =>
        Base(hierarchy, session, zoneId, o => o["pop_all"] = true);

    /// <summary>點某一項：往下走一層，是動作的話就執行。要帶區域，播放相關的動作才知道要在哪裡播。</summary>
    public static JsonObject Open(string itemKey, string zoneId, string hierarchy, string? input = null, string session = Session) =>
        Base(hierarchy, session, zoneId, o =>
        {
            o["item_key"] = itemKey;
            if (input != null) o["input"] = input;
        });

    /// <summary>往上回幾層。</summary>
    public static JsonObject Back(int levels, string zoneId, string hierarchy, string session = Session) =>
        Base(hierarchy, session, zoneId, o => o["pop_levels"] = Math.Max(1, levels));

    /// <summary>載入目前這一層的項目。</summary>
    public static JsonObject Load(string hierarchy, int offset = 0, int count = 100, string session = Session) => new()
    {
        ["hierarchy"] = hierarchy,
        ["multi_session_key"] = session,
        ["offset"] = offset,
        ["count"] = count,
    };

    private static JsonObject Base(string hierarchy, string session, string zoneId, Action<JsonObject> fill)
    {
        var o = new JsonObject { ["hierarchy"] = hierarchy, ["multi_session_key"] = session };
        if (zoneId.Length > 0) o["zone_or_output_id"] = zoneId;
        fill(o);
        return o;
    }

    public static RoonBrowseItem Item(JsonNode? n) => new(
        n?["title"]?.ToString() ?? "",
        n?["subtitle"]?.ToString() ?? "",
        n?["image_key"]?.ToString() ?? "",
        n?["item_key"]?.ToString() ?? "",
        Hint(n?["hint"]?.ToString(), "action", "action_list", "list", "header"),
        n?["input_prompt"]?["prompt"]?.ToString() ?? "");

    public static RoonBrowseResult Result(JsonNode? body) => new(
        body?["action"]?.ToString() ?? "none",
        body?["message"]?.ToString() ?? "",
        body?["is_error"] is JsonValue v && v.TryGetValue<bool>(out var e) && e,
        body?["item"] is JsonObject item ? Item(item) : null);

    /// <summary>load 的回應：{items, offset, list}。</summary>
    public static RoonBrowsePage Page(JsonNode? body)
    {
        var list = body?["list"];
        return new RoonBrowsePage(
            list?["title"]?.ToString() ?? "",
            list?["subtitle"]?.ToString() ?? "",
            (int)Number(list?["count"]),
            (int)Number(list?["level"]),
            Hint(list?["hint"]?.ToString(), "action_list"),
            (body?["items"] as JsonArray ?? []).Select(Item).ToList());
    }

    /// <summary>
    /// 要「直接播」某個結果時，在目前這一層下一步該點哪一項：
    /// 有動作就點第一個動作（Roon 把預設動作放最前面，通常是「立即播放」）；
    /// 否則點第一個會通往動作清單的（像「播放專輯」或曲目來源）；
    /// 只剩一個子清單時（同一張專輯的版本層）就走進去。都沒有就是 null，表示這裡沒有可以直接播的。
    /// </summary>
    public static RoonBrowseItem? NextStep(RoonBrowsePage page)
    {
        var usable = page.Items.Where(i => !i.IsHeader).ToList();
        return usable.FirstOrDefault(i => i.IsAction)
            ?? usable.FirstOrDefault(i => i.Hint == "action_list")
            ?? (usable.Count == 1 && usable[0].Hint == "list" ? usable[0] : null);
    }

    /// <summary>不認得的 hint 一律當空的（官方說之後可能會加新的）。</summary>
    private static string Hint(string? hint, params string[] known) => hint != null && known.Contains(hint) ? hint : "";

    private static double Number(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}

// Shima
// Copyright (C) 2026 Carinoasd
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;

namespace Shima.Playback;

/// <summary>播放佇列裡的一首。Id 是 queue_item_id，拿來「從這一首開始播」。</summary>
public sealed record RoonQueueItem(long Id, string Title, string Artist, string Album, TimeSpan Length, string ImageKey);

/// <summary>區域的播放設定。Loop 是 Roon 的原字："disabled"、"loop"（整份循環）、"loop_one"（單曲循環）。</summary>
public sealed record RoonZoneSettings(bool Shuffle, string Loop, bool AutoRadio);

/// <summary>
/// transport 服務的佇列與播放設定：組請求、解析回應。都是純函式，好測試。
/// 佇列從正在播的那首開始（第 0 首就是現在這首），之後是接下來要播的。
/// </summary>
public static class RoonQueue
{
    /// <summary>subscribe_queue 的內容。</summary>
    public static JsonObject Subscribe(string zoneId, int maxItems, int subscriptionKey) => new()
    {
        ["zone_or_output_id"] = zoneId,
        ["max_item_count"] = maxItems,
        ["subscription_key"] = subscriptionKey,
    };

    public static JsonObject Unsubscribe(int subscriptionKey) => new() { ["subscription_key"] = subscriptionKey };

    public static JsonObject PlayFromHere(string zoneId, long queueItemId) => new()
    {
        ["zone_or_output_id"] = zoneId,
        ["queue_item_id"] = queueItemId,
    };

    /// <summary>
    /// change_settings 的內容：只帶要改的那幾項。loop 傳 "next" 是 Roon 自己輪替
    /// （關 → 整份 → 單曲 → 關）。
    /// </summary>
    public static JsonObject ChangeSettings(string zoneId, bool? shuffle = null, string? loop = null, bool? autoRadio = null)
    {
        var body = new JsonObject { ["zone_or_output_id"] = zoneId };
        if (shuffle is { } s) body["shuffle"] = s;
        if (loop != null) body["loop"] = loop;
        if (autoRadio is { } a) body["auto_radio"] = a;
        return body;
    }

    public static RoonQueueItem? Item(JsonNode? node)
    {
        if (node is not JsonObject o || o["queue_item_id"] is not JsonValue idValue || !idValue.TryGetValue<long>(out var id)) return null;
        var lines = o["three_line"];
        return new RoonQueueItem(id,
            lines?["line1"]?.ToString() ?? o["one_line"]?["line1"]?.ToString() ?? "",
            lines?["line2"]?.ToString() ?? "",
            lines?["line3"]?.ToString() ?? "",
            TimeSpan.FromSeconds(Number(o["length"])),
            o["image_key"]?.ToString() ?? "");
    }

    /// <summary>
    /// 把一則佇列訊息套到清單上。"Subscribed" 帶整份 items；"Changed" 帶 changes，
    /// 每一項是 {operation: "insert" | "remove", index, items | count}，依序套用，index 以套完前一項的清單為準。
    /// 回傳 true 表示清單有變。
    /// </summary>
    public static bool Apply(List<RoonQueueItem> queue, string response, JsonNode? body)
    {
        if (response == "Subscribed")
        {
            queue.Clear();
            foreach (var n in body?["items"] as JsonArray ?? [])
                if (Item(n) is { } item) queue.Add(item);
            return true;
        }
        if (response != "Changed") return false;

        bool changed = false;
        foreach (var c in body?["changes"] as JsonArray ?? [])
        {
            int index = Math.Clamp((int)Number(c?["index"]), 0, queue.Count);
            switch (c?["operation"]?.ToString())
            {
                case "insert":
                    var items = (c["items"] as JsonArray ?? []).Select(Item).OfType<RoonQueueItem>().ToList();
                    queue.InsertRange(index, items);
                    changed |= items.Count > 0;
                    break;
                case "remove":
                    int count = Math.Clamp((int)Number(c["count"]), 0, queue.Count - index);
                    queue.RemoveRange(index, count);
                    changed |= count > 0;
                    break;
            }
        }
        return changed;
    }

    /// <summary>zone 裡的 settings；沒有就是 null。</summary>
    public static RoonZoneSettings? Settings(JsonNode? zone)
    {
        if (zone?["settings"] is not JsonObject s) return null;
        static bool Flag(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        return new RoonZoneSettings(Flag(s["shuffle"]), s["loop"]?.ToString() ?? "disabled", Flag(s["auto_radio"]));
    }

    private static double Number(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
}
